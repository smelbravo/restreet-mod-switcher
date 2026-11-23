namespace FifaStreetLauncher;

internal static partial class ModTeams
{
    internal static TeamSquad ReadWorldTourRoster(string gameRoot, string? file)
    {
        if (file == null) throw new InvalidDataException("No unambiguous current save.");
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length is <= 0 or > 32 * 1024 * 1024) throw new InvalidDataException("Unexpected settings save size.");
        var save = new byte[(int)input.Length];
        input.ReadExactly(save);
        byte[] metadata = ReadGameSchema(gameRoot);
        var teamsSchema = ReadSchema(metadata, "teams");
        byte[] db = ReadSavedTeamsDb(save, teamsSchema);
        byte[] original = ReadRosterDb(gameRoot);
        var originalTeams = FindTable(original, teamsSchema);
        var teamIdField = originalTeams.FindField(teamsSchema, "teamid");
        var originalIds = new HashSet<int>();
        for (int i = 0; i < originalTeams.Count; i++)
            originalIds.Add(ReadNumber(originalTeams.Record(i), teamIdField));

        var statsSchema = ReadSchema(metadata, "worldtour_playerstats");
        var stats = FindTable(db, statsSchema);
        var statsPlayer = stats.FindField(statsSchema, "playerid");
        var tourPlayers = new HashSet<int>();
        for (int i = 0; i < stats.Count; i++)
            tourPlayers.Add(ReadNumber(stats.Record(i), statsPlayer));

        var linkSchema = ReadSchema(metadata, "teamplayerlinks");
        var links = FindTable(db, linkSchema);
        var linkTeam = links.FindField(linkSchema, "teamid");
        var linkPlayer = links.FindField(linkSchema, "playerid");
        var scores = new Dictionary<int, int>();
        for (int i = 0; i < links.Count; i++)
        {
            var row = links.Record(i);
            int teamId = ReadNumber(row, linkTeam);
            if (teamId > 0 && !originalIds.Contains(teamId) && tourPlayers.Contains(ReadNumber(row, linkPlayer)))
                scores[teamId] = scores.GetValueOrDefault(teamId) + 1;
        }
        var best = scores.OrderByDescending(pair => pair.Value).Take(2).ToArray();
        if (best.Length == 0 || (best.Length == 2 && best[0].Value == best[1].Value))
            throw new InvalidDataException("No unique World Tour squad in the current save.");

        var teams = FindTable(db, teamsSchema);
        var savedId = teams.FindField(teamsSchema, "teamid");
        var savedName = teams.FindField(teamsSchema, "teamname");
        int wanted = best[0].Key;
        string? name = null;
        for (int i = 0; i < teams.Count; i++)
        {
            var row = teams.Record(i);
            if (ReadNumber(row, savedId) == wanted)
            {
                name = ReadInlineString(row, savedName).Trim();
                break;
            }
        }
        var players = ReadRoster(db, metadata, wanted, ReadPlayerNames(original, metadata), ReadEditedNames(db, metadata));
        if (players.Count == 0) throw new InvalidDataException("The saved World Tour squad has no players.");
        return new(name, players);
    }

    private static byte[] ReadSavedTeamsDb(byte[] save, System.Xml.Linq.XElement schema)
    {
        ReadOnlySpan<byte> remaining = save;
        while (remaining.IndexOf("DB\0\x08"u8) is >= 0 and var offset)
        {
            var candidate = remaining[offset..];
            if (candidate.Length >= 36)
            {
                int length = Big32(candidate[8..]);
                int tables = Big32(candidate[16..]);
                if (length >= 36 && length <= candidate.Length && tables is > 10 and <= 4096)
                {
                    var db = candidate[..length].ToArray();
                    try { _ = FindTable(db, schema); return db; }
                    catch (InvalidDataException) { }
                }
            }
            remaining = candidate[4..];
        }
        throw new InvalidDataException("The settings save contains no recognised team database.");
    }

    private static Dictionary<int, string> ReadEditedNames(byte[] db, byte[] metadata)
    {
        var schema = ReadSchema(metadata, "editedplayernames");
        var table = FindTable(db, schema);
        var id = table.FindField(schema, "playerid");
        var first = table.FindField(schema, "firstname");
        var last = table.FindField(schema, "surname");
        var common = table.FindField(schema, "commonname");
        int treeSize = StringTreeSize(table, first, last, common);
        var names = new Dictionary<int, string>();
        for (int i = 0; i < table.Count; i++)
        {
            var row = table.Record(i);
            string name = ReadRosterString(table, row, common, treeSize).Trim();
            if (name.Length == 0)
                name = (ReadRosterString(table, row, first, treeSize) + " " + ReadRosterString(table, row, last, treeSize)).Trim();
            if (name.Length > 0) names.TryAdd(ReadNumber(row, id), name);
        }
        return names;
    }
}
