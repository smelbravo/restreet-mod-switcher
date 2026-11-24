using System.Security.Cryptography;
using System.Text;

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
    private readonly Dictionary<string, ThunderstoreModProfile> communityProfiles = new(StringComparer.OrdinalIgnoreCase);
    private string modProfile = "vanilla";
    private bool launchingGame;
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
        string packageKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modProfile.ToLowerInvariant())));
        return Path.Combine(root, "ModProfiles", "Thunderstore", packageKey);
    }

    private void LoadModProfiles(LauncherSettings settings)
    {
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
        modProfile = settings.ModProfile switch
        {
            "classic" => "classic",
            "street2026" => "street2026",
            null when settings.ClassicSelected => "classic",
            string selected when communityProfiles.TryGetValue(selected, out var profile) => profile.Id,
            _ => "vanilla"
        };
    }

    private string ResolveBuiltInGameFolder(string profileId, string folder, string configured)
    {
        if (HasModGameFiles(configured)) return configured;
        string local = Path.Combine(AppContext.BaseDirectory, "Mods", folder, "Game");
        return HasModGameFiles(local) && !SameGameFolder(local, gamePathBox.Text.Trim()) && !IsOtherModFolder(local, profileId)
            ? local
            : configured;
    }

    private static bool HasModGameFiles(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "default.xex")) &&
        File.Exists(Path.Combine(path, "data0.big"));

    private void SelectModProfile(string? id)
    {
        if (launchingGame || AnyGameRunning()) return;
        string? selected = id is "vanilla" or "classic" or "street2026" ? id
            : id != null ? communityProfiles.GetValueOrDefault(id)?.Id : null;
        if (selected == null || selected == modProfile) return;
        string previous = modProfile;
        modProfile = selected;
        if (!SaveModSettings()) modProfile = previous;
    }

    private void BrowseModGame()
    {
        if (!UseMod || launchingGame || AnyGameRunning()) return;
        string profileId = modProfile;
        string previous = ModGamePath;
        string? selected = PickModGameFolder(profileId, previous);
        if (selected == null || modProfile != profileId) return;
        switch (profileId)
        {
            case "classic":
                classicGamePath = selected;
                if (!SaveModSettings()) classicGamePath = previous;
                break;
            case "street2026":
                street2026GamePath = selected;
                if (!SaveModSettings()) street2026GamePath = previous;
                break;
            default:
                if (communityProfiles.TryGetValue(profileId, out var profile))
                    SetCommunityProfile(profile with { GamePath = selected });
                break;
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
