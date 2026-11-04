using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// BundleGrep: read-only. Lists every place where a text occurs in the game's managed DLLs (UTF-8 and UTF-16) and in
// every Addressables bundle, decompressed, with the asset it is in (type, path ID, name). FSM scans only see what they
// decode; this sees any serialized string: FSM action parameters, component fields, animation events.
// Found that the hero's Map Control sends MAPWALK START (2026-11-04) after FSM-only checks had said nothing did.
// Usage: dotnet run -c Release --project tools/BundleGrep -- <text> [regex of bundle names to skip, e.g. "^(sfx|music)"]
//   Reads one bundle at a time; about a minute for all 2068 bundles with sound skipped.
const string Data = @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data";
var text = args[0];
var skip = args.Length > 1 ? new System.Text.RegularExpressions.Regex(args[1]) : null;
var utf8 = Encoding.UTF8.GetBytes(text);
var utf16 = Encoding.Unicode.GetBytes(text);

foreach (var dll in Directory.GetFiles(Path.Combine(Data, "Managed"), "*.dll")) {
    var bytes = File.ReadAllBytes(dll);
    foreach (var (needle, kind) in new[] { (utf8, "utf8"), (utf16, "utf16") }) {
        foreach (var at in Find(bytes, needle)) {
            Console.WriteLine($"DLL {Path.GetFileName(dll)} {kind} @{at}: {Around(bytes, at, needle.Length, kind == "utf16")}");
        }
    }
}

var bundleDir = Path.Combine(Data, "StreamingAssets", "aa", "StandaloneWindows64");
var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories);
var manager = new AssetsManager();
var done = 0;
var skipped = 0;
foreach (var path in paths) {
    var name = Path.GetFileName(path);
    if (skip != null && skip.IsMatch(name)) {
        skipped++;
        continue;
    }

    try {
        var bundle = manager.LoadBundleFile(path, true);
        var infos = bundle.file.BlockAndDirInfo.DirectoryInfos;
        for (var i = 0; i < infos.Count; i++) {
            var dir = infos[i];
            var reader = bundle.file.DataReader;
            reader.Position = dir.Offset;
            var bytes = reader.ReadBytes((int) dir.DecompressedSize);
            var hits = Find(bytes, utf8).ToList();
            if (hits.Count == 0) {
                continue;
            }

            AssetsFileInstance? instance = null;
            if (bundle.file.IsAssetsFile(i)) {
                instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
            }

            foreach (var at in hits) {
                var where = "";
                if (instance != null) {
                    var dataOffset = (long) instance.file.Header.DataOffset;
                    foreach (var info in instance.file.AssetInfos) {
                        var start = dataOffset + info.ByteOffset;
                        if (at >= start && at < start + info.ByteSize) {
                            string assetName;
                            try {
                                assetName = manager.GetBaseField(instance, info)?.Get("m_Name")?.AsString ?? "";
                            } catch {
                                assetName = "?";
                            }

                            where = $" in {(AssetClassID) info.TypeId} pathId {info.PathId} '{assetName}'";
                            break;
                        }
                    }
                }

                Console.WriteLine($"BUNDLE {name} [{dir.Name}]{where} @{at}: {Around(bytes, at, utf8.Length, false)}");
            }
        }
    } catch (Exception e) {
        Console.WriteLine($"error in {name}: {e.GetType().Name}: {e.Message}");
    } finally {
        manager.UnloadAll(true);
    }

    if (++done % 100 == 0) {
        GC.Collect();
        Console.Error.WriteLine($"{done} bundles read");
    }
}

Console.WriteLine($"{done} bundles read, {skipped} skipped, of {paths.Length}");

static List<long> Find(byte[] haystack, byte[] needle) {
    var found = new List<long>();
    var offset = 0;
    while (true) {
        var i = haystack.AsSpan(offset).IndexOf(needle);
        if (i < 0) {
            return found;
        }

        found.Add(offset + i);
        offset += i + 1;
    }
}

static string Around(byte[] bytes, long at, int length, bool wide) {
    var from = (int) Math.Max(0, at - 48);
    var to = (int) Math.Min(bytes.Length, at + length + 48);
    var sb = new StringBuilder();
    for (var i = from; i < to; i++) {
        var b = bytes[i];
        if (wide && b == 0) {
            continue;
        }

        sb.Append(b is >= 32 and < 127 ? (char) b : '.');
    }

    return sb.ToString();
}
