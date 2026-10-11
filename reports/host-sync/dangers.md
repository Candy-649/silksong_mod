# Host-aligned co-op save: dangerous and inconsistent cases

Read-only research, 2026-10-11. Real names are used here (for the developer only). V = verified in IL / asset dumps /
a real save; I = inferred. Files: `classification.tsv` (1350 PlayerData fields), `items.tsv` (305 per-item entries),
`world.tsv` (4238 saved world objects + 13 prefab drops). Scripts in `work/` regenerate all three.

## 0. Where the alignment must run (V)

`GameManager.RunContinueGame` does, in order: `SaveDataUpgradeHandler.UpgradeSaveData` -> `FixUpSaveState` ->
`if (playerData.IsAct3IntroQueued) StartAct3()` (IsAct3IntroQueued = `blackThreadWorld && !act3_wokeUp`).
The aligned data must be in place **before** this runs (write the merged JSON into the slot, or patch PlayerData
before `RunContinueGame` reaches `FixUpSaveState`). Then the game's own repairs run on the merged data, which is good:
`FixUpSaveState` already repairs several cross-class mismatches (V, IL):
- `IsSilkSpoolBroken` recomputed from silkMax / HeroCorpseScene; `Cloakless` crest reset when `slab_cloak_battle_completed`;
- `GourmandGivenStew` (HOST) -> sets the client's personal "Mossberry Stew" amount (takes it away);
- `bellShrineBellhart` (HOST) -> marks quest "The Threadspun Town" completed;
- `defeatedCoralDrillers`/`defeatedCoralDrillerSolo` (HOST) -> writes journal kills of "Coral Conch Driller Giant";
- `FleaGamesEnded` <- quest "Flea Games" IsCompleted;
- Dust_Shack "Collectable Item Extractor Pins" pickup re-opened when the quest is open and the player has no pins;
- `crashingIntoGreymoor` fixed from `crashedIntoGreymoor`; `PlayerDataFixer.FixSilkSpools` (see 4).

## 1. Act transitions (V)

- `StartAct3` is only called from `RunContinueGame` when `blackThreadWorld && !act3_wokeUp`.
  Its PlayerData writes (IL): respawnScene="Song_Tower_Destroyed", respawnMarkerName, respawnType, mapZone,
  extraRestZone, bindCutscenePlayed=false, churchRhinoBlackThreadCorpse, mortKeptWeightedAnklet, **ExtraToolEquips**,
  fixerBridgeBroken, scholarAcolytesReleased(+Act3), spinnerDefeated, marionettesBurned, encountered/defeatedSongGolem,
  encountered/defeatedTrobbio, savedPlinney, SeenLastJudgeGateOpen, splinterQueenSproutGrewLarge, garmondInEnclave,
  CrowCourtInSession, grubFarmerTimer, farmer_grub* = false, mapperSellingTubePins; plus (memory, IL 09-15) boss records
  and journal kills, TryEndQuest x2, RemoveIncompleteActiveQuest x4, breaking deliveries, auto-equipping crests and the
  skill tool, and one SceneData one-way wall.
- **Host in act 3, client not:** copying `blackThreadWorld=true` AND `act3_wokeUp=true` means StartAct3 never runs for
  the client. The HOST parts are copied anyway, but the personal parts are skipped: the client keeps courier cargo
  (not broken), its farmer grubs keep growing, its crest/skill loadout is not auto-equipped, `ExtraToolEquips` is not
  set up, `mortKeptWeightedAnklet` not set.
  Copying `blackThreadWorld=true` with the client's `act3_wokeUp=false` instead makes StartAct3 run at load: the client
  wakes in the act-3 intro scene, far from the host, and StartAct3 overwrites HOST values the host already changed later
  (respawn, CrowCourtInSession, bindCutscenePlayed, grub timer...). **Suggested:** copy both flags, and run a small
  "personal part of StartAct3" on the client in the mod: break deliveries (DeliveryQuestItem.BreakAll-equivalent),
  reset farmer grubs, set ExtraToolEquips/mortKeptWeightedAnklet as StartAct3 does, auto-equip as StartAct3 does.
  Then respawn = host's bench (HOST).
- **Client in act 3, host not:** HOST sends the client back to act 2 world state (flags, quests), but its act-3 personal
  results (broken deliveries, auto-equipped crest, journal kills, items) stay. Same as "client beat bosses the host has
  not": warn before entry, as decided for bosses.
- `act2Started` (V): written by a quest-starter FSM and a cutscene, no load hook; HOST copy is safe.
- `StartBlackThreadWorld` has no C# caller (FSM-called from the death sequence); sets blackThreadWorld=true,
  act3_wokeUp=false. A host saved between that sequence and the act-3 intro would hand the client the same
  "intro queued" state; StartAct3 then runs for both, which is consistent.

## 2. TimePasses outputs (V: 199 writes; callers PlayerDead, ReturnToMainMenu, MazeMistZone, 5 FSM states)

- About 200 HOST fields are outputs of `TimePasses/TimePassesElsewhere` (NPC positions, crowds, enemy groups, Sherma,
  Garmond, Grindle, the city merchant, Gilly, the mapper, Nuu, enclave level, Bonetown pilgrim counts...). They flip
  both ways (e.g. `CrowCourtInSession`, `mapperAway`, `pinstressInsideSitting`), so they must be **take-host**, never OR/max.
- Their inputs mix HOST and PERSONAL fields: `muchTimePassed` (personal), personal `*Seen` talk flags
  (`bonetownPilgrimRoundSeen`/`HornedSeen` gate the countdowns), farm timers. After alignment the client's next death
  or menu return runs TimePasses with personal inputs on host outputs; it diverges until the live sync or the next
  entry. Not a corruption, just drift.
- TimePasses also writes PERSONAL outputs (`farmer_grub*`, `CrawbellCurrency`, `GrowstoneState`, timers,
  `MottledChildNewTool`, `BelltownFurnishingSpaAvailable`, `BelltownHousePaintComplete`) -> keep client.

## 3. Fields written by more than one kind of source (V from the writer reports)

| field / item | sources | what goes wrong with a plain copy |
|---|---|---|
| `heartPieces`/`maxHealthBase`/`maxHealth` | 14 world shard objects (world.tsv, SavedItemTrackerMarker / Heart Container Control), shops (`PurchasedBonebottomHeartPiece` = Grindle AND Bonebottom shard, `MerchantEnclaveShellFragment`, `PurchasedBelltownShellFragment` has no writer), 4 wish rewards (Destroy Thread Cores, Beastfly Hunt, Sprintmaster Race, Ant Trapper), possibly FSM gifts | max() of the host's count gives the client shards the host BOUGHT; then the client can buy the same shop shard again -> more masks than the game has |
| `silkSpoolParts`/`silkMax` | game's own list (PlayerDataFixer.FixSilkSpools, V): 13 world spools, 3 shop flags (`purchasedGrindleSpoolPiece`, `PurchasedBelltownSpoolSegment`, `MerchantEnclaveSpoolPiece`), 1 NPC gift (`MetCaravanTroupeLeaderJudge`, a talk flag), 1 wish (Save Sherma) | same as masks |
| Crest Socket Unlocker | 19 world pickups + 1 hanging sack, 3 shops, 1 wish reward; consumed into `ToolEquips[].Slots[].IsUnlocked` | copying slot unlocks gives the client the host's bought lockets |
| Tool Pouch / Tool Kit | 3 shops each, 1 wish each, FSM template `upgrade_tool_pouch` gives | same |
| Simple Key / Ward Key / Architect Key | world pickups, shops, consumed at ItemReceptacle locks whose open state is HOST | adding a key per host pickup while the host's lock is already open (HOST) gives the client a spare key |
| tools Tack, Magnetite Dice, Reserve Bind, Curve Claws, Fractured Mask, Weighted Anklet | world pickup AND shop (Tack also a wish reward; Reserve Bind also a boss-corpse drop) | a host who bought it must copy nothing |
| `hasNeedolin`, `hasParry`, `hasBrolly`, `QuillState`, mementos | boss death "give" states + scenes + NPC dialogue | fine as OR, because the boss record is HOST and the reward is ONLY_ADD |
| `hasQuill` | shop (Mapper Quill) + library desk scene | classed PERSONAL (shop); the desk route is lost for a client only if the desk is HOST-done |

**Only-add rule for counts (suggested):** never max() or copy a count. Count per source:
1. Enumerated world sources (pickup bools in world.tsv with class ONLY_ADD): for each one the host has taken and the
   client has not, give the client +1 of the item and mark the client's pickup taken.
2. Personal sources (shop flags, wish rewards): never copied.
3. Remainder (boss gifts, FSM gives that cannot be enumerated): residual = total - enumerated world - personal sources,
   computed for both saves; add `max(0, residual_host - residual_client)`.
4. Re-derive: masks = 5 + floor(shards/4), heartPieces = shards % 4; spools: silkMax = 9 + floor(parts/2),
   silkSpoolParts = parts % 2. The bases 5/9 and the 4/2 per upgrade are inferred (I), consistent with a real save
   (maxHealthBase 6 + heartPieces 3; silkMax 12 + silkSpoolParts 1) and the 18 spool sources the game lists (9 spools). Raise
   `maxHealth` by the same delta as `maxHealthBase` (`PlayerData.AddToMaxHealth` adds to both, V).
5. Keys: per source, minus locks of that key type that the host opened and the client had not (clamped at 0).

## 4. Wish (quest) state vs rewards

- `QuestCompletionData` entries are HOST (accepted/completed), rewards are PERSONAL by rule. A client whose host
  finished a wish alone (e.g. host played with someone else) gets "completed" and **never gets the reward**: 4 mask
  shards, Longneedle, Tack, Poison Pouch, Pinstress Tool, Shakra Ring, Lifeblood Syringe, Flea Brew, Growstone, Tool
  Pouch (Journal), Tool Kit (Crow Feathers), Crest Socket Unlocker (Rock Rollers), Pale Oil, money.
  Options: (a) also give the reward when the host completed and the client did not (consistent with the live co-op
  turn-in, where both get the reward); (b) keep each wish's completion personal. **Suggested (a)**, money rewards
  included or not as the user prefers; collection items are not taken from the client.
- Wish items that only drop once from a boss/arena corpse (Beastfly Remains, Broodmother Remains, Skull King
  Fragment, Pickled Roach Egg; flags `gotPickledRoachEgg`, `broodMotherEyeCollected`): the boss is HOST-dead, so the
  client can never collect them and the (HOST) accepted wish can never be turned in. Suggested: ONLY_ADD these when the
  host collected them.
- `GourmandGiven*`, `MushroomQuestFound*`, `SteelQuestSpots`, `SavedFlea_*`: wish progress. Given-items are HOST
  (FixUpSaveState then removes the client's stew, V); found/seen/rescued are OR (counts from either player).
- Courier deliveries stay personal (cargo, timer, `BelltownCouriersGenericQuests`).

## 5. Shop purchase flags and NPC departures (V flags, I effect)

- All `Purchased*`/`purchased*`/`MerchantEnclave*` flags are PERSONAL: one-time stock per player. Two ShopItems can
  share one flag (Grindle and Bonebottom mask shard both write `PurchasedBonebottomHeartPiece`), so stock "moves"
  between shops per player, which is fine.
- **HOST NPC-left / killed flags can remove a shop the client never used**: `ArchitectLeft` (architect shop: tools,
  key, tool kit, shard pouch), `MapperLeft*`/`mapperAway`/`MapperAppearInBellhart` (maps and pins are PERSONAL, so a
  client could lose the chance to buy a map), `BoneBottomShopKeepLeft`, `cityMerchantInGrandForumLeft`/
  `cityMerchantInLibrary03Left`, `antMerchantKilled`, `pilbyLeftPilgrimsRest`, `BallowLeftSauna`. The user decided
  "NPC两边一致" (09-15), so this is accepted; check in play whether the departed seller reappears elsewhere with the
  remaining stock (the mapper does move; not verified for the others).
- Copying a shop's purchase flag would remove the item from the client's shop without giving it -> never copy them.

## 6. Unique items that the story uses up

- The 12 `shareRemoval` items (Belltown House Key, Dock Key, Dock Demo Key, Ward Boss Key, Coral Chunk, Craw Summons,
  Crawbell, Farsight, Materium, White Flower, Wood Witch Item, Blue Goop Jar) are ONLY_ADD, but must **not** be added
  when the host's world shows them used (host entry collected with Amount 0, or the HOST lock/door/flag that consumes
  them). A client who still holds one after the host used it keeps a dead item (only-add never removes); the earlier
  rule took such items from both, so either remove it at entry (exception to only-add) or accept the dead item.
- `ConstructedMaterium`/`ConstructedFarsight` ONLY_ADD: the ingredients must then not be added either.

## 7. Bosses, arenas, scripted scenes

- Boss records HOST: the boss reward reaches the client only if the reward is ONLY_ADD (abilities, tools, mementos,
  hearts, quills). PERSONAL rewards of a HOST-dead boss (rosaries, relics, wish items above) are lost for the client.
- Arena wins HOST: the arena's reward pickups are PERSONAL and stay in the client's world when they are ordinary saved
  pickups; a reward flung only at the end of the fight (and saved through the arena) is lost. Not checked per arena.
- Scripted room-entry triggers (`entered*`, `gotPastDockSpearThrower`, `fullyEnteredVerdania`,
  `SatAtBenchAfterAbyssEscape`...) are HOST: the client will not see a scene the host already saw. This contradicts the
  earlier 10-xx rule that load/door scenes are seen by each player (CoopSave.Arrivals); decide which wins.
- Curse event (`gainedCurse`, `bindCutscenePlayed`, cure flag `BelltownDoctorCuredCurse` HOST): copying flags without
  the crest switch leaves the client on/off the cursed crest inconsistently; FixUpSaveState does not repair it (only
  Cloakless). UNSURE.
- `slab_cloak_battle_completed` / prison: FixUpSaveState resets the Cloakless crest when that HOST flag is set (V).

## 8. Other edge cases decided in the tables

- Journal (`EnemyJournalKillData`): suggested ONLY_ADD max(Kills)/OR(HasBeenSeen) per enemy; otherwise a boss the host
  killed can never enter the client's journal (journal-completion wish, thread-core wish). Game precedent: FixUpSaveState
  writes journal kills from boss records.
- Maps: bought (map seller) PERSONAL; found in the world (14 Has*Map via PlayerDataCollectable) ONLY_ADD (suggested).
  `scenesMapped/Visited`, markers, pins, quill: PERSONAL. QuillState (boss reward) ONLY_ADD max.
- Fleas `SavedFlea_*`: ONLY_ADD (OR), the cage is gone if either freed it.
- Home furnishings: ONLY_ADD (world purchase for the shared home, never lost); paint colour UNSURE.
- Relics: PERSONAL by default (their use is selling for rosaries); mementos ONLY_ADD; displays (MementosDeposited,
  MateriumCollected, IsDeposited) PERSONAL.
- Needle upgrades: PERSONAL (paid NPC service with rosaries + Pale Oil), Pale Oil PERSONAL.
- Respawn: respawnScene/Marker/Type/mapZone/extraRestZone HOST; hazard/temp/non-lethal respawn SESSION; the client's
  death cocoon (HeroCorpse*, HeroCorpseMoneyPool) PERSONAL and stays where it is.
- `permadeathMode`: refuse entry when the modes differ.
- Same saved-object ID with items of two classes (bone_east_weavehome "Collectable Item Pickup": a tool and a relic)
  share one saved bool, so they cannot be split; marked UNSURE in world.tsv.
- 8 memory-font objects (dock_11, greymoor_15b, library_11b, weave_08) give an item through an FSM action whose item
  reference FsmScan cannot resolve; UNSURE until resolved.
