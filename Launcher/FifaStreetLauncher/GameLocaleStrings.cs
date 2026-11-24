using System.Buffers.Binary;
using System.Text;

namespace FifaStreetLauncher;

internal static class GameLocaleStrings
{
    internal static string? Read(ReadOnlySpan<byte> strings, int treeSize, int offset, int lengthBytes, int maxLength)
    {
        if (offset < treeSize || offset > strings.Length - lengthBytes) return null;
        int count = lengthBytes == 1 ? strings[offset] : BinaryPrimitives.ReadUInt16BigEndian(strings[offset..]);
        if (count > maxLength) return null;
        if (count == 0) return "";

        Span<byte> decoded = count <= 512 ? stackalloc byte[count] : new byte[count];
        int node = 0;
        int written = 0;
        // Each branch is a pair of bytes. A nonzero first byte is a node index.
        // A zero first byte marks a leaf whose second byte is a character.
        for (int pos = offset + lengthBytes; pos < strings.Length; pos++)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                int edge = node * 4 + ((strings[pos] >> bit) & 1) * 2;
                if (edge >= treeSize - 1) return null;
                node = strings[edge];
                if (node != 0) continue;
                decoded[written++] = strings[edge + 1];
                if (written == count) return Encoding.UTF8.GetString(decoded);
            }
            // A valid byte-valued Huffman tree has at most 255 branches per symbol.
            if (pos - offset > count * 32 + lengthBytes) return null;
        }
        return null;
    }
}
