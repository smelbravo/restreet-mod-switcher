using System.Security.Cryptography;
using System.Buffers.Binary;

namespace FifaStreetLauncher;

internal static class ThunderstoreDataPatch
{
    private const uint Format = 0x31505352; // RSP1
    private const int HeaderSize = 44;
    private const int HashSize = 32;
    private const int CommandSize = 8;

    internal static void Apply(Stream patch, long patchSize, string vanilla, string relativePath, string output,
        CancellationToken cancellation)
    {
        if (patchSize < HeaderSize + HashSize + sizeof(int) + CommandSize)
            throw new InvalidDataException("Invalid game data patch size.");
        Span<byte> header = stackalloc byte[HeaderSize];
        patch.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Format)
            throw new InvalidDataException("Invalid game data patch format.");
        int outputSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (outputSize <= 0) throw new InvalidDataException("Invalid patched data size.");
        ReadOnlySpan<byte> expected = header.Slice(8, HashSize);
        int recipeCount = BinaryPrimitives.ReadInt32LittleEndian(header[(8 + HashSize)..]);
        if (recipeCount is < 1 or > 8) throw new InvalidDataException("Invalid game data patch recipe count.");

        string sourcePath = Path.Combine(vanilla, relativePath);
        if (!File.Exists(sourcePath)) throw new IncompatibleBaseException();
        using var source = File.OpenRead(sourcePath);
        byte[] actualHash = SHA256.HashDataAsync(source, cancellation).AsTask().GetAwaiter().GetResult();
        byte[] buffer = new byte[128 * 1024];
        Span<byte> recipe = stackalloc byte[HashSize + sizeof(int)];
        long remaining = patchSize - HeaderSize;
        for (int i = 0; i < recipeCount; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (remaining < recipe.Length) throw new InvalidDataException("Incomplete game data patch recipe.");
            patch.ReadExactly(recipe);
            remaining -= recipe.Length;
            int recipeSize = BinaryPrimitives.ReadInt32LittleEndian(recipe[HashSize..]);
            if (recipeSize < CommandSize || recipeSize > remaining)
                throw new InvalidDataException("Invalid game data patch recipe size.");
            remaining -= recipeSize;
            if (!recipe[..HashSize].SequenceEqual(actualHash))
            {
                CopyRange(patch, Stream.Null, recipeSize, buffer, null, cancellation);
                continue;
            }

            using var result = File.Create(output);
            using var resultHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            ApplyRecipe(patch, recipeSize, source, result, outputSize, buffer, resultHash, cancellation);
            if (!resultHash.GetHashAndReset().AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("Patched game data does not match its expected hash.");
            return;
        }

        throw new IncompatibleBaseException();
    }

    private static void ApplyRecipe(Stream patch, int recipeSize, Stream source, Stream output, int outputSize,
        byte[] buffer, IncrementalHash hash, CancellationToken cancellation)
    {
        Span<byte> command = stackalloc byte[CommandSize];
        int remainingOutput = outputSize;
        while (remainingOutput > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (recipeSize < CommandSize) throw new InvalidDataException("Incomplete game data patch command.");
            patch.ReadExactly(command);
            recipeSize -= CommandSize;
            int offset = BinaryPrimitives.ReadInt32LittleEndian(command);
            int length = BinaryPrimitives.ReadInt32LittleEndian(command[4..]);
            if (length <= 0 || length > remainingOutput || offset < -1 ||
                (offset >= 0 && offset > source.Length - length))
                throw new InvalidDataException("Invalid range in game data patch.");

            // Offset -1 inserts literal bytes. Other offsets copy data already installed in Vanilla.
            if (offset == -1)
            {
                if (length > recipeSize) throw new InvalidDataException("Incomplete game data patch contents.");
                CopyRange(patch, output, length, buffer, hash, cancellation);
                recipeSize -= length;
            }
            else
            {
                source.Position = offset;
                CopyRange(source, output, length, buffer, hash, cancellation);
            }
            remainingOutput -= length;
        }
        if (recipeSize != 0) throw new InvalidDataException("Extra data in game data patch.");
    }

    private static void CopyRange(Stream input, Stream output, int remaining, byte[] buffer,
        IncrementalHash? hash, CancellationToken cancellation)
    {
        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = Math.Min(remaining, buffer.Length);
            input.ReadExactly(buffer.AsSpan(0, count));
            output.Write(buffer.AsSpan(0, count));
            hash?.AppendData(buffer, 0, count);
            remaining -= count;
        }
    }

    internal sealed class IncompatibleBaseException : IOException { }
}
