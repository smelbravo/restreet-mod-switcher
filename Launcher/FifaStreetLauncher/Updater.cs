using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace FifaStreetLauncher;

// The switcher checks its own releases and asks before applying a verified installer in place.
public partial class LauncherForm
{
    internal const string LauncherVersion = "2.1.5";
    private static readonly string SwitcherVersion = typeof(LauncherForm).Assembly.GetName().Version!.ToString(3);
    private const string ReleasesApi = "https://api.github.com/repos/smelbravo/restreet-mod-switcher/releases";
    private const string ReleaseDownloads = "https://github.com/smelbravo/restreet-mod-switcher/releases/download/";
    private const string InstallerName = "ReStreet-Mod-Switcher-Setup.exe";
    private static readonly HttpClient UpdateHttp = CreateUpdateHttp();
    private (string Version, string ExeUrl, string ShaUrl, string Notes)? pendingUpdate;
    private bool updating;

    private static HttpClient CreateUpdateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReStreetLauncher/" + LauncherVersion);
        return http;
    }

    private System.Windows.Forms.Timer? updateTimer;

    // A launcher left open for days still hears about a new version: ask GitHub again every 2 hours (its API allows
    // 60 requests an hour per address) until one is found.
    private void StartUpdateChecks()
    {
        _ = CheckForUpdateAsync();
        updateTimer = new System.Windows.Forms.Timer { Interval = 2 * 60 * 60 * 1000 };
        updateTimer.Tick += (_, _) => { if (pendingUpdate == null) _ = CheckForUpdateAsync(); };
        updateTimer.Start();
    }

    private async Task CheckForUpdateAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (!await FindUpdateAsync(timeout.Token)) return;
        // The page may still be loading (it asks again with "state"); give it a moment to say whether it is up.
        await Task.Delay(4000);
        await OfferUpdateAsync();
    }

    // Sets pendingUpdate when GitHub has a newer release with a patch; true when it did.
    private async Task<bool> FindUpdateAsync(CancellationToken cancel = default)
    {
        if (Environment.GetEnvironmentVariable("RESTREET_NO_UPDATE") == "1") return false;
        try
        {
            using var doc = JsonDocument.Parse(await UpdateHttp.GetStringAsync(ReleasesApi, cancel));
            pendingUpdate = FindSwitcherRelease(doc.RootElement, Version.Parse(SwitcherVersion));
            return pendingUpdate.HasValue;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static (string Version, string ExeUrl, string ShaUrl, string Notes)? FindSwitcherRelease(JsonElement releases, Version installed)
    {
        (string Version, string ExeUrl, string ShaUrl, string Notes)? update = null;
        Version newest = installed;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string? tag = release.GetProperty("tag_name").GetString();
            if (tag == null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
                version.Build < 0 || version.Revision >= 0 || tag != "v" + version || version <= newest)
                continue;

            string exe = ReleaseDownloads + tag + "/" + InstallerName;
            string sha = exe + ".sha256";
            bool hasExe = false, hasSha = false;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("state").GetString() != "uploaded") continue;
                string? name = asset.GetProperty("name").GetString();
                string? url = asset.GetProperty("browser_download_url").GetString();
                hasExe |= name == InstallerName && url == exe;
                hasSha |= name == InstallerName + ".sha256" && url == sha;
            }
            if (!hasExe || !hasSha) continue;
            newest = version;
            update = (version.ToString(), exe, sha, release.GetProperty("name").GetString() ?? tag);
        }
        return update;
    }

    private async Task OfferUpdateAsync()
    {
        if (pendingUpdate is not { } u) return;
        if (PageConnected)
        {
            PostToPage(new { type = "update", version = u.Version, notes = u.Notes });
        }
        else
        {
            // Classic interface (no WebView2, for example on Wine/Proton): ask with a plain dialog.
            var answer = MessageBox.Show(this, "ReStreet Mod Switcher " + u.Version + " is available.\n\nThe launcher downloads it from GitHub, " +
                "checks it and installs it for you. Your name, server and saves are kept.\n\nUpdate now?", "ReStreet update",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                UpdateStatus("Downloading the update...", true);
                await InstallUpdateAsync();
            }
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (pendingUpdate is not { } u || updating) return;
        if (launchingGame || gameRunning || AnyGameRunning())
        {
            ReportUpdateError("Close the game before updating ReStreet Mod Switcher.");
            return;
        }
        updating = true;
        string exePath = Path.Combine(Path.GetTempPath(), $"ReStreet-Mod-Switcher-{u.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            string expected = (await UpdateHttp.GetStringAsync(u.ShaUrl)).Trim().Split(' ', '\t')[0];
            if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("bad checksum file");
            using (var response = await UpdateHttp.GetAsync(u.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0, done = 0;
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.Create(exePath);
                var buffer = new byte[1 << 20];
                int read, lastPercent = -1;
                while ((read = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != lastPercent) { lastPercent = percent; PostToPage(new { type = "updateProgress", percent }); }
                }
            }
            string actual;
            await using (var check = File.OpenRead(exePath)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check)).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the download is damaged (checksum mismatch)");

            if (launchingGame || gameRunning || AnyGameRunning())
                throw new InvalidOperationException("Close the game before updating ReStreet Mod Switcher.");

            string root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            using var installer = Process.Start(new ProcessStartInfo(exePath)
            {
                ArgumentList = { "--auto-update", root, Environment.ProcessId.ToString() },
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("the installer could not be started");
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            updating = false;
            try { File.Delete(exePath); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            ReportUpdateError("Update failed: " + ex.Message);
        }
    }

    private void ReportUpdateError(string message)
    {
        PostToPage(new { type = "updateError", message });
        if (!PageConnected) UpdateStatus(message, false);
    }

    private void PostToPage(object message)
    {
        SendToPage(JsonSerializer.Serialize(message));
    }
}
