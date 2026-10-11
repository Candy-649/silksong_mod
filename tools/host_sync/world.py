"""Builds world.tsv: every saved world object (reports/coop-world-items.tsv rows, keyed scene + id like
coop-world-items.json / persistent-items.tsv) with the host-aligned class, plus the prefab (corpse/pool) pickups that
have no scene row."""
import collections, csv, json, os, re

R = r'D:\programming\silksong_mod\reports'
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(R, 'host-sync')
info = json.load(open(os.path.join(OUT, 'item_class.json')))
item_class = info['class']

# scene pickups: (scene, object name) -> set(items)
by_object = collections.defaultdict(set)
for item, places in info['pickups'].items():
    for p in places:
        scene, name = p.split(':', 1)
        by_object[(scene.lower(), name)].add(item)

rows = list(csv.DictReader(open(os.path.join(R, 'coop-world-items.tsv'), encoding='utf-8'), delimiter='\t'))
MONEY_COMPONENTS = re.compile(r'RosaryCache|GeoControl|ShellShard|CurrencyObject|SilkGrubCocoon|SilkRationMachine')
MECHANISM_INT = re.compile(r'HitSlidePlatform|StateChangeSequence|ThreadSpinner|BouncePodSequence|LiftPlatform|HitResponse')


def pickup_class(items):
    classes = {item_class.get(i, 'UNSURE') for i in items}
    if classes == {'ONLY_ADD'}:
        return 'ONLY_ADD'
    if classes <= {'PERSONAL', 'SESSION'}:
        return 'PERSONAL'
    if classes == {'HOST'}:
        return 'HOST'
    return 'UNSURE'


def classify(r):
    cat, reason, comps, fsms, kind = r['category'], r['reason'], r['components'], r['fsms'], r['kind']
    scene, oid, path = r['scene'].lower(), r['id'], r['path']
    last = path.split('/')[-1]
    if cat == 'resets':
        return 'SESSION', 'semi-persistent: resets at a bench, never copied', ''
    if cat in ('world', 'arena', 'boss', 'scripted', 'character', 'enemy', 'reward'):
        why = {'world': 'world mechanism (door, lever, wall, shortcut, toll, arena win)', 'arena': 'arena part',
               'boss': 'object that depends on a boss record', 'scripted': 'scripted scene',
               'character': 'NPC state', 'enemy': 'one-time enemy (dead stays dead; its money drop is lost to the client)',
               'reward': 'one-time enemy/arena that records a journal kill (journal entry follows the journal rule)'}[cat]
        return 'HOST', why + f' [old {cat}: {reason}]', ''
    # ---- kept: decide per kind
    if kind == 'GeoRock':
        return 'PERSONAL', 'rosary rock (money)', ''
    if kind == 'PersistentIntItem':
        if MONEY_COMPONENTS.search(comps) or re.search(r'Shard|rosary|scissor_string|ant_item_string', oid, re.I):
            return 'PERSONAL', 'money cache / shell shard fossil (PersistentIntItem)', ''
        if oid == 'Pin Challenge':
            return 'PERSONAL', 'pin gallery counter (pin gallery personal)', ''
        if oid.startswith('Shower Lever'):
            return 'HOST', 'lever (world mechanism)', ''
        if oid == 'Hero Treadmill Rosaries Paid':
            return 'HOST', 'paid world mechanism (toll-like world purchase)', ''
        if 'Toll_machine_silk_ration' in oid:
            return 'PERSONAL', 'vending machine stock (purchase)', ''
        if re.search(r'HealthManager|NPC', comps):
            return 'HOST', 'NPC / enemy state (PersistentIntItem)', ''
        if MECHANISM_INT.search(comps) or re.search(r'Sequence|Spinner|gate|lift', oid, re.I):
            return 'HOST', 'world mechanism with a counter (sliding platform, sequence, spinner, gate, lift)', ''
        return 'UNSURE', 'PersistentIntItem without a rule', ''
    items = by_object.get((scene, last)) or by_object.get((scene, oid)) or set()
    if items:
        if pickup_class(items) == 'HOST':
            return 'HOST', 'pickup that starts a wish (wish state follows the host): ' + ','.join(sorted(items)), ','.join(sorted(items))
        return pickup_class(items), 'world pickup of ' + ','.join(sorted(items)) + ' -> ' + '/'.join(sorted({item_class.get(i, '?') for i in items})), ','.join(sorted(items))
    if 'SavedItemTrackerMarker' in comps:
        if re.search(r'Heart Piece', oid):
            return 'ONLY_ADD', 'world mask shard (Heart Container Control): per-source +1 shard, pickup taken', 'Heart Piece'
        if re.search(r'Silk Spool', oid):
            return 'ONLY_ADD', 'world spool piece: per-source +1 piece, pickup taken', 'Silk Spool'
        if 'moss_berry' in oid:
            return 'PERSONAL', 'mossberry (wish material)', 'Mossberry'
    if oid == 'Heart Piece':
        return 'ONLY_ADD', 'world mask shard: per-source +1 shard, pickup taken', 'Heart Piece'
    if 'MemoryOrbSource' in comps or oid == 'Memory Orb Group (1)':
        return 'ONLY_ADD', 'memory orb gathered (goes with memoryOrbs_* bitmask, union)', ''
    if re.search(r'GeoControl|ShellShard|SilkGrubCocoon|BreakableHolder', reason) or re.search(r'SetGeoDrop', reason):
        return 'PERSONAL', 'money / shards / silk grub: ' + reason, ''
    if 'Chest Control' in reason:
        return 'PERSONAL', 'UNSURE-lite: chest; contents not resolved (names suggest rosaries/shards); default personal', ''
    if oid.startswith('Slab Lock'):
        return 'HOST', 'key lock gate (mechanism opened with a key; key stays personal)', ''
    if oid.startswith('craw_court_summons_pin'):
        return 'ONLY_ADD', 'story summons item pickup (goes with the plot item)', 'Craw Summons'
    if 'Memory Locket' in oid:
        return 'ONLY_ADD', 'world crest socket unlocker source', 'Crest Socket Unlocker'
    if oid.startswith('thread_memory'):
        return 'PERSONAL', 'needolin lore memory: no item or world change found; each player may see it', ''
    if oid in ('Needolin Memory', 'Memory Orb Challenge', 'Memory Orb Challenge (1)'):
        return 'UNSURE', 'memory font challenge that gives an item through an FSM (item not resolvable); suggested ONLY_ADD if it is an upgrade', ''
    if oid in ('door_wakeInMemory', 'Reminder Needolin - Memory', 'Memory Group'):
        return 'HOST', 'memory scene / memento dropper state (scripted)', ''
    if oid.startswith(('tool_metal_deposit', 'Nectar Pickup', 'Shell Shard Fossil', 'churchkeeper_rosary', 'pustule')):
        return 'PERSONAL', 'material / wish item / money source', ''
    if oid.startswith('Ladybug Craft Pickup'):
        return 'PERSONAL', 'pin gallery craft (pin gallery personal)', ''
    if reason.startswith('flag '):
        return 'HOST', 'world gate / cell door / cave-in tied to a story flag (' + reason + '); scripted scenes follow the host', ''
    if 'NPC' in oid or re.search(r'Caretaker|Mask Maker|Pinstress|Caravan', oid):
        if oid == 'Moss Creep NPC':
            return 'PERSONAL', 'NPC trade for mossberries (purchase)', ''
        return 'HOST', 'NPC state (its gift goes through the item rules)', ''
    return 'UNSURE', 'kept object without a rule: ' + reason, ''


out = []
for r in rows:
    cls, why, items = classify(r)
    out.append((r['scene'], r['id'], r['kind'], cls, why, items, r['path']))

# prefab pickups (corpses, pools): saved by playerDataBool or by the corpse scene; listed for completeness
for item, places in info['drops'].items():
    for p in places:
        bundle, name = p.split(':', 1)
        out.append(('(prefab) ' + bundle, name, 'CollectableItemPickup', item_class.get(item, 'UNSURE'),
                    'corpse/pool drop of ' + item + ' (class of the item; saved through a PlayerData bool or the corpse)', item, ''))

with open(os.path.join(OUT, 'world.tsv'), 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n')
    w.writerow(['scene', 'id', 'kind', 'class', 'reason', 'items', 'path'])
    for row in out:
        w.writerow(row)

c = collections.Counter(r[3] for r in out)
print(c, len(out))
print(collections.Counter((r[3], r[4][:60]) for r in out if r[3] in ('UNSURE', 'ONLY_ADD')).most_common(40))
