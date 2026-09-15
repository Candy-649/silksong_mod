using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// For --pdflags: lists everything in the bundles that writes or reads a field of PlayerData by name, for sorting the
/// fields into state that a two-player save shares and state that belongs to each player.
///
/// FSM rows come from the enabled actions of every FSM and FSM template: the actions that set, add to or count a field
/// (SetPlayerDataBool, PlayerDataIntAdd, ...), with the other actions of the state, notable actions up to two
/// transitions away (rewards, quests, dialogue), whether the interact button leads to the state, and the class that
/// --interactions gives the FSM. Component rows come from serialized MonoBehaviours and ScriptableObjects whose type
/// has a field named like a player data reference (setPDBoolOnEnd, playerDataBoolName, PlayerDataTest.FieldName,
/// QuestTargetPlayerDataBools.pdBools, ...) and whose string value is the name of a PlayerData field. Whether such a
/// field writes or reads is a guess from the class and field name (see ComponentWrites), which the classifier treats as
/// such.
///
/// Output in the out dir: playerdata-writes.tsv, one row per write; playerdata-reads.tsv, one row per field with counts
/// by reader category and examples; playerdata-quest-targets.tsv, the quest target assets that count player data
/// fields. All bundles but asset-only groups are read; the fsmtemplates come first so FSMs get their states.
/// </summary>
internal static class PlayerDataFlagScanner {
    private const int MaxExamples = 6;

    /// <summary>
    /// FSM actions that write a field of the player data named by a parameter.
    /// </summary>
    private static readonly Regex WriteAction = new(
        @"^(SetPlayerData\w*|IncrementPlayerData\w*|DecrementPlayerData\w*|PlayerDataIntAdd|SetCollectablePickupPlayerDataBool)$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Parameters of the write actions that name the field.
    /// </summary>
    private static readonly HashSet<string> NameParams = new(StringComparer.Ordinal) {
        "boolName", "intName", "VariableName", "stringName", "floatName", "vector3Name", "PlayerDataBoolName",
    };

    /// <summary>
    /// Parameters of the write actions that hold the value written.
    /// </summary>
    private static readonly HashSet<string> ValueParams = new(StringComparer.Ordinal) {
        "value", "SetValue", "amount", "Delay",
    };

    /// <summary>
    /// Actions that tell what kind of part of an FSM a write belongs to: rewards, quests, dialogue, payment, journal
    /// and achievements, scene changes and boss titles.
    /// </summary>
    private static readonly Regex NotableAction = new(
        @"^(SavedItemGet\w*|CollectableItemCollect\w*|CollectableItemTake\w*|AddCurrency\w*|TakeCurrency\w*|SpawnPowerUpGetMsg|" +
        @"SpawnSkillGetMsg|CreateUIMsgGetItem|SetToolUnlocked|GetQuestReward\w*|SetShopItemPurchased|OpenSimpleShopMenu|" +
        @"RunDialogue\w*|DialogueYesNo\w*|QuestCompleteYesNo|RecordJournalKill\w*|CompleteJournalRecord\w*|" +
        @"AwardAchievement\w*|QueueAchievement\w*|BeginSceneTransition\w*|DisplayBossTitle\w*|ShowBossTitle\w*|" +
        @"StartBattleScene\w*|SetPersistentBool|SetPersistentInt|AutoSaveGame\w*|SaveGame\w*|StartAct\w*|" +
        @"SetWorldRumble\w*|PlayCutscene\w*|EquipCrest\w*|UnlockCrest\w*|AddToolAmount\w*|TakeTool\w*)$|^Quest",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Field names in a serialized type that may hold the name of a field of the player data.
    /// </summary>
    private static readonly Regex ReferenceFieldName = new(
        @"PD|Pd[A-Z]|^pd[A-Z]|[pP]layer[dD]ata|boolName|BoolName|intName|IntName|FieldName|variableName|uniqueCollectBool|" +
        @"pdFieldTemplate|orderListPd|statueStatePD",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Serialized fields that write the player data field they name, as "Class.field path" with array items left out,
    /// or as just the last field name when that is enough. Everything else that names a field reads it.
    /// </summary>
    private static readonly HashSet<string> ComponentWrites = new(StringComparer.Ordinal) {
        "BattleScene.setPDBoolOnEnd", "BattleScene.setExtraPDBoolOnEnd", "SetPlayerDataBool.boolName",
        "Lever.playerDataBool", "Lever_tk2d.setPlayerDataBool", "BridgeLever.playerDataBool",
        "PersistentPressurePlate.playerDataBool", "SilkGrubCocoon.setPDBoolOnBreak", "SilkGrubCocoon.unsetPDBoolOnBreak",
        "EnemyDeathEffects.setPlayerDataBool", "ScuttlerControl.killedPDBool", "ScuttlerControl.killsPDBool",
        "ScuttlerControl.newDataPDBool", "CollectableItemBasic.uniqueCollectBool",
        "CollectableItemBasic.setExtraPlayerDataBools.variableName", "CollectableItemBasic.setExtraPlayerDataInts.variableName",
        "PlayerDataCollectable.setPlayerDataBools.variableName", "PlayerDataCollectable.setPlayerDataInts.variableName",
        "PlayerDataCollectable.linkedPDBool", "PlayerDataCollectable.linkedPDInt", "PlayerDataBoolCollectable.boolName",
        "ShopItem.playerDataBoolName", "ShopItem.playerDataIntName", "ShopItem.setExtraPlayerDataBools",
        "ShopItem.setExtraPlayerDataInts.variableName", "QuestRewardHolder.pickupPdBool", "CurrencyObjectBase.firstGetPDBool",
        "CurrencyObjectBase.popupPDBool", "SceneAdditiveLoadConditional.setPdBoolOnLoad", "BellBench.fixedPDBool",
        "ItemReceptacle.playerDataBool", "InventoryItemBasic.hasSeenPdBool", "InventoryPane.hasNewPDBool",
        "DreamPlant.playerdataBool", "CollectableItemPickup.playerDataBool", "MemoryOrbGroup.pdBitmask",
        "CollectionGramaphone.playingPdField", "ToolItemToggleState.statePdBool", "CollectableItemGrower.growStatePdInt",
        "RecordDoorEntry.pdFromSceneName", "BellShrineTuningForkGroup.pdActivatedBitmask", "AntRegionHandler.pickedUpBoolName",
        "hasSeenPdBool",
    };

    /// <summary>
    /// Sibling fields of a reference that tell what is written or tested, like PlayerDataBoolOperation.value.
    /// </summary>
    private static readonly HashSet<string> SiblingValues = new(StringComparer.Ordinal) {
        "value", "operation", "number", "Type", "BoolValue", "NumType", "IntValue", "StringValue", "setOnLoad",
        "setOnTriggerEnter",
    };

    private sealed record WriteRow(
        string Source, string Place, string Kind, string Category, string InArena, string Object, string Fsm,
        string Template, string State, string Action, string Field, string Value, string InteractClass,
        string InteractReason, string InTalk, string StateActions, string NearActions, string FsmNotable, string Components
    );

    private sealed class ReadInfo {
        public int FsmReads;
        public int ComponentReads;
        public readonly Dictionary<string, int> Kinds = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Categories = new(StringComparer.Ordinal);
        public readonly List<string> Examples = [];
    }

    public static void Run(string bundleDir, string filter, string ssmpDir, string outDir) {
        var fieldNames = LoadPlayerDataFields(bundleDir);
        Console.WriteLine($"PlayerData fields: {fieldNames.Count}");
        var registry = EntityRegistryData.Load(ssmpDir);
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();
        ActionParams.ReadStructuredParams = true;

        var writes = new List<WriteRow>();
        var reads = new Dictionary<string, ReadInfo>(StringComparer.Ordinal);
        var questTargets = new List<string>();
        var unknownActions = new Dictionary<string, int>(StringComparer.Ordinal);
        var relevantTypes = new Dictionary<string, bool>(StringComparer.Ordinal);

        void AddRead(string field, bool fsm, string kind, string category, string example) {
            if (!reads.TryGetValue(field, out var info)) {
                reads[field] = info = new ReadInfo();
            }

            if (fsm) {
                info.FsmReads++;
            } else {
                info.ComponentReads++;
            }

            info.Kinds[kind] = info.Kinds.GetValueOrDefault(kind) + 1;
            info.Categories[category] = info.Categories.GetValueOrDefault(category) + 1;
            if (info.Examples.Count < MaxExamples && !info.Examples.Contains(example)) {
                info.Examples.Add(example);
            }
        }

        BundleScanner.ComponentCollector = (assets, instance, context, monoBehaviours, bundle, scene) => {
            foreach (var info in monoBehaviours) {
                if (!context.MonoClasses.TryGetValue(info.PathId, out var className) || className == null ||
                    className is "PlayMakerFSM" or "FsmTemplate") {
                    continue;
                }

                if (!relevantTypes.TryGetValue(className, out var relevant)) {
                    relevant = HasReferenceField(assets.GetTemplateBaseField(instance, info), 0);
                    relevantTypes[className] = relevant;
                }

                if (!relevant) {
                    continue;
                }

                AssetTypeValueField root;
                try {
                    root = assets.GetBaseField(instance, info);
                } catch {
                    continue;
                }

                var gameObjectId = root.Get("m_GameObject")?["m_PathID"].AsLong ?? 0;
                string objectName;
                var components = "";
                if (gameObjectId != 0 && context.GetGameObject(gameObjectId) is { } gameObject) {
                    objectName = string.Join("/", context.Ancestors(gameObject).AsEnumerable().Reverse()
                        .Select(a => a.Name).Append(gameObject.Name));
                    components = string.Join(",", gameObject.ComponentClasses);
                } else {
                    objectName = root.Get("m_Name")?.AsString ?? "?";
                }

                var place = scene ?? Path.GetFileNameWithoutExtension(bundle.Rel);
                foreach (var (path, value, siblings) in References(root, fieldNames)) {
                    var shortPath = Regex.Replace(path, @"\.Array\.data", "");
                    var lastName = shortPath[(shortPath.LastIndexOf('.') + 1)..];
                    var isTemplate = !fieldNames.Contains(value);
                    var isWrite = ComponentWrites.Contains($"{className}.{shortPath}") || ComponentWrites.Contains(lastName);
                    if (className.StartsWith("QuestTarget", StringComparison.Ordinal)) {
                        questTargets.Add(string.Join("\t", new[] {
                            place, objectName, className, shortPath, value, isTemplate ? "template" : "field",
                            root.Get("getAppendsSceneName") is { Value: not null } appends ? appends.AsBool.ToString() : "",
                        }.Select(Clean)));
                    }

                    if (isTemplate || isWrite) {
                        writes.Add(new WriteRow(
                            "component", place, scene != null ? "scene" : "asset", isTemplate ? "template" : "", "",
                            objectName, className, "", shortPath, isTemplate ? "template" : "write", value, siblings, "",
                            "", "", "", "", "", components
                        ));
                    } else {
                        AddRead(value, false, $"{className}.{shortPath}", "component",
                            $"{place}:{objectName}:{className}.{shortPath}");
                    }
                }
            }
        };

        var all = Directory.GetFiles(bundleDir, "*.bundle", SearchOption.AllDirectories)
            .Select(path => (Path: path, Rel: Path.GetRelativePath(bundleDir, path).Replace('\\', '/')))
            .Where(b => !Regex.IsMatch(Path.GetFileName(b.Rel),
                @"(monoscripts|unitybuiltinassets|textures|tk2dcollections|herocollections|sfxstatic|sfxdynamic|audiocues|fonts|vibrationstatic|materials|animations|shaders|models)"))
            .Where(b => !b.Rel.Split('/').Contains("_atlases"))
            .Where(b => filter == null || b.Rel.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        b.Rel.Contains("fsmtemplates", StringComparison.Ordinal))
            .OrderBy(b => b.Rel.Contains("fsmtemplates", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(b => b.Rel, StringComparer.Ordinal)
            .ToList();

        var templates = new Dictionary<string, TemplateRecord>(StringComparer.Ordinal);
        var templatesDone = false;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var scanned = 0;
        foreach (var bundle in all) {
            var isTemplateBundle = bundle.Rel.Contains("fsmtemplates", StringComparison.Ordinal);
            if (!isTemplateBundle && !templatesDone) {
                templatesDone = true;
                foreach (var (key, template) in templates) {
                    AddFsmRows(null, template, key, fieldNames, writes, AddRead, unknownActions);
                }
            }

            var result = new ScanResult();
            try {
                BundleScanner.ScanBundleAt(manager, bundleDir, bundle.Path, scriptNames, registry, result);
            } catch (Exception e) {
                Console.WriteLine($"error in {bundle.Rel}: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }

            foreach (var (key, template) in result.Templates) {
                templates.TryAdd(key, template);
            }

            foreach (var record in result.Fsms) {
                if (record.TemplateKey != null && templates.TryGetValue(record.TemplateKey, out var template)) {
                    record.States = template.States;
                    record.GlobalTransitions = template.GlobalTransitions;
                    record.TemplateName = template.Name;
                }

                if (record.States != null) {
                    AddFsmRows(record, null, null, fieldNames, writes, AddRead, unknownActions);
                }
            }

            if (++scanned % 100 == 0) {
                Console.WriteLine($"  {scanned}/{all.Count} bundles, {writes.Count} writes, {stopwatch.Elapsed:mm\\:ss}");
            }
        }

        Directory.CreateDirectory(outDir);
        var writesPath = Path.Combine(outDir, "playerdata-writes.tsv");
        var builder = new StringBuilder();
        builder.AppendLine("source\tplace\tkind\tcategory\tin_arena\tobject\tfsm\ttemplate\tstate\taction\tfield\tvalue\t" +
                           "interact_class\tinteract_reason\tin_talk\tstate_actions\tnear_actions\tfsm_notable\tcomponents");
        foreach (var row in writes) {
            builder.AppendLine(string.Join("\t", new[] {
                row.Source, row.Place, row.Kind, row.Category, row.InArena, row.Object, row.Fsm, row.Template, row.State,
                row.Action, row.Field, row.Value, row.InteractClass, row.InteractReason, row.InTalk, row.StateActions,
                row.NearActions, row.FsmNotable, row.Components,
            }.Select(Clean)));
        }

        File.WriteAllText(writesPath, builder.ToString(), new UTF8Encoding(false));

        var readsPath = Path.Combine(outDir, "playerdata-reads.tsv");
        builder.Clear();
        builder.AppendLine("field\tfsm_reads\tcomponent_reads\tcategories\treaders\texamples");
        foreach (var (field, info) in reads.OrderBy(r => r.Key, StringComparer.Ordinal)) {
            builder.AppendLine(string.Join("\t",
                Clean(field), info.FsmReads, info.ComponentReads,
                Clean(string.Join(",", info.Categories.OrderByDescending(k => k.Value).Select(k => $"{k.Key}:{k.Value}"))),
                Clean(string.Join(",", info.Kinds.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} x{k.Value}"))),
                Clean(string.Join(" | ", info.Examples))));
        }

        File.WriteAllText(readsPath, builder.ToString(), new UTF8Encoding(false));

        var questPath = Path.Combine(outDir, "playerdata-quest-targets.tsv");
        File.WriteAllText(questPath,
            "place\tasset\tclass\tpath\tvalue\tkind\tappends_scene\n" + string.Join("\n", questTargets.Distinct()) + "\n",
            new UTF8Encoding(false));

        Console.WriteLine($"Scanned {scanned} bundles in {stopwatch.Elapsed:mm\\:ss}, {templates.Count} templates");
        Console.WriteLine($"{writes.Count} writes of {writes.Select(w => w.Field).Distinct().Count()} names, " +
                          $"reads of {reads.Count} fields, {questTargets.Distinct().Count()} quest target references");
        Console.WriteLine("== other actions with a parameter that names a PlayerData field (counted as reads)");
        foreach (var (name, count) in unknownActions.OrderByDescending(a => a.Value)) {
            Console.WriteLine($"  {count,6}  {name}");
        }

        Console.WriteLine("== component classes with references");
        foreach (var group in writes.Where(w => w.Source == "component").GroupBy(w => $"{w.Fsm}.{w.State} ({w.Action})")
                     .OrderByDescending(g => g.Count())) {
            Console.WriteLine($"  {group.Count(),6}  {group.Key}");
        }

        Console.WriteLine($"Writes: {writesPath}");
        Console.WriteLine($"Reads:  {readsPath}");
        Console.WriteLine($"Quest targets: {questPath}");
    }

    /// <summary>
    /// Adds the writes and reads of an FSM, or of a template on its own when record is null.
    /// </summary>
    private static void AddFsmRows(
        FsmRecord record,
        TemplateRecord template,
        string templateKey,
        HashSet<string> fieldNames,
        List<WriteRow> writes,
        Action<string, bool, string, string, string> addRead,
        Dictionary<string, int> unknownActions
    ) {
        var states = record?.States ?? template.States;
        var byName = new Dictionary<string, StateData>(StringComparer.Ordinal);
        foreach (var state in states) {
            byName.TryAdd(state.Name, state);
        }

        var place = record == null ? "fsmtemplates" : record.Scene ?? Path.GetFileNameWithoutExtension(record.Bundle);
        var objectName = record?.ObjectPath ?? $"(template {template.Name})";
        var fsmName = record?.FsmName ?? template.Name;
        var templateName = record?.TemplateName ?? templateKey ?? "";

        // The states that the interact button leads to
        InteractionAnalyzer.Graph graph = null;
        var talk = new HashSet<string>(StringComparer.Ordinal);
        if (record != null) {
            graph = new InteractionAnalyzer.Graph(record);
            if (graph.IdleStates.Count > 0) {
                var idle = graph.IdleStates.ToHashSet(StringComparer.Ordinal);
                var queue = new Queue<string>();
                foreach (var state in states.Where(s => idle.Contains(s.Name))) {
                    foreach (var (eventName, to) in state.Transitions.Select(Split)) {
                        if (eventName == "INTERACT" && to != null && byName.ContainsKey(to) && !idle.Contains(to) && talk.Add(to)) {
                            queue.Enqueue(to);
                        }
                    }
                }

                while (queue.Count > 0) {
                    foreach (var (_, to) in byName[queue.Dequeue()].Transitions.Select(Split)) {
                        if (to != null && byName.ContainsKey(to) && !idle.Contains(to) && talk.Add(to)) {
                            queue.Enqueue(to);
                        }
                    }
                }
            }
        }

        string Resolve(ParamValue param) {
            if (param.Variable == null) {
                return param.Value?.Trim('"');
            }

            var key = ActionParams.VariableKey(param.Type, param.Variable);
            return key != null && record?.Variables != null && record.Variables.TryGetValue(key, out var value)
                ? value
                : $"var {param.Variable}";
        }

        List<(StateData State, ActionParamsData Action, string Field, string Value)> found = [];
        foreach (var state in states) {
            var readCategory = (record?.Category ?? "template") + (talk.Contains(state.Name) ? "-talk" : "");
            foreach (var action in state.Structured) {
                var shortName = ShortName(action.Name);
                if (WriteAction.IsMatch(shortName)) {
                    var nameParam = action.Params.FirstOrDefault(p => NameParams.Contains(p.Field));
                    var field = nameParam == null ? "?" : Resolve(nameParam) ?? "?";
                    var valueParam = action.Params.FirstOrDefault(p => ValueParams.Contains(p.Field));
                    var value = shortName switch {
                        "IncrementPlayerDataInt" => "+1",
                        "DecrementPlayerDataInt" => "-1",
                        "PlayerDataIntAdd" => $"+{(valueParam == null ? "?" : Resolve(valueParam))}",
                        _ => valueParam == null ? "" : Resolve(valueParam) ?? "",
                    };
                    found.Add((state, action, field, value));
                    continue;
                }

                var named = action.Params.Select(Resolve).Where(v => v != null && fieldNames.Contains(v)).Distinct().ToList();
                if (named.Count == 0) {
                    continue;
                }

                unknownActions[shortName] = unknownActions.GetValueOrDefault(shortName) + 1;
                foreach (var field in named) {
                    addRead(field, true, shortName, readCategory, $"{place}:{objectName}:{fsmName}:{state.Name}:{shortName}");
                }
            }

            // Arrays of strings, like PlayerDataBoolAllTrue.stringVariables, only show up among the state's strings
            var structuredValues = state.Structured.SelectMany(a => a.Params).Select(p => p.Value?.Trim('"')).ToHashSet();
            foreach (var text in state.Strings.Where(s => fieldNames.Contains(s) && !structuredValues.Contains(s))) {
                addRead(text, true, "(state strings)", readCategory, $"{place}:{objectName}:{fsmName}:{state.Name}");
            }
        }

        if (found.Count == 0) {
            return;
        }

        // States up to two transitions before or after each state, for the notable actions near a write
        var predecessors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var state in states) {
            foreach (var (_, to) in state.Transitions.Select(Split)) {
                if (to == null) {
                    continue;
                }

                if (!predecessors.TryGetValue(to, out var list)) {
                    predecessors[to] = list = [];
                }

                list.Add(state.Name);
            }
        }

        HashSet<string> Near(string name) {
            var near = new HashSet<string>(StringComparer.Ordinal) { name };
            var frontier = new List<string> { name };
            for (var step = 0; step < 2; step++) {
                var next = new List<string>();
                foreach (var current in frontier) {
                    var neighbours = (byName.TryGetValue(current, out var s) ? s.Transitions.Select(t => Split(t).To) : [])
                        .Concat(predecessors.GetValueOrDefault(current) ?? []);
                    foreach (var neighbour in neighbours) {
                        if (neighbour != null && byName.ContainsKey(neighbour) && near.Add(neighbour)) {
                            next.Add(neighbour);
                        }
                    }
                }

                frontier = next;
            }

            return near;
        }

        var fsmNotable = string.Join(",", states.SelectMany(s => s.Structured).Select(a => ShortName(a.Name))
            .Where(n => NotableAction.IsMatch(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(20));
        var hasInteract = graph != null && graph.IdleStates.Count > 0;
        foreach (var (state, action, field, value) in found) {
            var stateActions = string.Join(",", state.Structured.Select(a => ShortName(a.Name)).Distinct());
            var nearActions = string.Join(",", Near(state.Name).SelectMany(n => byName[n].Structured)
                .Select(a => ShortName(a.Name)).Where(n => NotableAction.IsMatch(n)).Distinct()
                .OrderBy(n => n, StringComparer.Ordinal));
            writes.Add(new WriteRow(
                record == null ? "template" : "fsm", place, record?.BundleKind ?? "template", record?.Category ?? "",
                record?.InArena == true ? "1" : "0", objectName, fsmName, templateName, state.Name,
                ShortName(action.Name), field, value, hasInteract ? graph.Class : "", hasInteract ? graph.Reason : "",
                talk.Contains(state.Name) ? "1" : "0", stateActions, nearActions, fsmNotable,
                string.Join(",", record?.Components ?? [])
            ));
        }
    }

    /// <summary>
    /// Whether a serialized type has a field whose name looks like a reference to the player data.
    /// </summary>
    private static bool HasReferenceField(AssetTypeTemplateField field, int depth) {
        if (field == null || depth > 10) {
            return false;
        }

        if (depth > 0 && ReferenceFieldName.IsMatch(field.Name)) {
            return true;
        }

        return field.Children != null && field.Children.Any(child => HasReferenceField(child, depth + 1));
    }

    /// <summary>
    /// The string fields under a serialized object whose value is the name of a PlayerData field, or whose name is a
    /// template of such names, with the simple values next to them.
    /// </summary>
    private static IEnumerable<(string Path, string Value, string Siblings)> References(
        AssetTypeValueField root,
        HashSet<string> fieldNames
    ) {
        var stack = new Stack<(AssetTypeValueField Field, string Path, AssetTypeValueField Parent, int Depth)>();
        foreach (var child in root.Children) {
            stack.Push((child, child.FieldName, root, 0));
        }

        while (stack.Count > 0) {
            var (field, path, parent, depth) = stack.Pop();
            if (field.Value?.ValueType == AssetValueType.String) {
                var value = field.AsString;
                if (string.IsNullOrEmpty(value)) {
                    continue;
                }

                var isName = fieldNames.Contains(value);
                var isTemplate = !isName && Regex.IsMatch(field.FieldName, "Template|orderListPd") && value.Length < 80;
                if (!isName && !isTemplate) {
                    continue;
                }

                var siblings = string.Join(",", parent.Children
                    .Where(c => SiblingValues.Contains(c.FieldName) && c.Value != null && c.Value.ValueType != AssetValueType.ByteArray)
                    .Select(c => $"{c.FieldName}={c.Value.AsObject}"));
                yield return (path, value, siblings);
                continue;
            }

            if (depth > 12 || field.Children == null || field.Value?.ValueType == AssetValueType.ByteArray) {
                continue;
            }

            foreach (var child in field.Children.Take(4096)) {
                stack.Push((child, $"{path}.{child.FieldName}", field, depth + 1));
            }
        }
    }

    /// <summary>
    /// The names of the fields of PlayerData, read from the game's Assembly-CSharp.dll next to the bundles.
    /// </summary>
    private static HashSet<string> LoadPlayerDataFields(string bundleDir) {
        var dataDir = Directory.GetParent(bundleDir)!.Parent!.Parent!.FullName;
        var dll = Path.Combine(dataDir, "Managed", "Assembly-CSharp.dll");
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.TypeDefinitions) {
            var type = metadata.GetTypeDefinition(handle);
            if (metadata.GetString(type.Name) != "PlayerData" || !type.GetDeclaringType().IsNil) {
                continue;
            }

            foreach (var fieldHandle in type.GetFields()) {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & System.Reflection.FieldAttributes.Static) == 0) {
                    names.Add(metadata.GetString(field.Name));
                }
            }
        }

        return names;
    }

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    private static (string Event, string To) Split(string transition) {
        var index = transition.IndexOf("->", StringComparison.Ordinal);
        return index < 0 ? (transition, null) : (transition[..index], transition[(index + 2)..]);
    }

    private static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}
