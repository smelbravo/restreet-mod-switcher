namespace FifaStreetLauncher;

internal static partial class ModTeams
{
    private static readonly string[] Positions =
    [
        "GK", "SW", "RWB", "RB", "RCB", "CB", "LCB", "LB", "LWB",
        "RDM", "CDM", "LDM", "RM", "RCM", "CM", "LCM", "LM", "RAM",
        "CAM", "LAM", "RF", "CF", "LF", "RW", "RS", "ST", "LS", "LW"
    ];

    internal sealed record RosterPlayer(string Name, string Position, int Number, int Overall,
        int Speed, int Shooting, int Passing, int Dribbling, int Defending, int Athleticism, int Goalkeeping);
    internal sealed record TeamSquad(string? TeamName, IReadOnlyList<RosterPlayer> Players);

    internal static TeamSquad ReadRoster(string gameRoot, int teamId)
    {
        return new(null, ReadRoster(ReadRosterDb(gameRoot), ReadGameSchema(gameRoot), teamId));
    }

    private static byte[] ReadRosterDb(string gameRoot) =>
        ModAssets.Read(gameRoot, "data/db/fifa_street_db.db", "data0.big")
        ?? throw new InvalidDataException("The game team database is missing.");

    private static byte[] ReadGameSchema(string gameRoot) =>
        ModAssets.Read(gameRoot, "data/db/fifa_street_db-meta.xml", "data0.big")
            ?? throw new InvalidDataException("The game team schema is missing.");

    private static IReadOnlyList<RosterPlayer> ReadRoster(byte[] db, byte[] metadata, int teamId,
        IReadOnlyDictionary<int, string>? baseNames = null, IReadOnlyDictionary<int, string>? editedNames = null)
    {
        var linkSchema = ReadSchema(metadata, "teamplayerlinks");
        var links = FindTable(db, linkSchema);
        var linkTeam = links.FindField(linkSchema, "teamid");
        var linkPlayer = links.FindField(linkSchema, "playerid");
        var linkPosition = links.FindField(linkSchema, "position");
        var linkNumber = links.FindField(linkSchema, "jerseynumber");
        var members = new Dictionary<int, (int Position, int Number)>();
        for (int i = 0; i < links.Count; i++)
        {
            var row = links.Record(i);
            if (ReadNumber(row, linkTeam) == teamId)
                members.TryAdd(ReadNumber(row, linkPlayer), (ReadNumber(row, linkPosition), ReadNumber(row, linkNumber)));
        }
        if (members.Count == 0) return [];

        var names = baseNames ?? ReadPlayerNames(db, metadata);
        var schema = ReadSchema(metadata, "players");
        var players = FindTable(db, schema);
        var id = players.FindField(schema, "playerid");
        var first = players.FindField(schema, "firstnameid");
        var last = players.FindField(schema, "lastnameid");
        var common = players.FindField(schema, "commonnameid");
        var preferred = players.FindField(schema, "preferredposition1");
        var overall = players.FindField(schema, "overallrating");
        var speed = players.FindField(schema, "fsspeed");
        var shooting = players.FindField(schema, "fsshooting");
        var passing = players.FindField(schema, "fspassing");
        var dribbling = players.FindField(schema, "fsdribbling");
        var defending = players.FindField(schema, "fsdefending");
        var athleticism = players.FindField(schema, "fsathleticism");
        var goalkeeping = players.FindField(schema, "fsgk");
        var result = new List<(int Position, RosterPlayer Player)>();
        for (int i = 0; i < players.Count; i++)
        {
            var row = players.Record(i);
            int playerId = ReadNumber(row, id);
            if (!members.TryGetValue(playerId, out var member)) continue;
            string? name = editedNames?.GetValueOrDefault(playerId);
            if (string.IsNullOrWhiteSpace(name)) name = names.GetValueOrDefault(ReadNumber(row, common));
            if (string.IsNullOrWhiteSpace(name))
            {
                string? given = names.GetValueOrDefault(ReadNumber(row, first));
                string? family = names.GetValueOrDefault(ReadNumber(row, last));
                name = $"{given} {family}".Trim();
            }
            if (string.IsNullOrWhiteSpace(name)) name = $"#{playerId}";
            int position = member.Position;
            if ((uint)position >= Positions.Length) position = ReadNumber(row, preferred);
            string label = (uint)position < Positions.Length ? Positions[position] : "—";
            result.Add((position, new(name, label, member.Number, ReadNumber(row, overall),
                ReadNumber(row, speed), ReadNumber(row, shooting), ReadNumber(row, passing),
                ReadNumber(row, dribbling), ReadNumber(row, defending), ReadNumber(row, athleticism),
                ReadNumber(row, goalkeeping))));
        }
        return result.OrderBy(item => item.Position).ThenByDescending(item => item.Player.Overall)
            .ThenBy(item => item.Player.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => item.Player).ToArray();
    }

    private static Dictionary<int, string> ReadPlayerNames(byte[] db, byte[] metadata)
    {
        var schema = ReadSchema(metadata, "playernames");
        var table = FindTable(db, schema);
        var id = table.FindField(schema, "nameid");
        var name = table.FindField(schema, "name");
        int treeSize = StringTreeSize(table, name);
        var result = new Dictionary<int, string>();
        for (int i = 0; i < table.Count; i++)
        {
            var row = table.Record(i);
            string value = ReadRosterString(table, row, name, treeSize).Trim();
            int nameId = ReadNumber(row, id);
            if (nameId > 0 && value.Length > 0) result.TryAdd(nameId, value);
        }
        return result;
    }

    private static int StringTreeSize(Table table, params Field[] fields)
    {
        int size = int.MaxValue;
        foreach (var field in fields)
        {
            if (field.Kind is not (13 or 14)) continue;
            for (int i = 0; i < table.Count; i++)
            {
                int offset = ReadNumber(table.Record(i), field);
                if (offset >= 0) size = Math.Min(size, offset);
            }
        }
        return size >= 4 && size <= table.Strings.Length && size % 4 == 0 ? size : 0;
    }

    private static string ReadRosterString(Table table, ReadOnlySpan<byte> row, Field field, int treeSize) =>
        field.Kind is 13 or 14
            ? (treeSize == 0 ? "" : ReadLocaleString(table.Strings, treeSize, ReadNumber(row, field), field.Kind == 13 ? 1 : 2, 256) ?? "")
            : ReadInlineString(row, field);
}
