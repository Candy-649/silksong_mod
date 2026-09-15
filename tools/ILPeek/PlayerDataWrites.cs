using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

/// <summary>
/// --pdwrites [writes.tsv] [reads.tsv]: finds the C# methods of the game that write fields of PlayerData, either
/// directly (stfld, or ldflda to change a struct or pass the field by reference) or by name through PlayerData.SetBool,
/// SetInt, IncrementInt, DecrementInt, IntAdd, SetFloat, SetString, SetVector3 or VariableExtensions.SetVariable with a
/// constant string. Name arguments that aren't a constant string show as "?". Also counts the methods that read each
/// field directly (ldfld) or by name (GetBool, GetInt, ...).
/// </summary>
internal static class PlayerDataWrites
{
    private const string Managed = @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\Managed";

    private static readonly HashSet<string> NamedSetters = new(StringComparer.Ordinal)
    {
        "SetBool", "SetInt", "IncrementInt", "DecrementInt", "IntAdd", "SetFloat", "SetString", "SetVector3", "SetVariable",
    };

    private static readonly HashSet<string> NamedGetters = new(StringComparer.Ordinal)
    {
        "GetBool", "GetInt", "GetFloat", "GetString", "GetVector3", "GetVariable",
    };

    public static void Run(string[] args)
    {
        var writesPath = args.Length > 0 ? args[0] : "reports/playerdata-code-writes.tsv";
        var readsPath = args.Length > 1 ? args[1] : "reports/playerdata-code-reads.tsv";

        var oneByte = new OpCode[0x100];
        var twoByte = new OpCode[0x100];
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var oc = (OpCode)f.GetValue(null);
            var v = (ushort)oc.Value;
            if (oc.Size == 1) oneByte[v] = oc;
            else twoByte[v & 0xFF] = oc;
        }

        var writes = new List<string>();
        var reads = new Dictionary<string, (int Count, List<string> Methods)>(StringComparer.Ordinal);
        HashSet<string> fieldNames = null;

        foreach (var file in Directory.GetFiles(Managed, "*.dll").Where(f => Path.GetFileName(f).StartsWith("Assembly-CSharp") ||
                                                                                Path.GetFileName(f).StartsWith("TeamCherry")).OrderBy(f => f))
        {
            using var fs = File.OpenRead(file);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) continue;
            var md = pe.GetMetadataReader();

            if (fieldNames == null && Path.GetFileName(file) == "Assembly-CSharp.dll")
            {
                fieldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var th in md.TypeDefinitions)
                {
                    var td = md.GetTypeDefinition(th);
                    if (md.GetString(td.Name) != "PlayerData" || !td.GetDeclaringType().IsNil) continue;
                    foreach (var fh in td.GetFields()) fieldNames.Add(md.GetString(md.GetFieldDefinition(fh).Name));
                }
            }

            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                var typeName = DisplayName(md, th);
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    if (m.RelativeVirtualAddress == 0) continue;
                    byte[] il;
                    try { il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes(); }
                    catch { continue; }
                    if (il == null) continue;
                    var method = $"{typeName}::{md.GetString(m.Name)}";

                    // The recent instructions, for the constant arguments of a call
                    var recent = new List<(string Op, string Operand)>();
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
                        else oc = oneByte[b0];
                        if (oc.Name == null) break;
                        if (oc.OperandType == OperandType.InlineSwitch)
                        {
                            int n = BitConverter.ToInt32(il, i);
                            i += 4 + 4 * n;
                            recent.Add((oc.Name, ""));
                            continue;
                        }

                        string operand = "";
                        if (oc.OperandType == OperandType.InlineString)
                        {
                            operand = md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i) & 0xFFFFFF));
                        }
                        else if (oc.OperandType == OperandType.InlineField)
                        {
                            var (owner, field) = FieldTarget(md, BitConverter.ToInt32(il, i));
                            operand = $"{owner}::{field}";
                            if (owner == "PlayerData")
                            {
                                if (oc.Name is "stfld" or "stsfld")
                                {
                                    writes.Add($"{field}\tstfld\t{Constant(recent)}\t{method}");
                                }
                                else if (oc.Name == "ldflda")
                                {
                                    writes.Add($"{field}\tldflda\t\t{method}");
                                }
                                else if (oc.Name is "ldfld" or "ldsfld")
                                {
                                    AddRead(reads, field, method);
                                }
                            }
                        }
                        else if (oc.OperandType == OperandType.InlineMethod)
                        {
                            var (ns, owner, name) = MethodTarget(md, BitConverter.ToInt32(il, i));
                            operand = $"{owner}::{name}";
                            var byName = owner is "PlayerData" or "PlayerDataBase" ||
                                         (owner == "VariableExtensions" && name is "SetVariable" or "GetVariable");
                            if (byName && (NamedSetters.Contains(name) || NamedGetters.Contains(name)))
                            {
                                // The name is the first string constant among the arguments pushed since the last call
                                var args2 = new List<string>();
                                for (int k = recent.Count - 1; k >= 0 && recent.Count - k <= 8; k--)
                                {
                                    if (recent[k].Op.StartsWith("call") || recent[k].Op == "newobj") break;
                                    if (recent[k].Op == "ldstr") args2.Insert(0, recent[k].Operand);
                                }
                                var fieldName = args2.Count > 0 ? args2[0] : "?";
                                if (NamedSetters.Contains(name))
                                {
                                    var value = name == "SetString" && args2.Count > 1 ? args2[1] : Constant(recent);
                                    writes.Add($"{fieldName}\t{name}\t{value}\t{method}");
                                }
                                else if (fieldName != "?")
                                {
                                    AddRead(reads, fieldName, method);
                                }
                            }
                        }

                        recent.Add((oc.Name, operand));
                        if (recent.Count > 16) recent.RemoveAt(0);
                        i += OperandSize(oc.OperandType);
                    }
                }
            }
        }

        var builder = new StringBuilder("field\tkind\tvalue\tmethod\tknown_field\n");
        foreach (var line in writes.Distinct())
        {
            var field = line[..line.IndexOf('\t')];
            builder.Append(line).Append('\t').Append(fieldNames?.Contains(field) == true ? "1" : "0").Append('\n');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(writesPath)));
        File.WriteAllText(writesPath, builder.ToString(), new UTF8Encoding(false));

        builder.Clear().Append("field\tmethods\texamples\n");
        foreach (var (field, info) in reads.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            builder.Append($"{field}\t{info.Count}\t{string.Join(" | ", info.Methods)}\n");
        }
        File.WriteAllText(readsPath, builder.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{writes.Distinct().Count()} writes of {writes.Select(w => w[..w.IndexOf('\t')]).Distinct().Count()} names, " +
                          $"reads of {reads.Count} names: {writesPath}, {readsPath}");
    }

    private static void AddRead(Dictionary<string, (int Count, List<string> Methods)> reads, string field, string method)
    {
        if (!reads.TryGetValue(field, out var info)) info = (0, new List<string>());
        if (info.Methods.Contains(method)) return;
        if (info.Methods.Count < 6) info.Methods.Add(method);
        reads[field] = (info.Count + 1, info.Methods);
    }

    /// <summary>
    /// The constant that the instruction before a store or call pushed, if it is one.
    /// </summary>
    private static string Constant(List<(string Op, string Operand)> recent)
    {
        if (recent.Count == 0) return "";
        var (op, operand) = recent[^1];
        return op switch
        {
            "ldc.i4.0" => "0/false",
            "ldc.i4.1" => "1/true",
            "ldc.i4.m1" => "-1",
            "ldstr" => $"\"{operand}\"",
            _ when op.StartsWith("ldc.i4.") && op.Length == 8 => op[7..],
            _ when op.StartsWith("ldc.") => "const",
            _ => "",
        };
    }

    private static string DisplayName(MetadataReader md, TypeDefinitionHandle th)
    {
        var td = md.GetTypeDefinition(th);
        var name = md.GetString(td.Name);
        if (!td.GetDeclaringType().IsNil) return $"{DisplayName(md, td.GetDeclaringType())}/{name}";
        var ns = md.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static (string Owner, string Field) FieldTarget(MetadataReader md, int token)
    {
        try
        {
            int table = (token >> 24) & 0xFF;
            int row = token & 0xFFFFFF;
            if (table == 0x04)
            {
                var fd = md.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(row));
                var td = md.GetTypeDefinition(fd.GetDeclaringType());
                return (md.GetString(td.Name), md.GetString(fd.Name));
            }
            if (table == 0x0A)
            {
                var mr = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                return (TypeName(md, mr.Parent), md.GetString(mr.Name));
            }
        }
        catch { }
        return (null, null);
    }

    private static (string Ns, string Type, string Method) MethodTarget(MetadataReader md, int token)
    {
        try
        {
            int table = (token >> 24) & 0xFF;
            int row = token & 0xFFFFFF;
            switch (table)
            {
                case 0x0A:
                {
                    var mr = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                    return (null, TypeName(md, mr.Parent), md.GetString(mr.Name));
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

    private static string TypeName(MetadataReader md, EntityHandle h)
    {
        if (h.IsNil) return null;
        switch (h.Kind)
        {
            case HandleKind.TypeReference:
                return md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name);
            case HandleKind.TypeDefinition:
                return md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name);
            case HandleKind.TypeSpecification:
            {
                var ts = md.GetTypeSpecification((TypeSpecificationHandle)h);
                var br = md.GetBlobReader(ts.Signature);
                if (br.ReadByte() != 0x15) return null;
                br.ReadByte();
                return TypeName(md, br.ReadTypeHandle());
            }
            default:
                return null;
        }
    }

    private static int OperandSize(OperandType t) => t switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };
}
