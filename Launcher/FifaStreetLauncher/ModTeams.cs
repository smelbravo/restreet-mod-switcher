using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FifaStreetLauncher;

internal static partial class ModTeams
{
    internal sealed record Team(int Id, int AssetId, string Name, string[] Groups);

    internal static IReadOnlyList<Team> ReadTeams(string gameRoot, string? language = null, string? vanilla = null)
    {
        byte[] db = ModAssets.Read(gameRoot, "data/db/fifa_street_db.db", "data0.big")
            ?? throw new InvalidDataException("The game team database is missing.");
        byte[] metadata = ModAssets.Read(gameRoot, "data/db/fifa_street_db-meta.xml", "data0.big")
            ?? throw new InvalidDataException("The game team schema is missing.");
        var schema = ReadSchema(metadata, "teams");
        var table = FindTable(db, schema);
        var idField = table.FindField(schema, "teamid");
        var assetField = table.FindField(schema, "assetid");
        var nameField = table.FindField(schema, "teamname");

        var (labels, leagueLabels) = ReadLocaleNames(gameRoot);
        if (GameLocaleStrings.GetFiles(language) != GameLocaleStrings.GetFiles("en"))
        {
            var (localizedTeams, localizedLeagues) = ReadLocaleNames(gameRoot, language);
            var (stockTeams, stockLeagues) = vanilla == null ? (null, null) : ReadLocaleNames(vanilla, language);
            var (stockEnglishTeams, stockEnglishLeagues) = vanilla == null ? (null, null) : ReadLocaleNames(vanilla);
            PreferLocalizedNames(labels, localizedTeams, stockTeams, stockEnglishTeams);
            PreferLocalizedNames(leagueLabels, localizedLeagues, stockLeagues, stockEnglishLeagues);
        }
        var groups = ReadTeamGroups(db, metadata, leagueLabels);
        var teams = new Dictionary<int, Team>();
        for (int i = 0; i < table.Count; i++)
        {
            ReadOnlySpan<byte> row = table.Record(i);
            int id = ReadNumber(row, idField);
            int assetId = ReadNumber(row, assetField);
            if (id <= 0) continue;
            string name = labels.GetValueOrDefault(id) ?? ReadInlineString(row, nameField);
            if (!string.IsNullOrWhiteSpace(name))
                teams.TryAdd(id, new Team(id, assetId, name.Trim(), groups.GetValueOrDefault(id) ?? []));
        }
        return teams.Values.OrderBy(team => team.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(team => team.Id).ToArray();
    }

    private static Dictionary<int, string[]> ReadTeamGroups(byte[] db, byte[] metadata, Dictionary<int, string> labels)
    {
        try
        {
            var leagueSchema = ReadSchema(metadata, "leagues");
            var leagues = FindTable(db, leagueSchema);
            var leagueId = leagues.FindField(leagueSchema, "leagueid");
            var leagueName = leagues.FindField(leagueSchema, "leaguename");
            var names = new Dictionary<int, string>();
            for (int i = 0; i < leagues.Count; i++)
            {
                var row = leagues.Record(i);
                int id = ReadNumber(row, leagueId);
                string name = labels.GetValueOrDefault(id) ?? ReadInlineString(row, leagueName);
                if (!string.IsNullOrWhiteSpace(name)) names.TryAdd(id, name.Trim());
            }

            var linkSchema = ReadSchema(metadata, "leagueteamlinks");
            var links = FindTable(db, linkSchema);
            var linkLeague = links.FindField(linkSchema, "leagueid");
            var linkTeam = links.FindField(linkSchema, "teamid");
            var groups = new Dictionary<int, HashSet<string>>();
            for (int i = 0; i < links.Count; i++)
            {
                var row = links.Record(i);
                if (!names.TryGetValue(ReadNumber(row, linkLeague), out string? name)) continue;
                int teamId = ReadNumber(row, linkTeam);
                if (!groups.TryGetValue(teamId, out var teamGroups))
                    groups.Add(teamId, teamGroups = new(StringComparer.OrdinalIgnoreCase));
                teamGroups.Add(name);
            }
            return groups.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or FormatException or OverflowException)
        {
            // Some mods omit league tables. The full team list and text search still work.
            return new();
        }
    }

    private static void PreferLocalizedNames(Dictionary<int, string> names, Dictionary<int, string> localized,
        Dictionary<int, string>? stock, Dictionary<int, string>? stockEnglish)
    {
        foreach (var (id, name) in localized)
        {
            // An unmodified language file can still have Vanilla's name for an edited team.
            if (stock?.GetValueOrDefault(id) == name && names.TryGetValue(id, out string? changed) &&
                changed != stockEnglish?.GetValueOrDefault(id)) continue;
            names[id] = name;
        }
    }

    private static (Dictionary<int, string> Teams, Dictionary<int, string> Leagues) ReadLocaleNames(string gameRoot, string? language = null)
    {
        var names = new Dictionary<int, string>();
        var leagues = new Dictionary<int, string>();
        try
        {
            var locale = GameLocaleStrings.GetFiles(language);
            string archive = "data/loc/" + locale.Archive;
            string prefix = "data/loc/" + locale.Database;
            var db = ModAssets.Read(gameRoot, prefix + ".db", archive);
            var metadata = ModAssets.Read(gameRoot, prefix + "-meta.xml", archive);
            if (db == null || metadata == null) return (names, leagues);

            var schema = ReadSchema(metadata, "LanguageStrings");
            var table = FindTable(db, schema);
            var keyField = table.FindField(schema, "stringid");
            var textField = table.FindField(schema, "sourcetext");
            // The location of the earliest string also marks the end of the Huffman tree.
            int treeSize = int.MaxValue;
            for (int i = 0; i < table.Count; i++)
            {
                ReadOnlySpan<byte> row = table.Record(i);
                int key = ReadNumber(row, keyField);
                int value = ReadNumber(row, textField);
                if (key >= 0) treeSize = Math.Min(treeSize, key);
                if (value >= 0) treeSize = Math.Min(treeSize, value);
            }
            ReadOnlySpan<byte> strings = table.Strings;
            if (treeSize < 4 || treeSize > strings.Length || treeSize % 4 != 0) return (names, leagues);

            for (int i = 0; i < table.Count; i++)
            {
                ReadOnlySpan<byte> row = table.Record(i);
                string? key = GameLocaleStrings.Read(strings, treeSize, ReadNumber(row, keyField), 1, 24);
                if (key == null) continue;
                bool isTeam = key.StartsWith("TeamName_", StringComparison.Ordinal);
                bool isLeague = key.StartsWith("LeagueName_", StringComparison.Ordinal);
                if (!(isTeam || isLeague) ||
                    !int.TryParse(key.AsSpan(isTeam ? "TeamName_".Length : "LeagueName_".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                    continue;
                string? name = GameLocaleStrings.Read(strings, treeSize, ReadNumber(row, textField), 2, 256);
                if (!string.IsNullOrWhiteSpace(name)) (isTeam ? names : leagues).TryAdd(id, name.Trim());
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or XmlException)
        {
            // The team database still provides names if this language is unavailable or invalid.
        }
        return (names, leagues);
    }

    private static XElement ReadSchema(byte[] metadata, string table)
    {
        using var stream = new MemoryStream(metadata, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 4 * 1024 * 1024
        });
        return XDocument.Load(reader).Descendants("table").FirstOrDefault(element => (string?)element.Attribute("name") == table)
            ?? throw new InvalidDataException($"The {table} table schema is missing.");
    }

    private static Table FindTable(byte[] database, XElement schema)
    {
        ReadOnlySpan<byte> bytes = database;
        if (bytes.Length < 36 || !bytes[..2].SequenceEqual("DB"u8))
            throw new InvalidDataException("Invalid game database.");
        // The Xbox tables use big endian integers. Only this format is supported.
        int tableCount = Big32(bytes[16..]);
        if (tableCount is <= 0 or > 4096 || 28 + tableCount * 8 > bytes.Length)
            throw new InvalidDataException("Invalid game database table directory.");

        string? tag = (string?)schema.Attribute("shortname");
        int origin = 28 + tableCount * 8;
        for (int i = 0; i < tableCount; i++)
        {
            int offset = 24 + i * 8;
            if (Encoding.ASCII.GetString(bytes.Slice(offset, 4)) != tag) continue;
            int table = Big32(bytes[(offset + 4)..]);
            int limit = i + 1 == tableCount ? bytes.Length - origin : Big32(bytes[(offset + 12)..]);
            if (table < 0 || limit > bytes.Length - origin || (long)limit - table < 36)
                throw new InvalidDataException("Invalid game database table bounds.");
            return new Table(database.AsMemory(origin + table, limit - table));
        }
        throw new InvalidDataException("The game database table is missing.");
    }

    private sealed class Table
    {
        private readonly ReadOnlyMemory<byte> data;
        private readonly int stride;
        private readonly int fields;
        private readonly int recordStart;
        private readonly int stringStart;
        private readonly int stringLength;
        internal int Count { get; }
        internal ReadOnlySpan<byte> Strings => data.Span.Slice(stringStart, stringLength);

        internal Table(ReadOnlyMemory<byte> data)
        {
            this.data = data;
            var header = data.Span;
            stride = Big32(header[4..]);
            stringLength = Big32(header[12..]);
            int totalRecords = Big16(header[16..]);
            Count = Big16(header[18..]);
            fields = header[24];
            recordStart = 36 + 16 * fields;
            long end = recordStart + (long)stride * totalRecords;
            if (stride is <= 0 or > 65536 || Count > totalRecords || stringLength < 0 || end + stringLength > data.Length)
                throw new InvalidDataException("Invalid game database record layout.");
            stringStart = (int)end;
        }

        internal ReadOnlySpan<byte> Record(int index) => data.Span.Slice(recordStart + index * stride, stride);

        internal Field FindField(XElement schema, string name)
        {
            var metadata = schema.Descendants("field").FirstOrDefault(field => (string?)field.Attribute("name") == name);
            string? tag = (string?)metadata?.Attribute("shortname");
            for (int i = 0; i < fields; i++)
            {
                var descriptor = data.Span.Slice(36 + 16 * i, 16);
                if (Encoding.ASCII.GetString(descriptor.Slice(8, 4)) != tag) continue;
                int kind = Big32(descriptor);
                int offset = Big32(descriptor[4..]);
                int width = Big32(descriptor[12..]);
                // A locale pointer occupies 32 bits in a record; its descriptor width defines
                // the maximum string size, not the pointer width.
                if (kind is 13 or 14) width = 32;
                int bias = (int?)metadata?.Attribute("rangelow") ?? 0;
                if (offset < 0 || width <= 0 || (long)offset + width > stride * 8)
                    throw new InvalidDataException("Invalid game database field layout.");
                return new Field(offset, width, bias, kind);
            }
            throw new InvalidDataException($"The game database field {name} is missing.");
        }
    }

    private readonly record struct Field(int BitOffset, int Width, int Bias, int Kind);

    private static int ReadNumber(ReadOnlySpan<byte> row, Field field)
    {
        if (field.Width > 32) throw new InvalidDataException("Invalid game database integer.");
        uint result = 0;
        for (int i = 0; i < field.Width; i++)
        {
            int bit = field.BitOffset + i;
            result = (result << 1) | (uint)((row[bit / 8] >> (7 - bit % 8)) & 1);
        }
        return unchecked((int)result + field.Bias);
    }

    private static string ReadInlineString(ReadOnlySpan<byte> row, Field field)
    {
        if (field.BitOffset % 8 != 0 || field.Width % 8 != 0) return "";
        var text = row.Slice(field.BitOffset / 8, field.Width / 8);
        int zero = text.IndexOf((byte)0);
        return Encoding.UTF8.GetString(zero < 0 ? text : text[..zero]);
    }

    private static int Big32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32BigEndian(bytes);
    private static int Big16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16BigEndian(bytes);
}
