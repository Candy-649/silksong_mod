"""Sorts every field of PlayerData into story and world state that a two-player save shares, and state that belongs to
each player, and writes the shared fields for SSMP.

Inputs are the outputs of FsmScan --pdflags (FSM actions and serialized components that write or read a field by
name, and the quest target assets that count fields), of ILPeek --pdwrites (C# methods that write or read a field)
and the list of PlayerData fields with their types. Each field gets one class:

  shared-world     world or story state both saves must agree on: flags that scene FSMs, levers, plates and receptacles
                   set outside dialogue and rewards, NPC moves and act changes that the story code sets
                   (GameManager.TimePasses, StartAct3, ...), paid mechanisms, and fields that quest targets count
  personal-reward  rewards, pickups, purchases, upgrades, abilities, maps, keys and what a player gave away: flags set
                   in the state of a give or pay action, by collectable, shop and pickup components or by the C# code of
                   items, and names like hasDash, HasWildsMap, Purchased*, Collected*
  personal-talk    dialogue flags (met, talked, heard, offered, queued) and flags of having seen a scene once, set in
                   dialogue states or named so
  personal-boss    records of beaten and met bosses, and arena and challenge records (BattleScene.setPDBoolOnEnd,
                   *_battleCompleted)
  personal-hero    hero state, UI, settings, stats, tutorial prompts, respawn and bench, exploration (visited*,
                   entered*), inventory panes and script flags, and fields nothing writes or reads
  unsure           the evidence disagrees or is missing; the reason says which

Rules are applied in order: OVERRIDES, then the type, then names that decide alone (HERO_NAMES, BOSS_RECORD), then the
quest targets, then the evidence of the writers (tags below), then names again for fields without clear writers. When
in doubt a field is not shared: a wrong shared flag can take a reward or a scene from the partner, while a wrong
personal flag only lets the worlds differ until the partner does the same thing.

Each shared int also gets a merge hint: "max" when every write adds a positive amount or sets a positive constant,
"no" when a write decrements, resets to 0 or sets a computed value.

Usage: py tools/coop_story_flags.py [reports dir] [coop-story-flags.json]
  reads <reports>/playerdata-fields.tsv, playerdata-writes.tsv, playerdata-reads.tsv, playerdata-quest-targets.tsv,
  playerdata-code-writes.tsv and playerdata-code-reads.tsv, and writes <reports>/coop-story-flags.tsv and the JSON
  (default <reports>/coop-story-flags.json) with only the shared-world fields: {"Bools": [], "Ints": [], "Strings": [], "Enums": [], "Records": []}, where Enums has the
  fields of all other types and Records has the booleans among Bools that record something done for good, like the
  win of an arena, which a check keeps if either save has them
"""
import collections
import csv
import json
import os
import re
import sys

reports = sys.argv[1] if len(sys.argv) > 1 else 'reports'
target = sys.argv[2] if len(sys.argv) > 2 else os.path.join(reports, 'coop-story-flags.json')
TAB = chr(9)

# Decisions made by hand after looking at the FSM states or IL, with why
OVERRIDES = {
    # The act change: StartAct3 runs on load while blackThreadWorld is set and act3_wokeUp is not, so each save must
    # run it itself. Sharing act3_wokeUp would skip the partner's StartAct3 and all it resets
    'blackThreadWorld': ('shared-world', 'act change: IsAct3IntroQueued = blackThreadWorld && !act3_wokeUp'),
    'act3_wokeUp': ('personal-hero', 'act change done in this save: sharing it skips the partner\'s StartAct3'),
    'act2Started': ('shared-world', 'act change'),
    # Scripted time passing, which GameManager.TimePasses reads to move characters; each save keeps its own clock
    'muchTimePassed': ('personal-hero', 'input of TimePasses for this save'),
    # Countdowns that TimePassesElsewhere lowers each time: shared, they would need a rule other than the larger value
    'bonetownPilgrimHornedCount': ('unsure', 'countdown that TimePassesElsewhere decrements, a merge cannot take max'),
    'bonetownPilgrimRoundCount': ('unsure', 'countdown that TimePassesElsewhere decrements, a merge cannot take max'),
    # Decided 2026-09-16 from the readers, under rules the user already set: a world purchase, a change of the
    # world and where a character is are shared. None of these were carried by a world object either, so without
    # this they reached the partner through nothing at all
    # Fast travel stations opened by paying a toll: world purchases, read by the station and its keeper
    'UnlockedSongTube': ('shared-world', 'fast travel station opened by a toll, a world purchase'),
    'UnlockedUnderTube': ('shared-world', 'fast travel station opened by a toll, a world purchase'),
    'UnlockedCityBellwayTube': ('shared-world', 'fast travel station opened by a toll, a world purchase'),
    'UnlockedHangTube': ('shared-world', 'fast travel station opened by a toll, a world purchase'),
    'UnlockedEnclaveTube': ('shared-world', 'fast travel station opened by a toll, a world purchase'),
    'UnlockedArboriumTube': ('shared-world', 'fast travel station opened by a toll, a world purchase'),
    # Shrines standing in the world, each read by about nine of its own components
    'bellShrineBoneForest': ('shared-world', 'shrine lit in the world, read by its own components'),
    'bellShrineWilds': ('shared-world', 'shrine lit in the world, read by its own components'),
    'bellShrineGreymoor': ('shared-world', 'shrine lit in the world, read by its own components'),
    'bellShrineShellwood': ('shared-world', 'shrine lit in the world, read by its own components'),
    'bellShrineBellhart': ('shared-world', 'shrine lit in the world, read by its own components'),
    'bellShrineEnclave': ('shared-world', 'shrine lit in the world, read by its own components'),
    # Ways that stay open once opened
    'slab_07_gateOpen': ('shared-world', 'gate that stays open, read by its own component'),
    'song18Shortcut': ('shared-world', 'shortcut that stays open, read by its own components'),
    'openedBeastmasterDen': ('shared-world', 'door that stays open, read by its own component'),
    'PilgrimsRestDoorBroken': ('shared-world', 'door broken for good, read by the entities and characters there'),
    'completedAbyssAscent': ('shared-world', 'a way of the world finished, read by the scene and a component'),
    'aspid_04b_wildlifeReturned': ('shared-world', 'the wildlife of a room came back, read by its component'),
    'collectorEggsHatched': ('shared-world', 'hatched in the world, read by ten of its own components'),
    # Named characters of the world that are gone for good: the world has to look the same to both players
    'pilbyKilled': ('shared-world', 'a named character of the world is gone'),
    'boneEastJailerKilled': ('shared-world', 'a named character of the world is gone'),
    'churchRhinoKilled': ('shared-world', 'a named character of the world is gone'),
    'greymoor05_killedJailer': ('shared-world', 'a named character of the world is gone'),
    'whiteCloverPos': ('shared-world', 'where a thing of the world stands, read by its component'),
    'LibrarianCollectionComplete': ('shared-world', 'a collection of the world finished, read by its component'),
    # Where characters are and what they will do, which the user decided is the same in both games
    'FastTravelNPCLocation': ('shared-world', 'where the fast travel keeper is, read by 26 components'),
    'shermaCitadelSpa_Visiting': ('shared-world', 'where a character is'),
    'garmondWillAidInForumBattle': ('shared-world', 'what a character will do, read by five components'),
    'garmondAidForumBattle': ('shared-world', 'what a character does, read by the character and the scene'),
    'gillyQueueMovingOn': ('shared-world', 'a character moved on'),
    'boneBottomFuneralComplete': ('shared-world', 'a scene of the world happened, read by nine components'),
    'BonebottomBellwayPilgrimState': ('shared-world', 'the state of a character at a station'),
    'BelltownDoctorCuredCurse': ('shared-world', 'a character was cured'),
    'CaravanLechSaved': ('shared-world', 'a character was saved, read by the character and the scene'),
    'MetTroupeHunterWild': ('shared-world', 'a character was met, read by nine of its own FSMs'),
    'fleaGames_juggling_played': ('shared-world', 'a character remembers the game was played'),
    'fleaGames_bouncing_played': ('shared-world', 'a character remembers the game was played'),
    'fleaGames_dodging_played': ('shared-world', 'a character remembers the game was played'),
}
# The wins of arenas that save them in the player data (BattleScene.setPDBoolOnEnd and setExtraPDBoolOnEnd). An arena
# that one player won counts as won for both, as the user decided on 2026-10-02. The arenas that count as bosses keep
# their records with each player (see coop_world_items.py), and so does slab_cloak_battle_completed, the fight after
# taking back the prison clothes: dying back into the prison clothes unsets it, and slab_16 only offers the clothes
# while it is unset, so sharing it could keep a player in the prison clothes for good
ARENA_WINS = {
    'ant21_InitBattleCompleted', 'aspid06_battleComplete', 'completedCog10_abyssBattle', 'dust03_battleCompleted',
    'silkFarmAbyssCoresCleared', 'silkFarmBattle1_complete', 'greymoor_04_battleCompleted',
    'completedLibraryAcolyteBattle', 'completedLibraryEntryBattle', 'under07_battleCompleted',
    'ant04_battleCompleted', 'savedPlinney', 'hang04Battle',
}
# What the live end of a shared arena sets besides its win, without which a partner who wasn't there loses what the
# end brings: in ant_04_mid the character who stays as the room's merchant only shows for a save that met her there,
# and two items of her shop need the second flag
ARENA_WIN_COMPANIONS = {'mapperMetInAnt04', 'SeenMapperHuntersNest'}
for _win in ARENA_WINS:
    OVERRIDES[_win] = ('shared-world', 'arena win, which one player wins for both')
for _companion in ARENA_WIN_COMPANIONS:
    OVERRIDES[_companion] = ('shared-world', 'set by the end of an arena whose win is shared, which a partner who '
                                             'was not there would lose otherwise')
# The crest chapels: the scene FSM "Chapel Door Control" passes the name to the chapel_door_control template, whose
# "Do Close" state sets it when this player enters the chapel's memory
for _crest in ('reaper', 'wanderer', 'beast', 'witch', 'toolmaster', 'shaman'):
    OVERRIDES['chapelClosed_' + _crest] = ('personal-reward', 'crest chapel of this player, set by chapel_door_control')

# Fields that are the state of the hero, the UI or the save, by name
HERO_NAMES = re.compile(
    r'^(disable\w+|respawn\w*|hazard\w*|tempRespawn\w*|nonLethal\w*|isInvincible|isInventoryOpen|isFirstGame|IsTeleporting|'
    r'atBench|travelling|health|maxHealth\w*|healthBlue|prevHealth|damaged(Blue|Purple)|silk|silkMax|silkParts|geo|'
    r'ShellShards|Temp(Geo|ShellShard)Store|currentInvPane|currentArea|show\w+UI|promptFocus|HasSeen\w+|SeenTool\w*Prompt|'
    r'SeenBindPrompt|seen(Journal|Materium)\w*Msg|seenFocusTablet|seenDreamNailPrompt|InvNailHasNew|\w+PaneHasNew|'
    r'mapKeyPref|profileID|RevisionBreak|version|date|LastSetFieldName|previousDarkness|HeroCorpse\w*|infiniteAirJump|'
    r'betaEnd|newDatTraitorLord|bossRushMode|permadeathMode|mapUpdateQueued|mapAllRooms|visited\w+|previouslyVisited\w+|'
    r'entered\w+|fullyEntered\w+|enteredTutorialFirstTime|LightningToolToggle|IsCurrentCrestTemp|WasIn\w+|'
    r'DidEnterPreviousMazeDoor|EnteredMazeRestScene|(Correct|Incorrect)MazeDoorsEntered|MaggotCharmHits|hasKilled|'
    r'openingCreditsPlayed|HasStoredMemoryState|SatAtBench\w*|queuedGodfinderIcon|bossStatueTargetLevel|'
    r'unlockedNewBossStatue|Maze\w+|PreviousMaze\w+|nextScene|dreamReturnScene|bossReturnEntryGate|'
    r'currentBossStatueCompletionKey|CurrentCrestID|PreviousCrestID|UnlockedFastTravelTeleport|gMap_\w+|'
    r'IsSilkSpoolBroken|cloakOdour_\w+|HeroDeath\w+|FisherWalker\w+|nailRange|beamDamage|silkSpecialLevel|attunementLevel|'
    r'CrowSummonsAppearedScene|gotPast\w+|\w+_entered|completedTutorial|\w*MapUpdated|\w+Cooldown)$'
)
# Records of beaten and met bosses, and of arenas and challenges
BOSS_RECORD = re.compile(
    r'^([Dd]efeated|[Ee]ncountered)|(Defeated|Encountered)$|_highscore$|[Bb]attle_?[Cc]omplete|battleCompleted|^completed\w*Battle$|'
    r'[Bb]attle_?(complete|completed|encountered)$|^hang04Battle$|Challenge(Max)?$|^completedLavaChallenge$|'
    r'^(skullKing(Defeated\w*|Killed)|spinnerDefeated|wardBoss(Encountered|Defeated)|dicePilgrimDefeated|'
    r'rockRollerDefeated_\w+|cog7_automaton_defeated|shellwoodSlabflyDefeated|roofCrab(Encountered|Defeated))$'
)
# Rewards by name: abilities, maps, keys, purchases, collected things, upgrades, items given away
REWARD_NAME = re.compile(
    r'^(has(?!ActivatedBellBench|Killed)[A-Z]\w*|Has\w+Map|HasSlabKey\w|HasMelody\w+|HasBoundCrestUpgrader|'
    r'[Pp]urchased\w+|[Cc]ollected\w+|Got\w+|got\w+|PickedUp\w+|took\w+|\w*Collected$|\w*MementoAwarded|'
    r'\w*MementoGiven|MerchantEnclave\w+|BoneBottomShellFrag\d|gainedCurse|nailUpgrades|attunement|ToolPouchUpgrades|'
    r'ToolKitUpgrades|silkRegenMax|heartPieces|silkSpoolParts|QuillState|UnlockedExtra\w+Slot|GourmandGiven\w+|'
    r'\w+GivenTool|\w+GaveRelic|GivenLibrarianRelic|\w+Given\w*|\w+Gave\w+|completedMemory_\w+|Constructed\w+|'
    r'Belltown(Furnishing\w+|HouseColour|HousePaintComplete)|CrawbellInstalled|HalfwayDrinksPurchased|'
    r'\w*Sold$|memoryOrbs_\w+|druidMossBerriesSold|PinGalleryWallet|dicePilgrimBank|HeroCorpseMoneyPool|'
    r'UnlockSilkFinalCutscene|MottledChildNewTool|SprintMasterExtraRaceWon|\w*Paid\w*|Crawbell\w+|completed\w*Sequence|'
    r'\w+Kept\w+)$'
)
# Dialogue flags by name, and flags of having seen something once
TALK_NAME = re.compile(
    r'(Convo|Dlg|Talked|TalkState|Talk$|Spoke|Spoken|Heard|heard|Offered|Intro|Mentioned|Asked|Explained|Queued|'
    r'Introduced|Remeet|Accepted|Declined|^promised|^learned|^[Mm]et[A-Z]|^[Mm]et$|Met$|Met[A-Z][a-z]+$|'
    r'Meet|Friendship|Greet|Greeter|^[Ss]een[A-Z]|^[Ss]aw[A-Z]|Seen(In)?[A-Z]\w*$|Seen$|Reacted|Hint|SingConvo|'
    r'Scared$|Spoke\w*|[Ee]ncountered_|Encounters)'
)
# Dialogue and seen-once names that win over world names, even when story code or a scene FSM sets them
STRONG_TALK = re.compile(
    r'(Convo|Dlg|Talked|Heard|heard|Offered|Queued|Spoken|Mentioned|Asked|Intro|^[Mm]et[A-Z]|Met$|Met[A-Z]|[Ss]een|'
    r'^[Ss]aw[A-Z]|[Ee]ncountered_|Encounters|CutscenePlayed)'
)
# World and story state by name: ways opened, characters that moved, things built or broken
WORLD_NAME = re.compile(
    r'([Oo]pened|[Uu]nlocked|[Bb]roke|[Bb]roken|[Ss]hortcut|[Gg]ate|[Ww]all|[Ll]ift|[Bb]ridge|[Oo]ne_?[Ww]ay|oneway|'
    r'[Pp]lat$|[Ff]loor|[Tt]rapdoor|[Dd]oor|[Cc]leared|Left$|Left[A-Z]|Gone$|Leave|Returned|Return|[Aa]ppeared$|'
    r'Appear|Arrives|Emerge|Invade|Invaded|Awake|[Ww]oken|Released|[Ss]aved|InSession|Installed|Fixed|Mended|'
    r'Populated|Ready$|Available$|CanLeave|CanReturn|CanVisit|CanStart|MovedTo|[a-z]In[A-Z][a-z]|^\w+In[A-Z]\w*$|'
    r'Active|Stationed|Destroyed|destroyed|Burned|CavedIn|ExplodeWall|explodeWall|Repair|Started$|Ended$|Level$|'
    r'Location\w*$|Pos$|IsHome|Away$|Crowd$|Group\w*$|Woke|wokeUp|Cut$|GrewLarge|Hatched|Grown|Growing|Emerged|'
    r'Possessed|Corpse\w*|Lurking|CleanedUp|InsideSitting|StoppedResting|Setup$|Open$|Rubbish|Ambush|ambush)'
)

# Actions in the state of a write that give the hero something
GIVE_ACTIONS = re.compile(
    r'^(SavedItemGet\w*|CollectableItemCollect\w*|AddCurrency\w*|SetToolUnlocked|GetQuestReward\w*|SpawnPowerUpGetMsg|'
    r'SpawnSkillGetMsg|CreateUIMsgGetItem|SetShopItemPurchased|UnlockCrest\w*|AddToolAmount\w*)$'
)
# Actions in the state of a write that take something from the hero: a purchase or an item handed over
PAY_ACTIONS = re.compile(r'^(TakeCurrency\w*|CollectableItemTake\w*|TakeTool\w*)$')
DIALOGUE_ACTIONS = re.compile(r'^(RunDialogue\w*|DialogueYesNo\w*|QuestYesNo\w*|QuestCompleteYesNo\w*|StartConversation\w*)$')
BOSS_ACTIONS = re.compile(r'^(RecordJournalKill\w*|DisplayBossTitle\w*|ShowBossTitle\w*|StartBattleScene\w*)$')
WORLD_ACTIONS = re.compile(r'^(SetPersistentBool|SetPersistentInt)$')
# Serialized components that write a field, by what they are
COMPONENT_CLASSES = {
    'world': {'Lever', 'Lever_tk2d', 'BridgeLever', 'PersistentPressurePlate', 'ItemReceptacle', 'BellBench', 'TempGate',
              'SceneAdditiveLoadConditional'},
    'arena': {'BattleScene', 'EnemyDeathEffects', 'ScuttlerControl'},
    'reward': {'ShopItem', 'CollectableItemBasic', 'CollectableItemPickup', 'PlayerDataCollectable',
               'PlayerDataBoolCollectable', 'QuestRewardHolder', 'CurrencyObjectBase', 'SilkGrubCocoon', 'MemoryOrbGroup',
               'CollectableItemGrower', 'DreamPlant'},
    'hero': {'InventoryItemBasic', 'InventoryItemConditional', 'InventoryItemComboButtonPrompt', 'InventoryItemSpool',
             'InventoryPane', 'RecordDoorEntry', 'CollectionGramaphone', 'ToolItemToggleState'},
    # SetPlayerDataBool fires when the hero enters a trigger or the scene loads: it may be a world change or just
    # having been somewhere, so the name decides
    'trigger': {'SetPlayerDataBool'},
}
# C# methods that set up, reset or cheat the whole player data, which say nothing about a field
IGNORED_CODE = re.compile(
    r'^(PlayerData::(\.ctor|\.cctor|SetupNewPlayerData|SetupExistingPlayerData|CreateNewSingleton|ResetNonSerializableFields|'
    r'AddEditorOverrides|ActivateTestingCheats|AddGGPlayerDataOverrides|OnDeserialized|GetAllPowerups|ClearOptimisers|'
    r'set_instance|SetBool|SetInt|SetFloat|SetString|SetVector3|IncrementInt|DecrementInt|IntAdd)|CheatManager|'
    r'SaveDataUpgradeHandler|DebugMenu|\w*Cheat|ToolItemList::UnlockAll|ToolItemManager::UnlockAllCrests|'
    r'GameManager::(SetPlayerData\w+|IncrementPlayerDataInt|DecrementPlayerDataInt|IntAdd|StartNewGame)|'
    r'TeamCherry\.SharedUtils\.VariableExtensions)'
)
# C# methods that change the story: characters move and the world changes when time passes or the act changes
STORY_CODE = re.compile(
    r'^(GameManager::(TimePasses|TimePassesElsewhere|MuchTimePasses|StartAct3|StartBlackThreadWorld|SethTravelCheck|'
    r'MapperLeavePreviousLocations)|PlayerData::MapperLeaveAll|'
    r'GameManager/<TimePassesElsewhere>\w*|NPCEncounterStateController::\w+)'
)
REWARD_CODE = re.compile(
    r'^(ToolItem\w*|Collectable\w*|ShopItem|PlayerDataCollectable|PlayerDataBoolCollectable|QuestRewardHolder|SkillGetMsg|'
    r'PowerUpGetMsg|SilkGrubCocoon|CaravanTroupeHunter|RelicBoardOwner|DeliveryQuestItem|SimpleQuestsShopOwner|SubQuest|'
    r'QuestType|TakeCrawbellCurrency|CurrencyManager|GeoControl|ShellShard|MemoryOrbGroup|CurrencyObjectBase|'
    r'HeroItemsState|ToolCrest\w*|UIMsgProxy|PlayerData::(AddGeo|TakeGeo|AddShards|TakeShards))[:/]'
)
BOSS_CODE = re.compile(
    r'^(BossStatue|BossSequence\w*|BossChallengeUI|GodfinderIcon|EnemyDeathEffects|ScuttlerControl|BattleScene)[:/]'
)
HERO_CODE = re.compile(
    r'^(HeroController|MazeController|MazeMistZone|TransitionPoint|SetDeathRespawn\w*|RestBenchHelper|AreaTitleController|'
    r'RecordDoorEntry|CrossSceneWalker|HeroSlabCapture|GameMap|InputHandler|InventoryPane\w*|OpeningGameplayCredits|'
    r'PlayerData::(AddHealth|TakeHealth|AddSilk|TakeSilk|AddToMaxHealth|MaxHealth|ResetCutsceneBools|ResetTempRespawn|'
    r'SetBenchRespawn|SetHazardRespawn|CountGameCompletion|UpdateDate|OnBeforeSave|CaptureToolAmountsOverride|'
    r'ClearToolAmountsOverride|EquipCharm|UnequipCharm|CalculateNotchesUsed|ReduceOdours)|GameManager::(?!TimePasses|'
    r'TimePassesElsewhere|MuchTimePasses|StartAct3|StartBlackThreadWorld|SethTravelCheck|MapperLeavePreviousLocations))'
)
# Fields of other types: the JSON only holds bools, ints and strings, so these are sorted for the report only
OTHER_TYPES = {
    'personal-reward': re.compile(
        r'^(Collectables|Relics|MementosDeposited|MateriumCollected|Tools|ToolEquips|ToolLiquids|ExtraToolEquips|'
        r'EnemyJournalKillData|CrawbellCurrency\w*|SteelQuestSpots|toolAmountsOverride|mossBerryValueList|'
        r'GrubFarmerMimicValueList|memoryOrbs_\w+|BelltownHouseColour)$'),
    'personal-boss': re.compile(r'^(currentBossSequence|unlockedBossScenes|CompletedEndings|LastCompletedEnding|'
                                r'greyWarriorDeathX|laceCorpse\w+)$'),
    'shared-world': re.compile(r'^(SethNpcLocation|GreenPrinceLocation|CaravanTroupeLocation|BelltownHouseState|'
                               r'EnclaveState\w+)$'),
}

# A write of an int that only goes up
INCREMENT = re.compile(r'^\+\d+$')


def rows(name):
    path = os.path.join(reports, name)
    with open(path, encoding='utf-8', newline='') as f:
        return list(csv.DictReader(f, delimiter=TAB, quoting=csv.QUOTE_NONE))


fields = []
with open(os.path.join(reports, 'playerdata-fields.tsv'), encoding='utf-8') as f:
    for line in f:
        parts = line.rstrip('\n').split(TAB)
        if len(parts) >= 2:
            fields.append((parts[1], parts[0]))

writes = collections.defaultdict(list)
for row in rows('playerdata-writes.tsv'):
    if row['source'] == 'component' and row['action'] != 'write':
        continue
    writes[row['field']].append(row)
reads = {row['field']: row for row in rows('playerdata-reads.tsv')}
code_writes = collections.defaultdict(list)
for row in rows('playerdata-code-writes.tsv'):
    if not IGNORED_CODE.match(row['method']):
        code_writes[row['field']].append(row)
code_reads = {row['field']: row for row in rows('playerdata-code-reads.tsv')}
quest_targets = collections.defaultdict(list)
quest_templates = []
for row in rows('playerdata-quest-targets.tsv'):
    if row['kind'] == 'template':
        quest_templates.append((row['value'], row['asset']))
    else:
        quest_targets[row['value']].append(row['asset'])


def split(text):
    return [part for part in text.split(',') if part]


def fsm_tag(row):
    """What a write in an FSM state looks like: give, pay, world, boss, hero, talk or scene."""
    state = split(row['state_actions'])
    if any(GIVE_ACTIONS.match(a) for a in state):
        return 'give'
    if any(PAY_ACTIONS.match(a) for a in state):
        return 'mechanism' if row['interact_class'] == 'mechanism' else 'pay'
    if row['interact_class'] == 'mechanism' and row['in_talk'] == '1':
        return 'mechanism'
    if row['category'] in ('hero', 'ui'):
        return 'hero'
    if row['category'] in ('entity', 'entity-child', 'enemy-unregistered', 'corpse') or row['in_arena'] == '1' or \
            any(BOSS_ACTIONS.match(a) for a in state):
        return 'boss'
    dialogue = any(DIALOGUE_ACTIONS.match(a) for a in state)
    if dialogue or (row['in_talk'] == '1' and row['interact_class'] in ('npc', 'talk', 'wish', 'give')):
        return 'talk'
    if any(WORLD_ACTIONS.match(a) for a in state):
        return 'world'
    return 'scene'


def component_tag(row):
    for tag, classes in COMPONENT_CLASSES.items():
        if row['fsm'] in classes:
            return tag
    return 'component'


def code_tag(row):
    method = row['method']
    if STORY_CODE.match(method):
        return 'story'
    if REWARD_CODE.match(method):
        return 'reward-code'
    if BOSS_CODE.match(method):
        return 'boss-code'
    if HERO_CODE.match(method):
        return 'hero-code'
    return 'code'


def int_merge(name):
    """Whether two saves could merge an int by taking the larger value."""
    values = [row['value'] for row in writes.get(name, [])]
    code = [row for row in code_writes.get(name, [])]
    if not values and not code:
        return ''
    for value in values:
        if value.startswith('-') or value in ('0', '+0') or value.startswith('var') or value in ('', '<missing>'):
            return 'no: writes ' + (value or 'a computed value')
        if not INCREMENT.match(value) and not re.match(r'^\d+$', value):
            return 'no: writes ' + value
    for row in code:
        if row['value'] in ('0/false', '-1') or row['value'] == '':
            return 'no: code writes ' + (row['value'] or 'a computed value') + ' in ' + row['method']
    if values and all(INCREMENT.match(v) for v in values):
        return 'max: only increments'
    return 'max?: sets constants ' + ','.join(sorted(set(values) | {row['value'] for row in code}))


def classify(name, kind):
    """The class of a field, the rule that decided it and the tags of its writers."""
    tags = collections.Counter()
    for row in writes.get(name, []):
        tags[fsm_tag(row) if row['source'] != 'component' else component_tag(row)] += 1
    for row in code_writes.get(name, []):
        tags[code_tag(row)] += 1

    if name in OVERRIDES:
        return OVERRIDES[name] + (tags,)
    if kind not in ('Boolean', 'Int32', 'String'):
        for cls, pattern in OTHER_TYPES.items():
            if pattern.match(name):
                return cls, 'type ' + kind + ', name', tags
        if HERO_NAMES.match(name) or re.match(r'^(scenes\w+|mapBoolList|placedMarkers|mapZone|extraRestZone|'
                                              r'environmentType|playTime|completionPercentage|PreMemoryState|'
                                              r'StoryEvents|\w+PlayingInfo|HeroCorpse\w+)$', name):
            return 'personal-hero', 'type ' + kind + ', name', tags
        return 'unsure', 'type ' + kind + ', no rule', tags
    if HERO_NAMES.match(name):
        return 'personal-hero', 'name', tags
    if BOSS_RECORD.search(name):
        return 'personal-boss', 'name', tags

    quest = quest_targets.get(name) or [asset for prefix, asset in quest_templates if name.startswith(prefix)]
    world = tags['world'] + tags['scene'] + tags['mechanism'] + tags['story']
    talk = tags['talk']
    reward = tags['give'] + tags['pay'] + tags['reward'] + tags['reward-code']
    boss = tags['boss'] + tags['boss-code']
    hero = tags['hero'] + tags['hero-code']
    trigger = tags['trigger']
    evidence = ' '.join(f'{tag}:{count}' for tag, count in sorted(tags.items()))
    if quest:
        if reward or talk:
            return 'unsure', f'quest target {quest[0]}, but written by {evidence}', tags
        return 'shared-world', 'quest target ' + quest[0], tags

    if tags['arena']:
        return 'personal-boss', 'arena or enemy component record: ' + evidence, tags
    if tags['give'] or tags['reward'] or tags['reward-code']:
        if (tags['world'] or tags['mechanism']) and not tags['give']:
            return 'unsure', 'reward component or code, and world writers: ' + evidence, tags
        return 'personal-reward', 'written with a reward: ' + evidence, tags
    if tags['pay']:
        return 'personal-reward', 'written with a payment in dialogue: ' + evidence, tags
    # Scene FSMs and story code also write reward names (a counter reset, a cutscene unlock), so only levers, plates,
    # receptacles, saved world objects and paid mechanisms keep a reward name from deciding
    if REWARD_NAME.match(name) and not tags['mechanism'] and not tags['world']:
        return 'personal-reward', 'name' + (', writers ' + evidence if evidence else ''), tags
    if tags['boss'] and not world and not talk:
        return 'personal-boss', 'written by a boss, enemy or arena: ' + evidence, tags
    if hero and not world and not talk and not boss:
        return 'personal-hero', 'written by hero or UI: ' + evidence, tags

    talk_name = bool(TALK_NAME.search(name))
    world_name = bool(WORLD_NAME.search(name))
    if talk and not world and not trigger:
        return 'personal-talk', 'written in dialogue: ' + evidence, tags
    if trigger and not world and not talk:
        if talk_name:
            return 'personal-talk', 'trigger, seen or met once: ' + evidence, tags
        if world_name:
            return 'shared-world', 'trigger, world name: ' + evidence, tags
        return 'unsure', 'set by entering a trigger: ' + evidence, tags
    if world:
        if STRONG_TALK.search(name):
            return 'personal-talk', 'dialogue or seen-once name, written outside dialogue: ' + evidence, tags
        if talk or trigger:
            if talk_name and not world_name:
                return 'personal-talk', 'dialogue name, also world writers: ' + evidence, tags
            if world_name and not talk_name:
                return 'shared-world', 'world name, also dialogue writers: ' + evidence, tags
            return 'unsure', 'written in dialogue and outside it: ' + evidence, tags
        if talk_name and not world_name:
            return 'personal-talk', 'dialogue name, written outside dialogue: ' + evidence, tags
        if boss and not tags['story']:
            return 'unsure', 'written by the world and by a boss or arena: ' + evidence, tags
        return 'shared-world', 'written by the world outside dialogue: ' + evidence, tags
    if boss:
        return 'unsure', 'written by a boss or enemy and in dialogue: ' + evidence, tags

    # No writer that decides: the name and readers decide, never sharing without a writer
    read = reads.get(name)
    code_read = code_reads.get(name)
    if talk_name:
        return 'personal-talk', 'name, no deciding writer' + (': ' + evidence if evidence else ''), tags
    if not evidence and not read and not code_read:
        return 'personal-hero', 'nothing writes or reads it', tags
    if world_name:
        return 'unsure', 'world name but no writer found' + (': ' + evidence if evidence else ''), tags
    return 'unsure', 'no deciding writer' + (': ' + evidence if evidence else ''), tags


def example_writers(name):
    examples = []
    for row in writes.get(name, [])[:3]:
        if row['source'] == 'component':
            examples.append(f"{row['place']}:{row['object']}:{row['fsm']}.{row['state']}")
        else:
            examples.append(f"{row['place']}:{row['object']}:{row['fsm']}:{row['state']}={row['value']}")
    for row in code_writes.get(name, [])[:2]:
        examples.append(f"{row['method']}={row['value']}")
    return ' | '.join(examples).replace(TAB, ' ')


counts = collections.Counter()
by_type = collections.defaultdict(collections.Counter)
shared = {'Bools': [], 'Ints': [], 'Strings': [], 'Enums': []}
with open(os.path.join(reports, 'coop-story-flags.tsv'), 'w', encoding='utf-8', newline='') as out:
    writer = csv.writer(out, delimiter=TAB, lineterminator='\n', quoting=csv.QUOTE_NONE, escapechar='\\')
    writer.writerow(['name', 'type', 'class', 'reason', 'int_merge', 'fsm_writes', 'component_writes', 'code_writes',
                     'fsm_reads', 'component_reads', 'code_reads', 'writer_tags', 'example_writers', 'reader_categories'])
    for name, kind in fields:
        cls, reason, tags = classify(name, kind)
        counts[cls] += 1
        by_type[kind if kind in ('Boolean', 'Int32', 'String') else 'other'][cls] += 1
        if cls == 'shared-world':
            shared[{'Boolean': 'Bools', 'Int32': 'Ints', 'String': 'Strings'}.get(kind, 'Enums')].append(name)
        read = reads.get(name, {})
        writer.writerow([
            name, kind, cls, reason.replace(TAB, ' '), int_merge(name) if kind == 'Int32' else '',
            sum(1 for r in writes.get(name, []) if r['source'] != 'component'),
            sum(1 for r in writes.get(name, []) if r['source'] == 'component'),
            len(code_writes.get(name, [])), read.get('fsm_reads', 0), read.get('component_reads', 0),
            code_reads.get(name, {}).get('methods', 0),
            ','.join(f'{t}:{c}' for t, c in sorted(tags.items())), example_writers(name),
            read.get('categories', ''),
        ])

# Records of something done for good, which a check keeps if either save has them, instead of taking the save that was
# played longer
shared['Records'] = [name for name in shared['Bools'] if name in ARENA_WINS | ARENA_WIN_COMPANIONS]
with open(target, 'w', encoding='utf-8', newline='\n') as out:
    json.dump(shared, out, indent=1, ensure_ascii=False)
    out.write('\n')

print(dict(counts))
for kind, classes in sorted(by_type.items()):
    print(f'  {kind}: {dict(classes)}')
print(f"shared: {len(shared['Bools'])} bools, {len(shared['Ints'])} ints, {len(shared['Strings'])} strings, "
      f"{len(shared['Enums'])} of other types: {target}")
print(f"report: {os.path.join(reports, 'coop-story-flags.tsv')}")
