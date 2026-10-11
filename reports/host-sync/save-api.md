# Silksong save API facts for "client loads the host's save" (research 2026-10-11)

All facts below come from IL dumps of Assembly-CSharp (game version string "1.0.30000", RevisionBreak 28104) made with
ILPeek, plus the SSMP source on branch `sync-fixes`. Raw dumps are next to this file (`ssb.txt`, `load1.txt`,
`load2.txt`, `ser.txt`, `savegame.txt`, `savegame2.txt`, `desk.txt`, `cont.txt`, `resp.txt`, `respawn.txt`,
`stats.txt`). "Unverified" marks anything I did not see in IL.

Note: decrypting the user's save files was refused by the permission system, so file contents were **not** read;
JSON size below is computed from file sizes only.

---

## 1. Producing save data from the live game (host)

### Types
- `SaveGameData` has exactly two fields: `playerData : PlayerData`, `sceneData : SceneData`.
  Ctors: `()` (new PlayerData + new SceneData) and `(PlayerData, SceneData)` (stores the references, no copy).
- `SceneData` serialized fields: `persistentBools`, `persistentInts`, `geoRocks` (plus static `_instance`).
- No field in PlayerData/SceneData identifies a Steam user or machine (searched names for steam/user/guid/machine/
  platform/owner: only `HeroCorpseMarkerGuid`, a scene marker id).

### Serialization format
- JSON via **Newtonsoft.Json**: `SaveDataUtility.SerializeSaveData<T>(T, Action<JsonSerializer>)` /
  `DeserializeSaveData<T>(string)`. Serializer (`CreateJsonObjects`) = `JsonSerializer.Create(UnityConverterInitializer.defaultUnityConvertersSettings)`
  with DefaultValueHandling=2 (Populate), ReferenceLoopHandling=1 (Ignore), NullValueHandling=1 (Ignore),
  MissingMemberHandling=0 (Ignore).
- `GameManager.GetJson<T>(T)` = `SerializeSaveData(obj, null)`.
- Bytes on disk: `GameManager.GetBytesForSaveJson(string)` = if `gameConfig.useSaveEncryption && !Platform.IsFileSystemProtected`
  (base `Platform.IsFileSystemProtected` returns false) -> `TeamCherry.SharedUtils.Encryption.Encrypt(json)` (string),
  then `BinaryFormatter.Serialize` of that string into a MemoryStream; else UTF-8 bytes.
  Reverse: `GameManager.GetJsonForSaveBytes(byte[])` (BinaryFormatter.Deserialize -> `Encryption.Decrypt`).
  `GameManager.GetBytesForSaveData(SaveGameData)` = `GetBytesForSaveJson(GetJson(data))`.
  Static variants `GetJsonForSaveBytesStatic` / `GetBytesForSaveJsonStatic` exist.
  (The AES-ECB/PKCS7 key and file layout are in memory note reading-the-save.md.)

### `GameManager.GetSaveGameData(int profileId)` - what it does (165 bytes IL)
1. `FixUpSaveState()` - mutates live data: may clear `IsSilkSpoolBroken`, restore crest from "Cloakless" and call
   `HeroController.ResetAllCrestState()`, remove Mossberry Stew, complete "The Threadspun Town" quest,
   fix "Coral Conch Driller Giant" journal kills, set `FleaGamesEnded`, reset a Dust_Shack persistent bool,
   clear `crashingIntoGreymoor`, `PlayerDataFixer.FixSilkSpools`.
2. `achievementHandler.FlushRecordsToDisk()`.
3. `playerData.playTime += sessionPlayTimer; ResetGameTimer()`.
4. Sets `playerData.version = "1.0.30000"`, `RevisionBreak = 28104`, `profileID = profileId`.
5. `playerData.CountGameCompletion()` (recomputes completion).
6. `playerData.OnBeforeSave()` - **can change the live hero's crest**: if `IsCurrentCrestTemp` it restores
   `PreviousCrestID` and fires `ToolItemManager.SendEquippedChangedEvent`; also a Slab_16 cloakless swap; then
   `Platform.UpdatePlayTime`.
7. Returns `new SaveGameData(playerData, sceneData)` - **references to the live objects**, not a copy.
- It has **no callers** in Assembly-CSharp. `PreparePlayerDataForSave(int)` is the same minus FixUpSaveState and is
  what `SaveGame` and `CreateSaveGameData` use. Every real save (`SaveGame(int, Action<bool>, bool, string)`) also
  first calls `SaveLevelState()` and then serializes on the save worker thread (`SaveDataUtility.AddTaskToAsyncQueue`),
  i.e. while the game keeps running.

### `SaveLevelState()`
Invokes the `GameManager.SavePersistentObjects` event; subscribers are `PersistentItem<T>.Awake` and
`GeoRock.OnEnable`. It writes the current room's object states into SceneData. The game calls it on every scene
transition (`BeginSceneTransitionRoutine`, `TransitionScene`, `ChangeToScene`, `PlayerDeadFromHazard`), on
`SaveGame` and `CreateRestorePoint`. Without it the current room's not-yet-saved changes are missing.

### Recommended host snapshot (no disk writes, no gameplay side effects)
On the main thread, at a moment where `/copy` would allow a save (CoopSave.CopySave.cs:56-58: GameState PLAYING,
not in scene transition, hero not dead/transitioning/hazard-respawning/down, `!disablePause`):
1. `gm.SaveLevelState()`.
2. `json = SaveDataUtility.SerializeSaveData(new SaveGameData(gm.playerData, gm.sceneData), null)` (or `gm.GetJson(...)`).
   Main thread avoids the torn reads the game's own worker-thread serialization risks.
3. Patch the copy (on the client after deserializing, or in JSON): `version = "1.0.30000"`, `RevisionBreak = 28104`.
   Reason: the in-memory `version` is whatever the file had when loaded; if the host loaded an older-version save and
   has not saved since, the client's `SaveDataUpgradeHandler.UpgradeSaveData` would re-run upgrades on data that the
   host's game already upgraded (idempotence of the upgrades is unverified).
4. Compress (JSON is roughly 167 KB, see 2) and send over the existing fragmented reliable channel.
- Alternative: `gm.SaveGame(cb)` then `Platform.Current.ReadSaveSlot` (what `/copy` does,
  CoopSave.CopySave.cs:74,106). Gives exactly the game's own save, but rotates the host's .bak files, may also write a
  restore point (`isAutoSaveQueued` -> `SaveGameWithAutoSave`), and runs FixUpSaveState/OnBeforeSave on the live game.
- If the host is still in the menu (not loaded yet), read its slot: `Platform.Current.ReadSaveSlot(slot, bytes)` +
  `gm.GetJsonForSaveBytes(bytes)`.
- Main-thread serialization time for ~167 KB is unmeasured (unverified; expect a one-frame hitch).

---

## 2. Reading / writing a slot

### Platform API (all in Assembly-CSharp)
- `Platform.Current` (DesktopPlatform on PC). `Platform.SaveSlotCount` field; `IsSaveSlotIndexValid(i)` = `0 <= i < 5`.
  Menu shows slots 1-4.
- `IsSaveSlotInUse(int, Action<bool>)`, `ReadSaveSlot(int, Action<byte[]>)`, `WriteSaveSlot(int, byte[], Action<bool>)`,
  `ClearSaveSlot(int, Action<bool>)`, `EnsureSaveSlotSpace`, `WriteSaveBackup(int, byte[], Action<bool>)`
  (-> `SaveRestoreHandler.WriteVersionBackup`), `DeleteRestorePointsForSlot`, `DeleteVersionBackupsForSlot(int, Action<bool>)`.
- DesktopPlatform delegates to the online subsystem only if `HandlesGameSaves`; `DesktopOnlineSubsystem.HandlesGameSaves`
  returns false (only `GameCoreOnlineSubsystem` overrides it), so Steam uses the local files.
- Save dir: `Application.persistentDataPath` + online subsystem `UserId` (Steam id folder `1670838450`), else "default"
  (`DesktopPlatform.Awake`). The data itself is not tied to the account; only the folder is.
- `GetSaveSlotPath(slot, usage)` = dir + `Platform.GetSaveSlotFileName(slot, usage)`: slot 0 -> `user.dat`, else
  `user{N}.dat`; usage 1 appends `.bak`, 2 appends `.del`.
- `LocalIsSaveSlotInUse` = `File.Exists(user{N}.dat)`, answered through `CoreLoop.InvokeSafe`.
- `LocalReadSaveSlot` = `File.ReadAllBytes` or null if missing; callback via `CoreLoop.InvokeSafe`.
- `WriteSaveSlot`: writes `user{N}.dat.new`, then if `user{N}.dat` exists
  `File.Replace(new, dat, dat + ".bak" + GetBackupNumber(...))`, else `File.Move(new, dat)`. `GetBackupNumber` keeps
  at most 3 numbered backups (deletes the lowest when there are >= 3) and returns max+1. Synchronous file IO on the
  calling thread; callback via `CoreLoop.InvokeSafe`.
- `ClearSaveSlot` deletes only `user{N}.dat`. `GameManager.ClearSaveFile` additionally deletes restore points;
  `RunStartNewGame` deletes the slot's version backups.

### How SSMP writes a slot today
`CoopSave.CopySave` (CoopSave.CopySave.cs): `gm.SaveGame(cb)` (:74) -> `FindEmptySlot` via `Platform.Current.IsSaveSlotInUse` (:173)
-> `Platform.Current.ReadSaveSlot(slot, ...)` (:106) -> `Platform.Current.WriteSaveSlot(target, bytes, ...)` (:121),
i.e. raw file bytes copied, never re-encoded; never writes over a used slot. `BackUpSave` (CoopSave.Check.cs:1105)
copies `user{N}.dat` (path from `GetSaveSlotPath` by reflection, :1135) into `ssmp/coop-backups/<folder>/slot{N}`, newest 10.

### Files (sizes only, user's folder 1670838450)
| file | bytes |
|---|---|
| user1.dat / user1.dat.bak1 / user1_1.0.30000.dat | 89,778 each |
| user2.dat / user2.dat.bak1 / user2_1.0.30000.dat | 222,470 each |
| user3_1.0.30000.dat (no user3.dat, so slot 3 shows Empty) | 52,486 |
| shared.dat / shared.dat.bak | 645 |
| steam_autocloud.vdf | 53 |
| Restore_Points1: 3 files 93-153 KB; Restore_Points2: 10 files 93-295 KB (`NODELrestoreData1.dat`, `restoreData2..10.dat`) |

JSON size estimate for user2.dat: 222,470 - 22 (BinaryFormatter header) - 3 (length) - 1 = 222,444 base64 chars ->
166,833 bytes of ciphertext -> about **167 KB of JSON**.

### What must stay consistent
- Load reads only `user{N}.dat` (`LocalReadSaveSlot`). `.bakN`, `.new`, `user{N}_1.0.30000.dat` (version backup) and
  `Restore_Points{N}/` are not read on load. Version backups and restore points are listed by `RestoreSaveButton`
  (`FetchRestorePoints`, `FetchVersionBackupPoints`) for the in-menu "restore" feature, so after a host-derived save
  the player can still restore their own pre-co-op points (feature or desync source, design decision).
- An empty slot reused this way keeps any stale `user{N}_1.0.30000.dat` and `Restore_Points{N}` from an older save
  (the game's own new game deletes version backups via `DeleteVersionBackupsForSlot`; skipping new game skips that).
- `steam_autocloud.vdf` means Steam Auto-Cloud syncs this folder (which files is Steam-side config, unverified);
  anything the mod writes will be uploaded too.

---

## 3. Loading path

```
SaveSlotButton.OnSubmit(BaseEventData)                       (ssb.txt)
  if !interactable return
  LocalSharedData.SetInt("lastProfileIndex", SaveSlotIndex)
  state 3 LoadedStats:
     saveStats.PermadeathMode == 2 -> ForceDeselect + ClearSavePrompt (dead Steel Soul)
     !TryEnterSave() -> return   (black-thread slot needs several presses: saveStats.IsBlackThreadInfected)
     ui.UIContinueGame(slot, saveStats.saveGameData)   <-- CACHED data from the menu preload, no file read
  state 2 Empty:
     gm.profileID = slot
     status record RecPermadeathMode==1 or RecBossRushMode==1 -> ui.UIGoToPlayModeMenu()
     else ui.StartNewGame(false, false)
  state 4 Corrupted -> GenericMessageCanvas.Show("SAVE_CORRUPTED")
  then base MenuButton.OnSubmit(eventData)
UIManager.UIContinueGame(int, SaveGameData)
  ih.StopUIInput, IsSelectingProfile, saveSlot = slot
  if overscan and brightness adjusted -> gm.LoadGameFromUI(slot, data)
  else -> overscan/brightness menu; later UIStartNewGameContinue -> UIContinueGame(slot) WITHOUT data -> reads the file
GameManager.LoadGameFromUIRoutine(slot, data)   (d__443)
  ui.ContinueGame()  (menu audio, HideSaveProfileMenu)
  data != null -> SetLoadedGameData(data, slot)        (no file read)
  data == null -> LoadGame(slot, cb): Platform.ReadSaveSlot -> GetJsonForSaveBytes -> SetLoadedGameData(string json, slot)
                  -> DeserializeSaveData<SaveGameData> -> SetLoadedGameData(SaveGameData, slot)
  success -> GameManager.ContinueGame() -> RunContinueGame(fromMenu); failure -> ui.UIGoToMainMenu()
GameManager.SetLoadedGameData(SaveGameData, int)   (118 bytes)
  pd.ResetNonSerializableFields()   (tempRespawnScene/Marker = null, tempRespawnType = -1)
  PlayerData.instance = pd; gm.playerData = pd; SceneData.instance = sd; gm.sceneData = sd; gm.profileID = slot
  pd.silk = 0; pd.silkParts = 0; pd.SetupExistingPlayerData(); inputHandler.RefreshPlayerData();
  QuestManager.UpgradeQuests(); Platform.Current.OnSetGameData(profileID)  (base: no-op)
RunContinueGame (d__474)
  fade, music; SetState(2); SaveDataUpgradeHandler.UpgradeSaveData(sceneData, playerData)  (by pd.version)
  FixUpSaveState()
  if pd.IsAct3IntroQueued (blackThreadWorld && !act3_wokeUp): StartAct3() and later load "Opening_Sequence_Act3"
  TimePassesLoadedIn() (Belltown paint), LoadGlobalPoolPrefab, CreateStartupPools, LoadHeroPrefab, SetupSceneRefs
  ... ReadyForRespawn(true) -> GetRespawnInfo -> BeginSceneTransition(respawn scene, entry gate = marker)
```

### (a) Best in-memory hook
**`GameManager.SetLoadedGameData(SaveGameData, int)`** is the single choke point for both load routes (cached data
from the button and the file route through `LoadGame`). A MonoMod hook (like `OnClearSaveFile`, CoopSave.cs:376-383)
can swap/merge the `SaveGameData` argument before `orig` runs; everything after (upgrade, fix-up, act 3 check, respawn)
then runs on the merged data. The incoming argument for a non-empty slot **is** the client's own save, so the merge
has the personal data at hand. It is synchronous, so the host data must already be in memory when the player submits.
Gate it so it only acts for the co-op load the mod started.

### (b) Writing the slot first, then the normal load
- **Not enough alone with the menu open**: a LoadedStats slot submits the cached `saveStats.saveGameData` (read at menu
  preload), and an Empty slot always goes to `StartNewGame`. The file written meanwhile is ignored.
- The menu can be refreshed: **`UIManager.ReloadSaves()`** calls `ResetButton(gm, isSaveProfileMenu, true)` on slots
  1-4, which sets `saveFileState = 0`, then `Prepare(gm, force: true, animate, fetchRestorePoints)` ->
  `IsSaveSlotInUse` -> `GetSaveStatsForSlot` (async, may use the save worker) -> `ProcessSaveStats` -> state 3/2/4/5.
  The game itself does exactly this after importing saves (`MenuButtonSaveImport.ProcessImportData` -> `ReloadSaves`).
  The mod must then wait until the button's `saveFileState` is 3 before submitting (with an animation from empty to
  full). `ClearSaveCache()` only clears state/stats without re-reading.
- Pros: crash-safe (the merged save exists on disk before play, with the game's own `.bakN` rotation), covers the
  overscan/brightness route that reads the file. Cons: async refresh + wait, visible slot animation, and for a slot whose
  merged data has `IsAct3IntroQueued` the black-thread multi-press gate appears.
- **Empty slot without writing**: intercept `OnSubmit` for state 2 (the existing `OnSaveSlotSubmit` prefix,
  CoopSave.cs:2557) and call `ui.UIContinueGame(slot, mergedData)` instead of `orig` - the same call OnSubmit makes for a
  full slot. `SetLoadedGameData` sets `gm.profileID = slot`; the first save creates `user{N}.dat` via `File.Move`. Base
  `MenuButton.OnSubmit` (submit sound/flash) would be skipped unless called. Personal data for an empty slot: a fresh
  `PlayerData` is what `PlayerData.CreateNewSingleton` builds - `DeserializeSaveData<PlayerData>("{}")` +
  `SetupNewPlayerData()` + `SetupExistingPlayerData()` (CreateNewSingleton itself also sets the static instance; build it
  by hand to avoid that).
- Recommended: hook `SetLoadedGameData` for the merge + route Empty slots through `UIContinueGame(slot, data)`; back up
  the client's slot first (`BackUpSave`). Optionally also write the merged bytes (`gm.GetBytesForSaveData`) with
  `WriteSaveSlot` for crash safety; no menu refresh is needed in that case because the in-memory route is used anyway.
- Edge: if `gs.overscanAdjusted`/`brightnessAdjusted` are not both 1 (first launch), UIContinueGame goes through the
  overscan menu and then loads **from the file** without data - an Empty slot then fails (`LoadGame` callback false ->
  main menu) unless the file was written first.

---

## 4. Spawn

- `GameManager.GetRespawnInfo(out scene, out marker)`: uses `tempRespawnScene/tempRespawnMarker` if set (always nulled by
  `ResetNonSerializableFields` on load), else **`respawnScene` + `respawnMarkerName`**. It validates them against
  `SceneTeleportMap.GetTeleportMap()[scene].RespawnPoints` (a static map, not the player's visit history); if invalid,
  falls back to `"Tut_01"` / `"Death Respawn Marker Init"`.
- `HeroController.Respawn(spawnPoint)` coroutine: sits the hero on the bench (`RespawnResting` on the "Bench Control" FSM)
  only when **`respawnType == 1`** and the marker has a Bench Control FSM; otherwise a wake-up (`Wake Up Ground`/
  `Prostrate`/custom RespawnMarker wake-up).
- Bench saves set them via `PlayerData.SetBenchRespawn(...)`: `respawnMarkerName`, `respawnScene`, `respawnType`, and
  `GameManager.SetCurrentMapZoneAsRespawn()` / `SetOverrideMapZoneAsRespawn` -> **`mapZone`, `extraRestZone`**
  (used by the slot preview and map/HUD zone).
- So copying `respawnScene`, `respawnMarkerName`, `respawnType`, `mapZone`, `extraRestZone` from the host puts the client
  at the host's last respawn point, seated if it was a bench. Not used on load: `nonLethalRespawn*`, `hazardRespawn*`,
  `atBench` (not checked further).
- Never-visited scenes: nothing in this path checks `scenesVisited`; the scene's object states come from `SceneData`
  (host's if taken). Map knowledge (`scenesVisited`, `scenesMapped`, `scenesEncounteredBench`, `hasPinBench`...) is
  PlayerData and follows the merge choice. Whether some benches need personal PlayerData flags to be usable is unverified
  per bench (see memory toll-bench-sync / trap-bench-facts).
- `StartAct3` overwrites respawn to `Song_Tower_Destroyed` / `Death Respawn Marker Init` / type 0 / mapZone 29 (see 7).
- `TimePassesLoadedIn` sets `BelltownHousePaintComplete` when respawnScene is Belltown/Belltown_Room_Spare and
  `BelltownHouseColour` is set.

---

## 5. Compatibility fields

- Menu check: `SaveSlotButton.ProcessSaveStats` -> `IsVersionIncompatible(saveStats.Version, saveStats.RevisionBreak)` =
  `SaveDataUtility.IsVersionIncompatible("1.0.30000", version, revisionBreak, 28104)`: incompatible if version empty,
  if `revisionBreak > 28104`, if save major > game major, or same major and save minor > game minor. Older saves are
  compatible. Only the slot preview checks it (state 5 Incompatible); `SetLoadedGameData` does not.
- `version` also drives `SaveDataUpgradeHandler.UpgradeSaveData` (scene splits for `scenesMapped`, `_saveDataUpgrades`).
- `profileID`: only written (GetSaveGameData/PreparePlayerDataForSave), never read on load; `gm.profileID` comes from the
  slot argument of `SetLoadedGameData`.
- `permadeathMode` (0 normal, 1 Steel Soul, 2 dead Steel Soul) and `bossRushMode`: read by `SaveStats`, `OnSubmit`
  (2 -> clear prompt), `HeroController.Die`, `GameManager.PlayerDead`, completion checks. Must be decided explicitly in
  the merge (a client inheriting 1 would play by Steel Soul death rules).
- Set on the merged data: `version = "1.0.30000"`, `RevisionBreak = 28104`; `profileID` is harmless.
- Both players must run the same game version (a newer host save would be Incompatible in the client's menu if written
  to disk; in memory it would load without the check - avoid).
- Account/machine: only the save folder (Steam UserId); nothing in the data.

---

## 6. Confirm/cancel dialog in the save menu

- `GenericMessageCanvas` (static `Show(string key, Action onOk)`, private `ShowInternal`, `Hide`, private
  `OkButtonClicked`): fields `rootGroup`, `labelText`, `descText` (UnityEngine.UI.Text), **one** `okButton`,
  `okButtonCallback`, `isActive`, `_instance`. Text from `LocalisedString("Error", key + "_TITLE"/"_DESC")` - SSMP
  already feeds custom text through a language hook (CoopSave.cs:2765, 2807, 2815). It selects the OK button and calls
  `ih.StartUIInput()`, so gamepad A works. **No cancel/No button and no cancel handling.**
- The game's own yes/no prompts in the menus are prefab-specific, not reusable with custom text:
  - Clear save: `SaveSlotButton.ClearSavePrompt` (slot state 7) / `ClearSaveConfirmPrompt` (state 8) / `CancelClearSave`,
    driven by `ClearSaveButton` objects; its "yes" leads to `ClearSaveFile` -> `GameManager.ClearSaveFile` (destructive).
  - Restore point: `RestoreSaveButton.RestoreSaveConfirmYes/No`.
  - Save import: `MenuButtonSaveImport.ShowConfirmPrompt/ConfirmPrompt/CancelConfirmPrompt` (Extras menu).
  - In game only: `DialogueYesNoBox`, `YesNoBox`, `QuestYesNoBox`, `InteractEvents.ShowYesNo`.
- Simplest safe alternatives (in order):
  1. Make the choice itself the confirmation: after READY, the slot press means "load the host's world into this slot";
     the client's slot is backed up first, and a GenericMessageCanvas OK message explains it before/after.
  2. Two-press: first press shows the explanation (OK closes, nothing happens); pressing the same slot again commits;
     Back cancels as usual. Uses only verified pieces.
  3. A cloned GenericMessageCanvas with a second button instantiated from `okButton` (custom wiring; gamepad navigation
     between the two buttons unverified).
  SSMP's own `ComponentGroup` UI is not gamepad-selectable in the menu (memory: CoopPromptInterface uses a key line
  because nothing shows a cursor; save-menu gamepad issues in input-and-gamepad-facts.md).

---

## 7. Other dangers

- **Act 3 transition on load**: `RunContinueGame` runs `StartAct3()` when `IsAct3IntroQueued` (blackThreadWorld &&
  !act3_wokeUp). StartAct3 rewrites respawn, ends quests (`Silk Defeat Snare`, `Grand Gate Bellshrines`), removes
  incomplete active quests, auto-equips a Hunter crest and resets `ExtraToolEquips`, breaks delivery items, sets many
  world flags and journal kills. If the host snapshot is taken in that state, the client's merged personal loadout gets
  rewritten too. Also the menu's `TryEnterSave` black-thread multi-press gate applies to such a slot. Recommend not
  snapshotting while `IsAct3IntroQueued`.
- **FixUpSaveState on load** runs on the merged data (crest from Cloakless, Mossberry Stew, quests, Coral driller kills,
  FleaGamesEnded, Dust_Shack bool, silk spools). It mixes personal (crest, collectables) and world (persistent bool)
  fields - harmless if merged data is consistent.
- `SetLoadedGameData` zeroes `silk`/`silkParts`, `SetupExistingPlayerData` fills random lists if empty
  (mossBerryValueList, GrubFarmerMimicValueList, SteelQuestSpots) - per-player random rolls, keep the client's own.
- `QuestManager.UpgradeQuests()` runs on load.
- Achievements: `CheckAllAchievements` has no callers in Assembly-CSharp; nothing recomputes achievements on load.
  Achievements are awarded at events (`AwardQueuedAchievements` on transitions/death/menu return), so later events on
  host-derived progress may award Steam achievements to the client (unverified which).
- Slot preview: `SaveStats` (built from `version`, `RevisionBreak`, `maxHealthBase`, `IsSilkSpoolBroken`, silk max,
  `geo`, `ShellShards`, `mapZone`, `extraRestZone`, `BelltownHouseColour`, `playTime`, `permadeathMode`, `bossRushMode`,
  `completionPercentage`, `ConstructedFarsight`, act 3 flags, `CurrentCrestID`, endings) will show the merged save
  after the first save; `saveStats.saveGameData` keeps the whole deserialized save.
- Journal (`EnemyJournalKillData`), `Collectables`, `QuestCompletionData`, `Tools`, `ToolEquips`, crests are PlayerData;
  personal/world split is a design decision (see story-flags-facts / coop-economy-save-proposal).
- Cocoon: `HeroCorpseScene/MarkerGuid/Type/MoneyPool` and `IsSilkSpoolBroken` belong together (FixUpSaveState clears
  `IsSilkSpoolBroken` when there is no corpse scene) - keep them from the same player.
- Restore points / version backups of the slot stay (see 2). Steam Auto-Cloud uploads the folder.
- The host's in-memory `playTime` excludes the current session timer; the client should keep its own `playTime`/`date`.
- SSMP's existing `SaveManager.SetSaveWithData(CurrentSave)` (SaveManager.cs:713) applies mapped save values from the
  server; interaction with a host-derived load is not analysed here.

## Open / unverified
- Main-thread serialization cost; Newtonsoft round-trip fidelity of every PlayerData field (should match since the game
  itself round-trips through the same serializer).
- Whether re-running `SaveDataUpgradeHandler` upgrades is idempotent (avoid by setting version).
- Steam Auto-Cloud file patterns; Steam achievements triggered later.
- Gamepad navigation in a cloned two-button message canvas.
- Visibility of `SaveDataUtility`/`GetJson` etc. (SSMP references `Silksong.GameLibs`; memory says private access works,
  not re-checked).
