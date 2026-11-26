using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace FifaStreetLauncher;

public sealed record ThunderstoreModProfile(string Author, string Name, string GamePath)
{
    internal string Id => "thunderstore:" + Author + "/" + Name;
    public string? Version { get; init; }
}

public partial class LauncherForm
{
    private string classicGamePath = "";
    private string street2026GamePath = "";
    private bool manualModSetup;
    private readonly HashSet<string> removedManualProfiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThunderstoreModProfile> communityProfiles = new(StringComparer.OrdinalIgnoreCase);
    private string modProfile = "vanilla";
    private bool launchingGame;
    private bool uninstallingMod;
    private bool ManualModSetupLocked => launchingGame || gameRunning || thunderstoreInstalling || uninstallingMod || updating;
    private bool UseMod => modProfile != "vanilla" && PageConnected;
    private bool IsBuiltInMod => modProfile is "classic" or "street2026";
    private string ModGamePath => modProfile switch
    {
        "classic" => classicGamePath,
        "street2026" => street2026GamePath,
        _ => communityProfiles.GetValueOrDefault(modProfile)?.GamePath ?? ""
    };

    private string ProfileSavesRoot()
    {
        string root = SavesRoot();
        if (!UseMod) return root;
        if (IsBuiltInMod)
            return Path.Combine(root, "ModProfiles", modProfile == "street2026" ? "FifaStreet2026" : "FifaStreetClassic");
        string packageKey = ThunderstoreProfileHash(modProfile);
        return Path.Combine(root, "ModProfiles", "Thunderstore", packageKey);
    }

    private static string ThunderstoreProfileHash(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToLowerInvariant())));

    private static string ThunderstoreProfileKey(string id) => ThunderstoreProfileHash(id)[..16];

    private void LoadModProfiles(LauncherSettings settings)
    {
        manualModSetup = settings.ManualModSetup;
        removedManualProfiles.UnionWith((settings.RemovedManualProfiles ?? []).Where(IsManualProfile));
        classicGamePath = settings.ClassicGamePath ?? "";
        street2026GamePath = settings.Street2026GamePath ?? "";
        foreach (var profile in settings.ThunderstoreProfiles ?? [])
        {
            if (profile?.Author is not { } author || profile.Name is not { } name ||
                !PackageSegment.IsMatch(author) || !PackageSegment.IsMatch(name) ||
                string.IsNullOrWhiteSpace(profile.GamePath) || !Path.IsPathFullyQualified(profile.GamePath))
                continue;
            try
            {
                string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.GamePath));
                communityProfiles.TryAdd(profile.Id, profile with { GamePath = path });
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A malformed stored path cannot be used to launch a profile.
            }
        }
        classicGamePath = ResolveBuiltInGameFolder("classic", "Classic", classicGamePath);
        street2026GamePath = ResolveBuiltInGameFolder("street2026", "2026", street2026GamePath);
        string? selectedId = settings.ModProfile switch
        {
            null when settings.ClassicSelected => "classic",
            _ => settings.ModProfile
        };
        modProfile = InstalledMods().FirstOrDefault(mod => mod.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))?.Id ?? "vanilla";
    }

    private string ResolveBuiltInGameFolder(string profileId, string folder, string configured)
    {
        if (removedManualProfiles.Contains(profileId)) return "";
        if (HasModGameFiles(configured) && !SameGameFolder(configured, gamePathBox.Text.Trim()) &&
            !IsOtherModFolder(configured, profileId))
            return configured;
        string local = Path.Combine(AppContext.BaseDirectory, "Mods", folder, "Game");
        return HasModGameFiles(local) && !SameGameFolder(local, gamePathBox.Text.Trim()) && !IsOtherModFolder(local, profileId)
            ? local
            : "";
    }

    private static bool HasModGameFiles(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "default.xex")) &&
        File.Exists(Path.Combine(path, "data0.big"));

    private void SelectModProfile(string? id)
    {
        if (launchingGame || gameRunning || uninstallingMod || updating || AnyGameRunning()) return;
        string? selected = id == "vanilla" ? id
            : InstalledMods().FirstOrDefault(mod => mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Id;
        if (selected == null || selected == modProfile) return;
        string previous = modProfile;
        modProfile = selected;
        if (!SaveModSettings()) modProfile = previous;
    }

    private void ToggleManualModSetup(bool enabled)
    {
        if (enabled == manualModSetup) return;
        manualModSetup = enabled;
        if (!SaveModSettings()) manualModSetup = !enabled;
    }

    private void BrowseModGame(string profileId)
    {
        if (!manualModSetup || !IsManualProfile(profileId) || ManualModSetupLocked || AnyGameRunning()) return;
        string previous = profileId == "classic" ? classicGamePath : street2026GamePath;
        string? selected = PickModGameFolder(profileId, previous);
        if (selected == null || !manualModSetup || ManualModSetupLocked) return;
        bool wasRemoved = removedManualProfiles.Remove(profileId);
        SetManualGamePath(profileId, selected);
        if (!SaveModSettings())
        {
            SetManualGamePath(profileId, previous);
            if (wasRemoved) removedManualProfiles.Add(profileId);
        }
    }

    private static bool IsManualProfile(string id) => id is "classic" or "street2026";

    private void SetManualGamePath(string id, string path)
    {
        if (id == "classic") classicGamePath = path;
        else street2026GamePath = path;
    }

    private sealed record InstalledMod(string Id, string Name, string Path, bool Manual, string? Author = null, string? Version = null);

    private IEnumerable<InstalledMod> InstalledMods()
    {
        if (HasModGameFiles(classicGamePath))
            yield return new("classic", "FIFA Street Classic", classicGamePath, true);
        if (HasModGameFiles(street2026GamePath))
            yield return new("street2026", "FIFA Street 2026", street2026GamePath, true);
        foreach (var profile in communityProfiles.Values)
            if (HasModGameFiles(profile.GamePath))
                yield return new(profile.Id, profile.Name, profile.GamePath, false, profile.Author, profile.Version);
    }

    private bool CanRecycleModFiles(InstalledMod mod, IReadOnlyList<InstalledMod> installed)
    {
        if (!Path.IsPathFullyQualified(mod.Path)) return false;
        string game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod.Path));
        string modsRoot = Path.Combine(AppContext.BaseDirectory, "Mods");
        if (mod.Manual)
        {
            if (!SameGameFolder(game, Path.Combine(modsRoot, mod.Id == "classic" ? "Classic" : "2026", "Game")))
                return false;
        }
        else
        {
            string? package = Path.GetDirectoryName(game);
            if (package == null || !Path.GetFileName(game).Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                !SameGameFolder(Path.GetDirectoryName(package) ?? "", Path.Combine(modsRoot, "Thunderstore")))
                return false;
            string[] parts = Path.GetFileName(package).Split('-');
            if (parts.Length != 3 || !parts[0].Equals(ThunderstoreProfileKey(mod.Id), StringComparison.OrdinalIgnoreCase) ||
                !VersionSegment.IsMatch(parts[1]) || !Guid.TryParseExact(parts[2], "N", out _))
                return false;
        }

        try
        {
            if (OverlapsGameFolder(game, Path.Combine(AppContext.BaseDirectory, "Game")) ||
                OverlapsGameFolder(game, gamePathBox.Text.Trim()) ||
                OverlapsGameFolder(game, exePathBox.Text.Trim()) ||
                OverlapsGameFolder(game, SavesRoot()) ||
                installed.Any(other => other.Id != mod.Id && OverlapsGameFolder(game, other.Path)))
                return false;
            for (string? path = game; path != null; path = Path.GetDirectoryName(path))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool OverlapsGameFolder(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(second)) return false;
        second = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return SameGameFolder(first, second) ||
            first.StartsWith(Path.EndsInDirectorySeparator(second) ? second : second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task UninstallModAsync(string id, string? path, bool recycleFiles)
    {
        void Report(string note, bool error = false, string? recoveryPath = null) =>
            PostToPage(new { type = "modUninstall", id, note, error, recoveryPath });

        if (updating)
        {
            Report("Wait for the launcher update to finish.", true);
            return;
        }
        if (ManualModSetupLocked || AnyGameRunning())
        {
            Report("Close the game and wait for any mod operation to finish.", true);
            return;
        }
        var installed = InstalledMods().ToArray();
        var mod = installed.FirstOrDefault(mod => mod.Id == id);
        if (mod == null || path != mod.Path || recycleFiles != CanRecycleModFiles(mod, installed))
        {
            Report("The mod changed. Refresh the list and try again.", true);
            PushGlassState(true);
            return;
        }

        uninstallingMod = true;
        string? staged = null;
        bool saved = false;
        try
        {
            string gamePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod.Path));
            using var installLock = recycleFiles && !mod.Manual ? LockThunderstoreInstalls() : null;
            if (recycleFiles)
            {
                string target = Path.Combine(Path.GetDirectoryName(gamePath)!, ".uninstall-" + Guid.NewGuid().ToString("N"));
                Directory.Move(gamePath, target);
                staged = target;
            }

            string previousSelection = modProfile;
            var package = communityProfiles.GetValueOrDefault(id);
            if (mod.Manual)
            {
                SetManualGamePath(id, "");
                removedManualProfiles.Add(id);
            }
            else communityProfiles.Remove(id);
            if (modProfile == id) modProfile = "vanilla";

            try { SaveSettings(); }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                modProfile = previousSelection;
                if (mod.Manual)
                {
                    SetManualGamePath(id, mod.Path);
                    removedManualProfiles.Remove(id);
                }
                else if (package != null) communityProfiles.Add(id, package);
                if (staged != null) Directory.Move(staged, gamePath);
                Report("Could not save the mod change.", true);
                return;
            }
            saved = true;
            PushGlassState(true);
            if (staged != null)
                await Task.Run(() => FileSystem.DeleteDirectory(staged, UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin, UICancelOption.ThrowException));
            Report(recycleFiles ? "Mod uninstalled." : "Mod removed from the launcher.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            Report(saved ? "The profile was removed, but its files could not be recycled." : "Could not uninstall the mod.",
                true, staged);
        }
        finally
        {
            uninstallingMod = false;
            PushGlassState(true);
        }
    }

    private bool SetCommunityProfile(ThunderstoreModProfile profile)
    {
        var previous = communityProfiles.GetValueOrDefault(profile.Id);
        string previousProfile = modProfile;
        communityProfiles[profile.Id] = profile;
        modProfile = profile.Id;
        if (SaveModSettings()) return true;
        modProfile = previousProfile;
        if (previous == null) communityProfiles.Remove(profile.Id);
        else communityProfiles[profile.Id] = previous;
        return false;
    }

    private string? PickModGameFolder(string profileId, string currentPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the separate Game folder with the selected mod installed.",
            SelectedPath = currentPath,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || launchingGame || AnyGameRunning()) return null;

        string selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.SelectedPath));
        string vanilla = gamePathBox.Text.Trim();
        if (!HasModGameFiles(selected) ||
            (vanilla.Length > 0 && SameGameFolder(selected, vanilla)) ||
            IsOtherModFolder(selected, profileId))
        {
            ShowError("Select a Game folder with default.xex and data0.big that belongs only to this profile.");
            return null;
        }
        return selected;
    }

    private bool IsOtherModFolder(string path, string? profileId = null)
    {
        profileId ??= modProfile;
        return (profileId != "classic" && classicGamePath.Length > 0 && SameGameFolder(path, classicGamePath)) ||
            (profileId != "street2026" && street2026GamePath.Length > 0 && SameGameFolder(path, street2026GamePath)) ||
            communityProfiles.Values.Any(profile => !profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase) &&
                SameGameFolder(path, profile.GamePath));
    }

    private static bool SameGameFolder(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private bool SaveModSettings()
    {
        try
        {
            SaveSettings();
            return true;
        }
        catch (IOException ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }
}
