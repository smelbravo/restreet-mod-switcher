using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Microsoft.Xna.Framework.Content;

namespace FifaStreetLauncher;

// Read individual assets directly from the player's installation, honoring loose-file overrides.
internal static class ModAssets
{
    private const int MaxAssetSize = 64 * 1024 * 1024;
    private static readonly string[] GameArchives = Enumerable.Range(0, 8).Select(i => $"data{i}.big").ToArray();
    private static readonly ConcurrentDictionary<string, ArchiveIndex> Indexes = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct ArchiveEntry(int Offset, int Length);
    private sealed record ArchiveIndex(long Length, DateTime Modified, Dictionary<string, ArchiveEntry> Entries);

    internal static byte[]? Read(string gameRoot, string relativePath, params string[] archiveNames) =>
        Read(gameRoot, new[] { relativePath }, archiveNames);

    internal static byte[]? Read(string gameRoot, IReadOnlyList<string> relativePaths, params string[] archiveNames)
    {
        foreach (string relative in relativePaths)
        {
            string file = Path.Combine(gameRoot, relative);
            if (!File.Exists(file)) continue;
            using var input = File.OpenRead(file);
            return ReadAsset(input, checked((int)input.Length));
        }

        var archives = archiveNames.Length == 0 ? GameArchives : archiveNames;
        // Prefer names in the requested order when several resolutions or languages are acceptable.
        foreach (string candidate in relativePaths)
        {
            foreach (string archive in archives)
            {
                string path = Path.Combine(gameRoot, archive);
                var info = new FileInfo(path);
                if (!info.Exists) continue;

                if (!Indexes.TryGetValue(path, out var index) || index.Length != info.Length || index.Modified != info.LastWriteTimeUtc)
                {
                    using var input = File.OpenRead(path);
                    index = ReadIndex(input, info.LastWriteTimeUtc);
                    Indexes[path] = index;
                }

                if (!index.Entries.TryGetValue(candidate.Replace('\\', '/'), out var entry)) continue;

                using var archiveStream = File.OpenRead(path);
                archiveStream.Position = entry.Offset;
                return ReadAsset(archiveStream, entry.Length);
            }
        }
        return null;
    }

    private static ArchiveIndex ReadIndex(Stream input, DateTime modified)
    {
        Span<byte> header = stackalloc byte[16];
        input.ReadExactly(header);
        Require(header[..4].SequenceEqual("BIG4"u8) || header[..4].SequenceEqual("BIGF"u8), "Unsupported BIG archive.");
        int count = BigInt(header[8..]);
        int end = BigInt(header[12..]);
        Require(count is >= 0 and <= 100000 && end is >= 16 and <= 8 * 1024 * 1024 && end <= input.Length,
            "Invalid BIG directory.");

        var table = new byte[end - 16];
        input.ReadExactly(table);
        int position = 0;
        var result = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            Require(position <= table.Length - 9, "Truncated BIG directory.");
            int offset = BigInt(table.AsSpan(position));
            int length = BigInt(table.AsSpan(position + 4));
            position += 8;
            int zero = Array.IndexOf(table, (byte)0, position);
            Require(zero > position && offset >= end && length >= 0 && (long)offset + length <= input.Length, "Invalid BIG entry.");
            string name = Encoding.UTF8.GetString(table, position, zero - position).Replace('\\', '/');
            result.TryAdd(name, new(offset, length));
            position = zero + 1;
        }
        return new(input.Length, modified, result);
    }

    private static byte[] ReadAsset(Stream input, int size)
    {
        Require(size is >= 0 and <= MaxAssetSize, "Game asset exceeds size limit.");
        var data = new byte[size];
        input.ReadExactly(data);
        return Decode(data);
    }

    // Chunk framing matches ReStreetAssetScan's EA BIG decoder.
    private static byte[] Decode(byte[] stored)
    {
        if (stored.Length < 8 || !(stored.AsSpan(0, 8).SequenceEqual("chunklzx"u8) ||
            stored.AsSpan(0, 8).SequenceEqual("chunkzip"u8))) return stored;

        Require(stored.Length >= 48 && BigInt(stored.AsSpan(8)) == 2, "Unsupported chunk version.");
        int size = BigInt(stored.AsSpan(12));
        int chunkSize = BigInt(stored.AsSpan(16));
        Require(size is >= 0 and <= MaxAssetSize && chunkSize is > 0 and <= 1024 * 1024, "Invalid chunk size.");
        var result = new byte[size];
        using var output = new MemoryStream(result, writable: true);
        int position = 40;
        while (output.Position < size)
        {
            position = (position + 7) / 16 * 16 + 8;
            Require(position <= stored.Length - 8, "Truncated chunk header.");
            int compressed = BigInt(stored.AsSpan(position));
            int type = BigInt(stored.AsSpan(position + 4));
            position += 8;
            Require(compressed > 0 && compressed <= stored.Length - position, "Truncated chunk.");
            int expected = Math.Min(chunkSize, size - (int)output.Position);
            switch (type)
            {
                case 1:
                    using (var inflater = new DeflateStream(new MemoryStream(stored, position, compressed, false), CompressionMode.Decompress))
                    {
                        inflater.ReadExactly(result.AsSpan((int)output.Position, expected));
                        Require(inflater.ReadByte() == -1, "Unexpected data at end of compressed chunk.");
                    }
                    output.Position += expected;
                    break;
                case 3:
                    DecodeLzx(stored, position, compressed, output, expected);
                    break;
                case 4:
                    Require(compressed == expected, "Invalid uncompressed chunk size.");
                    output.Write(stored, position, compressed);
                    break;
                default:
                    throw new InvalidDataException("Unsupported game asset compression.");
            }
            position += compressed;
        }
        return result;
    }

    private static void DecodeLzx(byte[] stored, int position, int compressed, Stream output, int expected)
    {
        var decoder = new LzxDecoder(17);
        int end = position + compressed;
        while (expected > 0)
        {
            Require(position <= end - 2, "Truncated LZX frame.");
            int expanded = 32768;
            int size;
            if (stored[position] == 255)
            {
                Require(position <= end - 5, "Truncated extended LZX frame.");
                expanded = BinaryPrimitives.ReadUInt16BigEndian(stored.AsSpan(position + 1));
                size = BinaryPrimitives.ReadUInt16BigEndian(stored.AsSpan(position + 3));
                position += 5;
            }
            else
            {
                size = BinaryPrimitives.ReadUInt16BigEndian(stored.AsSpan(position));
                position += 2;
            }
            Require(size > 0 && size <= end - position && expanded > 0 && expanded <= expected, "Invalid LZX frame.");
            using var input = new MemoryStream(stored, position, size, false);
            Require(decoder.Decompress(input, size, output, expanded) == 0, "LZX decompression failed.");
            position += size;
            expected -= expanded;
        }
    }

    private static int BigInt(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32BigEndian(bytes);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
