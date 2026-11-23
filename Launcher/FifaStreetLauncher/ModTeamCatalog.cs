using System.Diagnostics;

namespace FifaStreetLauncher;

public partial class LauncherForm
{
    private sealed record ModCatalog(string GamePath, IReadOnlyList<ModTeams.Team> Teams);
    private ModCatalog? modCatalog;
    private CancellationTokenSource? modCatalogRequest;
    private CancellationTokenSource? modCrestRequest;

    private string CatalogGamePath => UseMod ? ModGamePath : gamePathBox.Text.Trim();

    private bool IsCurrentModCatalog(string root) =>
        !IsDisposed && string.Equals(CatalogGamePath, root, StringComparison.OrdinalIgnoreCase);

    private void CloseModTeams()
    {
        modCatalogRequest?.Cancel();
        modCrestRequest?.Cancel();
        modCatalog = null;
    }

    private async Task LoadModTeamsAsync(string id)
    {
        CloseModTeams();
        string root = CatalogGamePath;
        if (!IsCurrentModCatalog(root) || !HasModGameFiles(root))
        {
            PostToPage(new { type = "modTeams", id, error = true, missingFolder = true });
            return;
        }
        using var request = new CancellationTokenSource();
        modCatalogRequest = request;
        try
        {
            var teams = await Task.Run(() => ModTeams.ReadTeams(root), request.Token);
            if (request.IsCancellationRequested || !IsCurrentModCatalog(root)) return;
            modCatalog = new(root, teams);
            PostToPage(new
            {
                type = "modTeams", id,
                teams = teams.Select(team => new { id = team.Id, asset = team.AssetId, name = team.Name, groups = team.Groups })
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine(ex);
            if (!request.IsCancellationRequested && IsCurrentModCatalog(root))
                PostToPage(new { type = "modTeams", id, error = true });
        }
        finally
        {
            if (modCatalogRequest == request) modCatalogRequest = null;
        }
    }

    private async Task LoadTeamRosterAsync(string id, int teamId, bool official)
    {
        string root = official ? gamePathBox.Text.Trim() : CatalogGamePath;
        string saves = official ? SavesRoot() : ProfileSavesRoot();
        try
        {
            if (!HasModGameFiles(root)) throw new InvalidDataException("Game data is missing.");
            var players = await Task.Run(() => teamId == 0
                ? ModTeams.ReadWorldTourRoster(root, WorldTourSavePath(saves))
                : ModTeams.ReadRoster(root, teamId));
            if (IsDisposed) return;
            PostToPage(new
            {
                type = "teamRoster", id, name = players.TeamName,
                players = players.Players.Select(p => new
                {
                    name = p.Name, position = p.Position, number = p.Number,
                    overall = p.Overall, speed = p.Speed, shooting = p.Shooting, passing = p.Passing,
                    dribbling = p.Dribbling, defending = p.Defending, athleticism = p.Athleticism, goalkeeping = p.Goalkeeping
                })
            });
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or FormatException or OverflowException)
        {
            Debug.WriteLine(error);
            if (!IsDisposed) PostToPage(new { type = "teamRoster", id, error = true, worldTour = teamId == 0 });
        }
    }

    private static string? WorldTourSavePath(string root)
    {
        var latest = SaveTitleFolders(root).Select(title =>
        {
            string current = Path.Combine(title, SaveType);
            if (!Directory.Exists(current)) return null;
            return Directory.EnumerateDirectories(current)
                .Select(dir => (Name: Path.GetFileName(dir), Dir: dir))
                .Where(save => save.Name.StartsWith("Settings ", StringComparison.OrdinalIgnoreCase) && SaveTimeOf(save.Name) != null)
                .OrderByDescending(save => SaveTimeOf(save.Name))
                .Select(save => Path.Combine(save.Dir, save.Name)).FirstOrDefault(File.Exists);
        }).Where(file => file != null).Take(2).ToArray();
        return latest.Length == 1 ? latest[0] : null;
    }

    private async Task LoadModTeamCrestsAsync(string id, int[] ids)
    {
        if (modCatalog is not { } catalog || !IsCurrentModCatalog(catalog.GamePath)) return;
        modCrestRequest?.Cancel();
        using var request = new CancellationTokenSource();
        modCrestRequest = request;
        var knownAssets = catalog.Teams.Select(team => team.AssetId).ToHashSet();
        try
        {
            var crests = await Task.Run(() =>
            {
                var images = new Dictionary<int, string>();
                foreach (int asset in ids)
                {
                    request.Token.ThrowIfCancellationRequested();
                    if (!knownAssets.Contains(asset)) continue;
                    try
                    {
                        if (ModCrests.ReadModCrest(catalog.GamePath, asset) is { Length: > 0 } png)
                            images.Add(asset, Convert.ToBase64String(png));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Debug.WriteLine(ex);
                    }
                }
                return images;
            }, request.Token);
            if (!request.IsCancellationRequested && IsCurrentModCatalog(catalog.GamePath))
                PostToPage(new { type = "modTeamCrests", id, crests });
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (modCrestRequest == request) modCrestRequest = null;
        }
    }
}
