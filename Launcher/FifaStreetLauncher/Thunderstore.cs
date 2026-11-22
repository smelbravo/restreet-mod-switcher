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

    private sealed record ThunderstorePackage(string Namespace, string Name, string Description)
    {
        public string Id => Namespace + "/" + Name;
    }

    private sealed record ThunderstorePage(string? Next, ThunderstorePackage[] Results);
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
                packages = packages.Values.Select(p => new { id = p.Id, author = p.Namespace, name = p.Name, description = p.Description })
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

    private async Task DownloadThunderstoreAsync(string id)
    {
        if (!thunderstorePackages.ContainsKey(id)) return;
        try
        {
            var detailsUrl = new Uri(ThunderstoreCommunity, $"/api/experimental/package/{id}/");
            var details = await ThunderstoreHttp.GetFromJsonAsync<ThunderstoreDetails>(detailsUrl);
            if (details?.Latest?.Number is not { } version || !VersionSegment.IsMatch(version))
                throw new JsonException("Missing Thunderstore package version.");

            OpenThunderstoreUrl(new Uri(ThunderstoreCommunity, $"/package/download/{id}/{version}/"));
            PostToPage(new { type = "thunderstoreAction", id });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException or Win32Exception)
        {
            Debug.WriteLine(ex);
            PostToPage(new { type = "thunderstoreAction", id, error = true });
        }
    }

    private static void OpenThunderstoreUrl(Uri url) =>
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
