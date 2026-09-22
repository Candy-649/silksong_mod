using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// Prints the objects of a prefab or scene as a tree for --tree: each object with whether it starts switched off, its
/// physics layer and depth, and every component on it (built-in ones by class, scripts by script class), with whether
/// colliders are triggers, which renderers start switched off and whether particle systems play by themselves. Used to
/// see what a character is made of, such as which parts of the hero only show while it does something.
/// </summary>
internal static class TreeDumper {
    public static void Run(string bundleDir, string filter, string rootPattern) {
        var rootRegex = new Regex(rootPattern ?? ".", RegexOptions.IgnoreCase);
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();

        var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Where(path => filter == null || path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var path in paths) {
            try {
                PrintBundle(manager, path, bundleDir, scriptNames, rootRegex);
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
        public float Z;
        public readonly List<string> Components = new();
        public readonly List<long> Children = new();
        public long Parent;
    }

    private static void PrintBundle(
        AssetsManager manager,
        string path,
        string bundleDir,
        Dictionary<string, string> scriptNames,
        Regex rootRegex
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
                    Layer = field.Get("m_Layer")?.AsInt ?? 0
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

                return parts.Count == 0 ? type.ToString() : $"{type}({string.Join(",", parts)})";
            }
            case AssetClassID.MeshRenderer:
            case AssetClassID.SpriteRenderer:
            case AssetClassID.LineRenderer:
            case AssetClassID.TrailRenderer:
            case AssetClassID.ParticleSystemRenderer: {
                var field = manager.GetBaseField(instance, info);
                return field.Get("m_Enabled")?.AsBool == false ? $"{type}(off)" : type.ToString();
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

    private static void Print(Dictionary<long, Node> nodes, long id, int depth) {
        var node = nodes[id];
        var off = node.Active ? "" : " [off]";
        Console.WriteLine(
            $"{new string(' ', depth * 2)}{node.Name}{off} (layer {node.Layer}, z {node.Z:R}): " +
            string.Join(", ", node.Components)
        );
        foreach (var child in node.Children) {
            Print(nodes, child, depth + 1);
        }
    }
}
