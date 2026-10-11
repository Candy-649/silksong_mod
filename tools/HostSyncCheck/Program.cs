using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SSMP.Game.Client.Save;

// HostSyncCheck: runs SSMP's CoopHostSync on decrypted saves outside the game, to see what a joining player's save
// becomes when it is aligned with the host's, and checks the merged save against rules that must always hold.
//
// Usage: dotnet run -c Release --project tools/HostSyncCheck -- <host.json> <client.json> [merged.json]
//            [--data <coop-host-sync.json>]
//   host.json and client.json are SaveGameData JSON ({"playerData": ..., "sceneData": ...}), like the decrypted
//   user<N>.dat files (see the memory note on reading the save).

var arguments = args.ToList();
var dataPath = @"D:\programming\silksong_mod\SSMP\SSMP\Resource\coop-host-sync.json";
var dataIndex = arguments.IndexOf("--data");
if (dataIndex >= 0) {
    dataPath = arguments[dataIndex + 1];
    arguments.RemoveRange(dataIndex, 2);
}

if (arguments.Count < 2) {
    Console.WriteLine("Usage: HostSyncCheck <host.json> <client.json> [merged.json] [--data <coop-host-sync.json>]");
    return 1;
}

var data = JsonConvert.DeserializeObject<CoopHostSyncData>(File.ReadAllText(dataPath))!;
var host = JObject.Parse(File.ReadAllText(arguments[0]));
var client = JObject.Parse(File.ReadAllText(arguments[1]));
var clientBefore = (JObject) client.DeepClone();

var merged = CoopHostSync.Merge(host, client, data, out var report);
if (merged == null) {
    Console.WriteLine($"Refused: {report.Refusal}");
    return 2;
}

Console.WriteLine(report);
Console.WriteLine($"ahead: {report.AheadBosses.Count} bosses, act {report.AheadAct}; arrivals to see: " +
                  string.Join(", ", report.ArrivalsToSee));
foreach (var line in report.Lines) {
    Console.WriteLine("  " + line);
}

var failures = new List<string>();
void Check(bool condition, string what) {
    if (!condition) {
        failures.Add(what);
    }
}

Check(JToken.DeepEquals(client, clientBefore), "the joining player's save was changed in place");

var hostData = (JObject) host["playerData"]!;
var clientData = (JObject) client["playerData"]!;
var mergedData = (JObject) merged["playerData"]!;
int Int(JObject o, string name) => o[name]?.Value<int>() ?? 0;
bool Bool(JObject o, string name) => o[name]?.Type == JTokenType.Boolean && o[name]!.Value<bool>();

// Every field the joining player had is still there, of the same JSON type
foreach (var property in clientData.Properties()) {
    Check(mergedData[property.Name] != null, $"field {property.Name} is gone");
    if (mergedData[property.Name] is { } value && value.Type != property.Value.Type &&
        !(value.Type is JTokenType.Integer or JTokenType.Float && property.Value.Type is JTokenType.Integer or JTokenType.Float)) {
        failures.Add($"field {property.Name} changed type from {property.Value.Type} to {value.Type}");
    }
}

// Masks and spools within what the game has, never fewer than before
var masks = Int(mergedData, "maxHealthBase");
var shards = (masks - 5) * 4 + Int(mergedData, "heartPieces");
var shardsBefore = (Int(clientData, "maxHealthBase") - 5) * 4 + Int(clientData, "heartPieces");
Check(shards is >= 0 and <= 20 && Int(mergedData, "heartPieces") is >= 0 and < 4, $"mask shards {shards}");
Check(shards >= shardsBefore, $"mask shards went down from {shardsBefore} to {shards}");
Check(Int(mergedData, "maxHealth") - Int(clientData, "maxHealth") == masks - Int(clientData, "maxHealthBase"),
    "maxHealth didn't move with maxHealthBase");
var pieces = (Int(mergedData, "silkMax") - 9) * 2 + Int(mergedData, "silkSpoolParts");
var piecesBefore = (Int(clientData, "silkMax") - 9) * 2 + Int(clientData, "silkSpoolParts");
Check(pieces is >= 0 and <= 18 && Int(mergedData, "silkSpoolParts") is 0 or 1, $"spool pieces {pieces}");
Check(pieces >= piecesBefore, $"spool pieces went down from {piecesBefore} to {pieces}");

// What stays the joining player's
foreach (var name in new[] { "geo", "ShellShards", "playTime", "CurrentCrestID", "HeroCorpseScene", "nailUpgrades" }) {
    if (name == "CurrentCrestID") {
        continue;
    }

    Check(JToken.DeepEquals(mergedData[name], clientData[name]), $"{name} isn't the joining player's");
}

// What is the host's
foreach (var name in new[] { "respawnScene", "respawnMarkerName", "respawnType", "blackThreadWorld", "act3_wokeUp" }) {
    Check(JToken.DeepEquals(mergedData[name], hostData[name]), $"{name} isn't the host's");
}

// Beaten bosses are the host's
foreach (var property in hostData.Properties()) {
    if (property.Value.Type == JTokenType.Boolean && CoopHostSync.IsDefeatRecord(property.Name)) {
        Check(Bool(mergedData, property.Name) == (bool) property.Value, $"boss record {property.Name} isn't the host's");
    }
}

// Important items only get added: abilities, tools and crests the joining player had stay
foreach (var name in data.Fields.GetValueOrDefault("or", [])) {
    Check(!Bool(clientData, name) || Bool(mergedData, name), $"{name} was taken away");
}

foreach (var listName in new[] { "Tools", "ToolEquips" }) {
    var before = clientData[listName]?["savedData"] as JArray ?? [];
    var after = (mergedData[listName]?["savedData"] as JArray ?? []).OfType<JObject>()
        .ToDictionary(entry => (string) entry["Name"]!, entry => entry);
    foreach (var entry in before.OfType<JObject>()) {
        var name = (string) entry["Name"]!;
        if (entry["Data"]?["IsUnlocked"]?.Value<bool>() == true) {
            Check(after.TryGetValue(name, out var now) && now["Data"]?["IsUnlocked"]?.Value<bool>() == true,
                $"{listName} {name} was taken away");
        }
    }
}

// Wishes are the host's, but for the deliveries
var hostQuests = (hostData["QuestCompletionData"]?["savedData"] as JArray ?? []).OfType<JObject>()
    .ToDictionary(entry => (string) entry["Name"]!, entry => entry);
foreach (var entry in (mergedData["QuestCompletionData"]?["savedData"] as JArray ?? []).OfType<JObject>()) {
    var name = (string) entry["Name"]!;
    if (data.Quests.GetValueOrDefault(name) == "own") {
        continue;
    }

    Check(hostQuests.TryGetValue(name, out var hostEntry) && JToken.DeepEquals(hostEntry["Data"], entry["Data"]),
        $"wish {name} isn't the host's");
}

// No duplicate entries in any list
foreach (var listName in new[] { "Collectables", "Tools", "ToolEquips", "QuestCompletionData", "Relics" }) {
    var names = (mergedData[listName]?["savedData"] as JArray ?? []).OfType<JObject>()
        .Select(entry => (string) entry["Name"]!).ToList();
    Check(names.Count == names.Distinct().Count(), $"{listName} has the same name twice");
}

foreach (var listName in new[] { "persistentBools", "persistentInts", "geoRocks" }) {
    var keys = (merged["sceneData"]?[listName]?["serializedList"] as JArray ?? []).OfType<JObject>()
        .Select(entry => ((string) entry["SceneName"]!).ToLowerInvariant() + "/" + (string) entry["ID"]!).ToList();
    Check(keys.Count == keys.Distinct().Count(), $"{listName} has the same object twice");
}

Check((string?) mergedData["version"] == CoopHostSync.GameVersion, "version isn't the game's");

if (arguments.Count > 2) {
    File.WriteAllText(arguments[2], merged.ToString(Formatting.None));
}

if (failures.Count > 0) {
    Console.WriteLine($"{failures.Count} checks failed:");
    foreach (var failure in failures) {
        Console.WriteLine("  " + failure);
    }

    return 3;
}

Console.WriteLine("All checks passed");
return 0;
