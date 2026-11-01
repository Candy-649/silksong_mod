# Code tidy log

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
