using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

/// <summary>
/// Prints where the objects that make up boss rooms are, for --layout: camera lock areas, arenas, enter triggers,
/// enemies, and the FSMs and colliders inside boss scenes, with world positions and collider bounds. Used to check how
/// co-op can tell which players are in a room.
/// </summary>
internal static class LayoutDumper {
    /// <summary>
    /// Script classes whose objects define or start rooms, printed wherever they are in a scene.
    /// </summary>
    private static readonly HashSet<string> RoomClasses = new(StringComparer.Ordinal) {
        "CameraLockArea", "BattleScene", "TriggerEnterEvent", "AlertRange", "HeroPerformanceRegion",
    };

    /// <summary>
    /// Parts of object paths that belong to boss rooms, whose FSMs and colliders are printed.
    /// </summary>
    private static readonly string[] BossPathParts = ["Boss Scene", "Battle Scene", "Battle Gate", "Gates"];

    /// <param name="bundleDir">The folder of the game's bundles.</param>
    /// <param name="scenes">The scenes to print, separated by commas.</param>
    /// <param name="extraClasses">More script classes to print wherever they are, separated by commas, each with its
    /// simple serialized values; null for none.</param>
    public static void Run(string bundleDir, string scenes, string extraClasses = null) {
        var extras = new HashSet<string>(
            (extraClasses ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal
        );
        var manager = new AssetsManager();
        var scriptNames = BundleScanner.LoadScriptNames(manager, bundleDir);
        manager.UnloadAll();

        foreach (var scene in scenes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var path = Path.Combine(bundleDir, "scenes_scenes_scenes", scene + ".bundle");
            Console.WriteLine($"== {scene}");
            if (!File.Exists(path)) {
                Console.WriteLine("   no bundle");
                continue;
            }

            try {
                var bundle = manager.LoadBundleFile(path, true);
                for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++) {
                    if (!bundle.file.IsAssetsFile(i)) {
                        continue;
                    }

                    var instance = manager.LoadAssetsFileFromBundle(bundle, i, false);
                    instance.file.GenerateQuickLookup();
                    var context = new FileContext(manager, instance, scriptNames);
                    foreach (var info in instance.file.GetAssetsOfType(AssetClassID.MonoBehaviour)) {
                        context.MonoClasses[info.PathId] = context.ScriptClass(info);
                    }

                    new SceneLayout(manager, instance, context, extras).Print();
                }
            } catch (Exception e) {
                Console.WriteLine($"   error: {e.GetType().Name}: {e.Message}");
            } finally {
                manager.UnloadAll();
            }
        }
    }

    /// <summary>
    /// The layout of one assets file of a scene.
    /// </summary>
    private sealed class SceneLayout {
        private readonly AssetsManager _manager;
        private readonly AssetsFileInstance _instance;
        private readonly FileContext _context;
        private readonly Dictionary<long, (Vector3 Position, Quaternion Rotation, Vector3 Scale)> _worlds = new();

        /// <summary>
        /// Script classes printed on top of the room ones, with their values.
        /// </summary>
        private readonly HashSet<string> _extras;

        public SceneLayout(
            AssetsManager manager,
            AssetsFileInstance instance,
            FileContext context,
            HashSet<string> extras
        ) {
            _manager = manager;
            _instance = instance;
            _context = context;
            _extras = extras;
        }

        public void Print() {
            var lines = new List<string>();
            foreach (var info in _instance.file.GetAssetsOfType(AssetClassID.GameObject)) {
                var gameObject = _context.GetGameObject(info.PathId);
                if (gameObject == null) {
                    continue;
                }

                var ancestors = _context.Ancestors(gameObject);
                var path = string.Join(
                    "/",
                    ancestors.AsEnumerable().Reverse().Select(ancestor => ancestor.Name).Append(gameObject.Name)
                );
                var classes = gameObject.ComponentClasses;
                var isRoomObject = classes.Any(name => RoomClasses.Contains(name) || _extras.Contains(name));
                var isBossObject = BossPathParts.Any(part => path.Contains(part, StringComparison.Ordinal));
                if (!isRoomObject && !isBossObject) {
                    continue;
                }

                // Children of enemies are mostly their attacks and hitboxes
                if (!isRoomObject && ancestors.Any(ancestor => ancestor.ComponentClasses.Contains("HealthManager"))) {
                    continue;
                }

                var field = _manager.GetBaseField(_instance, info);
                var fsmNames = new List<string>();
                var colliders = new List<string>();
                var extra = "";
                foreach (var pair in field.Get("m_Component", "Array")?.Children ?? []) {
                    var pointer = pair.Get("component");
                    if (pointer == null || pointer["m_FileID"].AsInt != 0) {
                        continue;
                    }

                    var componentInfo = _instance.file.GetAssetInfo(pointer["m_PathID"].AsLong);
                    if (componentInfo == null) {
                        continue;
                    }

                    var typeId = (AssetClassID) componentInfo.TypeId;
                    switch (typeId) {
                        case AssetClassID.BoxCollider2D:
                        case AssetClassID.CapsuleCollider2D:
                        case AssetClassID.CircleCollider2D:
                        case AssetClassID.PolygonCollider2D:
                        case AssetClassID.EdgeCollider2D:
                            colliders.Add(
                                DescribeCollider(
                                    typeId,
                                    _manager.GetBaseField(_instance, componentInfo),
                                    gameObject.TransformPathId
                                )
                            );
                            break;
                        case AssetClassID.MonoBehaviour:
                            var className = _context.MonoClasses.GetValueOrDefault(componentInfo.PathId);
                            if (className == "PlayMakerFSM") {
                                fsmNames.Add(
                                    _manager.GetBaseField(_instance, componentInfo).Get("fsm", "name")?.AsString ?? "?"
                                );
                            } else if (className is "CameraLockArea" or "TriggerEnterEvent" ||
                                       (className != null && _extras.Contains(className))) {
                                extra += DescribeValues(className, _manager.GetBaseField(_instance, componentInfo));
                            }

                            break;
                    }
                }

                if (!isRoomObject && fsmNames.Count == 0 && colliders.Count == 0 &&
                    !classes.Contains("HealthManager")) {
                    continue;
                }

                var world = GetWorld(gameObject.TransformPathId);
                var active = field.Get("m_IsActive")?.AsBool == false ? " (inactive)" : "";
                var otherClasses = classes.Where(name => name != "PlayMakerFSM").Distinct();
                lines.Add(
                    $"   {path}{active} | pos ({world.Position.X:F1}, {world.Position.Y:F1}) | " +
                    $"{string.Join(",", otherClasses)} | fsm {string.Join(",", fsmNames)} | " +
                    $"{string.Join("; ", colliders)}{extra}"
                );
            }

            lines.Sort(StringComparer.Ordinal);
            foreach (var line in lines) {
                Console.WriteLine(line);
            }
        }

        /// <summary>
        /// Gets the world position, rotation and scale of a transform.
        /// </summary>
        private (Vector3 Position, Quaternion Rotation, Vector3 Scale) GetWorld(long transformPathId) {
            if (transformPathId == 0) {
                return (Vector3.Zero, Quaternion.Identity, Vector3.One);
            }

            if (_worlds.TryGetValue(transformPathId, out var cached)) {
                return cached;
            }

            var info = _instance.file.GetAssetInfo(transformPathId);
            if (info == null) {
                return (Vector3.Zero, Quaternion.Identity, Vector3.One);
            }

            var field = _manager.GetBaseField(_instance, info);
            var localPosition = ReadVector3(field.Get("m_LocalPosition"));
            var localRotation = ReadQuaternion(field.Get("m_LocalRotation"));
            var localScale = ReadVector3(field.Get("m_LocalScale"), 1f);
            var father = field.Get("m_Father");
            (Vector3 Position, Quaternion Rotation, Vector3 Scale) parent =
                father != null && father["m_FileID"].AsInt == 0 && father["m_PathID"].AsLong != 0
                ? GetWorld(father["m_PathID"].AsLong)
                : (Vector3.Zero, Quaternion.Identity, Vector3.One);

            var world = (
                parent.Position + Vector3.Transform(parent.Scale * localPosition, parent.Rotation),
                parent.Rotation * localRotation,
                parent.Scale * localScale
            );
            _worlds[transformPathId] = world;
            return world;
        }

        /// <summary>
        /// Describes a collider with its kind, whether it's a trigger, and its bounds in the world.
        /// </summary>
        private string DescribeCollider(AssetClassID typeId, AssetTypeValueField field, long transformPathId) {
            var offset = ReadVector2(field.Get("m_Offset"));
            var points = new List<Vector2>();
            switch (typeId) {
                case AssetClassID.BoxCollider2D:
                case AssetClassID.CapsuleCollider2D:
                    var half = ReadVector2(field.Get("m_Size")) / 2f;
                    points.Add(offset - half);
                    points.Add(offset + half);
                    points.Add(new Vector2(offset.X - half.X, offset.Y + half.Y));
                    points.Add(new Vector2(offset.X + half.X, offset.Y - half.Y));
                    break;
                case AssetClassID.CircleCollider2D:
                    var radius = field.Get("m_Radius")?.AsFloat ?? 0f;
                    points.Add(offset - new Vector2(radius));
                    points.Add(offset + new Vector2(radius));
                    break;
                case AssetClassID.PolygonCollider2D:
                    foreach (var pathField in field.Get("m_Points", "m_Paths", "Array")?.Children ?? []) {
                        foreach (var point in pathField.Get("Array")?.Children ?? []) {
                            points.Add(offset + ReadVector2(point));
                        }
                    }

                    break;
                case AssetClassID.EdgeCollider2D:
                    foreach (var point in field.Get("m_Points", "Array")?.Children ?? []) {
                        points.Add(offset + ReadVector2(point));
                    }

                    break;
            }

            var name = typeId.ToString().Replace("Collider2D", "");
            var flags = (field.Get("m_IsTrigger")?.AsBool == true ? "trigger" : "solid") +
                        (field.Get("m_Enabled")?.AsBool == false ? ",disabled" : "");
            if (points.Count == 0) {
                return $"{name}({flags})";
            }

            var world = GetWorld(transformPathId);
            var worldPoints = points
                .Select(point => world.Position + Vector3.Transform(world.Scale * new Vector3(point, 0f), world.Rotation))
                .ToList();
            return $"{name}({flags}) x[{worldPoints.Min(p => p.X):F1}..{worldPoints.Max(p => p.X):F1}] " +
                   $"y[{worldPoints.Min(p => p.Y):F1}..{worldPoints.Max(p => p.Y):F1}]";
        }

        /// <summary>
        /// Describes the simple serialized values of a component, leaving out Unity's own fields.
        /// </summary>
        private static string DescribeValues(string className, AssetTypeValueField field) {
            var values = field.Children
                .Where(child => !child.FieldName.StartsWith("m_", StringComparison.Ordinal) && child.Value != null)
                .Where(child => child.Value.ValueType is AssetValueType.Float or AssetValueType.Bool
                    or AssetValueType.String or AssetValueType.Int32)
                .Select(child => $"{child.FieldName}={FormatValue(child)}");
            return $" | {className}: {string.Join(" ", values)}";
        }

        private static string FormatValue(AssetTypeValueField field) {
            return field.Value.ValueType switch {
                AssetValueType.Float => field.AsFloat.ToString("F1"),
                AssetValueType.Bool => field.AsBool.ToString(),
                AssetValueType.String => $"\"{field.AsString}\"",
                _ => field.AsInt.ToString(),
            };
        }

        private static Vector2 ReadVector2(AssetTypeValueField field) {
            return field == null || field.IsDummy
                ? Vector2.Zero
                : new Vector2(field.Get("x")?.AsFloat ?? 0f, field.Get("y")?.AsFloat ?? 0f);
        }

        private static Vector3 ReadVector3(AssetTypeValueField field, float fallback = 0f) {
            return field == null || field.IsDummy
                ? new Vector3(fallback)
                : new Vector3(field.Get("x")?.AsFloat ?? 0f, field.Get("y")?.AsFloat ?? 0f, field.Get("z")?.AsFloat ?? 0f);
        }

        private static Quaternion ReadQuaternion(AssetTypeValueField field) {
            return field == null || field.IsDummy
                ? Quaternion.Identity
                : new Quaternion(
                    field.Get("x")?.AsFloat ?? 0f,
                    field.Get("y")?.AsFloat ?? 0f,
                    field.Get("z")?.AsFloat ?? 0f,
                    field.Get("w")?.AsFloat ?? 1f
                );
        }
    }
}
