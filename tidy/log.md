# Code tidy log

## 2026-11-12, cd45757..933ed32

Third run (register D01-D105). At the user's word ("只整上次之后的有什么用，上次理过也没把这个问题揪出来啊") the readers searched the whole code, not only the commits since cd45757, and followed every hidden backstop (an else, an expiry, a "never came" branch) to what still reaches it. Four readers sent 48 findings. Merged, these come to 40 items: 15 picked (7 quiet, 8 visible), 11 later, 9 big, 2 for the user, 3 exceptions written into the register (D14 with a pointer from D45, D53, D74), 0 rejected.

Picked:
- T1 Delete the upstream code nothing reaches: SSMP.Fsm.Rotate, FollowObject, HazardRespawnComponent.ClimbRespawnOffset, the Concurrency folder, CompoundCondition, four Util methods and the PlayerDeath receive case
- T2 Delete the unreferenced upstream mod menu, its setting attribute and 29 uses, SettingsEntryInterface, the radio button box and LoginResponse
- T3 Take the echo-wait checks out of the catch paths, which can no longer wait for an echo (D35)
- T4 Stale words: catches stopping because the scene host did not take them (D35), the rescue wait of 45 s (D12), the relay renewal doc, the CurrentSave warning on every join
- T5 One StableHash for the eight FNV-1a copies, bit-identical at each site
- T6 SkinManager's sheet names from one SlashPictures.SheetsOf (D05)
- T7 Hornet's skins last and the Customizer folders in one place (D05)
- T8 visible: four chat lines - the rescue line for any number down (D37), the save-key line without the version (D56), /addon list in Chinese (D62), /help in plain gold (D100)
- T9 visible: the map position said again while still (D03)
- T10 visible: a boss's last blow shown on the scene host's figure by the data's sender, not "the one other player" (D37, D17)
- T11 visible: the scene host's FSM numbers kept off a combo the copy plays by itself (D102)
- T12 visible: the copy of the partner's pale nails fires on the owner's word, targeted or not, with one fallback (D09)
- T13 visible: a catch played here when nobody can be sent it, not let go (D35, D14)
- T14 visible: whatever else the copy runs here gives way to a catch of the local player, in PlayHere, and nothing is sent of one it can't play (D35, D98, D14)
- T15 visible: a catch judged from where the copy shows the creature, not after taking in what it is behind with (D102, D14; a D80 exception)

Left for later:
- A second input on the FSM that plays a catch (a wound by the player's own tool) resets the catch; check first in the FSMs whether any catch state takes a wound, D14/D77 (SSMP/Game/Client/Entity/Entity.RunHere.cs, Entity.Struck.cs, Save/CoopHits.cs)
- Wakes, signs, tools and struck parts that lead the copy into a choice of its next move only sent, as wounds are since c990e43, D14/D25 (Entity/Entity.Struck.cs, Save/CoopHits.cs, Save/CoopHits.Signals.cs)
- One ScenePath.Under/FindUnder for the three "path of a part" copies; shown parts go by numbered names as told parts do (Save/CoopSave.ShownParts.cs, Save/ToldParts.cs, GamePatcher.Senders.cs, ScenePath.cs)
- RollAlike of the flock flyers onto ThrownThings.Seed/Rolling, D27 (Entity/EntityManager.cs, Save/ThrownThings.cs)
- Lift, carriage and lava copies aged by both sides' RTT, one MessageAge helper, a field in three wire layouts, D49 (Save/CoopSave.LiftReplay.cs, CoopSave.Carriage.cs, CoopSave.LavaChase.cs, CoopSave.RoomClocks.cs)
- One HeldHero for the three ControlVersion holds and one taken-by-game test, D87 (MinigameCoop.Watching.cs, Save/CoopSave.WishConfirm.cs, Save/CoopSave.Deliveries.cs, BossRoomCoop.Endings.cs)
- One rule for who is on the creatures' list (film, cocoon, spawn), visible for a player stood up during their film, D42/D19 (PlayerTargetRegistry.cs, BossRoomCoop.Cutscenes.cs, Save/CoopSave.Rescue.cs, PlayerManager.cs)
- One FsmUtilExt.EventFieldsOf for four event-field scans; check the base-class walk finds nothing new, D69 (BossRoomCoop.Events.cs, Save/CoopSave.PacedMachines.cs, GamePatcher.NpcZones.cs, Save/ToldParts.cs)
- The pause menu onto the backpack's _keepTimeRunning, D68; first find which pause call stops time (Game/Client/PauseManager.cs, ClientManager.cs)
- One boss test for the health bars and the boss rooms, D30 (Ui/EnemyHealthBars.cs, BossRoomCoop.cs)
- The inner try/catch blocks of the send loop under its one catch, D77; check what a Steam call there throws (Networking/UpdateManager.cs)
- big: a talk's end judged as a late input is (taken where the host's FSM left its state by its own ways), and a refused talk frees the talker at once with an answer, not after 15 s, D14/D53, 2-3 commits (Entity/Entity.TalkHere.cs, Entity.HandOver.cs, Save/CoopHits.cs)
- big: the partner's pins and tacks under ToolCopies, with the hero's touch on a tack copy out, then the four effect classes, ToolPlacements and the tink bridge out, D08/D09, 3 commits (Animation/Effects/Tools/*, AnimationManager.cs, Save/CoopHits.cs)
- big: HeroCopies.Silence for the five hand-made copies plus the harpoon needle and ring thread, then HeroCopies.Make with a prepare step, D07/D05, 2 commits (HeroCopies.cs, HeroChildEffects.cs, HeroHarpoon.cs, HeroDust.cs, HeroRoomFigure.cs, Tools/ToolCopies.cs)
- big: one SaidAgain helper for the seven sub-effect resends, D02, 2 commits plus a talk with the user (HeroHit.cs, HeroHarpoon.cs, HarpoonRingLook.cs, HeroChildParticles.cs, HeroDust.cs, HeroHidden.cs, HeroRoomFigure.cs)
- big: one ordered hook on Fsm.ProcessEvent for the eight detours, D69, 4-6 commits (Ui/EnemyHealthBars.cs, ArenaCoop.BossArenas.cs, BindCoop.cs, BossRoomCoop.cs, HudWatch.cs, Save/CoopHits.Signals.cs, Save/CoopSave.cs, Save/CoopSave.FsmLift.cs, Hooks/EventHooks.cs)
- big: delete SSMPServer, D105, 1-2 commits, about 1200 lines (SSMPServer/, SSMP.sln, .github/workflows/build.yml, SSMP/SSMP.csproj)
- big: full synchronisation as the only mode, about 55 branches, 2-3 commits after SSMPServer (ClientManager.cs, ServerManager.cs, ModServerManager.cs, ServerInfo.cs, ArenaCoop*.cs, BossRoomCoop*.cs)
- big: delete the Steam P2P transport, D57, one commit of about 1000 lines (Networking/Transport/SteamP2P/*, ClientManager.cs, ModServerManager.cs, TransportType.cs)
- big: delete the upstream save mirror, 1-2 commits after SSMPServer (Save/SaveManager.cs, SaveDataMapping.cs, Server/Save/*, CopySaveCommand.cs, EncodeUtil.cs, SaveUpdate.cs, ServerInfo.cs, ClientManager.cs, ServerManager.cs, ClientUpdateManager.cs)

For the user:
- The room state taken in after a 3 s gap, from before reliable data was resent in order: delete it, or keep it (D80, D77; large if deleted)
- The scene host switching its creature off while it holds the client's player: hold the switch-off until the catch is through, or let it end the catch (D102 against D14's 11-12 sentence)

Rejected: 0.

Register wording to look at (not changed in this run): T15 adds to D80's exceptions that a catch of the local player does not take in first what the copy is behind with; T13 and T14 change no rule's words, but D35's "Now" could say a catch also plays when nobody can be sent it; D02's exception still says it was not recorded whether the uncounted path is on purpose.

Commits: T1 a47b11d, T2 f96da01, T3 725676e, T4 6f4fad1 (its last point, the CurrentSave warning, left out: no log line is taken out), T5 e9e0cd0, T6 e31f95d, T7 5bbee0f, T8 a8e31c3, T9 198af8e, T10 a7e3df4, T11 0b378c5, T12 8c3b790, T13 d22cae3, T14 67c0058, T15 f89dc61.

Review (one round): one high finding - a catch played with nobody to send it to (d22cae3) held the player for good once this game took the room over, as the room's own creature went on from the scene host's last state, which never had the catch: fixed in cbeab67, the takeover goes on with a catch the copy plays by itself from the copy's state (D35 now says so). Three low: the give-way of 67c0058 came before looking whether the catching FSM takes the catch, and ended a move without catching it up, fixed in 7b21fdf; the reading of a catch's told values off a touch was dead, deleted in 040d349; the relay renewal's words named ReliableNumbering as UpdateManager's, fixed in 040d349. CHANGES accbbfe; checklist t533-t541.

## 2026-11-08, f207047..9dda11a

Second run (157 commits, register D01-D97). Four readers sent 53 findings. Merged, these come to 46 items: 10 picked (6 quiet, 4 visible), 19 later, 9 big, 1 for the user, 5 exceptions written into the register (D09, D40, D42, D58, D77), 2 rejected.

Picked:
- T1 Delete the hero animator's ProcessEvents handler and its hook, which only made garbage and debug lines, and the lift ride's StartedAt that nothing reads
- T2 Drop the skin look's DressesHer flag and ShownColour, kept only for a colour that an older build sends under a skin (D56)
- T3 One fallback for the late copy of the song glow in HeroChildEffects: LookAgain out (D77), and its words say which parts are watched, with the unreachable ByCharacterClips return out (D01)
- T4 HeroPartClips and HeroRoomFigure onto EventHooks.SpriteAnimatorPlay instead of their own hooks on tk2dSpriteAnimator.Play (D69)
- T5 Four log-only try/catch blocks out, around sends and the boss fight start events (D77)
- T6 Stale words: the wish board summary (D40), the /copy reason (D36), the Anticipation doc (D22)
- T7 visible: the copy of the partner's memory-tool crystal no longer rolls its own tilt and look when the partner's change is lost (D09)
- T8 visible: the partner's hunter/witch/cloakless dash stab shows their needle's element (D04)
- T9 visible: the upstream /debug command out (D71)
- T10 visible: the temporary /0-/2 chat lines name the fights by place, not by boss (D63)

Left for later:
- RollAlike of the flock flyers onto ThrownThings.Seed/Rolling, D27; their rolled starts move in both games alike (SSMP/Game/Client/Entity/EntityManager.cs, Save/ThrownThings.cs)
- One StableHash for the eight FNV-1a copies, bit-identical per site (Save/ThrownThings.cs, SharedDice.cs, CoopAttackCheck.cs, CoopStateCheck.cs, CoopSave.RoomClocks.cs, Entity/Component/PartsComponent.cs, Entity/EntityProcessor.cs)
- One ScenePath.Under for the five "path of a part under an object" copies (Entity/Action/EntityFsmActions.cs, Save/ToldParts.cs, Save/CoopSave.ShownParts.cs, Entity/LetGoParts.cs, GamePatcher.Senders.cs)
- One FsmUtilExt.EventFieldsOf for the six cached event-field scans; their binding flags differ (base-class privates), so check what each finds (Save/ToldParts.cs, CoopSave.PacedMachines.cs, GamePatcher.NpcZones.cs, BossRoomCoop.Events.cs, Entity/Action/EntityFsmActions.Effects.cs, Entity/Entity.LateWounds.cs)
- One FsmUtilExt.SubFsmOf for the three RunFSM field reads (EntityFsmActions.Effects.cs, BossRoomCoop.HeartEnds.cs, Save/CoopSave.Interactions.cs)
- HeroPartOnFigure finds the scene host's figure by the data's sender, not TheOtherPlayer, visible for 3+, D37 (Entity/Action/EntityFsmActions.cs, PlayerTargetRegistry.cs)
- Lava chase, lift and carriage aged by both sides' RTT as RoomClocks is, a wire field in three updates, D49 (Save/CoopSave.LavaChase.cs, CoopSave.LiftReplay.cs, CoopSave.Carriage.cs, CoopSave.RoomClocks.cs)
- SendLiftRide onto SendToRoomMembers, and one SendTo for the member loops (Save/CoopSave.LiftReplay.cs, CoopSave.cs, CoopSave.Lifts.cs, CoopSave.WishTalk.cs)
- One FsmUtilExt.TakesEvent/StateOnEvent for the seven copies; check PlayMaker's global-or-own order for the two that read the target (ArenaCoop.MemoryEnds.cs, Save/CoopSave.PullRings.cs, BossRoomCoop.CatchUp.cs, Save/CoopSave.ShownParts.cs, Save/ToldParts.cs, Save/CoopSave.PacedMachines.cs, BindCoop.cs)
- One HeldHero for the three ControlVersion holds; the ending watch gains the taken-by-game and bench checks, D87 (MinigameCoop.Watching.cs, Save/CoopSave.WishConfirm.cs, Save/CoopSave.Deliveries.cs, BossRoomCoop.Endings.cs)
- Race lines name every missing member, visible for 3+, D37 (Save/CoopSave.Races.cs)
- GetWishTalkAbsence onto GetMissingMemberNames / one absence rule, D40 (Save/CoopSave.WishTalk.cs, CoopSave.WishBoard.cs)
- The two PathOf copies onto ScenePath.Get (Save/CoopSave.Rescue.cs, Save/WishDrops.cs, Save/CoopSave.Hurts.cs)
- The Customizer active.txt names in one place (Skin/OwnSkin.cs, Skin/SkinMods.cs, Skin/AtlasSlashes.cs)
- Delete the unreferenced upstream ModMenu, ModMenuSettingAttribute (and its 29 uses), SettingsEntryInterface, RadioButtonBox and LoginResponse, one commit (Ui/Menu/*, Ui/SettingsEntryInterface.cs, Ui/Component/*RadioButtonBox*.cs, Networking/Packet/Data/LoginResponse.cs, Game/Settings/ServerSettings.cs)
- Delete the unused Concurrency folder, CompoundCondition, four Util methods and the PlayerDeath receive case, one commit (SSMP/Concurrency/*, Util/CompoundCondition.cs, FileUtil.cs, FsmUtilExt.cs, GameObjectUtil.cs, MonoBehaviourUtil.cs, Networking/Packet/Update/ClientUpdatePacket.cs)
- The pause menu onto the backpack's _keepTimeRunning, D68; first make sure the pause menu stops time through SetPausedState (Game/Client/PauseManager.cs)
- The inner try/catch blocks of the send loop (renewal hold, stall notice, ping, link watch) under its one catch, D77; check first what a Steam call there throws and how often (Networking/UpdateManager.cs)
- One boss test for the health bars and the boss rooms, D30; needs the game data to tell whether the six named bosses sit under a boss scene (Ui/EnemyHealthBars.cs, BossRoomCoop.cs)
- big: the partner's pins and tacks under ToolCopies, then the four effect classes, ToolPlacements and the tink bridge out, D08/D09, 3 commits; weigh with the user first (Animation/Effects/Tools/*, AnimationManager.cs, Save/CoopHits.cs)
- big: build D95, room objects with no health are not a creature's entity, one predicate for five askers, the traps checked one by one, 4-6 commits (Entity/EntityProcessor.cs, EntityRegistry.cs, Entity.cs, Action/EntityFsmActions.cs, Entity.RunHere.cs, BossRoomCoop.cs, Resource/entity-registry.json)
- big: build D27, the table of pre-rolled seeds for 12 RollByName and 3 RollAs users, 6-7 commits (Save/SharedDice.cs and its users)
- big: start picks found from the FSMs instead of the one-row table, D30/D25, 2-3 commits and a census (Save/CoopSave.StartPicks.cs)
- big: delete SSMPServer as the user ruled 11-01, 1-2 commits, about 1200 lines (SSMPServer/, SSMP.sln, .github/workflows/build.yml, SSMP/SSMP.csproj)
- big: full synchronisation as the only mode, about 50 branches, 2-3 commits after SSMPServer (ClientManager.cs, ServerManager.cs, ModServerManager.cs, ServerInfo.cs, ArenaCoop*.cs, BossRoomCoop*.cs)
- big: delete the upstream save mirror, 2-3 commits after SSMPServer (Save/SaveManager.cs, SaveDataMapping.cs, ClientManager.cs, ServerManager.cs, ClientUpdateManager.cs, CopySaveCommand.cs, EncodeUtil.cs, BenchCoop.cs)
- big: delete the Steam P2P transport, D57, one commit of about 1000 lines (Networking/Transport/SteamP2P/*, ClientManager.cs, ModServerManager.cs, SteamRelayLoopbackChannel.cs, TransportType.cs)
- big: one ordered hook on Fsm.ProcessEvent for the six detours, D69, 4-6 commits (HudWatch.cs, BossRoomCoop.cs, BindCoop.cs, ArenaCoop.BossArenas.cs, Save/CoopHits.Signals.cs, Save/CoopSave.cs, Hooks/EventHooks.cs)

For the user:
- Which of the host's two stall notices stay ("X 好像卡了"/"恢复了" at 1.5 s and "连接断了"/"重新连上了" at 10 s), D58/D97

Rejected: 2 (the renewal docs, which say what the transport does and are still right; HudWatch's two catches, at the making and the body of a hook, which D77's exceptions keep).

Register wording to look at (not changed in this run): D73 names `PlayerManager.ShownColour`, which is `SkinLook.ShownColour` and goes with T2, with "whatever colour an older build sends"; D22 names `CanMoveTheCreature(FsmState)`, replaced by `RunKinds`/`KindsOf` (fb497f3); D41's exception names `TryHoldDialogueEnd`, gone, the Waiting/Done sets being on `BossRoomCoop.SharedDialogue`; D08's "Also" says the crystal copy goes on by itself after 1.5 s, and after T7 it no longer rolls then; the user's 11-01 ruling to delete SSMPServer has no entry; D71 loses /debug after T9.

Outcome: 8 committed in SSMP (59ffae1, 9108b49, e62a43f, 4b4f962, 556451b, 3ca8ceb, c1fffec, 4095790), each built; 2 skipped: T1, whose handler also holds upstream's unconditional warning of out-of-range frames, which would go unlogged; T5, whose catches D77's exceptions keep (the only catch of a hook body, and 8002ff4 kept the raises of events into the save). The review found nothing high or medium and two low things, both mended: 556451b let a crystal copy made fresh, whose first change from the thrower was lost, show the prefab's look 0, which is no clip (reverted 011e9c2, an exception in D09); /2's Chinese lines said the door, where it puts players at the fight's start (cd45757). INSTALL 8dd3d36 (two entries, 0.4.157). Run: 9 agents, 60 minutes, 2.1 M subagent tokens.
Next run: since cd45757.

## 2026-11-01, 5f0922a..86431a4

First run against the register (decisions.md D01-D71). Four readers sent 46 findings. Merged, these come to 39 items: 10 picked (8 quiet, 2 visible), 17 later, 5 big, 3 for the user, 3 exceptions written into the register (D16, D17, D25), 1 rejected.

Picked:
- T1 Delete members that nothing reaches (PlayerManager.UpdateInterpolations and the empty Initialize, AnimationManager.EffectDistanceThreshold, CoopHostSync.CompareProgress, CoopSave.IsCheckedMember, ServerSaveData.IsSteelSoul, TextComponent.GetPreferredWidth, SpawnEntityGameObject's spawningType)
- T2 Delete the PlayerDeath packet path that nothing sends, and the returning-host rule only it fed (LastHostedScene)
- T3 Delete the lockstep hold on a boss's shared dialogue (D41)
- T4 Delete the BounceLook/DeathLook handlers and the DeathLook value (D45, D56)
- T5 Drop the "older build" and "new ones go at the end" reasons from the wire enums and lists (D56, with the Death.cs non-lethal words, D12)
- T6 Reword the save-pairing words and mark the eight pairing kinds unused (D36)
- T7 Reword the co-op key's docs and the CoopSave summary to the rescue-only key (D55)
- T8 Read and write bit flags from a cached value array (D69)
- T9 visible: PvP and compass setting messages in both languages, with the compass message that said the opposite fixed (D62, D63)
- T10 visible: the partner-name fallback says 队友 in a Chinese game (D62)

Left for later:
- Witch crest dash slash copy without the partner's needle element, visible, D04 (SSMP/Animation/Effects/DashSlash.cs, SlashBase.cs)
- Remove the upstream /debug command, visible, D71 (SSMP/Game/Command/Client/DebugCommand.cs, ClientManager.cs)
- Drop PartnerKey/PartnerName/TakeOverPartner/PairedUtc from the save marker and name every member, visible for 3+, D36/D37; check first whether old markers on disk still need TakeOverPartner (SSMP/Game/Client/Save/CoopSaveData.cs, CoopSave.cs, CoopSave.Check.cs, CoopSave.HostSync.cs, CoopSave.Checkpoint.cs, CoopSave.Races.cs)
- Lava chase sample aged one way with the sender's RTT, D49; do it with the lift and carriage ageing as one helper (SSMP/Game/Client/Save/CoopSave.LavaChase.cs, CoopSave.RoomClocks.cs, CoopSave.LiftReplay.cs, CoopSave.Carriage.cs)
- D01 census of the parts still sent by message that 8880b9b did not list (ring thread, Parry DashBurst, two bind halos, Pt QuickCraft, Wallslide Dust, HeroDust kinds); needs the hero FSMs read while the game is closed (SSMP/Animation/Effects/HeroChildEffects.cs, HeroChildParticles.cs, HeroDust.cs)
- HeroChildEffects summary still says every part is watched, plus the unreachable ByCharacterClips return, D01 (SSMP/Animation/Effects/HeroChildEffects.cs)
- One FsmUtilExt.TakesEvent for the five "does this FSM go anywhere on this event" copies (BossRoomCoop.CatchUp.cs, ArenaCoop.MemoryEnds.cs, CoopSave.PullRings.cs, CoopSave.ShownParts.cs, ArenaCoop.cs)
- One "on another player's screen" helper for the camera check and the act-3 buff (GamePatcher.CameraChecks.cs, GamePatcher.BlackThread.cs)
- One StableHash for the eight FNV-1a copies; values travel between games, so each site must stay bit-identical (ThrownThings.cs, SharedDice.cs, CoopAttackCheck.cs, CoopStateCheck.cs, CoopSave.RoomClocks.cs, PartsComponent.cs, EntityProcessor.cs)
- Flock flyers' RollAlike onto ThrownThings.Seed/Rolling instead of string.GetHashCode, D27 (EntityManager.cs)
- The two PathOf copies onto ScenePath (CoopSave.Rescue.cs, WishDrops.cs, ScenePath.cs)
- ModVersion.HasChinese onto ChineseLineBreaks.IsChinese (Util/ModVersion.cs, Ui/Util/ChineseLineBreaks.cs)
- HeroPartClips and HeroRoomFigure onto EventHooks.SpriteAnimatorPlay instead of their own hooks, D69; check first that the event is raised after orig (Hooks/EventHooks.cs, HeroPartClips.cs, HeroRoomFigure.cs)
- HeroCopies.Make with a prepare step, for the hand-written holders, D05 (HeroCopies.cs, ToolCopies.cs, HeroChildEffects.cs, HeroHarpoon.cs, HeroDust.cs, HeroRoomFigure.cs)
- One HeroCopies.Silence for "copies make no shake, rumble or noise", D07; the sites differ (destroy vs disable), and the harpoon needle gains it (HeroChildEffects.cs, HeroDust.cs, ToolCopies.cs, BindBurst.cs and about 15 one-off destroys)
- SkinManager's sheet names from SlashPictures.HeroSheets; check the skin fingerprint stays the same (Skin/SkinManager.cs, Skin/SlashSkin.cs)
- The Customizer active.txt names in one place (Skin/OwnSkin.cs, Skin/SkinMods.cs, Skin/AtlasSlashes.cs)
- big: D48 MemberHolds helper for plates, rings and poles, hatches, toll benches, cogs and grinders, about 6-7 commits (CoopSave.SpikedCogs.cs, PullRings.cs, WorldTriggers.cs, Trapdoors.cs, TollBenches.cs, PacedMachines.cs, CoopSave.cs)
- big: one SaidAgain helper for the seven hand-made sub-effect resends, D02, 2 commits plus a talk with the user about carrying them in PlayerUpdate (HeroHit.cs, HeroHarpoon.cs, HarpoonRingLook.cs, HeroChildParticles.cs, HeroDust.cs, HeroHidden.cs, HeroRoomFigure.cs)
- big: delete the Steam P2P transport nothing can choose, D57, one commit of about 1000 lines plus the register's D57 exception (Networking/Transport/SteamP2P/*, ClientManager.cs, ModServerManager.cs, SteamRelayLoopbackChannel.cs)
- big: delete the upstream save mirror (SaveManager, SaveUpdate, server save store, /copysave), 2-3 commits, waits on the SSMPServer question (Save/SaveManager.cs, SaveDataMapping.cs, ClientManager.cs, ServerManager.cs, ServerSaveData.cs, ModSaveFile.cs, CopySaveCommand.cs, UiManager.cs, EncodeUtil.cs)
- big: full synchronisation as the only mode, about 50 branches, 2-3 commits, waits on the SSMPServer question (ClientManager.cs, ServerManager.cs, ArenaCoop*.cs, BossRoomCoop*.cs, ServerInfo.cs, SSMPServer/ConsoleSettings.cs)

For the user:
- Keep the standalone console server SSMPServer, record it as an exception, or remove it
- Server admin command replies: translate them, or record them as English only
- Tack and pin copies when the thrower's game noted no placement: show nothing, or keep the copy's own roll as an exception

Rejected: 1 (merging the three one-line place formatters of the log, CoopHits.FormatPlace and the check classes' Where: too small to be worth it).

Register wording to look at (not changed in this run): D27 says RollByName has 6 users, and there are now about 12; D42 says arena bosses raise BossRoomCoop.BossFightStartedEvent, but ArenaCoop has its own events, which are wired to the same handlers; D43 could add that since 7ea3b10 the end of a memory arena is played live for a player who walks in; D57's exception names Steam P2P, which goes with the big item above.

Outcome: all 10 committed in SSMP (4c96c61, 2029274, dba83cf, b00a561, 63ca1e0, 53e18c2, fc3321c, ebe335e, 9bd1463, 4a1fa11), each built; 48 files, 197 lines in, 619 out; the game stayed closed. The review found nothing high or medium and four low things, all mended in 24d1e54: RemoveGoneReaders and SharedDialogue.StartTime that dba83cf left without a use; EnumValues<T> folded into EnumCache<T> (one place for an enum's facts); the compass line says nothing in chat while map icons are always shown; ProtocolCheck/FlowSurvey.md notes the end-hold and pairing are gone. INSTALL f207047 (two entries, 0.4.143), checklist t426 (hidden) and t427 (daily). Run: 9 agents, 66 minutes, 2.1 M subagent tokens.
Next run: since f207047.
