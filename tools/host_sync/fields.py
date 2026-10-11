"""Builds classification.tsv: every PlayerData field with type, new class (HOST / ONLY_ADD / PERSONAL / SESSION / UNSURE),
merge rule and reason. Starts from the old classes of reports/coop-story-flags.tsv and applies the host-aligned rules."""
import collections, csv, os, re

R = r'D:\programming\silksong_mod\reports'
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(R, 'host-sync')

rows = list(csv.DictReader(open(os.path.join(R, 'coop-story-flags.tsv'), encoding='utf-8'), delimiter='\t'))

# explicit decisions: name -> (class, merge, reason)
X = {}


def put(names, cls, merge, reason):
    for n in names.split():
        X[n] = (cls, merge, reason)


# ---------- SESSION: live hero/session state, never copied, the client's own value is kept or reset by the game
put('health healthBlue silk prevHealth damagedBlue damagedPurple silkParts atBench hazardRespawnLocation hazardRespawnFacing '
    'tempRespawnScene tempRespawnMarker tempRespawnType nonLethalRespawnScene nonLethalRespawnMarker nonLethalRespawnType '
    'isInvincible infiniteAirJump disablePause disableSaveQuit disableInventory isInventoryOpen disableSilkAbilities '
    'travelling nextScene IsTeleporting currentInvPane LastSetFieldName TempGeoStore TempShellShardStore toolAmountsOverride '
    'environmentType previousDarkness currentArea MazeEntranceScene MazeEntranceDoor MazeEntranceInitialScene '
    'MazeEntranceInitialDoor PreviousMazeTargetDoor PreviousMazeScene PreviousMazeDoor CorrectMazeDoorsEntered '
    'IncorrectMazeDoorsEntered EnteredMazeRestScene DidEnterPreviousMazeDoor WasInPinChallenge WasInSceneRace '
    'PreMemoryState HasStoredMemoryState dreamReturnScene mapUpdateQueued FisherWalkerTimer FisherWalkerDirection '
    'FisherWalkerIdleTimeLeft CrowSummonsAppearedScene bossReturnEntryGate currentBossStatueCompletionKey '
    'bossStatueTargetLevel bossRushMode queuedGodfinderIcon unlockedNewBossStatue currentBossSequence '
    'version RevisionBreak date profileID isFirstGame betaEnd',
    'SESSION', 'keep client', 'hero session / transient state; the client keeps its own value (load path resets most of it)')
put('IsSilkSpoolBroken', 'UNSURE', 'keep client', 'HUD/hero state also written by story scenes; suggested SESSION (keep client)')

# ---------- HOST: respawn point (client spawns at the host's bench)
put('respawnScene respawnMarkerName respawnType mapZone extraRestZone', 'HOST', 'take host',
    'respawn point: the client loads at the host\'s bench (user rule)')
put('UnlockedFastTravelTeleport', 'HOST', 'take host', 'set by a boss scene, read by 22 scenes: world/story state')

# ---------- PERSONAL hero state
put('geo ShellShards HeroCorpseMoneyPool', 'PERSONAL', 'keep client', 'money (user rule)')
put('HeroCorpseScene HeroDeathScenePos HeroDeathSceneSize HeroCorpseMarkerGuid HeroCorpseType', 'PERSONAL', 'keep client',
    'the client\'s own death cocoon (holds its money)')
put('playTime completionPercentage openingCreditsPlayed CompletedEndings LastCompletedEnding hasKilled enteredTutorialFirstTime '
    'completedTutorial entered_Tut01b MaggotCharmHits cloakOdour_slabFly garmondEncounterCooldown songChevalierEncounterCooldown '
    'BelltownHermitConvoCooldown muchTimePassed StoryEvents mapKeyPref CurrentCrestID PreviousCrestID IsCurrentCrestTemp '
    'LightningToolToggle BelltownHousePlayingInfo LibrarianPlayingInfo',
    'PERSONAL', 'keep client', 'per-player record, preference, loadout or cooldown')
put('permadeathMode', 'UNSURE', 'refuse', 'game mode of the save; suggested: refuse entry when host and client modes differ, never copy')

# ---------- ONLY_ADD upgrades (counts derived per source, see dangers.md)
put('maxHealthBase maxHealth', 'ONLY_ADD', 'per-source recompute',
    'mask count: = 5 + world/boss shards (only-add per source) + personal shop/wish shards; recompute from sources, never max()')
put('heartPieces', 'ONLY_ADD', 'per-source recompute', 'partial mask shards (0-3); recomputed together with maxHealthBase')
put('silkMax silkSpoolParts', 'ONLY_ADD', 'per-source recompute', 'spool size / partial spool pieces; per source like masks')
put('silkRegenMax', 'ONLY_ADD', 'per-source recompute', 'silk hearts from bosses/memories; count per source')
put('attunement', 'ONLY_ADD', 'per-source recompute', 'attunement upgrades from the world; per source')
put('ToolPouchUpgrades ToolKitUpgrades', 'ONLY_ADD', 'per-source recompute',
    'pouch/kit upgrades: world pickups only-add; shop and wish-reward ones personal; count per source')
put('nailUpgrades', 'PERSONAL', 'keep client', 'needle upgrades are a paid service (rosaries + material) of an NPC; treated like a shop purchase')
put('nailRange beamDamage silkSpecialLevel attunementLevel', 'PERSONAL', 'keep client', 'no writer or reader found (leftover); never copied')
put('hasDash hasWalljump hasDoubleJump hasSuperJump hasBrolly hasHarpoonDash hasChargeSlash hasNeedolin hasNeedolinMemoryPowerup '
    'hasSilkSpecial hasNeedleThrow hasThreadSphere hasParry hasSilkCharge hasSilkBomb hasSilkBossNeedle hasJournal hasGodfinder',
    'ONLY_ADD', 'OR', 'ability / skill (user rule only-add)')
put('completedSuperJumpSequence', 'ONLY_ADD', 'OR', 'goes with the ability it grants')
put('HasMelodyArchitect HasMelodyLibrarian HasMelodyConductor', 'ONLY_ADD', 'OR', 'story items for the main quest')
put('completedMemory_reaper completedMemory_wanderer completedMemory_beast completedMemory_witch completedMemory_toolmaster '
    'completedMemory_shaman chapelClosed_reaper chapelClosed_wanderer chapelClosed_beast chapelClosed_witch '
    'chapelClosed_toolmaster chapelClosed_shaman', 'ONLY_ADD', 'OR', 'crest memory done / chapel closed: goes with the crest it unlocks')
put('HasBoundCrestUpgrader', 'ONLY_ADD', 'OR', 'crest upgrade progress (story)')
put('UnlockedExtraBlueSlot UnlockedExtraYellowSlot', 'ONLY_ADD', 'OR', 'extra crest slots (user rule crest slots); writer not found')
put('ExtraToolEquips', 'UNSURE', 'union of slot ids',
    'FloatingCrestSlotsData (extra floating slots + what is equipped in them); StartAct3 writes it. Suggested: union of slot unlocks, equips personal')
put('HasSlabKeyA HasSlabKeyB HasSlabKeyC CollectedDockDemoKey collectedWardKey collectedWardBossKey CollectedDustCageKey BallowGivenKey',
    'ONLY_ADD', 'OR', 'key collected flag (world/NPC key); goes with the key item')
put('PickedUpCrowMemento CollectedMementoGrey CollectedMementoSprintmaster nuuMementoAwarded FleaGamesMementoGiven '
    'CollectedHeartFlower CollectedHeartCoral CollectedHeartHunter CollectedHeartClover',
    'ONLY_ADD', 'OR', 'memento / heart collected flag; goes with the item (mementos only-add)')
put('ConstructedMaterium ConstructedFarsight', 'ONLY_ADD', 'OR', 'plot item built at the desk; if the host built it the client must not keep a usable ingredient (see dangers)')
put('CrawbellInstalled', 'ONLY_ADD', 'OR', 'story item installed in the home')
put('pilgrimQuestSpoolCollected tookGreymoor17Spool', 'ONLY_ADD', 'per-source', 'spool piece world source flag; goes with silkSpoolParts')
put('memoryOrbs_Clover_02c_A memoryOrbs_Clover_03_B memoryOrbs_Clover_06_A memoryOrbs_Clover_11 memoryOrbs_Clover_16_B '
    'memoryOrbs_Clover_16_C memoryOrbs_Clover_21', 'ONLY_ADD', 'OR', 'world memory orb gathered (story progress in that area)')
put('memoryOrbs_Clover_18_A memoryOrbs_Clover_18_B memoryOrbs_Clover_18_C memoryOrbs_Clover_18_D memoryOrbs_Clover_18_E '
    'memoryOrbs_Clover_19', 'ONLY_ADD', 'bitwise OR', 'bitmask of gathered memory orbs (UInt64)')
put('SteelQuestSpots', 'ONLY_ADD', 'OR per spot (IsSeen)', 'wish progress that is not taken: counts from either player (existing rule)')
put('unlockedBossScenes', 'ONLY_ADD', 'union', 'only grows; already unioned live')
put('SprintMasterExtraRaceWon', 'ONLY_ADD', 'OR', 'race won (one win = both win rule)')
put('UnlockSilkFinalCutscene', 'HOST', 'take host', 'story step set by a boss death sequence')
put('CompletedRedMemory', 'HOST', 'take host', 'story memory finished (scripted)')

# ---------- maps / quill / pins / markers
FOUND_MAPS = 'HasSlabMap HasCitadelUnderstoreMap HasCloverMap HasAbyssMap HasHangMap HasSongGateMap HasHallsMap HasWardMap HasCogMap HasLibraryMap HasCradleMap HasArboriumMap HasAqueductMap HasWeavehomeMap'
put(FOUND_MAPS, 'ONLY_ADD', 'OR', 'UNSURE default only-add: map found in the world / story (no shop sells it); found maps count as important world finds')
put('HasMossGrottoMap HasWildsMap HasBoneforestMap HasDocksMap HasGreymoorMap HasBellhartMap HasShellwoodMap HasCrawlMap '
    'HasHuntersNestMap HasJudgeStepsMap HasDustpensMap HasPeakMap HasCoralMap HasSwampMap', 'PERSONAL', 'keep client',
    'map sold by the map seller: shop purchase, one-time stock per player')
put('hasPinBench hasPinCocoon hasPinShop hasPinSpa hasPinStag hasPinTube hasPinFleaMarrowlands hasPinFleaMidlands '
    'hasPinFleaBlastedlands hasPinFleaCitadel hasPinFleaPeaklands hasPinFleaMucklands hasMarker hasMarker_a hasMarker_b '
    'hasMarker_c hasMarker_d hasMarker_e', 'PERSONAL', 'keep client', 'map pins / markers bought from the map seller')
put('hasQuill', 'PERSONAL', 'keep client', 'bought from the map seller (also a desk scene); shop purchase')
put('QuillState', 'ONLY_ADD', 'max', 'quill colour upgrades from a boss reward (world); max is safe because states only go up')
put('scenesVisited scenesMapped scenesEncounteredBench scenesEncounteredCocoon mapBoolList placedMarkers mapAllRooms '
    'HasSeenMapUpdated HasSeenMapMarkerUpdated act3MapUpdated', 'PERSONAL', 'keep client', 'map knowledge / markers (map state personal)')

# ---------- shop / purchase / reward personal
put('BelltownFurnishingDesk BelltownFurnishingSpa BelltownFurnishingFairyLights BelltownFurnishingGramaphone '
    'BelltownFurnishingSpaAvailable BelltownHousePaintComplete', 'ONLY_ADD', 'OR',
    'home furnishing: a world purchase for the shared home (earlier rule: world purchases shared); only-add so a client never loses one it paid for')
put('BelltownHouseColour', 'UNSURE', 'take host if host painted', 'paint colour bought for the home; suggested: host value when the host painted, else keep client')
put('CrawbellCurrency CrawbellCurrencyCaps CrawbellTimer CrawbellCrawsInside GrowstoneState GrowstoneTimer grubFarmerTimer '
    'farmer_grubGrowing_1 farmer_grubGrown_1 farmer_grubGrowing_2 farmer_grubGrown_2 farmer_grubGrowing_3 farmer_grubGrown_3 '
    'GrubFarmerSilkGrubsSold GrubFarmerMimicValueList mossBerryValueList druidMossBerriesSold dicePilgrimBank '
    'HalfwayDrinksPurchased HalfwayNectarPaid pinGalleriesCompleted PinGalleryWallet tookRestroomRosaries '
    'mosstownAspidBerryCollected bonegraveAspidBerryCollected bonetownAspidBerryCollected '
    'CollectedToolMetal CollectedCommonSpine GotGourmandReward ForgeDaughterSpentToolMetal MementosDeposited MateriumCollected '
    'BelltownRelicDealerGaveRelic GivenLibrarianRelic MottledChildGivenTool MottledChildNewTool WoodWitchGaveMandrake '
    'mortKeptWeightedAnklet BelltownCouriersBrokenDlgQueued CaravanSpiderTargetScene CaravanSpiderTravelDirection '
    'CaravanSpiderPaidExtraBellhart BoneBottomShellFrag1 fleaGames_juggling_highscore fleaGames_bouncing_highscore '
    'fleaGames_dodging_highscore BelltownCouriersGenericQuests FleasCollectedTargetOrder',
    'PERSONAL', 'keep client', 'personal economy: money, materials, farm clocks, wish items/rewards, gifts, displays, high scores')
put('gotPickledRoachEgg broodMotherEyeCollected', 'UNSURE', 'OR when the host boss record is set', 'wish item dropped once by a boss/arena corpse; if the boss is HOST-dead the client can never get it. Suggested: ONLY_ADD (item + flag) when the host collected it and the client has the boss record only through the host')
put('Collectables', 'ONLY_ADD', 'per entry (items.tsv)',
    'CollectableItemsData = list {Name, Data{Amount, IsSeenMask, AmountWhileHidden}}; per item by items.tsv: ONLY_ADD items get +N by source (Amount), personal items untouched, IsSeenMask personal')
put('Relics', 'PERSONAL', 'per entry (items.tsv)',
    'CollectableRelicsData = list {Name, Data{IsCollected, IsDeposited, HasSeenInRelicBoard}}; default PERSONAL (relics are money), see UNSURE')
put('Tools', 'ONLY_ADD', 'per entry (items.tsv)',
    'ToolItemsData = list {Name, Data{IsUnlocked, IsHidden, HasBeenSeen, HasBeenSelected, AmountLeft}}; IsUnlocked only-add for ONLY_ADD tools (world/boss/skill), rest personal')
put('ToolEquips', 'ONLY_ADD', 'per entry (items.tsv)',
    'ToolCrestsData = list {Name(crest), Data{IsUnlocked, Slots[{EquippedTool, IsUnlocked}], DisplayNewIndicator}}; crest IsUnlocked only-add; equips and slot unlocks personal')
put('ToolLiquids', 'PERSONAL', 'keep client', 'ToolItemLiquidsData {Name, Data{RefillsLeft, SeenEmptyState, UsedExtra}}: consumable refills')
put('EnemyJournalKillData', 'UNSURE', 'per entry max(Kills), OR(HasBeenSeen)',
    'list {Name, Record{Kills, HasBeenSeen}}; suggested ONLY_ADD max per enemy: a boss the host beat can never be recorded by the client otherwise (journal completion, thread-core wish)')
put('QuestCompletionData', 'HOST', 'per entry (items.tsv)',
    'list {Name, Data{HasBeenSeen, IsAccepted, CompletedCount, IsCompleted, WasEverCompleted}}; story quests and wishes take the host\'s state, courier deliveries personal; see UNSURE for rewards')
put('QuestRumourData', 'HOST', 'take host', 'list of wish rumours offered by boards/NPCs; follows the quest state (wish sync already owns it)')

# ---------- story / HOST overrides of old personal classes
put('slab_05_gateOpen slab_17_openedGateRight slab_17_openedGateLeft', 'HOST', 'take host', 'gate opened by a key lock (mechanism), was mis-tagged as dialogue')
put('soulSnareReady', 'HOST', 'take host', 'story step granted in a wish dialogue (quest state)')
put('MushroomQuestFound1 MushroomQuestFound2 MushroomQuestFound3 MushroomQuestFound4 MushroomQuestFound5 MushroomQuestFound6 MushroomQuestFound7',
    'ONLY_ADD', 'OR', 'progress of a wish that is not taken: counts from either player (existing wish rule)')
put('GourmandGivenStew GourmandGivenNectar GourmandGivenEgg GourmandGivenMeat GourmandGivenCoral', 'HOST', 'take host',
    'items handed in for a wish = wish state; the items themselves stay personal (both pay in co-op)')
put('shakraAidForumBattle songChevalierQuestReady', 'HOST', 'take host', 'NPC/story state')
put('gainedCurse bindCutscenePlayed', 'UNSURE', 'take host + fix crest',
    'curse story event changes the hero\'s crest; suggested HOST, and if the result says cursed-and-not-cured set the curse crest, else make sure the client is not left on it')
put('SatAtBenchAfterAbyssEscape fullyEnteredVerdania gotPastDockSpearThrower enteredCoral_10 enteredSong_19 enteredSong_01 '
    'enteredSong_02 enteredSong_13 enteredSong_17 visitedBoneEast14b greymoor_10_entered visitedHalfway enteredGreymoor05 '
    'previouslyVisitedGreymoor_05 visitedShellwood_16 visitedShadow03 visitedCoralBellshrine visitedUpperSlab enteredHang_08',
    'HOST', 'take host', 'room-entry trigger that starts a scripted scene or world change once (scripted scenes follow the host)')
put('act3_wokeUp', 'HOST', 'special', 'act change; must be copied together with blackThreadWorld and the StartAct3 handling in dangers.md')
put('blackThreadWorld act2Started', 'HOST', 'special', 'act flags; see dangers.md (StartAct3 on load)')
put('fixerStatueConvo', 'PERSONAL', 'keep client', 'dialogue in which an NPC hands over a gift; the gift stays with the talker')
put('newDatTraitorLord', 'PERSONAL', 'keep client', 'no writer or reader found (leftover)')
put('PinGalleryHasPlayedFinalChallenge', 'PERSONAL', 'keep client', 'pin gallery is personal (user: each plays their own)')
put('citadel_encounteredFencers', 'HOST', 'take host', 'boss/arena encounter record')
put('SprintMasterCurrentRace', 'UNSURE', 'max', 'race index of the race wish (quest target, written with its reward); suggested ONLY_ADD max (races won by either count)')

TRIGGER_VISITED = re.compile(r'^visited[A-Z]')


def decide(r):
    name, typ, old = r['name'], r['type'], r['class']
    if name.startswith('SavedFlea_'):
        return 'ONLY_ADD', 'OR', 'flea rescued: world find that counts toward a wish; union so neither player loses a rescue (cage gone if either freed it)'
    if name in X:
        return X[name]
    if old == 'shared-world':
        return 'HOST', 'take host', 'world/story state (was shared-world): ' + r['reason']
    if old == 'personal-boss':
        return 'HOST', 'take host', 'boss / arena / challenge record (beaten bosses follow the host): ' + r['reason']
    if old == 'personal-talk':
        return 'PERSONAL', 'keep client', 'casual NPC talk / seen-once flag: ' + r['reason']
    if old == 'personal-reward':
        if name.startswith(('Purchased', 'purchased', 'MerchantEnclave')):
            return 'PERSONAL', 'keep client', 'shop purchase flag: one-time stock per player'
        return 'UNSURE', '?', 'old personal-reward without a rule: ' + r['reason']
    if old == 'personal-hero':
        if r['reason'] == 'nothing writes or reads it':
            return 'PERSONAL', 'keep client', 'nothing writes or reads it (leftover)'
        if TRIGGER_VISITED.match(name) or name.startswith(('HasSeen', 'Seen', 'seen', 'promptFocus', 'show', 'Inv', 'Tool', 'Quest', 'Journal')):
            return 'PERSONAL', 'keep client', 'area title / tutorial prompt / UI flag'
        return 'UNSURE', '?', 'old personal-hero without a rule: ' + r['reason']
    return 'UNSURE', '?', 'old class ' + old + ': ' + r['reason']


out_rows = []
for r in rows:
    cls, merge, why = decide(r)
    out_rows.append((r['name'], r['type'], cls, merge, why, r['class']))

with open(os.path.join(OUT, 'classification.tsv'), 'w', encoding='utf-8', newline='') as out:
    w = csv.writer(out, delimiter='\t', lineterminator='\n')
    w.writerow(['field', 'type', 'class', 'merge', 'reason', 'old_class'])
    for row in out_rows:
        w.writerow(row)

c = collections.Counter(r[2] for r in out_rows)
print(c, len(out_rows))
for r in out_rows:
    if r[2] == 'UNSURE':
        print('UNSURE', r[0], r[1], r[4][:120])
missing = set(X) - {r['name'] for r in rows}
print('explicit names not in PlayerData:', missing)
