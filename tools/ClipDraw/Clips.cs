using AssetsTools.NET;

/// <summary>A clip of a tk2d animation library: its frames, how fast it plays and how it wraps.</summary>
internal sealed class Clip {
    private static readonly string[] WrapModes =
        ["Loop", "LoopSection", "Once", "PingPong", "RandomFrame", "RandomLoop", "Single"];

    public string Name;
    public float Fps;
    public int LoopStart, WrapMode;
    public readonly List<Frame> Frames = new();

    /// <summary>The clips of a library by their names; of two with one name, the first.</summary>
    public static Dictionary<string, Clip> ReadAll(Asset library) {
        var clips = new Dictionary<string, Clip>(StringComparer.Ordinal);
        foreach (var field in library.Field["clips"]["Array"].Children) {
            var clip = new Clip {
                Name = field["name"].AsString,
                Fps = field["fps"].AsFloat,
                LoopStart = field["loopStart"].AsInt,
                WrapMode = field["wrapMode"].AsInt
            };
            foreach (var frame in field["frames"]["Array"].Children) {
                var collection = Assets.Resolve(library.File, frame["spriteCollection"]);
                clip.Frames.Add(new Frame(
                    Sprite.Of(collection, frame["spriteId"].AsInt),
                    frame["triggerEvent"].AsBool,
                    frame["eventInfo"].AsString
                ));
            }

            clips.TryAdd(clip.Name, clip);
        }

        return clips;
    }

    public string Describe() {
        var fired = Frames
            .Select((frame, i) => frame.Trigger ? i + (frame.EventInfo is "" ? "" : $"({frame.EventInfo})") : null)
            .Where(text => text != null);
        return $"'{Name}': {Frames.Count} frames at {Fps:0.##}/s = {Frames.Count / Fps:0.###} s, " +
               $"{WrapModes[WrapMode]}{(WrapMode == 1 ? $" from {LoopStart}" : "")}, fires at " +
               $"[{string.Join(", ", fired)}], sprites {string.Join(" ", Frames.Select(f => f.Sprite.Name))}";
    }
}

/// <summary>A frame of a clip: the sprite it shows, and whether the clip fires its event as it comes to it.</summary>
internal sealed record Frame(Sprite Sprite, bool Trigger, string EventInfo);

/// <summary>A sprite of a tk2d collection: its mesh in units around the object's place, and its texture.</summary>
internal sealed class Sprite {
    private static readonly Dictionary<(string, long, int), Sprite> Known = new();

    public string Name;
    public float TexelSize;
    public readonly List<(float X, float Y)> Positions = new();
    public readonly List<(float U, float V)> Uvs = new();
    public readonly List<int> Indices = new();
    private Asset _collection;
    private AssetTypeValueField _definition;
    private Texture _texture;

    public Texture Texture => _texture ??= Texture.Of(Assets.Resolve(_collection.File, _definition["material"]));

    public static Sprite Of(Asset collection, int id) {
        var key = (collection.File.name, collection.Info.PathId, id);
        if (Known.TryGetValue(key, out var known)) {
            return known;
        }

        var definition = collection.Field["spriteDefinitions"]["Array"].Children[id];
        var sprite = new Sprite {
            Name = definition["name"].AsString,
            TexelSize = definition["texelSize"]["x"].AsFloat,
            _collection = collection,
            _definition = definition
        };
        foreach (var position in definition["positions"]["Array"].Children) {
            sprite.Positions.Add((position["x"].AsFloat, position["y"].AsFloat));
        }

        foreach (var uv in definition["uvs"]["Array"].Children) {
            sprite.Uvs.Add((uv["x"].AsFloat, uv["y"].AsFloat));
        }

        foreach (var index in definition["indices"]["Array"].Children) {
            sprite.Indices.Add(index.AsInt);
        }

        Known[key] = sprite;
        return sprite;
    }
}

/// <summary>The decoded texture that a material draws with.</summary>
internal sealed class Texture {
    private static readonly Dictionary<(string, long), Texture> Known = new();

    public int Width, Height;
    public byte[] Texels; // RGBA, row 0 = bottom

    public static Texture Of(Asset material) {
        var main = material.Field["m_SavedProperties"]["m_TexEnvs"]["Array"].Children
            .First(pair => pair["first"].AsString == "_MainTex");
        var asset = Assets.Resolve(material.File, main["second"]["m_Texture"]);
        var key = (asset.File.name, asset.Info.PathId);
        if (Known.TryGetValue(key, out var known)) {
            return known;
        }

        var field = asset.Field;
        var texture = new Texture { Width = field["m_Width"].AsInt, Height = field["m_Height"].AsInt };
        var stream = field["m_StreamData"];
        var streamPath = stream["path"].AsString;
        var data = streamPath is ""
            ? field["image data"].AsByteArray
            : Assets.ReadResource(
                streamPath[(streamPath.LastIndexOf('/') + 1)..],
                (long) stream["offset"].AsULong,
                (int) stream["size"].AsUInt
            );
        var format = field["m_TextureFormat"].AsInt;
        Console.WriteLine($"Texture '{field["m_Name"].AsString}' {texture.Width}x{texture.Height}, format {format}");
        texture.Texels = format switch {
            25 => Bc7.Decode(data, texture.Width, texture.Height),
            4 => data[..(texture.Width * texture.Height * 4)],
            _ => throw new InvalidDataException($"Texture format {format} is not read")
        };
        Known[key] = texture;
        return texture;
    }
}

/// <summary>Draws sprites as the game does, one texel to a pixel.</summary>
internal static class Draw {
    /// <summary>
    /// Each sprite drawn as a PNG, all on canvases of one size around one place, so that the frames keep where they
    /// are to each other as they follow one another.
    /// </summary>
    public static Dictionary<Sprite, byte[]> Pngs(IReadOnlyCollection<Sprite> sprites, out int width, out int height) {
        var texel = sprites.Min(sprite => sprite.TexelSize);
        var points = sprites.SelectMany(sprite => sprite.Positions).ToList();
        var minX = points.Min(p => p.X) - 2 * texel;
        var maxY = points.Max(p => p.Y) + 2 * texel;
        var w = width = (int) Math.Ceiling((points.Max(p => p.X) + 2 * texel - minX) / texel);
        var h = height = (int) Math.Ceiling((maxY - points.Min(p => p.Y) + 2 * texel) / texel);
        return sprites.ToDictionary(
            sprite => sprite,
            sprite => Png.Encode(Render(sprite, minX, maxY, texel, w, h), w, h)
        );
    }

    private static byte[] Render(Sprite sprite, float minX, float maxY, float texel, int width, int height) {
        var canvas = new byte[width * height * 4];
        var texture = sprite.Texture;
        for (var t = 0; t + 2 < sprite.Indices.Count; t += 3) {
            var (a, b, c) = (sprite.Indices[t], sprite.Indices[t + 1], sprite.Indices[t + 2]);
            var (ax, ay) = Place(sprite.Positions[a]);
            var (bx, by) = Place(sprite.Positions[b]);
            var (cx, cy) = Place(sprite.Positions[c]);
            var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            var x0 = Math.Max(0, (int) Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            var x1 = Math.Min(width - 1, (int) Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            var y0 = Math.Max(0, (int) Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            var y1 = Math.Min(height - 1, (int) Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            for (var y = y0; y <= y1; y++) {
                for (var x = x0; x <= x1; x++) {
                    var (px, py) = (x + 0.5f, y + 0.5f);
                    var wa = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                    var wb = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                    var wc = 1 - wa - wb;
                    if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) {
                        continue;
                    }

                    var u = wa * sprite.Uvs[a].U + wb * sprite.Uvs[b].U + wc * sprite.Uvs[c].U;
                    var v = wa * sprite.Uvs[a].V + wb * sprite.Uvs[b].V + wc * sprite.Uvs[c].V;
                    var tx = Math.Clamp((int) (u * texture.Width), 0, texture.Width - 1);
                    var ty = Math.Clamp((int) (v * texture.Height), 0, texture.Height - 1);
                    Buffer.BlockCopy(texture.Texels, (ty * texture.Width + tx) * 4, canvas, (y * width + x) * 4, 4);
                }
            }
        }

        return canvas;

        (float X, float Y) Place((float X, float Y) p) => ((p.X - minX) / texel, (maxY - p.Y) / texel);
    }
}
