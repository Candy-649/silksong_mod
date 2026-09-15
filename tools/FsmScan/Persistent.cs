using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// One saved object of a scene: a PersistentBoolItem, PersistentIntItem or GeoRock, with what else is on its object and
/// its parent, for telling changes to the world like doors and walls apart from pickups and money of one player.
/// </summary>
internal sealed class PersistentRecord {
    public string Bundle;
    public string File;
    public string Scene;
    public long GameObjectPathId;
    public long ParentPathId;
    public string Kind;
    public string Id;
    public bool IsSemiPersistent;
    public string ObjectPath;
    public List<string> Components;
    public string ParentName;
    public List<string> ParentComponents;
}

/// <summary>
/// Lists every saved object in the scenes with the FSMs and FSM actions on it and on its parent, as a TSV for working
/// out which saved objects are shared world state in a two-player save.
/// </summary>
internal static class PersistentScanner {
    /// <summary>
    /// The script classes whose state the game keeps in SceneData.
    /// </summary>
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal) {
        "PersistentBoolItem", "PersistentIntItem", "GeoRock",
    };

    /// <summary>
    /// Adds the saved objects of one assets file of a scene bundle to the scan result.
    /// </summary>
    public static void Collect(
        AssetsManager manager,
        AssetsFileInstance instance,
        FileContext context,
        IEnumerable<AssetFileInfo> monoBehaviours,
        BundleRef bundle,
        string scene,
        ScanResult result
    ) {
        foreach (var info in monoBehaviours) {
            if (!context.MonoClasses.TryGetValue(info.PathId, out var className) || className == null ||
                !Classes.Contains(className)) {
                continue;
            }

            var root = manager.GetBaseField(instance, info);
            var gameObjectId = root["m_GameObject"]["m_PathID"].AsLong;
            var gameObject = context.GetGameObject(gameObjectId);
            if (gameObject == null) {
                continue;
            }

            // The game fills in an empty ID with the name of the object when the item is set up
            var id = root.Get("itemData", "ID")?.AsString ?? root.Get("geoRockData", "id")?.AsString;
            var ancestors = context.Ancestors(gameObject);
            var parent = ancestors.FirstOrDefault();
            result.Persistent.Add(new PersistentRecord {
                Bundle = bundle.Rel,
                File = instance.name,
                Scene = scene,
                GameObjectPathId = gameObjectId,
                ParentPathId = parent?.PathId ?? 0,
                Kind = className,
                Id = string.IsNullOrEmpty(id) ? gameObject.Name : id,
                IsSemiPersistent = root.Get("itemData", "IsSemiPersistent")?.AsBool ?? false,
                ObjectPath = string.Join(
                    "/",
                    ancestors.AsEnumerable().Reverse().Select(a => a.Name).Append(gameObject.Name)
                ),
                Components = gameObject.ComponentClasses.ToList(),
                ParentName = parent?.Name ?? "",
                ParentComponents = parent?.ComponentClasses.ToList() ?? new List<string>(),
            });
        }
    }

    public static void Run(string bundleDir, string filter, EntityRegistryData registry, string outPath) {
        BundleScanner.CollectPersistent = true;
        var scan = BundleScanner.Run(bundleDir, filter ?? "scenes_scenes_scenes", 0, registry);
        var fsmsByObject = scan.Fsms
            .GroupBy(r => (r.Bundle, r.File, r.GameObjectPathId))
            .ToDictionary(g => g.Key, g => g.ToList());

        List<FsmRecord> FsmsOf(PersistentRecord record, long pathId) {
            return pathId != 0 && fsmsByObject.TryGetValue((record.Bundle, record.File, pathId), out var fsms)
                ? fsms
                : new List<FsmRecord>();
        }

        static string Actions(List<FsmRecord> fsms) {
            return string.Join(",", fsms
                .SelectMany(f => f.States ?? new List<StateData>())
                .SelectMany(s => s.Actions)
                .Select(a => a[(a.LastIndexOf('.') + 1)..])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal));
        }

        static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        var builder = new StringBuilder();
        builder.AppendLine(
            "scene\tid\tkind\tsemi\tpath\tcomponents\tparent\tparent_components\tfsms\tcategory\tactions\tparent_fsms\tparent_actions"
        );
        foreach (var record in scan.Persistent.OrderBy(r => r.Scene, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)) {
            var fsms = FsmsOf(record, record.GameObjectPathId);
            var parentFsms = FsmsOf(record, record.ParentPathId);
            builder.AppendLine(string.Join("\t",
                Clean(record.Scene),
                Clean(record.Id),
                record.Kind,
                record.IsSemiPersistent ? "1" : "0",
                Clean(record.ObjectPath),
                Clean(string.Join(",", record.Components)),
                Clean(record.ParentName),
                Clean(string.Join(",", record.ParentComponents)),
                Clean(string.Join(",", fsms.Select(f => f.FsmName))),
                Clean(fsms.FirstOrDefault()?.Category ?? ""),
                Clean(Actions(fsms)),
                Clean(string.Join(",", parentFsms.Select(f => f.FsmName))),
                Clean(Actions(parentFsms))
            ));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        System.IO.File.WriteAllText(outPath, builder.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{scan.Persistent.Count} saved objects in {scan.BundlesScanned} bundles: {outPath}");
    }
}
