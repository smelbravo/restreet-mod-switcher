using System.Buffers.Binary;
using ReStreetAssetScan;

namespace FifaStreetLauncher;

internal static class ModCrests
{
    internal static byte[]? ReadModCrest(string gameRoot, int assetId)
    {
        if (assetId <= 0) return null;

        const string crestFolder = "data/ui/imgassets/crest";
        string file = FormattableString.Invariant($"l{assetId}.dds");
        var paths = new[]
        {
            $"{crestFolder}/licensed_medium/{file}",
            $"{crestFolder}/licensed_small/{file}",
            $"{crestFolder}/licensed_large/{file}",
        };
        byte[]? dds = ModAssets.Read(gameRoot, paths, "data1.big")
            ?? ModAssets.Read(gameRoot, paths);
        if (dds is not { Length: >= 128 } || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8))
            return null;

        // Licensed crests are up to 256 × 256. Avoid allocating a large decoded image for malformed assets.
        int height = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12));
        int width = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16));
        if (width is <= 0 or > 512 || height is <= 0 or > 512)
            return null;

        return Textures.ToPng(dds).FirstOrDefault().Png;
    }
}
