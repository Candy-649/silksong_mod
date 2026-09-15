using System.Text;
using System.Text.Json;
using AssetsTools.NET.Extra;

/// <summary>
/// For --interactions: finds the FSMs that the interact button starts and sorts them the way SSMP's two-player saves
/// do. A mechanism changes the world after the hero pays or confirms, and the partner's game replays that part. The
/// others are conversations about wishes, interactions that give something to one player, conversations, travel, and
/// the rest. For each mechanism it prints the states its change to the world starts in, the actions of that part that
/// may touch the hero, and what the interaction saves.
/// </summary>
internal static class InteractionAnalyzer {
    private const string InteractEvent = "INTERACT";

    /// <summary>
    /// Events that a state waits for while a dialogue or a yes/no box is open.
    /// </summary>
    private static readonly HashSet<string> WaitEvents = new(StringComparer.Ordinal) {
        "YES", "NO", "CONVO_END", "LINE_END",
    };

    /// <summary>
    /// Events that leave a prompt without paying or confirming.
    /// </summary>
    private static readonly HashSet<string> CancelEvents = new(StringComparer.Ordinal) {
        "CANCEL", "NO", "FALSE", "HERO DAMAGED", "CONVO_END_FORCED",
    };

    /// <summary>
    /// Actions of the part of an interaction that belongs to the hero who pressed the button: prompts, payment,
    /// dialogue and moving the hero.
    /// </summary>
    private static readonly string[] PromptPrefixes = [
        "RunFSM", "DialogueYesNo", "RunDialogue", "TakeCurrency", "CurrencyCounterMethod",
        "RespondToCurrencyCounterEvents", "CollectableItemTake", "AddHeroInputBlocker", "HeroRelinquishControl",
        "WaitForHeroInPosition", "DoHeroMovement", "RelicBoardOwnerYesNo", "QuestCompleteYesNo",
    ];

    /// <summary>
    /// Actions of a prompt that pay for or confirm what comes after it.
    /// </summary>
    private static readonly string[] PaymentPrefixes = [
        "RunFSM", "DialogueYesNo", "TakeCurrency", "CollectableItemTake",
    ];

    /// <summary>
    /// Actions that give the hero something, which makes an interaction belong to each player.
    /// </summary>
    private static readonly string[] GivePrefixes = [
        "SavedItemGet", "CollectableItemCollect", "AddCurrency", "SpawnPowerUpGetMsg", "SpawnSkillGetMsg",
        "SetShopItemPurchased", "OpenSimpleShopMenu",
    ];

    /// <summary>
    /// Actions that save something.
    /// </summary>
    private static readonly string[] WritePrefixes = [
        "SetPlayerData", "IncrementPlayerData", "DecrementPlayerData", "PlayerDataIntAdd", "SetPersistentBool",
        "SetPersistentInt",
    ];

    public static void Run(string bundleDir, string scenes, string ssmpDir, string outPath) {
        var registry = EntityRegistryData.Load(ssmpDir);
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();
        ActionParams.ReadStructuredParams = true;
        BundleScanner.CollectPersistent = true;

        var sceneNames = scenes == "all"
            ? null
            : scenes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Where(path => {
                var name = Path.GetFileName(path);
                var rel = Path.GetRelativePath(bundleDir, path).Replace('\\', '/');
                return name.Contains("fsmtemplates", StringComparison.Ordinal) ||
                       (rel.StartsWith("scenes_scenes_scenes/", StringComparison.Ordinal) &&
                        (sceneNames == null || sceneNames.Contains(Path.GetFileNameWithoutExtension(name))));
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var result = new ScanResult();
        foreach (var path in paths) {
            try {
                BundleScanner.ScanBundleAt(manager, bundleDir, path, scriptNames, registry, result);
            } catch (Exception e) {
                Console.WriteLine($"error in {Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }
        }

        BundleScanner.ResolveTemplates(result);
        var worldItems = LoadWorldItems(ssmpDir);
        var itemsByObject = result.Persistent
            .GroupBy(item => (item.File, item.GameObjectPathId))
            .ToDictionary(group => group.Key, group => group.ToList());
        var childItemsByObject = result.Persistent
            .Where(item => item.ParentPathId != 0)
            .GroupBy(item => (item.File, item.ParentPathId))
            .ToDictionary(group => group.Key, group => group.ToList());

        var analyses = result.Fsms
            .Where(record => record.Scene != null && record.States != null)
            .Select(record => new Graph(record))
            .Where(graph => graph.IdleStates.Count > 0)
            .ToList();

        var dump = Environment.GetEnvironmentVariable("INTERACTIONS_DUMP");
        var tsv = new StringBuilder();
        tsv.AppendLine("scene\tpath\tfsm\tclass\treason\tcomponents\tidle\tprompt\tstarts\twrites\thero_actions\titems");
        foreach (var graph in analyses.OrderBy(g => g.Record.Scene, StringComparer.Ordinal)
                     .ThenBy(g => g.Record.ObjectPath, StringComparer.Ordinal)) {
            var record = graph.Record;
            var items = (itemsByObject.GetValueOrDefault((record.File, record.GameObjectPathId)) ?? [])
                .Concat(childItemsByObject.GetValueOrDefault((record.File, record.GameObjectPathId)) ?? [])
                .Select(item => $"{item.Id}({(IsWorldItem(worldItems, item) ? "world" : "kept")})")
                .Distinct()
                .ToList();

            tsv.AppendLine(string.Join("\t",
                record.Scene, record.ObjectPath, record.FsmName, graph.Class, graph.Reason,
                string.Join(",", record.Components ?? []),
                string.Join(",", graph.IdleStates), string.Join(",", graph.PromptStates),
                string.Join(",", graph.WorldStarts), string.Join(";", graph.Writes),
                string.Join(";", graph.HeroActions), string.Join(",", items)
            ).Replace('\n', ' ').Replace('\r', ' '));

            if (!string.IsNullOrEmpty(dump) &&
                $"{record.Scene}/{record.ObjectPath}".Contains(dump, StringComparison.Ordinal)) {
                Console.WriteLine($"== {record.Scene} / {record.ObjectPath} · {record.FsmName}: {graph.Class} {graph.Reason}");
                graph.Dump();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(outPath, tsv.ToString(), new UTF8Encoding(false));

        Console.WriteLine($"{analyses.Count} FSMs with {InteractEvent} in {analyses.Select(g => g.Record.Scene).Distinct().Count()} scenes");
        foreach (var group in analyses.GroupBy(g => g.Class).OrderByDescending(g => g.Count())) {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        Console.WriteLine("== mechanisms by FSM and object name");
        foreach (var group in analyses.Where(g => g.Class == "mechanism")
                     .GroupBy(g => $"{g.Record.FsmName} on {g.Record.ObjectName}")
                     .OrderByDescending(g => g.Count())) {
            var starts = group.SelectMany(g => g.WorldStarts).GroupBy(s => s).OrderByDescending(s => s.Count())
                .Select(s => $"{s.Key} x{s.Count()}");
            Console.WriteLine($"  {group.Count(),4}  {group.Key}: starts {string.Join(", ", starts)}");
        }

        Console.WriteLine($"TSV: {outPath}");
    }

    /// <summary>
    /// Loads the saved objects of the world that two-player saves share, by scene.
    /// </summary>
    private static Dictionary<string, HashSet<string>> LoadWorldItems(string ssmpDir) {
        var path = Path.Combine(ssmpDir, "SSMP", "Resource", "coop-world-items.json");
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (!File.Exists(path)) {
            return result;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("bools", out var bools)) {
            return result;
        }

        foreach (var scene in bools.EnumerateObject()) {
            result[scene.Name] = scene.Value.EnumerateArray().Select(id => id.GetString()).ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private static bool IsWorldItem(Dictionary<string, HashSet<string>> worldItems, PersistentRecord item) {
        return worldItems.TryGetValue(item.Scene, out var ids) && ids.Contains(item.Id);
    }

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    private static bool HasPrefix(ActionParamsData action, string[] prefixes) {
        var shortName = ShortName(action.Name);
        return prefixes.Any(prefix => shortName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsQuestAction(ActionParamsData action) {
        return action.Name.StartsWith("QuestPlaymakerActions.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a name of the player data holds the state of the hero instead of progress, like SSMP's
    /// BossRoomCoop.IsHeroStateName.
    /// </summary>
    private static bool IsHeroStateName(string name) {
        return name.StartsWith("disable", StringComparison.Ordinal) ||
               name.StartsWith("respawn", StringComparison.Ordinal) ||
               name.StartsWith("hazard", StringComparison.Ordinal) ||
               name.EndsWith("Cooldown", StringComparison.Ordinal) ||
               name is "isInvincible" or "atBench";
    }

    private static (string Event, string To) Split(string transition) {
        var index = transition.IndexOf("->", StringComparison.Ordinal);
        return index < 0 ? (transition, null) : (transition[..index], transition[(index + 2)..]);
    }

    internal sealed class Graph {
        public readonly FsmRecord Record;
        public readonly List<string> IdleStates = [];
        public readonly List<string> PromptStates = [];
        public readonly List<string> WorldStarts = [];
        public readonly List<string> Writes = [];
        public readonly List<string> HeroActions = [];
        public string Class = "other";
        public string Reason = "";

        private readonly Dictionary<string, StateData> _states = new(StringComparer.Ordinal);
        private readonly HashSet<string> _world = new(StringComparer.Ordinal);

        public Graph(FsmRecord record) {
            Record = record;
            foreach (var state in record.States) {
                _states.TryAdd(state.Name, state);
            }

            foreach (var state in record.States) {
                if (state.Transitions.Any(t => Split(t).Event == InteractEvent)) {
                    IdleStates.Add(state.Name);
                }
            }

            if (IdleStates.Count == 0) {
                return;
            }

            var idle = IdleStates.ToHashSet(StringComparer.Ordinal);
            var targets = record.States.Where(s => idle.Contains(s.Name))
                .SelectMany(s => s.Transitions.Select(Split))
                .Where(t => t.Event == InteractEvent && t.To != null && _states.ContainsKey(t.To) && !idle.Contains(t.To))
                .Select(t => t.To)
                .Distinct()
                .ToList();

            // The prompt: the states from the interaction on that belong to the hero. The world starts in the states
            // that a prompt leads to when the hero paid or confirmed
            var prompt = new HashSet<string>(StringComparer.Ordinal);
            var starts = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var target in targets.Where(target => IsPrompt(_states[target]))) {
                prompt.Add(target);
                queue.Enqueue(target);
            }

            while (queue.Count > 0) {
                var state = _states[queue.Dequeue()];
                foreach (var (eventName, to) in state.Transitions.Select(Split)) {
                    if (to == null || !_states.TryGetValue(to, out var next) || idle.Contains(to)) {
                        continue;
                    }

                    if (IsPrompt(next)) {
                        if (prompt.Add(to)) {
                            queue.Enqueue(to);
                        }
                    } else if (!CancelEvents.Contains(eventName)) {
                        starts.Add(to);
                    }
                }
            }

            PromptStates.AddRange(prompt);
            var reachable = Reach(targets, idle);
            foreach (var action in reachable.SelectMany(name => _states[name].Structured)) {
                if (!HasPrefix(action, WritePrefixes)) {
                    continue;
                }

                var name = action.Params.FirstOrDefault(p => p.Field is "boolName" or "intName" or "VariableName" or "stringName");
                if (name?.Value != null && IsHeroStateName(name.Value)) {
                    continue;
                }

                var text = $"{ShortName(action.Name)}({string.Join(",", action.Params.Select(ParamText))})";
                if (!Writes.Contains(text)) {
                    Writes.Add(text);
                }
            }

            var actions = reachable.SelectMany(name => _states[name].Structured).ToList();
            if (actions.FirstOrDefault(IsQuestAction) is { } quest) {
                (Class, Reason) = ("wish", ShortName(quest.Name));
                return;
            }

            if (actions.FirstOrDefault(a => HasPrefix(a, GivePrefixes)) is { } give) {
                (Class, Reason) = ("give", ShortName(give.Name));
                return;
            }

            // Characters belong to conversations and wishes, and benches to the bench rules
            var npcComponent = (record.Components ?? []).FirstOrDefault(c =>
                c != "PlayMakerNPC" && (c.Contains("NPC", StringComparison.Ordinal) || c.Contains("Npc", StringComparison.Ordinal))
            );
            if (record.ObjectName.Contains("NPC", StringComparison.Ordinal) || npcComponent != null) {
                (Class, Reason) = ("npc", npcComponent ?? "name");
                return;
            }

            if (record.States.SelectMany(s => s.Transitions).Concat(record.GlobalTransitions ?? [])
                .Any(t => Split(t).Event.StartsWith("BENCHREST", StringComparison.Ordinal))) {
                (Class, Reason) = ("bench", "BENCHREST");
                return;
            }

            if (actions.FirstOrDefault(a => ShortName(a.Name).StartsWith("RunDialogue", StringComparison.Ordinal)) is { } talk) {
                (Class, Reason) = ("talk", ShortName(talk.Name));
                return;
            }

            if (actions.FirstOrDefault(a => ShortName(a.Name).StartsWith("BeginSceneTransition", StringComparison.Ordinal)) is { } travel) {
                (Class, Reason) = ("travel", ShortName(travel.Name));
                return;
            }

            var payment = prompt.SelectMany(name => _states[name].Structured).FirstOrDefault(a => HasPrefix(a, PaymentPrefixes));
            if (payment == null || starts.Count == 0) {
                Reason = payment == null ? "no payment" : "no world part";
                return;
            }

            (Class, Reason) = ("mechanism", ShortName(payment.Name));
            WorldStarts.AddRange(starts);
            _world.UnionWith(Reach(starts, idle));
            foreach (var name in _world) {
                foreach (var action in _states[name].Structured) {
                    if (MayTouchHero(action)) {
                        HeroActions.Add($"{name}:{ShortName(action.Name)}({string.Join(",", action.Params.Select(ParamText))})");
                    }
                }
            }
        }

        private string ParamText(ParamValue param) {
            if (param.Variable == null) {
                return $"{param.Field}={param.Value}";
            }

            var key = ActionParams.VariableKey(param.Type, param.Variable);
            return key != null && Record.Variables != null && Record.Variables.TryGetValue(key, out var value)
                ? $"{param.Field}={value}"
                : $"{param.Field}=var:{param.Variable}";
        }

        private static bool IsPrompt(StateData state) {
            return state.Structured.Any(a => HasPrefix(a, PromptPrefixes)) ||
                   state.Transitions.Any(t => WaitEvents.Contains(Split(t).Event));
        }

        /// <summary>
        /// Actions in the part of a mechanism that changes the world which may touch the hero or the dialogue, and so
        /// may need to be skipped when the partner's game replays it.
        /// </summary>
        private static bool MayTouchHero(ActionParamsData action) {
            var shortName = ShortName(action.Name);
            return HasPrefix(action, PromptPrefixes) || shortName.Contains("Hero", StringComparison.Ordinal) ||
                   shortName is "EndDialogue" or "SendMessageV2" or "SendMessage" or "ActivateInteractible" ||
                   shortName.StartsWith("Tk2dPlayAnimation", StringComparison.Ordinal) ||
                   action.Params.Any(p => p.Value?.Contains("Hero", StringComparison.Ordinal) == true);
        }

        private HashSet<string> Reach(IEnumerable<string> starts, HashSet<string> idle) {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var start in starts) {
                if (_states.ContainsKey(start) && reached.Add(start)) {
                    queue.Enqueue(start);
                }
            }

            while (queue.Count > 0) {
                var state = _states[queue.Dequeue()];
                foreach (var (_, to) in state.Transitions.Select(Split)) {
                    if (to != null && _states.ContainsKey(to) && !idle.Contains(to) && reached.Add(to)) {
                        queue.Enqueue(to);
                    }
                }
            }

            return reached;
        }

        /// <summary>
        /// Prints every state with its marks (Idle, Prompt, world Start, World), its actions and its transitions.
        /// </summary>
        public void Dump() {
            foreach (var state in Record.States) {
                var marks = (IdleStates.Contains(state.Name) ? "I" : "") + (PromptStates.Contains(state.Name) ? "P" : "") +
                            (WorldStarts.Contains(state.Name) ? "S" : "") + (_world.Contains(state.Name) ? "W" : "");
                Console.WriteLine($"   [{marks}] {state.Name}: {string.Join(", ", state.Transitions)}");
                foreach (var action in state.Structured) {
                    Console.WriteLine($"       {ShortName(action.Name)}({string.Join(", ", action.Params.Select(ParamText))})");
                }
            }

            foreach (var transition in Record.GlobalTransitions ?? []) {
                Console.WriteLine($"   (global) {transition}");
            }
        }
    }
}
