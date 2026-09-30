"""Sorts the saved objects of the scenes into objects of the world, which a two-player save shares, and things that one
player keeps, and writes the objects of the world for SSMP.

Input is the TSV of FsmScan --persistent. Only saved booleans that don't reset at benches can be objects of the world:
walls, floors, levers, gates, lifts, masks of hidden areas and paid tolls. Each player keeps pickups, money and what
holds money, upgrades, chests, characters (quest state isn't shared yet) and enemies (they drop money for each player).
Arenas, objects that give a reward like a journal entry, objects that play a scripted scene and objects that depend on a
boss stay with each player too, because copying them could take a fight, a scene or a reward from the other player. Some
objects of the world set booleans of the player data, like a broken wall that the other side checks; those booleans go
with the object. When in doubt an object stays with each player.

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
# The pustules save whether a player drew their sample from them (LifebloodPustule's persistentBroken, which sits on
# the pustule's parent), like a pickup; each player has their own to draw from, as they take their own hits
OWN_NAMES = re.compile(
    r'pickup|collectable|chest|heart piece|silk spool|memory|locket|nectar|deposit|shard|rosary|cocoon|summons_pin|'
    r'moss_berry|relic|mimic|pustule',
    re.I,
)
# Rewards that one player gets for the object, on the object or its parent
REWARD_ACTIONS = re.compile(
    r'^(RecordJournalKill\w*|CompleteJournalRecord\w*|AwardAchievement\w*|QueueAchievement\w*|AwardQueuedAchievements|'
    r'SpawnPowerUpGetMsg|SpawnSkillGetMsg|CreateUIMsgGetItem|SetToolUnlocked)$'
)
# Arenas and what belongs to them
ARENA_COMPONENTS = {'BattleScene'}
ARENA_NAMES = re.compile(r'battle ?scene', re.I)
# Flags of the player data that scripted scenes set while they play
SCRIPT_FLAGS = {'disableInventory', 'disablePause', 'disableSaveQuit', 'isInvincible'}
# Flags of objects that free a character, reopen a story gate or close a challenge: copying them could skip a scene, or
# take a challenge and its reward from the other player
KEEP_FLAGS = {'UnlockedDustCage', 'slab_cloak_gate_reopened', 'lavaChallengeEntranceCavedIn'}
# Records of beaten and met bosses
BOSS_RECORD = re.compile(r'^(defeated|encountered)|(Defeated|Encountered)$')
# Characters and enemies
NPC_COMPONENT = re.compile(r'NPC|Npc')
QUEST_ACTION = re.compile(r'Quest')
ENEMY_COMPONENTS = {'HealthManager'}
# Paid tolls that open the way are world purchases, which both players share
WORLD_NAMES = re.compile(r'^(bell_toll_machine|Toll Door)', re.I)
WORLD_COMPONENTS = {'BlackThreadCore'}
# A write of the player data in the TSV, like "SetPlayerDataBool(boolName=x,value=True)"
WRITE = re.compile(r'^(\w+)\((.*)\)$')


def split(text):
    return {part for part in text.split(',') if part}


def player_data(row):
    """The booleans of the player data that an object and its parent set to true, and the category and reason that
    keep the object with each player, if its writes can't go with it."""
    names = set()
    writes = [(text, True) for text in row['data_writes'].split(';') if text]
    writes += [(text, False) for text in row['parent_data_writes'].split(';') if text]
    for text, own in writes:
        match = WRITE.match(text)
        if not match:
            return names, ('kept', 'player data ' + text)
        action = match.group(1)
        params = dict(re.findall(r'(\w+)=([^,]*)', match.group(2)))
        if action.endswith('Test'):
            continue
        if action == 'SetPlayerDataBool':
            name, value = params.get('boolName', ''), params.get('value', '')
        elif action == 'SetPlayerDataVariable':
            name, value = params.get('VariableName', ''), params.get('SetValue', '')
        else:
            return names, ('kept', 'player data ' + text)
        # The FSMs that many walls share leave the name empty when a wall has no boolean of its own
        if name == '':
            continue
        if name in SCRIPT_FLAGS:
            return names, ('scripted', name)
        if name in KEEP_FLAGS:
            return names, ('kept', 'flag ' + name)
        if BOSS_RECORD.search(name):
            return names, ('boss', name)
        if name.startswith('var:') or value != 'True':
            return names, ('kept', 'player data ' + text)
        # A parent can hold many saved objects, so only the booleans of the object itself go with it
        if own:
            names.add(name)
    return names, None


def classify(row):
    """The category of a saved object, why, and the booleans of the player data that go with it if it is shared."""
    if row['kind'] != 'PersistentBoolItem':
        return 'kept', row['kind'], set()
    if row['semi'] == '1':
        return 'resets', 'semi-persistent', set()

    components = split(row['components'])
    fsms = split(row['fsms'])
    actions = split(row['actions'])
    toll = WORLD_NAMES.search(row['id'])
    if not toll:
        if components & OWN_COMPONENTS:
            return 'kept', 'component ' + min(components & OWN_COMPONENTS), set()
        if fsms & OWN_FSMS:
            return 'kept', 'fsm ' + min(fsms & OWN_FSMS), set()
        own_actions = sorted(action for action in actions if OWN_ACTIONS.match(action))
        if own_actions:
            return 'kept', 'action ' + own_actions[0], set()
        if OWN_NAMES.search(row['id']):
            return 'kept', 'name', set()

    reward_actions = sorted(action for action in actions | split(row['parent_actions']) if REWARD_ACTIONS.match(action))
    if reward_actions:
        return 'reward', 'action ' + reward_actions[0], set()
    arena = (components | split(row['parent_components'])) & ARENA_COMPONENTS
    if arena or ARENA_NAMES.search(row['path']):
        return 'arena', min(arena) if arena else 'name', set()
    records = split(row['records']) | split(row['parent_records'])
    if records:
        return 'boss', 'record ' + min(records), set()

    if not toll and not components & WORLD_COMPONENTS:
        if 'NPC' in row['id'] or any(NPC_COMPONENT.search(component) for component in components):
            return 'character', 'npc', set()
        quest_actions = sorted(action for action in actions if QUEST_ACTION.search(action))
        if quest_actions:
            return 'character', 'action ' + quest_actions[0], set()
        if components & ENEMY_COMPONENTS:
            return 'enemy', 'HealthManager', set()

    names, problem = player_data(row)
    if problem:
        return problem[0], problem[1], set()
    return 'world', 'toll' if toll else '', names


rows = list(csv.DictReader(open(source, encoding='utf-8'), delimiter='\t'))
world = collections.defaultdict(set)
companions = collections.defaultdict(lambda: collections.defaultdict(set))
not_shared = set()
counts = collections.Counter()
reasons = collections.Counter()
with open(report, 'w', encoding='utf-8', newline='') as out:
    writer = csv.writer(out, delimiter='\t', lineterminator='\n')
    writer.writerow(['scene', 'id', 'category', 'reason', 'player_data', 'kind', 'path', 'components', 'fsms'])
    for row in rows:
        category, reason, names = classify(row)
        counts[category] += 1
        key = (row['scene'].lower(), row['id'])
        if category == 'world':
            world[key[0]].add(row['id'])
            companions[key[0]][row['id']] |= names
        else:
            not_shared.add(key)
            if category in ('reward', 'arena', 'boss', 'scripted') or reason.startswith(('player data', 'flag')):
                reasons[category + ': ' + re.sub(r'\d+', '#', reason)] += 1
        writer.writerow([row['scene'], row['id'], category, reason, ','.join(sorted(names)), row['kind'], row['path'],
                         row['components'], row['fsms']])

# Objects of a scene with the same ID share one saved boolean, which is only shared if none of them is kept
conflicts = 0
for scene, ids in world.items():
    for item_id in list(ids):
        if (scene, item_id) in not_shared:
            ids.discard(item_id)
            companions[scene].pop(item_id, None)
            conflicts += 1

data = {
    'bools': {scene: sorted(ids) for scene, ids in sorted(world.items()) if ids},
    'playerData': {
        scene: {item_id: sorted(names) for item_id, names in sorted(items.items()) if names}
        for scene, items in sorted(companions.items())
        if any(items.values())
    },
}
with open(target, 'w', encoding='utf-8', newline='\n') as out:
    json.dump(data, out, indent=1, ensure_ascii=False)
    out.write('\n')

print(dict(counts))
for reason, count in reasons.most_common(25):
    print(f'  {count:4} {reason}')
shared = sum(len(ids) for ids in data['bools'].values())
with_data = sum(len(items) for items in data['playerData'].values())
print(f"{shared} world objects in {len(data['bools'])} scenes, {with_data} with player data, {conflicts} IDs kept "
      f"because another object with the ID is kept: {target}")
print(f"report: {report}")
