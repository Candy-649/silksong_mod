using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// Checks the stable entity IDs of SSMP for --stableids. SSMP numbers the creatures of a room by a hash of the room's
/// name and of each object's name and local position on the way down to it (EntityProcessor.GetStableSceneId), so that
/// both games give a creature the same number. A SetZRandom on the object or above it sets its depth at random when it
/// is switched on, which for an object that is on as the room loads is before SSMP numbers it: such a creature gets
/// another number in each game. This prints how many objects that SSMP would register by name are like that, and which
/// of them would share a number if the depth were left out of the hash, which the depth alone tells apart today.
/// Objects are matched with the registry by name, parent name and FSM names, as SSMP does.
/// </summary>
internal static class StableIdChecker {
    private sealed class Node {
        public string Name;
        public bool Active;
        public long Father;
        public float X, Y, Z;
        public bool RandomDepth;
        public readonly List<string> FsmNames = new();
    }

    public static void Run(string bundleDir, string filter, EntityRegistryData registry) {
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();

        var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Where(path => path.Contains(@"scenes_scenes_scenes", StringComparison.OrdinalIgnoreCase))
            .Where(path => filter == null || path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal);

        int candidates = 0, randomDepth = 0, randomDepthOnAtLoad = 0, newShared = 0, sharedToday = 0;
        var randomDepthTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var randomDepthRooms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths) {
            var room = Path.GetFileNameWithoutExtension(path);
            try {
                var bundle = manager.LoadBundleFile(path, true);
                for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
                    if (!bundle.file.IsAssetsFile(i)) {
                        continue;
                    }

                    var instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
                    instance.file.GenerateQuickLookup();
                    var context = new FileContext(manager, instance, scriptNames);
                    var nodes = ReadNodes(manager, instance, context);

                    // The objects SSMP would register, with their keys with and without the depth
                    var found = new List<(long Id, string Type, string WithZ, string WithoutZ, bool Random, bool OnAtLoad)>();
                    foreach (var (id, node) in nodes) {
                        var parent = nodes.TryGetValue(node.Father, out var father) ? father.Name : null;
                        var entry = registry.Match(node.Name, node.FsmNames, parent);
                        if (entry == null) {
                            continue;
                        }

                        var withZ = new List<string>();
                        var withoutZ = new List<string>();
                        var random = false;
                        var onAtLoad = true;
                        for (var current = id; current != 0 && nodes.TryGetValue(current, out var step);
                             current = step.Father) {
                            withoutZ.Add($"{step.Name}|{BitConverter.SingleToInt32Bits(step.X)}|" +
                                         $"{BitConverter.SingleToInt32Bits(step.Y)}");
                            withZ.Add($"{withoutZ[^1]}|{BitConverter.SingleToInt32Bits(step.Z)}");
                            random |= step.RandomDepth;
                            onAtLoad &= step.Active;
                        }

                        found.Add((id, entry.Type, string.Join("/", withZ), string.Join("/", withoutZ), random,
                            onAtLoad));
                    }

                    candidates += found.Count;
                    foreach (var item in found.Where(item => item.Random)) {
                        randomDepth++;
                        if (item.OnAtLoad) {
                            randomDepthOnAtLoad++;
                            randomDepthRooms.Add(room);
                            randomDepthTypes[item.Type] = randomDepthTypes.GetValueOrDefault(item.Type) + 1;
                        }
                    }

                    foreach (var group in found.GroupBy(item => item.WithoutZ).Where(group => group.Count() > 1)) {
                        var distinct = group.Select(item => item.WithZ).Distinct().Count();
                        var names = string.Join(", ", group.Select(item => $"{nodes[item.Id].Name} ({item.Type})"));
                        if (distinct > 1) {
                            newShared += group.Count();
                            Console.WriteLine($"would share without depth: {room}: {names}");
                        } else {
                            sharedToday += group.Count();
                            Console.WriteLine($"share today already: {room}: {names}");
                        }
                    }
                }
            } catch (Exception e) {
                Console.WriteLine($"error in {Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }
        }

        Console.WriteLine($"objects registered by name: {candidates}");
        Console.WriteLine(
            $"with a random depth on them or above: {randomDepth}, of which on as the room loads: " +
            $"{randomDepthOnAtLoad} in {randomDepthRooms.Count} rooms"
        );
        Console.WriteLine(
            "  by type: " + string.Join(", ", randomDepthTypes.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key} {pair.Value}"))
        );
        Console.WriteLine($"objects that would share a number only without the depth: {newShared}");
        Console.WriteLine($"objects that share a number today already: {sharedToday}");
    }

    private static Dictionary<long, Node> ReadNodes(AssetsManager manager, AssetsFileInstance instance,
        FileContext context) {
        var nodes = new Dictionary<long, Node>();
        var objectOfTransform = new Dictionary<long, long>();
        var fatherOfTransform = new Dictionary<long, long>();
        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.GameObject)) {
            var field = manager.GetBaseField(instance, info);
            var node = new Node {
                Name = field["m_Name"].AsString,
                Active = field.Get("m_IsActive")?.AsBool ?? true
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
                    if (transform.Get("m_LocalPosition") is { IsDummy: false } position) {
                        node.X = position["x"].AsFloat;
                        node.Y = position["y"].AsFloat;
                        node.Z = position["z"].AsFloat;
                    }
                } else if (type == AssetClassID.MonoBehaviour) {
                    switch (context.ScriptClass(componentInfo)) {
                        case "SetZRandom": {
                            var behaviour = manager.GetBaseField(instance, componentInfo);
                            node.RandomDepth |= behaviour.Get("m_Enabled")?.AsBool ?? true;
                            break;
                        }
                        case "PlayMakerFSM": {
                            var behaviour = manager.GetBaseField(instance, componentInfo);
                            if (behaviour.Get("fsm")?.Get("name")?.AsString is { } fsmName) {
                                node.FsmNames.Add(fsmName);
                            }

                            break;
                        }
                    }
                }
            }
        }

        foreach (var (transformId, objectId) in objectOfTransform) {
            var fatherId = fatherOfTransform[transformId];
            if (fatherId != 0 && objectOfTransform.TryGetValue(fatherId, out var parentId)) {
                nodes[objectId].Father = parentId;
            }
        }

        return nodes;
    }
}
