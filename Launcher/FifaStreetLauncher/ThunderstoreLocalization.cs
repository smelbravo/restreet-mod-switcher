using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace FifaStreetLauncher;

internal static class ThunderstoreLocalization
{
    private const int MaximumArchiveSize = 32 * 1024 * 1024;
    private static readonly string[] Archives = ["locale.big", "locale_ptbr.big", "locale_ptpt.big"];
    private sealed record ArchiveEntry(int DirectoryOffset, int Offset, int Length, bool IsDatabase);
    private sealed record TextEntry(int Hash, string Key, string? Text);

    internal static void Apply(Stream input, long size, string game, CancellationToken cancellation)
    {
        if (size is <= 0 or > 1024 * 1024) throw new InvalidDataException("Invalid localization package size.");
        var updates = JsonSerializer.Deserialize<Dictionary<string, string>>(input);
        if (updates == null || updates.Count is < 1 or > 8192 ||
            updates.Any(entry => Encoding.UTF8.GetByteCount(entry.Key) is < 1 or > 255 ||
                entry.Value == null || Encoding.UTF8.GetByteCount(entry.Value) > ushort.MaxValue))
            throw new InvalidDataException("Invalid localization package strings.");

        string folder = Path.Combine(game, "data", "loc");
        bool applied = false;
        foreach (string name in Archives)
        {
            cancellation.ThrowIfCancellationRequested();
            var file = new FileInfo(Path.Combine(folder, name));
            if (!file.Exists) continue;
            if (file.Length > MaximumArchiveSize) throw new InvalidDataException("Invalid locale archive size.");
            File.WriteAllBytes(file.FullName, UpdateArchive(File.ReadAllBytes(file.FullName), updates, cancellation));
            applied = true;
        }
        if (!applied) throw new InvalidDataException("The base installation has no locale archives.");
    }

    private static byte[] UpdateArchive(byte[] original, Dictionary<string, string> updates, CancellationToken cancellation)
    {
        if (original.Length < 16 || !(original.AsSpan(0, 4).SequenceEqual("BIG4"u8) ||
            original.AsSpan(0, 4).SequenceEqual("BIGF"u8)))
            throw new InvalidDataException("Invalid locale archive.");

        int count = ReadInt(original, 8);
        int headerEnd = ReadInt(original, 12);
        if (count is < 1 or > 512 || headerEnd < 16 || headerEnd > original.Length)
            throw new InvalidDataException("Invalid locale archive directory.");
        var entries = new List<ArchiveEntry>();
        int position = 16;
        int firstEntry = original.Length;
        for (int i = 0; i < count; i++)
        {
            if (position > headerEnd - 9) throw new InvalidDataException("Truncated locale archive directory.");
            int offset = ReadInt(original, position);
            int length = ReadInt(original, position + 4);
            int nameEnd = Array.IndexOf(original, (byte)0, position + 8, headerEnd - position - 8);
            if (nameEnd < 0 || offset < headerEnd || length < 0 || offset > original.Length - length)
                throw new InvalidDataException("Invalid locale archive entry.");
            string name = Encoding.Latin1.GetString(original, position + 8, nameEnd - position - 8);
            entries.Add(new ArchiveEntry(position, offset, length, name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)));
            firstEntry = Math.Min(firstEntry, offset);
            position = nameEnd + 1;
        }
        if (!entries.Any(entry => entry.IsDatabase)) throw new InvalidDataException("The locale archive has no text database.");

        using var output = new MemoryStream();
        output.Write(original.AsSpan(0, firstEntry));
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            while (output.Position % 64 != 0) output.WriteByte(0);
            Span<byte> data = original.AsSpan(entry.Offset, entry.Length);
            byte[]? encoded = entry.IsDatabase ? UpdateDatabase(data, updates) : null;
            int newOffset = checked((int)output.Position);
            WriteInt(original, entry.DirectoryOffset, newOffset);
            WriteInt(original, entry.DirectoryOffset + 4, encoded?.Length ?? data.Length);
            output.Write(encoded != null ? encoded.AsSpan() : data);
        }
        byte[] archive = output.ToArray();
        original.AsSpan(0, firstEntry).CopyTo(archive);
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(4), archive.Length);
        return archive;
    }

    private static byte[] UpdateDatabase(ReadOnlySpan<byte> data, Dictionary<string, string> updates)
    {
        if (data.Length < 36 || !data[..2].SequenceEqual("DB"u8) || ReadInt(data, 16) != 1)
            throw new InvalidDataException("Invalid locale database.");
        long tableOffset = 36L + ReadInt(data, 28);
        if (tableOffset < 36 || tableOffset > data.Length - 36) throw new InvalidDataException("Missing locale table.");
        int table = (int)tableOffset;
        int totalRows = BinaryPrimitives.ReadUInt16BigEndian(data[(table + 16)..]);
        int rows = BinaryPrimitives.ReadUInt16BigEndian(data[(table + 18)..]);
        int poolLength = ReadInt(data, table + 12);
        int records = table + 36 + 16 * data[table + 24];
        int poolStart = records + totalRows * 16;
        if (ReadInt(data, table + 4) != 16 || data[table + 24] != 3 || rows > totalRows ||
            poolStart > data.Length || poolLength <= 0 || poolLength > data.Length - poolStart)
            throw new InvalidDataException("Invalid locale database records.");

        var pool = data.Slice(poolStart, poolLength);
        int treeSize = poolLength;
        for (int i = 0; i < rows; i++)
        {
            int row = records + i * 16;
            int key = ReadInt(data, row);
            int text = ReadInt(data, row + 4);
            if (key >= 0) treeSize = Math.Min(treeSize, key);
            if (text >= 0) treeSize = Math.Min(treeSize, text);
        }
        if (treeSize is < 4 or > 1020 || treeSize % 4 != 0)
            throw new InvalidDataException("Invalid locale string tree.");

        var entries = new List<TextEntry>(rows + updates.Count);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rows; i++)
        {
            int row = records + i * 16;
            string key = GameLocaleStrings.Read(pool, treeSize, ReadInt(data, row), 1, byte.MaxValue)
                ?? throw new InvalidDataException("Invalid locale key.");
            string? text = updates.TryGetValue(key, out string? replacement) ? replacement
                : GameLocaleStrings.Read(pool, treeSize, ReadInt(data, row + 4), 2, ushort.MaxValue);
            entries.Add(new TextEntry(ReadInt(data, row + 8) ^ int.MinValue, key, text));
            existing.Add(key);
        }
        foreach (var update in updates)
            if (!existing.Contains(update.Key)) entries.Add(new TextEntry(TextHash(update.Key), update.Key, update.Value));
        if (entries.Count > ushort.MaxValue) throw new InvalidDataException("Too many locale strings.");

        using var strings = new MemoryStream();
        WriteIdentityTree(strings);
        byte[] newRows = new byte[entries.Count * 16];
        var offsets = new Dictionary<(string Text, int Prefix), int>();
        int index = 0;
        foreach (var entry in entries.OrderBy(entry => entry.Hash))
        {
            WriteInt(newRows, index, StoreText(entry.Key, 1));
            WriteInt(newRows, index + 4, entry.Text == null ? -1 : StoreText(entry.Text, 2));
            WriteInt(newRows, index + 8, entry.Hash ^ int.MinValue);
            index += 16;
        }

        int stringsLength = checked((int)strings.Length);
        int padding = (8 - stringsLength % 8) % 8;
        byte[] result = new byte[records + newRows.Length + stringsLength + padding + 4];
        data[..records].CopyTo(result);
        newRows.CopyTo(result, records);
        strings.GetBuffer().AsSpan(0, stringsLength).CopyTo(result.AsSpan(records + newRows.Length));
        WriteInt(result, 8, result.Length);
        WriteInt(result, 20, unchecked((int)DatabaseChecksum(result.AsSpan(0, 20))));
        WriteInt(result, table + 12, stringsLength);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(table + 16), (ushort)entries.Count);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(table + 18), (ushort)entries.Count);
        WriteInt(result, table + 32, unchecked((int)DatabaseChecksum(result.AsSpan(table, 32))));
        WriteInt(result, result.Length - 4, unchecked((int)DatabaseChecksum(result.AsSpan(table + 36, result.Length - table - 40))));
        return result;

        int StoreText(string text, int lengthBytes)
        {
            if (offsets.TryGetValue((text, lengthBytes), out int offset)) return offset;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            offset = checked((int)strings.Position);
            if (lengthBytes == 2) strings.WriteByte((byte)(bytes.Length >> 8));
            strings.WriteByte((byte)bytes.Length);
            strings.Write(bytes);
            offsets.Add((text, lengthBytes), offset);
            return offset;
        }
    }

    // An eight-level tree assigns each byte its own eight-bit code, so existing and new
    // UTF-8 strings can share one tree even when a mod introduces new characters.
    private static void WriteIdentityTree(Stream output)
    {
        for (int node = 0; node < 255; node++)
        {
            for (int branch = 1; branch <= 2; branch++)
            {
                int child = node * 2 + branch;
                output.WriteByte(child < 255 ? (byte)child : (byte)0);
                output.WriteByte(child < 255 ? (byte)0 : (byte)(child - 255));
            }
        }
    }

    private static int TextHash(string key)
    {
        uint hash = 0;
        foreach (byte value in Encoding.UTF8.GetBytes(key))
        {
            hash ^= (uint)(value & 0xDF);
            for (int bit = 0; bit < 8; bit++)
                hash = (hash & 1) == 0 ? hash >> 1 : (hash >> 1) ^ 0xEDB88320;
        }
        return unchecked((int)hash) ^ int.MinValue;
    }

    private static uint DatabaseChecksum(ReadOnlySpan<byte> bytes)
    {
        uint checksum = uint.MaxValue;
        foreach (byte value in bytes)
        {
            checksum ^= (uint)value << 24;
            for (int bit = 0; bit < 8; bit++)
                checksum = (checksum & 0x80000000) == 0 ? checksum << 1 : (checksum << 1) ^ 0x04C11DB7;
        }
        return checksum;
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
    private static void WriteInt(Span<byte> data, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(data[offset..], value);
}
