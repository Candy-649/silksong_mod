using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// FsmScan: reads every PlayMakerFSM in the game's Addressables bundles and works out who actually uses the FSM
// actions that SyncAudit tagged as SSMP sync gaps: registered entities, unregistered FSMs inside them, other enemies,
// scene mechanisms, NPCs, pooled prefabs, corpses, the hero or UI. Read-only; writes a Markdown and a JSON report.
//
// Usage: dotnet run -c Release --project tools/FsmScan -- [--bundles <dir>] [--ssmp <repo>] [--audit <sync-audit.json>]
//            [--out <report.md>] [--filter <bundle path substring>] [--limit <bundle count>]
//    or: dotnet run -c Release --project tools/FsmScan -- --dump <script class> [--filter <bundle path substring>]
//            [--fields <regex>]  to print the serialized fields of every MonoBehaviour with that script class
//    or: dotnet run -c Release --project tools/FsmScan -- --params <action type regex> [--fields <regex>]
//            [--filter ...]  to count the parameter values of those FSM actions per FSM category instead of reporting
//    or: dotnet run -c Release --project tools/FsmScan -- --persistent <out.tsv> [--filter ...]  to list every saved
//            object in the scenes with the FSMs and FSM actions on it and on its parent

var options = Cli.Parse(args);
var bundleDir = options.GetValueOrDefault(
    "bundles",
    @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64"
);
var ssmpDir = options.GetValueOrDefault("ssmp", @"D:\programming\silksong_mod\SSMP");
var auditPath = options.GetValueOrDefault("audit", @"D:\programming\silksong_mod\reports\sync-audit.json");
var outPath = options.GetValueOrDefault("out", @"D:\programming\silksong_mod\reports\fsm-usage.md");
var filter = options.GetValueOrDefault("filter");
var limit = options.TryGetValue("limit", out var limitText) ? int.Parse(limitText) : 0;
if (options.TryGetValue("items", out var itemsText)) {
    ComponentDumper.MaxArrayItems = int.Parse(itemsText);
}
if (options.TryGetValue("depth", out var depthText)) {
    ComponentDumper.MaxDepth = int.Parse(depthText);
}

if (options.TryGetValue("dump", out var dumpClass)) {
    ComponentDumper.Run(bundleDir, filter, dumpClass, options.GetValueOrDefault("fields"));
    return;
}

if (options.TryGetValue("layout", out var layoutScenes)) {
    LayoutDumper.Run(bundleDir, layoutScenes);
    return;
}

if (options.TryGetValue("rooms", out var roomScenes)) {
    RoomAnalyzer.Run(bundleDir, roomScenes, ssmpDir);
    return;
}

if (options.TryGetValue("persistent", out var persistentOut)) {
    PersistentScanner.Run(bundleDir, filter, EntityRegistryData.Load(ssmpDir), persistentOut);
    return;
}

if (options.TryGetValue("params", out var paramActions)) {
    ActionParams.ActionPattern = new Regex(paramActions);
    ActionParams.FieldPattern = options.TryGetValue("fields", out var paramFields) ? new Regex(paramFields) : null;
    ActionParams.FsmPattern = options.TryGetValue("fsm", out var paramFsms) ? new Regex(paramFsms) : null;
}

var audit = AuditData.Load(auditPath);
var registry = EntityRegistryData.Load(ssmpDir);
Console.WriteLine(
    $"Audit: SSMP {audit.SsmpBranch} @ {audit.SsmpCommit}, {audit.Actions.Count(a => a.IsGap)} gap actions; " +
    $"registry: {registry.Entries.Count} entries ({registry.IgnoredEntries} with unknown types ignored)"
);

var scan = BundleScanner.Run(bundleDir, filter, limit, registry);
if (ActionParams.ActionPattern != null) {
    ActionParams.Print(scan);
    return;
}

var analysis = Analysis.Build(scan, audit);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
File.WriteAllText(outPath, ReportWriter.Markdown(analysis, scan, audit, registry, bundleDir), new UTF8Encoding(false));
var jsonPath = Path.ChangeExtension(outPath, ".json");
File.WriteAllText(jsonPath, ReportWriter.Json(analysis), new UTF8Encoding(false));
var indexPath = Path.ChangeExtension(outPath, ".tsv");
ReportWriter.WriteIndex(analysis, indexPath);

ReportWriter.PrintSummary(analysis, scan);
Console.WriteLine($"Markdown: {outPath}");
Console.WriteLine($"JSON:     {jsonPath}");
Console.WriteLine($"Index:    {indexPath}");

internal static class Cli {
    public static Dictionary<string, string> Parse(string[] args) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i += 2) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) {
                throw new ArgumentException($"Expected '--option value', got '{args[i]}'");
            }

            result[args[i][2..]] = args[i + 1];
        }

        return result;
    }
}

internal static class FieldExtensions {
    /// <summary>
    /// Walks a path of child fields, returning null as soon as one is missing.
    /// </summary>
    public static AssetTypeValueField Get(this AssetTypeValueField field, params string[] path) {
        foreach (var name in path) {
            if (field == null || field.IsDummy) {
                return null;
            }

            field = field[name];
        }

        return field == null || field.IsDummy ? null : field;
    }
}

/// <summary>
/// One FSM action type with the report sections SyncAudit put it in.
/// </summary>
internal sealed class ActionGap {
    public string Name;
    public string FullName;
    public List<string> Effects;
    public List<string> Tags;
    public List<string> VariantOf;

    /// <summary>
    /// Whether the action is in one of the gap sections A-D; E and L are only worth a look.
    /// </summary>
    public bool IsGap => Tags.Any(t => t is "A" or "B" or "C1" or "C2" or "D");

    public string TagText => string.Join("/", Tags);
}

internal sealed class AuditData {
    public string SsmpBranch;
    public string SsmpCommit;
    private readonly Dictionary<string, ActionGap> _byFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActionGap> _bySimpleName = new(StringComparer.Ordinal);

    public IEnumerable<ActionGap> Actions => _byFullName.Values;

    public static AuditData Load(string path) {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var ssmp = doc.RootElement.GetProperty("ssmp");
        var data = new AuditData {
            SsmpBranch = ssmp.GetProperty("branch").GetString(),
            SsmpCommit = ssmp.GetProperty("commit").GetString(),
        };

        foreach (var element in doc.RootElement.GetProperty("actions").EnumerateArray()) {
            if (!element.TryGetProperty("tags", out var tags)) {
                throw new InvalidDataException($"{path} has no gap tags, re-run tools/SyncAudit first");
            }

            var action = new ActionGap {
                Name = element.GetProperty("name").GetString(),
                FullName = element.GetProperty("fullName").GetString(),
                Effects = Strings(element.GetProperty("effects")),
                Tags = Strings(tags),
                VariantOf = Strings(element.GetProperty("variantOf")),
            };
            data._byFullName.TryAdd(action.FullName, action);
            data._bySimpleName.TryAdd(action.Name, action);
        }

        return data;
    }

    /// <summary>
    /// Looks up an action by the full type name stored in the FSM, falling back to the class name.
    /// </summary>
    public ActionGap Find(string fullName) {
        // Reflection separates nested types with '+', SyncAudit with '/'
        var key = fullName.Replace('+', '/');
        if (_byFullName.TryGetValue(key, out var action)) {
            return action;
        }

        return _bySimpleName.GetValueOrDefault(key[(key.LastIndexOfAny(['.', '/']) + 1)..]);
    }

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()).ToList();
}

internal sealed class RegistryEntry {
    public string BaseObjectName;
    public string Type;
    public HashSet<string> FsmNames;
    public string ParentName;
}

/// <summary>
/// SSMP's entity registry, matched with the same rules as SSMP's EntityRegistry.TryGetEntry.
/// </summary>
internal sealed class EntityRegistryData {
    public readonly List<RegistryEntry> Entries = new();
    public int IgnoredEntries;

    public static EntityRegistryData Load(string ssmpRoot) {
        var code = Path.Combine(ssmpRoot, "SSMP");
        var validTypes = ParseEnumMembers(File.ReadAllText(Path.Combine(code, "Game", "Client", "Entity", "EntityType.cs")));

        var data = new EntityRegistryData();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(code, "Resource", "entity-registry.json")));
        foreach (var element in doc.RootElement.EnumerateArray()) {
            var type = element.GetProperty("type").GetString();
            if (!validTypes.Contains(type)) {
                data.IgnoredEntries++;
                continue;
            }

            var entry = new RegistryEntry {
                BaseObjectName = element.GetProperty("base_object_name").GetString(),
                Type = type,
                ParentName = element.TryGetProperty("parent_name", out var parent) && parent.ValueKind == JsonValueKind.String
                    ? parent.GetString()
                    : null,
            };
            if (element.TryGetProperty("fsm_names", out var fsmNames) && fsmNames.ValueKind == JsonValueKind.Array) {
                var names = fsmNames.EnumerateArray().Select(e => e.GetString()).ToList();
                if (names.Count > 0) {
                    entry.FsmNames = new HashSet<string>(names, StringComparer.Ordinal);
                }
            }

            data.Entries.Add(entry);
        }

        return data;
    }

    public RegistryEntry Match(string objectName, IReadOnlyCollection<string> fsmNames, string parentName) {
        RegistryEntry found = null;
        foreach (var entry in Entries) {
            if (!objectName.Contains(entry.BaseObjectName, StringComparison.Ordinal)) {
                continue;
            }

            if (entry.FsmNames != null && !fsmNames.Any(entry.FsmNames.Contains)) {
                continue;
            }

            if (entry.ParentName != null &&
                (parentName == null || !parentName.Contains(entry.ParentName, StringComparison.Ordinal))) {
                continue;
            }

            if (found == null || entry.BaseObjectName.Length > found.BaseObjectName.Length) {
                found = entry;
            }
        }

        return found;
    }

    private static HashSet<string> ParseEnumMembers(string source) {
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//.*$", "", RegexOptions.Multiline);
        var start = source.IndexOf("enum EntityType", StringComparison.Ordinal);
        var open = source.IndexOf('{', start);
        var close = source.IndexOf('}', open);
        var members = source[(open + 1)..close]
            .Split(',')
            .Select(part => Regex.Replace(part.Split('=')[0], @"\[[^\]]*\]", "").Trim())
            .Where(part => Regex.IsMatch(part, @"^[A-Za-z_]\w*$"));
        return new HashSet<string>(members, StringComparer.Ordinal);
    }
}

internal sealed record BundleRef(string Path, string Rel, long Size);

/// <summary>
/// One FSM state: its enabled actions (full type names), its transitions ("EVENT->State") and the string parameters
/// of its actions, which include the names of events and FSMs the actions send to.
/// </summary>
internal sealed record StateData(string Name, List<string> Actions, List<string> Transitions, List<string> Strings) {
    /// <summary>
    /// Decoded parameters of the enabled actions picked with --params, one "Action: field=value, ..." line each.
    /// </summary>
    [JsonIgnore]
    public List<string> Params { get; init; } = new();

    /// <summary>
    /// The parameters of every enabled action, read only for --rooms.
    /// </summary>
    [JsonIgnore]
    public List<ActionParamsData> Structured { get; init; } = new();
}

/// <summary>
/// One enabled action of a state with its parameters.
/// </summary>
internal sealed record ActionParamsData(string Name, List<ParamValue> Params);

/// <summary>
/// One parameter of an action: its field, its PlayMaker parameter type, its value, which is null for a string that a
/// variable fills in, and the name of the FSM variable that fills it in, if any.
/// </summary>
internal sealed record ParamValue(string Field, int Type, string Value, string Variable = null);

internal sealed class GameObjectInfo {
    public long PathId;
    public string Name;
    public long TransformPathId;
    public bool IsRect;
    public readonly List<string> ComponentClasses = new();
}

internal sealed class FsmRecord {
    public string Bundle;
    public string File;
    public string BundleKind;
    public string Scene;
    public long GameObjectPathId;
    public string ObjectName;
    public string ObjectPath;
    public string FsmName;
    public string TemplateKey;
    public string TemplateName;
    public string StartState;
    public bool InArena;
    public List<StateData> States;
    public List<string> GlobalTransitions;
    public string Category;
    public string EntityType;
    public string EntityObject;
    public bool InEntityScene;

    // The values of the FSM's own variables by "type:name", with --persistent and --rooms, for the parameters that
    // variables fill in. A component that uses a template keeps its own variables.
    public Dictionary<string, string> Variables;
}

internal sealed class TemplateRecord {
    public string Name;
    public string StartState;
    public List<StateData> States;
    public List<string> GlobalTransitions;
}

internal sealed class ScanResult {
    public readonly List<FsmRecord> Fsms = new();
    public readonly List<PersistentRecord> Persistent = new();
    public readonly Dictionary<string, TemplateRecord> Templates = new(StringComparer.Ordinal);
    public readonly List<string> Errors = new();
    public readonly List<string> SkippedGroupSamples = new();
    public int BundlesScanned;
    public int BundlesSkipped;
    public int TemplateAttributions;
    public int UnresolvedTemplates;
    public int UnresolvedScripts;
}

/// <summary>
/// Lookups within one assets file: MonoBehaviour script classes, GameObjects and their parents.
/// </summary>
internal sealed class FileContext {
    private readonly AssetsManager _manager;
    private readonly AssetsFileInstance _instance;
    private readonly Dictionary<string, string> _scriptNames;
    private readonly Dictionary<long, GameObjectInfo> _gameObjects = new();
    private readonly Dictionary<long, long> _parentIds = new();

    /// <summary>
    /// Script class name (without namespace) of each MonoBehaviour in the file, or null when unresolved.
    /// </summary>
    public readonly Dictionary<long, string> MonoClasses = new();

    public FileContext(AssetsManager manager, AssetsFileInstance instance, Dictionary<string, string> scriptNames) {
        _manager = manager;
        _instance = instance;
        _scriptNames = scriptNames;
    }

    /// <summary>
    /// Name of the assets file a PPtr file ID points to, matching AssetsFileInstance.name of the loaded target.
    /// </summary>
    public string FileNameOf(int fileId) {
        if (fileId == 0) {
            return _instance.name;
        }

        var externals = _instance.file.Metadata.Externals;
        if (fileId - 1 >= externals.Count) {
            return null;
        }

        var pathName = externals[fileId - 1].PathName;
        return pathName[(pathName.LastIndexOf('/') + 1)..];
    }

    public string ScriptClass(AssetFileInfo info) {
        int index = info.GetScriptIndex(_instance.file);
        var scripts = _instance.file.Metadata.ScriptTypes;
        if (index == 0xFFFF || index >= scripts.Count) {
            return null;
        }

        var pointer = scripts[index];
        var file = FileNameOf(pointer.FileId);
        return file != null && _scriptNames.TryGetValue($"{file}:{pointer.PathId}", out var name) ? name : null;
    }

    public GameObjectInfo GetGameObject(long pathId) {
        if (pathId == 0) {
            return null;
        }

        if (_gameObjects.TryGetValue(pathId, out var cached)) {
            return cached;
        }

        GameObjectInfo gameObject = null;
        var info = _instance.file.GetAssetInfo(pathId);
        if (info != null && info.TypeId == (int) AssetClassID.GameObject) {
            var field = _manager.GetBaseField(_instance, info);
            gameObject = new GameObjectInfo { PathId = pathId, Name = field["m_Name"].AsString };
            var components = field.Get("m_Component", "Array");
            if (components != null) {
                foreach (var pair in components.Children) {
                    var pointer = pair.Get("component");
                    if (pointer == null || pointer["m_FileID"].AsInt != 0) {
                        continue;
                    }

                    var componentId = pointer["m_PathID"].AsLong;
                    var componentInfo = _instance.file.GetAssetInfo(componentId);
                    if (componentInfo == null) {
                        continue;
                    }

                    if (componentInfo.TypeId is (int) AssetClassID.Transform or (int) AssetClassID.RectTransform) {
                        gameObject.TransformPathId = componentId;
                        gameObject.IsRect = componentInfo.TypeId == (int) AssetClassID.RectTransform;
                    } else if (MonoClasses.TryGetValue(componentId, out var className) && className != null) {
                        gameObject.ComponentClasses.Add(className);
                    }
                }
            }
        }

        _gameObjects[pathId] = gameObject;
        return gameObject;
    }

    public GameObjectInfo ParentOf(GameObjectInfo gameObject) {
        if (gameObject == null || gameObject.TransformPathId == 0) {
            return null;
        }

        if (!_parentIds.TryGetValue(gameObject.PathId, out var parentId)) {
            var transformInfo = _instance.file.GetAssetInfo(gameObject.TransformPathId);
            var father = transformInfo == null ? null : _manager.GetBaseField(_instance, transformInfo).Get("m_Father");
            if (father != null && father["m_FileID"].AsInt == 0 && father["m_PathID"].AsLong != 0) {
                var fatherInfo = _instance.file.GetAssetInfo(father["m_PathID"].AsLong);
                if (fatherInfo != null) {
                    parentId = _manager.GetBaseField(_instance, fatherInfo)["m_GameObject"]["m_PathID"].AsLong;
                }
            }

            _parentIds[gameObject.PathId] = parentId;
        }

        return GetGameObject(parentId);
    }

    public List<GameObjectInfo> Ancestors(GameObjectInfo gameObject) {
        var result = new List<GameObjectInfo>();
        for (var current = ParentOf(gameObject); current != null && result.Count < 64; current = ParentOf(current)) {
            result.Add(current);
        }

        return result;
    }
}

internal static class BundleScanner {
    /// <summary>
    /// Bundle name prefixes of groups that only hold textures, sprite collections, audio or fonts.
    /// </summary>
    private static readonly HashSet<string> AssetOnlyPrefixes = new(StringComparer.Ordinal) {
        "textures", "tk2dcollections", "herocollections", "sfxstatic", "sfxstaticpatch", "audiocuesstatic", "fonts",
        "fontspatch", "fontsmenu", "vibrationstatic",
    };

    private static readonly Regex HashPrefix = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    /// <summary>
    /// Whether scans also collect the saved objects of scenes, for --persistent. Such scans always read the FSM
    /// templates, so that FSMs made from a template get its actions.
    /// </summary>
    public static bool CollectPersistent;

    public static ScanResult Run(string bundleDir, string filter, int limit, EntityRegistryData registry) {
        var result = new ScanResult();
        var manager = new AssetsManager();
        var scriptNames = LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();
        Console.WriteLine($"MonoScripts: {scriptNames.Count}");

        var all = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Select(path => new BundleRef(path, Path.GetRelativePath(bundleDir, path).Replace('\\', '/'), new FileInfo(path).Length))
            .Where(b => PrefixOf(b.Rel) is not ("monoscripts" or "unitybuiltinassets"))
            .Where(b => filter == null || b.Rel.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        (CollectPersistent && PrefixOf(b.Rel) == "fsmtemplates"))
            .OrderBy(b => b.Rel, StringComparer.Ordinal)
            .ToList();

        // Asset-only groups are skipped except for their smallest and largest bundle, which are scanned to back up that
        // assumption; the report lists what they contained
        var samples = all
            .Where(b => AssetOnlyGroup(b.Rel) != null)
            .GroupBy(b => AssetOnlyGroup(b.Rel))
            .SelectMany(g => new[] { g.MinBy(b => b.Size), g.MaxBy(b => b.Size) })
            .ToHashSet();
        var bundles = all
            .Where(b => AssetOnlyGroup(b.Rel) == null)
            .Concat(samples.OrderBy(b => b.Rel, StringComparer.Ordinal))
            .ToList();
        result.BundlesSkipped = all.Count - bundles.Count;
        if (limit > 0) {
            bundles = bundles.Take(limit).ToList();
        }

        var stopwatch = Stopwatch.StartNew();
        foreach (var bundle in bundles) {
            var fsmsBefore = result.Fsms.Count;
            var templatesBefore = result.Templates.Count;
            try {
                ScanBundle(manager, bundle, scriptNames, registry, result);
            } catch (Exception e) {
                result.Errors.Add($"{bundle.Rel}: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }

            if (samples.Contains(bundle)) {
                result.SkippedGroupSamples.Add(
                    $"`{bundle.Rel}`（{bundle.Size / 1048576.0:F1} MB）：{result.Fsms.Count - fsmsBefore} 个状态机、{result.Templates.Count - templatesBefore} 个模板"
                );
            }

            result.BundlesScanned++;
            if (result.BundlesScanned % 50 == 0) {
                Console.WriteLine($"  {result.BundlesScanned}/{bundles.Count} bundles, {result.Fsms.Count} FSMs, {stopwatch.Elapsed:mm\\:ss}");
            }
        }

        ResolveTemplates(result);

        var entityScenes = result.Fsms.Where(r => r.Category == "entity" && r.Scene != null).Select(r => r.Scene).ToHashSet();
        foreach (var record in result.Fsms) {
            record.InEntityScene = record.Category == "scene" && record.Scene != null && entityScenes.Contains(record.Scene);
        }

        Console.WriteLine(
            $"Scanned {result.BundlesScanned} bundles in {stopwatch.Elapsed:mm\\:ss}: {result.Fsms.Count} FSMs, " +
            $"{result.Templates.Count} templates, {result.Errors.Count} errors"
        );
        return result;
    }

    /// <summary>
    /// Gives the FSMs that use a template the states of their template.
    /// </summary>
    internal static void ResolveTemplates(ScanResult result) {
        foreach (var record in result.Fsms.Where(r => r.TemplateKey != null)) {
            // PlayMakerFSM.InitTemplate swaps the component's own FSM for a copy of the template's
            if (result.Templates.TryGetValue(record.TemplateKey, out var template)) {
                record.States = template.States;
                record.GlobalTransitions = template.GlobalTransitions;
                record.TemplateName = template.Name;
                record.StartState = template.StartState;
                result.TemplateAttributions++;
            } else {
                result.UnresolvedTemplates++;
            }
        }
    }

    private static string PrefixOf(string rel) {
        var parts = Path.GetFileNameWithoutExtension(rel).Split('_');
        return parts.Length > 1 && HashPrefix.IsMatch(parts[0]) ? parts[1] : parts[0];
    }

    private static string AssetOnlyGroup(string rel) {
        if (rel.Split('/').Contains("_atlases")) {
            return "_atlases";
        }

        var prefix = PrefixOf(rel);
        return AssetOnlyPrefixes.Contains(prefix) ? prefix : null;
    }

    private static string KindOf(string rel) {
        if (rel.StartsWith("scenes_scenes_scenes/", StringComparison.Ordinal)) {
            return "scene";
        }

        return PrefixOf(rel) switch {
            "localpoolprefabs" or "globalpoolprefabs" => "pool",
            "enemycorpses" => "corpse",
            "fsmtemplates" => "template",
            _ => "other",
        };
    }

    /// <summary>
    /// Maps "assets file name:path ID" of every MonoScript to its class name without namespace.
    /// </summary>
    public static Dictionary<string, string> LoadScriptNames(AssetsManager manager, string bundleDir) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(bundleDir, "*_monoscripts.bundle", SearchOption.TopDirectoryOnly)) {
            var bundle = manager.LoadBundleFile(path, true);
            for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
                if (!bundle.file.IsAssetsFile(i)) {
                    continue;
                }

                var instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
                foreach (var info in instance.file.GetAssetsOfType(AssetClassID.MonoScript)) {
                    map[$"{instance.name}:{info.PathId}"] = manager.GetBaseField(instance, info)["m_ClassName"].AsString;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Scans the bundle at a path, for modes that pick their own bundles.
    /// </summary>
    internal static void ScanBundleAt(
        AssetsManager manager,
        string bundleDir,
        string path,
        Dictionary<string, string> scriptNames,
        EntityRegistryData registry,
        ScanResult result
    ) {
        var rel = Path.GetRelativePath(bundleDir, path).Replace(Path.DirectorySeparatorChar, '/');
        ScanBundle(manager, new BundleRef(path, rel, new FileInfo(path).Length), scriptNames, registry, result);
    }

    private static void ScanBundle(
        AssetsManager manager,
        BundleRef bundleRef,
        Dictionary<string, string> scriptNames,
        EntityRegistryData registry,
        ScanResult result
    ) {
        var bundle = manager.LoadBundleFile(bundleRef.Path, true);
        var kind = KindOf(bundleRef.Rel);
        var scene = kind == "scene" ? Path.GetFileNameWithoutExtension(bundleRef.Rel) : null;

        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
            if (!bundle.file.IsAssetsFile(i)) {
                continue;
            }

            var instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
            var monoBehaviours = instance.file.GetAssetsOfType(AssetClassID.MonoBehaviour);
            if (monoBehaviours.Count == 0) {
                continue;
            }

            instance.file.GenerateQuickLookup();
            var context = new FileContext(manager, instance, scriptNames);
            foreach (var info in monoBehaviours) {
                var className = context.ScriptClass(info);
                if (className == null) {
                    result.UnresolvedScripts++;
                }

                context.MonoClasses[info.PathId] = className;
            }

            if (CollectPersistent && scene != null) {
                PersistentScanner.Collect(manager, instance, context, monoBehaviours, bundleRef, scene, result);
            }

            var fileRecords = new List<FsmRecord>();
            foreach (var info in monoBehaviours) {
                var className = context.MonoClasses[info.PathId];
                if (className is not ("PlayMakerFSM" or "FsmTemplate")) {
                    continue;
                }

                var root = manager.GetBaseField(instance, info);
                var fsm = root.Get("fsm");
                if (fsm == null) {
                    continue;
                }

                if (className == "FsmTemplate") {
                    result.Templates[$"{instance.name}:{info.PathId}"] = new TemplateRecord {
                        Name = root["m_Name"].AsString,
                        StartState = fsm.Get("startState")?.AsString,
                        States = ReadStates(fsm),
                        GlobalTransitions = ReadTransitions(fsm.Get("globalTransitions", "Array")),
                    };
                    continue;
                }

                var record = new FsmRecord {
                    Bundle = bundleRef.Rel,
                    File = instance.name,
                    BundleKind = kind,
                    Scene = scene,
                    GameObjectPathId = root["m_GameObject"]["m_PathID"].AsLong,
                    FsmName = fsm.Get("name")?.AsString ?? "?",
                    StartState = fsm.Get("startState")?.AsString,
                    States = ReadStates(fsm),
                    GlobalTransitions = ReadTransitions(fsm.Get("globalTransitions", "Array")),
                };

                if (ActionParams.ReadStructuredParams) {
                    record.Variables = ReadVariables(fsm);
                }

                var template = root.Get("fsmTemplate");
                if (template != null && template["m_PathID"].AsLong != 0) {
                    var file = FileNameOrUnknown(context, template["m_FileID"].AsInt);
                    record.TemplateKey = $"{file}:{template["m_PathID"].AsLong}";
                }

                fileRecords.Add(record);
            }

            if (fileRecords.Count > 0) {
                Classify(context, fileRecords, registry, kind);
                result.Fsms.AddRange(fileRecords);
            }
        }
    }

    private static string FileNameOrUnknown(FileContext context, int fileId) => context.FileNameOf(fileId) ?? "?";

    /// <summary>
    /// Reads the values of the string, bool, int and float variables of an FSM by "type:name".
    /// </summary>
    private static Dictionary<string, string> ReadVariables(AssetTypeValueField fsm) {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (arrayName, type) in new[] {
                     ("stringVariables", "string"), ("boolVariables", "bool"), ("intVariables", "int"),
                     ("floatVariables", "float"),
                 }) {
            var array = fsm.Get("variables", arrayName, "Array");
            if (array == null) {
                continue;
            }

            foreach (var variable in array.Children) {
                var name = variable.Get("name")?.AsString;
                var value = variable.Get("value");
                if (string.IsNullOrEmpty(name) || value?.Value == null) {
                    continue;
                }

                variables[$"{type}:{name}"] = type switch {
                    "string" => value.AsString,
                    "bool" => value.AsBool.ToString(),
                    "int" => value.AsInt.ToString(),
                    _ => value.AsFloat.ToString("R"),
                };
            }
        }

        return variables;
    }

    /// <summary>
    /// Reads each state's enabled actions as full type names, its transitions and its actions' string parameters.
    /// </summary>
    private static List<StateData> ReadStates(AssetTypeValueField fsm) {
        var states = new List<StateData>();
        var stateArray = fsm.Get("states", "Array");
        if (stateArray == null) {
            return states;
        }

        foreach (var state in stateArray.Children) {
            var names = state.Get("actionData", "actionNames", "Array");
            var enabled = state.Get("actionData", "actionEnabled", "Array");
            var actions = new List<string>();
            for (var i = 0; names != null && i < names.Children.Count; i++) {
                if (enabled != null && i < enabled.Children.Count && !enabled.Children[i].AsBool) {
                    continue;
                }

                actions.Add(string.Intern(names.Children[i].AsString));
            }

            // Event parameters are serialized as their names in stringParams, FsmString values in fsmStringParams
            var strings = new List<string>();
            AddStrings(strings, state.Get("actionData", "stringParams", "Array"), null);
            AddStrings(strings, state.Get("actionData", "fsmStringParams", "Array"), "value");

            states.Add(new StateData(
                state.Get("name")?.AsString ?? "?",
                actions,
                ReadTransitions(state.Get("transitions", "Array")),
                strings
            ) {
                Params = ActionParams.Read(state.Get("actionData")),
                Structured = ActionParams.ReadStructured(state.Get("actionData"))
            });
        }

        return states;
    }

    private static List<string> ReadTransitions(AssetTypeValueField transitionArray) {
        var transitions = new List<string>();
        if (transitionArray == null) {
            return transitions;
        }

        foreach (var transition in transitionArray.Children) {
            var eventName = transition.Get("fsmEvent", "name")?.AsString ?? "?";
            var toState = transition.Get("toState")?.AsString ?? "?";
            transitions.Add(string.Intern($"{eventName}->{toState}"));
        }

        return transitions;
    }

    private static void AddStrings(List<string> strings, AssetTypeValueField array, string childName) {
        if (array == null) {
            return;
        }

        foreach (var item in array.Children) {
            var value = childName == null ? item.AsString : item.Get(childName)?.AsString;
            if (!string.IsNullOrWhiteSpace(value) && !strings.Contains(value)) {
                strings.Add(string.Intern(value));
            }
        }
    }

    private static void Classify(FileContext context, List<FsmRecord> records, EntityRegistryData registry, string kind) {
        var fsmNamesByObject = records
            .GroupBy(r => r.GameObjectPathId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>) g.Select(r => r.FsmName).ToList());

        RegistryEntry Match(GameObjectInfo gameObject) {
            var fsmNames = fsmNamesByObject.TryGetValue(gameObject.PathId, out var names) ? names : Array.Empty<string>();
            return registry.Match(gameObject.Name, fsmNames, context.ParentOf(gameObject)?.Name);
        }

        foreach (var record in records) {
            var gameObject = context.GetGameObject(record.GameObjectPathId);
            var ancestors = context.Ancestors(gameObject);
            record.ObjectName = gameObject?.Name ?? "?";
            record.ObjectPath = string.Join("/", ancestors.AsEnumerable().Reverse().Select(a => a.Name).Append(record.ObjectName));
            record.InArena = HasComponent(gameObject, "BattleScene") || ancestors.Any(a => HasComponent(a, "BattleScene"));

            // SSMP never registers corpses, see EntityManager.CollectEntityCandidates
            if (kind == "corpse" || IsCorpse(gameObject) || ancestors.Any(IsCorpse)) {
                record.Category = "corpse";
                continue;
            }

            var entry = gameObject == null ? null : Match(gameObject);
            if (entry != null) {
                record.Category = "entity";
                record.EntityType = entry.Type;
                continue;
            }

            var owner = ancestors.Select(a => (Object: a, Entry: Match(a))).FirstOrDefault(x => x.Entry != null);
            if (owner.Entry != null) {
                record.Category = "entity-child";
                record.EntityType = owner.Entry.Type;
                record.EntityObject = owner.Object.Name;
            } else if (HasComponent(gameObject, "HealthManager") || ancestors.Any(a => HasComponent(a, "HealthManager"))) {
                record.Category = "enemy-unregistered";
            } else if (record.ObjectPath.Contains("Hero_Hornet", StringComparison.Ordinal)) {
                record.Category = "hero";
            } else if ((gameObject?.IsRect ?? false) || ancestors.Any(a => a.IsRect)) {
                record.Category = "ui";
            } else if (kind == "scene") {
                var isNpc = record.ObjectName.Contains("NPC", StringComparison.Ordinal) ||
                            (gameObject?.ComponentClasses.Any(c => c.Contains("NPC", StringComparison.Ordinal)) ?? false);
                record.Category = isNpc ? "npc" : "scene";
            } else {
                record.Category = kind == "pool" ? "pool" : "other";
            }
        }
    }

    private static bool IsCorpse(GameObjectInfo gameObject) {
        return gameObject != null &&
               (gameObject.Name.StartsWith("corpse", StringComparison.OrdinalIgnoreCase) ||
                gameObject.ComponentClasses.Any(c => c is "Corpse" or "ActiveCorpse" or "CorpseItems"));
    }

    private static bool HasComponent(GameObjectInfo gameObject, string className) {
        return gameObject != null && gameObject.ComponentClasses.Contains(className);
    }
}

/// <summary>
/// Prints the serialized fields of every MonoBehaviour with a given script class, for looking up tuning values such
/// as ranges and durations that only exist in the assets.
/// </summary>
internal static class ComponentDumper {
    public static int MaxDepth = 6;
    public static int MaxArrayItems = 8;

    public static void Run(string bundleDir, string filter, string className, string fieldPattern) {
        var fieldRegex = fieldPattern == null ? null : new Regex(fieldPattern, RegexOptions.IgnoreCase);
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();

        var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Where(path => filter == null || path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal);
        var found = 0;
        foreach (var path in paths) {
            try {
                found += DumpBundle(manager, path, bundleDir, scriptNames, className, fieldRegex);
            } catch (Exception e) {
                Console.WriteLine($"error in {Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }
        }

        Console.WriteLine($"{found} {className} components");
    }

    private static int DumpBundle(
        AssetsManager manager,
        string path,
        string bundleDir,
        Dictionary<string, string> scriptNames,
        string className,
        Regex fieldRegex
    ) {
        var found = 0;
        var bundle = manager.LoadBundleFile(path, true);
        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
            if (!bundle.file.IsAssetsFile(i)) {
                continue;
            }

            var instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
            var monoBehaviours = instance.file.GetAssetsOfType(AssetClassID.MonoBehaviour);
            if (monoBehaviours.Count == 0) {
                continue;
            }

            instance.file.GenerateQuickLookup();
            var context = new FileContext(manager, instance, scriptNames);
            foreach (var info in monoBehaviours) {
                if (context.ScriptClass(info) != className) {
                    continue;
                }

                var root = manager.GetBaseField(instance, info);
                var owner = context.GetGameObject(root.Get("m_GameObject", "m_PathID")?.AsLong ?? 0);
                var name = owner?.Name ?? root.Get("m_Name")?.AsString ?? "?";
                Console.WriteLine($"== {Path.GetRelativePath(bundleDir, path)} : {name}");
                Print(root, "", fieldRegex, 0);
                found++;
            }
        }

        return found;
    }

    private static void Print(AssetTypeValueField field, string path, Regex fieldRegex, int depth) {
        var valueType = field.Value?.ValueType;
        if (valueType is not null and not AssetValueType.Array) {
            if (fieldRegex == null || fieldRegex.IsMatch(path)) {
                Console.WriteLine($"   {path} = {Format(field)}");
            }

            return;
        }

        if (depth >= MaxDepth) {
            return;
        }

        if (valueType == AssetValueType.Array) {
            var count = field.Children.Count;
            if (count > 0 && (fieldRegex == null || fieldRegex.IsMatch(path))) {
                Console.WriteLine($"   {path} (count {count})");
            }

            for (var i = 0; i < Math.Min(count, MaxArrayItems); i++) {
                Print(field.Children[i], $"{path}[{i}]", fieldRegex, depth + 1);
            }

            return;
        }

        foreach (var child in field.Children) {
            // Unity wraps every list in a field called "Array"; leave that level out of the path
            var childPath = child.FieldName == "Array" ? path
                : path.Length == 0 ? child.FieldName
                : $"{path}.{child.FieldName}";
            Print(child, childPath, fieldRegex, depth + 1);
        }
    }

    private static string Format(AssetTypeValueField field) {
        return field.Value.ValueType switch {
            AssetValueType.String => $"\"{field.AsString}\"",
            AssetValueType.Bool => field.AsBool.ToString(),
            AssetValueType.Float => field.AsFloat.ToString("R"),
            AssetValueType.Double => field.AsDouble.ToString("R"),
            AssetValueType.ByteArray => $"<{field.AsByteArray.Length} bytes>",
            _ => field.AsLong.ToString(),
        };
    }
}

/// <summary>
/// Decodes the serialized parameters of chosen FSM action types for --params, e.g. which events
/// CheckHeroPerformanceRegion sends for the inner and outer needolin range, and counts them per FSM category.
/// </summary>
internal static class ActionParams {
    // HutongGames.PlayMaker.ParamDataType values, read from PlayMaker.dll
    private enum ParamType {
        Integer = 0,
        Boolean = 1,
        Float = 2,
        String = 3,
        Enum = 7,
        FsmFloat = 15,
        FsmInt = 16,
        FsmBool = 17,
        FsmString = 18,
        FsmOwnerDefault = 20,
        FsmEvent = 23,
    }

    private const int MaxCombinations = 15;
    private const int MaxExamples = 4;
    private const int MaxTemplates = 6;

    public static Regex ActionPattern;
    public static Regex FieldPattern;

    // Limits the printed actions to FSMs with a matching name, and prefixes each combination with its state
    public static Regex FsmPattern;

    // Whether ReadStructured reads the parameters of every action, for --rooms
    public static bool ReadStructuredParams;

    /// <summary>
    /// The parameters of every enabled action of a state, when ReadStructuredParams is set.
    /// </summary>
    public static List<ActionParamsData> ReadStructured(AssetTypeValueField actionData) {
        var result = new List<ActionParamsData>();
        var names = actionData?.Get("actionNames", "Array");
        var enabled = actionData?.Get("actionEnabled", "Array");
        var starts = actionData?.Get("actionStartIndex", "Array");
        var fieldNames = actionData?.Get("paramName", "Array");
        var types = actionData?.Get("paramDataType", "Array");
        var positions = actionData?.Get("paramDataPos", "Array");
        if (!ReadStructuredParams || names == null || starts == null || fieldNames == null || types == null ||
            positions == null) {
            return result;
        }

        for (var i = 0; i < names.Children.Count && i < starts.Children.Count; i++) {
            if (enabled != null && i < enabled.Children.Count && !enabled.Children[i].AsBool) {
                continue;
            }

            var end = i + 1 < starts.Children.Count ? starts.Children[i + 1].AsInt : fieldNames.Children.Count;
            var parameters = new List<ParamValue>();
            for (var p = starts.Children[i].AsInt; p < end && p < fieldNames.Children.Count; p++) {
                var type = (ParamType) types.Children[p].AsInt;
                var position = positions.Children[p].AsInt;
                var value = type switch {
                    ParamType.FsmEvent or ParamType.String => Item(actionData, "stringParams", position)?.AsString,
                    ParamType.FsmString => Item(actionData, "fsmStringParams", position) is { } variable &&
                                           variable.Get("useVariable")?.AsBool != true
                        ? variable.Get("value")?.AsString
                        : null,
                    _ => Value(actionData, type, position, p),
                };
                string variableName = null;
                if (type == ParamType.FsmString) {
                    if (Item(actionData, "fsmStringParams", position) is { } stringVariable &&
                        stringVariable.Get("useVariable")?.AsBool == true) {
                        variableName = stringVariable.Get("name")?.AsString;
                    }
                } else if (type is not (ParamType.FsmEvent or ParamType.String) &&
                           value?.StartsWith("var ", StringComparison.Ordinal) == true) {
                    variableName = value[4..];
                }

                parameters.Add(new ParamValue(fieldNames.Children[p].AsString, (int) type, value, variableName));
            }

            result.Add(new ActionParamsData(names.Children[i].AsString, parameters));
        }

        return result;
    }

    /// <summary>
    /// One "Action: field=value, ..." line per enabled action matching ActionPattern. PlayMaker keeps the parameters
    /// of all actions in a state in shared arrays: paramName, paramDataType and paramDataPos from the action's
    /// actionStartIndex on, with each value at paramDataPos in the array for its type.
    /// </summary>
    public static List<string> Read(AssetTypeValueField actionData) {
        var result = new List<string>();
        var names = actionData?.Get("actionNames", "Array");
        var enabled = actionData?.Get("actionEnabled", "Array");
        var starts = actionData?.Get("actionStartIndex", "Array");
        var fieldNames = actionData?.Get("paramName", "Array");
        var types = actionData?.Get("paramDataType", "Array");
        var positions = actionData?.Get("paramDataPos", "Array");
        if (ActionPattern == null || names == null || starts == null || fieldNames == null || types == null ||
            positions == null) {
            return result;
        }

        for (var i = 0; i < names.Children.Count && i < starts.Children.Count; i++) {
            var actionName = names.Children[i].AsString;
            if (!ActionPattern.IsMatch(actionName) ||
                (enabled != null && i < enabled.Children.Count && !enabled.Children[i].AsBool)) {
                continue;
            }

            var end = i + 1 < starts.Children.Count ? starts.Children[i + 1].AsInt : fieldNames.Children.Count;
            var values = new List<string>();
            for (var p = starts.Children[i].AsInt; p < end && p < fieldNames.Children.Count; p++) {
                var fieldName = fieldNames.Children[p].AsString;
                if (FieldPattern == null || FieldPattern.IsMatch(fieldName)) {
                    var type = (ParamType) types.Children[p].AsInt;
                    values.Add($"{fieldName}={Value(actionData, type, positions.Children[p].AsInt, p)}");
                }
            }

            result.Add(string.Intern($"{ShortName(actionName)}: {string.Join(", ", values)}"));
        }

        return result;
    }

    /// <summary>
    /// Per FSM category, how many matching actions there are and their most common parameter combinations, with
    /// example entity types or object names.
    /// </summary>
    public static void Print(ScanResult scan) {
        var uses = scan.Fsms
            .Where(record => FsmPattern == null || FsmPattern.IsMatch(record.FsmName ?? ""))
            .SelectMany(record => record.States.SelectMany(state => state.Params.Select(line =>
                (Record: record, Line: FsmPattern == null ? line : $"[{state.Name}] {line}"))))
            .ToList();
        var maxCombinations = FsmPattern == null ? MaxCombinations : 200;
        foreach (var category in uses.GroupBy(use => use.Record.Category).OrderByDescending(group => group.Count())) {
            var objects = category.Select(use => use.Record.ObjectName).Distinct().Count();
            var combinations = category.GroupBy(use => use.Line).OrderByDescending(group => group.Count()).ToList();
            Console.WriteLine($"== {category.Key}: {category.Count()} actions on {objects} objects");
            foreach (var combination in combinations.Take(maxCombinations)) {
                var examples = combination.Select(use => use.Record.EntityType ?? use.Record.ObjectName).Distinct();
                Console.WriteLine(
                    $"  {combination.Count(),6}  {combination.Key}  e.g. {string.Join("; ", examples.Take(MaxExamples))}"
                );
            }

            if (combinations.Count > MaxCombinations) {
                Console.WriteLine($"  ... {combinations.Count - MaxCombinations} more combinations");
            }
        }

        // Sub-FSMs started with RunFSM come from templates that no FSM record points at, so print the whole templates
        // that use the actions, with each state's transitions and strings (which include animation clip names)
        foreach (var template in scan.Templates.Values.Where(t => t.States.Any(s => s.Params.Count > 0)).Take(MaxTemplates)) {
            Console.WriteLine($"== template {template.Name}: {template.States.Count} states");
            foreach (var state in template.States) {
                Console.WriteLine($"  [{state.Name}] {string.Join(",", state.Actions.Select(ShortName))}");
                if (state.Params.Count > 0) {
                    Console.WriteLine($"      params: {string.Join("; ", state.Params)}");
                }

                if (state.Transitions.Count > 0) {
                    Console.WriteLine($"      transitions: {string.Join("; ", state.Transitions)}");
                }

                if (state.Strings.Count > 0) {
                    Console.WriteLine($"      strings: {string.Join("; ", state.Strings)}");
                }
            }
        }
    }

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    /// <summary>
    /// The key of a variable in FsmRecord.Variables for a parameter of a type, or null for types without one.
    /// </summary>
    public static string VariableKey(int type, string name) => (ParamType) type switch {
        ParamType.FsmString => $"string:{name}",
        ParamType.FsmBool => $"bool:{name}",
        ParamType.FsmInt => $"int:{name}",
        ParamType.FsmFloat => $"float:{name}",
        _ => null,
    };

    private static string Value(AssetTypeValueField actionData, ParamType type, int position, int paramIndex = -1) {
        switch (type) {
            case ParamType.FsmEvent:
            case ParamType.String:
                return Quote(Item(actionData, "stringParams", position)?.AsString);
            case ParamType.FsmString:
                return Variable(Item(actionData, "fsmStringParams", position), value => Quote(value.AsString));
            case ParamType.FsmBool:
                return Item(actionData, "fsmBoolParams", position) is { } fsmBool
                    ? Variable(fsmBool, value => value.AsBool.ToString())
                    : OldFsmBool(actionData, position, paramIndex);
            case ParamType.FsmFloat:
                return Variable(Item(actionData, "fsmFloatParams", position), value => value.AsFloat.ToString("R"));
            case ParamType.FsmInt:
                return Variable(Item(actionData, "fsmIntParams", position), value => value.AsInt.ToString());
            case ParamType.FsmOwnerDefault:
                // OwnerDefaultOption: 0 is UseOwner, 1 is SpecifyGameObject
                return Item(actionData, "fsmOwnerDefaultParams", position)?.Get("ownerOption")?.AsInt switch {
                    null => "<missing>",
                    0 => "owner",
                    _ => "gameObject",
                };
            case ParamType.Boolean:
                return Bytes(actionData, position, 1) is { } flag ? (flag[0] != 0).ToString() : "<missing>";
            case ParamType.Float:
                return Bytes(actionData, position, 4) is { } single ? BitConverter.ToSingle(single).ToString("R") : "<missing>";
            case ParamType.Integer:
            case ParamType.Enum:
                return Bytes(actionData, position, 4) is { } number ? BitConverter.ToInt32(number).ToString() : "<missing>";
            case (ParamType) 19:
                return Variable(Item(actionData, "fsmGameObjectParams", position), _ => "<object>");
            case (ParamType) 28:
                return Variable(Item(actionData, "fsmVector3Params", position), value =>
                    $"({value.Get("x")?.AsFloat:R}, {value.Get("y")?.AsFloat:R}, {value.Get("z")?.AsFloat:R})");
            case (ParamType) 37:
                return Variable(Item(actionData, "fsmVector2Params", position), value =>
                    $"({value.Get("x")?.AsFloat:R}, {value.Get("y")?.AsFloat:R})");
            case (ParamType) 21:
                // FunctionCall, as used by SendMessage, is serialized whole in functionCallParams
                var functionCall = Item(actionData, "functionCallParams", position);
                return functionCall == null
                    ? "<missing>"
                    : $"{Quote(functionCall.Get("FunctionName")?.AsString)}({Quote(functionCall.Get("parameterType")?.AsString)})";
            case (ParamType) 39:
                return FsmVar(Item(actionData, "fsmVarParams", position));
            default:
                return $"<type {(int) type}>";
        }
    }

    private static AssetTypeValueField Item(AssetTypeValueField actionData, string arrayName, int position) {
        var array = actionData.Get(arrayName, "Array");
        return array != null && position >= 0 && position < array.Children.Count ? array.Children[position] : null;
    }

    private static string Variable(AssetTypeValueField variable, Func<AssetTypeValueField, string> format) {
        if (variable == null) {
            return "<missing>";
        }

        var name = variable.Get("name")?.AsString;
        if (variable.Get("useVariable")?.AsBool == true && !string.IsNullOrEmpty(name)) {
            return $"var {name}";
        }

        var value = variable.Get("value");
        return value == null ? "<missing>" : format(value);
    }

    /// <summary>
    /// Reads an FsmBool that PlayMaker saved in byteData, as it did before fsmBoolParams: a byte for the value, a byte
    /// for whether a variable fills it in, and the name of the variable in the rest of the parameter's bytes.
    /// </summary>
    private static string OldFsmBool(AssetTypeValueField actionData, int position, int paramIndex) {
        if (Bytes(actionData, position, 2) is not { } flags) {
            return "<missing>";
        }

        if (flags[1] == 0) {
            return (flags[0] != 0).ToString();
        }

        var sizes = actionData.Get("paramByteDataSize", "Array");
        var size = sizes != null && paramIndex >= 0 && paramIndex < sizes.Children.Count
            ? sizes.Children[paramIndex].AsInt
            : 0;
        return size > 2 && Bytes(actionData, position + 2, size - 2) is { } name
            ? $"var {System.Text.Encoding.UTF8.GetString(name)}"
            : "<missing>";
    }

    /// <summary>
    /// Reads an FsmVar, as SetPlayerDataVariable uses: a value of each type and which of them it holds.
    /// </summary>
    private static string FsmVar(AssetTypeValueField fsmVar) {
        if (fsmVar == null) {
            return "<missing>";
        }

        var name = fsmVar.Get("variableName")?.AsString;
        if (fsmVar.Get("useVariable")?.AsBool == true && !string.IsNullOrEmpty(name)) {
            return $"var {name}";
        }

        // VariableType: 0 is Float, 1 is Int, 2 is Bool and 4 is String
        return fsmVar.Get("type")?.AsInt switch {
            0 => fsmVar.Get("floatValue")?.AsFloat.ToString("R") ?? "<missing>",
            1 => fsmVar.Get("intValue")?.AsInt.ToString() ?? "<missing>",
            2 => fsmVar.Get("boolValue")?.AsBool.ToString() ?? "<missing>",
            4 => Quote(fsmVar.Get("stringValue")?.AsString),
            null => "<missing>",
            var other => $"<variable type {other}>",
        };
    }

    private static byte[] Bytes(AssetTypeValueField actionData, int position, int count) {
        var array = actionData.Get("byteData", "Array");
        if (array == null) {
            return null;
        }

        var bytes = array.Value?.ValueType == AssetValueType.ByteArray
            ? array.AsByteArray
            : array.Children.Select(child => (byte) child.AsInt).ToArray();
        return position >= 0 && position + count <= bytes.Length ? bytes[position..(position + count)] : null;
    }

    private static string Quote(string value) => value == null ? "<missing>" : $"\"{value}\"";
}

internal sealed class ActionUse {
    public ActionGap Action;
    public readonly List<string> States = new();
}

internal sealed class RecordAnalysis {
    public FsmRecord Record;
    public List<ActionUse> Uses;

    public IEnumerable<ActionUse> Gaps => Uses.Where(u => u.Action.IsGap);

    public bool HasGap => Uses.Any(u => u.Action.IsGap);

    public bool HasTag(string tag) => Uses.Any(u => u.Action.Tags.Contains(tag));
}

internal sealed class Analysis {
    public static readonly (string Key, string Label)[] Categories = [
        ("entity", "SSMP 登记的实体"),
        ("entity-child", "登记实体的子物体（自己没登记）"),
        ("enemy-unregistered", "有 HealthManager 但没登记"),
        ("scene", "场景物体（机关/道具/环境）"),
        ("npc", "NPC"),
        ("pool", "对象池预制体（子弹/特效/召唤物）"),
        ("corpse", "敌人尸体"),
        ("hero", "主角"),
        ("ui", "UI"),
        ("other", "其他预制体"),
    ];

    public readonly List<RecordAnalysis> Records = new();
    public readonly HashSet<string> UnknownActions = new(StringComparer.Ordinal);

    public static Analysis Build(ScanResult scan, AuditData audit) {
        var analysis = new Analysis();
        foreach (var record in scan.Fsms) {
            var uses = new Dictionary<ActionGap, ActionUse>();
            foreach (var state in record.States) {
                foreach (var name in state.Actions) {
                    var action = audit.Find(name);
                    if (action == null) {
                        analysis.UnknownActions.Add(name);
                        continue;
                    }

                    if (action.Tags.Count == 0) {
                        continue;
                    }

                    if (!uses.TryGetValue(action, out var use)) {
                        use = new ActionUse { Action = action };
                        uses[action] = use;
                    }

                    if (!use.States.Contains(state.Name)) {
                        use.States.Add(state.Name);
                    }
                }
            }

            analysis.Records.Add(new RecordAnalysis {
                Record = record,
                Uses = uses.Values.OrderBy(u => u.Action.Name, StringComparer.Ordinal).ToList(),
            });
        }

        return analysis;
    }
}

internal static class ReportWriter {
    private static readonly string[] TagOrder = ["A", "B", "C1", "C2", "D", "E", "L"];

    private static readonly Regex InstanceSuffix = new(@"\s*\((Clone|\d+)\)", RegexOptions.Compiled);

    public static string Markdown(Analysis analysis, ScanResult scan, AuditData audit, EntityRegistryData registry, string bundleDir) {
        var sb = new StringBuilder();
        var kinds = scan.Fsms.GroupBy(r => r.BundleKind).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
        sb.AppendLine("# 状态机缺口的实际使用者");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- SSMP：`{audit.SsmpBranch}` @ `{audit.SsmpCommit}`（动作的缺口标签来自 `reports/sync-audit.md`）");
        sb.AppendLine($"- 资源包目录：`{bundleDir}`");
        sb.AppendLine($"- 扫描 {scan.BundlesScanned} 个资源包，跳过 {scan.BundlesSkipped} 个纯贴图/音频/字体包；读到 {scan.Fsms.Count} 个状态机组件（按包类型：{string.Join("、", kinds)}）和 {scan.Templates.Count} 个状态机模板");
        sb.AppendLine($"- {scan.TemplateAttributions} 个组件用了模板，按模板内容统计（{scan.UnresolvedTemplates} 个模板没找到）；{scan.UnresolvedScripts} 个 MonoBehaviour 的脚本类没解析出来；{analysis.UnknownActions.Count} 种动作名在审计里找不到");
        sb.AppendLine("- 重新生成：先跑 `tools/SyncAudit`，再跑 `dotnet run -c Release --project tools/FsmScan`");
        sb.AppendLine();
        sb.AppendLine("> 标签：**A** 效果到不了其他玩家（生成物体/音效/粒子/镜头/操作主角/时间缩放），**B** 只认主机的大黄蜂，**C1** 随机结果没同步且没处理函数，**C2** 有处理函数但随机数没拦截，**D** SSMP 只处理了同名动作的另一个版本。");
        sb.AppendLine("> **E** 已处理，但只在进入状态时同步一次、之后每帧持续生效；**L** 没处理，但效果可能只落在实体自己身上（移动/动画/开关/血量），也许被组件同步兜住。E 和 L 不算缺口，只是值得留意。");
        sb.AppendLine("> 「登记的实体」按 SSMP 自己的规则判断（物体名包含登记名、状态机名、父物体名），尸体一律排除。NPC、UI、主角是按名字和 RectTransform 猜的。运行时才生成或改名的物体这里看不到。");
        sb.AppendLine();

        sb.AppendLine("## 总览");
        sb.AppendLine();
        sb.AppendLine("每格是「用到该标签动作的状态机组件数」，一个组件可以同时算进多个标签。");
        sb.AppendLine();
        sb.AppendLine($"| 类别 | 状态机组件 | 用到 A–D 的 | {string.Join(" | ", TagOrder)} |");
        sb.AppendLine($"|---|---|---|{string.Concat(Enumerable.Repeat("---|", TagOrder.Length))}");
        foreach (var (key, label) in Analysis.Categories) {
            var records = analysis.Records.Where(r => r.Record.Category == key).ToList();
            var counts = TagOrder.Select(tag => records.Count(r => r.HasTag(tag)));
            sb.AppendLine($"| {label} | {records.Count} | {records.Count(r => r.HasGap)} | {string.Join(" | ", counts)} |");
        }

        sb.AppendLine();
        sb.AppendLine("只有「SSMP 登记的实体」这一类是主机运行、其他玩家靠转发，A–D 标签在这里就是真正的同步缺口。其他类别的状态机在每台电脑上各自运行：音效、镜头这些效果会在本地照常发生，取主角（B）拿到的是本地的大黄蜂（伤害判定这样反而是对的），问题变成各自的状态和随机结果对不上，那是世界同步要解决的事。");
        sb.AppendLine();
        AppendEffects(sb, analysis);
        AppendEntities(sb, analysis, registry);
        AppendEntityChildren(sb, analysis);
        AppendUnregisteredEnemies(sb, analysis);
        AppendGapRanking(sb, analysis, audit);
        AppendEntityWatchList(sb, analysis);
        AppendEntityScenes(sb, analysis);

        sb.AppendLine("## 附：抽查被跳过的资源包组");
        sb.AppendLine();
        sb.AppendLine("每组抽最小和最大的包实际读一遍，确认里面没有状态机。");
        sb.AppendLine();
        foreach (var sample in scan.SkippedGroupSamples) {
            sb.AppendLine($"- {sample}");
        }

        sb.AppendLine();
        if (analysis.UnknownActions.Count > 0) {
            sb.AppendLine($"## 附：审计里找不到的动作名（{analysis.UnknownActions.Count}）");
            sb.AppendLine();
            sb.AppendLine(string.Join("、", analysis.UnknownActions.OrderBy(n => n, StringComparer.Ordinal).Take(100).Select(n => $"`{n}`")));
            sb.AppendLine();
        }

        if (scan.Errors.Count > 0) {
            sb.AppendLine($"## 附：读取失败的资源包（{scan.Errors.Count}）");
            sb.AppendLine();
            foreach (var error in scan.Errors.Take(50)) {
                sb.AppendLine($"- `{error}`");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static readonly (string Effect, string Label)[] EffectLabels = [
        ("Spawn", "生成物体"),
        ("Activate", "开关物体/渲染器"),
        ("HeroRef", "取主角实例"),
        ("HeroCall", "调用主角方法"),
        ("Audio", "音效"),
        ("Particles", "粒子"),
        ("Camera", "镜头"),
        ("TimeScale", "时间缩放"),
        ("Motion", "移动/物理"),
        ("Animation", "动画"),
        ("Health", "血量"),
        ("Random", "随机数"),
    ];

    private static void AppendEffects(StringBuilder sb, Analysis analysis) {
        sb.AppendLine("## 按效果看 A–D 动作影响什么");
        sb.AppendLine();
        sb.AppendLine("每格是「用到带这种效果的 A–D 动作的状态机组件数」。效果是从动作代码调用的 API 推出来的，只读查询（比如判断主角状态）也会算进「调用主角方法」。");
        sb.AppendLine();
        sb.AppendLine($"| 效果 | {string.Join(" | ", Analysis.Categories.Select(c => ShortLabel(c.Key)))} |");
        sb.AppendLine($"|---|{string.Concat(Enumerable.Repeat("---|", Analysis.Categories.Length))}");
        foreach (var (effect, label) in EffectLabels) {
            var counts = Analysis.Categories.Select(c => analysis.Records.Count(r =>
                r.Record.Category == c.Key && r.Gaps.Any(u => u.Action.Effects.Contains(effect))
            ));
            sb.AppendLine($"| {label} | {string.Join(" | ", counts)} |");
        }

        sb.AppendLine();
    }

    private static void AppendEntities(StringBuilder sb, Analysis analysis, EntityRegistryData registry) {
        var groups = analysis.Records
            .Where(r => r.Record.Category == "entity")
            .GroupBy(r => r.Record.EntityType)
            .Select(g => new {
                Type = g.Key,
                Count = g.Count(),
                Places = Places(g.Select(r => Place(r.Record))),
                Gaps = DistinctActions(g.SelectMany(r => r.Gaps)),
                Watch = DistinctActions(g.SelectMany(r => r.Uses.Where(u => !u.Action.IsGap))),
            })
            .OrderByDescending(g => g.Gaps.Count)
            .ThenByDescending(g => g.Count)
            .ToList();

        sb.AppendLine($"## 1. 登记实体自己的状态机（{groups.Count(g => g.Gaps.Count > 0)}/{groups.Count} 种实体用到 A–D）");
        sb.AppendLine();
        sb.AppendLine("非主机玩家那边这些状态机是关掉的，只靠主机转发，所以这里的每个 A–D 动作都直接影响敌人在其他玩家眼里的表现。");
        sb.AppendLine();
        sb.AppendLine("| 实体类型 | 组件数 | 出现位置 | A–D 动作 | 留意（E/L） |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var group in groups) {
            sb.AppendLine($"| {group.Type} | {group.Count} | {Escape(group.Places)} | {Tagged(group.Gaps)} | {Plain(group.Watch, 8)} |");
        }

        var found = groups.Select(g => g.Type).ToHashSet();
        var missing = registry.Entries.Select(e => e.Type).Distinct().Where(t => !found.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        sb.AppendLine();
        sb.AppendLine($"登记了、但资源里没匹配到状态机的实体类型（{missing.Count}，可能是运行时生成、改名，或者没有状态机）：{(missing.Count == 0 ? "（无）" : string.Join("、", missing))}");
        sb.AppendLine();
    }

    private static void AppendEntityChildren(StringBuilder sb, Analysis analysis) {
        var groups = analysis.Records
            .Where(r => r.Record.Category == "entity-child" && r.HasGap)
            .GroupBy(r => (r.Record.EntityType, Object: InstanceSuffix.Replace(r.Record.ObjectName, ""), r.Record.FsmName))
            .Select(g => new {
                g.Key.EntityType,
                g.Key.Object,
                g.Key.FsmName,
                Count = g.Count(),
                Gaps = DistinctActions(g.SelectMany(r => r.Gaps)),
            })
            .OrderByDescending(g => g.Gaps.Count)
            .ThenByDescending(g => g.Count)
            .ToList();
        var total = analysis.Records.Count(r => r.Record.Category == "entity-child");

        sb.AppendLine($"## 2. 登记实体的子物体上、自己没登记的状态机（{total} 个组件，{groups.Count} 组用到 A–D）");
        sb.AppendLine();
        sb.AppendLine("SSMP 只接管实体物体本身的状态机（`GetComponents<PlayMakerFSM>`）。客户端那份是整个物体克隆出来的，子物体上的状态机在每台电脑上各自运行，里面的动作会在本地发生。要看的是触发它们的事件（通常由实体的状态机发出）在客户端有没有被转发，以及它们自己计时、随机的部分会不会和主机分叉。");
        sb.AppendLine();
        sb.AppendLine("| 所属实体 | 子物体 | 状态机 | 组件数 | A–D 动作 |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var group in groups.Take(60)) {
            sb.AppendLine($"| {group.EntityType} | {Escape(group.Object)} | {Escape(group.FsmName)} | {group.Count} | {Tagged(group.Gaps)} |");
        }

        sb.AppendLine();
    }

    private static void AppendUnregisteredEnemies(StringBuilder sb, Analysis analysis) {
        var groups = analysis.Records
            .Where(r => r.Record.Category == "enemy-unregistered")
            .GroupBy(r => InstanceSuffix.Replace(r.Record.ObjectName, ""))
            .Select(g => new {
                Object = g.Key,
                Count = g.Count(),
                Places = Places(g.Select(r => Place(r.Record))),
                Fsms = g.Select(r => r.Record.FsmName).Distinct().ToList(),
                Gaps = DistinctActions(g.SelectMany(r => r.Gaps)),
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        sb.AppendLine($"## 3. 有 HealthManager 但没登记的物体（{groups.Count} 种）");
        sb.AppendLine();
        sb.AppendLine("可能是漏登记的敌人，也可能是能打坏的场景物件。没登记的物体完全不同步，每个玩家看到的是自己电脑上的一份。");
        sb.AppendLine();
        sb.AppendLine("| 物体 | 组件数 | 出现位置 | 状态机 | A–D 动作 |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var group in groups.Take(80)) {
            sb.AppendLine($"| {Escape(group.Object)} | {group.Count} | {Escape(group.Places)} | {Escape(string.Join("、", group.Fsms.Take(4)))} | {Tagged(group.Gaps)} |");
        }

        sb.AppendLine();
    }

    private static void AppendGapRanking(StringBuilder sb, Analysis analysis, AuditData audit) {
        var rows = analysis.Records
            .SelectMany(r => r.Gaps.Select(u => (r.Record, Use: u)))
            .GroupBy(x => x.Use.Action)
            .Select(g => new {
                Action = g.Key,
                Total = g.Count(),
                ByCategory = Analysis.Categories.Select(c => g.Count(x => x.Record.Category == c.Key)).ToList(),
                OnEntities = g.Count(x => x.Record.Category is "entity" or "entity-child"),
                Example = Example(g),
            })
            .OrderByDescending(x => x.OnEntities)
            .ThenByDescending(x => x.Total)
            .ToList();

        sb.AppendLine($"## 4. A–D 动作使用排行（{rows.Count} 种被实际用到）");
        sb.AppendLine();
        sb.AppendLine("按「登记实体 + 实体子物体」的组件数排序，数字是用到该动作的状态机组件数。");
        sb.AppendLine();
        sb.AppendLine($"| 动作 | 标签 | 变体来源 | 合计 | {string.Join(" | ", Analysis.Categories.Select(c => ShortLabel(c.Key)))} | 例子（实体/物体 · 状态机 · 状态） |");
        sb.AppendLine($"|---|---|---|---|{string.Concat(Enumerable.Repeat("---|", Analysis.Categories.Length))}---|");
        foreach (var row in rows) {
            var variantOf = string.Join("、", row.Action.VariantOf.Select(v => $"`{v}`"));
            sb.AppendLine($"| `{row.Action.Name}` | {row.Action.TagText} | {variantOf} | {row.Total} | {string.Join(" | ", row.ByCategory)} | {Escape(row.Example)} |");
        }

        var used = rows.Select(r => r.Action).ToHashSet();
        var unused = audit.Actions.Where(a => a.IsGap && !used.Contains(a)).Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        sb.AppendLine();
        sb.AppendLine($"资源里没有任何状态机用到的 A–D 动作（{unused.Count}，可以忽略）：{(unused.Count == 0 ? "（无）" : string.Join("、", unused.Select(n => $"`{n}`")))}");
        sb.AppendLine();
    }

    private static void AppendEntityWatchList(StringBuilder sb, Analysis analysis) {
        var rows = analysis.Records
            .Where(r => r.Record.Category == "entity")
            .SelectMany(r => r.Uses.Where(u => !u.Action.IsGap).Select(u => (r.Record, Use: u)))
            .GroupBy(x => x.Use.Action)
            .Select(g => new {
                Action = g.Key,
                Count = g.Count(),
                Types = g.Select(x => x.Record.EntityType).Distinct().Count(),
                Example = Example(g),
            })
            .OrderByDescending(x => x.Types)
            .ThenByDescending(x => x.Count)
            .ToList();

        sb.AppendLine($"## 5. 登记实体用到的 E/L 动作（{rows.Count} 种）");
        sb.AppendLine();
        sb.AppendLine("不算确定的缺口，但修的时候要确认：L 是没处理的动作（客户端那边不会执行），E 是只同步了进入状态那一下。");
        sb.AppendLine();
        sb.AppendLine("| 动作 | 标签 | 效果 | 实体种类 | 组件数 | 例子（实体 · 状态机 · 状态） |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var row in rows.Take(60)) {
            sb.AppendLine($"| `{row.Action.Name}` | {row.Action.TagText} | {string.Join("/", row.Action.Effects)} | {row.Types} | {row.Count} | {Escape(row.Example)} |");
        }

        sb.AppendLine();
    }

    private static void AppendEntityScenes(StringBuilder sb, Analysis analysis) {
        var groups = analysis.Records
            .Where(r => r.Record.InEntityScene && r.HasGap)
            .GroupBy(r => r.Record.Scene)
            .Select(g => new {
                Scene = g.Key,
                Count = g.Count(),
                Examples = g.OrderByDescending(r => r.Gaps.Count())
                    .Take(3)
                    .Select(r => $"{InstanceSuffix.Replace(r.Record.ObjectName, "")}·{r.Record.FsmName}（{string.Join("、", r.Gaps.Take(3).Select(u => u.Action.Name))}）")
                    .ToList(),
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        sb.AppendLine($"## 6. 有登记实体的场景里、用到 A–D 动作的场景物体（{groups.Count} 个场景）");
        sb.AppendLine();
        sb.AppendLine("Boss 房的机关、门、会掉下来的东西通常在这里。它们不是实体，每个玩家电脑上各跑各的。");
        sb.AppendLine();
        sb.AppendLine("| 场景 | 组件数 | 用到缺口最多的几个 |");
        sb.AppendLine("|---|---|---|");
        foreach (var group in groups.Take(60)) {
            sb.AppendLine($"| {group.Scene} | {group.Count} | {Escape(string.Join("；", group.Examples))} |");
        }

        sb.AppendLine();
    }

    public static string Json(Analysis analysis) {
        var payload = new {
            fsms = analysis.Records
                .Where(r => r.Uses.Count > 0)
                .Select(r => new {
                    bundle = r.Record.Bundle,
                    category = r.Record.Category,
                    entityType = r.Record.EntityType,
                    entityObject = r.Record.EntityObject,
                    path = r.Record.ObjectPath,
                    fsm = r.Record.FsmName,
                    template = r.Record.TemplateName,
                    actions = r.Uses.Select(u => new { name = u.Action.Name, tags = u.Action.Tags, states = u.States }),
                }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>
    /// Writes one tab-separated line per FSM state, plus one per FSM for its global transitions, so events, states
    /// and actions across all FSMs can be searched with grep.
    /// </summary>
    public static void WriteIndex(Analysis analysis, string path) {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("category\tentity\tplace\tobject\tfsm\tstate\tactions\ttransitions\tstrings");
        foreach (var record in analysis.Records.Select(r => r.Record)) {
            var prefix = string.Join(
                "\t",
                record.Category,
                record.EntityType ?? "",
                Place(record),
                Clean(record.ObjectPath),
                Clean(record.FsmName)
            );
            if (record.GlobalTransitions is { Count: > 0 }) {
                writer.WriteLine($"{prefix}\t(global)\t\t{Clean(string.Join("; ", record.GlobalTransitions))}\t");
            }

            foreach (var state in record.States) {
                var actions = string.Join(",", state.Actions.Select(a => a[(a.LastIndexOfAny(['.', '+']) + 1)..]));
                writer.WriteLine(
                    $"{prefix}\t{Clean(state.Name)}\t{actions}\t{Clean(string.Join("; ", state.Transitions))}\t" +
                    Clean(string.Join("; ", state.Strings))
                );
            }
        }
    }

    private static string Clean(string text) => text?.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ') ?? "";

    public static void PrintSummary(Analysis analysis, ScanResult scan) {
        Console.WriteLine(
            $"Templates used: {scan.TemplateAttributions} (unresolved {scan.UnresolvedTemplates}); unresolved scripts: " +
            $"{scan.UnresolvedScripts}; action names missing from the audit: {analysis.UnknownActions.Count}"
        );
        foreach (var (key, _) in Analysis.Categories) {
            var records = analysis.Records.Where(r => r.Record.Category == key).ToList();
            Console.WriteLine($"  {key,-20} {records.Count,6} FSMs, {records.Count(r => r.HasGap),5} use A-D actions");
        }

        var topEntities = analysis.Records
            .Where(r => r.Record.Category == "entity")
            .GroupBy(r => r.Record.EntityType)
            .Select(g => (Type: g.Key, Gaps: DistinctActions(g.SelectMany(r => r.Gaps)).Count))
            .OrderByDescending(x => x.Gaps)
            .Take(10);
        Console.WriteLine("Entities with the most distinct A-D actions: " + string.Join(", ", topEntities.Select(x => $"{x.Type}({x.Gaps})")));
        foreach (var error in scan.Errors.Take(5)) {
            Console.WriteLine($"  error: {error}");
        }
    }

    private static List<ActionGap> DistinctActions(IEnumerable<ActionUse> uses) {
        return uses.Select(u => u.Action).Distinct().OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
    }

    private static string Example(IEnumerable<(FsmRecord Record, ActionUse Use)> uses) {
        var (record, use) = uses
            .OrderBy(x => x.Record.Category switch { "entity" => 0, "entity-child" => 1, "enemy-unregistered" => 2, _ => 3 })
            .First();
        var owner = record.EntityType ?? InstanceSuffix.Replace(record.ObjectName, "");
        return $"{owner} · {record.FsmName} · {use.States[0]}";
    }

    private static string Place(FsmRecord record) => record.Scene ?? Path.GetFileNameWithoutExtension(record.Bundle);

    private static string Places(IEnumerable<string> places) {
        var list = places.Distinct().ToList();
        var shown = string.Join("、", list.Take(4));
        return list.Count > 4 ? $"{shown} 等 {list.Count} 处" : shown;
    }

    private static string Tagged(List<ActionGap> actions) {
        return string.Join("、", actions.Select(a => $"`{a.Name}`<sub>{a.TagText}</sub>"));
    }

    private static string Plain(List<ActionGap> actions, int max) {
        var shown = string.Join("、", actions.Take(max).Select(a => $"`{a.Name}`"));
        return actions.Count > max ? $"{shown} 等 {actions.Count} 个" : shown;
    }

    private static string ShortLabel(string key) => key switch {
        "entity" => "实体",
        "entity-child" => "实体子物体",
        "enemy-unregistered" => "未登记敌人",
        "scene" => "场景",
        "npc" => "NPC",
        "pool" => "对象池",
        "corpse" => "尸体",
        "hero" => "主角",
        "ui" => "UI",
        _ => "其他",
    };

    private static string Escape(string text) => text?.Replace("|", "\\|") ?? "";
}
