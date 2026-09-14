using AssetsTools.NET.Extra;

/// <summary>
/// For --rooms: finds the events that start the fight of each boss scene the way SSMP's BossRoomCoop does, from the
/// FSMs in the assets, to check which events co-op holds back until every player is in the room.
/// </summary>
internal static class RoomAnalyzer {
    private const int MaxSteps = 8;
    private const string Finished = "FINISHED";

    private static readonly string[] DetectionPrefixes = [
        "CheckAlertRange", "CheckHeroPerformanceRegion", "Trigger2dEvent", "CheckXPosition", "CheckYPosition",
        "TriggerEnterEventSubscribe", "CheckTrackTriggerCount", "CheckCanSeeHero",
    ];

    private static readonly HashSet<string> CloseEvents = new(StringComparer.Ordinal) { "BG CLOSE", "BG QUICK CLOSE" };

    private static readonly HashSet<string> OpenEvents = new(StringComparer.Ordinal) {
        "BG OPEN", "BG QUICK OPEN", "BG DESTROY",
    };

    public static void Run(string bundleDir, string scenes, string ssmpDir) {
        var registry = EntityRegistryData.Load(ssmpDir);
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();
        ActionParams.ReadStructuredParams = true;

        var sceneNames = scenes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var paths = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Where(path => {
                var name = Path.GetFileName(path);
                var rel = Path.GetRelativePath(bundleDir, path).Replace('\\', '/');
                return name.Contains("fsmtemplates", StringComparison.Ordinal) ||
                       (rel.StartsWith("scenes_scenes_scenes/", StringComparison.Ordinal) &&
                        sceneNames.Contains(Path.GetFileNameWithoutExtension(name)));
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
        foreach (var scene in result.Fsms.Where(r => r.Scene != null).GroupBy(r => r.Scene).OrderBy(g => g.Key)) {
            foreach (var room in scene.Where(r => BossRoot(r.ObjectPath) != null).GroupBy(r => BossRoot(r.ObjectPath))) {
                AnalyzeRoom(scene.Key, room.Key, room.ToList());
            }
        }
    }

    /// <summary>
    /// Gets the path of the outermost boss scene that an object is in, or null.
    /// </summary>
    private static string BossRoot(string objectPath) {
        var parts = (objectPath ?? "").Split('/');
        for (var i = 0; i < parts.Length; i++) {
            if (parts[i].StartsWith("Boss Scene", StringComparison.Ordinal)) {
                return string.Join("/", parts.Take(i + 1));
            }
        }

        return null;
    }

    private static void AnalyzeRoom(string scene, string root, List<FsmRecord> records) {
        var graphs = new List<Graph>();
        foreach (var record in records) {
            if (record.InArena || record.States == null) {
                continue;
            }

            var events = record.States.SelectMany(s => s.Transitions)
                .Concat(record.GlobalTransitions ?? [])
                .Select(t => Split(t).Event)
                .ToList();
            if (events.Any(CloseEvents.Contains) && events.Any(OpenEvents.Contains)) {
                continue;
            }

            graphs.Add(new Graph(record));
        }

        var fightEvents = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 8; round++) {
            var changed = false;
            foreach (var graph in graphs) {
                changed |= graph.AddSendersOf(fightEvents);
                graph.UpdateLeading();
                foreach (var eventName in graph.StartEvents()) {
                    changed |= fightEvents.Add(eventName);
                }
            }

            if (!changed) {
                break;
            }
        }

        Console.WriteLine($"== {scene} / {root}");
        Console.WriteLine($"   fight events: {string.Join(", ", fightEvents)}");
        foreach (var graph in graphs) {
            var starts = graph.EventStarts(graph.PreFightStates());
            if (starts.Count == 0 && graph.FightStates.Count == 0) {
                continue;
            }

            var entity = graph.Record.Category == "entity" ? " [entity]" : "";
            Console.WriteLine($"   {graph.Record.ObjectPath} · {graph.Record.FsmName}{entity} (start {graph.Record.StartState})");
            Console.WriteLine($"      fight states: {string.Join(", ", graph.FightStates)}");
            if (starts.Count > 0) {
                Console.WriteLine($"      event starts: {string.Join("; ", starts)}");
            }
        }
    }

    private static (string Event, string To) Split(string transition) {
        var index = transition.IndexOf("->", StringComparison.Ordinal);
        return index < 0 ? (transition, null) : (transition[..index], transition[(index + 2)..]);
    }

    private sealed class Graph {
        public readonly FsmRecord Record;
        public readonly HashSet<string> FightStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _ownEvents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _sentEvents = new(StringComparer.Ordinal);
        private readonly List<(string State, string Event, string To)> _transitions = [];
        private readonly List<(string Event, string To)> _globals = [];
        private readonly HashSet<string> _stateNames = new(StringComparer.Ordinal);
        private HashSet<string> _leading = new(StringComparer.Ordinal);

        public Graph(FsmRecord record) {
            Record = record;
            var isEntity = record.Category == "entity";
            foreach (var state in record.States) {
                _stateNames.Add(state.Name);
                var own = new HashSet<string>(StringComparer.Ordinal) { Finished };
                var sent = new List<string>();
                foreach (var action in state.Structured) {
                    var shortName = action.Name[(action.Name.LastIndexOf('.') + 1)..];
                    var sentEvent = shortName is "SendEventByName" or "SendEventByNameV2"
                        ? action.Params.FirstOrDefault(p => p.Field == "sendEvent")?.Value
                        : shortName.StartsWith("SendEventToRegister", StringComparison.Ordinal)
                            ? action.Params.FirstOrDefault(p => p.Field == "eventName")?.Value
                            : null;
                    if (sentEvent != null) {
                        sent.Add(sentEvent);
                        if (CloseEvents.Contains(sentEvent)) {
                            FightStates.Add(state.Name);
                        }
                    }

                    if (isEntity && shortName == "DisplayBossTitle") {
                        FightStates.Add(state.Name);
                    }

                    if (!DetectionPrefixes.Any(prefix => shortName.StartsWith(prefix, StringComparison.Ordinal))) {
                        foreach (var parameter in action.Params) {
                            if (parameter.Type == 23 && !string.IsNullOrEmpty(parameter.Value)) {
                                own.Add(parameter.Value);
                            }
                        }
                    }
                }

                _ownEvents[state.Name] = own;
                _sentEvents[state.Name] = sent;
                foreach (var transition in state.Transitions) {
                    var (eventName, to) = Split(transition);
                    _transitions.Add((state.Name, eventName, to));
                }
            }

            foreach (var transition in record.GlobalTransitions ?? []) {
                _globals.Add(Split(transition));
            }
        }

        public bool AddSendersOf(HashSet<string> fightEvents) {
            var changed = false;
            foreach (var pair in _sentEvents) {
                if (pair.Value.Any(fightEvents.Contains)) {
                    changed |= FightStates.Add(pair.Key);
                }
            }

            return changed;
        }

        public void UpdateLeading() {
            var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (state, eventName, to) in _transitions) {
                if (to == null || !IsOwn(state, eventName)) {
                    continue;
                }

                if (!sources.TryGetValue(to, out var list)) {
                    list = [];
                    sources[to] = list;
                }

                list.Add(state);
            }

            var steps = new Dictionary<string, int>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var fightState in FightStates) {
                steps[fightState] = 0;
                queue.Enqueue(fightState);
            }

            while (queue.Count > 0) {
                var state = queue.Dequeue();
                if (steps[state] >= MaxSteps || !sources.TryGetValue(state, out var list)) {
                    continue;
                }

                foreach (var source in list) {
                    if (steps.TryAdd(source, steps[state] + 1)) {
                        queue.Enqueue(source);
                    }
                }
            }

            _leading = new HashSet<string>(steps.Keys, StringComparer.Ordinal);
        }

        public IEnumerable<string> StartEvents() {
            foreach (var (state, eventName, to) in _transitions) {
                if (IsStart(state, eventName, to)) {
                    yield return eventName;
                }
            }

            foreach (var (eventName, to) in _globals) {
                if (IsStart(null, eventName, to)) {
                    yield return eventName;
                }
            }
        }

        public HashSet<string> PreFightStates() {
            var preFight = new HashSet<string>(StringComparer.Ordinal);
            var start = Record.StartState;
            if (string.IsNullOrEmpty(start) || !_stateNames.Contains(start) || FightStates.Contains(start)) {
                return preFight;
            }

            var queue = new Queue<string>();
            preFight.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0) {
                var current = queue.Dequeue();
                foreach (var (state, _, to) in _transitions) {
                    if (state == current && to != null && _stateNames.Contains(to) && !FightStates.Contains(to) &&
                        preFight.Add(to)) {
                        queue.Enqueue(to);
                    }
                }
            }

            return preFight;
        }

        public List<string> EventStarts(HashSet<string> preFight) {
            var starts = new List<string>();
            foreach (var (state, eventName, to) in _transitions) {
                if (preFight.Contains(state) && IsStart(state, eventName, to)) {
                    starts.Add($"{state}/{eventName}");
                }
            }

            foreach (var (eventName, to) in _globals) {
                if (IsStart(null, eventName, to)) {
                    starts.Add($"(global)/{eventName}");
                }
            }

            return starts;
        }

        private bool IsStart(string state, string eventName, string to) {
            return !string.IsNullOrEmpty(eventName) && eventName != Finished && to != null && _leading.Contains(to) &&
                   (state == null || !IsOwn(state, eventName));
        }

        private bool IsOwn(string state, string eventName) {
            return eventName != null && _ownEvents.TryGetValue(state, out var events) && events.Contains(eventName);
        }
    }
}
