# Code tidy log

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
