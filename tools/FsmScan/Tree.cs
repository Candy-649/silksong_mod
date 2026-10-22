using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// Prints the objects of a prefab or scene as a tree for --tree: each object with whether it starts switched off, its
/// physics layer and depth, where it is in its parent and how it is turned there if not at the parent's place, and
/// every component on it (built-in ones by class, scripts by script class), with whether
/// colliders are triggers, which renderers start switched off and whether particle systems play by themselves, and
/// the size of the sprite that each sprite renderer draws. Used to see what a character is made of, such as which parts
/// of the hero only show while it does something, and how large a thing is drawn, such as a bench.
/// </summary>
internal static class TreeDumper {
    /// <summary>
    /// The folder of the bundles, which the bundles of sprites are found in (<see cref="SpriteSizes"/>).
    /// </summary>
    private static string _bundleDir;

    /// <summary>
    /// Whether each object is printed with its path ID, for --ids, by which an action names an object of its own
    /// prefab or scene, like the points a spawn is made at.
    /// </summary>
    public static bool PrintIds;

    public static void Run(string bundleDir, string filter, string rootPattern, int? tag = null) {
        _bundleDir = bundleDir;
        var rootRegex = new Regex(rootPattern ?? ".", RegexOptions.IgnoreCase);
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();

        var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Where(path => filter == null || path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var path in paths) {
            try {
                PrintBundle(manager, path, bundleDir, scriptNames, rootRegex, tag);
            } catch (Exception e) {
                Console.WriteLine($"error in {Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }
        }
    }

    private sealed class Node {
        public string Name;
        public bool Active;
        public int Layer;
        public int Tag;
        public float Z;
        public string Scale;
        public string Place;
        public string Turn;
        public readonly List<string> Components = new();
        public readonly List<long> Children = new();
        public long Parent;
    }

    private static void PrintBundle(
        AssetsManager manager,
        string path,
        string bundleDir,
        Dictionary<string, string> scriptNames,
        Regex rootRegex,
        int? tag
    ) {
        var bundle = manager.LoadBundleFile(path, true);
        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
            if (!bundle.file.IsAssetsFile(i)) {
                continue;
            }

            var instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
            instance.file.GenerateQuickLookup();
            var context = new FileContext(manager, instance, scriptNames);

            // The transform of each object, and the object of each transform, to walk the tree by
            var nodes = new Dictionary<long, Node>();
            var objectOfTransform = new Dictionary<long, long>();
            var fatherOfTransform = new Dictionary<long, long>();
            foreach (var info in instance.file.GetAssetsOfType(AssetClassID.GameObject)) {
                var field = manager.GetBaseField(instance, info);
                var node = new Node {
                    Name = field["m_Name"].AsString,
                    Active = field.Get("m_IsActive")?.AsBool ?? true,
                    Layer = field.Get("m_Layer")?.AsInt ?? 0,
                    Tag = field.Get("m_Tag")?.AsInt ?? 0
                };
                nodes[info.PathId] = node;

                var components = field.Get("m_Component", "Array");
                if (components == null) {
                    continue;
                }

                foreach (var pair in components.Children) {
                    var pointer = pair.Get("component");
                    if (pointer == null || pointer["m_FileID"].AsInt != 0) {
                        continue;
                    }

                    var componentInfo = instance.file.GetAssetInfo(pointer["m_PathID"].AsLong);
                    if (componentInfo == null) {
                        continue;
                    }

                    var type = (AssetClassID) componentInfo.TypeId;
                    if (type is AssetClassID.Transform or AssetClassID.RectTransform) {
                        var transform = manager.GetBaseField(instance, componentInfo);
                        objectOfTransform[componentInfo.PathId] = info.PathId;
                        fatherOfTransform[componentInfo.PathId] = transform["m_Father"]["m_PathID"].AsLong;
                        node.Z = transform.Get("m_LocalPosition")?["z"].AsFloat ?? 0f;
                        if (transform.Get("m_LocalPosition") is { IsDummy: false } place &&
                            (place["x"].AsFloat != 0f || place["y"].AsFloat != 0f)) {
                            node.Place = $"{place["x"].AsFloat:0.###},{place["y"].AsFloat:0.###}";
                        }

                        if (transform.Get("m_LocalRotation") is { IsDummy: false } turn && turn["z"].AsFloat != 0f) {
                            // The turn about the depth axis of a turn that is only about it
                            var angle = 2 * Math.Atan2(turn["z"].AsFloat, turn["w"].AsFloat) * 180 / Math.PI;
                            node.Turn = $"{angle:0.##}";
                        }

                        if (transform.Get("m_LocalScale") is { IsDummy: false } scale &&
                            (scale["x"].AsFloat != 1f || scale["y"].AsFloat != 1f)) {
                            node.Scale = $"{scale["x"].AsFloat:0.###},{scale["y"].AsFloat:0.###}";
                        }
                        continue;
                    }

                    node.Components.Add(Describe(manager, instance, context, componentInfo, type));
                }
            }

            foreach (var (transformId, objectId) in objectOfTransform) {
                var fatherId = fatherOfTransform[transformId];
                if (fatherId != 0 && objectOfTransform.TryGetValue(fatherId, out var parentId)) {
                    nodes[objectId].Parent = parentId;
                    nodes[parentId].Children.Add(objectId);
                }
            }

            // With a tag, every object that carries it, wherever it is, under the path from the top of the room
            if (tag != null) {
                var printedBundle = false;
                foreach (var (id, node) in nodes.OrderBy(pair => pair.Value.Name, StringComparer.Ordinal)) {
                    if (node.Tag != tag) {
                        continue;
                    }

                    if (!printedBundle) {
                        printedBundle = true;
                        Console.WriteLine($"== {Path.GetRelativePath(bundleDir, path)}");
                    }

                    var names = new List<string>();
                    for (var current = node.Parent; current != 0 && nodes.TryGetValue(current, out var parent);
                         current = parent.Parent) {
                        names.Insert(0, parent.Name);
                    }

                    Console.WriteLine($"-- {string.Join("/", names)}");
                    Print(nodes, id, 1);
                }

                continue;
            }

            foreach (var (id, node) in nodes.OrderBy(pair => pair.Value.Name, StringComparer.Ordinal)) {
                if (node.Parent == 0 && rootRegex.IsMatch(node.Name)) {
                    Console.WriteLine($"== {Path.GetRelativePath(bundleDir, path)}");
                    Print(nodes, id, 0);
                }
            }
        }
    }

    private static string Describe(
        AssetsManager manager,
        AssetsFileInstance instance,
        FileContext context,
        AssetFileInfo info,
        AssetClassID type
    ) {
        if (type == AssetClassID.MonoBehaviour) {
            var field = manager.GetBaseField(instance, info);
            var enabled = field.Get("m_Enabled")?.AsBool ?? true;
            return (context.ScriptClass(info) ?? "?script") + (enabled ? "" : "(off)");
        }

        switch (type) {
            case AssetClassID.BoxCollider2D:
            case AssetClassID.CircleCollider2D:
            case AssetClassID.PolygonCollider2D:
            case AssetClassID.EdgeCollider2D:
            case AssetClassID.CapsuleCollider2D: {
                var field = manager.GetBaseField(instance, info);
                var parts = new List<string>();
                if (field.Get("m_IsTrigger")?.AsBool == true) {
                    parts.Add("trigger");
                }

                if (field.Get("m_Enabled")?.AsBool == false) {
                    parts.Add("off");
                }

                if (type is AssetClassID.BoxCollider2D or AssetClassID.CapsuleCollider2D) {
                    var offset = field.Get("m_Offset");
                    var size = field.Get("m_Size");
                    parts.Add(
                        $"offset {offset["x"].AsFloat:0.###},{offset["y"].AsFloat:0.###} " +
                        $"size {size["x"].AsFloat:0.###}x{size["y"].AsFloat:0.###}"
                    );
                } else if (type == AssetClassID.CircleCollider2D) {
                    var offset = field.Get("m_Offset");
                    parts.Add(
                        $"offset {offset["x"].AsFloat:0.###},{offset["y"].AsFloat:0.###} " +
                        $"radius {field.Get("m_Radius")?.AsFloat ?? 0f:0.###}"
                    );
                }

                return parts.Count == 0 ? type.ToString() : $"{type}({string.Join(",", parts)})";
            }
            case AssetClassID.MeshRenderer:
            case AssetClassID.SpriteRenderer:
            case AssetClassID.LineRenderer:
            case AssetClassID.TrailRenderer:
            case AssetClassID.ParticleSystemRenderer:
            case AssetClassID.SortingGroup: {
                var field = manager.GetBaseField(instance, info);
                var parts = new List<string>();
                if (field.Get("m_Enabled")?.AsBool == false) {
                    parts.Add("off");
                }

                // What it is drawn in front of: the sorting layer's ID and the order within it
                var sortingLayer = field.Get("m_SortingLayerID")?.AsInt ?? 0;
                var sortingOrder = field.Get("m_SortingOrder")?.AsInt ?? 0;
                if (sortingLayer != 0 || sortingOrder != 0) {
                    parts.Add($"sorting {sortingLayer}:{sortingOrder}");
                }

                // How large the sprite is drawn, in units before the scale of the object and its parents: the size
                // of the sprite itself, or the size set on the renderer when the sprite is stretched or tiled to it
                if (type == AssetClassID.SpriteRenderer) {
                    if (field.Get("m_Sprite") is { } sprite) {
                        parts.Add(SpriteSizes.Describe(manager, instance, context, sprite));
                    }

                    if ((field.Get("m_DrawMode")?.AsInt ?? 0) != 0 && field.Get("m_Size") is { } size) {
                        parts.Add($"drawn {size["x"].AsFloat:0.##}x{size["y"].AsFloat:0.##}");
                    }
                }

                return parts.Count == 0 ? type.ToString() : $"{type}({string.Join(",", parts)})";
            }
            case AssetClassID.Rigidbody2D: {
                var field = manager.GetBaseField(instance, info);
                var parts = new List<string> {
                    (field.Get("m_BodyType")?.AsInt ?? 0) switch {
                        0 => "dynamic",
                        1 => "kinematic",
                        _ => "static"
                    }
                };
                if (field.Get("m_Simulated")?.AsBool == false) {
                    parts.Add("not simulated");
                }

                if (field.Get("m_GravityScale") is { IsDummy: false } gravity && gravity.AsFloat != 1f) {
                    parts.Add($"gravity {gravity.AsFloat:0.###}");
                }

                // Whether a fast body is stopped where it first touches something, rather than where its step ends
                if (field.Get("m_CollisionDetection")?.AsInt == 1) {
                    parts.Add("continuous");
                }

                // Whether it is drawn between the steps of physics, rather than where the last step left it
                switch (field.Get("m_Interpolate")?.AsInt) {
                    case 1:
                        parts.Add("interpolate");
                        break;
                    case 2:
                        parts.Add("extrapolate");
                        break;
                }

                return $"{type}({string.Join(",", parts)})";
            }
            case AssetClassID.ParticleSystem: {
                var field = manager.GetBaseField(instance, info);
                var parts = new List<string>();
                if (field.Get("playOnAwake")?.AsBool == true) {
                    parts.Add("plays");
                }

                if (field.Get("looping")?.AsBool == true) {
                    parts.Add("loops");
                }

                return parts.Count == 0 ? type.ToString() : $"{type}({string.Join(",", parts)})";
            }
            default:
                return type.ToString();
        }
    }

    /// <summary>
    /// The size of the sprites that sprite renderers draw, in units. A scene keeps only a reference to each sprite: the
    /// assets file that holds it, which is in the bundle of its sprite atlas, and its path ID. The bundles are searched
    /// for their assets files once, and each assets file of sprites is loaded once, with a manager of its own that the
    /// unloading of each scene's bundle leaves alone.
    /// </summary>
    private static class SpriteSizes {
        private static readonly AssetsManager Manager = new();
        private static readonly Dictionary<string, AssetsFileInstance> Files = new(StringComparer.Ordinal);
        private static Dictionary<string, string> _bundleOfFile;

        public static string Describe(
            AssetsManager sceneManager,
            AssetsFileInstance instance,
            FileContext context,
            AssetTypeValueField pointer
        ) {
            var pathId = pointer["m_PathID"].AsLong;
            if (pathId == 0) {
                return "no sprite";
            }

            var fileId = pointer["m_FileID"].AsInt;
            try {
                var file = fileId == 0 ? instance : FindFile(context.FileNameOf(fileId));
                var info = file?.file.GetAssetInfo(pathId);
                if (info == null) {
                    return "sprite not found";
                }

                var sprite = (fileId == 0 ? sceneManager : Manager).GetBaseField(file, info);
                var rect = sprite["m_Rect"];
                var unitsPerPixel = 1f / sprite["m_PixelsToUnits"].AsFloat;
                var width = rect["width"].AsFloat * unitsPerPixel;
                var height = rect["height"].AsFloat * unitsPerPixel;
                return $"sprite {width:0.##}x{height:0.##}";
            } catch (Exception e) {
                return $"sprite unreadable ({e.GetType().Name})";
            }
        }

        private static AssetsFileInstance FindFile(string name) {
            if (name == null) {
                return null;
            }

            if (Files.TryGetValue(name, out var known)) {
                return known;
            }

            _bundleOfFile ??= MapBundles();
            AssetsFileInstance found = null;
            if (_bundleOfFile.TryGetValue(name, out var path)) {
                var bundle = Manager.LoadBundleFile(path, true);
                var infos = bundle.file.BlockAndDirInfo.DirectoryInfos;
                for (var i = 0; i < infos.Count; i++) {
                    if (infos[i].Name == name) {
                        found = Manager.LoadAssetsFileFromBundle(bundle, i, false);
                        found.file.GenerateQuickLookup();
                        break;
                    }
                }
            }

            Files[name] = found;
            return found;
        }

        /// <summary>
        /// The bundle that holds each assets file, by the file's name, as the references of scenes name them.
        /// </summary>
        private static Dictionary<string, string> MapBundles() {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(_bundleDir, "*.bundle", SearchOption.AllDirectories)) {
                try {
                    var bundle = Manager.LoadBundleFile(path, false);
                    foreach (var directory in bundle.file.BlockAndDirInfo.DirectoryInfos) {
                        map.TryAdd(directory.Name, path);
                    }

                    Manager.UnloadBundleFile(bundle);
                } catch (Exception e) {
                    Console.Error.WriteLine($"Could not list the files of {Path.GetFileName(path)}: {e.Message}");
                }
            }

            return map;
        }
    }

    private static void Print(Dictionary<long, Node> nodes, long id, int depth) {
        var node = nodes[id];
        var off = node.Active ? "" : " [off]";
        Console.WriteLine(
            $"{new string(' ', depth * 2)}{node.Name}{off} (" + (PrintIds ? $"id {id}, " : "") +
            $"layer {node.Layer}, z {node.Z:R}" +
            (node.Place == null ? "" : $", at {node.Place}") + (node.Turn == null ? "" : $", turned {node.Turn}") +
            (node.Scale == null ? "" : $", scale {node.Scale}") + "): " +
            string.Join(", ", node.Components)
        );
        foreach (var child in node.Children) {
            Print(nodes, child, depth + 1);
        }
    }
}
