using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Read-only IL inspection of the game's managed assemblies.
// Default mode:
//   1) per-frame methods that consume UnityEngine.Random
//   2) types that read Time.deltaTime inside per-frame methods
//   3) call sites that fetch the single HeroController instance
//   4) IL listings of "Type::Method" targets, or "Type::*" for all methods of a type (args override the defaults)
// Callers mode: --callers Type::method ...  (prefix "!" to list callers without IL context)
//   lists methods calling the given APIs and prints IL context around each call
// Find mode: --find text ...  lists types, fields and methods whose names contain any text (case-insensitive)
// Strings mode: --strings text ...  lists methods with string literals containing any text (case-insensitive)

const string Managed = @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\Managed";
// PlayerData writes mode: --pdwrites [writes.tsv] [reads.tsv]  lists C# writes and reads of PlayerData fields
if (args.Length > 0 && args[0] == "--pdwrites")
{
    PlayerDataWrites.Run(args.Skip(1).ToArray());
    return;
}
const int ContextBefore = 14;
const int ContextAfter = 4;

var callersMode = args.Length > 0 && args[0] == "--callers";
var callerArgs = callersMode ? args.Skip(1).ToList() : new List<string>();
var listOnlyTargets = new HashSet<string>(callerArgs.Where(a => a.StartsWith('!')).Select(a => a[1..]));
var callerTargets = new HashSet<string>(callerArgs.Select(a => a.TrimStart('!')));
var callerHits = new List<string>();
var callerSnippets = new List<string>();
var findMode = args.Length > 0 && args[0] == "--find";
var stringsMode = args.Length > 0 && args[0] == "--strings";
var searchTerms = findMode || stringsMode ? args.Skip(1).ToList() : new List<string>();
var searchHits = new List<string>();

var dumpTargets = callersMode || findMode || stringsMode
    ? Array.Empty<string>()
    : args.Length > 0
        ? args
        : new[]
        {
            "HutongGames.PlayMaker.Actions.Wait::OnEnter",
            "HutongGames.PlayMaker.Actions.Wait::OnUpdate",
            "RandomWait::OnEnter",
            "RandomWait::OnUpdate",
            "Flicker::OnUpdate",
            "ObjectJitter::OnUpdate",
            "PlayMakerFSM::Update",
        };

var oneByte = new OpCode[0x100];
var twoByte = new OpCode[0x100];
foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
{
    var oc = (OpCode)f.GetValue(null);
    var v = (ushort)oc.Value;
    if (oc.Size == 1) oneByte[v] = oc;
    else twoByte[v & 0xFF] = oc;
}

var perFrame = new HashSet<string> { "Update", "LateUpdate", "FixedUpdate", "OnUpdate", "OnLateUpdate", "OnFixedUpdate" };
var baseOf = new Dictionary<string, string>();
var perFrameRandom = new Dictionary<string, int>();
var perFrameDeltaByType = new Dictionary<string, int>();
var heroByType = new Dictionary<string, int>();
var heroBySimple = new Dictionary<string, int>();
var dumpOutput = new List<string>();
int heroCalls = 0, perFrameDeltaTotal = 0;

var files = Directory.GetFiles(Managed, "*.dll")
    .Where(f =>
    {
        var n = Path.GetFileName(f);
        return n.StartsWith("Assembly-CSharp") || n.StartsWith("PlayMaker") || n.StartsWith("TeamCherry");
    })
    .OrderBy(f => f);

foreach (var file in files)
{
    using var fs = File.OpenRead(file);
    using var pe = new PEReader(fs);
    if (!pe.HasMetadata) continue;
    var md = pe.GetMetadataReader();

    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        var simpleName = md.GetString(td.Name);
        var typeNs = md.GetString(td.Namespace);
        var fullName = string.IsNullOrEmpty(typeNs) ? simpleName : typeNs + "." + simpleName;
        var isTopLevel = td.GetDeclaringType().IsNil;
        if (isTopLevel)
        {
            try
            {
                var b = TypeName(md, td.BaseType).name;
                if (b != null) baseOf.TryAdd(simpleName, b);
            }
            catch { }
        }
        var (outerFull, outerSimple) = OuterType(md, th);

        if (findMode)
        {
            var displayName = isTopLevel ? fullName : $"{outerFull}/{simpleName}";
            if (Matches(simpleName)) searchHits.Add($"type    {displayName}  ({Path.GetFileName(file)})");
            foreach (var fh in td.GetFields())
            {
                var fieldName = md.GetString(md.GetFieldDefinition(fh).Name);
                if (Matches(fieldName)) searchHits.Add($"field   {displayName}::{fieldName}");
            }
            foreach (var mh in td.GetMethods())
            {
                var name = md.GetString(md.GetMethodDefinition(mh).Name);
                if (Matches(name)) searchHits.Add($"method  {displayName}::{name}");
            }
            continue;
        }

        foreach (var mh in td.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            if (m.RelativeVirtualAddress == 0) continue;
            byte[] il;
            try { il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes(); }
            catch { continue; }
            if (il == null) continue;
            var methodName = md.GetString(m.Name);
            var isPerFrame = perFrame.Contains(methodName);

            foreach (var target in dumpTargets)
            {
                var sep = target.IndexOf("::", StringComparison.Ordinal);
                if (sep < 0) continue;
                var tType = target[..sep];
                var tMethod = target[(sep + 2)..];
                // Nested types, such as compiler-generated coroutines, are matched by "Outer/Nested" as --find prints them
                var displayName = isTopLevel ? fullName : $"{outerFull}/{simpleName}";
                var typeMatch = tType.Contains('/')
                    ? tType == displayName
                    : isTopLevel && (tType.Contains('.') ? tType == fullName : tType == simpleName);
                if (typeMatch && (tMethod == "*" || tMethod == methodName))
                {
                    dumpOutput.Add($"--- {displayName}::{methodName}  ({Path.GetFileName(file)}, {il.Length} bytes of IL) ---");
                    dumpOutput.AddRange(Disassemble(md, il, oneByte, twoByte));
                    dumpOutput.Add("");
                }
            }

            var hitsInMethod = new HashSet<string>();
            int i = 0;
            while (i < il.Length)
            {
                int b0 = il[i++];
                OpCode oc;
                if (b0 == 0xFE)
                {
                    if (i >= il.Length) break;
                    oc = twoByte[il[i++]];
                }
                else
                {
                    oc = oneByte[b0];
                }
                if (oc.Name == null) break;
                if (oc.OperandType == OperandType.InlineSwitch)
                {
                    int n = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * n;
                    continue;
                }
                if (stringsMode && oc.OperandType == OperandType.InlineString)
                {
                    var text = md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i) & 0xFFFFFF));
                    if (Matches(text)) searchHits.Add($"string  {outerFull}::{methodName}  \"{text}\"");
                }
                if (oc.OperandType == OperandType.InlineMethod)
                {
                    var (cns, ctn, cmn) = MethodTarget(md, BitConverter.ToInt32(il, i));
                    if (ctn != null)
                    {
                        var shortName = $"{ctn}::{cmn}";
                        if (callerTargets.Contains(shortName)) hitsInMethod.Add(shortName);

                        if (isPerFrame && cns == "UnityEngine" && ctn == "Random")
                        {
                            var key = $"{outerFull}::{methodName}";
                            perFrameRandom[key] = perFrameRandom.GetValueOrDefault(key) + 1;
                        }
                        else if (isPerFrame && cns == "UnityEngine" && ctn == "Time" && cmn == "get_deltaTime")
                        {
                            perFrameDeltaTotal++;
                            perFrameDeltaByType[outerFull] = perFrameDeltaByType.GetValueOrDefault(outerFull) + 1;
                        }
                        else if (ctn == "HeroController" && cmn is "get_instance" or "get_SilentInstance" or "get_UnsafeInstance")
                        {
                            heroCalls++;
                            heroByType[outerFull] = heroByType.GetValueOrDefault(outerFull) + 1;
                            heroBySimple[outerSimple] = heroBySimple.GetValueOrDefault(outerSimple) + 1;
                        }
                    }
                }
                i += OperandSize(oc.OperandType);
            }

            if (hitsInMethod.Count > 0)
            {
                callerHits.Add($"  {outerFull}::{methodName}  ({il.Length} bytes of IL) -> {string.Join(", ", hitsInMethod)}");
                var snippetKeys = hitsInMethod.Where(h => !listOnlyTargets.Contains(h)).ToList();
                if (snippetKeys.Count > 0)
                {
                    var lines = Disassemble(md, il, oneByte, twoByte);
                    var include = new bool[lines.Count];
                    for (int k = 0; k < lines.Count; k++)
                    {
                        if (!snippetKeys.Any(s => lines[k].Contains(s, StringComparison.Ordinal))) continue;
                        for (int j = Math.Max(0, k - ContextBefore); j <= Math.Min(lines.Count - 1, k + ContextAfter); j++)
                        {
                            include[j] = true;
                        }
                    }
                    callerSnippets.Add($"--- {outerFull}::{methodName}  ({Path.GetFileName(file)}) ---");
                    int last = -1;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (!include[j]) continue;
                        if (last >= 0 && j != last + 1) callerSnippets.Add("  ...");
                        callerSnippets.Add(lines[j]);
                        last = j;
                    }
                    callerSnippets.Add("");
                }
            }
        }
    }
}

bool Matches(string text) => searchTerms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

bool IsAction(string simple)
{
    var seen = new HashSet<string>();
    var cur = simple;
    while (cur != null && seen.Add(cur))
    {
        if (cur == "FsmStateAction") return true;
        cur = baseOf.TryGetValue(cur, out var b) ? b : null;
    }
    return false;
}

if (findMode || stringsMode)
{
    var hits = searchHits.Distinct().ToList();
    Console.WriteLine($"== {(findMode ? "Names" : "String literals")} containing {string.Join(", ", searchTerms)}: {hits.Count} ==");
    hits.ForEach(Console.WriteLine);
    return;
}

if (callersMode)
{
    Console.WriteLine($"== Callers: {callerHits.Count} methods ==");
    callerHits.ForEach(Console.WriteLine);
    Console.WriteLine();
    Console.WriteLine("== IL context around calls ==");
    callerSnippets.ForEach(Console.WriteLine);
    return;
}

Console.WriteLine($"== Per-frame methods calling UnityEngine.Random: {perFrameRandom.Values.Sum()} call sites in {perFrameRandom.Count} methods ==");
foreach (var kv in perFrameRandom.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
{
    Console.WriteLine($"  {kv.Value,3}  {kv.Key}");
}
Console.WriteLine();
Console.WriteLine($"== Time.deltaTime reads inside per-frame methods: {perFrameDeltaTotal} in {perFrameDeltaByType.Count} types (top 30) ==");
foreach (var kv in perFrameDeltaByType.OrderByDescending(k => k.Value).Take(30))
{
    Console.WriteLine($"  {kv.Value,3}  {kv.Key}");
}
Console.WriteLine();
var heroActions = heroBySimple.Keys.Where(IsAction).OrderBy(x => x).ToList();
Console.WriteLine($"== HeroController instance getter calls: {heroCalls} in {heroByType.Count} types; PlayMaker action types among them: {heroActions.Count} ==");
foreach (var kv in heroByType.OrderByDescending(k => k.Value).Take(30))
{
    Console.WriteLine($"  {kv.Value,3}  {kv.Key}");
}
Console.WriteLine("  actions: " + string.Join(", ", heroActions.Take(80)));
Console.WriteLine();
Console.WriteLine("== IL listings ==");
dumpOutput.ForEach(Console.WriteLine);

static List<string> Disassemble(MetadataReader md, byte[] il, OpCode[] oneByte, OpCode[] twoByte)
{
    var lines = new List<string>();
    int i = 0;
    while (i < il.Length)
    {
        int start = i;
        int b0 = il[i++];
        var oc = b0 == 0xFE ? twoByte[il[i++]] : oneByte[b0];
        if (oc.Name == null)
        {
            lines.Add($"  IL_{start:X4}: ??? (0x{b0:X2})");
            break;
        }
        var operand = "";
        switch (oc.OperandType)
        {
            case OperandType.InlineNone:
                break;
            case OperandType.ShortInlineBrTarget:
                operand = $"IL_{i + 1 + (sbyte)il[i]:X4}";
                i += 1;
                break;
            case OperandType.InlineBrTarget:
                operand = $"IL_{i + 4 + BitConverter.ToInt32(il, i):X4}";
                i += 4;
                break;
            case OperandType.ShortInlineI:
                operand = ((sbyte)il[i]).ToString();
                i += 1;
                break;
            case OperandType.ShortInlineVar:
                operand = il[i].ToString();
                i += 1;
                break;
            case OperandType.InlineVar:
                operand = BitConverter.ToUInt16(il, i).ToString();
                i += 2;
                break;
            case OperandType.InlineI:
                operand = BitConverter.ToInt32(il, i).ToString();
                i += 4;
                break;
            case OperandType.InlineI8:
                operand = BitConverter.ToInt64(il, i).ToString();
                i += 8;
                break;
            case OperandType.ShortInlineR:
                operand = BitConverter.ToSingle(il, i).ToString("R");
                i += 4;
                break;
            case OperandType.InlineR:
                operand = BitConverter.ToDouble(il, i).ToString("R");
                i += 8;
                break;
            case OperandType.InlineString:
                operand = "\"" + md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i) & 0xFFFFFF)) + "\"";
                i += 4;
                break;
            case OperandType.InlineSwitch:
            {
                int n = BitConverter.ToInt32(il, i);
                i += 4;
                int baseOffset = i + 4 * n;
                var targets = new List<string>();
                for (int k = 0; k < n; k++)
                {
                    targets.Add($"IL_{baseOffset + BitConverter.ToInt32(il, i):X4}");
                    i += 4;
                }
                operand = "(" + string.Join(", ", targets) + ")";
                break;
            }
            default:
                operand = ResolveToken(md, BitConverter.ToInt32(il, i));
                i += 4;
                break;
        }
        lines.Add($"  IL_{start:X4}: {oc.Name,-10} {operand}");
    }
    return lines;
}

static int OperandSize(OperandType t) => t switch
{
    OperandType.InlineNone => 0,
    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
    OperandType.InlineVar => 2,
    OperandType.InlineI8 or OperandType.InlineR => 8,
    _ => 4,
};

static string ResolveToken(MetadataReader md, int token)
{
    int table = (token >> 24) & 0xFF;
    int row = token & 0xFFFFFF;
    try
    {
        switch (table)
        {
            case 0x01:
            {
                var (ns, n) = TypeName(md, MetadataTokens.TypeReferenceHandle(row));
                return Q(ns, n);
            }
            case 0x02:
            {
                var (ns, n) = TypeName(md, MetadataTokens.TypeDefinitionHandle(row));
                return Q(ns, n);
            }
            case 0x1B:
            {
                var (ns, n) = TypeName(md, MetadataTokens.TypeSpecificationHandle(row));
                return Q(ns, n) + "<...>";
            }
            case 0x04:
            {
                var fd = md.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(row));
                var td = md.GetTypeDefinition(fd.GetDeclaringType());
                return $"{md.GetString(td.Name)}::{md.GetString(fd.Name)}";
            }
            case 0x06:
            {
                var m = md.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row));
                var td = md.GetTypeDefinition(m.GetDeclaringType());
                return $"{md.GetString(td.Name)}::{md.GetString(m.Name)}()";
            }
            case 0x0A:
            {
                var mr = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                var (ns, n) = TypeName(md, mr.Parent);
                var suffix = mr.GetKind() == MemberReferenceKind.Method ? "()" : "";
                return $"{Q(ns, n)}::{md.GetString(mr.Name)}{suffix}";
            }
            case 0x2B:
            {
                var spec = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
                return ResolveToken(md, MetadataTokens.GetToken(spec.Method)) + "<T>";
            }
        }
    }
    catch { }
    return $"token 0x{token:X8}";
}

static string Q(string ns, string n) => string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";

static (string ns, string name) TypeName(MetadataReader md, EntityHandle h)
{
    if (h.IsNil) return (null, null);
    switch (h.Kind)
    {
        case HandleKind.TypeReference:
        {
            var tr = md.GetTypeReference((TypeReferenceHandle)h);
            return (md.GetString(tr.Namespace), md.GetString(tr.Name));
        }
        case HandleKind.TypeDefinition:
        {
            var td = md.GetTypeDefinition((TypeDefinitionHandle)h);
            return (md.GetString(td.Namespace), md.GetString(td.Name));
        }
        case HandleKind.TypeSpecification:
        {
            var ts = md.GetTypeSpecification((TypeSpecificationHandle)h);
            var br = md.GetBlobReader(ts.Signature);
            if (br.ReadByte() != 0x15) return (null, null); // not a generic instantiation
            br.ReadByte(); // CLASS or VALUETYPE
            return TypeName(md, br.ReadTypeHandle());
        }
        default:
            return (null, null);
    }
}

static (string ns, string type, string method) MethodTarget(MetadataReader md, int token)
{
    try
    {
        int table = (token >> 24) & 0xFF;
        int row = token & 0x00FFFFFF;
        switch (table)
        {
            case 0x0A:
            {
                var mr = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                var (ns, tn) = TypeName(md, mr.Parent);
                return (ns, tn, md.GetString(mr.Name));
            }
            case 0x06:
            {
                var m = md.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row));
                var td = md.GetTypeDefinition(m.GetDeclaringType());
                return (md.GetString(td.Namespace), md.GetString(td.Name), md.GetString(m.Name));
            }
            case 0x2B:
            {
                var spec = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
                return MethodTarget(md, MetadataTokens.GetToken(spec.Method));
            }
        }
    }
    catch { }
    return (null, null, null);
}

static (string full, string simple) OuterType(MetadataReader md, TypeDefinitionHandle th)
{
    var td = md.GetTypeDefinition(th);
    while (!td.GetDeclaringType().IsNil)
    {
        th = td.GetDeclaringType();
        td = md.GetTypeDefinition(th);
    }
    var ns = md.GetString(td.Namespace);
    var name = md.GetString(td.Name);
    return (string.IsNullOrEmpty(ns) ? name : ns + "." + name, name);
}
