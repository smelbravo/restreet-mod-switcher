using System.Security.Cryptography;

namespace FifaStreetLauncher;

internal static class ThunderstoreDataPatch
{
    private const uint Format = 0x31505352; // RSP1
    private const int MaxDataSize = 16 * 1024 * 1024;
    private const int MaxPatchSize = 2 * MaxDataSize;
    private const int HashSize = 32;

    internal static void Apply(Stream patch, long patchSize, string vanilla, string relativePath, string output,
        CancellationToken cancellation)
    {
        if (patchSize is < 45 or > MaxPatchSize) throw new InvalidDataException("Invalid game data patch size.");

        using var reader = new BinaryReader(patch, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Format) throw new InvalidDataException("Invalid game data patch format.");
        int outputSize = reader.ReadInt32();
        if (outputSize is <= 0 or > MaxDataSize) throw new InvalidDataException("Invalid patched data size.");
        byte[] expected = ReadBytes(reader, HashSize);
        int recipeCount = reader.ReadInt32();
        if (recipeCount is < 1 or > 8) throw new InvalidDataException("Invalid game data patch recipe count.");

        byte[]? source = ReadSource(Path.Combine(vanilla, relativePath));
        byte[]? actualHash = source == null ? null : SHA256.HashData(source);
        for (int i = 0; i < recipeCount; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            byte[] sourceHash = ReadBytes(reader, HashSize);
            int recipeSize = reader.ReadInt32();
            if (recipeSize is < 1 or > MaxPatchSize) throw new InvalidDataException("Invalid game data patch recipe size.");
            byte[] recipe = ReadBytes(reader, recipeSize);
            if (source == null || !sourceHash.AsSpan().SequenceEqual(actualHash)) continue;

            byte[] result = new byte[outputSize];
            ApplyRecipe(recipe, source, result, cancellation);
            if (!SHA256.HashData(result).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("Patched game data does not match its expected hash.");
            File.WriteAllBytes(output, result);
            return;
        }

        throw new IncompatibleBaseException();
    }

    private static byte[]? ReadSource(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length <= MaxDataSize ? File.ReadAllBytes(path) : null;
    }

    private static byte[] ReadBytes(BinaryReader reader, int length)
    {
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("Incomplete game data patch.");
        return bytes;
    }

    private static void ApplyRecipe(byte[] recipe, byte[] source, byte[] output, CancellationToken cancellation)
    {
        using var stream = new MemoryStream(recipe);
        using var reader = new BinaryReader(stream);
        int position = 0;
        while (position < output.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            int offset = reader.ReadInt32();
            int length = reader.ReadInt32();
            if (length <= 0 || length > output.Length - position || offset < -1 ||
                (offset >= 0 && offset > source.Length - length))
                throw new InvalidDataException("Invalid range in game data patch.");

            // Offset -1 inserts literal bytes. Other offsets copy data already installed in Vanilla.
            if (offset == -1)
                stream.ReadExactly(output.AsSpan(position, length));
            else
                source.AsSpan(offset, length).CopyTo(output.AsSpan(position, length));
            position += length;
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Extra data in game data patch.");
    }

    internal sealed class IncompatibleBaseException : IOException { }
}
