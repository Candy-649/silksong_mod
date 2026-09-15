"""Sorts the saved objects of the scenes into objects of the world, which a two-player save shares, and things that one
player keeps, and writes the objects of the world for SSMP.

Input is the TSV of FsmScan --persistent. Only saved booleans that don't reset at benches can be objects of the world:
walls, floors, levers, gates, arenas, lifts, masks of hidden areas and paid tolls. Pickups, money and what holds money,
upgrades, chests, characters (quest state isn't shared yet) and enemies (they drop money for each player) are kept by
each player. When in doubt an object stays with each player, because sharing a pickup would take it from the other.

Usage: py tools/coop_world_items.py [persistent-items.tsv] [coop-world-items.json] [report.tsv]
"""
import collections
import csv
import json
import re
import sys

source = sys.argv[1] if len(sys.argv) > 1 else 'reports/persistent-items.tsv'
target = sys.argv[2] if len(sys.argv) > 2 else 'SSMP/SSMP/Resource/coop-world-items.json'
report = sys.argv[3] if len(sys.argv) > 3 else 'reports/coop-world-items.tsv'

# Pickups, money and what holds it, and upgrades
OWN_COMPONENTS = {
    'CollectableItemPickup', 'GeoControl', 'ShellShard', 'SavedItemTrackerMarker', 'MemoryOrbSource', 'SilkGrubCocoon',
    'BreakableHolder', 'GeoRock', 'RosaryCache', 'RosaryCacheString', 'RosaryCacheHanging', 'RosaryCacheShrine',
    'CurrencyObject',
}
OWN_FSMS = {'Chest Control', 'Heart Container Control'}
OWN_ACTIONS = re.compile(
    r'^(CollectableItem\w*|SetCollectablePickupItem|SavedItem\w*|SetGeoDrop|SetShardDrop|FlingObjectsFromGlobalPoolV3|'
    r'TakeCurrency|AddGeo|AddCurrency|DialogueYesNoItem\w*|CreateUIMsgGetItem|GetQuestReward\w*|SetToolUnlocked)$'
)
OWN_NAMES = re.compile(
    r'pickup|collectable|chest|heart piece|silk spool|memory|locket|nectar|deposit|shard|rosary|cocoon|summons_pin|'
    r'moss_berry|relic|mimic',
    re.I,
)
# Characters and enemies
NPC_COMPONENT = re.compile(r'NPC|Npc')
QUEST_ACTION = re.compile(r'Quest')
ENEMY_COMPONENTS = {'HealthManager'}
# Paid tolls that open the way are world purchases, which both players share
WORLD_NAMES = re.compile(r'^(bell_toll_machine|Toll Door)', re.I)
WORLD_COMPONENTS = {'BlackThreadCore'}


def split(text):
    return {part for part in text.split(',') if part}


def classify(row):
    if row['kind'] != 'PersistentBoolItem':
        return 'kept', row['kind']
    if row['semi'] == '1':
        return 'resets', 'semi-persistent'

    components = split(row['components'])
    fsms = split(row['fsms'])
    actions = split(row['actions'])
    if WORLD_NAMES.search(row['id']):
        return 'world', 'toll'
    if components & OWN_COMPONENTS:
        return 'kept', 'component ' + min(components & OWN_COMPONENTS)
    if fsms & OWN_FSMS:
        return 'kept', 'fsm ' + min(fsms & OWN_FSMS)
    own_actions = sorted(action for action in actions if OWN_ACTIONS.match(action))
    if own_actions:
        return 'kept', 'action ' + own_actions[0]
    if OWN_NAMES.search(row['id']):
        return 'kept', 'name'
    if components & WORLD_COMPONENTS:
        return 'world', 'component ' + min(components & WORLD_COMPONENTS)
    if 'NPC' in row['id'] or any(NPC_COMPONENT.search(component) for component in components):
        return 'character', 'npc'
    quest_actions = sorted(action for action in actions if QUEST_ACTION.search(action))
    if quest_actions:
        return 'character', 'action ' + quest_actions[0]
    if components & ENEMY_COMPONENTS:
        return 'enemy', 'HealthManager'
    return 'world', ''


rows = list(csv.DictReader(open(source, encoding='utf-8'), delimiter='\t'))
world = collections.defaultdict(set)
counts = collections.Counter()
with open(report, 'w', encoding='utf-8', newline='') as out:
    writer = csv.writer(out, delimiter='\t', lineterminator='\n')
    writer.writerow(['scene', 'id', 'category', 'reason', 'kind', 'path', 'components', 'fsms'])
    for row in rows:
        category, reason = classify(row)
        counts[category] += 1
        if category == 'world':
            world[row['scene'].lower()].add(row['id'])
        writer.writerow([row['scene'], row['id'], category, reason, row['kind'], row['path'], row['components'],
                         row['fsms']])

data = {'bools': {scene: sorted(ids) for scene, ids in sorted(world.items())}}
with open(target, 'w', encoding='utf-8', newline='\n') as out:
    json.dump(data, out, indent=1, ensure_ascii=False)
    out.write('\n')

print(dict(counts))
print(f"{sum(len(ids) for ids in world.values())} world objects in {len(world)} scenes: {target}")
print(f"report: {report}")
