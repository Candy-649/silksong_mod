using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// For --moves: looks through the FSMs of the creatures that SSMP syncs for moves that are single deterministic runs,
/// the way the theatre boss's tornado is: entered from a state that decides (dice or the player's position), then a
/// chain of states that rolls no dice, does not look at a player after its first two states, moves the body itself and
/// ends by itself (timers, FINISHED, wall or ground checks, animation end). Such a move could be started on the scene
/// client's copy by one message and run there, instead of being drawn from a stream of positions.
///
/// Writes moves.tsv (one line per candidate or near-miss, with the reason) and moves.txt (each run with its states,
/// actions and parameters) into the output folder. The FSMs of the registered creatures are kept in entity-fsms.json
/// there, so later runs skip the scan of the bundles; --rescan 1 scans again.
/// </summary>
internal static class MoveSurvey {
    // A run longer than this is a whole behaviour, not a move
    private const int MaxRunStates = 14;

    // How many states at the start of a run may look at a player once (aim, face), with the result kept in a variable,
    // the velocity or the facing
    private const int AimStates = 2;

    private static readonly HashSet<string> DiceActions = new(StringComparer.Ordinal) {
        "ArrayGetRandom", "ArrayListGetRandom", "ArrayListShuffle", "ArrayShuffle", "ChaseObject", "ChaseObjectSpread",
        "CreateObjectsRandom", "DistanceWalkServitor", "DistanceWalkServitorV", "DistanceWalkVertical", "FireAtTarget",
        "FloatAddRandom", "GetRandomChild", "GetRandomChildOffScreen", "GetRandomChildSceneSeed", "GetRandomObject",
        "RandomBool", "RandomBoolPercent", "RandomEvent", "RandomFloat", "RandomFloatEither", "RandomFloatV2",
        "RandomInt", "RandomVector2", "RandomVector3", "RandomWait", "RandomlyFlipFloat", "RandomlyFlipInt",
        "RandomlyFlipScale", "RandomlyFlipYScale", "SetRandomRotation", "SpawnRandomObjects",
        "SpawnRandomObjectsOverTime", "SpawnRandomObjectsOverTimeV2", "SpawnRandomObjectsRadial",
        "SpawnRandomObjectsRadialRandom", "SpawnRandomObjectsRadialV2", "SpawnRandomObjectsV2",
        "SpawnRandomObjectsVelocity", "Tk2dPlayRandomAnimationWithEvents", "TranslateRandom", "Vector2RandomValue",
        "WaitRandom", "WalkLeftRight", "PlayRandomAnimation", "SelectRandomFloat", "SelectRandomGameObject",
        "SelectRandomGameObjectV2", "SelectRandomInt", "SelectRandomString", "SelectRandomVector2",
        "SelectRandomVector3", "SendRandomEvent", "SendRandomEventFair", "SendRandomEventFairConditional",
        "SendRandomEventV2", "SendRandomEventV3", "SendRandomEventV3ActiveBool", "SendRandomEventV4",
        "IdleBuzz", "IdleBuzzV2", "IdleBuzzV3", "IdleBuzzV4", "SilkBossIdleBuzz", "WispBuzz",
    };

    // Dice that only pick something a player sees or hears, or what is thrown out of the body (which SSMP syncs on
    // its own), not where the body goes
    private static readonly Regex HarmlessDice = new(
        "^(Audio|PlayRandomSound|SelectRandomAudio|SelectRandomColor|Flicker|ObjectJitter|ShakePosition|" +
        "SetRandomMaterial|Tk2dSpriteSetIdRandom|FlingObjects|FlingFlashing|SpawnObjectFromGlobalPoolOverTime|" +
        "CreatePoolObjects|SpawnFromPool|SpawnProjectile)",
        RegexOptions.Compiled
    );

    // Actions that move the body that runs the FSM
    private static readonly Regex MoveAction = new(
        "^(SetVelocity|Accelerate|Decelerate|AddForce|AddRelativeForce|Translate|TranslateVelocity|AnimatePosition|" +
        "Animate[XYZ]Position|AnimateRigidBody|AnimateRigidBodyYPosition|iTweenMove|MoveTo|MovePosition|ChaseObject|" +
        "DistanceFly|DistanceWalk|StartWalker|FollowObject|FlyToward|SetPosition|SetPosition2d|SetPosition2D|" +
        "SetPositionV2|SetPosition2DV2|RigidBody2DSetPositionSafe|CurveFloat|AnimateFloat|EaseFloat|EaseVector)",
        RegexOptions.Compiled
    );

    // Of MoveAction, those that move the body only when they run every frame (once they put it somewhere)
    private static readonly Regex EveryFrameMove = new("^(Translate$|SetPosition)", RegexOptions.Compiled);

    // Of MoveAction, those that do something only when the value they ease feeds a position
    private static readonly Regex EaseAction = new("^(CurveFloat|AnimateFloat|EaseFloat|EaseVector)", RegexOptions.Compiled);

    // Actions that look at a player without naming them through a variable
    private static readonly Regex ImplicitHero = new(
        "^(GetHero|CheckHero(?!Performance)|CheckTargetDirection|CheckAlertRange|FindAlertRange|CheckCanSeeHero|" +
        "CheckFacingTarget|CheckPassedTarget|MovedPastTargetEvent|WaitForHeroInPosition|GetAngleToTarget|FireAtTarget|" +
        "ChaseObject|DistanceFly|DistanceWalk|CanHeroBeGrabbed|HeroTurnToFace|LineOfSight|UnalertTimer|" +
        "CompareUnalertTime|GetUnalertTime|CheckIsHeroInRange)",
        RegexOptions.Compiled
    );

    // Actions whose events come from what a player does (sings, steps into a trigger, is hit), which interrupt
    private static readonly Regex PlayerEventAction = new(
        "^(CheckHeroPerformanceRegion|Trigger2dEvent|TriggerEnter|TriggerStay|TriggerExit|OnDamagedHeroEvent|" +
        "CheckTrackTrigger|TrackTrigger|ListenFor|HeroDamaged|DamagedHero)",
        RegexOptions.Compiled
    );

    // Actions that follow something all the time, whatever their everyFrame says
    private static readonly Regex Continuous = new(
        "^(ChaseObject|MoveTowards|FollowObject|FlyToward|LookAt|SmoothLookAt|DistanceFly|DistanceWalk|" +
        "MovedPastTargetEvent|WaitForHeroInPosition|UnalertTimer)",
        RegexOptions.Compiled
    );

    private static readonly Regex Raycast = new("^(RayCast|Raycast|LineCast|Linecast|CheckGround|CheckWall|CheckIsCharacterGrounded|" +
                                                "CheckIsGrounded|CheckInsideTerrain|CheckTerrain)", RegexOptions.Compiled);

    private static readonly Regex Collision = new("^(CheckCollisionSide|Collision2dEvent|CollisionEnter|OnCollision|" +
                                                  "CheckCollisionSideEnter|CheckTouchingGround|CheckIsTouching)",
        RegexOptions.Compiled);

    private static readonly Regex HubName = new(
        "^(Idle|Choice|Choose|Decide|Decision|Select|Attack Choice|Move Choice|Rest|Wait|Ready|Range Check|" +
        "Check|Pause|Cooldown|Cool Down|Distance Check|Dormant|Init|Start|Alert|Patrol|Wander|Walk|Fly|Roam)\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Object variables that hold a player: the hero itself, or the creature's current target
    private static readonly Regex HeroVariableName = new(
        "^(hero|hornet|player|target|attack target|current target|hero target|chase target|aim target)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NumberSuffix = new(@"(\s*\(\d+\))+$|[\s_]*\d+$", RegexOptions.Compiled);

    private sealed class Cached {
        public string EntityType { get; set; }
        public string Scene { get; set; }
        public string Bundle { get; set; }
        public string ObjectPath { get; set; }
        public string ObjectName { get; set; }
        public string FsmName { get; set; }
        public string StartState { get; set; }
        public List<string> GlobalTransitions { get; set; }
        public Dictionary<string, string> Variables { get; set; }
        public List<string> Components { get; set; }
        public List<CachedState> States { get; set; }
    }

    private sealed class CachedState {
        public string Name { get; set; }
        public List<string> Transitions { get; set; }
        public List<string> Strings { get; set; }
        public List<CachedAction> Actions { get; set; }
    }

    private sealed class CachedAction {
        public string Name { get; set; }
        public List<string[]> Params { get; set; }
    }

    public static void Run(string bundleDir, string filter, string ssmpDir, string outDir, bool rescan) {
        Directory.CreateDirectory(outDir);
        var cachePath = Path.Combine(outDir, "entity-fsms.json");
        List<Cached> fsms;
        if (!rescan && File.Exists(cachePath)) {
            fsms = JsonSerializer.Deserialize<List<Cached>>(File.ReadAllText(cachePath));
            Console.WriteLine($"Read {fsms.Count} FSMs of registered creatures from {cachePath}");
        } else {
            fsms = Scan(bundleDir, filter, ssmpDir);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(fsms), new UTF8Encoding(false));
            Console.WriteLine($"Wrote {fsms.Count} FSMs of registered creatures to {cachePath}");
        }

        WriteActionCounts(fsms, Path.Combine(outDir, "action-counts.txt"));

        // One FSM per creature kind: the same creature in many rooms has the same FSM, give or take its variables
        var kinds = fsms
            .GroupBy(f => $"{f.EntityType}|{NumberSuffix.Replace(f.ObjectName, "")}|{f.FsmName}|{Signature(f)}")
            .Select(g => (Fsm: g.First(), Places: g.Select(f => f.Scene ?? Path.GetFileNameWithoutExtension(f.Bundle))
                .Distinct().ToList()))
            .ToList();
        Console.WriteLine($"{kinds.Count} distinct FSMs");

        var runs = new List<MoveRun>();
        foreach (var (fsm, places) in kinds) {
            runs.AddRange(FindRuns(fsm, places));
        }

        // The same run is found from every state that decides to start it
        runs = runs
            .GroupBy(r => $"{r.Fsm.EntityType}|{NumberSuffix.Replace(r.Fsm.ObjectName, "")}|{r.Fsm.FsmName}|" +
                          string.Join(",", r.States.Select(s => s.State.Name).OrderBy(n => n, StringComparer.Ordinal)))
            .Select(g => {
                var first = g.First();
                first.From = g.SelectMany(r => r.From).Distinct().ToList();
                return first;
            })
            .OrderBy(r => r.Verdict == "candidate" ? 0 : 1)
            .ThenByDescending(r => r.Score)
            .ToList();

        WriteTable(runs, Path.Combine(outDir, "moves.tsv"));
        WriteDetails(runs, Path.Combine(outDir, "moves.txt"));
        Console.WriteLine(
            $"{runs.Count(r => r.Verdict == "candidate")} candidates, {runs.Count(r => r.Verdict != "candidate")} near-misses; " +
            $"see {Path.Combine(outDir, "moves.tsv")} and moves.txt"
        );
    }

    private static string Signature(Cached fsm) =>
        string.Join(";", fsm.States.Select(s => s.Name + ":" + string.Join(",", s.Actions.Select(a => a.Name))))
            .GetHashCode().ToString("x8");

    private static List<Cached> Scan(string bundleDir, string filter, string ssmpDir) {
        var registry = EntityRegistryData.Load(ssmpDir);
        var fsmNamesByType = registry.Entries
            .GroupBy(e => e.Type)
            .ToDictionary(
                g => g.Key,
                g => g.Any(e => e.FsmNames == null) ? null : g.SelectMany(e => e.FsmNames).ToHashSet(StringComparer.Ordinal)
            );

        ActionParams.ReadStructuredParams = true;
        // Only the registered creatures are kept; the other FSMs lose their states as soon as their file is read
        BundleScanner.FileCollector = (_, _, _, records, _, _) => {
            foreach (var record in records.Where(r => r.Category != "entity")) {
                record.States = new List<StateData>();
                record.Variables = null;
            }
        };
        var scan = BundleScanner.Run(bundleDir, filter, 0, registry);
        foreach (var error in scan.Errors) {
            Console.WriteLine($"error: {error}");
        }

        return scan.Fsms
            .Where(r => r.Category == "entity" && r.States.Count > 0)
            .Where(r => !fsmNamesByType.TryGetValue(r.EntityType, out var names) || names == null || names.Count == 0 ||
                        names.Contains(r.FsmName))
            .Select(r => new Cached {
                EntityType = r.EntityType,
                Scene = r.Scene,
                Bundle = r.Bundle,
                ObjectPath = r.ObjectPath,
                ObjectName = r.ObjectName,
                FsmName = r.FsmName,
                StartState = r.StartState,
                GlobalTransitions = r.GlobalTransitions ?? new List<string>(),
                Variables = r.Variables ?? new Dictionary<string, string>(),
                Components = r.Components ?? new List<string>(),
                States = r.States.Select(s => new CachedState {
                    Name = s.Name,
                    Transitions = s.Transitions,
                    Strings = s.Strings,
                    Actions = s.Structured.Select(a => new CachedAction {
                        Name = ShortName(a.Name),
                        Params = a.Params.Select(p => new[] { p.Field, p.Type.ToString(), p.Value, p.Variable }).ToList(),
                    }).ToList(),
                }).ToList(),
            })
            .ToList();
    }

    private static void WriteActionCounts(List<Cached> fsms, string path) {
        var counts = fsms.SelectMany(f => f.States.SelectMany(s => s.Actions.Select(a => a.Name)))
            .GroupBy(n => n).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count(),7}  {g.Key}");
        File.WriteAllLines(path, counts);
    }

    private sealed class StateFacts {
        public CachedState State;
        public readonly List<string> Dice = new();
        public readonly List<string> HeroOnce = new();
        public readonly List<string> HeroAlways = new();
        public readonly List<string> Moves = new();
        public readonly List<string> Ends = new();
        public readonly List<string> Walls = new();
        public readonly List<string> Gravity = new();
        public readonly HashSet<string> OwnEvents = new(StringComparer.Ordinal);
        public readonly List<string> External = new();
        public readonly HashSet<string> Reads = new(StringComparer.Ordinal);
        public readonly HashSet<string> Writes = new(StringComparer.Ordinal);
        public float Speed;
        public float Seconds;
        public bool Moving => Moves.Count > 0;
        public bool Deciding;
    }

    private sealed class MoveRun {
        public Cached Fsm;
        public List<string> Places;
        public List<string> From = new();
        public List<StateFacts> States;
        public List<string> Leaves;
        public string Verdict;
        public List<string> Reasons = new();
        public float Speed;
        public float Seconds;
        public double Score;
        public List<string> Carried = new();
        public List<string> SetInRun = new();
        public Dictionary<string, int> Depths;
        public bool Reaction;
        public bool Boss;
    }

    private static IEnumerable<MoveRun> FindRuns(Cached fsm, List<string> places) {
        var heroVars = HeroVariables(fsm);
        var deciding = DecidingVariables(fsm);
        var facts = fsm.States.ToDictionary(s => s.Name, s => Facts(fsm, s, heroVars, deciding), StringComparer.Ordinal);
        var incoming = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var state in fsm.States) {
            foreach (var to in state.Transitions.Select(t => Split(t).To)) {
                if (to == null) {
                    continue;
                }

                if (!incoming.TryGetValue(to, out var set)) {
                    incoming[to] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(state.Name);
            }
        }

        // Global transitions start states from anywhere: those are interrupts (stun, death), never a run's road
        var globalTargets = fsm.GlobalTransitions.Select(t => Split(t).To).Where(t => t != null)
            .ToHashSet(StringComparer.Ordinal);

        bool Boundary(StateFacts f) => f.Deciding || HubName.IsMatch(f.State.Name) || f.State.Name == fsm.StartState ||
                                       globalTargets.Contains(f.State.Name) ||
                                       (incoming.TryGetValue(f.State.Name, out var from) && from.Count >= 4);

        // A state that rolls dice for the body or follows a player cannot be run from one message; one that looks at
        // a player once can, at the start of the run
        static bool Dirty(StateFacts f, int depth) =>
            f.Dice.Count > 0 || f.HeroAlways.Count > 0 || (depth >= AimStates && f.HeroOnce.Count > 0);

        foreach (var decider in facts.Values.Where(f => Boundary(f) || f.Dice.Count > 0)) {
            foreach (var transition in decider.State.Transitions) {
                var (_, entry) = Split(transition);
                if (entry == null || entry == decider.State.Name || !facts.TryGetValue(entry, out var entryFacts) ||
                    Boundary(entryFacts)) {
                    continue;
                }

                var full = Walk(facts, entryFacts, decider.State.Name, Boundary, null);
                var clean = Dirty(entryFacts, 0) ? null : Walk(facts, entryFacts, decider.State.Name, Boundary, Dirty);
                if (clean != null && clean.States.Any(s => s.Moving)) {
                    clean.Fsm = fsm;
                    clean.Places = places;
                    Judge(clean, fsm, null);
                    yield return clean;
                }

                // The move goes on past dice or a look at a player: worth naming so that it is not added by mistake
                if (full != null && full.States.Any(s => s.Moving && (clean == null || !clean.States.Contains(s)))) {
                    full.Fsm = fsm;
                    full.Places = places;
                    Judge(full, fsm, Dirty);
                    yield return full;
                }
            }
        }
    }

    /// <summary>
    /// Follows the states from the entry on along the transitions that the states' own actions send (FINISHED,
    /// timers, wall checks), up to a state that decides, a hub or the start state, and with dirty given, up to a
    /// state that rolls dice for the body or looks at a player.
    /// </summary>
    private static MoveRun Walk(Dictionary<string, StateFacts> facts, StateFacts entry, string decider,
        Func<StateFacts, bool> boundary, Func<StateFacts, int, bool> dirty) {
        var order = new List<StateFacts>();
        var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [entry.State.Name] = 0 };
        var leaves = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<StateFacts>();
        queue.Enqueue(entry);
        while (queue.Count > 0) {
            var current = queue.Dequeue();
            order.Add(current);
            if (order.Count > MaxRunStates) {
                return null;
            }

            var depth = depths[current.State.Name];
            foreach (var transition in current.State.Transitions) {
                var (eventName, to) = Split(transition);
                if (to == null || !current.OwnEvents.Contains(eventName) || depths.ContainsKey(to) ||
                    !facts.TryGetValue(to, out var next)) {
                    continue;
                }

                if (to == decider || boundary(next)) {
                    leaves.Add(to);
                    continue;
                }

                if (dirty != null && dirty(next, depth + 1)) {
                    leaves.Add($"{to} (dice/player)");
                    continue;
                }

                depths[to] = depth + 1;
                queue.Enqueue(next);
            }
        }

        return new MoveRun { From = [decider], States = order, Leaves = leaves.ToList(), Depths = depths };
    }

    private static readonly Regex ReactionName = new(
        "stun|death|dead|die|hit|tink|recoil|knock|flinch|bounce|pogo|parry|wall|floor|ceiling|roof|land|grab|caught|" +
        "fall|drop|wake|sleep|enter|entry|intro|appear|arrive|spawn|emerge|burrow|hide|leave|exit|retreat|escape",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static void Judge(MoveRun run, Cached fsm, Func<StateFacts, int, bool> dirty) {
        var movers = run.States.Where(s => s.Moving).ToList();
        run.Speed = run.States.Max(s => s.Speed);
        run.Seconds = run.States.Sum(s => s.Seconds);

        var problems = new List<string>();
        if (dirty != null) {
            foreach (var s in run.States.Where(s => dirty(s, run.Depths[s.State.Name]))) {
                problems.AddRange(s.Dice.Select(d => $"dice in [{s.State.Name}]: {d}"));
                problems.AddRange(s.HeroAlways.Select(h => $"follows a player in [{s.State.Name}]: {h}"));
                if (run.Depths[s.State.Name] >= AimStates) {
                    problems.AddRange(s.HeroOnce.Select(h => $"looks at a player mid-run in [{s.State.Name}]: {h}"));
                }
            }
        }

        // Every state must have a way on that its own actions give; one that only waits for others never ends alone
        foreach (var s in run.States) {
            var ownWayOut = s.State.Transitions.Any(t => s.OwnEvents.Contains(Split(t).Event));
            if (!ownWayOut) {
                problems.Add($"[{s.State.Name}] only ends on an event from outside ({string.Join(", ", s.External)})");
            }
        }

        run.Reasons.AddRange(problems);
        if (run.Speed < 10 && run.Seconds < 0.3f && problems.Count == 0) {
            run.Reasons.Add($"short or slow (speed {run.Speed:0.#}, timed {run.Seconds:0.##} s)");
        }

        run.Verdict = run.Reasons.Count == 0 ? "candidate" : "near-miss";
        run.Reaction = ReactionName.IsMatch(run.States[0].State.Name) && !AttackName.IsMatch(run.States[0].State.Name);

        // What the run reads and none of its states sets: what the start message must bring (constants of the room
        // included, which the copy already has unless something changed them)
        var written = run.States.SelectMany(s => s.Writes).ToHashSet(StringComparer.Ordinal);
        var reads = run.States.SelectMany(s => s.Reads).Where(v => !written.Contains(v)).ToHashSet(StringComparer.Ordinal);
        run.SetInRun = written.Where(v => fsm.Variables.ContainsKey($"float:{v}") || fsm.Variables.ContainsKey($"int:{v}"))
            .OrderBy(v => v, StringComparer.Ordinal).ToList();
        run.Carried = reads
            .Where(v => fsm.Variables.ContainsKey($"float:{v}") || fsm.Variables.ContainsKey($"int:{v}") ||
                        fsm.Variables.ContainsKey($"bool:{v}"))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        run.Boss = fsm.Components.Any(c => c is "BossSceneController" or "BossStatue") ||
                   fsm.ObjectPath.Contains("Boss", StringComparison.OrdinalIgnoreCase);
        run.Score = Math.Min(run.Speed, 60) * Math.Max(Math.Min(run.Seconds, 3), 0.2) + (run.Boss ? 50 : 0) +
                    movers.Count * 2 - (run.Reaction ? 40 : 0);
    }

    private static readonly Regex AttackName = new(
        "attack|antic|dash|charge|slash|lunge|tornado|spin|swoop|leap|jump|rush|stomp|slam|throw|shoot|strike|thrust|" +
        "roll|drill|dive|sweep|combo|stab|pounce|launch",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// The variables that decide something about the body: read by an action that moves it, by a timer or wait, or by
    /// an action that sends one of its state's events. Dice that only fill other variables (a projectile's speed, a
    /// pitch) do not decide the move.
    /// </summary>
    private static HashSet<string> DecidingVariables(Cached fsm) {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in fsm.States) {
            var events = state.Transitions.Select(t => Split(t).Event).ToHashSet(StringComparer.Ordinal);
            foreach (var action in state.Actions) {
                var decides = (MoveAction.IsMatch(action.Name) && TargetsOwner(action)) ||
                              action.Name.StartsWith("Wait", StringComparison.Ordinal) ||
                              action.Name is "FloatAdd" or "FloatSubtract" or "IntAdd" or "SetScale" or "FlipScale" or
                                  "SetGravity2dScale" or "SetPosition" or "SetPosition2d" or "SetPosition2D" ||
                              action.Params.Any(p => p[1] == "23" && p[2] != null && events.Contains(p[2]));
                if (!decides) {
                    continue;
                }

                foreach (var p in action.Params.Where(p => p[3] != null)) {
                    result.Add(p[3]);
                }
            }
        }

        // Two steps of copying: a rolled value put into another variable that decides
        for (var pass = 0; pass < 2; pass++) {
            foreach (var action in fsm.States.SelectMany(s => s.Actions).Where(a =>
                         a.Name is "SetFloatValue" or "FloatOperator" or "FloatMultiply" or "SetIntValue" or
                             "IntOperator" or "ConvertIntToFloat" or "SetVector2XY" or "SetVector3XYZ" or "FloatAdd" or
                             "FloatClamp" or "SetFloatToLowest" or "SetFloatToHighest")) {
                var stores = action.Params.Where(p => p[3] != null &&
                                                      (p[0].StartsWith("store", StringComparison.OrdinalIgnoreCase) ||
                                                       p[0] is "floatVariable" or "intVariable" or "vector2Variable" or
                                                           "vector3Variable"))
                    .Select(p => p[3]).ToList();
                if (stores.Any(result.Contains)) {
                    foreach (var p in action.Params.Where(p => p[3] != null)) {
                        result.Add(p[3]);
                    }
                }
            }
        }

        return result;
    }

    private static HashSet<string> HeroVariables(Cached fsm) {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in fsm.Variables.Keys.Where(k => k.StartsWith("gameobject:", StringComparison.Ordinal))) {
            var name = key["gameobject:".Length..];
            if (HeroVariableName.IsMatch(name)) {
                result.Add(name);
            }
        }

        // Whatever GetHero or a find of the hero's object stores
        foreach (var action in fsm.States.SelectMany(s => s.Actions)) {
            var findsHero = action.Name.StartsWith("GetHero", StringComparison.Ordinal) ||
                            (action.Name.StartsWith("Find", StringComparison.Ordinal) &&
                             action.Params.Any(p => p[2]?.Contains("Hero", StringComparison.Ordinal) == true));
            if (!findsHero) {
                continue;
            }

            foreach (var p in action.Params.Where(p => p[0].StartsWith("store", StringComparison.OrdinalIgnoreCase) &&
                                                       p[3] != null)) {
                result.Add(p[3]);
            }
        }

        return result;
    }

    private static StateFacts Facts(Cached fsm, CachedState state, HashSet<string> heroVars, HashSet<string> deciding) {
        var facts = new StateFacts { State = state };
        var transitionEvents = state.Transitions.Select(t => Split(t).Event).ToHashSet(StringComparer.Ordinal);
        facts.OwnEvents.Add("FINISHED");

        foreach (var action in state.Actions) {
            var name = action.Name;
            var text = Describe(action);

            foreach (var p in action.Params) {
                // Events an action of this state sends itself
                if (p[1] == "23" && !string.IsNullOrEmpty(p[2]) && transitionEvents.Contains(p[2]) &&
                    !PlayerEventAction.IsMatch(name) && !(Collision.IsMatch(name) && TouchesPlayer(action))) {
                    facts.OwnEvents.Add(p[2]);
                }

                if (p[3] != null) {
                    var field = p[0];
                    if (field.StartsWith("store", StringComparison.OrdinalIgnoreCase) ||
                        field is "result" or "storeResult" or "vector3Variable" or "vector2Variable") {
                        facts.Writes.Add(p[3]);
                    } else {
                        facts.Reads.Add(p[3]);
                    }

                    if (field is "floatVariable" or "intVariable" or "boolVariable") {
                        facts.Writes.Add(p[3]);
                    }
                }
            }

            // An event sent to the FSM itself (SendEvent to self, SendEventByScale) is the state's own too
            if (name.StartsWith("SendEvent", StringComparison.Ordinal) &&
                action.Params.Any(p => p[2] == "<self>" || p[2] == "<self owner>" || p[0] == "xScale")) {
                foreach (var p in action.Params.Where(p => p[1] == "23" && !string.IsNullOrEmpty(p[2]))) {
                    facts.OwnEvents.Add(p[2]);
                }
            }

            var storesOnlyElsewhere = !name.StartsWith("SendRandom", StringComparison.Ordinal) && name != "RandomEvent" &&
                                      !name.StartsWith("Wait", StringComparison.Ordinal) && !name.StartsWith("RandomWait", StringComparison.Ordinal) &&
                                      !MoveAction.IsMatch(name) &&
                                      !action.Params.Any(p => p[1] == "23" && !string.IsNullOrEmpty(p[2])) &&
                                      action.Params.Where(p => p[3] != null && (p[0].StartsWith("store", StringComparison.OrdinalIgnoreCase) ||
                                                                                p[0] is "floatVariable" or "intVariable" or "boolVariable"))
                                          .All(p => !deciding.Contains(p[3])) &&
                                      !name.Contains("Flip", StringComparison.Ordinal) && !name.Contains("Rotation", StringComparison.Ordinal) &&
                                      // A picked string is mostly an animation clip: the copy would show another one
                                      name != "SelectRandomString";
            if (DiceActions.Contains(name) && !HarmlessDice.IsMatch(name) && !FixedRoll(action) && !storesOnlyElsewhere) {
                facts.Dice.Add(text);
                if (name.StartsWith("SendRandomEvent", StringComparison.Ordinal) || name == "RandomEvent") {
                    facts.Deciding = true;
                }
            }

            // The hero is mostly a global variable of PlayMaker, which the FSM's own variables do not list
            var heroVar = !name.StartsWith("Audio", StringComparison.Ordinal) && !name.StartsWith("PlayAudio", StringComparison.Ordinal) && action.Params.Any(p => p[3] != null && p[1] is "19" or "20" &&
                                                 (heroVars.Contains(p[3]) || HeroVariableName.IsMatch(p[3])) &&
                                                 !p[3].Contains("Light", StringComparison.Ordinal));
            var implicitHero = ImplicitHero.IsMatch(name);
            if (heroVar || implicitHero) {
                var always = Continuous.IsMatch(name) || action.Params.Any(p =>
                    Regex.IsMatch(p[0], "^every(Frame|FixedUpdate|Update)$", RegexOptions.IgnoreCase) && p[2] == "True");
                // Collision and trigger checks with walls or the ground are not about a player
                if (!heroVar && Collision.IsMatch(name)) {
                    always = false;
                } else if (always) {
                    facts.HeroAlways.Add(text);
                } else {
                    facts.HeroOnce.Add(text);
                }

                // A state that branches on where a player is decides
                if (action.Params.Any(p => p[1] == "23" && !string.IsNullOrEmpty(p[2]) && transitionEvents.Contains(p[2]))) {
                    facts.Deciding = true;
                }
            }

            var everyFrame = action.Params.Any(p =>
                Regex.IsMatch(p[0], "^every(Frame|FixedUpdate|Update)$", RegexOptions.IgnoreCase) && p[2] == "True");
            if (MoveAction.IsMatch(name) && TargetsOwner(action) && !EaseAction.IsMatch(name) &&
                (!EveryFrameMove.IsMatch(name) || everyFrame)) {
                facts.Moves.Add(text);
                facts.Speed = Math.Max(facts.Speed, SpeedOf(action, fsm));
            }

            if (name is "Wait" or "WaitV2" or "WaitFrames") {
                var seconds = Number(action, "time", fsm);
                facts.Seconds = Math.Max(facts.Seconds, seconds);
                facts.Ends.Add(text);
            } else if (name.StartsWith("Tk2dPlayAnimationWith", StringComparison.Ordinal) ||
                       name.StartsWith("Tk2dWatchAnimation", StringComparison.Ordinal) ||
                       name.StartsWith("PlayAnimationWait", StringComparison.Ordinal)) {
                facts.Ends.Add(text);
            } else if (name is "FloatAdd" && action.Params.Any(p => p[0] == "perSecond" && p[2] == "True")) {
                facts.Ends.Add(text);
                // A timer counted down by the second lasts what it was set to
                var timer = action.Params.FirstOrDefault(p => p[0] == "floatVariable")?[2];
                if (Number(action, "add", fsm) < 0 && timer != null) {
                    facts.Seconds = Math.Max(facts.Seconds, Math.Abs(Resolve(timer, fsm)));
                }
            }

            // A move that stops when its own speed reads zero stops on a wall by collision, which a kinematic body
            // never has
            if (name.StartsWith("GetVelocity", StringComparison.Ordinal) && everyFrame) {
                facts.Walls.Add($"velocity-stall {text}");
            }

            if (Raycast.IsMatch(name)) {
                facts.Walls.Add($"raycast {text}");
            } else if (Collision.IsMatch(name)) {
                facts.Walls.Add($"collision {text}");
            }

            if (name.StartsWith("SetGravity", StringComparison.Ordinal) || name.StartsWith("SetIsKinematic", StringComparison.Ordinal) ||
                name.StartsWith("SetCollider", StringComparison.Ordinal) || name.StartsWith("SetBodyType", StringComparison.Ordinal)) {
                facts.Gravity.Add(text);
            }
        }

        foreach (var eventName in transitionEvents.Where(e => !facts.OwnEvents.Contains(e))) {
            facts.External.Add(eventName);
        }

        // Several roads out that the state's own checks pick between, other than a wall check, make it a decider
        // only when one of the checks reads a player; plain bool tests on the FSM's own variables do not
        return facts;
    }

    private static bool TouchesPlayer(CachedAction action) =>
        action.Params.Any(p => p[2] != null && (p[2].Contains("Player", StringComparison.Ordinal) ||
                                                p[2].Contains("Hero", StringComparison.Ordinal)));

    private static bool FixedRoll(CachedAction action) {
        string Get(string field) => action.Params.FirstOrDefault(p => p[0] == field)?[2];
        if (action.Name is "RandomFloat" or "RandomInt" or "RandomFloatV2") {
            var min = Get("min");
            var max = Get("max");
            return min != null && min == max && !min.StartsWith("var ", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TargetsOwner(CachedAction action) {
        var target = action.Params.FirstOrDefault(p => p[1] == "20" || p[0] is "gameObject" or "go" or "rigidBody2D");
        if (target == null) {
            return true;
        }

        return target[2] is "owner" or "var Self" or "<object var Self>";
    }

    private static float SpeedOf(CachedAction action, Cached fsm) {
        var best = 0f;
        foreach (var p in action.Params) {
            if (!Regex.IsMatch(p[0], "^(x|y|speed|xSpeed|ySpeed|targetSpeed|velocity|force|vector|toX|toY|maxSpeed)$",
                    RegexOptions.IgnoreCase)) {
                continue;
            }

            if (p[0] == "vector" && p[2] != null && p[2].StartsWith("(", StringComparison.Ordinal)) {
                foreach (var part in p[2].Trim('(', ')').Split(',')) {
                    if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v)) {
                        best = Math.Max(best, Math.Abs(v));
                    }
                }

                continue;
            }

            best = Math.Max(best, Math.Abs(Resolve(p[2], fsm)));
        }

        return best;
    }

    private static float Number(CachedAction action, string field, Cached fsm) =>
        Resolve(action.Params.FirstOrDefault(p => p[0] == field)?[2], fsm);

    private static float Resolve(string value, Cached fsm) {
        if (value == null) {
            return 0;
        }

        if (value.StartsWith("var ", StringComparison.Ordinal)) {
            var name = value[4..];
            {
                var stored = fsm.Variables.TryGetValue($"float:{name}", out var storedText) &&
                             float.TryParse(storedText, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var storedValue)
                    ? Math.Abs(storedValue)
                    : 0f;
                // Set somewhere in the FSM with a literal: take the largest such literal
                var best = 0f;
                foreach (var a in fsm.States.SelectMany(s => s.Actions).Where(a => a.Name is "SetFloatValue" or "RandomFloat")) {
                    if (a.Params.Any(p => p[3] == name && p[0] is "floatVariable" or "storeResult")) {
                        foreach (var p in a.Params.Where(p => p[0] is "floatValue" or "min" or "max")) {
                            if (float.TryParse(p[2], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var v)) {
                                best = Math.Max(best, Math.Abs(v));
                            }
                        }
                    }
                }

                return Math.Max(best, stored);
            }
        }

        return float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    private static string Describe(CachedAction action) {
        var interesting = action.Params
            .Where(p => p[2] != null && p[2] != "False" && p[2] != "0" && p[2] != "\"\"" && p[2] != "null" &&
                        !p[2].StartsWith("<type", StringComparison.Ordinal) &&
                        !p[0].Contains("audio", StringComparison.OrdinalIgnoreCase) &&
                        !p[0].Contains("debug", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p[0]}={p[2]}");
        return $"{action.Name}({string.Join(", ", interesting)})";
    }

    private static (string Event, string To) Split(string transition) {
        var arrow = transition.IndexOf("->", StringComparison.Ordinal);
        return arrow < 0 ? (transition, null) : (transition[..arrow], transition[(arrow + 2)..]);
    }

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOfAny(['.', '+']) + 1)..];

    private static void WriteTable(List<MoveRun> runs, string path) {
        var sb = new StringBuilder();
        sb.AppendLine("verdict\tscore\ttype\tobject\tfsm\tentry\tstates\tfrom\tleaves\tspeed\ttimed_s\twalls\tgravity\tcarried\tinterrupts\treasons\tplaces");
        foreach (var r in runs) {
            var interrupts = r.States.SelectMany(s => s.External).Distinct().Concat(r.Fsm.GlobalTransitions.Select(t => Split(t).Event))
                .Distinct();
            sb.AppendLine(string.Join("\t",
                r.Verdict, r.Score.ToString("0"), r.Fsm.EntityType, NumberSuffix.Replace(r.Fsm.ObjectName, ""), r.Fsm.FsmName,
                r.States[0].State.Name, string.Join(" > ", r.States.Select(s => s.State.Name)), string.Join(", ", r.From),
                string.Join(", ", r.Leaves), r.Speed.ToString("0.#"), r.Seconds.ToString("0.##"),
                string.Join(" | ", r.States.SelectMany(s => s.Walls).Distinct().Select(w => w.Split('(')[0])),
                string.Join(" | ", r.States.SelectMany(s => s.Gravity).Distinct()),
                string.Join(", ", r.Carried), string.Join(", ", interrupts),
                string.Join(" ; ", r.Reasons), string.Join(", ", r.Places.Take(4)) + (r.Places.Count > 4 ? $" +{r.Places.Count - 4}" : "")
            ));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteDetails(List<MoveRun> runs, string path) {
        var sb = new StringBuilder();
        foreach (var r in runs) {
            sb.AppendLine($"== {r.Verdict} {r.Score:0} | {r.Fsm.EntityType} | {r.Fsm.ObjectPath} | {r.Fsm.FsmName} | " +
                          $"from [{string.Join(", ", r.From)}] to [{string.Join(", ", r.Leaves)}] | speed {r.Speed:0.#} timed {r.Seconds:0.##} s");
            sb.AppendLine($"   places: {string.Join(", ", r.Places)}");
            sb.AppendLine($"   globals: {string.Join("; ", r.Fsm.GlobalTransitions)}");
            sb.AppendLine($"   carried: {string.Join(", ", r.Carried.Select(v => $"{v}={Value(r.Fsm, v)}"))}; set in the run: {string.Join(", ", r.SetInRun)}");
            foreach (var reason in r.Reasons) {
                sb.AppendLine($"   ! {reason}");
            }

            foreach (var s in r.States) {
                sb.AppendLine($"  [{s.State.Name}] -> {string.Join("; ", s.State.Transitions)}");
                foreach (var a in s.State.Actions) {
                    sb.AppendLine($"      {Describe(a)}");
                }
            }

            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Value(Cached fsm, string name) {
        foreach (var type in new[] { "float", "int", "bool" }) {
            if (fsm.Variables.TryGetValue($"{type}:{name}", out var value)) {
                return value;
            }
        }

        return "?";
    }
}
