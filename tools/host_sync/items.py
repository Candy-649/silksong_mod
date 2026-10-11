"""Builds items.tsv: every per-item entry of the PlayerData collections (Collectables, Relics, Tools, ToolEquips crests,
QuestCompletionData quests, plus the PlayerDataCollectable / PrefabCollectable upgrades that live in plain fields) with
kind, class and reason, and the item sources (shop, wish reward, world pickups) that decide the class.

Also writes reports/host-sync/item_class.json for world.py (item name -> class)."""
import collections, csv, json, os, re, sys

R = r'D:\programming\silksong_mod\reports'
D = os.path.join(R, 'itemdumps')
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(R, 'host-sync')
DUMPS = os.path.join(OUT, 'dumps')


def blocks(path):
    """Yields (bundle, name, lines) for every '== bundle : name' block of an FsmScan dump."""
    bundle = name = None
    lines = []
    for line in open(path, encoding='utf-8', errors='replace'):
        line = line.rstrip('\n')
        if line.startswith('== '):
            if name is not None:
                yield bundle, name, lines
            head = line[3:]
            bundle, _, name = head.partition(' : ')
            lines = []
        elif name is not None:
            lines.append(line.strip())
    if name is not None:
        yield bundle, name, lines


def field(lines, key):
    for line in lines:
        if line.startswith(key + ' = ') or line.startswith(key + ' -> '):
            value = line.split(' = ', 1)[1] if ' = ' in line[:len(key) + 3] else line.split(' -> ', 1)[1]
            return value.strip().strip('"')
    return None


def ref(value):
    """'Tack (ToolItemBasic)' -> ('Tack', 'ToolItemBasic')"""
    if not value or value == 'null':
        return None, None
    m = re.match(r'^(.*) \((\w+)\)$', value)
    return (m.group(1), m.group(2)) if m else (value, None)


# ---- shops
shop = collections.defaultdict(list)          # item -> shop item names
shop_pd = collections.defaultdict(list)       # playerdata field -> shop item names
shop_required = set()
for _, name, lines in blocks(os.path.join(D, 'dump_shopitem.txt')):
    item, kind = ref(field(lines, 'savedItem'))
    if item:
        shop[item].append(name)
    for key in ('playerDataBoolName', 'playerDataIntName'):
        v = field(lines, key)
        if v:
            shop_pd[v].append(name)
    for l in lines:
        m = re.match(r'setExtraPlayerDataBools\[\d+\] = "(.*)"', l)
        if m:
            shop_pd[m.group(1)].append(name + ' (extra)')
    for key in ('requiredItem', 'upgradeFromItem'):
        r, _ = ref(field(lines, key))
        if r:
            shop_required.add(r)

# ---- wish rewards and targets
reward_of = collections.defaultdict(list)
target_of = collections.defaultdict(list)
quests = []
for f, kind in (('dump_Quest.txt', 'wish'), ('dump_MainQuest.txt', 'main quest'), ('dump_SubQuest.txt', 'sub quest')):
    for _, name, lines in blocks(os.path.join(D, f)):
        quests.append((name, kind))
        r, _ = ref(field(lines, 'rewardItem'))
        if r:
            reward_of[r].append(name)
        for l in lines:
            m = re.match(r'targets\[\d+\]\.Counter -> (.*)$', l)
            if m:
                t, _ = ref(m.group(1))
                if t:
                    target_of[t].append(name)

# ---- world pickups (scene bundles only count as world finds; prefab bundles are corpse or pool drops)
pickups = collections.defaultdict(list)       # item -> [scene/object]
drops = collections.defaultdict(list)         # item -> [prefab bundle/object]
for bundle, name, lines in blocks(os.path.join(D, 'dump_pickups.txt')):
    item, _ = ref(field(lines, 'item'))
    if not item:
        continue
    b = bundle.replace('\\', '/')
    if b.startswith('scenes_scenes_scenes/'):
        pickups[item].append(b.split('/')[-1].replace('.bundle', '') + ':' + name)
    else:
        drops[item].append(b.split('/')[-1].replace('.bundle', '') + ':' + name)

# ---- the old story item list (kind per item)
story = {}
for row in csv.DictReader(open(os.path.join(R, 'coop-story-items.tsv'), encoding='utf-8'), delimiter='\t'):
    story[row['name']] = row

items = []   # (collection, name, type, kind, class, reason, sources)


def sources(name):
    parts = []
    if pickups.get(name):
        parts.append(f'world pickups {len(pickups[name])}')
    if drops.get(name):
        parts.append(f'prefab/corpse drops {len(drops[name])}')
    if shop.get(name):
        parts.append('shop: ' + ','.join(shop[name]))
    if reward_of.get(name):
        parts.append('wish reward: ' + ','.join(reward_of[name]))
    if target_of.get(name):
        parts.append('wish target: ' + ','.join(target_of[name]))
    return '; '.join(parts)


# ---- Collectables (CollectableItem types), from the dumps
ITEM_DUMPS = ['dump_basic.txt', 'dump_CollectableItemMemento.txt', 'dump_CollectableItemStates.txt',
              'dump_CollectableItemToolDamage.txt', 'dump_CollectableItemRelicType.txt', 'dump_CollectableItemGrower.txt',
              'dump_CollectableItemStack.txt', 'dump_CollectableItemQuestDisplay.txt', 'dump_DeliveryQuestItem.txt',
              'dump_DeliveryQuestItemStandalone.txt']
TYPE_OF_DUMP = {'dump_basic.txt': 'CollectableItemBasic'}

CURRENCY = {'Rosary_Set_Frayed', 'Rosary_Set_Huge_White', 'Rosary_Set_Large', 'Rosary_Set_Medium', 'Rosary_Set_Small',
            'Shard Pouch', 'Great Shard'}
MATERIAL = {'Tool Metal', 'Pristine Core', 'Pale_Oil', 'Silk Grub', 'Growstone', 'Mossberry', 'Mossberry Stew',
            'Common Spine', 'Shining Cog', 'Conchfly Remains', 'Fixer Idol', 'Plasmium Gland'}
DERIVED = {'Quill', 'Tool Pouch&Kit Inv', 'Dresses', 'Slab Key', 'Invalid Item Template'}
UNIQUE_KEYS = {'Dock Demo Key', 'Ward Boss Key', 'Belltown House Key', 'Dock Key'}
COUNT_KEYS = {'Simple Key', 'Ward Key', 'Architect Key'}
PLOT = {'White Flower', 'Wood Witch Item', 'Coral Chunk', 'Craw Summons', 'Crawbell', 'Farsight', 'Materium',
        'Broken SilkShot', 'Blue Goop Jar', 'Cog Heart Pieces'}

seen = set()
for f in ITEM_DUMPS:
    for _, name, lines in blocks(os.path.join(D, f)):
        if name in seen:
            continue
        seen.add(name)
        t = TYPE_OF_DUMP.get(f) or re.sub(r'^dump_|\.txt$', '', f)
        src = sources(name)
        st = story.get(name, {})
        if name in DERIVED or t == 'CollectableItemRelicType':
            cls, kind, why = 'SESSION', 'display', 'inventory display item whose amount the game derives from other state (flags, relic list, counts); never copied'
        elif name in CURRENCY:
            cls, kind, why = 'PERSONAL', 'currency', 'money item (rosary string / shard bundle); user rule: currency items are personal'
        elif t in ('DeliveryQuestItem', 'DeliveryQuestItemStandalone'):
            cls, kind, why = 'PERSONAL', 'delivery', 'courier delivery cargo of an accepted wish; breaks on hits; per player'
        elif name == 'Crest Socket Unlocker':
            cls, kind, why = 'ONLY_ADD', 'slot unlocker', 'crest slot unlocker; user rule: crest slots only-add. Count it PER SOURCE: +1 for each world pickup the host took and the client did not (pickup marked taken); shop lockets and wish-reward lockets stay personal; used lockets live in ToolEquips slot IsUnlocked'
        elif name in UNIQUE_KEYS:
            cls, kind, why = 'ONLY_ADD', 'key', 'unique story key; give it unless the host world shows it already used (its lock/door is HOST-open), then the item must NOT be added'
        elif name in COUNT_KEYS:
            cls, kind, why = 'ONLY_ADD', 'key', 'generic key also sold in shops: per source, +1 per world pickup the host took and the client did not, minus the locks of this key kind that the host opened and the client did not (clamped at 0); bought keys stay personal'
        elif name in PLOT:
            cls, kind, why = 'ONLY_ADD', 'plot', 'unique story item from the world/bosses; give it unless the host world shows it used up (shareRemoval items)'
        elif t == 'CollectableItemMemento':
            cls, kind, why = 'ONLY_ADD', 'memento', 'unique keepsake from the world/NPC/boss, cannot be farmed; only-add. Depositing (MementosDeposited) stays personal'
        elif t == 'CollectableItemToolDamage' or name in MATERIAL:
            cls, kind, why = 'PERSONAL', 'material', 'crafting/upgrade material or farmable drop; user rule: materials personal'
        elif st.get('kind') == 'wish' or target_of.get(name):
            cls, kind, why = 'PERSONAL', 'wish item', 'collected or dropped for a wish; user rule: wish collection items personal'
        else:
            cls, kind, why = 'PERSONAL', 'other', 'no rule matched; farmable or not part of the story'
        if kind == 'wish item' and drops.get(name):
            why += ' [DANGER: also dropped once by a boss/arena corpse; if the boss is HOST-dead for the client, the client can never get it -> suggested ONLY_ADD when the host holds or used it]'
        if st.get('shareRemoval') == 'True' and cls == 'ONLY_ADD':
            why += ' [story removes it: shareRemoval]'
        items.append(('Collectables', name, t, kind, cls, why, src))

# ---- Relics
for _, name, lines in blocks(os.path.join(D, 'dump_CollectableRelic.txt')):
    items.append(('Relics', name, 'CollectableRelic', 'relic', 'PERSONAL',
                  'UNSURE default PERSONAL: unique world find, but its main use is selling for rosaries (money); keep each relic pickup available in the client world. Alt: ONLY_ADD IsCollected, never IsDeposited',
                  sources(name)))

# ---- PlayerDataCollectable / PrefabCollectable (upgrades that live in plain PlayerData fields)
PDC = {
    'Needle Upgrade': ('PERSONAL', 'needle upgrade', 'nailUpgrades: paid service of the pinsmith (rosaries + material); treated like a shop purchase'),
    'Silk Heart': ('ONLY_ADD', 'upgrade', 'silkRegenMax; count per source (boss/memory that gives it)'),
    'Attunement Increase': ('ONLY_ADD', 'upgrade', 'attunement int; per source'),
    'Tool Kit Pickup': ('ONLY_ADD', 'upgrade', 'ToolKitUpgrades; per source: world pickups + boss gifts only-add, shop kits and wish-reward kits personal'),
    'Tool Pouch Pickup': ('ONLY_ADD', 'upgrade', 'ToolPouchUpgrades; per source: world pickups only-add, shop pouches and wish-reward pouches personal'),
    'Slab Key A': ('ONLY_ADD', 'bool key', 'HasSlabKeyA; never taken'), 'Slab Key B': ('ONLY_ADD', 'bool key', 'HasSlabKeyB; never taken'),
    'Slab Key C': ('ONLY_ADD', 'bool key', 'HasSlabKeyC; never taken'),
    'Trobbio Quill Purple': ('ONLY_ADD', 'quill', 'QuillState from a boss reward'), 'Trobbio Quill Red': ('ONLY_ADD', 'quill', 'QuillState from a boss reward'),
}
for _, name, lines in blocks(os.path.join(D, 'dump_PlayerDataCollectable.txt')):
    if name.endswith('Map'):
        cls, kind, why = 'ONLY_ADD', 'map (found)', 'UNSURE default ONLY_ADD: map found in the world (no shop sells it); bought maps (Has*Map written by a shop) stay personal'
    else:
        cls, kind, why = PDC.get(name, ('UNSURE', 'other', 'no rule'))
    items.append(('PlayerData (via collectable)', name, 'PlayerDataCollectable', kind, cls, why, sources(name)))
for _, name, lines in blocks(os.path.join(D, 'dump_PrefabCollectable.txt')):
    if name.startswith('Silk Ration'):
        cls, kind, why = 'PERSONAL', 'consumable', 'silk ration consumable'
    elif name == 'Heart Piece':
        cls, kind, why = 'ONLY_ADD', 'mask shard', 'heartPieces/maxHealthBase; per source: world shard (SavedItemTrackerMarker objects) and boss gifts only-add; shop shards and wish-reward shards personal'
    else:
        cls, kind, why = 'ONLY_ADD', 'spool piece', 'silkSpoolParts/silkMax; per source like mask shards'
    items.append(('PlayerData (via collectable)', name, 'PrefabCollectable', kind, cls, why, sources(name)))

# ---- Tools
TOOL_TYPE = {'0': 'red (attack)', '1': 'blue', '2': 'yellow', '3': 'skill'}
for f in ('ToolItemBasic', 'ToolItemSkill', 'ToolItemStatesLiquid', 'ToolItemLerpStates', 'ToolItemToggleState'):
    for bundle, name, lines in blocks(os.path.join(DUMPS, f + '.txt')):
        if 'temptools' in bundle:
            items.append(('Tools', name, f, 'temporary', 'SESSION', 'temporary tool (pin gallery etc.)', ''))
            continue
        t = TOOL_TYPE.get(field(lines, 'type') or '', '?')
        src = sources(name)
        if f == 'ToolItemSkill':
            cls, why = 'ONLY_ADD', 'silk skill from a shrine/boss/story: ability; IsUnlocked only-add with its has* flag'
        elif (shop.get(name) or reward_of.get(name)) and (pickups.get(name) or drops.get(name)):
            cls, why = 'ONLY_ADD', ('MIXED sources: only-add ONLY through its world pickup/drop source (host took that pickup -> '
                                    'client gets the tool and the pickup counts taken); a host who bought it or got it as a wish reward copies nothing')
        elif shop.get(name):
            cls, why = 'PERSONAL', 'sold or crafted in a shop: one-time stock per player'
        elif reward_of.get(name):
            cls, why = 'PERSONAL', 'wish reward; user rule personal (see dangers: wish state HOST vs reward)'
        elif pickups.get(name) or drops.get(name):
            cls, why = 'ONLY_ADD', 'found in the world or dropped by a boss'
        else:
            cls, why = 'ONLY_ADD', 'no shop/wish source found: given by an NPC, boss or scene (FSM gives are not resolvable by name); default only-add, check'
        if f == 'ToolItemStatesLiquid':
            why += '; ToolLiquids refills stay personal'
        items.append(('Tools', name, f + ' ' + t, 'tool', cls, why + '. Per entry: IsUnlocked by class; AmountLeft, HasBeenSeen, HasBeenSelected, IsHidden personal', src))

# ---- Crests
CREST = {
    'Hunter': ('ONLY_ADD', 'starting crest'), 'Hunter_v2': ('ONLY_ADD', 'crest upgrade by the upgrader NPC after story progress'),
    'Hunter_v3': ('ONLY_ADD', 'crest upgrade by the upgrader NPC after story progress'),
    'Reaper': ('ONLY_ADD', 'crest memory (completedMemory_reaper)'), 'Wanderer': ('ONLY_ADD', 'crest memory'),
    'Warrior': ('ONLY_ADD', 'crest memory (beast)'), 'Witch': ('ONLY_ADD', 'crest memory / curse cure'),
    'Toolmaster': ('ONLY_ADD', 'crest memory'), 'Spell': ('ONLY_ADD', 'crest memory (shaman)'),
    'Cursed': ('UNSURE', 'hidden crest forced by the curse story event; follow gainedCurse/BelltownDoctorCuredCurse, never only-add on its own'),
    'Cloakless': ('SESSION', 'hidden crest used while the prison took the clothes; follows the prison capture state'),
}
for _, name, lines in blocks(os.path.join(DUMPS, 'ToolCrest.txt')):
    cls, why = CREST.get(name, ('UNSURE', 'unknown crest'))
    items.append(('ToolEquips', name, 'ToolCrest', 'crest', cls,
                  why + '. Per entry: IsUnlocked by class; Slots[].EquippedTool personal (loadout); Slots[].IsUnlocked = spent socket unlockers, personal (lockets are only-added as items per source); DisplayNewIndicator personal', ''))

# ---- Quests
for name, kind in quests:
    if kind != 'wish':
        cls, why = 'HOST', 'story quest: state follows the host'
    elif name.startswith('Courier Delivery'):
        cls, why = 'PERSONAL', 'repeatable courier delivery; cargo and timer are per player'
    else:
        cls, why = 'HOST', ('wish state follows the host (IsAccepted/IsCompleted/WasEverCompleted/CompletedCount); HasBeenSeen personal. '
                            'UNSURE: when the host completed it and the client did not, the client never gets the reward; suggested: also give the reward item if it is in an ONLY_ADD category')
    r = [k for k, v in reward_of.items() if name in v]
    items.append(('QuestCompletionData', name, kind, 'quest', cls, why, ('reward: ' + ','.join(r)) if r else ''))

with open(os.path.join(OUT, 'items.tsv'), 'w', encoding='utf-8', newline='') as out:
    w = csv.writer(out, delimiter='\t', lineterminator='\n')
    w.writerow(['collection', 'item', 'type', 'kind', 'class', 'reason', 'sources'])
    for row in items:
        w.writerow(row)

item_class = {row[1]: row[4] for row in items}
json.dump({'class': item_class, 'pickups': pickups, 'drops': drops}, open(os.path.join(OUT, 'item_class.json'), 'w'), indent=1)
print(collections.Counter((r[0], r[4]) for r in items))
print(len(items), 'items')
