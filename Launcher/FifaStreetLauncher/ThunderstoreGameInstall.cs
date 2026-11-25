using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FifaStreetLauncher;

internal static class ThunderstoreGameInstall
{
    private const string PatchPrefix = "restreet-patches/";
    private const string LocalizationFile = "restreet-localization.json";
    private static readonly Regex ArchiveFile = new(@"\Adata[0-7]\.(big|bh)\z", RegexOptions.IgnoreCase);
    private static readonly Regex PatchTarget = new(@"\A(data[0-7]\.(big|bh)|data/loc/locale\.big|data/ui/imgassets/heads/2dheads\.(big|bh))\z", RegexOptions.IgnoreCase);
    private static readonly Regex DeviceName = new(@"\A(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase);
    private static readonly HashSet<string> MetadataFiles = new(StringComparer.OrdinalIgnoreCase)
        { "manifest.json", "icon.png", "README.md", "CHANGELOG.md", "LICENSE", "LICENSE.md", "LICENSE.txt" };
    private static readonly HashSet<string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".xex", ".msi", ".bat", ".cmd", ".ps1", ".psm1", ".sh", ".com", ".scr", ".lnk", ".url", ".vbs", ".js", ".reg", ".hta" };

    private sealed record Manifest([property: JsonPropertyName("dependencies")] string[]? Dependencies);
    internal sealed record Archive(string FilePath, string[] Dependencies);
    private sealed record GameFile(ZipArchiveEntry Entry, string Path, bool IsPatch);

    internal static Archive Inspect(string file)
    {
        using var archive = ZipFile.OpenRead(file);
        var manifest = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing Thunderstore manifest.");
        if (manifest.Length > 1024 * 1024) throw new InvalidDataException("Invalid Thunderstore manifest size.");
        using var contents = manifest.Open();
        var metadata = JsonSerializer.Deserialize<Manifest>(contents) ?? throw new InvalidDataException("Invalid Thunderstore manifest.");
        return new(file, metadata.Dependencies ?? []);
    }

    internal static void PrepareGame(string vanilla, string destination, IReadOnlyList<Archive> archives, CancellationToken cancellation)
    {
        if (!File.Exists(Path.Combine(vanilla, "default.xex")) || !File.Exists(Path.Combine(vanilla, "data0.big")))
            throw new InvalidDataException("Vanilla Game folder is missing its base files.");

        // Validate the entire package layout before copying the large base game.
        bool hasGameFiles = false;
        foreach (var package in archives)
        {
            using var zip = ZipFile.OpenRead(package.FilePath);
            hasGameFiles |= GameFiles(zip).Count > 0;
        }
        if (!hasGameFiles) throw new UnsupportedPackageException();

        CopyBaseGame(vanilla, destination, cancellation);
        foreach (var package in archives)
        {
            using var zip = ZipFile.OpenRead(package.FilePath);
            foreach (var file in GameFiles(zip).OrderBy(file => file.Path == LocalizationFile))
            {
                cancellation.ThrowIfCancellationRequested();
                string output = Path.Combine(destination, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                if (File.Exists(output)) File.SetAttributes(output, FileAttributes.Normal);
                using var content = file.Entry.Open();
                if (file.Path == LocalizationFile)
                {
                    ThunderstoreLocalization.Apply(content, file.Entry.Length, destination, cancellation);
                    continue;
                }
                if (file.IsPatch)
                {
                    ThunderstoreDataPatch.Apply(content, file.Entry.Length, vanilla, file.Path, output, cancellation);
                    continue;
                }
                using var replacement = File.Create(output);
                CopyFile(content, replacement, cancellation);
            }
        }
    }

    private static List<GameFile> GameFiles(ZipArchive archive)
    {
        var files = new List<GameFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            if (MetadataFiles.Contains(name)) continue;
            if (name.StartsWith("Game/", StringComparison.OrdinalIgnoreCase)) name = name[5..];
            bool isPatch = name.StartsWith(PatchPrefix, StringComparison.OrdinalIgnoreCase);
            if (isPatch) name = name[PatchPrefix.Length..];
            var parts = name.Split('/');
            if (parts.Any(part => part.Length == 0 || part.EndsWith('.') || part.EndsWith(' ') ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || DeviceName.IsMatch(part)) ||
                !seen.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Invalid path in Thunderstore archive.");
            if (!isPatch && name == LocalizationFile)
            {
                files.Add(new GameFile(entry, name, false));
                continue;
            }
            bool isGameFile = parts.Length > 1 && (parts[0].Equals("data", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("dlc", StringComparison.OrdinalIgnoreCase)) || ArchiveFile.IsMatch(name);
            if (ProgramExtensions.Contains(Path.GetExtension(name)) || !(isPatch ? PatchTarget.IsMatch(name) : isGameFile))
                throw new UnsupportedPackageException();
            files.Add(new GameFile(entry, Path.Combine(parts), isPatch));
        }
        return files;
    }

    private static void CopyBaseGame(string source, string destination, CancellationToken cancellation)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Game folder contains linked paths.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            cancellation.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The Game folder contains linked paths.");
            using var input = File.OpenRead(file);
            using var output = File.Create(Path.Combine(destination, Path.GetFileName(file)));
            CopyFile(input, output, cancellation);
        }
        foreach (string folder in Directory.EnumerateDirectories(source))
            CopyBaseGame(folder, Path.Combine(destination, Path.GetFileName(folder)), cancellation);
    }

    private static void CopyFile(Stream input, Stream output, CancellationToken cancellation) =>
        input.CopyToAsync(output, cancellation).GetAwaiter().GetResult();

    internal sealed class UnsupportedPackageException : Exception { }
}
