using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FifaStreetLauncher;

// The player's personal online key (online.json "key"): the server binds the online name to the first key it sees,
// so a reinstall, which writes a new online.json, must not lose it. The launcher keeps a copy outside the game folder
// (%LOCALAPPDATA%\ReStreet\player-key.json, ~/.local/share/ReStreet on Linux) and puts it back into online.json when
// a fresh install brought another key for the same name. A recovery code (RS1-...) carries the name and the key to
// another PC. Nothing here ever drops a key: one that is replaced stays in the backup under "previous".
// Shared by the Windows and the Linux launcher.
internal static class PlayerKey
{
    private const int MinKey = 16;       // the server binds no shorter key (friends.py MIN_KEY)
    private const string CodePrefix = "RS1-";
    // Same nickname rules as the official installer.
    private static readonly Regex NewName = new(@"\A[A-Za-z0-9][A-Za-z0-9 _-]{1,13}[A-Za-z0-9_-]\z");

    internal sealed record Result(bool Ok, string Note);
    private sealed record ConfigChange(string Path, string? Before, string After);

    // RESTREET_KEY_BACKUP points the copy elsewhere, for tests.
    internal static string BackupPath() =>
        Environment.GetEnvironmentVariable("RESTREET_KEY_BACKUP") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReStreet", "player-key.json");

    // At launcher start, with the online.json copies (the first is the one the launcher reads). Makes them and the
    // backup agree; online.json without a key gets a new one so the name can be claimed.
    internal static void Sync(IReadOnlyList<string> onlineFiles)
    {
        var (name, key) = Read(onlineFiles);
        if (name.Length == 0) return;
        var backup = LoadBackup();
        var saved = backup[Id(name)] as JsonObject;
        string savedKey = saved?["key"]?.GetValue<string>() ?? "";
        if (savedKey.Length >= MinKey)
        {
            if (savedKey == key) return;
            Remember(saved!, key);
            if (Write(onlineFiles, name, savedKey)) SaveBackup(backup);
            return;
        }
        if (key.Length < MinKey && !Write(onlineFiles, name, key = NewKey())) return;
        backup[Id(name)] = new JsonObject { ["name"] = name, ["key"] = key, ["saved"] = DateTime.UtcNow.ToString("s") };
        SaveBackup(backup);
    }

    // The code shown in Advanced, or "" when online is not set up.
    internal static string RecoveryCode(IReadOnlyList<string> onlineFiles)
    {
        var (name, key) = Read(onlineFiles);
        return name.Length == 0 || key.Length < MinKey ? "" : CodePrefix + Base64Url(Encoding.UTF8.GetBytes(name + "\n" + key));
    }

    // Names are account identifiers on the server. Keep each name's key so the player can switch back.
    internal static Result SwitchName(IReadOnlyList<string> onlineFiles, string? requested)
    {
        var (current, currentKey) = Read(onlineFiles);
        if (current.Length == 0) return new(false, "Online is not set up. Run the online patch again.");
        string name = (requested ?? "").Trim();
        if (name == current) return new(true, "This PC already uses this name.");

        var backup = LoadBackup();
        string currentId = Id(current);
        string nameId = Id(name);
        var entry = backup[nameId] as JsonObject;
        string savedKey = ReadKey(entry);
        if (!NewName.IsMatch(name) && !(ValidName(name) && savedKey.Length >= MinKey))
            return new(false, "Use 3 to 15 letters, numbers, spaces, hyphens or underscores. Start with a letter or number.");

        if (currentKey.Length >= MinKey)
        {
            var old = backup[currentId] as JsonObject ?? new JsonObject();
            if (ReadKey(old) is { Length: >= MinKey } oldKey && oldKey != currentKey)
                Remember(old, oldKey);
            old["name"] = current;
            old["key"] = currentKey;
            backup[currentId] = old;
        }

        string key = ActivateName(backup, name);
        if (!SaveBackup(backup)) return new(false, "Could not back up your online key. Your online name was kept.");
        if (!WriteNameCopies(onlineFiles, name, key)) return new(false, "Could not write online.json. Close the game and try again.");

        return new(true, "Online name saved on this PC.");
    }

    // Configure an unconfigured install without replacing a possibly recoverable identity.
    internal static Result ConfigureOnline(IReadOnlyList<string> onlineFiles, string? requested, string server)
    {
        if (onlineFiles.Any(File.Exists))
            return new(false, "Existing online settings could not be read. Restore your online.json before setting up a new name.");

        string name = (requested ?? "").Trim();
        var backup = LoadBackup();
        if (!NewName.IsMatch(name) && !(ValidName(name) && ReadKey(backup[Id(name)] as JsonObject).Length >= MinKey))
            return new(false, "Use 3 to 15 letters, numbers, spaces, hyphens or underscores. Start with a letter or number.");

        string key = ActivateName(backup, name);
        if (!SaveBackup(backup)) return new(false, "Could not back up your online key. Check that you can write to your profile folder.");

        string json = new JsonObject { ["name"] = name, ["key"] = key, ["server"] = server }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        // The launcher copy is written last so it only detects a completed setup.
        var changes = onlineFiles.Distinct(StringComparer.OrdinalIgnoreCase).Reverse().Select(path => new ConfigChange(path, null, json));
        if (!ApplyConfigChanges(changes))
            return new(false, "Could not write online.json. Close the game and try again.");
        return new(true, "Online name saved on this PC.");
    }

    // A code from another PC: its name and key go into online.json (and the backup) here.
    internal static Result UseCode(IReadOnlyList<string> onlineFiles, string? code)
    {
        if (!TryParse(code, out string name, out string key)) return new(false, "That is not a recovery code. Copy the whole code, starting with RS1-.");
        var (current, currentKey) = Read(onlineFiles);
        if (current.Length == 0) return new(false, "Online is not set up. Run the online patch again.");
        if (current == name && currentKey == key) return new(true, "This PC already uses this code.");
        var backup = LoadBackup();
        // The key this PC had stays in the backup, under its own name.
        if (currentKey.Length > 0)
        {
            if (backup[Id(current)] is not JsonObject old) backup[Id(current)] = new JsonObject { ["name"] = current, ["key"] = currentKey };
            else if (old["key"]?.GetValue<string>() != currentKey) Remember(old, currentKey);
        }
        var entry = backup[Id(name)] as JsonObject ?? new JsonObject();
        if (entry["key"]?.GetValue<string>() is { Length: > 0 } before && before != key) Remember(entry, before);
        entry["name"] = name;
        entry["key"] = key;
        entry["saved"] = DateTime.UtcNow.ToString("s");
        backup[Id(name)] = entry;
        if (!Write(onlineFiles, name, key)) return new(false, "Could not write online.json. Close the game and try again.");
        SaveBackup(backup);
        return new(true, "Done. This PC now plays online with the name and key from the code.");
    }

    internal static bool TryParse(string? code, out string name, out string key)
    {
        name = key = "";
        string text = new((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!text.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            string payload = text[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\n');
            if (parts.Length != 2) return false;
            (name, key) = (parts[0].Trim(), parts[1].Trim());
        }
        catch (FormatException) { return false; }
        // The server's rules for a name (friends.py valid_name) and a key.
        return ValidName(name) && key.Length is >= MinKey and <= 128;
    }

    private static bool ValidName(string name) =>
        name.Length is >= 1 and <= 32 && !name.Any(c => c < 0x20 || c == 0x7f || "\\\"{}[]".Contains(c));

    private static string ReadKey(JsonObject? entry) =>
        entry?["key"] is JsonValue value && value.TryGetValue<string>(out var key) ? key : "";

    private static string ActivateName(JsonObject backup, string name)
    {
        string id = Id(name);
        var entry = backup[id] as JsonObject ?? new JsonObject();
        string key = ReadKey(entry) is { Length: >= MinKey } existing ? existing : NewKey();
        entry["name"] = name;
        entry["key"] = key;
        entry["saved"] = DateTime.UtcNow.ToString("s");
        backup[id] = entry;
        return key;
    }

    private static (string Name, string Key) Read(IReadOnlyList<string> onlineFiles)
    {
        try
        {
            if (onlineFiles.Count == 0 || !File.Exists(onlineFiles[0])) return ("", "");
            var node = JsonNode.Parse(File.ReadAllText(onlineFiles[0])) as JsonObject;
            return ((node?["name"]?.GetValue<string>() ?? "").Trim(), (node?["key"]?.GetValue<string>() ?? "").Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ("", "");
        }
    }

    private static bool WriteNameCopies(IReadOnlyList<string> onlineFiles, string name, string key)
    {
        var changes = new List<ConfigChange>();
        try
        {
            foreach (string path in onlineFiles.Distinct(StringComparer.OrdinalIgnoreCase).Reverse())
            {
                if (!File.Exists(path)) continue;
                string before = File.ReadAllText(path);
                if (JsonNode.Parse(before) is not JsonObject config) return false;
                config["name"] = name;
                config["key"] = key;
                changes.Add(new(path, before, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
        return ApplyConfigChanges(changes);
    }

    private static bool ApplyConfigChanges(IEnumerable<ConfigChange> changes)
    {
        var applied = new List<ConfigChange>();
        try
        {
            foreach (var change in changes)
            {
                if (change.Before != null && File.ReadAllText(change.Path) != change.Before)
                    throw new IOException("Online settings changed during setup.");
                WriteAtomic(change.Path, change.After, overwrite: change.Before != null);
                applied.Add(change);
            }
            return applied.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var change in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.ReadAllText(change.Path) != change.After) continue;
                    if (change.Before == null) File.Delete(change.Path);
                    else WriteAtomic(change.Path, change.Before);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { }
            }
            return false;
        }
    }

    // Name and key into every online.json copy that exists; the other fields (server, ...) stay as they are.
    private static bool Write(IReadOnlyList<string> onlineFiles, string name, string key)
    {
        bool wrote = false;
        foreach (string path in onlineFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
                node["name"] = name;
                node["key"] = key;
                WriteAtomic(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                wrote = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                if (path == onlineFiles[0]) return false;
            }
        }
        return wrote;
    }

    // Keeps |key| in the entry's "previous" list (when it is not the entry's key, or is about to be replaced).
    private static void Remember(JsonObject entry, string key)
    {
        if (key.Length == 0) return;
        var previous = entry["previous"] as JsonArray ?? new JsonArray();
        if (!previous.Any(p => p?.GetValue<string>() == key)) previous.Add(JsonValue.Create(key));
        while (previous.Count > 5) previous.RemoveAt(0);
        entry["previous"] = previous;
    }

    private static JsonObject LoadBackup()
    {
        try
        {
            string path = BackupPath();
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o ? o : new JsonObject();
        }
        catch (JsonException)
        {
            // A damaged copy is put aside (never deleted) before a new one is written.
            try { File.Copy(BackupPath(), BackupPath() + ".bad", true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return new JsonObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }

    private static bool SaveBackup(JsonObject backup)
    {
        try
        {
            string path = BackupPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomic(path, backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void WriteAtomic(string path, string text, bool overwrite = true)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite);
        }
        finally
        {
            try { File.Delete(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Id(string name) => name.Trim().ToLowerInvariant();

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
