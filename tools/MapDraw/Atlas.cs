using AssetsTools.NET;
using AssetsTools.NET.Extra;

// The map's sprite atlas: its texture, decoded, and where each packed sprite's pixels are in it
internal sealed class Atlas {
    public int Width, Height;
    public byte[] Texels; // RGBA, row 0 = bottom
    public readonly Dictionary<long, SpriteInfo> Sprites = new();
    public string CabName;

    public static Atlas Load(AssetsManager manager, string path) {
        var atlas = new Atlas();
        var bundle = manager.LoadBundleFile(path, true);
        var instance = manager.LoadAssetsFileFromBundle(bundle, 0, false);
        atlas.CabName = instance.name;

        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.Texture2D)) {
            var f = manager.GetBaseField(instance, info);
            atlas.Width = f["m_Width"].AsInt;
            atlas.Height = f["m_Height"].AsInt;
            var format = f["m_TextureFormat"].AsInt;
            if (format != 25) throw new InvalidDataException($"texture format {format}, not BC7");
            var stream = f["m_StreamData"];
            var streamPath = stream["path"].AsString;
            var resName = streamPath[(streamPath.LastIndexOf('/') + 1)..];
            var dir = bundle.file.BlockAndDirInfo.DirectoryInfos.First(d => d.Name == resName);
            var reader = bundle.file.DataReader;
            reader.Position = dir.Offset + (long) stream["offset"].AsULong;
            var data = reader.ReadBytes((int) stream["size"].AsUInt);
            atlas.Texels = Bc7.Decode(data, atlas.Width, atlas.Height);
        }

        var renderData = new Dictionary<(uint, uint, uint, uint, long), AssetTypeValueField>();
        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.SpriteAtlas)) {
            var f = manager.GetBaseField(instance, info);
            foreach (var pair in f["m_RenderDataMap"]["Array"].Children) {
                renderData[Key(pair["first"])] = pair["second"];
            }
        }

        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.Sprite)) {
            var f = manager.GetBaseField(instance, info);
            if (!renderData.TryGetValue(Key(f["m_RenderDataKey"]), out var data)) {
                Console.WriteLine($"no atlas data for sprite {f["m_Name"].AsString}");
                continue;
            }

            var sprite = new SpriteInfo {
                Name = f["m_Name"].AsString,
                W = (int) Math.Round(f["m_Rect"]["width"].AsFloat),
                H = (int) Math.Round(f["m_Rect"]["height"].AsFloat),
                Ppu = f["m_PixelsToUnits"].AsFloat,
                PivotX = f["m_Pivot"]["x"].AsFloat,
                PivotY = f["m_Pivot"]["y"].AsFloat,
                AtlasX = data["atlasRectOffset"]["x"].AsFloat,
                AtlasY = data["atlasRectOffset"]["y"].AsFloat,
                Settings = data["settingsRaw"].AsUInt,
                RectX = data["textureRect"]["x"].AsFloat, RectY = data["textureRect"]["y"].AsFloat,
                RectW = data["textureRect"]["width"].AsFloat, RectH = data["textureRect"]["height"].AsFloat,
                OffsetX = data["textureRectOffset"]["x"].AsFloat, OffsetY = data["textureRectOffset"]["y"].AsFloat,
            };
            ReadMesh(f["m_RD"], sprite);
            atlas.Sprites[info.PathId] = sprite;
        }

        return atlas;
    }

    private static (uint, uint, uint, uint, long) Key(AssetTypeValueField key) {
        var guid = key["first"];
        return (guid["data[0]"].AsUInt, guid["data[1]"].AsUInt, guid["data[2]"].AsUInt, guid["data[3]"].AsUInt,
            key["second"].AsLong);
    }

    private static void ReadMesh(AssetTypeValueField rd, SpriteInfo sprite) {
        var vertexData = rd["m_VertexData"];
        var count = (int) vertexData["m_VertexCount"].AsUInt;
        var bytes = vertexData["m_DataSize"].AsByteArray;
        var channels = vertexData["m_Channels"]["Array"].Children
            .Select(c => (Stream: c["stream"].AsInt, Offset: c["offset"].AsInt, Format: c["format"].AsInt,
                Dimension: c["dimension"].AsInt & 0xF))
            .ToList();

        // Streams follow each other, each starting on 16 bytes
        var used = channels.Where(c => c.Dimension > 0).ToList();
        var streamCount = used.Select(c => c.Stream).DefaultIfEmpty(0).Max() + 1;
        var strides = new int[streamCount];
        foreach (var c in used) {
            strides[c.Stream] = Math.Max(strides[c.Stream], c.Offset + c.Dimension * FormatSize(c.Format));
        }

        var starts = new int[streamCount];
        var at = 0;
        for (var s = 0; s < streamCount; s++) {
            starts[s] = at;
            at += strides[s] * count;
            at = (at + 15) & ~15;
        }

        var position = channels[0];
        if (position.Format != 0 || position.Dimension < 2) throw new InvalidDataException("position channel not float");
        for (var i = 0; i < count; i++) {
            var o = starts[position.Stream] + i * strides[position.Stream] + position.Offset;
            sprite.Positions.Add((BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4)));
        }

        var indexBytes = rd["m_IndexBuffer"]["Array"].AsByteArray;
        for (var i = 0; i + 1 < indexBytes.Length; i += 2) {
            sprite.Indices.Add(BitConverter.ToUInt16(indexBytes, i));
        }
    }

    private static int FormatSize(int format) => format switch {
        0 => 4, 1 => 2, 2 => 1, 3 => 1, 4 => 2, 5 => 2, 6 => 1, 7 => 1, 8 => 2, 9 => 2, 10 => 4, 11 => 4, _ => 4
    };

    // Bilinear sample of the atlas at texel coordinates (texel centres at +0.5); premultiplied RGBA 0..1
    public (float R, float G, float B, float A) Sample(float x, float y) {
        x -= 0.5f;
        y -= 0.5f;
        var x0 = (int) Math.Floor(x);
        var y0 = (int) Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        float r = 0, g = 0, b = 0, a = 0;
        for (var dy = 0; dy < 2; dy++) {
            for (var dx = 0; dx < 2; dx++) {
                var tx = Math.Clamp(x0 + dx, 0, Width - 1);
                var ty = Math.Clamp(y0 + dy, 0, Height - 1);
                var w = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                var o = (ty * Width + tx) * 4;
                var al = Texels[o + 3] / 255f;
                r += w * Texels[o] / 255f * al;
                g += w * Texels[o + 1] / 255f * al;
                b += w * Texels[o + 2] / 255f * al;
                a += w * al;
            }
        }

        return (r, g, b, a);
    }
}

internal sealed class SpriteInfo {
    public string Name;
    public int W, H;
    public float Ppu, PivotX, PivotY, AtlasX, AtlasY, RectX, RectY, RectW, RectH, OffsetX, OffsetY;
    public uint Settings;
    public readonly List<(float X, float Y)> Positions = new(); // local units, around the pivot
    public readonly List<int> Indices = new();

    public int Rotation => (int) ((Settings >> 2) & 0xF);

    // Atlas texel coordinates of a local position. The packed rectangle sits at the same place whatever the packing
    // turn; only the picture inside it is mirrored.
    public (float X, float Y) AtlasPoint(float lx, float ly) {
        var tx = lx * Ppu + PivotX * W - OffsetX;
        var ty = ly * Ppu + PivotY * H - OffsetY;
        return Rotation switch {
            1 => (RectX + RectW - tx, RectY + ty),
            2 => (RectX + tx, RectY + RectH - ty),
            3 => (RectX + RectW - tx, RectY + RectH - ty),
            _ => (RectX + tx, RectY + ty),
        };
    }
}
