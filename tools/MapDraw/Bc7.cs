// BC7 (BPTC) block decoder: 16 bytes -> 4x4 RGBA8, in the order the block lists its texels.
internal static class Bc7 {
    private static readonly byte[] Partition2 = {
        0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1, 0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,
        0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1, 0,0,0,1,0,0,1,1,0,0,1,1,0,1,1,1,
        0,0,0,0,0,0,0,1,0,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,1,0,1,1,1,1,1,1,1,
        0,0,0,1,0,0,1,1,0,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,1,0,0,1,1,0,1,1,1,
        0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,1,1,1,1,1,1,1,1,1,
        0,0,0,0,0,0,0,1,0,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,1,0,1,1,1,
        0,0,0,1,0,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,
        0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1, 0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,
        0,0,0,0,1,0,0,0,1,1,1,0,1,1,1,1, 0,1,1,1,0,0,0,1,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,1,0,0,0,1,1,1,0, 0,1,1,1,0,0,1,1,0,0,0,1,0,0,0,0,
        0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0, 0,0,0,0,1,0,0,0,1,1,0,0,1,1,1,0,
        0,0,0,0,0,0,0,0,1,0,0,0,1,1,0,0, 0,1,1,1,0,0,1,1,0,0,1,1,0,0,0,1,
        0,0,1,1,0,0,0,1,0,0,0,1,0,0,0,0, 0,0,0,0,1,0,0,0,1,0,0,0,1,1,0,0,
        0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0, 0,0,1,1,0,1,1,0,0,1,1,0,1,1,0,0,
        0,0,0,1,0,1,1,1,1,1,1,0,1,0,0,0, 0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0,
        0,1,1,1,0,0,0,1,1,0,0,0,1,1,1,0, 0,0,1,1,1,0,0,1,1,0,0,1,1,1,0,0,
        0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1, 0,0,0,0,1,1,1,1,0,0,0,0,1,1,1,1,
        0,1,0,1,1,0,1,0,0,1,0,1,1,0,1,0, 0,0,1,1,0,0,1,1,1,1,0,0,1,1,0,0,
        0,0,1,1,1,1,0,0,0,0,1,1,1,1,0,0, 0,1,0,1,0,1,0,1,1,0,1,0,1,0,1,0,
        0,1,1,0,1,0,0,1,0,1,1,0,1,0,0,1, 0,1,0,1,1,0,1,0,1,0,1,0,0,1,0,1,
        0,1,1,1,0,0,1,1,1,1,0,0,1,1,1,0, 0,0,0,1,0,0,1,1,1,1,0,0,1,0,0,0,
        0,0,1,1,0,0,1,0,0,1,0,0,1,1,0,0, 0,0,1,1,1,0,1,1,1,1,0,1,1,1,0,0,
        0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0, 0,0,1,1,1,1,0,0,1,1,0,0,0,0,1,1,
        0,1,1,0,0,1,1,0,1,0,0,1,1,0,0,1, 0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0,
        0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,0, 0,0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,
        0,0,0,0,0,0,1,0,0,1,1,1,0,0,1,0, 0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,0,
        0,1,1,0,1,1,0,0,1,0,0,1,0,0,1,1, 0,0,1,1,0,1,1,0,1,1,0,0,1,0,0,1,
        0,1,1,0,0,0,1,1,1,0,0,1,1,1,0,0, 0,0,1,1,1,0,0,1,1,1,0,0,0,1,1,0,
        0,1,1,0,1,1,0,0,1,1,0,0,1,0,0,1, 0,1,1,0,0,0,1,1,0,0,1,1,1,0,0,1,
        0,1,1,1,1,1,1,0,1,0,0,0,0,0,0,1, 0,0,0,1,1,0,0,0,1,1,1,0,0,1,1,1,
        0,0,0,0,1,1,1,1,0,0,1,1,0,0,1,1, 0,0,1,1,0,0,1,1,1,1,1,1,0,0,0,0,
        0,0,1,0,0,0,1,0,1,1,1,0,1,1,1,0, 0,1,0,0,0,1,0,0,0,1,1,1,0,1,1,1,
    };

    private static readonly byte[] Partition3 = {
        0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2, 0,0,0,1,0,0,1,1,2,2,1,1,2,2,2,1,
        0,0,0,0,2,0,0,1,2,2,1,1,2,2,1,1, 0,2,2,2,0,0,2,2,0,0,1,1,0,1,1,1,
        0,0,0,0,0,0,0,0,1,1,2,2,1,1,2,2, 0,0,1,1,0,0,1,1,0,0,2,2,0,0,2,2,
        0,0,2,2,0,0,2,2,1,1,1,1,1,1,1,1, 0,0,1,1,0,0,1,1,2,2,1,1,2,2,1,1,
        0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2, 0,0,0,0,1,1,1,1,1,1,1,1,2,2,2,2,
        0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2, 0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,
        0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2, 0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2,
        0,0,1,1,0,1,1,2,1,1,2,2,1,2,2,2, 0,0,1,1,2,0,0,1,2,2,0,0,2,2,2,0,
        0,0,0,1,0,0,1,1,0,1,1,2,1,1,2,2, 0,1,1,1,0,0,1,1,2,0,0,1,2,2,0,0,
        0,0,0,0,1,1,2,2,1,1,2,2,1,1,2,2, 0,0,2,2,0,0,2,2,0,0,2,2,1,1,1,1,
        0,1,1,1,0,1,1,1,0,2,2,2,0,2,2,2, 0,0,0,1,0,0,0,1,2,2,2,1,2,2,2,1,
        0,0,0,0,0,0,1,1,0,1,2,2,0,1,2,2, 0,0,0,0,1,1,0,0,2,2,1,0,2,2,1,0,
        0,1,2,2,0,1,2,2,0,0,1,1,0,0,0,0, 0,0,1,2,0,0,1,2,1,1,2,2,2,2,2,2,
        0,1,1,0,1,2,2,1,1,2,2,1,0,1,1,0, 0,0,0,0,0,1,1,0,1,2,2,1,1,2,2,1,
        0,0,2,2,1,1,0,2,1,1,0,2,0,0,2,2, 0,1,1,0,0,1,1,0,2,0,0,2,2,2,2,2,
        0,0,1,1,0,1,2,2,0,1,2,2,0,0,1,1, 0,0,0,0,2,0,0,0,2,2,1,1,2,2,2,1,
        0,0,0,0,0,0,0,2,1,1,2,2,1,2,2,2, 0,2,2,2,0,0,2,2,0,0,1,2,0,0,1,1,
        0,0,1,1,0,0,1,2,0,0,2,2,0,2,2,2, 0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,0,
        0,0,0,0,1,1,1,1,2,2,2,2,0,0,0,0, 0,1,2,0,1,2,0,1,2,0,1,2,0,1,2,0,
        0,1,2,0,2,0,1,2,1,2,0,1,0,1,2,0, 0,0,1,1,2,2,0,0,1,1,2,2,0,0,1,1,
        0,0,1,1,1,1,2,2,2,2,0,0,0,0,1,1, 0,1,0,1,0,1,0,1,2,2,2,2,2,2,2,2,
        0,0,0,0,0,0,0,0,2,1,2,1,2,1,2,1, 0,0,2,2,1,1,2,2,0,0,2,2,1,1,2,2,
        0,0,2,2,0,0,1,1,0,0,2,2,0,0,1,1, 0,2,2,0,1,2,2,1,0,2,2,0,1,2,2,1,
        0,1,0,1,2,2,2,2,2,2,2,2,0,1,0,1, 0,0,0,0,2,1,2,1,2,1,2,1,2,1,2,1,
        0,1,0,1,0,1,0,1,0,1,0,1,2,2,2,2, 0,2,2,2,0,1,1,1,0,2,2,2,0,1,1,1,
        0,0,0,2,1,1,1,2,0,0,0,2,1,1,1,2, 0,0,0,0,2,1,1,2,2,1,1,2,2,1,1,2,
        0,2,2,2,0,1,1,1,0,1,1,1,0,2,2,2, 0,0,0,2,1,1,1,2,1,1,1,2,0,0,0,2,
        0,1,1,0,0,1,1,0,0,1,1,0,2,2,2,2, 0,0,0,0,0,0,0,0,2,1,1,2,2,1,1,2,
        0,1,1,0,0,1,1,0,2,2,2,2,2,2,2,2, 0,0,2,2,0,0,1,1,0,0,1,1,0,0,2,2,
        0,0,2,2,1,1,2,2,1,1,2,2,0,0,2,2, 0,0,0,0,0,0,0,0,0,0,0,0,2,1,1,2,
        0,0,0,2,0,0,0,1,0,0,0,2,0,0,0,1, 0,2,2,2,1,2,2,2,0,2,2,2,1,2,2,2,
        0,1,0,1,2,2,2,2,2,2,2,2,2,2,2,2, 0,1,1,1,2,0,1,1,2,2,0,1,2,2,2,0,
    };

    private static readonly byte[] Anchor2 = {
        15,15,15,15,15,15,15,15, 15,15,15,15,15,15,15,15,
        15, 2, 8, 2, 2, 8, 8,15,  2, 8, 2, 2, 8, 8, 2, 2,
        15,15, 6, 8, 2, 8,15,15,  2, 8, 2, 2, 2,15,15, 6,
         6, 2, 6, 8,15,15, 2, 2, 15,15,15,15,15, 2, 2,15,
    };

    private static readonly byte[] Anchor3A = {
         3, 3,15,15, 8, 3,15,15,  8, 8, 6, 6, 6, 5, 3, 3,
         3, 3, 8,15, 3, 3, 6,10,  5, 8, 8, 6, 8, 5,15,15,
         8,15, 3, 5, 6,10, 8,15, 15, 3,15, 5,15,15,15,15,
         3,15, 5, 5, 5, 8, 5,10,  5,10, 8,13,15,12, 3, 3,
    };

    private static readonly byte[] Anchor3B = {
        15, 8, 8, 3,15,15, 3, 8, 15,15,15,15,15,15,15, 8,
        15, 8,15, 3,15, 8,15, 8,  3,15, 6,10,15,15,10, 8,
        15, 3,15,10,10, 8, 9,10,  6,15, 8,15, 3, 6, 6, 8,
        15, 3,15,15,15,15,15,15, 15,15,15,15, 3,15,15, 8,
    };

    private static readonly int[] W2 = { 0, 21, 43, 64 };
    private static readonly int[] W3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    private static readonly int[] W4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

    // subsets, partition bits, rotation bits, index selection bits, colour bits, alpha bits, endpoint p-bits,
    // shared p-bits, index bits, second index bits
    private static readonly int[,] Modes = {
        { 3, 4, 0, 0, 4, 0, 1, 0, 3, 0 },
        { 2, 6, 0, 0, 6, 0, 0, 1, 3, 0 },
        { 3, 6, 0, 0, 5, 0, 0, 0, 2, 0 },
        { 2, 6, 0, 0, 7, 0, 1, 0, 2, 0 },
        { 1, 0, 2, 1, 5, 6, 0, 0, 2, 3 },
        { 1, 0, 2, 0, 7, 8, 0, 0, 2, 2 },
        { 1, 0, 0, 0, 7, 7, 1, 0, 4, 0 },
        { 2, 6, 0, 0, 5, 5, 1, 0, 2, 0 },
    };

    private static int[] Weights(int bits) => bits switch { 2 => W2, 3 => W3, _ => W4 };

    private static int Expand(int value, int bits) => bits >= 8 ? value : (value << (8 - bits)) | (value >> (2 * bits - 8));

    private static int Interpolate(int a, int b, int w) => (a * (64 - w) + b * w + 32) >> 6;

    // Decodes one block into out[16 * 4] (RGBA per texel, texels in block order: row by row, 4 per row)
    public static void DecodeBlock(byte[] data, int at, byte[] output) {
        var lo = BitConverter.ToUInt64(data, at);
        var hi = BitConverter.ToUInt64(data, at + 8);
        var position = 0;

        int Read(int count) {
            if (count == 0) {
                return 0;
            }

            ulong value;
            if (position >= 64) {
                value = hi >> (position - 64);
            } else if (position + count <= 64) {
                value = lo >> position;
            } else {
                value = (lo >> position) | (hi << (64 - position));
            }

            position += count;
            return (int) (value & ((1UL << count) - 1));
        }

        var mode = 0;
        while (mode < 8 && Read(1) == 0) {
            mode++;
        }

        if (mode == 8) {
            Array.Clear(output, 0, 64);
            return;
        }

        int subsets = Modes[mode, 0], partitionBits = Modes[mode, 1], rotationBits = Modes[mode, 2];
        int selectionBits = Modes[mode, 3], colourBits = Modes[mode, 4], alphaBits = Modes[mode, 5];
        int endpointPBits = Modes[mode, 6], sharedPBits = Modes[mode, 7];
        int indexBits = Modes[mode, 8], indexBits2 = Modes[mode, 9];

        var partition = Read(partitionBits);
        var rotation = Read(rotationBits);
        var selection = Read(selectionBits);

        var endpoints = new int[6, 4];
        for (var c = 0; c < 3; c++) {
            for (var i = 0; i < subsets * 2; i++) {
                endpoints[i, c] = Read(colourBits);
            }
        }

        if (alphaBits > 0) {
            for (var i = 0; i < subsets * 2; i++) {
                endpoints[i, 3] = Read(alphaBits);
            }
        }

        var cBits = colourBits;
        var aBits = alphaBits;
        if (endpointPBits > 0) {
            for (var i = 0; i < subsets * 2; i++) {
                var p = Read(1);
                for (var c = 0; c < 3; c++) {
                    endpoints[i, c] = (endpoints[i, c] << 1) | p;
                }

                if (alphaBits > 0) {
                    endpoints[i, 3] = (endpoints[i, 3] << 1) | p;
                }
            }

            cBits++;
            if (alphaBits > 0) {
                aBits++;
            }
        } else if (sharedPBits > 0) {
            for (var s = 0; s < subsets; s++) {
                var p = Read(1);
                for (var e = 0; e < 2; e++) {
                    for (var c = 0; c < 3; c++) {
                        endpoints[s * 2 + e, c] = (endpoints[s * 2 + e, c] << 1) | p;
                    }

                    if (alphaBits > 0) {
                        endpoints[s * 2 + e, 3] = (endpoints[s * 2 + e, 3] << 1) | p;
                    }
                }
            }

            cBits++;
            if (alphaBits > 0) {
                aBits++;
            }
        }

        for (var i = 0; i < subsets * 2; i++) {
            for (var c = 0; c < 3; c++) {
                endpoints[i, c] = Expand(endpoints[i, c], cBits);
            }

            endpoints[i, 3] = alphaBits > 0 ? Expand(endpoints[i, 3], aBits) : 255;
        }

        Span<int> indices = stackalloc int[16];
        Span<int> indices2 = stackalloc int[16];
        for (var i = 0; i < 16; i++) {
            var anchor = i == 0 ||
                         subsets == 2 && i == Anchor2[partition] ||
                         subsets == 3 && (i == Anchor3A[partition] || i == Anchor3B[partition]);
            indices[i] = Read(indexBits - (anchor ? 1 : 0));
        }

        if (indexBits2 > 0) {
            for (var i = 0; i < 16; i++) {
                indices2[i] = Read(indexBits2 - (i == 0 ? 1 : 0));
            }
        }

        for (var i = 0; i < 16; i++) {
            var subset = subsets switch {
                2 => Partition2[partition * 16 + i],
                3 => Partition3[partition * 16 + i],
                _ => 0
            };

            int colourWeight, alphaWeight;
            if (indexBits2 > 0) {
                if (selection == 0) {
                    colourWeight = Weights(indexBits)[indices[i]];
                    alphaWeight = Weights(indexBits2)[indices2[i]];
                } else {
                    colourWeight = Weights(indexBits2)[indices2[i]];
                    alphaWeight = Weights(indexBits)[indices[i]];
                }
            } else {
                colourWeight = alphaWeight = Weights(indexBits)[indices[i]];
            }

            var e0 = subset * 2;
            var e1 = e0 + 1;
            var r = Interpolate(endpoints[e0, 0], endpoints[e1, 0], colourWeight);
            var g = Interpolate(endpoints[e0, 1], endpoints[e1, 1], colourWeight);
            var b = Interpolate(endpoints[e0, 2], endpoints[e1, 2], colourWeight);
            var a = Interpolate(endpoints[e0, 3], endpoints[e1, 3], alphaWeight);

            switch (rotation) {
                case 1: (a, r) = (r, a); break;
                case 2: (a, g) = (g, a); break;
                case 3: (a, b) = (b, a); break;
            }

            output[i * 4] = (byte) r;
            output[i * 4 + 1] = (byte) g;
            output[i * 4 + 2] = (byte) b;
            output[i * 4 + 3] = (byte) a;
        }
    }

    // Decodes a whole texture; row 0 of the result is row 0 of the data (the bottom of a Unity texture)
    public static byte[] Decode(byte[] data, int width, int height) {
        var output = new byte[width * height * 4];
        var blocksX = width / 4;
        var blocksY = height / 4;
        Parallel.For(0, blocksY, by => {
            var texels = new byte[64];
            for (var bx = 0; bx < blocksX; bx++) {
                DecodeBlock(data, (by * blocksX + bx) * 16, texels);
                for (var row = 0; row < 4; row++) {
                    Buffer.BlockCopy(texels, row * 16, output, ((by * 4 + row) * width + bx * 4) * 4, 16);
                }
            }
        });
        return output;
    }
}
