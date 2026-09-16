"""Sorts the items of the game into story items, which a two-player save shares, and things that each player keeps,
and writes the story items for SSMP.

Input is the dumps of FsmScan --dump in reports/itemdumps. A story item is one that the story gates progress on: a
lock key, an item that a one-off story interaction uses up, a memento and a relic. Picking one up is never shared:
every pickup in the world stays one copy per player, like all other pickups. The list only says which items a character
handing one over on a first talk gives to both players, because the partner has no pickup of it anywhere, and which of
them a one-off story interaction takes from both. Everything that a player can farm or buy stays with each player:
consumables, materials, upgrades, maps, currency sets, tools and crests, and the targets of wishes that drop from
enemies.

Removing an item is shared only where the story takes it for good and the world shows the result to both players: the
keys that exist once, and the items that a one-off interaction builds something out of. An item that a shop sells, asks
for or takes in for an upgrade stays personal when it is used, so a player who bought or upgraded their own still has
theirs. Mementos and relics are never taken, and what a wish takes is already taken from both players by the wish sync.

Usage: py tools/coop_story_items.py [reports/itemdumps] [SSMP/SSMP/Resource/coop-story-items.json] [report.tsv]
"""
import json
import os
import re
import sys

source = sys.argv[1] if len(sys.argv) > 1 else 'reports/itemdumps'
target = sys.argv[2] if len(sys.argv) > 2 else 'SSMP/SSMP/Resource/coop-story-items.json'
report = sys.argv[3] if len(sys.argv) > 3 else 'reports/coop-story-items.tsv'

# The dumps that hold the items, by the type that SSMP resolves them as
ITEM_DUMPS = {
    'dump_basic.txt': 'CollectableItemBasic',
    'dump_CollectableItemMemento.txt': 'CollectableItemMemento',
    'dump_CollectableItemStates.txt': 'CollectableItemStates',
    'dump_CollectableItemRelicType.txt': 'CollectableItemRelicType',
    'dump_CollectableItemToolDamage.txt': 'CollectableItemToolDamage',
    'dump_CollectableItemGrower.txt': 'CollectableItemGrower',
    'dump_CollectableItemStack.txt': 'CollectableItemStack',
    'dump_CollectableItemQuestDisplay.txt': 'CollectableItemQuestDisplay',
    'dump_CollectableRelic.txt': 'CollectableRelic',
    'dump_PlayerDataCollectable.txt': 'PlayerDataCollectable',
}

# Items that a one-off story interaction uses up, outside the locks and the collection desk, which the dumps can't tell
# apart from materials: a single one exists, a character or a machine takes it, and the world shows the result
UNIQUE_PLOT = {
    'Crawbell', 'Craw Summons', 'White Flower', 'Wood Witch Item', 'Coral Chunk', 'Broken SilkShot', 'Blue Goop Jar',
}
# Fake collectables that only write the player data and gate the story, the only ones of their kind that are shared:
# the maps, the upgrades and the quills that count a state stay with each player
BOOL_KEYS = {'Slab Key A', 'Slab Key B', 'Slab Key C'}
# At most this many pickups in the world, and at least one, for the target of a wish to count as fixed rather than
# farmed. Targets that drop from enemies or breakables have no pickup of their own at all.
MAX_FIXED_PICKUPS = 3
# Names of items that are money, materials or consumables even where something else would match them
MATERIAL_NAMES = re.compile(
    r'^(Rosary_Set|Shard Pouch|Great Shard|Pristine Core|Tool Metal|Silk Grub|Enemy Morsel|Crest Socket Unlocker|'
    r'Cog Heart Pieces|Growstone|Quill|Dresses|Tool Pouch|Invalid Item Template|Slab Key$)'
)


def read_dump(path):
    """The entries of a dump of FsmScan --dump, as dictionaries of their fields."""
    entries = []
    current = None
    for line in open(path, encoding='utf-8', errors='replace'):
        if line.startswith('== '):
            current = {}
            entries.append(current)
            continue
        if current is None:
            continue
        text = line.strip()
        if ' = ' in text:
            key, _, value = text.partition(' = ')
            current[key] = value.strip().strip('"')
        elif ' -> ' in text:
            key, _, value = text.partition(' -> ')
            current[key] = value.strip()
    return [entry for entry in entries if entry]


def reference(value):
    """The name of a reference that a dump prints as "Name (Type)", or None for a null reference."""
    if not value or value == 'null':
        return None
    match = re.match(r'^(.*?) \((\w+)\)$', value)
    return match.group(1) if match else None


def read_items(folder):
    """Every item of the game, by name, with its type and the fields that tell what it is."""
    items = {}
    for file_name, type_name in ITEM_DUMPS.items():
        path = os.path.join(folder, file_name)
        if not os.path.exists(path):
            print(f'missing dump: {path}', file=sys.stderr)
            continue
        for entry in read_dump(path):
            name = entry.get('m_Name', '')
            if name:
                items[name] = {'type': type_name, 'fields': entry}
    return items


def read_referenced(folder, file_name, pattern):
    """The names of the items that a dump refers to through fields matching the pattern."""
    names = set()
    path = os.path.join(folder, file_name)
    if not os.path.exists(path):
        print(f'missing dump: {path}', file=sys.stderr)
        return names
    for entry in read_dump(path):
        for key, value in entry.items():
            if pattern.match(key) and (name := reference(value)):
                names.add(name)
    return names


def read_pickups(folder):
    """How many pickups of each item lie in the world."""
    counts = {}
    path = os.path.join(folder, 'dump_pickups.txt')
    if not os.path.exists(path):
        print(f'missing dump: {path}', file=sys.stderr)
        return counts
    for entry in read_dump(path):
        if name := reference(entry.get('item', '')):
            counts[name] = counts.get(name, 0) + 1
    return counts


def read_shop_items(folder):
    """The names of the items that shops sell, ask for, or take in for an upgrade."""
    return read_referenced(
        folder, 'dump_shopitem.txt', re.compile(r'^(savedItem|requiredItem|upgradeFromItem)$')
    )


def read_wish_targets(folder):
    """The names of the items that wishes count towards their targets."""
    names = set()
    path = os.path.join(folder, 'dump_Quest.txt')
    if not os.path.exists(path):
        print(f'missing dump: {path}', file=sys.stderr)
        return names
    for entry in read_dump(path):
        for key, value in entry.items():
            if re.match(r'^targets\[\d+\]\.Counter$|^targetCounter$', key) and (name := reference(value)):
                names.add(name)
    return names


def classify(items, keys, desk_items, shop_items, pickups, wish_targets):
    """Sorts the items into the story items that both players share, with the kind of each one and whether the story
    takes it from both players, and the reason that keeps every other item with each player."""
    shared = []
    kept = []
    for name in sorted(items):
        item = items[name]
        type_name = item['type']
        kind, share_removal, reason = None, False, None

        if MATERIAL_NAMES.match(name):
            reason = 'money, material or consumable'
        elif type_name == 'CollectableRelic':
            kind = 'relic'
        elif type_name == 'CollectableItemMemento':
            kind = 'memento'
        elif type_name == 'PlayerDataCollectable':
            if name in BOOL_KEYS:
                kind = 'boolKey'
            else:
                reason = 'map, upgrade or other state of the player'
        elif name in keys:
            # A key that shops also sell is bought as well as found, so using one only takes the key of its user
            kind = 'key'
            share_removal = name not in shop_items
        elif name in desk_items or name in UNIQUE_PLOT:
            kind = 'plot'
            # An item that a shop takes in for an upgrade is used up by each player on their own
            share_removal = name not in shop_items
        elif type_name == 'CollectableItemRelicType':
            # The counter of a kind of relic, which the relics themselves raise
            reason = 'counter of the relics of one kind'
        elif name in wish_targets:
            # What a wish takes is taken from both players by the wish sync, so only the gain is shared, and only for
            # targets that lie in the world rather than dropping from enemies
            count = pickups.get(name, 0)
            if 0 < count <= MAX_FIXED_PICKUPS:
                kind = 'wish'
            else:
                reason = f'target of a wish that is farmed ({count} pickups)'
        elif item['fields'].get('useResponses (count', '0') != '0':
            reason = 'consumable'
        else:
            reason = 'not part of the story'

        if kind:
            shared.append({'type': type_name, 'name': name, 'kind': kind, 'shareRemoval': share_removal})
        else:
            kept.append((type_name, name, reason))
    return shared, kept


def main():
    items = read_items(source)
    keys = read_referenced(source, 'dump_receptacle.txt', re.compile(r'^requiredItem$'))
    desk_items = read_referenced(source, 'dump_desk.txt', re.compile(r'^sections\[\d+\]\.UnlockItem$'))
    shop_items = read_shop_items(source)
    pickups = read_pickups(source)
    wish_targets = read_wish_targets(source)

    shared, kept = classify(items, keys, desk_items, shop_items, pickups, wish_targets)

    # What a wish takes is taken from both players by the wish sync already, so a shared removal on top of that
    # would take the item twice
    both = sorted({entry['name'] for entry in shared if entry['shareRemoval']} & wish_targets)
    assert not both, f'items whose removal is shared are also targets of wishes: {", ".join(both)}'

    missing = sorted((keys | desk_items | UNIQUE_PLOT | BOOL_KEYS) - set(items))
    if missing:
        print(f'items that no dump holds: {", ".join(missing)}', file=sys.stderr)

    os.makedirs(os.path.dirname(target) or '.', exist_ok=True)
    with open(target, 'w', encoding='utf-8', newline='\n') as file:
        json.dump({'items': shared}, file, indent=2, ensure_ascii=False)
        file.write('\n')

    os.makedirs(os.path.dirname(report) or '.', exist_ok=True)
    with open(report, 'w', encoding='utf-8', newline='\n') as file:
        file.write('state\ttype\tname\tkind\tshareRemoval\treason\tpickups\n')
        for entry in shared:
            file.write(
                f'shared\t{entry["type"]}\t{entry["name"]}\t{entry["kind"]}\t{entry["shareRemoval"]}\t\t'
                f'{pickups.get(entry["name"], 0)}\n'
            )
        for type_name, name, reason in kept:
            file.write(f'kept\t{type_name}\t{name}\t\t\t{reason}\t{pickups.get(name, 0)}\n')

    counts = {}
    for entry in shared:
        counts[entry['kind']] = counts.get(entry['kind'], 0) + 1
    removals = sum(1 for entry in shared if entry['shareRemoval'])
    print(f'{len(shared)} story items ({", ".join(f"{kind} {count}" for kind, count in sorted(counts.items()))}), '
          f'{removals} of them removed from both players; {len(kept)} items stay with each player')
    print(f'wrote {target} and {report}')


main()
