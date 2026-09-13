using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// SyncAudit: cross-references the PlayMaker FSM action types in the game's assemblies with what SSMP's entity
// sync handles, and lists actions whose effects would not reach other players, that only look at the host's hero,
// or whose random results are not networked. Read-only on both inputs; writes a Markdown and a JSON report.
//
// Usage: dotnet run -- [--game <Managed dir>] [--ssmp <SSMP repo root>] [--out <report.md>]

var options = Cli.Parse(args);
var gameDir = options.GetValueOrDefault(
    "game",
    @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\Managed"
);
var ssmpDir = options.GetValueOrDefault("ssmp", @"D:\programming\silksong_mod\SSMP");
var outPath = options.GetValueOrDefault("out", @"D:\programming\silksong_mod\reports\sync-audit.md");

var game = GameIndex.Load(gameDir);
var ssmp = SsmpCoverage.Load(ssmpDir);
var rows = Audit.BuildRows(game, ssmp);
var allTypeNames = new HashSet<string>(game.All.Select(t => t.SimpleName));

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
File.WriteAllText(outPath, ReportWriter.Markdown(rows, ssmp, gameDir, allTypeNames), new UTF8Encoding(false));
var jsonPath = Path.ChangeExtension(outPath, ".json");
File.WriteAllText(jsonPath, ReportWriter.Json(rows, ssmp), new UTF8Encoding(false));

ReportWriter.PrintSummary(rows, ssmp);
Console.WriteLine($"Markdown: {outPath}");
Console.WriteLine($"JSON:     {jsonPath}");

/// <summary>
/// Effects an FSM action can have, derived from the methods its code calls.
/// </summary>
[Flags]
internal enum Effect {
    None = 0,
    Random = 1 << 0,
    HeroRef = 1 << 1,
    HeroCall = 1 << 2,
    Spawn = 1 << 3,
    Audio = 1 << 4,
    Particles = 1 << 5,
    Activate = 1 << 6,
    Animation = 1 << 7,
    Motion = 1 << 8,
    Health = 1 << 9,
    Camera = 1 << 10,
    TimeScale = 1 << 11,
    FrameTimer = 1 << 12,
}

internal static class Cli {
    public static Dictionary<string, string> Parse(string[] args) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i++) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) {
                continue;
            }

            result[args[i][2..]] = args[i + 1];
            i++;
        }

        return result;
    }
}

internal sealed record CallTarget(string Ns, string Type, string Method);

internal sealed class TypeInfo {
    public string Assembly = "";
    public string FullName = "";
    public string SimpleName = "";
    public string BaseFullName;
    public bool IsAbstract;
    public bool IsNested;
    public string OuterFullName = "";
    public string DeclaringFullName;
    public readonly HashSet<string> Fields = new();
    public Effect OwnEffects;
    public readonly Dictionary<Effect, SortedSet<string>> Evidence = new();
}

/// <summary>
/// Index of all types in the game's own assemblies, with the effects of the calls made by their methods.
/// </summary>
internal sealed class GameIndex {
    public readonly List<TypeInfo> All = new();
    public const string FsmStateActionName = "HutongGames.PlayMaker.FsmStateAction";

    private readonly Dictionary<string, TypeInfo> _byFullName = new();
    private readonly Dictionary<string, List<TypeInfo>> _nestedByDeclaring = new();

    public static GameIndex Load(string dir) {
        var index = new GameIndex();
        var files = Directory.GetFiles(dir, "*.dll")
            .Where(f => {
                var name = Path.GetFileName(f);
                return name.StartsWith("Assembly-CSharp", StringComparison.Ordinal) ||
                       name.StartsWith("PlayMaker", StringComparison.Ordinal) ||
                       name.StartsWith("TeamCherry", StringComparison.Ordinal);
            })
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var file in files) {
            using var fs = File.OpenRead(file);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) {
                continue;
            }

            var md = pe.GetMetadataReader();
            var assembly = Path.GetFileName(file);

            foreach (var th in md.TypeDefinitions) {
                var td = md.GetTypeDefinition(th);
                var outerFullName = Meta.FullTypeName(md, Meta.Outermost(md, th));
                var outerSimpleName = Meta.SimpleOf(outerFullName);
                var info = new TypeInfo {
                    Assembly = assembly,
                    SimpleName = md.GetString(td.Name),
                    FullName = Meta.FullTypeName(md, th),
                    BaseFullName = SafeBaseFullName(md, td),
                    IsAbstract = (td.Attributes & TypeAttributes.Abstract) != 0,
                    IsNested = !td.GetDeclaringType().IsNil,
                    OuterFullName = outerFullName,
                    DeclaringFullName = td.GetDeclaringType().IsNil ? null : Meta.FullTypeName(md, td.GetDeclaringType()),
                };

                foreach (var fh in td.GetFields()) {
                    info.Fields.Add(md.GetString(md.GetFieldDefinition(fh).Name));
                }

                foreach (var mh in td.GetMethods()) {
                    var method = md.GetMethodDefinition(mh);
                    if (method.RelativeVirtualAddress == 0) {
                        continue;
                    }

                    byte[] il;
                    try {
                        il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                    } catch {
                        continue;
                    }

                    if (il == null) {
                        continue;
                    }

                    var methodName = md.GetString(method.Name);
                    foreach (var target in Meta.CallTargets(md, il)) {
                        // Calls into the type's own methods say nothing about what it touches
                        if (target.Type == info.SimpleName || target.Type == outerSimpleName) {
                            continue;
                        }

                        var effect = Classifier.Classify(target, methodName);
                        if (effect == Effect.None) {
                            continue;
                        }

                        info.OwnEffects |= effect;
                        foreach (var flag in Classifier.Flags(effect)) {
                            if (!info.Evidence.TryGetValue(flag, out var set)) {
                                set = new SortedSet<string>(StringComparer.Ordinal);
                                info.Evidence[flag] = set;
                            }

                            set.Add($"{target.Type}::{target.Method}");
                        }
                    }
                }

                index.All.Add(info);
            }
        }

        foreach (var info in index.All) {
            index._byFullName.TryAdd(info.FullName, info);
            if (info.DeclaringFullName == null) {
                continue;
            }

            if (!index._nestedByDeclaring.TryGetValue(info.DeclaringFullName, out var list)) {
                list = new List<TypeInfo>();
                index._nestedByDeclaring[info.DeclaringFullName] = list;
            }

            list.Add(info);
        }

        return index;
    }

    /// <summary>
    /// Whether the type derives (directly or indirectly) from PlayMaker's FsmStateAction.
    /// </summary>
    public bool IsAction(TypeInfo type) {
        var seen = new HashSet<string>();
        var name = type.BaseFullName;
        while (name != null && seen.Add(name)) {
            if (name == FsmStateActionName) {
                return true;
            }

            name = _byFullName.TryGetValue(name, out var baseType) ? baseType.BaseFullName : null;
        }

        return false;
    }

    /// <summary>
    /// The type itself, its nested (compiler-generated) types, and its base types up to FsmStateAction.
    /// </summary>
    public List<TypeInfo> Closure(TypeInfo type) {
        var result = new List<TypeInfo>();
        var seen = new HashSet<string>();
        var current = type;
        while (current != null && current.FullName != FsmStateActionName && seen.Add(current.FullName)) {
            AddWithNested(current, result);
            current = current.BaseFullName != null && _byFullName.TryGetValue(current.BaseFullName, out var baseType)
                ? baseType
                : null;
        }

        return result;
    }

    private void AddWithNested(TypeInfo type, List<TypeInfo> result) {
        result.Add(type);
        if (!_nestedByDeclaring.TryGetValue(type.FullName, out var nested)) {
            return;
        }

        foreach (var child in nested) {
            AddWithNested(child, result);
        }
    }

    private static string SafeBaseFullName(MetadataReader md, TypeDefinition td) {
        try {
            return Meta.ReferencedFullName(md, td.BaseType);
        } catch {
            return null;
        }
    }
}

internal static class Classifier {
    private static readonly HashSet<string> PerFrameMethods = ["OnUpdate", "OnFixedUpdate", "OnLateUpdate", "DoUpdate"];

    private static readonly Regex CameraVerb = new(
        "Shake|Freeze|Fade|Flash|Follow|Reposition|Lock|Rumble|Cut|Mode|Zoom|Offset|Fov|FOV|Blur|Bloom|Snap|Position",
        RegexOptions.Compiled
    );

    public static Effect Classify(CallTarget call, string fromMethod) {
        var ns = call.Ns ?? "";
        var type = call.Type ?? "";
        var m = call.Method ?? "";
        var isGetter = m.StartsWith("get_", StringComparison.Ordinal);
        var isEventAccessor = m.StartsWith("add_", StringComparison.Ordinal) ||
                              m.StartsWith("remove_", StringComparison.Ordinal);
        var isUnity = ns == "UnityEngine";
        var effect = Effect.None;

        if ((isUnity && type == "Random") ||
            (ns == "System" && type == "Random") ||
            type.StartsWith("Probability", StringComparison.Ordinal) ||
            (!isGetter && m.Contains("Random", StringComparison.OrdinalIgnoreCase))) {
            effect |= Effect.Random;
        }

        if (type == "HeroController") {
            if (m is "get_instance" or "get_SilentInstance" or "get_UnsafeInstance") {
                effect |= Effect.HeroRef;
            } else if (!isGetter && !isEventAccessor) {
                effect |= Effect.HeroCall;
            }
        }

        if ((isUnity && type == "Object" && m == "Instantiate") ||
            (m == "Spawn" && (type.Contains("Pool", StringComparison.Ordinal) || type == "ObjectPoolExtensions"))) {
            effect |= Effect.Spawn;
        }

        if ((isUnity && type == "AudioSource" && m is "Play" or "PlayOneShot" or "PlayDelayed" or "PlayScheduled" or "Stop" or "set_clip") ||
            (!isGetter &&
             (type.Contains("Audio", StringComparison.Ordinal) || type.Contains("Sound", StringComparison.Ordinal)) &&
             (m.Contains("Play", StringComparison.Ordinal) || m.StartsWith("Spawn", StringComparison.Ordinal)))) {
            effect |= Effect.Audio;
        }

        if (isUnity && type == "ParticleSystem" && m is "Play" or "Stop" or "Emit" or "Clear" or "Pause") {
            effect |= Effect.Particles;
        }

        if ((isUnity && type == "GameObject" && m == "SetActive") ||
            (isUnity && type is "Renderer" or "SpriteRenderer" or "MeshRenderer" or "Behaviour" or "Collider2D" &&
             m == "set_enabled")) {
            effect |= Effect.Activate;
        }

        if ((type.StartsWith("tk2dSpriteAnimator", StringComparison.Ordinal) && m.StartsWith("Play", StringComparison.Ordinal)) ||
            (isUnity && type == "Animator" && m is "Play" or "SetTrigger" or "SetBool" or "SetFloat" or "SetInteger" or "CrossFade")) {
            effect |= Effect.Animation;
        }

        if ((isUnity && type == "Rigidbody2D" && m is "set_velocity" or "set_linearVelocity" or "AddForce" or "AddTorque"
                 or "MovePosition" or "MoveRotation" or "set_angularVelocity" or "set_gravityScale" or "set_isKinematic"
                 or "set_bodyType" or "set_position") ||
            (isUnity && type == "Transform" && m is "set_position" or "set_localPosition" or "Translate" or "set_rotation"
                 or "set_localRotation" or "set_eulerAngles" or "set_localEulerAngles" or "set_localScale"
                 or "SetPositionAndRotation" or "Rotate" or "set_parent" or "SetParent")) {
            effect |= Effect.Motion;
        }

        if (type == "HealthManager" && !isGetter && !isEventAccessor) {
            effect |= Effect.Health;
        }

        if (type.Contains("Camera", StringComparison.Ordinal) &&
            !(isUnity && type == "Camera") &&
            !isGetter &&
            !isEventAccessor &&
            CameraVerb.IsMatch(m)) {
            effect |= Effect.Camera;
        }

        if ((isUnity && type == "Time" && m == "set_timeScale") || m.Contains("FreezeMoment", StringComparison.Ordinal)) {
            effect |= Effect.TimeScale;
        }

        if (isUnity && type == "Time" && m == "get_deltaTime" && PerFrameMethods.Contains(fromMethod)) {
            effect |= Effect.FrameTimer;
        }

        return effect;
    }

    public static IEnumerable<Effect> Flags(Effect effect) {
        foreach (var flag in Enum.GetValues<Effect>()) {
            if (flag != Effect.None && effect.HasFlag(flag)) {
                yield return flag;
            }
        }
    }
}

internal sealed class RegistryEntry {
    public string UpdateField;
    public bool TransferSafe;
    public bool Targeted;
}

/// <summary>
/// What SSMP handles, read from its source: action types with Get/Apply handlers, IL-hooked action types,
/// the action registry JSON, and types referenced by the game patcher.
/// </summary>
internal sealed class SsmpCoverage {
    public readonly HashSet<string> Supported = new();
    public readonly HashSet<string> IlHooked = new();
    public readonly Dictionary<string, RegistryEntry> Registry = new();
    public readonly HashSet<string> PatcherTypes = new();
    public string Branch = "?";
    public string Commit = "?";

    public static SsmpCoverage Load(string root) {
        var coverage = new SsmpCoverage();
        var code = Path.Combine(root, "SSMP");
        var actionDir = Path.Combine(code, "Game", "Client", "Entity", "Action");

        var handlerSignature = new Regex(
            @"(?:Get|Apply)NetworkDataFromAction\(\s*EntityNetworkData\??\s+data\s*,\s*([\w\.]+)\s+action\s*\)"
        );
        var ilHook = new Regex(@"typeof\(\s*([\w\.]+)\s*\)\s*\.GetMethod\(");
        foreach (var file in Directory.GetFiles(actionDir, "EntityFsmActions*.cs")) {
            var text = StripComments(File.ReadAllText(file));
            foreach (Match match in handlerSignature.Matches(text)) {
                var name = LastSegment(match.Groups[1].Value);
                if (name != "FsmStateAction") {
                    coverage.Supported.Add(name);
                }
            }

            foreach (Match match in ilHook.Matches(text)) {
                coverage.IlHooked.Add(LastSegment(match.Groups[1].Value));
            }
        }

        var registryJson = File.ReadAllText(Path.Combine(code, "Resource", "action-registry.json"));
        using (var doc = JsonDocument.Parse(registryJson)) {
            foreach (var element in doc.RootElement.EnumerateArray()) {
                var type = element.GetProperty("type").GetString();
                coverage.Registry[type] = new RegistryEntry {
                    UpdateField = element.TryGetProperty("update_field", out var updateField) &&
                                  updateField.ValueKind == JsonValueKind.String
                        ? updateField.GetString()
                        : null,
                    TransferSafe = element.TryGetProperty("transfer_safe_setup", out var transferSafe) &&
                                   transferSafe.ValueKind == JsonValueKind.True,
                    Targeted = element.TryGetProperty("targeted_fsm", out var targeted) &&
                               targeted.ValueKind == JsonValueKind.True,
                };
            }
        }

        var typeofReference = new Regex(@"typeof\(\s*([\w\.]+)\s*\)");
        foreach (var file in Directory.GetFiles(Path.Combine(code, "Game", "Client"), "GamePatcher*.cs")) {
            var text = StripComments(File.ReadAllText(file));
            foreach (Match match in typeofReference.Matches(text)) {
                coverage.PatcherTypes.Add(LastSegment(match.Groups[1].Value));
            }
        }

        coverage.Branch = Git(root, "rev-parse --abbrev-ref HEAD");
        coverage.Commit = Git(root, "rev-parse --short HEAD");
        return coverage;
    }

    private static string StripComments(string text) {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline);
    }

    private static string LastSegment(string name) {
        var index = name.LastIndexOf('.');
        return index < 0 ? name : name[(index + 1)..];
    }

    private static string Git(string root, string arguments) {
        try {
            var startInfo = new ProcessStartInfo("git", $"-C \"{root}\" {arguments}") {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(startInfo);
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "?";
        } catch {
            return "?";
        }
    }
}

internal sealed class ActionRow {
    public string Name;
    public string FullName;
    public string Assembly;
    public Effect Effects;
    public bool EveryFrameField;
    public bool Supported;
    public bool IlHooked;
    public bool InRegistry;
    public bool TransferSafe;
    public bool Targeted;
    public bool PatcherRef;
    public Dictionary<string, string[]> Evidence;
}

internal static class Audit {
    public static List<ActionRow> BuildRows(GameIndex game, SsmpCoverage ssmp) {
        var rows = new List<ActionRow>();
        foreach (var type in game.All) {
            // Nested action classes are fine, compiler-generated closures and iterators are not
            if (type.SimpleName.StartsWith('<') || type.IsAbstract || type.FullName == GameIndex.FsmStateActionName ||
                !game.IsAction(type)) {
                continue;
            }

            var effects = Effect.None;
            var everyFrame = false;
            var evidence = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var part in game.Closure(type)) {
                effects |= part.OwnEffects;
                everyFrame |= part.Fields.Contains("everyFrame");
                foreach (var (flag, calls) in part.Evidence) {
                    var key = flag.ToString();
                    if (!evidence.TryGetValue(key, out var merged)) {
                        merged = new SortedSet<string>(StringComparer.Ordinal);
                        evidence[key] = merged;
                    }

                    merged.UnionWith(calls);
                }
            }

            ssmp.Registry.TryGetValue(type.SimpleName, out var registryEntry);
            rows.Add(new ActionRow {
                Name = type.SimpleName,
                FullName = type.FullName,
                Assembly = type.Assembly,
                Effects = effects,
                EveryFrameField = everyFrame,
                Supported = ssmp.Supported.Contains(type.SimpleName),
                IlHooked = ssmp.IlHooked.Contains(type.SimpleName),
                InRegistry = registryEntry != null,
                TransferSafe = registryEntry?.TransferSafe ?? false,
                Targeted = registryEntry?.Targeted ?? false,
                PatcherRef = ssmp.PatcherTypes.Contains(type.SimpleName),
                Evidence = evidence.ToDictionary(kv => kv.Key, kv => kv.Value.Take(8).ToArray()),
            });
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return rows;
    }
}

internal static class ReportWriter {
    /// <summary>
    /// Effects that land outside the entity's own transform, animation and health, which SSMP's component sync
    /// does not carry over.
    /// </summary>
    private static readonly (Effect Flag, string Label)[] OffEntity = [
        (Effect.Spawn, "生成物体"),
        (Effect.Audio, "播放音效"),
        (Effect.Particles, "粒子特效"),
        (Effect.Camera, "镜头"),
        (Effect.HeroCall, "直接调用主角的方法"),
        (Effect.TimeScale, "时间缩放/顿帧"),
    ];

    /// <summary>
    /// Effects that may only touch the entity itself and could already be covered by SSMP's component sync.
    /// </summary>
    private static readonly (Effect Flag, string Label)[] MaybeLocal = [
        (Effect.Health, "血量/死亡/无敌（HealthManager 组件同步可能已覆盖）"),
        (Effect.Activate, "开关物体/渲染器"),
        (Effect.Animation, "播放动画"),
        (Effect.Motion, "移动/物理"),
    ];

    private static readonly (Effect Flag, string Label)[] Other = [
        (Effect.Random, "消耗随机数"),
        (Effect.HeroRef, "直接取 HeroController.instance"),
        (Effect.FrameTimer, "每帧用 deltaTime 计时"),
    ];

    private const Effect RandomSensitive = Effect.Spawn | Effect.Motion | Effect.Particles | Effect.Audio;

    private const Effect Visible = Effect.Spawn | Effect.Audio | Effect.Particles | Effect.Activate |
                                   Effect.Animation | Effect.Motion | Effect.Camera;

    /// <summary>
    /// Suffixes that mark a variant of the same action rather than a different action.
    /// </summary>
    private static readonly Regex VariantSuffix = new(
        @"^(s|V\d+|2[dD](V\d+)?|(Delay|Temp|Conditional|Bool|IfFalse|IfNull|IfNotPresent|Late|OnExit|Time|VelTime|Vel|Children|Single|Simple|OverTime|Upwards|Recursive|Random|Radial|Velocity|Local|Lerp|Timer)(V\d+)?)$",
        RegexOptions.Compiled
    );

    private static bool InSectionA(ActionRow row) => !row.Supported && OffEntity.Any(c => row.Effects.HasFlag(c.Flag));

    private static bool InSectionB(ActionRow row) =>
        row.Effects.HasFlag(Effect.HeroRef) && !row.Supported && !row.Targeted && !row.PatcherRef;

    private static bool InSectionC1(ActionRow row) =>
        row.Effects.HasFlag(Effect.Random) && (row.Effects & RandomSensitive) != 0 && !row.Supported;

    private static bool InSectionC2(ActionRow row) =>
        row.Effects.HasFlag(Effect.Random) && (row.Effects & RandomSensitive) != 0 && row.Supported && !row.IlHooked;

    private static bool IsVariantOf(ActionRow row, string supported) =>
        !row.Supported &&
        row.Name.Length > supported.Length &&
        row.Name.StartsWith(supported, StringComparison.Ordinal) &&
        VariantSuffix.IsMatch(row.Name[supported.Length..]);

    private static bool InSectionE(ActionRow row) =>
        row.Supported && (row.EveryFrameField || row.Effects.HasFlag(Effect.FrameTimer)) && (row.Effects & Visible) != 0;

    /// <summary>
    /// The report sections an action appears in, for tools that join this audit with asset data. "L" marks unsupported
    /// actions outside every section whose effects may stay on the entity itself.
    /// </summary>
    private static string[] Tags(ActionRow row, SsmpCoverage ssmp) {
        var tags = new List<string>();
        if (InSectionA(row)) {
            tags.Add("A");
        }

        if (InSectionB(row)) {
            tags.Add("B");
        }

        if (InSectionC1(row)) {
            tags.Add("C1");
        }

        if (InSectionC2(row)) {
            tags.Add("C2");
        }

        if (ssmp.Supported.Any(s => IsVariantOf(row, s))) {
            tags.Add("D");
        }

        if (InSectionE(row)) {
            tags.Add("E");
        }

        if (tags.Count == 0 && !row.Supported && MaybeLocal.Any(c => row.Effects.HasFlag(c.Flag))) {
            tags.Add("L");
        }

        return tags.ToArray();
    }

    public static List<string> SectionA(List<ActionRow> rows) => rows.Where(InSectionA).Select(r => r.Name).ToList();

    public static List<string> SectionB(List<ActionRow> rows) => rows.Where(InSectionB).Select(r => r.Name).ToList();

    public static List<string> SectionC1(List<ActionRow> rows) => rows.Where(InSectionC1).Select(r => r.Name).ToList();

    public static List<string> SectionC2(List<ActionRow> rows) => rows.Where(InSectionC2).Select(r => r.Name).ToList();

    public static List<string> SectionD(List<ActionRow> rows, SsmpCoverage ssmp) {
        var lines = new List<string>();
        foreach (var supported in ssmp.Supported.OrderBy(s => s, StringComparer.Ordinal)) {
            var variants = rows.Where(r => IsVariantOf(r, supported)).Select(r => r.Name).ToList();
            if (variants.Count > 0) {
                lines.Add($"- `{supported}` → {Names(variants)}");
            }
        }

        return lines;
    }

    public static List<string> SectionE(List<ActionRow> rows) => rows.Where(InSectionE).Select(r => r.Name).ToList();

    public static string Markdown(List<ActionRow> rows, SsmpCoverage ssmp, string gameDir, HashSet<string> allTypeNames) {
        var sb = new StringBuilder();
        var actionNames = new HashSet<string>(rows.Select(r => r.Name));

        sb.AppendLine("# 丝之歌 × SSMP 实体同步缺口审计");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- SSMP：`{ssmp.Branch}` @ `{ssmp.Commit}`");
        sb.AppendLine($"- 游戏程序集：`{gameDir}`");
        sb.AppendLine("- 重新生成：`dotnet run -c Release --project tools/SyncAudit`");
        sb.AppendLine();
        sb.AppendLine("> 这是静态分析：只看每种状态机动作的代码「会调用什么」，不知道哪些敌人真的用了它（那需要解析场景和预制体资源）。");
        sb.AppendLine("> 所以这是一份待排查清单，不是确定的 bug 列表。分类依据是调用的 API 名字，会有误报，证据见同目录的 JSON。");
        sb.AppendLine();
        sb.AppendLine("## 背景：SSMP 怎么同步状态机动作");
        sb.AppendLine();
        sb.AppendLine("- 非主机玩家电脑上，已登记敌人的状态机整个被关掉（`fsm.enabled = false`）。");
        sb.AppendLine("- 主机上，只有 `EntityFsmActions` 里写了 Get/Apply 处理函数的动作类型，才会在 `OnEnter` 时被拦截、发给其他人重放。");
        sb.AppendLine("- 没有处理函数的动作，其效果在其他玩家那边不会发生，除非恰好被位置、动画、血量等组件同步覆盖。");
        sb.AppendLine();

        sb.AppendLine("## 总览");
        sb.AppendLine();
        sb.AppendLine("| 项目 | 数量 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 游戏里的状态机动作类型 | {rows.Count} |");
        sb.AppendLine($"| SSMP 写了处理函数的类型 | {ssmp.Supported.Count}（在游戏动作里找到 {rows.Count(r => r.Supported)}） |");
        sb.AppendLine($"| SSMP 用 IL 钩子改写的类型 | {ssmp.IlHooked.Count} |");
        sb.AppendLine($"| `action-registry.json` 条目 | {ssmp.Registry.Count}（`targeted_fsm` {ssmp.Registry.Values.Count(v => v.Targeted)}） |");
        foreach (var (flag, label) in OffEntity.Concat(MaybeLocal).Concat(Other)) {
            var total = rows.Count(r => r.Effects.HasFlag(flag));
            var unsupported = rows.Count(r => r.Effects.HasFlag(flag) && !r.Supported);
            sb.AppendLine($"| {label} | {total}（SSMP 未处理 {unsupported}） |");
        }

        sb.AppendLine();

        sb.AppendLine($"## A. 其他玩家那边不会发生的效果（{SectionA(rows).Count}）");
        sb.AppendLine();
        sb.AppendLine("SSMP 没有处理函数，并且代码会影响实体自身以外的东西。一个动作可能出现在多个分类里。");
        sb.AppendLine();
        foreach (var (flag, label) in OffEntity) {
            var names = rows.Where(r => !r.Supported && r.Effects.HasFlag(flag)).Select(r => r.Name).ToList();
            sb.AppendLine($"### {label}（{names.Count}）");
            sb.AppendLine();
            sb.AppendLine(Names(names));
            sb.AppendLine();
        }

        sb.AppendLine("### 可能只影响实体自身、由组件同步兜底（需人工确认）");
        sb.AppendLine();
        foreach (var (flag, label) in MaybeLocal) {
            var names = rows.Where(r => !r.Supported && r.Effects.HasFlag(flag)).Select(r => r.Name).ToList();
            sb.AppendLine($"- **{label}**（{names.Count}）：{Names(names)}");
        }

        sb.AppendLine();

        var sectionB = SectionB(rows);
        sb.AppendLine($"## B. 只认主机那只大黄蜂的动作（{sectionB.Count}）");
        sb.AppendLine();
        sb.AppendLine("代码里直接取 `HeroController.instance`，但不在 `targeted_fsm` 列表里，也没有处理函数或 GamePatcher 补丁。放在主机上跑时，它们只会考虑主机玩家。");
        sb.AppendLine("其中不少是主角自己的状态机用的（回血、加丝、纹章状态等），和敌人有关的要结合资源数据判断。");
        sb.AppendLine();
        sb.AppendLine(Names(sectionB));
        sb.AppendLine();

        var sectionC1 = SectionC1(rows);
        var sectionC2 = SectionC2(rows);
        sb.AppendLine("## C. 随机结果可能没有同步的动作");
        sb.AppendLine();
        sb.AppendLine("消耗随机数，并且结果会影响生成、运动、特效或音效。");
        sb.AppendLine();
        sb.AppendLine($"### C1. 没有处理函数（{sectionC1.Count}）");
        sb.AppendLine();
        sb.AppendLine(Names(sectionC1));
        sb.AppendLine();
        sb.AppendLine($"### C2. 有处理函数，但随机数调用没有被 IL 钩子拦截（{sectionC2.Count}）");
        sb.AppendLine();
        sb.AppendLine("需要看处理函数是把主机的结果发过去，还是在客户端重新抽了一次随机数。");
        sb.AppendLine();
        sb.AppendLine(Names(sectionC2));
        sb.AppendLine();

        var sectionD = SectionD(rows, ssmp);
        sb.AppendLine($"## D. 疑似漏掉的变体（{sectionD.Count} 组）");
        sb.AppendLine();
        sb.AppendLine("SSMP 处理了某个动作，但游戏里还有同一动作的其他版本（V2/V3、Delay、Conditional 等后缀）没被处理。");
        sb.AppendLine();
        sb.AppendLine(sectionD.Count == 0 ? "（无）" : string.Join(Environment.NewLine, sectionD));
        sb.AppendLine();

        var sectionE = SectionE(rows);
        sb.AppendLine($"## E. 只在进入状态时同步、但会持续每帧生效的已处理动作（{sectionE.Count}）");
        sb.AppendLine();
        sb.AppendLine("SSMP 只钩了 `OnEnter`；这些动作有 `everyFrame` 字段或在每帧函数里计时，并且有可见效果。进入状态之后的持续效果要确认有没有别的同步手段。");
        sb.AppendLine();
        sb.AppendLine(Names(sectionE));
        sb.AppendLine();

        sb.AppendLine("## F. 在游戏动作类型里找不到的 SSMP 条目");
        sb.AppendLine();
        AppendMissing(sb, "`action-registry.json`", ssmp.Registry.Keys, actionNames, allTypeNames);
        AppendMissing(sb, "处理函数", ssmp.Supported, actionNames, allTypeNames);
        AppendMissing(sb, "IL 钩子", ssmp.IlHooked, actionNames, allTypeNames);

        return sb.ToString();
    }

    private static void AppendMissing(
        StringBuilder sb,
        string label,
        IEnumerable<string> entries,
        HashSet<string> actionNames,
        HashSet<string> allTypeNames
    ) {
        var missing = entries.Where(e => !actionNames.Contains(e)).OrderBy(e => e, StringComparer.Ordinal).ToList();
        var absent = missing.Where(e => !allTypeNames.Contains(e)).ToList();
        var notAction = missing.Where(e => allTypeNames.Contains(e)).ToList();
        sb.AppendLine($"- {label}：游戏里没有这个类型（{absent.Count}）：{Names(absent)}；类型存在但不是可实例化的状态机动作（{notAction.Count}）：{Names(notAction)}");
    }

    public static string Json(List<ActionRow> rows, SsmpCoverage ssmp) {
        var payload = new {
            ssmp = new {
                branch = ssmp.Branch,
                commit = ssmp.Commit,
                supported = ssmp.Supported.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                ilHooked = ssmp.IlHooked.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            },
            actions = rows.Select(r => new {
                name = r.Name,
                fullName = r.FullName,
                assembly = r.Assembly,
                effects = Classifier.Flags(r.Effects).Select(f => f.ToString()).ToArray(),
                everyFrameField = r.EveryFrameField,
                supported = r.Supported,
                ilHooked = r.IlHooked,
                inActionRegistry = r.InRegistry,
                transferSafe = r.TransferSafe,
                targeted = r.Targeted,
                patcherRef = r.PatcherRef,
                tags = Tags(r, ssmp),
                variantOf = ssmp.Supported.Where(s => IsVariantOf(r, s)).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                evidence = r.Evidence,
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void PrintSummary(List<ActionRow> rows, SsmpCoverage ssmp) {
        Console.WriteLine($"SSMP {ssmp.Branch} @ {ssmp.Commit}");
        Console.WriteLine($"Game FSM action types: {rows.Count}; SSMP handlers: {ssmp.Supported.Count} (found in game: {rows.Count(r => r.Supported)}); IL hooks: {ssmp.IlHooked.Count}");
        Console.WriteLine($"A. effects never reaching other players: {SectionA(rows).Count}");
        foreach (var (flag, _) in OffEntity.Concat(MaybeLocal)) {
            Console.WriteLine($"   {flag,-10} {rows.Count(r => !r.Supported && r.Effects.HasFlag(flag))}");
        }
        Console.WriteLine($"B. only consider the host's hero:        {SectionB(rows).Count}");
        Console.WriteLine($"C1. random results, no handler:          {SectionC1(rows).Count}");
        Console.WriteLine($"C2. random results, handler, no IL hook: {SectionC2(rows).Count}");
        Console.WriteLine($"D. likely missed variants (groups):      {SectionD(rows, ssmp).Count}");
        Console.WriteLine($"E. handled but continuous and visible:   {SectionE(rows).Count}");
    }

    private static string Names(IReadOnlyCollection<string> names) {
        return names.Count == 0 ? "（无）" : string.Join("、", names.Select(n => $"`{n}`"));
    }
}

internal static class IlOpcodes {
    public static readonly OpCode[] One = new OpCode[0x100];
    public static readonly OpCode[] Two = new OpCode[0x100];

    static IlOpcodes() {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            var opcode = (OpCode) field.GetValue(null);
            var value = (ushort) opcode.Value;
            if (opcode.Size == 1) {
                One[value] = opcode;
            } else {
                Two[value & 0xFF] = opcode;
            }
        }
    }

    public static int OperandSize(OperandType type) => type switch {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };
}

internal static class Meta {
    public static IEnumerable<CallTarget> CallTargets(MetadataReader md, byte[] il) {
        var i = 0;
        while (i < il.Length) {
            int first = il[i++];
            OpCode opcode;
            if (first == 0xFE) {
                if (i >= il.Length) {
                    yield break;
                }

                opcode = IlOpcodes.Two[il[i++]];
            } else {
                opcode = IlOpcodes.One[first];
            }

            if (opcode.Name == null) {
                yield break;
            }

            if (opcode.OperandType == OperandType.InlineSwitch) {
                if (i + 4 > il.Length) {
                    yield break;
                }

                var count = BitConverter.ToInt32(il, i);
                i += 4 + 4 * count;
                continue;
            }

            if (opcode.OperandType == OperandType.InlineMethod && i + 4 <= il.Length) {
                var target = MethodTarget(md, BitConverter.ToInt32(il, i));
                if (target != null) {
                    yield return target;
                }
            }

            i += IlOpcodes.OperandSize(opcode.OperandType);
        }
    }

    public static CallTarget MethodTarget(MetadataReader md, int token) {
        try {
            var table = (token >> 24) & 0xFF;
            var row = token & 0x00FFFFFF;
            switch (table) {
                case 0x0A: {
                    var reference = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                    var (ns, name) = TypeName(md, reference.Parent);
                    return name == null ? null : new CallTarget(ns, name, md.GetString(reference.Name));
                }
                case 0x06: {
                    var method = md.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row));
                    var declaring = md.GetTypeDefinition(method.GetDeclaringType());
                    return new CallTarget(
                        md.GetString(declaring.Namespace),
                        md.GetString(declaring.Name),
                        md.GetString(method.Name)
                    );
                }
                case 0x2B: {
                    var specification = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
                    return MethodTarget(md, MetadataTokens.GetToken(specification.Method));
                }
            }
        } catch {
            // Malformed or unsupported token; ignore
        }

        return null;
    }

    public static (string Ns, string Name) TypeName(MetadataReader md, EntityHandle handle) {
        if (handle.IsNil) {
            return (null, null);
        }

        switch (handle.Kind) {
            case HandleKind.TypeReference: {
                var reference = md.GetTypeReference((TypeReferenceHandle) handle);
                return (md.GetString(reference.Namespace), md.GetString(reference.Name));
            }
            case HandleKind.TypeDefinition: {
                var definition = md.GetTypeDefinition((TypeDefinitionHandle) handle);
                return (md.GetString(definition.Namespace), md.GetString(definition.Name));
            }
            case HandleKind.TypeSpecification: {
                var specification = md.GetTypeSpecification((TypeSpecificationHandle) handle);
                var reader = md.GetBlobReader(specification.Signature);
                if (reader.ReadByte() != 0x15) {
                    // Not a generic instantiation
                    return (null, null);
                }

                // CLASS or VALUETYPE marker
                reader.ReadByte();
                return TypeName(md, reader.ReadTypeHandle());
            }
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Full name of a referenced type in the FullTypeName format ("Namespace.Outer/Inner"). Generic instantiations
    /// resolve to their generic type definition.
    /// </summary>
    public static string ReferencedFullName(MetadataReader md, EntityHandle handle) {
        if (handle.IsNil) {
            return null;
        }

        switch (handle.Kind) {
            case HandleKind.TypeDefinition:
                return FullTypeName(md, (TypeDefinitionHandle) handle);
            case HandleKind.TypeReference: {
                var reference = md.GetTypeReference((TypeReferenceHandle) handle);
                var name = md.GetString(reference.Name);
                if (reference.ResolutionScope.Kind == HandleKind.TypeReference) {
                    return ReferencedFullName(md, reference.ResolutionScope) + "/" + name;
                }

                var ns = md.GetString(reference.Namespace);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }
            case HandleKind.TypeSpecification: {
                var specification = md.GetTypeSpecification((TypeSpecificationHandle) handle);
                var reader = md.GetBlobReader(specification.Signature);
                if (reader.ReadByte() != 0x15) {
                    // Not a generic instantiation
                    return null;
                }

                // CLASS or VALUETYPE marker
                reader.ReadByte();
                return ReferencedFullName(md, reader.ReadTypeHandle());
            }
            default:
                return null;
        }
    }

    public static string FullTypeName(MetadataReader md, TypeDefinitionHandle handle) {
        var definition = md.GetTypeDefinition(handle);
        var name = md.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil) {
            return FullTypeName(md, declaring) + "/" + name;
        }

        var ns = md.GetString(definition.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string SimpleOf(string fullName) {
        var index = Math.Max(fullName.LastIndexOf('.'), fullName.LastIndexOf('/'));
        return index < 0 ? fullName : fullName[(index + 1)..];
    }

    public static TypeDefinitionHandle Outermost(MetadataReader md, TypeDefinitionHandle handle) {
        var definition = md.GetTypeDefinition(handle);
        while (!definition.GetDeclaringType().IsNil) {
            handle = definition.GetDeclaringType();
            definition = md.GetTypeDefinition(handle);
        }

        return handle;
    }
}
