using System.IO.Compression;

// Minimal RGBA8 PNG writer. Rows are given top first.
internal static class Png {
    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable() {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++) {
            var c = n;
            for (var k = 0; k < 8; k++) {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc(byte[] type, byte[] data) {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream stream, string type, byte[] data) {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        WriteBigEndian(stream, (uint) data.Length);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteBigEndian(stream, Crc(typeBytes, data));
    }

    private static void WriteBigEndian(Stream stream, uint value) {
        stream.WriteByte((byte) (value >> 24));
        stream.WriteByte((byte) (value >> 16));
        stream.WriteByte((byte) (value >> 8));
        stream.WriteByte((byte) value);
    }

    public static void Write(string path, byte[] rgba, int width, int height) {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var header = new byte[13];
        header[0] = (byte) (width >> 24); header[1] = (byte) (width >> 16); header[2] = (byte) (width >> 8);
        header[3] = (byte) width;
        header[4] = (byte) (height >> 24); header[5] = (byte) (height >> 16); header[6] = (byte) (height >> 8);
        header[7] = (byte) height;
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        Chunk(file, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true)) {
            var row = new byte[width * 4 + 1];
            for (var y = 0; y < height; y++) {
                row[0] = 0;
                Buffer.BlockCopy(rgba, y * width * 4, row, 1, width * 4);
                z.Write(row);
            }
        }

        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }
}
