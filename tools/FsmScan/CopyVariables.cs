using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// For --copyvars: the object variables that the replicated actions of registered entities work on, and whether the
/// scene client's copy of the creature ever gets a value for them. The copy's FSMs never run, so a variable has a value
/// there only if the scene data gives it one, the start replay sets it, or a replicated action sets it. One that only an
/// action on the scene host's side fills in stays empty on the copy, and the replicated action that works on it does
/// nothing there. One that holds the player character holds the scene client's own character on the copy.
/// </summary>
internal static class CopyVariables {
    // EntityInitializer.InitStateNames and EntityInitializer.ToSkipTypes
    private static readonly HashSet<string> StartStateNames = new(StringComparer.Ordinal) {
        "init", "initiate", "initialise", "initialize", "dormant", "pause", "init pause", "deparents", "opened",
    };

    private static readonly HashSet<string> StartSkipTypes = new(StringComparer.Ordinal) {
        "Tk2dPlayAnimation", "ActivateAllChildren", "SetCollider",
    };

    // Replicated types whose object is something they read, aim at or spawn from, not something they change
    private static readonly HashSet<string> NotActingOnTarget = new(StringComparer.Ordinal) {
        "FindChild", "GetChild", "GetParent", "GetRandomChild", "GetOwner", "GetHero", "FindGameObject",
        "FindAlertRange", "GetPosition", "SetGameObject", "SpawnObjectFromGlobalPool", "CreateObject",
        "FlingObjectsFromGlobalPool", "FlingObjectsFromGlobalPoolTime", "FlingObjectsFromGlobalPoolVel", "SpawnBlood",
        "SpawnBloodTime", "PreSpawnGameObjects", "PreBuildTK2DSprites", "FireAtTarget",
    };

    private static readonly Regex EventTargetVariable = new(@"^<object (?:fsm )?var (.+?)(?: fsm .*| and children)?>$");

    private static readonly Regex ApplySignature = new(
        @"ApplyNetworkDataFromAction\(\s*EntityNetworkData\??\s+\w+\s*,\s*(?:HutongGames\.PlayMaker\.Actions\.)?(\w+)\s+\w+\s*\)",
        RegexOptions.Singleline
    );

    private sealed record Writer(string Type, string State, bool Replicated, bool Start);

    public static void Write(ScanResult scan, string ssmpDir, string outPath) {
        var replicated = ReplicatedTypes(ssmpDir);
        Console.WriteLine($"Replicated action types in SSMP: {replicated.Count}");

        var rows = new List<string[]>();
        var templated = 0;
        foreach (var record in scan.Fsms.Where(r => r.Category == "entity")) {
            if (record.States.Count == 0) {
                templated++;
                continue;
            }

            var writers = new Dictionary<string, List<Writer>>(StringComparer.Ordinal);
            foreach (var state in record.States) {
                var start = StartStateNames.Contains(state.Name.ToLowerInvariant());
                foreach (var action in state.Structured) {
                    var type = ShortName(action.Name);
                    foreach (var param in action.Params.Where(p => p.Variable != null && IsOutput(p.Field))) {
                        if (!writers.TryGetValue(param.Variable, out var list)) {
                            writers[param.Variable] = list = new List<Writer>();
                        }

                        var isReplicated = replicated.Contains(type);
                        list.Add(new Writer(type, state.Name, isReplicated,
                            start && isReplicated && !StartSkipTypes.Contains(type)));
                    }
                }
            }

            foreach (var state in record.States) {
                foreach (var action in state.Structured) {
                    var type = ShortName(action.Name);
                    if (!replicated.Contains(type) || NotActingOnTarget.Contains(type)) {
                        continue;
                    }

                    foreach (var param in action.Params) {
                        var variable = SubjectVariable(param);
                        if (variable == null) {
                            continue;
                        }

                        var initial = record.Variables != null &&
                                      record.Variables.TryGetValue($"gameobject:{variable}", out var value)
                            ? value
                            : "?";
                        var written = writers.TryGetValue(variable, out var list) ? list : new List<Writer>();
                        rows.Add([
                            Verdict(variable, initial, written), record.EntityType ?? "", record.Scene ?? "",
                            record.ObjectPath ?? record.ObjectName ?? "", record.FsmName ?? "", state.Name, type,
                            param.Field, variable, initial,
                            string.Join("; ", written.Select(w =>
                                $"{w.Type}@{w.State}{(w.Replicated ? "" : " (host only)")}{(w.Start ? " (start replay)" : "")}")),
                        ]);
                    }
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var lines = new List<string> {
            "verdict\tentity\tscene\tobject\tfsm\tstate\taction\tfield\tvariable\tscene value\twriters",
        };
        lines.AddRange(rows.Select(row => string.Join("\t", row.Select(cell => cell.Replace('\t', ' ')))));
        File.WriteAllLines(outPath, lines, new UTF8Encoding(false));

        Console.WriteLine(
            $"{rows.Count} uses of an object variable by a replicated action; {templated} FSMs from templates skipped"
        );
        foreach (var verdict in rows.GroupBy(row => row[0]).OrderByDescending(group => group.Count())) {
            var types = verdict.Select(row => row[1]).Distinct().Count();
            Console.WriteLine($"== {verdict.Key}: {verdict.Count()} uses in {types} entity types");
            foreach (var byAction in verdict.GroupBy(row => row[6]).OrderByDescending(group => group.Count()).Take(12)) {
                var examples = byAction.Select(row => $"{row[1]} {row[5]}/{row[8]}").Distinct().Take(3);
                Console.WriteLine($"  {byAction.Count(),6}  {byAction.Key}  e.g. {string.Join("; ", examples)}");
            }
        }

        Console.WriteLine($"Rows: {outPath}");
    }

    private static HashSet<string> ReplicatedTypes(string ssmpDir) {
        var directory = Path.Combine(ssmpDir, "SSMP", "Game", "Client", "Entity", "Action");
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(directory, "EntityFsmActions*.cs")) {
            foreach (Match match in ApplySignature.Matches(File.ReadAllText(file))) {
                types.Add(match.Groups[1].Value);
            }
        }

        return types;
    }

    /// <summary>
    /// The variable holding the object an action works on, from its object parameter or the target of the event it
    /// sends, or null when it works on its owner or on a fixed object.
    /// </summary>
    private static string SubjectVariable(ParamValue param) {
        if (param.Field == "eventTarget") {
            var match = EventTargetVariable.Match(param.Value ?? "");
            return match.Success ? match.Groups[1].Value : null;
        }

        return param.Field is "gameObject" or "parent" && param.Variable != null ? param.Variable : null;
    }

    private static bool IsOutput(string field) =>
        field.StartsWith("store", StringComparison.OrdinalIgnoreCase) ||
        field is "sentByGameObject" or "gameObjectHit" or "variable";

    private static string Verdict(string variable, string initial, List<Writer> written) {
        if (variable == "Hero" || written.Any(w => w.Type is "GetHero" or "GetHeroObject")) {
            return "player character";
        }

        if (initial.StartsWith("<object", StringComparison.Ordinal)) {
            return "given by the scene";
        }

        if (written.Any(w => w.Replicated)) {
            return written.Any(w => w.Replicated && w.Type.Contains("Random"))
                ? "set by a replicated random pick"
                : "set by a replicated action";
        }

        if (written.Any(w => w.Start)) {
            return "set by the start replay";
        }

        return written.Count == 0 ? "never set in this FSM" : "empty on the copy";
    }

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];
}
