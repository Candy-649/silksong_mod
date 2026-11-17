using AssetsTools.NET;
using AssetsTools.NET.Extra;

// ClipDraw: draws the frames of the tk2d clips that an object in a scene plays, to see what a creature's moves look
// like, and writes a page that plays runs of clips side by side at the game's speed, with every frame shown below.
//
// Usage: dotnet run -c Release --project tools/ClipDraw -- --scene <scene> --object <path> [--list]
//            [--run "<title>:<clip>[@<frame>|~<seconds>][=<note>];<clip>..."]... [--out <page.html>]
//            [--title <text>] [--intro <text>] [--bundles <dir>]
//   --object: the path of the object in the scene, or the end of it, e.g. "Boss Scene - To Additive Load/Song Knight".
//   --list: prints every clip of the object's animator: its speed, how it wraps, and the frames that fire an event.
//   --run: one column of the page: a title and the clips it plays one after another, each to its end, to a frame
//          (@n, that frame shown too) or for a time (~s, going round or holding its last frame as the clip does),
//          with a note for its row of frames after =.

internal static class Program {
    public static int Main(string[] args) {
        string scene = null, objectPath = null, outPath = null, title = null, intro = "";
        var list = false;
        var runs = new List<string>();
        for (var i = 0; i < args.Length; i++) {
            switch (args[i]) {
                case "--bundles": Assets.BundleDir = args[++i]; break;
                case "--scene": scene = args[++i]; break;
                case "--object": objectPath = args[++i]; break;
                case "--list": list = true; break;
                case "--run": runs.Add(args[++i]); break;
                case "--out": outPath = args[++i]; break;
                case "--title": title = args[++i]; break;
                case "--intro": intro = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown argument {args[i]}");
                    return 1;
            }
        }

        var (file, animator) = Assets.FindAnimator(scene, objectPath);
        var library = Assets.Resolve(file, animator["library"]);
        var clips = Clip.ReadAll(library);
        if (list) {
            foreach (var clip in clips.Values) {
                Console.WriteLine(clip.Describe());
            }
        }

        if (runs.Count > 0) {
            Page.Write(outPath, title ?? objectPath, intro, runs.Select(run => Run.Parse(run, clips)).ToList());
            Console.WriteLine($"Wrote {outPath}");
        }

        return 0;
    }
}

/// <summary>An object of an assets file, with its fields.</summary>
internal readonly record struct Asset(AssetsFileInstance File, AssetFileInfo Info, AssetTypeValueField Field);

/// <summary>
/// The game's bundles: the scene that holds the object, and the assets files that its references lead into, found by
/// their names in the bundles.
/// </summary>
internal static class Assets {
    public static string BundleDir =
        @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\StreamingAssets\aa\" +
        "StandaloneWindows64";

    private static readonly AssetsManager Manager = new();
    private static readonly Dictionary<string, AssetsFileInstance> Files = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BundleFileInstance> Bundles = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string, long), AssetTypeValueField> Fields = new();
    private static Dictionary<string, string> _bundleOfFile;

    /// <summary>The tk2d animator of the object at the end of a path in a scene, and the file it is in.</summary>
    public static (AssetsFileInstance File, AssetTypeValueField Animator) FindAnimator(string scene, string path) {
        var bundlePath = Path.Combine(BundleDir, "scenes_scenes_scenes", scene.ToLowerInvariant() + ".bundle");
        var bundle = Manager.LoadBundleFile(bundlePath, true);
        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
            if (!bundle.file.IsAssetsFile(i)) {
                continue;
            }

            var file = Manager.LoadAssetsFileFromBundle(bundle, i, false);
            file.file.GenerateQuickLookup();
            Files[file.name] = file;
            var gameObjects = file.file.GetAssetsOfType(AssetClassID.GameObject)
                .ToDictionary(info => info.PathId, info => Manager.GetBaseField(file, info));
            var objectOf = new Dictionary<long, long>();
            var fatherOf = new Dictionary<long, long>();
            foreach (var info in file.file.GetAssetsOfType(AssetClassID.Transform)) {
                var transform = Manager.GetBaseField(file, info);
                objectOf[info.PathId] = transform["m_GameObject"]["m_PathID"].AsLong;
                fatherOf[info.PathId] = transform["m_Father"]["m_PathID"].AsLong;
            }

            foreach (var (transform, gameObject) in objectOf) {
                var names = new List<string>();
                for (var at = transform; at != 0; at = fatherOf[at]) {
                    names.Insert(0, gameObjects[objectOf[at]]["m_Name"].AsString);
                }

                var full = string.Join('/', names);
                if (full != path && !full.EndsWith("/" + path, StringComparison.Ordinal)) {
                    continue;
                }

                foreach (var component in gameObjects[gameObject]["m_Component"]["Array"].Children) {
                    var info = file.file.GetAssetInfo(component["component"]["m_PathID"].AsLong);
                    if (info.TypeId != (int) AssetClassID.MonoBehaviour) {
                        continue;
                    }

                    var field = Manager.GetBaseField(file, info);
                    if (!field["library"].IsDummy && !field["defaultClipId"].IsDummy) {
                        Console.WriteLine($"Animator of '{full}' in {file.name}");
                        return (file, field);
                    }
                }
            }
        }

        throw new InvalidDataException($"No object with a tk2d animator at '{path}' in {scene}");
    }

    /// <summary>The object that a reference of an object of a file points to.</summary>
    public static Asset Resolve(AssetsFileInstance from, AssetTypeValueField pointer) {
        var fileId = pointer["m_FileID"].AsInt;
        var file = fileId == 0 ? from : FileNamed(ExternalName(from, fileId));
        var info = file.file.GetAssetInfo(pointer["m_PathID"].AsLong);
        var key = (file.name, info.PathId);
        if (!Fields.TryGetValue(key, out var field)) {
            Fields[key] = field = Manager.GetBaseField(file, info);
        }

        return new Asset(file, info, field);
    }

    /// <summary>Bytes of a file in a bundle, like the pixels that a texture keeps out of its assets file.</summary>
    public static byte[] ReadResource(string name, long offset, int size) {
        var bundle = BundleHolding(name);
        var directory = bundle.file.BlockAndDirInfo.DirectoryInfos.First(d => d.Name == name);
        var reader = bundle.file.DataReader;
        reader.Position = directory.Offset + offset;
        return reader.ReadBytes(size);
    }

    private static string ExternalName(AssetsFileInstance file, int fileId) {
        var pathName = file.file.Metadata.Externals[fileId - 1].PathName;
        return pathName[(pathName.LastIndexOf('/') + 1)..];
    }

    private static AssetsFileInstance FileNamed(string name) {
        if (Files.TryGetValue(name, out var known)) {
            return known;
        }

        var bundle = BundleHolding(name);
        var infos = bundle.file.BlockAndDirInfo.DirectoryInfos;
        var index = 0;
        while (infos[index].Name != name) {
            index++;
        }

        var file = Manager.LoadAssetsFileFromBundle(bundle, index, false);
        file.file.GenerateQuickLookup();
        Files[name] = file;
        return file;
    }

    private static BundleFileInstance BundleHolding(string name) {
        _bundleOfFile ??= MapBundles();
        var path = _bundleOfFile[name];
        if (!Bundles.TryGetValue(path, out var bundle)) {
            Bundles[path] = bundle = Manager.LoadBundleFile(path, true);
        }

        return bundle;
    }

    /// <summary>Which bundle each file of the bundles is in, read from their headers alone.</summary>
    private static Dictionary<string, string> MapBundles() {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(BundleDir, "*.bundle", SearchOption.AllDirectories)) {
            var bundle = Manager.LoadBundleFile(path, false);
            foreach (var directory in bundle.file.BlockAndDirInfo.DirectoryInfos) {
                map.TryAdd(directory.Name, path);
            }

            Manager.UnloadBundleFile(bundle);
        }

        return map;
    }
}
