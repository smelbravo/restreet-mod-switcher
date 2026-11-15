using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class SwitcherSetup
{
    private const string SetupUrl = "https://github.com/samuelit0davila/fifa-street-pc-port/releases/download/v2.1.4-online-beta/ReStreet-2.1.4-Setup.exe";
    private const string SetupHash = "467930DCBECB1F4D24E5489B19EB6139A0131CECB29A31036838B8843453F149";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using (var iso = new OpenFileDialog { Title = "Select your FIFA Street (2012) Xbox 360 ISO", Filter = "Xbox 360 ISO (*.iso)|*.iso" })
        using (var folder = new FolderBrowserDialog { Description = "Create or select an empty folder for ReStreet Mod Switcher." })
        using (var window = new Form { Text = "ReStreet Mod Switcher Setup", Width = 520, Height = 155, MaximizeBox = false, StartPosition = FormStartPosition.CenterScreen })
        {
            if (iso.ShowDialog() != DialogResult.OK || folder.ShowDialog() != DialogResult.OK) return;
            string destination = folder.SelectedPath;
            if (Directory.GetFileSystemEntries(destination).Length != 0)
            {
                MessageBox.Show("Choose an empty destination folder.", window.Text);
                return;
            }
            var status = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Preparing setup…" };
            window.Controls.Add(status);
            bool installing = true;
            window.FormClosing += (sender, e) => { if (installing) e.Cancel = true; };
            window.Shown += async (sender, e) =>
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
                    await Task.Run(() => InstallBase(setup, iso.FileName, destination));
                    status.Text = "Installing the Mod Switcher launcher…";
                    await Task.Run(() => InstallLauncher(destination));
                    MessageBox.Show(window, "Setup complete. Open FifaStreetLauncher.exe in the installation folder.", window.Text);
                }
                catch (Exception error)
                {
                    MessageBox.Show(window, error.Message, window.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    installing = false;
                    window.Close();
                }
            };
            Application.Run(window);
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
        var args = "--iso-install-test \"" + iso + "\" \"" + destination.TrimEnd(Path.DirectorySeparatorChar) + "\" Player";
        using (var process = Process.Start(new ProcessStartInfo(setup, args) { UseShellExecute = false, CreateNoWindow = true }))
        {
            process.WaitForExit();
            if (process.ExitCode != 0 ||
                !File.Exists(Path.Combine(destination, "Binaries", "fifastreet.exe")) ||
                !File.Exists(Path.Combine(destination, "Game", "default.xex")))
                throw new IOException("ReStreet setup failed. Exit code: " + process.ExitCode + ". Check the installation log and use a new empty folder when retrying.");
        }
    }

    private static void InstallLauncher(string destination)
    {
        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.zip"))
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                var output = Path.Combine(destination, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                entry.ExtractToFile(output, true);
            }
        }
    }
}
