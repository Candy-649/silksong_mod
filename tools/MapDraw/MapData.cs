using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// The map prefab: every drawn piece with its place, sprite, colour and GameMapScene settings
internal sealed class MapData {
    public readonly List<MapPiece> Pieces = new();
    public readonly Dictionary<long, string> Names = new();
    public readonly Dictionary<long, long> SceneObjectOf = new(); // GameMapScene script -> its game object
    public int AtlasFileId = -1;

    private sealed class TransformNote {
        public long GameObject, Father;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public Matrix4x4? World;
    }

    public static MapData Load(AssetsManager manager, string path, string atlasCab) {
        var map = new MapData();
        var bundle = manager.LoadBundleFile(path, true);
        var instance = manager.LoadAssetsFileFromBundle(bundle, 0, false);
        var externals = instance.file.Metadata.Externals;
        for (var i = 0; i < externals.Count; i++) {
            if (externals[i].PathName.Contains(atlasCab, StringComparison.OrdinalIgnoreCase)) {
                map.AtlasFileId = i + 1;
            }
        }

        var active = new Dictionary<long, bool>();
        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.GameObject)) {
            var f = manager.GetBaseField(instance, info);
            map.Names[info.PathId] = f["m_Name"].AsString;
            active[info.PathId] = f["m_IsActive"].AsBool;
        }

        var transforms = new Dictionary<long, TransformNote>();
        var transformOf = new Dictionary<long, long>();
        foreach (var info in instance.file.AssetInfos.Where(i =>
                     i.TypeId == (int) AssetClassID.Transform || i.TypeId == (int) AssetClassID.RectTransform)) {
            var f = manager.GetBaseField(instance, info);
            var p = f["m_LocalPosition"];
            var s = f["m_LocalScale"];
            var r = f["m_LocalRotation"];
            var note = new TransformNote {
                GameObject = f["m_GameObject"]["m_PathID"].AsLong,
                Father = f["m_Father"]["m_PathID"].AsLong,
                Position = new Vector3(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat),
                Scale = new Vector3(s["x"].AsFloat, s["y"].AsFloat, s["z"].AsFloat),
                Rotation = new Quaternion(r["x"].AsFloat, r["y"].AsFloat, r["z"].AsFloat, r["w"].AsFloat),
            };
            transforms[info.PathId] = note;
            transformOf[note.GameObject] = info.PathId;
        }

        Matrix4x4 World(long id) {
            var t = transforms[id];
            if (t.World != null) return t.World.Value;
            var local = Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) *
                        Matrix4x4.CreateTranslation(t.Position);
            var world = t.Father != 0 && transforms.ContainsKey(t.Father) ? local * World(t.Father) : local;
            t.World = world;
            return world;
        }

        bool ActiveInTree(long gameObject) {
            while (true) {
                if (!active.GetValueOrDefault(gameObject, true)) return false;
                if (!transformOf.TryGetValue(gameObject, out var t)) return true;
                var father = transforms[t].Father;
                if (father == 0 || !transforms.ContainsKey(father)) return true;
                gameObject = transforms[father].GameObject;
            }
        }

        string PathOf(long gameObject) {
            var parts = new List<string>();
            while (true) {
                parts.Add(map.Names.GetValueOrDefault(gameObject, "?"));
                if (!transformOf.TryGetValue(gameObject, out var t)) break;
                var father = transforms[t].Father;
                if (father == 0 || !transforms.ContainsKey(father)) break;
                gameObject = transforms[father].GameObject;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        // GameMapScene settings by game object
        var sceneNotes = new Dictionary<long, AssetTypeValueField>();
        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.MonoBehaviour)) {
            var f = manager.GetBaseField(instance, info);
            if (f["fullSprite"].IsDummy || f["initialState"].IsDummy) continue;
            sceneNotes[f["m_GameObject"]["m_PathID"].AsLong] = f;
            map.SceneObjectOf[info.PathId] = f["m_GameObject"]["m_PathID"].AsLong;
        }

        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.SpriteRenderer)) {
            var f = manager.GetBaseField(instance, info);
            var gameObject = f["m_GameObject"]["m_PathID"].AsLong;
            var c = f["m_Color"];
            var piece = new MapPiece {
                Name = map.Names.GetValueOrDefault(gameObject, "?"),
                Path = PathOf(gameObject),
                GameObject = gameObject,
                World = transformOf.TryGetValue(gameObject, out var t) ? World(t) : Matrix4x4.Identity,
                SpriteFileId = f["m_Sprite"]["m_FileID"].AsInt,
                SpritePathId = f["m_Sprite"]["m_PathID"].AsLong,
                Color = new Vector4(c["r"].AsFloat, c["g"].AsFloat, c["b"].AsFloat, c["a"].AsFloat),
                FlipX = f["m_FlipX"].AsBool,
                FlipY = f["m_FlipY"].AsBool,
                Enabled = f["m_Enabled"].AsBool,
                Active = ActiveInTree(gameObject),
                SortingLayer = f["m_SortingLayer"].AsInt,
                SortingOrder = f["m_SortingOrder"].AsInt,
            };
            if (sceneNotes.TryGetValue(gameObject, out var scene)) {
                piece.IsScene = true;
                piece.InitialState = scene["initialState"].AsInt;
                piece.FullSpriteFileId = scene["fullSprite"]["m_FileID"].AsInt;
                piece.FullSpritePathId = scene["fullSprite"]["m_PathID"].AsLong;
                var also = scene["mappedIfAllMapped"];
                if (!also.IsDummy) {
                    foreach (var other in also["Array"].Children) {
                        piece.MappedIfAllMapped.Add(other["m_PathID"].AsLong);
                    }
                }

                piece.MappedParent = scene["mappedParent"]["m_PathID"].AsLong;
                piece.HideTests = CountTests(scene["hideCondition"]);
                piece.AltSprites = scene["altFullSprites"]["Array"].Children.Count;
                piece.AltColors = scene["altColors"]["Array"].Children.Count;
                piece.HideCondition = scene["hideCondition"];

                piece.SceneFields = string.Join(", ", scene.Children.Skip(4).Select(Describe));
            }

            map.Pieces.Add(piece);
        }

        return map;
    }

    private static int CountTests(AssetTypeValueField test) {
        if (test.IsDummy || test["TestGroups"].IsDummy) return 0;
        return test["TestGroups"]["Array"].Children.Sum(g => g["Tests"].IsDummy ? 1 : g["Tests"]["Array"].Children.Count);
    }

    public static string Dump(AssetTypeValueField f, int depth = 0) {
        if (f.IsDummy) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append(new string(' ', depth * 2)).Append(f.FieldName).Append(" : ").Append(f.TypeName);
        if (f.Value != null && f.Value.ValueType != AssetValueType.Array && f.Value.ValueType != AssetValueType.ByteArray) {
            sb.Append(" = ").Append(f.Value.AsObject);
        }

        sb.AppendLine();
        foreach (var c in f.Children) sb.Append(Dump(c, depth + 1));
        return sb.ToString();
    }

    private static string Describe(AssetTypeValueField f) {
        if (f.Value != null && f.Value.ValueType != AssetValueType.Array && f.Value.ValueType != AssetValueType.ByteArray) {
            return $"{f.FieldName}={f.Value.AsObject}";
        }

        if (f.TypeName.StartsWith("PPtr")) return $"{f.FieldName}=({f["m_FileID"].AsInt},{f["m_PathID"].AsLong})";
        if (f["Array"].IsDummy) return $"{f.FieldName}:{f.TypeName}";
        return $"{f.FieldName}[{f["Array"].Children.Count}]";
    }
}

internal sealed class MapPiece {
    public string Name, Path, SceneFields;
    public long GameObject;
    public Matrix4x4 World;
    public int SpriteFileId, FullSpriteFileId, InitialState, SortingLayer, SortingOrder;
    public long SpritePathId, FullSpritePathId;
    public Vector4 Color;
    public bool FlipX, FlipY, Enabled, Active, IsScene;
    public readonly List<long> MappedIfAllMapped = new(); // GameMapScene scripts
    public long MappedParent; // GameMapScene script, 0 for none
    public int HideTests, AltSprites, AltColors;
    public AssetTypeValueField HideCondition;

    public Vector2 Place => new(World.M41, World.M42);
    public float Z => World.M43;
}
