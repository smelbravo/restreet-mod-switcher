using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FifaStreetLauncher;

public partial class LauncherForm
{
    private static readonly Uri ThunderstoreCommunity = new("https://thunderstore.io/c/restreet/");
    private static readonly Uri ThunderstoreListing = new("https://thunderstore.io/api/cyberstorm/listing/restreet/");
    private static readonly HttpClient ThunderstoreHttp = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(12),
        DefaultRequestHeaders = { { "User-Agent", "ReStreet-Mod-Switcher" } }
    };
    private static readonly Regex PackageSegment = new(@"\A[A-Za-z0-9_]+\z");
    private static readonly Regex VersionSegment = new(@"\A[0-9]+\.[0-9]+\.[0-9]+\z");

    private Dictionary<string, ThunderstorePackage> thunderstorePackages = new();
    private bool thunderstoreLoading;
    private bool thunderstoreInstalling;

    private sealed record ThunderstoreCategory(string Name);
    private sealed record ThunderstorePackage(
        string Namespace,
        string Name,
        string? Description,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("last_updated")] DateTimeOffset? LastUpdated,
        [property: JsonPropertyName("download_count")] long? DownloadCount,
        [property: JsonPropertyName("latest_version_number")] string? LatestVersion,
        long? Size,
        ThunderstoreCategory[]? Categories)
    {
        public string Id => Namespace + "/" + Name;
    }

    private sealed record ThunderstorePage(string? Next, ThunderstorePackage[] Results);
    private sealed record ThunderstoreReadme(string? Markdown);
    private sealed record ThunderstoreDetails(ThunderstoreVersion Latest);
    private sealed record ThunderstoreVersion([property: JsonPropertyName("version_number")] string Number);

    private async Task LoadThunderstoreAsync()
    {
        if (thunderstoreLoading) return;
        thunderstoreLoading = true;
        try
        {
            var packages = new Dictionary<string, ThunderstorePackage>();
            var visited = new HashSet<Uri>();
            Uri? next = ThunderstoreListing;
            while (next != null)
            {
                if (next.GetLeftPart(UriPartial.Path) != ThunderstoreListing.AbsoluteUri || !visited.Add(next))
                    throw new JsonException("Invalid Thunderstore pagination URL.");

                var page = await ThunderstoreHttp.GetFromJsonAsync<ThunderstorePage>(next);
                if (page?.Results == null)
                    throw new JsonException("Missing Thunderstore results.");

                foreach (var package in page.Results)
                {
                    if (package?.Namespace is { } owner && package.Name is { } name &&
                        PackageSegment.IsMatch(owner) && PackageSegment.IsMatch(name))
                        packages.TryAdd(package.Id, package);
                }
                next = string.IsNullOrWhiteSpace(page.Next) ? null : new Uri(ThunderstoreListing, page.Next);
            }

            thunderstorePackages = packages;
            PostToPage(new
            {
                type = "thunderstoreCatalog",
                packages = packages.Values.Select(p =>
                {
                    Uri? icon = ThunderstoreIcon(p.IconUrl);
                    return new
                    {
                        id = p.Id, author = p.Namespace, name = p.Name, description = p.Description,
                        icon = icon?.AbsoluteUri, version = ThunderstoreCatalogVersion(p, icon),
                        updated = p.LastUpdated?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        downloads = p.DownloadCount, size = p.Size,
                        categories = p.Categories?.Select(category => category.Name)
                    };
                })
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or UriFormatException or NotSupportedException)
        {
            Debug.WriteLine(ex);
            PostToPage(new { type = "thunderstoreCatalog", error = true });
        }
        finally
        {
            thunderstoreLoading = false;
        }
    }

    private static Uri? ThunderstoreIcon(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps &&
        url.IsDefaultPort && url.UserInfo.Length == 0 &&
        (url.Host == "thunderstore.io" || url.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase))
            ? url : null;

    private static string? ThunderstoreCatalogVersion(ThunderstorePackage package, Uri? icon)
    {
        if (package.LatestVersion is { } latest && VersionSegment.IsMatch(latest)) return latest;
        // The list response can omit the version number. Thunderstore's versioned icon uses
        // the package's full name. Installation still resolves the version from the details API.
        if (icon == null) return null;
        string filename = Path.GetFileNameWithoutExtension(icon.AbsolutePath);
        string prefix = $"{package.Namespace}-{package.Name}-";
        if (!filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string version = filename[prefix.Length..];
        return VersionSegment.IsMatch(version) ? version : null;
    }

    private async Task LoadThunderstoreReadmeAsync(string id)
    {
        if (!thunderstorePackages.ContainsKey(id)) return;
        string? version = null;
        try
        {
            var detailUrl = new Uri(ThunderstoreListing, id + "/");
            var details = await ThunderstoreHttp.GetFromJsonAsync<ThunderstorePackage>(detailUrl);
            version = details?.LatestVersion;
            if (version == null || !VersionSegment.IsMatch(version))
                throw new JsonException("Missing Thunderstore package version.");
            var readmeUrl = new Uri(ThunderstoreCommunity, $"/api/experimental/package/{id}/{version}/readme/");
            var readme = await ThunderstoreHttp.GetFromJsonAsync<ThunderstoreReadme>(readmeUrl);
            PostToPage(new { type = "thunderstoreReadme", id, version, markdown = readme?.Markdown });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            Debug.WriteLine(ex);
            PostToPage(new { type = "thunderstoreReadme", id, version, error = true });
        }
    }

    private void OpenThunderstorePage(string id)
    {
        Uri? url = id.Length == 0 ? ThunderstoreCommunity
            : thunderstorePackages.ContainsKey(id) ? new Uri(ThunderstoreCommunity, $"p/{id}/")
            : null;
        if (url == null) return;

        try
        {
            OpenThunderstoreUrl(url);
        }
        catch (Win32Exception ex)
        {
            Debug.WriteLine(ex);
            PostToPage(new { type = "thunderstoreAction", id, error = true });
        }
    }

    private async Task InstallThunderstoreAsync(string id)
    {
        if (thunderstoreInstalling || !thunderstorePackages.TryGetValue(id, out var package)) return;
        if (launchingGame || AnyGameRunning())
        {
            ReportThunderstoreInstall(id, "Close the game before installing a mod.", true);
            return;
        }
        thunderstoreInstalling = true;
        string? staging = null;
        string? publishedGame = null;
        try
        {
            ReportThunderstoreInstall(id, "Downloading mod…", busy: true);
            var detailsUrl = new Uri(ThunderstoreCommunity, $"/api/experimental/package/{id}/");
            var details = await ThunderstoreHttp.GetFromJsonAsync<ThunderstoreDetails>(detailsUrl);
            if (details?.Latest?.Number is not { } version || !VersionSegment.IsMatch(version))
                throw new JsonException("Missing Thunderstore package version.");

            var profile = new ThunderstoreModProfile(package.Namespace, package.Name, "") { Version = version };
            var previous = communityProfiles.GetValueOrDefault(profile.Id);
            string vanilla = gamePathBox.Text.Trim();
            if (!HasModGameFiles(vanilla))
            {
                ReportThunderstoreInstall(id, "Set up the Vanilla game before installing a mod.", true);
                return;
            }
            if (previous?.Version == version && HasModGameFiles(previous.GamePath))
            {
                SelectModProfile(previous.Id);
                PushGlassState(true);
                ReportThunderstoreInstall(id, "This version of the mod is already installed.");
                return;
            }

            string installRoot = Path.Combine(AppContext.BaseDirectory, "Mods", "Thunderstore");
            Directory.CreateDirectory(installRoot);
            using var installLock = new FileStream(Path.Combine(installRoot, ".install.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            string token = Guid.NewGuid().ToString("N");
            staging = Path.Combine(installRoot, ".install-" + token);
            Directory.CreateDirectory(staging);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(45));
            var archives = await DownloadThunderstoreArchivesAsync(id, version, staging, timeout.Token);
            ReportThunderstoreInstall(id, "Creating the mod’s Game folder…", busy: true);
            string game = Path.Combine(staging, "Game");
            await Task.Run(() => ThunderstoreGameInstall.PrepareGame(vanilla, game, archives, timeout.Token), timeout.Token);

            if (launchingGame || AnyGameRunning())
            {
                ReportThunderstoreInstall(id, "Close the game before installing a mod.", true);
                return;
            }
            string baseName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(profile.Id.ToLowerInvariant())))[..16];
            publishedGame = Path.Combine(installRoot, baseName + "-" + version + "-" + token);
            Directory.CreateDirectory(publishedGame);
            string destination = Path.Combine(publishedGame, "Game");
            Directory.Move(game, destination);
            if (!SetCommunityProfile(profile with { GamePath = destination }))
                throw new IOException("Could not save the installed profile.");
            publishedGame = null;
            PushGlassState(true);
            ReportThunderstoreInstall(id, "Mod installed and selected.");
        }
        catch (ThunderstoreGameInstall.UnsupportedPackageException)
        {
            ReportThunderstoreInstall(id, "This package uses a format that cannot be installed automatically.", true);
        }
        catch (ThunderstoreDataPatch.IncompatibleBaseException)
        {
            ReportThunderstoreInstall(id, "This mod needs a different version of the Vanilla game files.", true);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or
            NotSupportedException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            ReportThunderstoreInstall(id, "Could not install the mod. Check the connection and available disk space, then try again.", true);
        }
        finally
        {
            RemoveIncompleteThunderstoreInstall(staging);
            RemoveIncompleteThunderstoreInstall(publishedGame);
            thunderstoreInstalling = false;
        }
    }

    private async Task<List<ThunderstoreGameInstall.Archive>> DownloadThunderstoreArchivesAsync(
        string id, string version, string staging, CancellationToken cancellation)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ThunderstoreGameInstall.Archive>();
        await Visit(id, version);
        return result;

        async Task Visit(string package, string release)
        {
            if (active.Contains(package)) throw new InvalidDataException("Circular Thunderstore dependencies.");
            if (packages.TryGetValue(package, out var pinned))
            {
                if (pinned != release) throw new InvalidDataException("Conflicting Thunderstore dependencies.");
                return;
            }
            if (packages.Count >= 32 || !thunderstorePackages.ContainsKey(package))
                throw new ThunderstoreGameInstall.UnsupportedPackageException();
            active.Add(package);
            packages.Add(package, release);
            string zip = Path.Combine(staging, $"package-{packages.Count}.zip");
            var url = new Uri(ThunderstoreCommunity, $"/package/download/{package}/{release}/");
            using (var response = await ThunderstoreHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                var final = response.RequestMessage?.RequestUri;
                if (final?.Scheme != Uri.UriSchemeHttps || !(final.Host == "thunderstore.io" ||
                    final.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The download did not come from Thunderstore.");
                await using var contents = await response.Content.ReadAsStreamAsync(cancellation);
                await using var file = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                await contents.CopyToAsync(file, cancellation);
            }
            var archive = ThunderstoreGameInstall.Inspect(zip);
            foreach (var dependency in archive.Dependencies)
            {
                var parts = dependency?.Split('-');
                if (parts?.Length != 3 || !PackageSegment.IsMatch(parts[0]) ||
                    !PackageSegment.IsMatch(parts[1]) || !VersionSegment.IsMatch(parts[2]))
                    throw new InvalidDataException("Invalid Thunderstore dependency.");
                await Visit(parts[0] + "/" + parts[1], parts[2]);
            }
            result.Add(archive);
            active.Remove(package);
        }
    }

    private void ReportThunderstoreInstall(string id, string note, bool error = false, bool busy = false) =>
        PostToPage(new { type = "thunderstoreInstall", id, note, error, busy });

    private static void RemoveIncompleteThunderstoreInstall(string? path)
    {
        if (path == null) return;
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }

    private static void OpenThunderstoreUrl(Uri url) =>
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
