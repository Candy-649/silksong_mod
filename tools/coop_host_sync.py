"""Writes the data that SSMP aligns a joining player's save with the host's save by (CoopHostSync).

A co-op save is no longer bound to its members: when a player joins, their save takes the host's world (beaten bosses,
story, mechanisms, wishes), keeps their own belongings (money, materials, shop buys, loadout), and gets the important
things that the host found and they didn't (abilities, upgrades, tools, crests, keys, story items) without losing the
ones they found themselves (the user's rules of 2026-10-11). The research behind each class is in reports/host-sync,
made by tools/host_sync/fields.py, items.py and world.py:

- classification.tsv: every field of the player data, with its class and how it merges
- items.tsv: every collectable, tool, crest, relic and wish, with its class and where it comes from
- world.tsv: every saved object of the scenes, with its class and the items a pickup gives

Wish rewards come from reports/itemdumps/dump_quest_rewards.txt (FsmScan --dump Quest/MainQuest/SubQuest --fields
"m_Name|reward"), the fields that collectables set from dump_PlayerDataCollectable.txt, and the key locks from
dump_receptacle.txt.

Usage: py tools/coop_host_sync.py [reports dir] [coop-host-sync.json]
"""
import collections
import csv
import json
import os
import re
import sys

reports = sys.argv[1] if len(sys.argv) > 1 else 'reports'
target = sys.argv[2] if len(sys.argv) > 2 else 'SSMP/SSMP/Resource/coop-host-sync.json'
research = os.path.join(reports, 'host-sync')
dumps = os.path.join(reports, 'itemdumps')


def rows(path):
    with open(path, encoding='utf-8', newline='') as file:
        return list(csv.DictReader(file, delimiter='\t'))


def blocks(path):
    """The objects of an FsmScan dump: (name, lines)."""
    name, lines = None, []
    for line in open(path, encoding='utf-8', errors='replace'):
        line = line.rstrip('\n')
        if line.startswith('== '):
            if name is not None:
                yield name, lines
            name, lines = line.split(' : ', 1)[1] if ' : ' in line else line[3:], []
        elif name is not None:
            lines.append(line.strip())
    if name is not None:
        yield name, lines


def value(lines, key):
    for line in lines:
        if line.startswith(key + ' = '):
            return line.split(' = ', 1)[1].strip('"')
        if line.startswith(key + ' -> '):
            return line.split(' -> ', 1)[1]
    return None


# ---------------------------------------------------------------- fields of the player data
FIELD_MERGES = {
    'take host': 'host',
    'special': 'host',  # the act flags go together, both from the host
    'take host + fix crest': 'host',  # SSMP puts the hero on the right crest afterwards
    'OR': 'or',
    'per-source': 'or',  # flags of spool sources, which go with the spools
    'max': 'max',
    'bitwise OR': 'bits',
}
fields = collections.defaultdict(list)
for row in rows(os.path.join(research, 'classification.tsv')):
    merge = FIELD_MERGES.get(row['merge'])
    if merge:
        fields[merge].append(row['field'])
# Silk hearts and attunement come from events that leave no saved object behind, so the larger count is taken; an
# event that the joining player loses with a boss they have to fight again gives its heart again
fields['max'] += ['silkRegenMax', 'attunement']
for merge in fields:
    fields[merge] = sorted(set(fields[merge]))

# ---------------------------------------------------------------- items
# How a collectable that sets a field of the player data gives itself
pd_items = {}
for name, lines in blocks(os.path.join(dumps, 'dump_PlayerDataCollectable.txt')):
    bool_name = value(lines, 'linkedPDBool') or ''
    int_name = value(lines, 'linkedPDInt') or ''
    entry = {}
    if bool_name:
        entry['bool'] = bool_name
    elif int_name:
        entry['add'] = int_name
    else:
        variable = value(lines, 'setPlayerDataInts[0].variableName')
        operation = value(lines, 'setPlayerDataInts[0].operation')
        number = value(lines, 'setPlayerDataInts[0].number')
        if variable:
            # 0 adds, 3 raises to at least the number (the quills)
            entry['add' if operation == '0' else 'atLeast'] = variable
            entry['n'] = int(number or 1)
    pd_items[name] = entry

# Items with more than one of them: each source gives one more. Everything else is unique, and a source makes sure
# the player has it.
COUNTED = {
    'Heart Piece', 'Silk Spool', 'Crest Socket Unlocker', 'Simple Key', 'Ward Key', 'Architect Key',
    'Cog Heart Pieces', 'Tool Pouch Pickup', 'Tool Kit Pickup', 'Needle Upgrade', 'Silk Heart',
    'Attunement Increase',
}
# Story items that something in the world uses up. The host's world says whether it was used: a host who had it and
# used it leaves it at 0 for the joining player too, as the lock or scene that took it is used up in their world now
STORY = {
    'Belltown House Key', 'Dock Key', 'Dock Demo Key', 'Ward Boss Key', 'Coral Chunk', 'Craw Summons', 'Crawbell',
    'Farsight', 'Materium', 'White Flower', 'Wood Witch Item', 'Blue Goop Jar', 'Cog Heart Pieces',
}

items = {}
collections_of = {'Collectables': 'col', 'Tools': 'tool', 'ToolEquips': 'crest', 'Relics': 'relic',
                  'PlayerData (via collectable)': 'pd', 'QuestCompletionData': 'quest'}
quests = {}
for row in rows(os.path.join(research, 'items.tsv')):
    where = collections_of[row['collection']]
    name = row['item']
    if where == 'quest':
        quests[name] = 'host' if row['class'] == 'HOST' else 'own'
        continue

    # A tool that the host has goes to the joining player whichever way the host got it: its pickups in the world
    # aren't saved (they show while the player hasn't got the tool), and a shop that sells it has no flag of its own
    # (ShopItem.playerDataBoolName is empty for tools), so a bought one can't be told from a found one
    entry = {'in': where, 'class': row['class'], 'kind': row['kind']}
    if name in COUNTED:
        entry['counted'] = True
    if name in STORY:
        entry['story'] = True
    if where == 'pd':
        link = pd_items.get(name)
        if link:
            entry.update(link)
    if 'dropped once by a boss' in row['reason']:
        # A wish item that a boss drops once: with the boss beaten in the host's world, a joining player whose wish is
        # still open gets the host's, or could never hand the wish in
        match = re.search(r'wish target: ([^;]+)', row['sources'])
        if match:
            entry['onceFor'] = [quest.strip() for quest in match.group(1).split(',')]
    items[name] = entry

# The mask shards and spool pieces are prefab collectables whose fields are kept whole by SSMP itself
items['Heart Piece'].update({'in': 'pd', 'counted': True, 'shards': True})
items['Silk Spool'].update({'in': 'pd', 'counted': True, 'spools': True})

# ---------------------------------------------------------------- wish rewards
rewards = {}
for name, lines in blocks(os.path.join(dumps, 'dump_quest_rewards.txt')):
    count = int(value(lines, 'rewardCount') or 1)
    count_act3 = int(value(lines, 'rewardCountAct3') or 0)
    rewards[name] = {'count': count, 'act3': count_act3}

reward_items = {}
for row in rows(os.path.join(research, 'items.tsv')):
    if row['collection'] != 'QuestCompletionData':
        continue
    match = re.search(r'reward: ([^;]+)', row['sources'])
    if match and row['class'] == 'HOST':
        item = match.group(1).strip()
        counts = rewards.get(row['item'], {'count': 1, 'act3': 0})
        reward_items[row['item']] = [{'item': item, 'count': counts['count'], 'act3': counts['act3']}]
# A wish that gives its reward through its character instead of the wish asset (the game counts it as a spool source
# itself, PlayerDataFixer.FixSilkSpools)
reward_items.setdefault('Save Sherma', []).append({'item': 'Silk Spool', 'count': 1, 'act3': 0})
for quest, given in reward_items.items():
    for reward in given:
        item = reward['item']
        if item not in items:
            raise SystemExit(f'the reward {item} of {quest} is not a known item')

# ---------------------------------------------------------------- saved objects of the world
CLASS_CODES = {'HOST': 'h', 'ONLY_ADD': 'a', 'UNSURE': 'a'}
# A host change of an object wins over one that only adds, and both over one that a player keeps
PRIORITY = {'a': 2, 'h': 1}
world = {'bools': collections.defaultdict(dict), 'ints': collections.defaultdict(dict)}
sources = collections.defaultdict(dict)
for row in rows(os.path.join(research, 'world.tsv')):
    if row['scene'].startswith('(prefab)'):
        continue
    kind = {'PersistentBoolItem': 'bools', 'PersistentIntItem': 'ints'}.get(row['kind'])
    code = CLASS_CODES.get(row['class'])
    scene = row['scene'].lower()
    if kind is None or code is None:
        continue
    known = world[kind][scene].get(row['id'])
    if known is None or PRIORITY[code] > PRIORITY[known]:
        world[kind][scene][row['id']] = code
    if kind == 'bools' and code == 'a' and row['items']:
        given = sorted(set(sources[scene].get(row['id'], [])) | set(row['items'].split(',')))
        sources[scene][row['id']] = given

# What SSMP itself saves of the world, like the arenas that a player won (coop-world-items.json), follows the host too
world_items_path = os.path.join(os.path.dirname(target), 'coop-world-items.json')
with open(world_items_path, encoding='utf-8') as file:
    for scene, ids in json.load(file)['bools'].items():
        for object_id in ids:
            world['bools'][scene.lower()].setdefault(object_id, 'h')

# A breakable that holds a pickup is saved apart from the pickup itself; only the pickup gives the item
for scene, objects in sources.items():
    for object_id in list(objects):
        if not object_id.startswith('Breakable'):
            continue
        others = [given for other, given in objects.items() if other != object_id]
        if any(set(objects[object_id]) <= set(given) for given in others):
            del objects[object_id]

for scene, objects in sources.items():
    for object_id, given in objects.items():
        for item in given:
            if item not in items:
                raise SystemExit(f'the world pickup {scene}/{object_id} gives the unknown item {item}')

# ---------------------------------------------------------------- key locks
# Each lock that the host opened and the joining player hadn't takes one of their keys; each one they had opened that
# the host hasn't gives it back, as it closes in their world again
LOCK_IDS = {
    # The saved object that a receptacle saves its state in, where it isn't the receptacle's own name (FsmScan --dump
    # ItemReceptacle --fields persistent --resolve <scene>.bundle)
    ('crawl_02', 'lock'): 'aspid_sealed_gate_stone',
    ('ward_01', 'key_machine'): 'under_lift_large',
}
locks = collections.defaultdict(list)
receptacles = []
current = None
for line in open(os.path.join(dumps, 'dump_receptacle.txt'), encoding='utf-8', errors='replace'):
    line = line.rstrip('\n')
    if line.startswith('== '):
        scene = line[3:].split(chr(92))[-1].split('.bundle : ')
        current = {'scene': scene[0].lower(), 'name': scene[1] if len(scene) > 1 else ''}
        receptacles.append(current)
    elif current is not None:
        stripped = line.strip()
        if stripped.startswith('requiredItem -> '):
            current['item'] = stripped.split(' -> ', 1)[1].split(' (', 1)[0]
        elif stripped.startswith('playerDataBool = '):
            current['bool'] = stripped.split(' = ', 1)[1].strip('"')
        elif stripped.startswith('persistent -> '):
            current['persistent'] = stripped.split(' -> ', 1)[1] != 'null'
for receptacle in receptacles:
    item = receptacle.get('item')
    if item not in ('Simple Key', 'Ward Key', 'Architect Key'):
        continue
    if receptacle.get('bool'):
        locks[item].append({'bool': receptacle['bool']})
    elif receptacle.get('persistent'):
        key = (receptacle['scene'], receptacle['name'])
        object_id = LOCK_IDS.get(key, receptacle['name'])
        if object_id and world['bools'].get(receptacle['scene'], {}).get(object_id) == 'h':
            locks[item].append({'scene': receptacle['scene'], 'id': object_id})
        else:
            print(f'lock without a known saved object: {receptacle["scene"]}/{receptacle["name"]} ({item})')

# ---------------------------------------------------------------- crests
# An upgraded crest takes over the slots of the version before it, like ToolCrest.Unlock does
crest_versions = {}
for name, entry in items.items():
    match = re.match(r'^(.+)_v(\d+)$', name)
    if entry['in'] == 'crest' and match:
        number = int(match.group(2))
        crest_versions[name] = match.group(1) if number == 2 else f'{match.group(1)}_v{number - 1}'

data = {
    'fields': dict(sorted(fields.items())),
    'bools': {scene: dict(sorted(objects.items())) for scene, objects in sorted(world['bools'].items())},
    'ints': {scene: dict(sorted(objects.items())) for scene, objects in sorted(world['ints'].items())},
    'sources': {scene: dict(sorted(objects.items())) for scene, objects in sorted(sources.items()) if objects},
    'items': dict(sorted(items.items())),
    'quests': dict(sorted(quests.items())),
    'rewards': dict(sorted(reward_items.items())),
    'locks': dict(sorted(locks.items())),
    'crestVersions': dict(sorted(crest_versions.items())),
}
with open(target, 'w', encoding='utf-8', newline='\n') as file:
    json.dump(data, file, ensure_ascii=False, separators=(',', ':'))
    file.write('\n')

print(
    f'{target}: ' + ', '.join(f'{len(names)} {merge}' for merge, names in sorted(fields.items())) + ' fields; '
    f'{sum(len(o) for o in world["bools"].values())} bools, {sum(len(o) for o in world["ints"].values())} ints; '
    f'{sum(len(o) for o in sources.values())} pickups; {len(items)} items; {len(quests)} quests; '
    f'{len(reward_items)} rewards; ' + ', '.join(f'{len(l)} {k} locks' for k, l in sorted(locks.items()))
)
