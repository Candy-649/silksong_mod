using System.Text;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// For --hazards: lists what can hurt a player in each room and is decided by the room itself, which each game runs by
/// itself in co-op unless SSMP shares it. In the fight on the stage two controllers of the room picked where flares
/// burst and which floor opened, each game by its own dice, and nobody saw it until a video showed rings on one side
/// that never hit the other (USER 10-13). Nothing that compares the two games while they play can see a moment like
/// that, so this looks for the kind of thing it was, all through the game.
///
/// A thing hurts if it carries DamageHero, CogMultiHitter or an FSM named "damages_hero", or if an FSM state hurts the
/// hero directly; these are all the ways into HeroController.TakeDamage. A creature's own parts are left out: the scene
/// host runs the creature and the other game replays it. The scene file is read the way the room stands once its
/// objects woke, since FixParentedJoints and Rb2dFollowWithVelocity take objects out from under their parents in Awake,
/// before SSMP looks for creatures. What is left belongs to the room, and each game decides for itself when and where
/// it hurts if a room FSM that reaches it
/// - rolls dice that decide something (which parts, where, after what wait), not only a sound or a flicker,
/// - goes round a loop by itself, from the start or once something set it going, or
/// - waits for its own player to come near, which in the other game only that game's player does.
/// An FSM reaches what is under its own object, what it names (switches, moves, tells), what the FSMs it tells reach,
/// and the prefabs it spawns. Each such FSM is printed with how it reaches what hurts, what sets it going and where
/// SSMP's code names it. Things that hurt with no FSM reaching them are counted by the scripts on them and above them,
/// for the ones that keep their own time or roll their own dice.
/// </summary>
internal static class HazardAudit {
    /// <summary>
    /// The FSM actions that roll dice: every action whose methods call UnityEngine.Random, or a helper of the game that
    /// does (ActionHelpers.GetRandomWeightedIndex, Probability, the picking and shuffling extensions, FlingUtils),
    /// found with ILPeek --callers.
    /// </summary>
    private static readonly HashSet<string> DiceActions = new(StringComparer.Ordinal) {
        "ArrayGetRandom", "ArrayListGetRandom", "ArrayListShuffle", "ArrayShuffle", "AudioPlayRandom",
        "AudioPlayRandomSingle", "AudioPlayRandomVoice", "AudioPlayerOneShot", "AudioPlayerOneShotSingle",
        "AudioPlayerOneShotSingleV2", "ChaseObject", "ChaseObjectSpread", "CreateObjectsRandom", "CreatePoolObjects",
        "DistanceWalkServitor", "DistanceWalkServitorV", "DistanceWalkVertical", "EnemySingControl", "FireAtTarget",
        "Flicker", "FlingFlashingGeo", "FlingObject", "FlingObjects", "FlingObjectsFromGlobalPool",
        "FlingObjectsFromGlobalPoolTime", "FlingObjectsFromGlobalPoolV2", "FlingObjectsFromGlobalPoolV3",
        "FlingObjectsFromGlobalPoolVel", "FlingObjectsFromGlobalPoolVelTime", "FlingObjectsV2", "FloatAddRandom",
        "GetRandomChild", "GetRandomChildOffScreen", "GetRandomChildSceneSeed", "GetRandomObject", "IdleBuzz",
        "IdleBuzzV2", "IdleBuzzV3", "IdleBuzzV4", "ObjectJitter", "ObjectJitterLocal", "ObjectJitterOnRender",
        "PlayEnemySingAudio", "PlayRandomAudioClipTableLooped", "RandomBool", "RandomBoolPercent", "RandomEvent",
        "RandomFloat", "RandomFloatEither", "RandomFloatV2", "RandomInt", "RandomVector2", "RandomVector3",
        "RandomWait", "RandomiseAudioPosition", "RandomlyFlipFloat", "RandomlyFlipInt", "RandomlyFlipScale",
        "RandomlyFlipYScale", "SetRandomMaterial", "SetRandomRotation", "ShakePosition", "ShakePositionV2",
        "SilkBossIdleBuzz", "SpawnFromPool", "SpawnFromPoolV2", "SpawnObjectFromGlobalPoolDelay",
        "SpawnObjectFromGlobalPoolOverTimeV2", "SpawnProjectileV2", "SpawnRandomObjects", "SpawnRandomObjectsOverTime",
        "SpawnRandomObjectsOverTimeV2", "SpawnRandomObjectsRadial", "SpawnRandomObjectsRadialRandom",
        "SpawnRandomObjectsRadialV2", "SpawnRandomObjectsV2", "SpawnRandomObjectsVelocity",
        "Tk2dPlayAnimationChildrenRandom", "Tk2dPlayRandomAnimationWithEvents", "Tk2dSpriteSetIdRandom",
        "TranslateRandom", "Vector2RandomValue", "WaitRandom", "WalkLeftRight", "WispBuzz",
        "tk2dPlayAnimAfterPreviousCompleteV2", "PlayRandomAnimation", "PlayRandomSound", "SelectRandomAudioClipFair",
        "SelectRandomColor", "SelectRandomFloat", "SelectRandomGameObject", "SelectRandomGameObjectV2",
        "SelectRandomInt", "SelectRandomString", "SelectRandomVector2", "SelectRandomVector3", "SendRandomEvent",
        "SendRandomEventFair", "SendRandomEventFairConditional", "SendRandomEventV2", "SendRandomEventV3",
        "SendRandomEventV3ActiveBool", "SendRandomEventV4",
    };

    /// <summary>
    /// Of <see cref="DiceActions"/>, those whose dice only change a sound, a flicker, a shake, how a sprite looks, or how
    /// a creature drifts while it idles.
    /// </summary>
    private static readonly HashSet<string> CosmeticDiceActions = new(StringComparer.Ordinal) {
        "AudioPlayRandom", "AudioPlayRandomSingle", "AudioPlayRandomVoice", "AudioPlayerOneShot",
        "AudioPlayerOneShotSingle", "AudioPlayerOneShotSingleV2", "EnemySingControl", "Flicker", "FlingFlashingGeo",
        "IdleBuzz", "IdleBuzzV2", "IdleBuzzV3", "IdleBuzzV4", "ObjectJitter", "ObjectJitterLocal",
        "ObjectJitterOnRender", "PlayEnemySingAudio", "PlayRandomAudioClipTableLooped", "RandomiseAudioPosition",
        "SetRandomMaterial", "ShakePosition", "ShakePositionV2", "SilkBossIdleBuzz", "Tk2dSpriteSetIdRandom",
        "WispBuzz", "PlayRandomSound", "SelectRandomAudioClipFair", "SelectRandomColor",
    };

    /// <summary>
    /// Of <see cref="DiceActions"/>, those that only keep what they rolled in a variable, which decides something only
    /// if something other than a sound or a look uses that variable.
    /// </summary>
    private static readonly HashSet<string> StoringDiceActions = new(StringComparer.Ordinal) {
        "ArrayGetRandom", "ArrayListGetRandom", "FloatAddRandom", "GetRandomChild", "GetRandomChildOffScreen",
        "GetRandomChildSceneSeed", "GetRandomObject", "RandomBool", "RandomBoolPercent", "RandomFloat",
        "RandomFloatEither", "RandomFloatV2", "RandomInt", "RandomVector2", "RandomVector3", "RandomlyFlipFloat",
        "RandomlyFlipInt", "SelectRandomFloat", "SelectRandomGameObject", "SelectRandomGameObjectV2", "SelectRandomInt",
        "SelectRandomString", "SelectRandomVector2", "SelectRandomVector3", "Vector2RandomValue",
    };

    /// <summary>
    /// Actions that only change a sound or how something looks, which a rolled value may go into without deciding
    /// anything.
    /// </summary>
    private static readonly Regex CosmeticAction = new(
        "Audio|Sound|Pitch|Volume|Colou?r|Alpha|Fade|Flicker|Jitter|Shake|Vibrat|Particle|Emission|Light|Tint|Material|" +
        "SpriteSetId|Flash|Music|Snapshot", RegexOptions.Compiled
    );

    /// <summary>
    /// Actions that play an animation, which a rolled clip name or frame may go into without deciding anything.
    /// </summary>
    private static readonly Regex AnimationAction = new(
        "^(Tk2dPlay|tk2dPlay|PlayAnimation|AnimatorPlay|PlayAnimator|Tk2dSpriteAnimator|SetAnimator)", RegexOptions.Compiled
    );

    /// <summary>
    /// Actions that notice the hero coming near or touching something, which in each game is only its own player.
    /// </summary>
    private static readonly Regex DetectionAction = new(
        "^(Trigger2d|TriggerStay|TriggerEnter|TriggerExit|Collision2d|CollisionEnter|CheckAlertRange|" +
        "CheckHeroPerformance|CheckTrackTrigger|CheckCanSee|CheckHero|HeroIn|CheckPlayer|CheckXPosition|CheckYPosition|" +
        "CheckTargetDirection|CheckInRange|CheckDistance|GetDistance|CheckHeroTouching|BoolTestHero)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Actions that send an event when something happens to their object from outside, such as a hit, a button or a
    /// creature, rather than when their own time is up.
    /// </summary>
    private static readonly Regex ListenAction = new(
        "^(ReceivedDamage|ListenFor|CheckHit|HitCheck|CheckTouch|TriggerEnterEventSubscribe|Collision|WaitForHero|" +
        "CheckCollisionSide|CheckIsGrounded|ConveyorBeltListener|ReceiveDamage|TakeDamage|HeroRelinquish)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Actions that make an object from a prefab or from an object of the room.
    /// </summary>
    private static readonly Regex SpawnAction = new("^(Spawn|Create(?!Empty)|Fling|Instantiate)", RegexOptions.Compiled);

    /// <summary>
    /// Actions that send an event to every FSM that listens for it, rather than to an object.
    /// </summary>
    private static readonly Regex SendAction = new("^(SendEvent|Broadcast|SendMessage)", RegexOptions.Compiled);

    /// <summary>
    /// Actions that change something a player could be hurt by: what is switched on, where it is, how it moves, what
    /// plays on it, what it makes and what it is told.
    /// </summary>
    private static readonly Regex EffectAction = new(
        "Activate|SetActive|Collider|Damage|Spawn|Create|Fling|Animation|Animator|Anim|Position|Translate|Move|" +
        "Velocity|Tween|Rotat|Scale|Send|Curve|Lerp|Throw|Shoot|Fire",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The fields of an action that hold what it rolled.
    /// </summary>
    private static readonly Regex StoreField = new(
        "^(store|result|output|floatVariable|intVariable|boolVariable|vector2?Variable|stringVariable|gameObject?Variable|" +
        "variable|randomGameObject|randomChild)", RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex ObjectReference = new(@"<object (?:(-?\d+):)?(-?\d+)>", RegexOptions.Compiled);
    private static readonly Regex TargetVariable = new(@"^<object (?:fsm )?var (.+?)(?: fsm .*| and children)?>$",
        RegexOptions.Compiled);
    private static readonly Regex NumberSuffix = new(@"(\s*\(\d+\))+$|[\s_]*\d+$", RegexOptions.Compiled);
    private static readonly Regex Literal = new("\"((?:[^\"\\\\\\n]|\\\\.)*)\"", RegexOptions.Compiled);

    /// <summary>
    /// How many FSMs of a room may listen for an event before it no longer says who it comes from.
    /// </summary>
    private const int MaxListeners = 12;

    /// <summary>
    /// How many steps of one FSM telling another are followed for what an FSM reaches.
    /// </summary>
    private const int MaxTellSteps = 3;

    private sealed class Node {
        public string Name;
        public bool Active;
        public long Parent;
        public readonly List<long> Children = new();
        public readonly List<string> Classes = new();
        public readonly List<string> FsmNames = new();
        public string Hurt;

        // An object that FixParentedJoints (its selfParent) or Rb2dFollowWithVelocity (itself) takes out to the top
        // of the scene in Awake, by path ID of the object or of its transform
        public long MovesOutTransform;
        public bool MovesOutSelf;
    }

    private sealed class FsmInfo {
        public FsmRecord Record;
        public string Scene;
        public string Path;
        public string Name;
        public string ParentName;
        public bool ActiveAtLoad;
        public bool UnderBossScene;
        public string Creature;
        public readonly HashSet<long> Refs = new();
        public readonly List<(string Key, string Action)> Spawns = new();
        public readonly List<string> Dice = new();
        public int CosmeticDice;
        public List<string> Loop;
        public bool LoopFromStart;
        public bool LoopTouches;
        public readonly List<string> Detection = new();
        public readonly HashSet<string> Broadcasts = new(StringComparer.Ordinal);
        public readonly HashSet<string> Heard = new(StringComparer.Ordinal);
        public readonly HashSet<long> Hazards = new();
        public readonly List<FsmInfo> Tells = new();
        public readonly List<FsmInfo> ToldBy = new();

        // Filled in once every bundle was read
        public readonly SortedSet<string> HazardNames = new(StringComparer.Ordinal);
        public readonly SortedSet<string> SpawnedHurting = new(StringComparer.Ordinal);
        public readonly SortedSet<string> SpawnedCreatures = new(StringComparer.Ordinal);
        public int HazardCount;

        // Dice that make or throw something, which decide something only if what they make or throw hurts: what
        // they name in the room and the prefabs they name, until those are known
        public readonly List<(string Text, List<long> Locals, List<string> Keys)> SpawnDice = new();

        // Whether all it does is hurt whoever touches its own object, which each game does for its own player, and
        // whether the only thing that hurts that it reaches is its own object, which decides that once its spawns are
        // known
        public bool Contact;
        public bool OwnOnly;

        // Whether it is part of something with a HealthManager that SSMP's registry doesn't name: an enemy that each
        // game runs by itself
        public bool Unregistered;

        public bool HasDecision => !Contact && (Dice.Count > 0 || (Loop != null && LoopTouches) || Detection.Count > 0);

        public string Kind => $"{NumberSuffix.Replace(Name, "")} | {Record.FsmName}";
    }

    private static EntityRegistryData _registry;
    private static readonly Dictionary<string, TemplateRecord> Templates = new(StringComparer.Ordinal);

    // Every object of a prefab file that hurts or holds something that does, by "file:path ID", with its prefab's name
    private static readonly Dictionary<string, string> HurtingPrefabs = new(StringComparer.Ordinal);

    // The prefabs that SSMP takes for creatures, by "file:path ID", with their entity type
    private static readonly Dictionary<string, string> CreaturePrefabs = new(StringComparer.Ordinal);

    private static readonly List<FsmInfo> RoomFsms = new();

    // Things that hurt with no FSM reaching them: per script on them or above them, how many and where
    private static readonly Dictionary<string, (int Count, SortedSet<string> Examples)> UnreachedClasses =
        new(StringComparer.Ordinal);

    private static int _roomHazards, _creatureHazards, _unreachedHazards, _scenes;

    public static void Run(string bundleDir, string filter, string ssmpDir, string outPath, string dossier = null) {
        _registry = EntityRegistryData.Load(ssmpDir);
        ActionParams.ReadStructuredParams = true;

        // The templates first, so that an FSM made from one has its states while its room is looked at
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();
        var templates = new ScanResult();
        foreach (var path in Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
                     .Where(path => Path.GetFileName(path).Contains("fsmtemplates", StringComparison.Ordinal))) {
            BundleScanner.ScanBundleAt(manager, bundleDir, path, scriptNames, _registry, templates);
            manager.UnloadAll();
        }

        foreach (var (key, template) in templates.Templates) {
            Templates[key] = template;
        }

        BundleScanner.FileCollector = Collect;
        var scan = BundleScanner.Run(bundleDir, filter, 0, _registry);
        BundleScanner.FileCollector = null;
        foreach (var error in scan.Errors) {
            Console.WriteLine($"error: {error}");
        }

        var literals = LoadLiterals(ssmpDir, scan);
        var candidates = Resolve();
        Write(candidates, literals, outPath);
        if (dossier != null) {
            PrintDossiers(candidates, new Regex(dossier, RegexOptions.IgnoreCase));
        }
    }

    /// <summary>
    /// Prints, for each kind of candidate whose name matches, its first FSM whole: where it is, its variables, who
    /// tells it and whom it tells, and every state with its actions, their parameters and where it goes next.
    /// </summary>
    private static void PrintDossiers(List<FsmInfo> candidates, Regex pattern) {
        foreach (var kind in candidates.Where(c => pattern.IsMatch(c.Kind)).GroupBy(c => c.Kind)) {
            var info = kind.First();
            var record = info.Record;
            Console.WriteLine();
            Console.WriteLine($"=== {kind.Key}: {kind.Select(i => i.Scene).Distinct().Count()} scenes " +
                              $"({string.Join(", ", kind.Select(i => i.Scene).Distinct().Take(8))})");
            Console.WriteLine($"  {info.Scene} | {info.Path} | {record.FsmName} (start {record.StartState}, " +
                              $"{(info.ActiveAtLoad ? "on" : "off")} at load{(record.Enabled ? "" : ", component off")}" +
                              $"{(info.Unregistered ? ", unregistered enemy" : "")})");
            Console.WriteLine($"  components: {string.Join(", ", record.Components ?? [])}");
            Console.WriteLine("  variables: " + string.Join("; ",
                (record.Variables ?? new Dictionary<string, string>()).Select(v => $"{v.Key}={v.Value}")));
            Console.WriteLine($"  contact {info.Contact}, spawns {info.Spawns.Count}, dice {string.Join(", ", info.Dice)}, " +
                              $"loop {(info.Loop == null ? "none" : string.Join(" > ", info.Loop))} " +
                              $"({(info.LoopTouches ? "changes things" : "changes nothing")}), " +
                              $"notices {string.Join(", ", info.Detection)}");
            Console.WriteLine($"  hurts {info.HazardCount}: {string.Join(", ", info.HazardNames)}; " +
                              $"spawns: {string.Join(", ", info.SpawnedHurting)} {string.Join(", ", info.SpawnedCreatures)}");
            Console.WriteLine("  told by: " + string.Join(", ", info.ToldBy.Select(t => $"{t.Path}/{t.Record.FsmName}" +
                (t.Creature is "" ? "" : $" [creature {t.Creature}]")).Distinct().Take(6)));
            Console.WriteLine("  tells: " + string.Join(", ",
                info.Tells.Select(t => $"{t.Path}/{t.Record.FsmName}").Distinct().Take(6)));
            Console.WriteLine($"  global: {string.Join("; ", record.GlobalTransitions ?? [])}");
            foreach (var state in record.States) {
                var actions = state.Structured.Select(a => {
                    var text = $"{ShortName(a.Name)}({string.Join(", ", a.Params.Select(p => $"{p.Field}={p.Value}"))})";
                    return text.Length > 220 ? text[..220] + "..." : text;
                });
                Console.WriteLine($"  [{state.Name}] {string.Join("; ", actions)}");
                if (state.Transitions.Count > 0) {
                    Console.WriteLine($"      -> {string.Join("; ", state.Transitions)}");
                }
            }
        }
    }

    private static void Collect(
        AssetsManager manager,
        AssetsFileInstance instance,
        FileContext context,
        List<FsmRecord> records,
        BundleRef bundle,
        string scene
    ) {
        var nodes = ReadNodes(manager, instance, context);
        foreach (var record in records) {
            if (nodes.TryGetValue(record.GameObjectPathId, out var owner)) {
                owner.FsmNames.Add(record.FsmName);
            }

            if (record.TemplateKey != null && Templates.TryGetValue(record.TemplateKey, out var template)) {
                record.States = template.States;
                record.GlobalTransitions = template.GlobalTransitions;
                record.StartState = template.StartState;
                record.TemplateName = template.Name;
            }
        }

        if (scene == null) {
            IndexPrefabs(instance.name, nodes);
            return;
        }

        _scenes++;
        AnalyzeRoom(scene, nodes, records, context);
    }

    /// <summary>
    /// Reads every object of a file with its parent, the scripts on it and whether it hurts, and puts the objects that
    /// are taken out from under their parents in Awake at the top.
    /// </summary>
    private static Dictionary<long, Node> ReadNodes(AssetsManager manager, AssetsFileInstance instance, FileContext context) {
        var nodes = new Dictionary<long, Node>();
        var objectOfTransform = new Dictionary<long, long>();
        var fatherOfTransform = new Dictionary<long, long>();
        foreach (var info in instance.file.GetAssetsOfType(AssetClassID.GameObject)) {
            var field = manager.GetBaseField(instance, info);
            var node = new Node { Name = field["m_Name"].AsString, Active = field.Get("m_IsActive")?.AsBool ?? true };
            nodes[info.PathId] = node;

            var components = field.Get("m_Component", "Array");
            foreach (var pair in components?.Children ?? []) {
                var pointer = pair.Get("component");
                if (pointer == null || pointer["m_FileID"].AsInt != 0) {
                    continue;
                }

                var componentInfo = instance.file.GetAssetInfo(pointer["m_PathID"].AsLong);
                if (componentInfo == null) {
                    continue;
                }

                var type = (AssetClassID) componentInfo.TypeId;
                if (type is AssetClassID.Transform or AssetClassID.RectTransform) {
                    var transform = manager.GetBaseField(instance, componentInfo);
                    objectOfTransform[componentInfo.PathId] = info.PathId;
                    fatherOfTransform[componentInfo.PathId] = transform["m_Father"]["m_PathID"].AsLong;
                    continue;
                }

                if (type is AssetClassID.Animator or AssetClassID.Animation) {
                    node.Classes.Add(type.ToString());
                    continue;
                }

                if (type != AssetClassID.MonoBehaviour) {
                    continue;
                }

                var className = context.MonoClasses.TryGetValue(componentInfo.PathId, out var known)
                    ? known
                    : context.ScriptClass(componentInfo);
                if (className == null) {
                    continue;
                }

                node.Classes.Add(className);
                switch (className) {
                    case "DamageHero": {
                        var behaviour = manager.GetBaseField(instance, componentInfo);
                        var damage = behaviour.Get("damageDealt")?.AsInt ?? 0;
                        var hazard = behaviour.Get("hazardType")?.AsInt ?? 0;
                        var enabled = behaviour.Get("m_Enabled")?.AsBool ?? true;
                        node.Hurt ??= $"DamageHero {damage}/{hazard}{(enabled ? "" : " off")}";
                        break;
                    }
                    case "CogMultiHitter":
                        node.Hurt ??= "CogMultiHitter";
                        break;
                    case "FixParentedJoints": {
                        var behaviour = manager.GetBaseField(instance, componentInfo);
                        if ((behaviour.Get("m_Enabled")?.AsBool ?? true) && (behaviour.Get("isActive")?.AsBool ?? false)) {
                            node.MovesOutTransform = behaviour.Get("selfParent", "m_PathID")?.AsLong ?? 0;
                        }

                        break;
                    }
                    case "Rb2dFollowWithVelocity": {
                        var behaviour = manager.GetBaseField(instance, componentInfo);
                        node.MovesOutSelf = (behaviour.Get("m_Enabled")?.AsBool ?? true) &&
                                            (behaviour.Get("deparent")?.AsBool ?? false);
                        break;
                    }
                }
            }
        }

        foreach (var (transformId, objectId) in objectOfTransform) {
            var fatherId = fatherOfTransform[transformId];
            if (fatherId != 0 && objectOfTransform.TryGetValue(fatherId, out var parentId)) {
                nodes[objectId].Parent = parentId;
            }
        }

        // Awake runs only on objects that are on in the scene, which is decided before anything moves
        var moves = new List<long>();
        foreach (var (id, node) in nodes) {
            if (!ActiveInHierarchy(nodes, id)) {
                continue;
            }

            if (node.MovesOutSelf) {
                moves.Add(id);
            }

            if (node.MovesOutTransform != 0) {
                if (objectOfTransform.TryGetValue(node.MovesOutTransform, out var moved)) {
                    moves.Add(moved);
                } else if (nodes.ContainsKey(node.MovesOutTransform)) {
                    moves.Add(node.MovesOutTransform);
                }
            }
        }

        foreach (var id in moves) {
            nodes[id].Parent = 0;
        }

        foreach (var (id, node) in nodes) {
            if (node.Parent != 0 && nodes.TryGetValue(node.Parent, out var parent)) {
                parent.Children.Add(id);
            }
        }

        return nodes;
    }

    private static bool ActiveInHierarchy(Dictionary<long, Node> nodes, long id) {
        for (var depth = 0; id != 0 && depth < 128 && nodes.TryGetValue(id, out var node); depth++) {
            if (!node.Active) {
                return false;
            }

            id = node.Parent;
        }

        return true;
    }

    private static IEnumerable<long> Ancestors(Dictionary<long, Node> nodes, long id) {
        for (var depth = 0; depth < 128 && nodes.TryGetValue(id, out var node) && node.Parent != 0; depth++) {
            id = node.Parent;
            yield return id;
        }
    }

    private static string PathOf(Dictionary<long, Node> nodes, long id) {
        var names = new List<string> { nodes[id].Name };
        names.AddRange(Ancestors(nodes, id).Select(a => nodes[a].Name));
        names.Reverse();
        return string.Join("/", names);
    }

    private static bool IsCorpse(Node node) {
        return node.Name.StartsWith("corpse", StringComparison.OrdinalIgnoreCase) ||
               node.Classes.Any(c => c is "Corpse" or "ActiveCorpse" or "CorpseItems");
    }

    /// <summary>
    /// The objects of a prefab file that hurt or hold something that does, and the prefabs SSMP takes for creatures.
    /// </summary>
    private static void IndexPrefabs(string file, Dictionary<long, Node> nodes) {
        foreach (var (id, node) in nodes) {
            if (node.Parent == 0 && _registry.Match(node.Name, node.FsmNames, null) is { } entry) {
                CreaturePrefabs[$"{file}:{id}"] = entry.Type;
            }

            if (node.Hurt == null) {
                continue;
            }

            var top = Ancestors(nodes, id).LastOrDefault();
            var name = $"{(top != 0 ? nodes[top].Name : node.Name)} ({node.Hurt})";
            HurtingPrefabs.TryAdd($"{file}:{id}", name);
            foreach (var ancestor in Ancestors(nodes, id)) {
                HurtingPrefabs.TryAdd($"{file}:{ancestor}", name);
            }
        }
    }

    private static void AnalyzeRoom(string scene, Dictionary<long, Node> nodes, List<FsmRecord> records, FileContext context) {
        // FSMs that hurt by themselves: the old damages_hero FSM, and states that hurt the hero directly
        foreach (var record in records) {
            if (!nodes.TryGetValue(record.GameObjectPathId, out var node)) {
                continue;
            }

            if (record.FsmName == "damages_hero") {
                node.Hurt ??= "damages_hero FSM";
            } else if (record.States?.Any(s => s.Actions.Any(a => ShortName(a).StartsWith("DamageHeroDirectly",
                           StringComparison.Ordinal))) == true) {
                node.Hurt ??= $"DamageHeroDirectly in {record.FsmName}";
            }
        }

        // The creature each object belongs to, as SSMP matches them: the object itself, then up its parents
        var creatureOf = new Dictionary<long, string>();
        string CreatureOf(long id) {
            if (id == 0 || !nodes.TryGetValue(id, out var node)) {
                return "";
            }

            if (creatureOf.TryGetValue(id, out var known)) {
                return known;
            }

            var parentName = node.Parent != 0 && nodes.TryGetValue(node.Parent, out var parent) ? parent.Name : null;
            var type = IsCorpse(node) ? "corpse" : _registry.Match(node.Name, node.FsmNames, parentName)?.Type;
            return creatureOf[id] = type ?? CreatureOf(node.Parent);
        }

        var hazards = nodes.Where(pair => pair.Value.Hurt != null).Select(pair => pair.Key).ToList();
        var roomHazards = hazards.Where(id => CreatureOf(id) == "").ToHashSet();
        _roomHazards += roomHazards.Count;
        _creatureHazards += hazards.Count - roomHazards.Count;

        // The room's hazards under each object, itself included
        var hazardsUnder = new Dictionary<long, List<long>>();
        List<long> HazardsUnder(long id) {
            if (hazardsUnder.TryGetValue(id, out var known)) {
                return known;
            }

            var list = new List<long>();
            if (roomHazards.Contains(id)) {
                list.Add(id);
            }

            foreach (var child in nodes[id].Children) {
                list.AddRange(HazardsUnder(child));
            }

            return hazardsUnder[id] = list;
        }

        var infos = new List<FsmInfo>();
        var infoOfObject = new Dictionary<long, List<FsmInfo>>();
        foreach (var record in records) {
            var id = record.GameObjectPathId;
            if (!nodes.TryGetValue(id, out var node) || record.States == null) {
                continue;
            }

            var path = PathOf(nodes, id);
            var info = new FsmInfo {
                Record = record,
                Scene = scene,
                Path = path,
                Name = node.Name,
                ParentName = node.Parent != 0 ? nodes[node.Parent].Name : "",
                ActiveAtLoad = ActiveInHierarchy(nodes, id) && record.Enabled,
                UnderBossScene = path.Contains("Boss Scene", StringComparison.Ordinal),
                Creature = CreatureOf(id),
            };
            ReadActions(info, nodes, context);
            FindLoop(info);
            infos.Add(info);
            if (!infoOfObject.TryGetValue(id, out var list)) {
                infoOfObject[id] = list = new List<FsmInfo>();
            }

            list.Add(info);

            // What it reaches by itself: what is under it, what it names, and what the objects it names are part of
            info.Hazards.UnionWith(HazardsUnder(id));
            foreach (var reference in info.Refs) {
                info.Hazards.UnionWith(HazardsUnder(reference));
                info.Hazards.UnionWith(Ancestors(nodes, reference).Where(roomHazards.Contains));
            }
        }

        // Who tells whom: by naming its object, or by an event sent to everything that a few FSMs listen for
        var listeners = new Dictionary<string, List<FsmInfo>>(StringComparer.Ordinal);
        foreach (var info in infos) {
            foreach (var heard in info.Heard) {
                if (!listeners.TryGetValue(heard, out var list)) {
                    listeners[heard] = list = new List<FsmInfo>();
                }

                list.Add(info);
            }
        }

        foreach (var info in infos) {
            var told = new HashSet<FsmInfo>();
            foreach (var reference in info.Refs) {
                if (infoOfObject.TryGetValue(reference, out var list)) {
                    told.UnionWith(list);
                }
            }

            foreach (var sent in info.Broadcasts) {
                if (sent != "FINISHED" && listeners.TryGetValue(sent, out var list) && list.Count <= MaxListeners) {
                    told.UnionWith(list);
                }
            }

            told.Remove(info);
            foreach (var other in told) {
                info.Tells.Add(other);
                other.ToldBy.Add(info);
            }
        }

        // What each FSM reaches through the FSMs it tells, and what hurts with nothing reaching it
        var reached = new HashSet<long>();
        foreach (var info in infos) {
            var all = new HashSet<long>(info.Hazards);
            foreach (var other in Told(info)) {
                all.UnionWith(other.Hazards);
            }

            info.HazardCount = all.Count;
            info.Unregistered = info.Creature == "" && nodes.TryGetValue(info.Record.GameObjectPathId, out var self) &&
                                (self.Classes.Contains("HealthManager") || Ancestors(nodes, info.Record.GameObjectPathId)
                                    .Any(a => nodes[a].Classes.Contains("HealthManager")));
            for (var i = info.SpawnDice.Count - 1; i >= 0; i--) {
                var (text, locals, keys) = info.SpawnDice[i];
                if (locals.Any(l => nodes.ContainsKey(l) && (HazardsUnder(l).Count > 0 ||
                                                             Ancestors(nodes, l).Any(roomHazards.Contains)))) {
                    info.Dice.Add(text);
                    info.SpawnDice.RemoveAt(i);
                } else if (keys.Count == 0) {
                    if (locals.Count == 0) {
                        info.Dice.Add(text);
                    } else {
                        info.CosmeticDice++;
                    }

                    info.SpawnDice.RemoveAt(i);
                }
            }

            info.OwnOnly = all.Count == 1 && all.Contains(info.Record.GameObjectPathId) && info.Tells.Count == 0;
            foreach (var id in all.Take(4)) {
                info.HazardNames.Add($"{nodes[id].Name} [{nodes[id].Hurt}]");
            }

            reached.UnionWith(all);
            if (info.Creature == "" && (all.Count > 0 || info.Spawns.Count > 0 || info.Tells.Count > 0)) {
                RoomFsms.Add(info);
            }
        }

        foreach (var id in roomHazards.Where(id => !reached.Contains(id))) {
            _unreachedHazards++;
            var classes = nodes[id].Classes.Select(c => $"{c} (on it)")
                .Concat(Ancestors(nodes, id).SelectMany(a => nodes[a].Classes).Select(c => $"{c} (above)"))
                .Where(c => !c.StartsWith("DamageHero ", StringComparison.Ordinal))
                .Distinct()
                .DefaultIfEmpty("(nothing)");
            foreach (var className in classes) {
                var entry = UnreachedClasses.TryGetValue(className, out var found)
                    ? found
                    : (Count: 0, Examples: new SortedSet<string>(StringComparer.Ordinal));
                if (entry.Examples.Count < 6) {
                    entry.Examples.Add($"{scene}:{PathOf(nodes, id)}");
                }

                UnreachedClasses[className] = (entry.Count + 1, entry.Examples);
            }
        }
    }

    /// <summary>
    /// The FSMs an FSM tells, and those they tell, up to <see cref="MaxTellSteps"/> steps away.
    /// </summary>
    private static HashSet<FsmInfo> Told(FsmInfo info) {
        var seen = new HashSet<FsmInfo> { info };
        var frontier = new List<FsmInfo> { info };
        for (var step = 0; step < MaxTellSteps && frontier.Count > 0; step++) {
            frontier = frontier.SelectMany(f => f.Tells).Where(seen.Add).ToList();
        }

        seen.Remove(info);
        return seen;
    }

    /// <summary>
    /// What an FSM names, spawns, rolls, notices and sends to everyone, from the parameters of its actions.
    /// </summary>
    private static void ReadActions(FsmInfo info, Dictionary<long, Node> nodes, FileContext context) {
        var record = info.Record;
        foreach (var state in record.States) {
            foreach (var action in state.Structured) {
                var name = ShortName(action.Name);
                var spawns = SpawnAction.IsMatch(name);
                foreach (var param in action.Params) {
                    foreach (var (fileId, pathId) in References(param, record)) {
                        if (fileId == 0) {
                            if (nodes.ContainsKey(pathId)) {
                                info.Refs.Add(pathId);
                            }
                        } else if (spawns && context.FileNameOf(fileId) is { } file) {
                            info.Spawns.Add(($"{file}:{pathId}", $"{state.Name}: {name}"));
                        }
                    }
                }

                if (DiceActions.Contains(name) && RollsNothing(action)) {
                    // A wait or a number between two equal bounds, which rolls no matter what
                } else if (DiceActions.Contains(name) && spawns) {
                    var locals = new List<long>();
                    var keys = new List<string>();
                    foreach (var (fileId, pathId) in action.Params.SelectMany(p => References(p, record))) {
                        if (fileId == 0) {
                            locals.Add(pathId);
                        } else if (context.FileNameOf(fileId) is { } file) {
                            keys.Add($"{file}:{pathId}");
                        }
                    }

                    info.SpawnDice.Add(($"{state.Name}: {name}", locals, keys));
                } else if (DiceActions.Contains(name)) {
                    if (IsCosmeticDice(name, action, record)) {
                        info.CosmeticDice++;
                    } else {
                        info.Dice.Add($"{state.Name}: {name}");
                    }
                }

                if (DetectionAction.IsMatch(name) && NoticesHero(name, action)) {
                    info.Detection.Add($"{state.Name}: {name}");
                }

                if (SendAction.IsMatch(name) && SentToEveryone(name, action)) {
                    foreach (var param in action.Params.Where(p => p.Type == 23 || (p.Type == 18 &&
                                 p.Field.Contains("event", StringComparison.OrdinalIgnoreCase)))) {
                        if (!string.IsNullOrEmpty(param.Value)) {
                            info.Broadcasts.Add(param.Value);
                        }
                    }
                }
            }

            foreach (var transition in state.Transitions) {
                info.Heard.Add(EventOf(transition));
            }
        }

        foreach (var transition in record.GlobalTransitions ?? []) {
            info.Heard.Add(EventOf(transition));
        }

        // Objects held in variables count as named, whether or not an action reads them where they are set
        foreach (var (key, value) in record.Variables ?? new Dictionary<string, string>()) {
            if (!key.StartsWith("gameobject:", StringComparison.Ordinal)) {
                continue;
            }

            foreach (Match match in ObjectReference.Matches(value)) {
                if (match.Groups[1].Success) {
                    continue;
                }

                var pathId = long.Parse(match.Groups[2].Value);
                if (nodes.ContainsKey(pathId)) {
                    info.Refs.Add(pathId);
                }
            }
        }
    }

    /// <summary>
    /// The objects a parameter names: directly, by an object variable of the FSM, or as the object an event goes to.
    /// </summary>
    private static IEnumerable<(int FileId, long PathId)> References(ParamValue param, FsmRecord record) {
        var texts = new List<string>();
        if (param.Value != null) {
            texts.Add(param.Value);
            if (TargetVariable.Match(param.Value) is { Success: true } target) {
                texts.Add(record.Variables?.GetValueOrDefault($"gameobject:{target.Groups[1].Value}"));
            }
        }

        if (param.Variable != null) {
            texts.Add(record.Variables?.GetValueOrDefault($"gameobject:{param.Variable}"));
        }

        foreach (var text in texts.Where(t => t != null)) {
            foreach (Match match in ObjectReference.Matches(text)) {
                var fileId = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
                var pathId = long.Parse(match.Groups[2].Value);
                if (pathId != 0) {
                    yield return (fileId, pathId);
                }
            }
        }
    }

    /// <summary>
    /// Whether a rolling action's bounds are the same number, so that whatever it rolls comes out as that number.
    /// </summary>
    private static bool RollsNothing(ActionParamsData action) {
        var min = action.Params.FirstOrDefault(p => p.Field is "timeMin" or "min" or "minValue");
        var max = action.Params.FirstOrDefault(p => p.Field is "timeMax" or "max" or "maxValue");
        return min?.Value != null && max?.Value != null && min.Variable == null && max.Variable == null &&
               min.Value == max.Value;
    }

    /// <summary>
    /// Whether a noticing action notices the hero: one that asks for a layer asks for the hero's (Player, Hero Box,
    /// Hero Only), and one that measures where something is or how far measures the hero.
    /// </summary>
    private static bool NoticesHero(string name, ActionParamsData action) {
        if (action.Params.FirstOrDefault(p => p.Field == "collideLayer") is { } layer) {
            return layer.Value is "9" or "20" or "28";
        }

        if (name.StartsWith("Check", StringComparison.Ordinal) && name.Contains("Position", StringComparison.Ordinal) ||
            name.Contains("Distance", StringComparison.Ordinal) || name.StartsWith("CheckTargetDirection", StringComparison.Ordinal)) {
            return action.Params.Any(p => p.Type is 19 or 20 && $"{p.Variable} {p.Value}".Contains("Hero",
                StringComparison.OrdinalIgnoreCase) || $"{p.Variable} {p.Value}".Contains("Player", StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    /// <summary>
    /// Whether a sending action sends to everything that listens rather than to an object or to its own FSM.
    /// </summary>
    private static bool SentToEveryone(string name, ActionParamsData action) {
        if (name.Contains("Register", StringComparison.Ordinal) || name.StartsWith("Broadcast", StringComparison.Ordinal)) {
            return true;
        }

        return action.Params.Any(p => p.Type == 31 && p.Value?.StartsWith("<broadcast", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Whether the dice of an action only go into a sound or a look: the cosmetic actions, and the actions that keep
    /// what they rolled in variables that nothing else uses.
    /// </summary>
    private static bool IsCosmeticDice(string name, ActionParamsData action, FsmRecord record) {
        if (CosmeticDiceActions.Contains(name)) {
            return true;
        }

        if (!StoringDiceActions.Contains(name) || action.Params.Any(p => p.Type == 23 && !string.IsNullOrEmpty(p.Value))) {
            return false;
        }

        var kept = action.Params
            .Where(p => p.Variable != null && StoreField.IsMatch(p.Field))
            .Select(p => p.Variable)
            .ToHashSet(StringComparer.Ordinal);
        if (kept.Count == 0) {
            return false;
        }

        foreach (var state in record.States) {
            foreach (var other in state.Structured) {
                if (ReferenceEquals(other, action) || !other.Params.Any(p => p.Variable != null && kept.Contains(p.Variable))) {
                    continue;
                }

                var otherName = ShortName(other.Name);
                if (!CosmeticAction.IsMatch(otherName) && !AnimationAction.IsMatch(otherName)) {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// A loop that an FSM goes round by itself: states that lead to each other when their own actions finish or send
    /// the event, from the start state if there is one that way, or else from anywhere. A state that notices the hero
    /// only leads on by finishing, since its events may be the hero's.
    /// </summary>
    private static void FindLoop(FsmInfo info) {
        var record = info.Record;
        var states = record.States.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var globals = (record.GlobalTransitions ?? []).Select(SplitTransition).ToList();
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var state in states.Values) {
            var notices = state.Actions.Any(a => DetectionAction.IsMatch(ShortName(a)) || ListenAction.IsMatch(ShortName(a)));
            var next = new List<string>();
            foreach (var (eventName, to) in state.Transitions.Select(SplitTransition)) {
                if (eventName == "FINISHED" || (!notices && state.Strings.Contains(eventName))) {
                    next.Add(to);
                }
            }

            if (!notices) {
                next.AddRange(globals.Where(g => g.Event != "FINISHED" && state.Strings.Contains(g.Event)).Select(g => g.To));
            }

            edges[state.Name] = next.Where(states.ContainsKey).Distinct().ToList();
        }

        List<string> FindCycle(string from) {
            var color = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new List<string>();
            List<string> Visit(string name) {
                color[name] = 1;
                stack.Add(name);
                foreach (var next in edges.GetValueOrDefault(name) ?? []) {
                    var seen = color.GetValueOrDefault(next);
                    if (seen == 1) {
                        return stack.Skip(stack.IndexOf(next)).ToList();
                    }

                    if (seen == 0 && Visit(next) is { } cycle) {
                        return cycle;
                    }
                }

                color[name] = 2;
                stack.RemoveAt(stack.Count - 1);
                return null;
            }

            return Visit(from);
        }

        if (record.StartState != null && states.ContainsKey(record.StartState) && FindCycle(record.StartState) is { } loop) {
            info.Loop = loop;
            info.LoopFromStart = true;
        } else {
            info.Loop = states.Keys.Select(FindCycle).FirstOrDefault(c => c != null);
        }

        if (info.Loop != null) {
            info.LoopTouches = info.Loop.Any(name => states[name].Actions.Select(ShortName)
                .Any(a => EffectAction.IsMatch(a) && !CosmeticAction.IsMatch(a)));
        }
    }

    private static (string Event, string To) SplitTransition(string transition) {
        var arrow = transition.IndexOf("->", StringComparison.Ordinal);
        return arrow < 0 ? (transition, "") : (transition[..arrow], transition[(arrow + 2)..]);
    }

    private static string EventOf(string transition) => SplitTransition(transition).Event;

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    /// <summary>
    /// Puts the prefabs each FSM spawns to what they are, and picks the room FSMs that decide something by themselves
    /// and reach something that hurts.
    /// </summary>
    private static List<FsmInfo> Resolve() {
        foreach (var info in RoomFsms) {
            foreach (var (text, _, keys) in info.SpawnDice) {
                if (keys.Any(k => HurtingPrefabs.ContainsKey(k) || CreaturePrefabs.ContainsKey(k))) {
                    info.Dice.Add(text);
                } else {
                    info.CosmeticDice++;
                }
            }

            info.SpawnDice.Clear();
            foreach (var (key, _) in info.Spawns) {
                if (CreaturePrefabs.TryGetValue(key, out var type)) {
                    info.SpawnedCreatures.Add(type);
                } else if (HurtingPrefabs.TryGetValue(key, out var name)) {
                    info.SpawnedHurting.Add(name);
                }
            }
        }

        var candidates = new List<FsmInfo>();
        foreach (var info in RoomFsms) {
            foreach (var other in Told(info)) {
                info.SpawnedHurting.UnionWith(other.SpawnedHurting);
            }

            info.Contact = info.OwnOnly && info.SpawnedHurting.Count == 0 && info.SpawnedCreatures.Count == 0 &&
                           info.Dice.Count == 0 && !(info.Loop != null && info.LoopTouches);

            if (info.HasDecision && (info.HazardCount > 0 || info.SpawnedHurting.Count > 0)) {
                candidates.Add(info);
            }
        }

        return candidates;
    }

    /// <summary>
    /// The files of SSMP whose tables say which things of a room it shares between the games.
    /// </summary>
    private static readonly Regex CoverageFile = new(
        @"[\\/]Save[\\/]|ArenaCoop|BossRoomCoop|PersonalPlaces|RoomCreatures|BenchCoop|FleaGameCoop|NeedolinCoop",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The FSM names that are rare enough to say which thing they run: those on at most this many kinds of objects.
    /// </summary>
    private const int MaxKindsOfRareFsmName = 6;

    // The FSM names that SSMP's tables name and that only a few kinds of objects carry
    private static readonly HashSet<string> RareFsmNames = new(StringComparer.Ordinal);

    /// <summary>
    /// The string literals of the files of SSMP that share things of a room, which is where its tables name them,
    /// without those that are names of FSMs, states or events anywhere in the game; and of those FSM names, the rare
    /// ones, which a table may name a thing by.
    /// </summary>
    private static List<(string Literal, string File)> LoadLiterals(string ssmpDir, ScanResult scan) {
        var generic = new HashSet<string>(StringComparer.Ordinal);
        var kindsPerFsmName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var record in scan.Fsms) {
            generic.Add(record.FsmName);
            if (!kindsPerFsmName.TryGetValue(record.FsmName, out var kinds)) {
                kindsPerFsmName[record.FsmName] = kinds = new HashSet<string>(StringComparer.Ordinal);
            }

            kinds.Add(NumberSuffix.Replace(record.ObjectName ?? "", ""));
            foreach (var state in record.States ?? []) {
                generic.Add(state.Name);
                foreach (var transition in state.Transitions) {
                    generic.Add(EventOf(transition));
                }
            }
        }

        var result = new List<(string, string)>();
        var seen = new HashSet<(string, string)>();
        foreach (var path in Directory.GetFiles(Path.Combine(ssmpDir, "SSMP"), "*.cs", SearchOption.AllDirectories)
                     .Where(path => CoverageFile.IsMatch(path))) {
            var code = Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", "", RegexOptions.Multiline);
            foreach (Match match in Literal.Matches(code)) {
                var literal = match.Groups[1].Value;
                if (literal.Length < 5 || !seen.Add((literal, Path.GetFileName(path)))) {
                    continue;
                }

                if (!generic.Contains(literal)) {
                    result.Add((literal, Path.GetFileName(path)));
                } else if (kindsPerFsmName.TryGetValue(literal, out var kinds) && kinds.Count <= MaxKindsOfRareFsmName) {
                    RareFsmNames.Add(literal);
                    result.Add(($"fsm:{literal}", Path.GetFileName(path)));
                }
            }
        }

        return result;
    }

    private static void Write(List<FsmInfo> candidates, List<(string Literal, string File)> literals, string outPath) {
        // Which literals name which kinds, to leave out those that name half the game
        var named = new Dictionary<FsmInfo, List<string>>();
        var kindsPerLiteral = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var info in candidates) {
            var matches = literals
                .Where(l => l.Literal.StartsWith("fsm:", StringComparison.Ordinal)
                    ? info.Record.FsmName == l.Literal[4..]
                    : info.Name.Contains(l.Literal, StringComparison.Ordinal) ||
                      info.ParentName.Contains(l.Literal, StringComparison.Ordinal))
                .Select(l => $"{l.Literal} ({l.File})")
                .Distinct()
                .ToList();
            named[info] = matches;
            foreach (var match in matches) {
                if (!kindsPerLiteral.TryGetValue(match, out var kinds)) {
                    kindsPerLiteral[match] = kinds = new HashSet<string>(StringComparer.Ordinal);
                }

                kinds.Add(info.Kind);
            }
        }

        string Covered(FsmInfo info) => string.Join("; ", named[info].Where(m => kindsPerLiteral[m].Count <= 8));

        int Tier(FsmInfo info) => info.Dice.Count > 0 ? 1 : info.Loop != null && info.LoopTouches ? 2 : 3;

        static string Join(IEnumerable<string> items, int max) {
            var list = items.Distinct().ToList();
            return list.Count <= max ? string.Join(", ", list) : $"{string.Join(", ", list.Take(max))} +{list.Count - max}";
        }

        string StartedBy(FsmInfo info) => Join(info.ToldBy.Select(t =>
            $"{t.Name}/{t.Record.FsmName}{(t.Creature is "" ? "" : $" [creature {t.Creature}]")}"), 3);

        string LoopText(FsmInfo info) => info.Loop == null || !info.LoopTouches
            ? ""
            : $"{(info.LoopFromStart ? info.ActiveAtLoad ? "from load" : "from switch-on" : "after an event")}: " +
              string.Join(" > ", info.Loop.Take(6));

        var tsv = new StringBuilder();
        tsv.AppendLine("tier\tkind\tssmp names it\tscene\tpath\tfsm\ton at load\tunregistered enemy\tset going by\tdice\tloop\tnotices player\t" +
                       "hurts (count)\thurts (examples)\tspawns that hurt\tspawns creatures");
        foreach (var info in candidates.OrderBy(Tier).ThenBy(i => i.Kind).ThenBy(i => i.Scene)) {
            tsv.AppendLine(string.Join("\t",
                Tier(info), info.Kind, Covered(info), info.Scene, info.Path, info.Record.FsmName, info.ActiveAtLoad,
                info.Unregistered, StartedBy(info), Join(info.Dice, 6), LoopText(info), Join(info.Detection, 4), info.HazardCount,
                string.Join(", ", info.HazardNames), Join(info.SpawnedHurting, 4), Join(info.SpawnedCreatures, 4)));
        }

        File.WriteAllText(outPath, tsv.ToString(), new UTF8Encoding(false));

        var md = new StringBuilder();
        md.AppendLine("# What hurts and is decided by the room");
        md.AppendLine();
        md.AppendLine($"{_scenes} scenes; {_roomHazards} room objects hurt, {_creatureHazards} creature parts hurt (left out); " +
                      $"{_unreachedHazards} room objects hurt with no FSM reaching them.");
        md.AppendLine();
        md.AppendLine($"{RoomFsms.Count} room FSMs reach something; {candidates.Count} of them decide something by themselves " +
                      "(tier 1 dice, 2 a loop that changes things, 3 noticing the player only); " +
                      $"{RoomFsms.Count(i => i.Contact)} only hurt whoever touches them.");
        foreach (var tier in candidates.GroupBy(Tier).OrderBy(g => g.Key)) {
            var kinds = tier.GroupBy(i => i.Kind).ToList();
            md.AppendLine();
            md.AppendLine($"## Tier {tier.Key}: {kinds.Count} kinds, {kinds.Count(k => k.Any(i => Covered(i) != ""))} named by SSMP");
            md.AppendLine();
            foreach (var kind in kinds.OrderBy(k => k.Any(i => Covered(i) != "") ? 1 : 0)
                         .ThenByDescending(k => k.Select(i => i.Scene).Distinct().Count())
                         .ThenBy(k => k.Key, StringComparer.Ordinal)) {
                var list = kind.ToList();
                var scenes = list.Select(i => i.Scene).Distinct().ToList();
                var parts = new List<string> {
                    $"{scenes.Count} scenes ({Join(scenes, 4)})",
                    $"hurts {list.Max(i => i.HazardCount)}: {Join(list.SelectMany(i => i.HazardNames), 3)}",
                };
                if (list.Any(i => i.SpawnedHurting.Count > 0)) {
                    parts.Add($"spawns {Join(list.SelectMany(i => i.SpawnedHurting), 3)}");
                }

                if (list.Any(i => i.Dice.Count > 0)) {
                    parts.Add($"dice {Join(list.SelectMany(i => i.Dice), 4)}");
                }

                if (list.FirstOrDefault(i => LoopText(i) != "") is { } looped) {
                    parts.Add($"loop {LoopText(looped)}");
                }

                if (list.Any(i => i.Detection.Count > 0)) {
                    parts.Add($"notices {Join(list.SelectMany(i => i.Detection), 3)}");
                }

                if (list.Any(i => i.ToldBy.Count > 0)) {
                    parts.Add($"set going by {Join(list.Select(StartedBy).Where(s => s != ""), 2)}");
                }

                if (list.Any(i => i.UnderBossScene)) {
                    parts.Add("under a Boss Scene");
                }

                if (list.Any(i => i.Unregistered)) {
                    parts.Add("UNREGISTERED ENEMY");
                }

                var covered = list.Select(Covered).FirstOrDefault(c => c != "");
                md.AppendLine($"- **{kind.Key}**{(covered != null ? $" — SSMP: {covered}" : "")} — {string.Join("; ", parts)}");
            }
        }

        md.AppendLine();
        md.AppendLine("## Scripts on or above room objects that hurt with no FSM reaching them");
        md.AppendLine();
        foreach (var (className, entry) in UnreachedClasses.OrderByDescending(p => p.Value.Count).Take(80)) {
            md.AppendLine($"- {className}: {entry.Count} — {string.Join(", ", entry.Examples.Take(3))}");
        }

        var mdPath = Path.ChangeExtension(outPath, ".md");
        File.WriteAllText(mdPath, md.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{candidates.Count} candidates in {candidates.Select(c => c.Kind).Distinct().Count()} kinds");
        Console.WriteLine($"TSV:      {outPath}");
        Console.WriteLine($"Markdown: {mdPath}");
    }
}
