using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class SwitcherSetup
{
    private const string SetupUrl = "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.1.5-online-beta/ReStreet-2.1.5-Setup.exe";
    private const string SetupHash = "51A69D52DCDFB4207B15292A67DB8E155D8BA4EA5A35AC0BEDD157CB2EE37DEE";
    private const string LauncherName = "FifaStreetLauncher.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            if (args.Length == 0) return RunInteractive();

            int launcherPid;
            if (args.Length != 3 || args[0] != "--auto-update" || !int.TryParse(args[2], out launcherPid) || launcherPid <= 0)
                throw new ArgumentException("Invalid update arguments.");

            string destination = Path.GetFullPath(args[1]);
            RequireInstallation(destination);
            return RunSetup(destination, null, launcherPid);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "ReStreet Mod Switcher Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunInteractive()
    {
        using (var folder = new FolderBrowserDialog { Description = "Select your ReStreet Mod Switcher folder to update, or an empty folder for a fresh installation." })
        {
            if (folder.ShowDialog() != DialogResult.OK) return 0;
            string destination = Path.GetFullPath(folder.SelectedPath);
            if (Directory.GetFileSystemEntries(destination).Length > 0)
            {
                RequireInstallation(destination);
                return RunSetup(destination, null, null);
            }

            using (var iso = new OpenFileDialog { Title = "Select your FIFA Street (2012) Xbox 360 ISO", Filter = "Xbox 360 ISO (*.iso)|*.iso" })
            {
                return iso.ShowDialog() == DialogResult.OK ? RunSetup(destination, iso.FileName, null) : 0;
            }
        }
    }

    private static int RunSetup(string destination, string iso, int? launcherPid)
    {
        bool updating = iso == null;
        int result = 1;
        using (var window = new Form { Text = "ReStreet Mod Switcher Setup", Width = 520, Height = 155, MaximizeBox = false, StartPosition = FormStartPosition.CenterScreen })
        {
            var status = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Preparing setup…" };
            window.Controls.Add(status);
            bool installing = true;
            window.FormClosing += (sender, e) => { if (installing) e.Cancel = true; };
            window.Shown += async (sender, e) =>
            {
                try
                {
                    if (updating)
                    {
                        status.Text = "Updating the Mod Switcher launcher…";
                        await Task.Run(() => UpdateLauncher(destination, launcherPid));
                    }
                    else await InstallFreshAsync(destination, iso, status);

                    if (launcherPid.HasValue)
                    {
                        using (Process.Start(new ProcessStartInfo(Path.Combine(destination, LauncherName)) { WorkingDirectory = destination })) { }
                    }
                    else
                        MessageBox.Show(window, "Setup complete. Open FifaStreetLauncher.exe in the installation folder.", window.Text);
                    result = 0;
                }
                catch (Exception error)
                {
                    MessageBox.Show(window, error.Message, window.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    installing = false;
                    window.Close();
                }
            };
            Application.Run(window);
        }
        return result;
    }

    private static async Task InstallFreshAsync(string destination, string iso, Label status)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "ReStreetSwitcher-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            var setup = Path.Combine(workspace, "ReStreet-Setup.exe");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "ReStreet-Mod-Switcher-Setup";
                client.DownloadProgressChanged += (source, progress) =>
                    status.Text = "Downloading the ReStreet base installer… " + progress.ProgressPercentage + "%";
                await client.DownloadFileTaskAsync(new Uri(SetupUrl), setup);
            }
            status.Text = "Verifying the download…";
            await Task.Run(() => VerifySetup(setup));
            status.Text = "Extracting FIFA Street from your ISO. This may take several minutes.";
            await Task.Run(() => InstallBase(setup, iso, destination));
            status.Text = "Installing the Mod Switcher launcher…";
            await Task.Run(() => ExtractLauncher(destination));
        }
        finally
        {
            TryRemoveWorkspace(workspace);
        }
    }

    private static void VerifySetup(string setup)
    {
        using (var file = File.OpenRead(setup))
        using (var sha = SHA256.Create())
            if (!string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""), SetupHash, StringComparison.Ordinal))
                throw new InvalidDataException("The downloaded ReStreet installer failed SHA-256 verification.");
    }

    private static void InstallBase(string setup, string iso, string destination)
    {
        var target = destination.TrimEnd(Path.DirectorySeparatorChar);
        var logPath = target + ".iso-install-test.log";
        var args = "--iso-install-test \"" + iso + "\" \"" + target + "\" Player";
        var startInfo = new ProcessStartInfo(setup, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(startInfo))
        {
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new IOException(DescribeBaseFailure(process.ExitCode, standardError, logPath));

            if (!File.Exists(Path.Combine(destination, "Binaries", "fifastreet.exe")) ||
                !File.Exists(Path.Combine(destination, "Game", "default.xex")))
                throw new IOException("ReStreet setup did not create the expected game files.\n\nInstallation log: " + logPath);
        }
    }

    private static string DescribeBaseFailure(int exitCode, string standardError, string logPath)
    {
        var message = "ReStreet setup failed (exit code " + exitCode + ").";
        if (!string.IsNullOrWhiteSpace(standardError))
        {
            using (var reader = new StringReader(standardError.Trim()))
                message += "\n\n" + reader.ReadLine();

            if (standardError.IndexOf(logPath, StringComparison.OrdinalIgnoreCase) >= 0)
                return message + "\n\nChoose a new empty installation folder inside a folder you can write to.";
        }

        if (File.Exists(logPath))
        {
            string error = null;
            try
            {
                foreach (var line in File.ReadLines(logPath))
                    if (line.StartsWith("ERROR:", StringComparison.Ordinal)) error = line;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (error != null) message += "\n\n" + error;
            message += "\n\nInstallation log: " + logPath;
        }
        return message;
    }

    private static void RequireInstallation(string destination)
    {
        var launcher = Path.Combine(destination, LauncherName);
        var page = Path.Combine(destination, "ui", "launcher.html");
        EnsureOrdinaryPath(launcher);
        EnsureOrdinaryPath(page);
        if (!File.Exists(launcher) || !File.Exists(page) ||
            !File.Exists(Path.Combine(destination, "Binaries", "fifastreet.exe")) ||
            !File.Exists(Path.Combine(destination, "Game", "default.xex")) ||
            !File.Exists(Path.Combine(destination, "Game", "data0.big")) ||
            !File.ReadAllText(page).Contains("selectModProfile"))
            throw new IOException("Select an existing ReStreet Mod Switcher installation or an empty folder.");
    }

    private static void WaitForLauncher(string destination, int? launcherPid)
    {
        var path = Path.Combine(destination, LauncherName);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LauncherName)))
        {
            using (process)
            {
                if (!IsRunningFrom(process, path)) continue;
                if (process.Id != launcherPid || !process.WaitForExit(30000))
                    throw new IOException("Close ReStreet Mod Switcher and run the update again.");
            }
        }
    }

    private static void RequireGameClosed(string destination)
    {
        var path = Path.Combine(destination, "Binaries", "fifastreet.exe");
        foreach (var process in Process.GetProcessesByName("fifastreet"))
        {
            using (process)
            {
                if (IsRunningFrom(process, path))
                    throw new IOException("Close FIFA Street and run the update again.");
            }
        }
    }

    private static bool IsRunningFrom(Process process, string path)
    {
        try
        {
            var module = process.MainModule;
            return module != null && string.Equals(module.FileName, path, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    private static void UpdateLauncher(string destination, int? launcherPid)
    {
        RequireInstallation(destination);
        WaitForLauncher(destination, launcherPid);
        RequireGameClosed(destination);
        string updateRoot = Path.Combine(destination, ".switcher-updates");
        string lockPath = Path.Combine(updateRoot, "update.lock");
        EnsureOrdinaryPath(lockPath);
        Directory.CreateDirectory(updateRoot);
        using (var updateLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            // Saves live outside the launcher payload. Keep the existing game, mods and settings byte-for-byte.
            string transaction = Path.Combine(updateRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            string staging = Path.Combine(transaction, "incoming");
            string backup = Path.Combine(transaction, "previous");
            try
            {
                var files = ExtractLauncher(staging);
                ReplaceLauncherFiles(destination, staging, backup, files);
            }
            finally
            {
                TryRemoveWorkspace(staging);
            }
        }
    }

    private static void ReplaceLauncherFiles(string destination, string staging, string backup, IEnumerable<string> files)
    {
        // Apply UI and resources first, then commit the new launcher executable.
        var ordered = files.OrderBy(file => file.Equals(LauncherName, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var file in ordered)
        {
            var target = Path.Combine(destination, file);
            EnsureOrdinaryPath(target);
            if (File.Exists(target))
                using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        }

        var replaced = new List<string>();
        try
        {
            foreach (var file in ordered)
            {
                string target = Path.Combine(destination, file);
                string source = Path.Combine(staging, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                {
                    string previous = Path.Combine(backup, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(previous));
                    File.Replace(source, target, previous, true);
                }
                else File.Move(source, target);
                replaced.Add(file);
            }
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            replaced.Reverse();
            foreach (var file in replaced)
            {
                string target = Path.Combine(destination, file);
                string previous = Path.Combine(backup, file);
                try
                {
                    if (File.Exists(previous)) File.Replace(previous, target, null, true);
                    else File.Delete(target);
                }
                catch (Exception rollbackError) { failures.Add(rollbackError); }
            }
            if (failures.Count > 0)
            {
                failures.Insert(0, error);
                throw new IOException("The update could not restore all launcher files. Keep the backup at " + backup + ".", new AggregateException(failures));
            }
            throw;
        }
    }

    private static void EnsureOrdinaryPath(string path)
    {
        for (; path != null; path = Path.GetDirectoryName(path))
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Setup cannot follow linked files or folders: " + path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void TryRemoveWorkspace(string workspace)
    {
        try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static List<string> ExtractLauncher(string destination)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.zip"))
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                string name = entry.FullName.Replace('\\', '/');
                if (name.Split('/').Any(segment => segment.Length == 0 || segment.EndsWith(".") ||
                    segment.EndsWith(" ") || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                    (!name.StartsWith("ui/", StringComparison.OrdinalIgnoreCase) &&
                     (name.Contains("/") || (!name.StartsWith("FifaStreetLauncher.", StringComparison.OrdinalIgnoreCase) &&
                      !name.StartsWith("Microsoft.Web.WebView2.", StringComparison.OrdinalIgnoreCase)))) ||
                    !seen.Add(name))
                    throw new InvalidDataException("Invalid launcher archive entry: " + name);

                var output = Path.Combine(destination, name);
                EnsureOrdinaryPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                entry.ExtractToFile(output, true);
                files.Add(name);
            }
        }
        if (!seen.Contains(LauncherName) || !seen.Contains("ui/launcher.html"))
            throw new InvalidDataException("The launcher archive is incomplete.");
        return files;
    }
}
