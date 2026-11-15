namespace FifaStreetLauncher;

public partial class LauncherForm
{
    private string classicGamePath = "";
    private string street2026GamePath = "";
    private string modProfile = "vanilla";
    private bool launchingGame;
    private bool UseMod => modProfile != "vanilla" && PageConnected;
    private string ModGamePath => modProfile == "street2026" ? street2026GamePath : classicGamePath;

    private string ProfileSavesRoot() => UseMod
        ? Path.Combine(SavesRoot(), "ModProfiles", modProfile == "street2026" ? "FifaStreet2026" : "FifaStreetClassic")
        : SavesRoot();

    private void BrowseModGame()
    {
        if (!UseMod || launchingGame || AnyGameRunning()) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the separate Game folder with the selected mod installed.",
            SelectedPath = ModGamePath,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.SelectedPath));
        string vanilla = gamePathBox.Text.Trim();
        if (!File.Exists(Path.Combine(selected, "default.xex")) ||
            !File.Exists(Path.Combine(selected, "data0.big")) ||
            (vanilla.Length > 0 && SameGameFolder(selected, vanilla)) ||
            IsOtherModFolder(selected))
        {
            ShowError("Select a Game folder with default.xex and data0.big that belongs only to this profile.");
            return;
        }
        if (modProfile == "street2026") street2026GamePath = selected;
        else classicGamePath = selected;
        SaveModSettings();
    }

    private bool IsOtherModFolder(string path)
    {
        string other = modProfile == "street2026" ? classicGamePath : street2026GamePath;
        return other.Length > 0 && SameGameFolder(path, other);
    }

    private static bool SameGameFolder(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private void SaveModSettings()
    {
        try { SaveSettings(); }
        catch (IOException ex) { ShowError(ex.Message); }
    }
}
