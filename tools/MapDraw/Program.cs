using System.Numerics;
using AssetsTools.NET.Extra;

// Draws the game's map from the game's own files, with only the rooms a save has on its map, and marks a spot in one
// room. Used to answer "where on my map is ..." without showing rooms the player hasn't seen.
//
//   dotnet run -c Release --project tools/MapDraw -- render <save json> <out prefix> <scene> <pixels per unit>
//       <room width> <room height> <spot x> <spot y> [<map area>]
//     save json: a decrypted user*.dat. scene: the room to mark, e.g. Greymoor_05. room size: the room's tk2dTileMap
//     width and height (FsmScan --layout <scene> --classes tk2dTileMap). spot: a position in that room, e.g. a
//     creature's from the same listing. map area: a group under Game_Map_Hornet, e.g. Greymoor, to draw only that
//     area; without it every mapped room is drawn. Writes <out prefix>_area.png (or _overview.png) and
//     <out prefix>_marks.json with the pixel places of the room and the spot; mark.ps1 then draws a circle and labels.
//   dotnet run ... -- dump <path part> ...   lists the map pieces whose path contains any part
//   dotnet run ... -- fields atlas|map <AssetClassID> <depth>   prints the field tree of the first asset of a type

var bundles = @"D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64";
var atlasPath = Path.Combine(bundles, @"atlases_assets_assets\sprites\_atlases\hornet_map.spriteatlas.bundle");
var mapPath = Path.Combine(bundles, "maps_assets_all.bundle");

if (args.Length > 0 && args[0] == "fields") {
    Fields.Run(args[1] == "atlas" ? atlasPath : mapPath, Enum.Parse<AssetClassID>(args[2]), int.Parse(args[3]));
    return;
}

var manager = new AssetsManager();
var atlas = Atlas.Load(manager, atlasPath);
var map = MapData.Load(manager, mapPath, atlas.CabName);
Console.WriteLine($"atlas {atlas.Width}x{atlas.Height} with {atlas.Sprites.Count} sprites; map {map.Pieces.Count} " +
                  $"sprite renderers, {map.Pieces.Count(p => p.IsScene)} rooms");

if (args[0] == "dump") {
    foreach (var piece in map.Pieces.Where(p => args.Skip(1).Any(a => p.Path.Contains(a)))) {
        var sprite = atlas.Sprites.GetValueOrDefault(piece.SpritePathId);
        var full = atlas.Sprites.GetValueOrDefault(piece.FullSpritePathId);
        Console.WriteLine($"{piece.Path} active={piece.Active} enabled={piece.Enabled} scene={piece.IsScene} " +
                          $"at=({piece.Place.X:0.##},{piece.Place.Y:0.##},{piece.Z:0.##}) " +
                          $"scale=({piece.World.M11:0.##},{piece.World.M22:0.##}) colour={piece.Color} " +
                          $"sprite=({piece.SpriteFileId}){sprite?.Name} full=({piece.FullSpriteFileId}){full?.Name} " +
                          $"state={piece.InitialState} sort={piece.SortingLayer}/{piece.SortingOrder} " +
                          $"flip={piece.FlipX}/{piece.FlipY}");
        if (piece.IsScene) Console.WriteLine("    " + piece.SceneFields);
    }

    return;
}

if (args[0] != "render") throw new ArgumentException($"unknown mode {args[0]}");

var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(args[1]));
var playerData = json.RootElement.TryGetProperty("playerData", out var pdElement) ? pdElement : json.RootElement;
var scenesMapped = playerData.GetProperty("scenesMapped").EnumerateArray().Select(e => e.GetString()).ToHashSet();
var target = args[3];
var scale = float.Parse(args[4]);
float roomW = float.Parse(args[5]), roomH = float.Parse(args[6]), spotX = float.Parse(args[7]), spotY = float.Parse(args[8]);
var group = args.Length > 9 ? args[9] : null;

var scenePieces = map.Pieces.Where(p => p.IsScene).ToList();
var byObject = scenePieces.GroupBy(p => p.GameObject).ToDictionary(g => g.Key, g => g.First());
MapPiece ByScript(long script) => map.SceneObjectOf.TryGetValue(script, out var go) ? byObject.GetValueOrDefault(go) : null;

// GameMap.SetupMap and GameMapScene.IsMapped: the room's name is in scenesMapped, or every room in its
// mappedIfAllMapped is, or its mappedParent is mapped
bool IsMapped(MapPiece p, int depth = 0) {
    if (scenesMapped.Contains(p.Name)) return true;
    var others = p.MappedIfAllMapped.Select(ByScript).Where(o => o != null).ToList();
    if (others.Count > 0 && others.All(o => scenesMapped.Contains(o.Name))) return true;
    var parent = p.MappedParent != 0 ? ByScript(p.MappedParent) : null;
    return parent != null && depth < 20 && IsMapped(parent, depth + 1);
}

// GameMapScene.SetMapped: the full sprite when the room starts rough (initialState 1); a renderer without a sprite
// of its own (file 0) draws nothing
SpriteInfo LookOf(MapPiece p) {
    var useFull = p.InitialState == 1 && p.FullSpritePathId != 0 && p.FullSpriteFileId == map.AtlasFileId;
    var id = useFull ? p.FullSpritePathId : p.SpritePathId;
    var file = useFull ? p.FullSpriteFileId : p.SpriteFileId;
    return file == map.AtlasFileId ? atlas.Sprites.GetValueOrDefault(id) : null;
}

(Vector2 Min, Vector2 Max) BoundsOf(MapPiece p, SpriteInfo s) {
    var min = new Vector2(float.MaxValue);
    var max = new Vector2(float.MinValue);
    foreach (var (x, y) in s.Positions) {
        var w = Vector2.Transform(new Vector2(x * (p.FlipX ? -1 : 1), y * (p.FlipY ? -1 : 1)), p.World);
        min = Vector2.Min(min, w);
        max = Vector2.Max(max, w);
    }

    return (min, max);
}

var drawn = scenePieces.Where(p => IsMapped(p)).ToList();
var looks = drawn.Select(p => (Piece: p, Sprite: LookOf(p))).Where(x => x.Sprite != null).ToList();
Console.WriteLine($"mapped rooms {drawn.Count}, drawn {looks.Count}; not in the map's atlas: " +
                  string.Join(" ", drawn.Where(p => LookOf(p) == null).Select(p => $"{p.Path}({p.SpriteFileId})")));
if (group != null) {
    looks = looks.Where(x => x.Piece.Path.StartsWith($"Game_Map_Hornet/{group}/", StringComparison.Ordinal) &&
                             x.Piece.Path.Count(ch => ch == '/') == 2).ToList();
    Console.WriteLine($"area {group}: {looks.Count} rooms");
}

// Unity draws by sorting layer, then order, then back to front
looks = looks.OrderBy(x => x.Piece.SortingLayer).ThenBy(x => x.Piece.SortingOrder).ThenByDescending(x => x.Piece.Z)
    .ToList();

var targetPiece = scenePieces.First(p => p.Name == target);
var targetSprite = LookOf(targetPiece);
var targetBounds = BoundsOf(targetPiece, targetSprite);
Console.WriteLine($"{target}: mapped={IsMapped(targetPiece)}, sprite {targetSprite.Name}, bounds " +
                  $"{targetBounds.Min}..{targetBounds.Max}");

// GameMap.GetMapPosition: the room's size maps onto the bounds sprite's rect, centred on the piece
var size = new Vector2(targetSprite.W / targetSprite.Ppu * targetPiece.World.M11,
    targetSprite.H / targetSprite.Ppu * targetPiece.World.M22);
var spot = targetPiece.Place - size / 2 + new Vector2(spotX / roomW * size.X, spotY / roomH * size.Y);

var all = looks.Select(x => BoundsOf(x.Piece, x.Sprite)).ToList();
var allMin = all.Aggregate(new Vector2(float.MaxValue), (m, b) => Vector2.Min(m, b.Min)) - new Vector2(0.4f);
var allMax = all.Aggregate(new Vector2(float.MinValue), (m, b) => Vector2.Max(m, b.Max)) + new Vector2(0.4f);
var canvas = new Canvas(allMin.X, allMin.Y, allMax.X, allMax.Y, scale);
foreach (var (p, s) in looks) {
    // The marked room is drawn yellow when one area is drawn
    var tint = group != null && p == targetPiece
        ? new Vector4(0.95f, 0.80f, 0.42f, 1)
        : new Vector4(p.Color.X, p.Color.Y, p.Color.Z, 1);
    canvas.DrawSprite(atlas, s, p.World, p.FlipX, p.FlipY, tint);
}

var name = group != null ? "area" : "overview";
Png.Write($"{args[2]}_{name}.png", canvas.ToBytes(new Vector3(0.075f, 0.07f, 0.065f)), canvas.Width, canvas.Height);
var centre = canvas.ToPixel((targetBounds.Min + targetBounds.Max) / 2);
var corner = canvas.ToPixel(targetBounds.Max);
var spotPixel = canvas.ToPixel(spot);
File.WriteAllText($"{args[2]}_marks.json",
    $"{{\"w\": {canvas.Width}, \"h\": {canvas.Height}, \"cx\": {centre.X:0.#}, \"cy\": {centre.Y:0.#}, " +
    $"\"hw\": {Math.Abs(corner.X - centre.X):0.#}, \"hh\": {Math.Abs(corner.Y - centre.Y):0.#}, " +
    $"\"sx\": {spotPixel.X:0.#}, \"sy\": {spotPixel.Y:0.#}}}");
Console.WriteLine($"{name}: {canvas.Width}x{canvas.Height}, spot at pixel ({spotPixel.X:0.#}, {spotPixel.Y:0.#})");
