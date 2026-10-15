using System.Numerics;

// A picture of part of the map: world x0..x1, y0..y1 at a number of pixels per map unit
internal sealed class Canvas {
    public readonly int Width, Height;
    public readonly float X0, YTop, Scale;
    private readonly float[] _rgba; // premultiplied, top row first

    public Canvas(float x0, float y0, float x1, float y1, float scale) {
        X0 = x0;
        YTop = y1;
        Scale = scale;
        Width = (int) Math.Ceiling((x1 - x0) * scale);
        Height = (int) Math.Ceiling((y1 - y0) * scale);
        _rgba = new float[Width * Height * 4];
    }

    public Vector2 ToPixel(Vector2 world) => new((world.X - X0) * Scale, (YTop - world.Y) * Scale);

    private Vector2 ToWorld(float px, float py) => new(X0 + px / Scale, YTop - py / Scale);

    public void DrawSprite(Atlas atlas, SpriteInfo sprite, Matrix4x4 world, bool flipX, bool flipY, Vector4 tint,
        int samples = 3) {
        float fx = flipX ? -1 : 1, fy = flipY ? -1 : 1;
        float a = world.M11, b = world.M12, c = world.M21, d = world.M22, tx = world.M41, ty = world.M42;
        var det = a * d - b * c;
        if (Math.Abs(det) < 1e-9) return;

        // Where the mesh lands on the canvas
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (px, py) in sprite.Positions) {
            var lx = px * fx;
            var ly = py * fy;
            var p = ToPixel(new Vector2(lx * a + ly * c + tx, lx * b + ly * d + ty));
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        var x0 = Math.Max(0, (int) Math.Floor(minX) - 1);
        var x1 = Math.Min(Width - 1, (int) Math.Ceiling(maxX) + 1);
        var y0 = Math.Max(0, (int) Math.Floor(minY) - 1);
        var y1 = Math.Min(Height - 1, (int) Math.Ceiling(maxY) + 1);
        if (x0 > x1 || y0 > y1) return;

        var triangles = new List<(Vector2 A, Vector2 B, Vector2 C)>();
        for (var i = 0; i + 2 < sprite.Indices.Count; i += 3) {
            var p0 = sprite.Positions[sprite.Indices[i]];
            var p1 = sprite.Positions[sprite.Indices[i + 1]];
            var p2 = sprite.Positions[sprite.Indices[i + 2]];
            triangles.Add((new Vector2(p0.X, p0.Y), new Vector2(p1.X, p1.Y), new Vector2(p2.X, p2.Y)));
        }

        var n = samples * samples;
        Parallel.For(y0, y1 + 1, y => {
            for (var x = x0; x <= x1; x++) {
                float r = 0, g = 0, bl = 0, al = 0;
                for (var sy = 0; sy < samples; sy++) {
                    for (var sx = 0; sx < samples; sx++) {
                        var w = ToWorld(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples);
                        var wx = w.X - tx;
                        var wy = w.Y - ty;
                        var local = new Vector2((d * wx - c * wy) / det * fx, (-b * wx + a * wy) / det * fy);
                        if (!Inside(triangles, local)) continue;
                        var at = sprite.AtlasPoint(local.X, local.Y);
                        var s = atlas.Sample(at.X, at.Y);
                        r += s.R * tint.X;
                        g += s.G * tint.Y;
                        bl += s.B * tint.Z;
                        al += s.A;
                    }
                }

                if (al <= 0) continue;
                var alpha = al / n * tint.W;
                var o = (y * Width + x) * 4;
                var keep = 1 - alpha;
                _rgba[o] = r / n * tint.W + _rgba[o] * keep;
                _rgba[o + 1] = g / n * tint.W + _rgba[o + 1] * keep;
                _rgba[o + 2] = bl / n * tint.W + _rgba[o + 2] * keep;
                _rgba[o + 3] = alpha + _rgba[o + 3] * keep;
            }
        });
    }

    private static bool Inside(List<(Vector2 A, Vector2 B, Vector2 C)> triangles, Vector2 p) {
        foreach (var (a, b, c) in triangles) {
            var d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            var d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
            var d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
            const float e = 1e-7f;
            var neg = d1 < -e || d2 < -e || d3 < -e;
            var pos = d1 > e || d2 > e || d3 > e;
            if (!(neg && pos)) return true;
        }

        return false;
    }

    // Over a flat background, rows top first
    public byte[] ToBytes(Vector3 background) {
        var bytes = new byte[Width * Height * 4];
        for (var i = 0; i < Width * Height; i++) {
            var o = i * 4;
            var keep = 1 - _rgba[o + 3];
            bytes[o] = ToByte(_rgba[o] + background.X * keep);
            bytes[o + 1] = ToByte(_rgba[o + 1] + background.Y * keep);
            bytes[o + 2] = ToByte(_rgba[o + 2] + background.Z * keep);
            bytes[o + 3] = 255;
        }

        return bytes;
    }

    private static byte ToByte(float v) => (byte) Math.Clamp((int) Math.Round(v * 255), 0, 255);
}
