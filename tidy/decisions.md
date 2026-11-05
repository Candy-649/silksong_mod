# Decisions in force

This register lists the decisions that SSMP's code should follow now. For each one it gives the rule, the earlier way
it replaced, what code still written the old way looks like, the places that keep the old way on purpose, and where
the decision came from (memory files of earlier sessions, commits, and the user's own words). The code tidy workflow
(`.claude/workflows/code-tidy.js`) reads it to find code that still follows a replaced decision, and both that
workflow and the main session keep it up to date: a better way the user settles gets its entry, or updates one, the
same day. Numbers are never reused. A replaced entry keeps its number and says "Replaced by D<nn>". "Decided, not
built" means the user approved the rule but no commit has carried it out yet. Memory file names are in
`C:/Users/48688/.claude/projects/D--programming-silksong-mod/memory`, and commit shas are on SSMP `sync-fixes`.

## A. The players' characters

### D01 A move sends only its steps
- Now: a player's move sends only its steps, which are the character's counted clips. Every game plays the parts of the hero that come with the move from those clips (`Animation/Effects/PartsByClips.cs`, called from `AnimationManager.UpdatePlayerAnimation`).
- Replaced: one on/off message per hero part through `HeroChildEffects` (6643be4, 09-22), with clips replayed per part by `HeroPartClips` (cb9de66). The super jump needles moved first (`SuperJumpNeedles`, ae77e03, 10-31). On 11-01 they were folded into `PartsByClips` with about 30 more parts (8880b9b), because separately sent parts arrived out of step with the move.
- Old-way signs: a part that `HeroChildEffects` watches and sends although a clip tells when it shows; a new `UpdateManager.UpdatePlayerAnimation(AnimationClip.X, 0, effectInfo)` or another message for an effect the clip already implies; hooks on hero FSM states that only send a message (as `TauntVoice` did); the name `SuperJumpNeedles`.
- Exceptions: parts whose clip does not tell them still go by messages (8880b9b lists them): the taunt thread (silk), the dash burst, the air and wall dash and wall cling effects, the backflip puff, the hunter sprint attack burst, the wanderer recoil stab, the wall bump, the silk charge wall slam, the updraft puff, the holding-attack glows, the can-bind glow, the needolin parts, the amplify effect - both of its glows, the original and the copy `Amplify_Effect(Clone)` that the game plays every other song, with its fade-out sent as a change of its own (c353ab7) - and the tool stand-ins. The `TauntVoice` clip stays in the enum so later IDs keep their values.
- Source: hero-child-effects-sync.md, prefer-light-network.md; ae77e03, 8880b9b, e5623d5. USER 10-31 "总之超级跳只需要两个消息，蓄力和发射", "其他的动画都绑定在这两个消息上", "就不会有这种网络问题"; USER 11-01 "你去普查一下其他地方还有没有类似的，是分开发送的，修掉".

### D02 A character's clips are counted in every update
- Now: every PlayerUpdate says what each character plays (`ClipState`: count, clip, ms, lasting effect info). A clip change lost on the way is put right by the next update, and a stale one is dropped.
- Replaced: a one-off, uncounted animation message for each clip change, which left a figure stuck on the clip before a lost one (df63fa8 + review eee4649, 0.4.103).
- Old-way signs: `ClientUpdateManager.UpdatePlayerAnimation(clip, frame, effectInfo)` used for a clip of the character itself.
- Exceptions: tools, the bench (`AnimationClip.Bench`), sub-effects and `MinigameView` still use the uncounted path. Memory names them as the uncounted ones; it was not recorded whether that is on purpose (see notes).
- Source: clip-state-sync.md; df63fa8. USER 10-16 "直接把动作放在包里传输".

### D03 A player's own position is resent while they stand still
- Now: the local player's position is sent again every 250 ms while it does not change, so a game that missed the player entering the room still places them.
- Replaced: a position sent only on change, by the unreliable channel (0.4.122).
- Old-way signs: position sends gated only on "changed since last send" with no repeat.
- Exceptions: none known.
- Source: position-resend-when-still.md. USER 10-23 "她坐在椅子中间 / 坐在我的座位上".

### D04 A remote player's attacks use that player's crest and needle
- Now: copies of another player's attacks take crit, rage, combo, taunt and needle element from that player. The partner's crit and combo flashes are replayed. Nothing is read from the local hero.
- Replaced: `DoDamage` and the charged slash reading the LOCAL hero's crest and needle element for remote attacks (667c779, 0dd2633, 10-29/10-30).
- Old-way signs: `HeroController.instance`, local `PlayerData` crest or tool reads, or `HeroNailImbuement` of the local hero, in code that handles a remote attack or a copy of one.
- Exceptions: none known.
- Source: partner-crest-on-copies.md; 667c779, 0dd2633. USER 10-29 "朋友猎手纹章在我这里打出漫游者暴击", "那我触发暴击她那边能看到吗".

### D05 Each player wears their own skin on every screen
- Now: each other player's character, every copy of what they do, the room's stand-in figures, and a creature that holds them are dressed in that player's Customizer skin and Player Color cloak. This uses per-figure copies (`HeroCopies.Make`, `DressHeroCopy`). A chat line says what this game cannot show.
- Replaced: the local player's skin shown on everyone, and effect copies instantiated from the local hero's objects (about 25 sites in `Animation/Effects`) (a94dcfc, b70b442, 1f4b65c, 677c3c3, 10-23).
- Old-way signs: `Object.Instantiate` of a local hero child or of the local hero's tk2d library or collection for a remote figure without going through `HeroCopies`; caches keyed by material identity.
- Exceptions: room-sheet roles keep the room's own look (`SheetRole.Room`).
- Source: skin-mod-compat.md, MEMORY.md ("never cache by material identity"); a94dcfc, a9e9d6d, b70b442. USER 10-23 "各穿各的", "攻击之类的不可以联机另一边也看得到变化嘛", "做吧".

### D06 Only the hit streak of the partner's hits shows
- Now: when the partner is hit, the other screen shows only the white streak across them, at a third of its size, without the radial lines.
- Replaced: 09-23 no hit effect of the partner at all (1123438 removed `low health hit effect`), which replaced the copied hit effects before it. The rule was widened 10-25 (7b27381).
- Old-way signs: copies of `Effects/Damage Effect/*` or of radial hit lines on the partner figure.
- Exceptions: none known.
- Source: hero-child-effects-sync.md; 1123438, 7b27381. USER 09-23 "无论如何受击效果都不应该同步吧啊喂！"; USER 10-25 "对，不要周围那种射线，只要那个受击的一横条".

### D07 The partner's hero sounds are only the chosen ones
- Now: hero sounds are copied to the partner only where the user asked: the taunt voice, now said by its clips. Copies of hero effects come without sound, noise or shake, except effect objects that carry their own one-shot sound, like the taunt thread.
- Replaced: nothing (first rule).
- Old-way signs: `AudioSource` left on in a partner effect copy; a new hero sound sent as a message.
- Exceptions: one-shot sounds inside a copied effect object.
- Source: hero-child-effects-sync.md, hero-dust-sync.md. USER "怕所有声音都同步会太吵".

### D08 The partner's tools are copies that only show
- Now: four rules. (1) The copy of a tool looks and moves like the real one. (2) Damage counts once, in the owner's game, so the copy only shows. (3) Only the owner's attacks set a tool off: thrower copies cannot be hit, and a foreign tool's hit is dropped first (`IsThrowerCopy`, `IsForeignToolHit`). (4) A hit carries where the enemy was.
- Replaced: tools that were not synced at all, such as the partner's sting shard, which hit the boss unseen (0.4.41, 436239d framework v2).
- Also: what the hero's own code spawns as it is hurt with a tool on (the crystal of the memory tool, `HeroDamageSpawn`) is followed like a thrown tool; a copy takes its place and facing from the thrower only where `ToolCopyRules.TakesPlaceFromThrower` says so; a change of state that only the owner's game knows of in one state is held on the copy and made by itself after 1.5 s if the owner's change never comes (efdb9b1).
- Old-way signs: a tool copy that runs damage or hit code; a local hit on a remote tool counted; an action of a copy rewritten from "the hero" to the thrower for every copy.
- Exceptions: none known.
- Source: thrown-things.md; 436239d, 0d83bfc, e39f681, db7b214. USER 09-23 "道具碎片都同步一下吧，就按照你说的做".

### D09 The owner's game rolls the random parts of a skill or tool
- Now: the owner's game rolls offsets, angles, turns and waits, and the copy uses those numbers. Tacks and pins send where each piece went (7be1749); a throw whose effect carries no placements shows no copies and logs a line (11-01). Rune Rage names each cluster and both games roll by the name (`RollAs`, 0a96923), which is the separate rolling that D27 makes only the fallback: it goes over to D27's table when that is built.
- Replaced: copies rolling for themselves (`Animation/Effects/Tools/Tacks.cs` offsets, `Longpin.cs` angle, `SilkSkills/RuneRage.cs` rotation and waits), and the tack and pin copies' own roll as a fallback when no placement came (Tacks, Longpin, ThreefoldPin; removed 11-01, 0.4.143).
- Old-way signs: `Random.Range` / `UnityEngine.Random` inside a remote effect copy that decides where or when something goes.
- Exceptions: copy audio pitch (`EntityFsmActions.Audio.cs`) and per-frame jitter or flicker stay per game.
- Source: same-rule-audit-todo.md item 9; 7be1749, 0a96923. USER 10-22 "第二个要同步一下，其他两个算了，只影响音效或者背景板的问题很小". The fallback: asked after the first tidy (11-01), the user didn't follow the question ("这啥玩意？没听懂"), so it was settled by their own rule 谁扔的谁掷 and removed.

### D10 The partner figure stays on layer 0 with depth sorting
- Now: the partner's figure is on layer 0, so nothing in the room detects it. It is sorted by depth only (z 0.0041). The room's tint reaches it by events, not by copying every frame, and it hides wherever the game hides the hero. New figure problems extend this method.
- Replaced: per-frame tint copying (the first version of 452c2b3, which the user rejected); drawing order fixes outside the layer method.
- Old-way signs: moving the avatar to layer 9 to make something detect it; `LateUpdate` tint or colour copying; new sorting schemes for the figure or its name.
- Exceptions: none known.
- Source: partner-figure.md; 452c2b3, da2f919. USER "一定要每帧同步吗，场景同步做不了？", "延续之前修改人物图层的方法，而不是增加新的复杂方法".

### D11 A mark that tells players apart goes on your own things
- Now: when players' things look alike, mark this player's own name, map head or figure, on their machine only, in the own name colour (`PlayerManager.OwnNameColours`, `ModSettings.OwnNameColour`). Teammates are drawn as the game draws them.
- Replaced: a proposed ring around the teammates' map heads (10-11, rejected before it was built).
- Old-way signs: outlines, rings or colours put on remote players' figures, names or map heads.
- Exceptions: none known.
- Source: highlight-yourself.md, own-figure-outline.md, map-head-facts.md. USER "任何高亮肯定是高亮自己的头像，名字，怎么可能高亮队友呢".

### D12 A player who goes down lies in a cocoon until pulled up
- Now: a death that can be held lays the player down in a cocoon, and a teammate breaks it to pull them up at half health and 5 silk; each hit on the cocoon gives the one who struck it a silk, and the line that they are up is said once they stand (`RescueStoodUp`). The wait has no time limit: it ends when the cocoon is broken, when everyone is down, or by the co-op key (`CoopLeave`, K / L3). Each player sees and hits only their own cocoon. The game's death flow runs only when the death really happens. A boss that dies in the room stands a player lying in their cocoon up at once, at the blow that kills it, with the line that the fight is over: each game its own player, the game that runs the boss as it dies there and the others as its copy is told so (`HealthManagerComponent.Died`, `CoopSave.StandUpAsABossDies`; 35e381a); the end of a boss fight by its part that each game plays, its save having it beaten or its arena won stands them up as well.
- Replaced: running `HeroController.Die` and undoing it field by field (09-26/27; lie-down 318a4b0). The wait limit went from 45 s to 3 min (0a70ffb) to none (331d7fa, 10-22). Cocoons shown in both games went away 09-16. The `Low Health Leak` spray that upstream's `Death.cs` copied onto the figure was removed (e3caf65). A rescue gave 9 silk, and the line that the player was up came with the last hit (`CocoonBroken`). A boss that fell stood a player in a cocoon up only once it reached the part each game plays for its own player or its save had it beaten, for one boss over twenty seconds after the blow (until 35e381a, 0.4.150).
- Old-way signs: code that starts a game flow (death, respawn) and then puts its effects back by hand; rescue timeouts; copies of death effects onto the figure; a player left lying in a cocoon after the boss of the room died.
- Exceptions: none known.
- Source: rescue-cocoon-design.md, dont-run-then-undo.md, partner-cocoon-visual.md, partner-down-look.md; 318a4b0, 331d7fa, e3caf65. USER 09-27 "好麻烦，就不能没有真的死吗", "给一半血加上5格丝…打茧的人打一下可以获得一格丝"; USER 10-06 "救人那个能不能真的救起来了再发救起来了"; USER 10-22 "把死亡之后的观战那个三分钟cd自动回椅子删了"; USER 09-28 "下次遇到这种场景直接让她退出假死"; USER 11-05 "如果她躺在茧里我打死boss，她应该立刻被释放出来……之前在打黑寡妇的时候我们就定下这个规矩了" (35e381a).

### D13 A fight is lost only when every player is down
- Now: any fight (boss, arena, spar, or a fight whose loss is not lethal) is lost only when every player is down, and the room reacts to a defeat only then. `CoopSave.WrapDeath` lays non-lethal deaths down like lethal ones.
- Replaced: non-lethal deaths that played out at once, so a spar ended at the first knock-out (fixed 0.4.86).
- Old-way signs: a room or FSM reacting to one player's `HeroController` death or defeat event without the all-down check.
- Exceptions: none known.
- Source: only-both-down-counts.md. USER 10-10 "比试就按照所有boss战的标准来啊，早就说了只有两个人都死才在boss战里判定死了".

### D81 A player pulled up stands where they lay, or on the ground they last stood on; harm there is the game's
- Now: a player pulled up from their cocoon stands where they lie when that is on the ground out of water. One who went down in the air or in water stands up, and their cocoon lies, where they last stood on the ground in that room while that ground is still there (`CoopSave.WhereToStandUp`, `NoteGroundStoodOn`), else at the game's safe place (`hazardRespawnLocation`). Lava, spikes or the sand of creatures there are left to the game: the 2 s after standing up take no health (`SetTakeNoDamage`) and add nothing to the frost bar (`TickFrostEffect` held, `_rescueHoldsBuildup`, 789b0db), and the game puts a player who meets them back at its safe place.
- Replaced: standing up at the safe place when the spot was in or over lava, spikes, a drop or such sand (`IsInOrOverTheRoomsHarm` in `WhereToStandUp`, 0.4.42 to 0.4.148, kept beside the 2 s by mistake since 5eb1939); standing up where the body would land falling from the spot (`WhereTheBodyLands`, 3148c6c, 0.4.145 to 0.4.148).
- Old-way signs: a stand-up place chosen by looking for harm below or around it; a cast down from where a player went down to find where they land.
- Exceptions: a death by a grinder (`ByGrinder`) stands up at the safe place; in the lava chase a player stands up beside the partner (D51), whose place keeps its own harm check; a room's corpse markers still place the cocoon (`WhereTheCocoonLies`).
- Source: rescue-cocoon-design.md, revival-lava-floors.md, sand-centipede-rescue.md; 5eb1939, 3148c6c, faa337b. USER 10-18 "干脆直接改成像受击了一样给几秒无敌…就直接不扣血然后按照游戏本身的逻辑走"; USER 10-25 "我觉得就改成这两秒锁血也不导致任何积蓄条增加就行了"; USER 11-05 "岩浆尖刺那个判定不是取消了吗，改成2秒无敌了", "茧就不要摆在可以直接掉下去这么远的地方".

### D73 Under a skin, Player Color colours nothing
- Now: while a player wears a skin, Player Color colours nothing in their game and their cloak colour is not sent; a player whose skin pack dresses Hornet is drawn without colour on every other screen, whatever colour an older build sends (`SkinMods.KeepOffUnderSkins`, `PlayerManager.ShownColour`, `CloakColourPreview.ShownLook`). Choosing the game's own look again brings the colour back.
- Replaced: Player Color always on, which under a skin coloured only what the skin has no art for: the friend's poisoned spike ball drew red in her game and, as SSMP drew it in her colour, with white knots and dark blocks in the user's (438f4cc, 0.4.143).
- Old-way signs: a colour applied to, or sent for, a player who wears a skin; code that colours a copy by the cloak colour without asking whether the player wears a skin.
- Exceptions: none known.
- Source: skin-mod-compat.md, spike-ball-render-glitch.md; 438f4cc. USER 11-01 "我们都用其他皮肤了就不要启动这个player color, 不管选了什么颜色都不应该启动player color".

### D74 A rider's height above a moving lift comes from the rider's own game
- Now: on a moving lift each game sends how high its own hero stands above the lift (once when standing, about 20 times a second while jumping, once on landing, and a jump off mid-ride); the other games show the figure at that height on their lift (`CoopSave.Lifts`, `LiftReplay`, `CoopSaveUpdateKind.LiftRide`).
- Replaced: the figure pinned to the lift at the height it boarded, as positions lag behind a moving lift; a jump at the start pinned it in the air (8481299, 0.4.143).
- Old-way signs: a figure on a lift placed at a fixed offset taken once; a rider's height worked out in the watching game from late positions.
- Exceptions: none known.
- Source: lift-sync-facts.md; 8481299. USER 11-01 "对方的角色被定在电梯上的某个位置，可实际上人是能跳的".

### D84 A heal goes on through a roar or intro that takes the hero over
- Now: while a player binds, a roar or a fight's intro that takes the hero over waits until the heal is over: the hero's Roar and Wound States hold their "ROAR *" events, and an FSM CANCEL to the Bind FSM from an FSM outside the hero is dropped (`BindCoop`). The intro itself goes on, and what it does to the player comes after the heal.
- Replaced: a check of the `take_hero_control` template alone (37f8df2, 0.4.57), which let roars that take the hero over without it cut the heal short (f5d3829, 0.4.75).
- Old-way signs: a bind cancelled by a creature's or a room's FSM while `cState.isBinding`; an intro paused, or a take-over skipped, for a player who binds.
- Exceptions: inside a creature FSM's own take-over the template check stays (`BindCoop.IsTakeOver`); burst roars are dropped; HERO DAMAGED, LEAVING SCENE and LEVEL LOADED drop the held roars.
- Source: bind-and-roar-facts.md; 37f8df2, f5d3829. USER 09-28 "改吧" (the intro goes on and holds the player as usual, the heal finishes; pausing the intro was turned down, "开场归开场，我回我的"); USER 10-04 "在我敷丝的时候屏蔽接管是这么困难的事情吗", "会接管角色的战吼就是会打断敷丝".

### D86 The fight outline goes out only as the creatures that were hit die
- Now: the outline of a player's own hero is lit while another player's figure is on their screen and a creature that a player hit in the room is still alive and active. It goes out when those creatures are all dead or gone, or the room changes, however long it is since the last hit (`FightOutline.IsFightOn`).
- Replaced: 8 s after any last hit (c722004, 0.4.114); then 5 s without a hit on a creature that is no boss (`QuietTime`, df0eda9, with bosses let off in a549ef4), my addition beside the user's rule, removed 291b4b8 (0.4.150).
- Old-way signs: a time since the last hit, or any timer, that ends the fight outline; bosses told apart for it.
- Exceptions: none known.
- Source: own-figure-outline.md, highlight-yourself.md; c722004, df0eda9, a549ef4, 291b4b8. USER 10-20 "只有两个人在同一个屏幕里的时候，如果有人打怪了，那么亮", "感觉8秒后有点久了", "打死就灭"; USER 11-05 "删掉 5 秒，打死才灭".

## B. Creatures and their copies

### D14 One local-first mechanism: the copy runs its own FSM
- Now: a time-critical touch of something shared (hit, bump, strike) plays at once in the local game. The copy runs its own FSM for the input. Shared-state effects are intercepted by kind (health, death, counts, spawns, save flags) and done by the scene host. The host's echo is the acknowledgement (`Entity.RunHere.cs`, registry `local_first`, `TakeInput`).
- Replaced: per-thing ways of acting a touch out (`Entity.BounceHere.cs`, the bell whitelist `PlayStrikeHere`, a rock's `PlayStateHere` / `_playedHere` / `HoldStill`, OwnMotion strike numbers) and round-trip designs (d6d29b3, 0.4.45).
- Old-way signs: an allow-list of things that play locally; waiting for the host before showing a hit; separate echo kinds per thing.
- Exceptions: health and death are never settled locally (D15).
- Source: local-first-is-the-rule.md, copy-fsm-local-run.md; d6d29b3. USER 09-26 "为什么你这么不喜欢客户端本身先反应然后再传回主机的方式", "不是该怎么演，而是该拦截掉哪部分", "回声正好用来告诉客户端我照做了".

### D15 The game that runs a creature settles its health and death
- Now: the scene host tells absolute health, plus the damage it took in per copy (`HealthMessage` State). Copies send running totals of their own damage (OwnDamage) and show the told health. A copy dies only of the host's Death. The hitter's game works out the amount, and the host finds the killing blow and tells its tool drop to every game.
- Replaced: health deltas (prev, new, epoch) that drifted apart for good (before 2b65f7d, 0.4.86); the local-first kill aa3780a (0.4.94, reverted in 0.4.95); the client-side prediction `BeforeOwnBlow` of 9b6fb66 (replaced by 597369c).
- Old-way signs: a copy setting its own HP from a local blow or calling `Die` itself; health sent as a change rather than a value; names `CanDieHere`, `DieHere`, `_killedHere`, `BeforeOwnBlow`.
- Exceptions: none known.
- Source: health-ledger.md, local-first-is-the-rule.md, desync-is-a-bug.md; 2b65f7d, 597369c, 9b6fb66. USER 10-10 "你到底怎么定义小问题的，血量对不上那打boss之类的不就误差越来越大了吗！"; USER 10-11 "我的重点在于伤害也不应该客机结算".

### D16 The hitter's game sends what it hit; the partner's attack copy only shows
- Now: for anything the partner can hit, the partner's game sends what it struck or touched (the thing's answer) and this game replays it. The swing copy (`RemoteAttackComponent`) touches nothing. A thing with health has one authoritative copy (an entity run by the scene host), and the other game shows only its hit effects. A strike's range questions ask about the striker alone, and players count in a range by their body only (c52f61f). Similar things use one mechanism.
- Replaced: 14 whitelisted replayed types plus the swing copy for everything else (c67955f, 0.4.43). Replaying hits on each game's copy of a thing with health was rejected (09-27). Answering strike ranges from the local player was dropped in 0.4.138.
- Old-way signs: a copy's collider or trigger reacting to the partner's swing; per-type send lists; "is the player near" answered with the local hero during a replay.
- Exceptions: `ReplayedWhenSpawnedTypes` (the 14 old types, still replayed when spawned); personal types (D45, `CoopHits.PersonalTypes`). Outside a checked co-op save (`CoopHits.HasMembers` false), and inside one for a creature with health that is not an entity, the copy of the partner's swing still hits in this game, as upstream did: `DamageAnimationEffect.FixRemoteAttack` makes it non-lethal and puts the health back (`RemoteVisualHitHpBefore`), and `CoopHits.OnNailHitEnemy` keeps it from paying the local player (`HitContext.Remote`). It is the only way the partner's hits show there, because the hitter-sends path (`SendHitEffect`, the room-object replays) runs only for checked members, so it is not a leftover to delete (tidy 2026-11-01).
- Source: hit-sync.md, partner-break-follow.md; c67955f, c52f61f, 7cf4453. USER 09-25 "看着很不舒服，这些东西明明差不多，有的用回放，有的用复制品砍到", "其实最不会丢包和丢受击的方法就是打击事件传回，然后复制品只放动画吧"; USER 09-27 "命中不要通过复制体重放来达成".

### D17 A replay never acts on the local player
- Now: replayed creature actions aimed at "the player" (grab, wound pose, FSM cancel, hide, freeze, bounce, recoil, damage, silk, money, dialogue) never reach the local player's character.
- Replaced: replays that hit the scene client's own character (filtered in c41a7f5, 0.4.38).
- Old-way signs: a new replay path that resolves the hero through `HeroController.instance` or the global var Hero with no hero guard.
- Exceptions: the copy's real attacks still hurt the local player who stands in them (local-first, D14). A creature that a talk or a challenge spot's intro hands the local player over to (`Entity.HandOver`: `HandOverTheLocalPlayer`, and since 902f5f1 `HandOverTheLocalPlayerTo`) plays its replays about "the player" on the local player until it gives them back, or `WatchHandOver` does after `HandOverGiveBackTime` (15 s): the local player is the one it talks to or holds (tidy 2026-11-01).
- Source: replays-hit-the-local-player.md, hit-sync.md; c41a7f5.

### D18 Things that count hits count every player's hits
- Now: hit counters (not health) count both players' hits on the scene host (`HitCounters.Count`). A wound reaction plays in the hitter's game (`hitter_game_fsms`), and listed FSMs run in each game (`each_game_fsms`).
- Replaced: `own_player_fsms` (became `each_game_fsms`, f1b6e8a); 31318c9 "tell every FSM the partner's hit" (reverted in 2c53ec5); `LocalPlayerFsmNames`.
- Old-way signs: names `own_player_fsms` and `LocalPlayerFsmNames`; telling every FSM of a creature about the partner's hit.
- Exceptions: none known.
- Source: hit-counters.md, overgeneralized-fix.md; 380e91f, f1b6e8a, 2c53ec5, a2fe882. USER 09-27 "计数器类"; USER 09-30 "挨打反应就谁打在谁那边掉，不能加在本地先运行那边吗".

### D19 One rule for whom every creature goes after
- Now: `KeptTarget`. A creature goes after the nearest player it notices first and holds them for 4 s. After that it rolls at each move start, or every second if it picks no moves. Its ranges, its sight, walkers (`MoverTarget`) and aims (`LookAsItAims`) all ask the same rule. A creature that answers a wound keeps the striker until its next move. A thing with no health that watches for players with ranges of its own and sends creatures out at the one it finds, such as the spawner in the maggot water registered with the creatures it sends, goes by the same rule as it aims, the players inside its ranges being the ones within its reach (`SendsFromElsewhere`, `IsWithinReachOf`; b1bd81a).
- Replaced: a roll only for move-pickers (d321123, `TurnAtMoveStart`); alert ranges that turned to a player a fifth nearer after a 3 s lock (`ShouldSwitchApprovedTarget`, `EnemyTargetLocks`); a `LineOfSightDetector` that dropped and re-took targets; creatures with no range that went after the host's own player (8ae85fd, 0.4.123); such a spawner sending its creatures at the host's own player alone (until b1bd81a, 0.4.150).
- Old-way signs: target choice inside one detection class only; `HeroController.instance` as a creature's target; names `ShouldSwitchApprovedTarget` and `EnemyTargetLocks`.
- Exceptions: none known.
- Source: enemy-target-randomness.md; d321123, 8ae85fd, b1bd81a. USER 10-15 "怪物的锁敌其实可以带一点随机性"; USER 10-23 "就不能所有怪都统一成一版吗"; USER 11-05 "那个会从蛆水里面跳出来咬人的虫，我这边好像比我朋友那边少很多".

### D20 Aims and chases point at the creature's target
- Now: actions with which a creature looks at, chases or measures a player point at the player it goes after. This covers the `targeted_fsm` / `targeted_where_player` registry kinds, rewritten only where the field holds a player now, and decided again each time (`PointedAims`). Things a creature sets off take its target.
- Replaced: untargeted actions that read the scene host's own var Hero (b680870 + review a6d0ef8, 10-18; aim markers ab47607).
- Old-way signs: a targeted action type missing from the registry lists; a field pointed at a player for good.
- Exceptions: camera things are never retargeted.
- Source: aim-actions-follow-target.md, random-sync.md; b680870, a6d0ef8, ab47607.

### D21 A creature answers a wound at the player who dealt it
- Now: in the game that runs it, a wounded creature answers at the striker, whichever player it fights, and goes back as its next move starts (`AnswerWound`). The copy in the striker's game answers with the room creature's body, which walls stop.
- Replaced: acec6b6, which kept the copy from playing the wound past where the creature looked (cddbfea + eaad26f, 10-22/23).
- Old-way signs: wound reactions aimed at var Hero of the host; cutting the client's reaction short.
- Exceptions: none known.
- Source: wound-answer-at-striker.md; cddbfea, eaad26f. USER 10-22 "主机那边也应该朝打它的那个人那里飞".

### D22 Knockback is as far and as fast as in the game itself
- Now: each blow pushes as in vanilla, never scaled by health or player count. While waiting for the host to take a knockback in, the copy goes on with the creature's own motion (the host reports its push per position stamp, then Rebase at release, 71483e3). `SetRecoilBlocked` and `SetRecoilSpeed` are replayed on copies (69fc637).
- Replaced: a hold of `AverageRtt + 70 ms`, then the time-guess hold (`PredictedRecoils`, `MinHoldUntil`), then the direction gate (e2a66c5); a copy that stood still in the wait.
- Old-way signs: names `PredictedRecoil(s)` and `MinHoldUntil`; knockback tuned by health split or number of players.
- Exceptions: none known.
- Source: knockback-wait-own-motion.md, hit-sync.md; 71483e3, 69fc637. USER 10-22 "不要，有病啊，击退距离一样手感才是一样的，和怪血量有什么关系".

### D23 A move that goes the same way in both games plays on the copy by itself
- Now: the copy plays such a move with its own body from where the host's FSM entered it (registry `runs_here`: 11 boss moves, 21 stuns, the lava burst), instead of following the host's positions. A stun lets the copy bounce by itself. The approved plan widens this to the moves of common creatures, most-met first.
- Replaced: following streamed positions through the move (836699d, c289a8c, a8162ec, 10-17 to 10-19).
- Old-way signs: a deterministic move followed by position snaps; a creature in the audit list `moves_left.tsv` (about 700 moves) still on positions.
- Exceptions: creature AI movement that reacts to players every frame still streams positions.
- Source: move-runs.md, same-rule-audit-todo.md item 1, prefer-light-network.md; 836699d, c289a8c. USER 10-17 "尽量不要用位置同步了", "单次确定性招式一次都改了"; USER 10-22 "慢慢做，记一下后面都做".

### D24 The network carries the decision; a thing that is set off moves by itself
- Now: when a thing's outcome is fixed once it is set off, the set-off is sent once and both games run the flight (`OwnMotionComponent` v2: position, angle, velocity, spin, body type, gravity). Per-step actions run on the copy.
- Replaced: streamed positions for falling rocks and similar things (e702e30, then no positions while flying dfa6ca2, v2 0074a1d).
- Old-way signs: per-frame position sends for something that has no AI after it is set off.
- Exceptions: hanging boulders were reverted to the old way (c5383ae).
- Source: prefer-light-network.md, random-sync.md; e702e30, dfa6ca2, 0074a1d. USER 09-25 "我希望通信轻一点", "只要把随机数部分控制好了，函数照样跑", "随机数与其他部分解耦".

### D25 One game rolls; the others use its numbers
- Now: a shared roll is made in one game and the copies use its numbers. The scene host rolls for what it runs. For things a player walks into or hits, the game that set it off rolls (`SharedDice`, which records `Random` per rolling action and sends it with the message). This covers the act 3 creature buff (`BlackThreadComponent`, 79634f2) and the world rumble (`CoopSave.WorldRumbles`, c1867de).
- Replaced: each game rolling its own (traps, room machines, act 3 buff and rumble until 10-31).
- Old-way signs: `Random.Range` / `Random.value` in a copy or client path that changes what both see; vanilla managers left rolling in every game (`WorldRumbleManager`-like loops).
- Exceptions: cosmetic per-game rolls the user allowed (D09 exceptions; the flare flash rotation "要么同步，要么客机自己摆"). The spin direction of a falling spawn jar's copy (`SpawnJarComponent`, `Random.Range(0, 2)` for ±300 angular velocity) is rolled in each game: it is cosmetic like the flare rotation and decides nothing, as the jar breaks at `breakY` either way (tidy 2026-11-01).
- Source: random-sync.md, dice-vs-result-rule.md, black-thread-roll-desync.md, world-rumble-facts.md; 6d4d484, 79634f2, c1867de. USER 09-26 "谁触发谁掷"; USER 10-13 "要么就房主投骰子，客机只同步骰子骰了啥".

### D26 Dice that feed a list or placed things: send the result
- Now: where a roll goes straight into an action, the dice may be shared. Where it goes into a list the client works on (a shuffle), or into placed things whose place depends on history, the result is sent: the order (`SharedDice` Order records, 5ea78bc), or each part as it goes up (`CoopSave.ShownParts`, host runs vanilla and the client is held, 1fd75e8 + a8cd564).
- Replaced: following the controller states with the host's dice (before 1fd75e8); "put both lists in the same order, then shuffle with the same dice" (6f8cbde, withdrawn 10-13).
- Old-way signs: sorting a list into a canonical order before a shared shuffle; a client controller that runs its own placement steps from shared dice.
- Exceptions: words that come late keep the host's spacing; things long over are dropped (except putting things out).
- Source: dice-vs-result-rule.md, dancer-controller-follow.md, spike-ball-controller-sync.md; 5ea78bc, 1fd75e8, 6f8cbde. USER 10-13 "为啥一定要用同一组骰子，烟花的位置不能直接拿到吗", "但是这样为什么不直接同步列表的结果，而是要同步骰子和列表？".

### D27 A named thing carries a table of pre-rolled seeds (decided, not built)
- Now: when a game names a thing (a throw, a trap, a hit or touch on a room object, a machine round, a shown or told part, an `ObjectBounce` of a thrown thing), it pre-rolls with its own dice a seed for the first 4 uses of each rolling action and the first 8 bounces. The seeds go in the same message, and both games use them. Past the table, or for a roll nobody foresaw, both fall back to the named seed.
- Replaced: named rolls that each game computes from the name, the action and a count (flock flyers 09-27, thrown things 10-13, then shown and told parts, machine rounds, traps and hit room objects).
- Old-way signs: `SharedDice.RollByName` (and its 6 users) and `SharedDice.RollAs` (the clusters of Rune Rage, 0a96923, built the evening of the ruling the old way) as the only source of a named thing's rolls.
- Exceptions: flock flyers, decided at load before any word, use the fallback only.
- Source: same-rule-audit-todo.md item 8 (no commit found that builds it). USER 10-22 "不可以分开掷骰子…一次性把能投的都投了，但是同时用同一个种子的骰子兜底".

### D28 What creatures throw is a named twin in each game
- Now: things creatures throw or spawn on a clock exist as twins in every game under the same name (`ThrownThings`). They fly by themselves, roll by that name, and take strikes, touches and hurts sent under it (b338edd). Timed spawners roll from a seed the runner sends as the state begins (b0b3182).
- Replaced: entities moved by positions (the grass ball, until f52f8cd); throws that each game rolled; timed spawns that were not replayed at all.
- Old-way signs: special cases in `EntitySpawner.CanSpawn` / `IsSpawnedAsEntity` / `SpawnEntityGameObject` or `EntityInitializer.RemoveClientTypes` for one projectile; an `EntityType` for a projectile.
- Exceptions: spawn-at-player things (thief rosaries, slashes spawned at the hero) stay in their own game.
- Source: thrown-things.md, same-rule-audit-todo.md items 2 and 7; b338edd, f52f8cd, b0b3182. USER 10-22 "改吧改吧".

### D29 The room's runner makes summoned and room-made creatures, on both games
- Now: a creature that a creature or the room makes is made by the scene host and becomes an entity on both games. It is made where the runner's game made it (d4b1dd8). Non-hosts register nothing spawned. What a creature grows onto itself is sent again to a player who walks in (993d1e5).
- Replaced: summons on the host only (before bd66d3c, 09-23); summons placed beside the first summoner of their kind in this game (before d4b1dd8, 10-30).
- Old-way signs: a non-host game registering or placing a spawned creature by itself.
- Exceptions: the hatchling is still one per game (told to the user); corpse let-outs follow af0858b.
- Source: summoned-creature-sync.md, room-creature-sync.md, grown-parts-resend.md; bd66d3c, d4b1dd8, 993d1e5, af0858b. USER 09-23 "做成两边都有吧".

### D30 Fix the class from the game's own data, not a list of one creature
- Now: a broken mechanism is fixed for every object it breaks, found from the game's data: parts from each creature's FSMs (`PartsComponent.FindParts`), self-coloured sprites found from FSMs (2ee17e9), a layer for every entity (`LayerComponent`, c2cfb9e), every continuing action at takeover from a full list (8e1ff0b), and let-go parts named by place (`LetGoParts`, a409113). The rule is stated with how many it covers.
- Replaced: registry `shared_parts` for one reed creature (ae4e543, replaced 0.4.84 by fc384b3); a crow-only colour list; a hand-made takeover action list; `LayerComponent` only for creatures with a root collider at setup; let-go parts matched by root index.
- Old-way signs: registry entries or C# lists that name one creature for a behaviour the game data shows; `if (name == ...)` special cases in generic paths.
- Exceptions: the registry still lists what cannot be found from data (`runs_here`, `each_game_fsms`, targeted kinds).
- Source: fix-the-mechanism-not-one-object.md, overgeneralized-fix.md, reed-cobweb-shared-parts.md, takeover.md, layer-for-every-creature.md, takeover-action-list.md, let-go-parts.md. USER 10-08 "我不喜欢你这个修bug的方式，一下子修出来两处只有一个这么判断的情况"; USER 10-24 "不是，又特殊优化一个怪？那其他这种情况怎么办呢".

### D31 Only Entity.UpdateIsActive switches a copy's body on or off
- Now: whether a copy is there follows what the scene host says of the whole creature, through `Entity.UpdateIsActive`. The server's kept on/off is sent again at handovers (`SendStateAgain`).
- Replaced: components that set the copy's root from a child's `activeSelf` (`DamageHeroComponent`, until 367a2ac).
- Old-way signs: `gameObject.SetActive` on a copy root outside `UpdateIsActive`.
- Exceptions: none known.
- Source: walk-in-copies-under-off-group.md, server-kept-entity-active.md; 367a2ac, 550e323.

### D82 The game expected to run a room keeps its creatures running
- Now: while a room settles who runs it, the game expected to run it (`EntityManager.RunsTheRoom`: the scene host, or before the server has said, the game that came into the room with nobody else in it) keeps its creatures running (`EntityProcessor.KeepsRunning` into `Entity(keepsRunning)`): no switch-off in the constructor, no `HideTheRoomsOwnCopy`, and no switch-on with a position save in `InitializeHost`.
- Replaced: every creature switched off while the room settled and on again in `InitializeHost`, which started its FSMs over, so a creature that had hidden itself lost the one state that gave its body back; `RememberWhereTheHostFsmsAre` / `PutHostFsmsBackWhereTheyWere` (e11660b, 0.4.30) and a position save repaired it, until 6e0ff59 (0.4.51).
- Old-way signs: a new `Object.Host.SetActive(false)` on the side that runs the room during the wait; FSMs put back by hand after a switch-on of the mod's own.
- Exceptions: when the guess was wrong, `InitializeClient` switches the creatures off the old way, with the repair.
- Source: fsm-restart-loses-the-body.md, dont-run-then-undo.md; e11660b, 6e0ff59. USER 09-27 "就不能不关吗", "为啥不是谁先进房间归谁", then "行" to this.

### D32 A creature's clip start is sent once, the reliable way
- Now: the clip a creature starts goes once in `ReliableEntityUpdate`, and a resent or overtaken clip is told apart by the packet it came in.
- Replaced: unreliable sends with resends at 0.1/0.3/0.7 s (0.4.88), which played a finished clip again (a31307b, 10-19).
- Old-way signs: timed resend of clip data; clip starts in the unreliable part of the update.
- Exceptions: position and scale may still be repeated (creature-facing-delivery.md); facing rides with every position.
- Source: clip-resend-double-play.md, creature-facing-delivery.md; a31307b. USER 10-19 chose A.

### D33 A walk-in still replays the deaths of the room's creatures
- Now: a player who walks into a room where the partner stayed sees each dead creature die again on its copy, money drop included. A room that emptied reloads with no corpses. A game that takes a room over keeps the deaths (`TakeOverDead`, b84c780).
- Replaced: nothing. The plan to replace the replay with `SetIsDead`, a Corpse message and stand-ins was dropped before any code was written.
- Old-way signs: none. Do not remove the replay in `HealthManagerComponent.UpdateHealth` (alreadyInSceneUpdate && _toldDead).
- Exceptions: this is the kept exception itself.
- Source: walk-in-death-replay-kept.md, creature-death-records.md; d80c45c, b84c780. USER 10-12 "既然空过，两边都没有尸体是对的的话，那不如保持现状？".

### D34 Enemy health comes from the server host's setting, by dividing damage
- Now: the server host picks one of four health modes (`ServerSettings.EnemyHealthMode`: co-op default, power, sum, fixed). It works by dividing the damage dealt, with the remainder carried.
- Replaced: the single co-op default (74ff852, 10-19).
- Old-way signs: health multipliers written onto `HealthManager.hp`; a client deciding the factor.
- Exceptions: none known.
- Source: enemy-health-setting.md; 74ff852. USER 10-19 "房主能改吧".

### D35 The caught player's game leads a catch
- Now: a creature part that grabs or catches the local player starts a catch that the caught player's game leads. The scene host follows state by state without its own player. Combos on the caught client play first. While the game holds a player, their figure hides on other screens.
- Replaced: host-run grabs that the caught player saw late or never (f4f98b8 + 9a8af01, 3131f68, e9bebf3, 00870ea, 51889f9).
- Old-way signs: a grab or catch FSM state replayed onto the local player from the host.
- Exceptions: none known.
- Source: catches-and-grabs.md; f4f98b8, 3131f68, e9bebf3, 00870ea, 51889f9. USER 09-30 "藤蔓抓人也去做…统一适配", "两个都做", "为啥会有两个人影…改吧".

### D75 A tag of damage shows on every screen; its health goes only where it was put on
- Now: a tag (poison, zap, the dust of a bomb) that a player puts on a creature is sent to the members in the room (`CoopHitKind.EnemyTag`, a stack that is on already at most every 0.5 s) and shown on their own of the creature with ticks that take no health (`HealthManagerComponent.ShowTag`, the `ApplyTagDamage` hook); the game that runs the creature lets it hear of each shown tick as of its own (TOOK TAG DAMAGE, TOOK DAMAGE, SING DURATION END); the health of the ticks is counted as a blow of the player who put it on, in their game.
- Replaced: tags only in the game of the player whose tool or needle it came from, so the other screen saw health go and nothing else (d605a75, 0.4.143).
- Old-way signs: a tick on a copy or a shown stack that takes health; a tag effect that exists in one game only.
- Exceptions: when both players put the same tag on, each game's own stack ticks (as before).
- Source: d605a75. USER 11-01 "她用带毒的工具，实际上我这边似乎连毒的效果都看不到，就是怪物被毒的效果".

### D78 A creature's FSM numbers go with each move, and in between only what the other game reads
- Now: the floats, vector2s and vector3s of a creature's host FSM go as the FSM goes into a state and ahead of each replay of its actions (`Entity.SendStateChange`, `AddVariableChanges` with all, `VariablesOwed`). In between go only the numbers that the other game reads there: those of the replayed actions that still run on the scene host (`HookedEntityAction.Reads`), and those that the copy's own answer to a wound reads (`WoundReadsOf`). Whole numbers, yes-or-no's and words still go as they change.
- Replaced: every number that changed, every frame, the reliable way, though the other game read almost none of them: heights, fall speeds, distances and stun timers that the FSM counts for its own next move, which goes once (until c364d88).
- Old-way signs: a creature's number sent every frame that only the scene host's FSM reads; a per-frame pass over a snapshot's floats that sends whatever changed.
- Exceptions: none known.
- Source: prefer-light-network.md; c364d88, 998729c (INSTALL). USER 11-03 "像织针这样本来就可以只发一次的，代码里还有没有每帧都发的", "这个数就是为了换招，而换招本身已经从房主那边传过来了所以不需要这个数".

### D79 A creature's turn goes by its rule; only an aim goes every frame, the newest only
- Now: the copy turns itself where the turn follows from what the other game has, and nothing of the turn goes meanwhile (`Entity.CopyTurnsItself`, `RotationComponent.TurnsOnTheCopy`): a replayed rule turn of the creature itself (Rotate every frame, RotateTo, AnimateRotationTo and V2, run on the copy by `RunMoment`), facing the way it is seen to go (FaceAngle, V2 and V3 every frame on a body, `FaceTheWayItIsSeenToGo`), a move that the copy plays by itself (D23), a body spinning by itself (D24). Any other turn: its first angle goes as data, each angle after it by the unreliable `EntityUpdateType.Rotation`, the newest only, and the angle it ends up at as data again, which the server keeps for walk-ins. Data and stream go in one order of the packets they were first sent in (`ReliableEntityUpdate.ReceivedSequence`; the reliable one wins a tie, being read after the other in a packet), on the server and in the other game.
- Replaced: every changed angle every frame as reliable data, where an angle sent again after newer ones turned the copy back (until 63899e5).
- Old-way signs: an angle, aim or facing sent every frame for a turn that a replayed action, the way the creature goes or a move the copy plays already decides; per-frame angles as reliable data; an angle taken without its packet order.
- Exceptions: a turn that aims at a player frame after frame still goes every frame, the newest only: the copy cannot reproduce it.
- Source: prefer-light-network.md; 63899e5. USER 11-03 "干啥每帧转啊", "不许再搞每帧转向这种屎山了", "不管实际负载怎么样都不应该逐帧发这种消息啊，这种稳定性太差了", "我建议你好好想想而不是突然就极端的和我说所有每帧同步都不需要保留", and "做吧" to the three kinds: a turn by a rule set at its start, one along its flight, an aim every frame.

### D80 A copy plays its move to its end, then what the scene host's creature did since, in order
- Now: once the scene host's FSM came out of a move that the copy plays by itself the way the move goes out - straight out, or on through states out of the move by events they raise themselves (`Entity.WayOutOfTheMove`, `GoesOnTo`) - the copy plays on to its own end of the move, and what the scene host sends of the creature from that word on waits with when it came (`Entity.KeepsForLater`, `_cameSince`). After the move it is taken in as far apart as it came, that much later (`PlayOnWhatCameSince`). The copy makes the lag up three times as fast while it stands still in a clip that goes round or is through (`StandsStill`), and takes it all in at once before anything the local player does to the creature (`PlayHere`, a local hit in `CoopHits`), before this game takes over the room, before its death, and once what it kept came more than 2 s ago (`CatchUpWithWhatCameSince`). Its health and the music of the room go as they come (`TakeInAsItComes`); the state of the room drops what was kept (`MakeWayForTheRoomState`).
- Replaced: a copy's move cut short when the scene host's FSM was heard a few states past its end, the copy put where the other creature was (a 16-unit jump, 11-03), and what came while a copy played on held and taken in at once at its end (`HoldsForTheMoveEnd`, until 6ca2953).
- Old-way signs: scene host data held for the end of a move and applied in one go; a way out of a move checked by one direct transition only; a copy's move cut by a state that its FSM comes to by itself.
- Exceptions: a state that a global transition goes to, or one reached by what told the FSM from outside, still takes the copy out of the move at once. Things that the room syncs on their own beside the creature - what it throws, parts that are entities of their own - are not delayed with it, and a creature that never stands still stays behind until the local player does something to it.
- Source: move-runs.md; 6ca2953, 1514cd2, 685b9d9 (INSTALL). USER 11-03 "这类客机先演的动作就应该演完接着演压下来的动作然后接着演主机新放回来的动作，不要屏蔽了"; the user chose (11-03) making the lag up while the creature stands still, and taking it all in at once when the local player hits it.

### D85 A copy told it died keeps nothing going from before the death
- Now: a replay that lasts until its state is left (particles, sounds, switches and colliders undone on exit, every-frame setters, delayed switches, singing, spawning over time) ends at once when the scene host's FSM has left that state already (`ActionInState.Register`), so nothing that lasts from before a creature's death goes on in its copy. A one-off replay that comes late still plays, as it is what the scene host's creature did.
- Replaced: the copy throwing away all but its death once told it died (`HealthManagerComponent.CopyToldItDied`, `EntityFsmActions.LeaveAllButHostState`, replays of other states dropped while `ToldDead`; 793a112), taken back the same day (352e1ab): it lost drops, let-outs and switch-ons the scene host really did, and the replays of the 15 kinds of creature whose FSM marks them dead and goes on acting (SetIsDead).
- Old-way signs: replays dropped because the copy was told it died; a lasting replay kept under a state that the scene host's FSM has left.
- Exceptions: none known.
- Source: copy-told-dead.md; 793a112, 352e1ab. USER 10-18 "死亡消息到就其他动作都扔了"; the narrowing to what that was for is mine, after the review, and the user was told.

## C. Rooms, the world and saves

### D36 A co-op save is a flag, aligned to the host's world at entry
- Now: a save is only marked as co-op. It needs at least 2 players, and the joiner's save takes the host's world as it loads. There are no member lists and no pairing. Where the two saves differ, the host's record wins.
- Replaced: the member-bound shared save with three-step pairing and a pairing key (coop-save-design.md, 9e79d96), then auto pairing (82b0ab3, 0.4.90, never published), both replaced by 63ccd82 (0.4.91). The marker kept the first member's key and name in `partnerKey` / `partnerName` for older builds, with `TakeOverPartner` and `pairedUtc`, until 5ad5f27 (0.4.150).
- Old-way signs: member lists kept beyond a session, "all members of the save", pairing words and keys (`CoopPair`, `RemoveLocalPairing`, `partnerKey`, `partnerName`, `TakeOverPartner`), tools that pair saves. The marker's `Members` is the roster of one session (`StartRoster`), not a pairing.
- Exceptions: `/coopsave off` makes a save normal again.
- Source: host-aligned-coop-save.md, auto-pairing.md, coop-save-design.md; 63ccd82, 82b0ab3. USER 10-11 "不要给存档限定成员，只限定这个存档是用来联机的，至少有两个人才能玩".

### D37 With more players, "both" means "all"
- Now: every two-player rule applies to all players of a session: everyone arrives, everyone agrees, everyone is down.
- Replaced: rules written for exactly one partner (3a807fd..478952c, 0.4.90).
- Old-way signs: a single `partner` field or id where a set of players is meant; "the other player" lookups that take the first one.
- Exceptions: the gaps that only 3+ players hit and that stay unless asked (same-rule-audit-todo.md: boss waits count all on the server, `ResetWishConfirm` on one member leaving).
- Source: more-players-survey.md. USER 10-11 "把双人改成多人吧，都沿用现在双人的规则".

### D38 Pickups and money are each player's own
- Now: every world pickup stays one copy per player. Only a character's first-talk gift goes to both, and a unique item that a one-off story moment uses up is taken from both. Money, enemy drops and floor items are per player.
- Replaced: shared single pickups for keys, unique items and relics (decided and reversed on 09-16).
- Old-way signs: hiding or giving a world pickup in the partner's game when one player takes it.
- Exceptions: generic keys that shops also sell (found: both get one; used: the lock opens for both).
- Source: story-items-research.md, coop-economy-save-proposal.md, wish-and-quest-sync.md. USER 09-16 "地上有的东西只要一个人捡就可以了，有的东西一个人捡另一个人拿不到这样的逻辑似乎有点奇怪".

### D39 A character leaves for good only once every member had its trigger
- Now: a leaving for good waits until every member's save has had what lets it leave (`HoldDepartures`, kept by name). A leaving already in the save when the saves were checked stays (`_departuresDone`).
- Replaced: leaving as soon as any one game let time pass (4c35dd7, 10-30); the earlier shared NPC rule "NPC两边一致" read from one game.
- Old-way signs: story departures taken from the host's flags or the first game's.
- Exceptions: departures already in the agreed save (348ec6c).
- Source: story-flags-facts.md; 4c35dd7, 348ec6c. USER 10-30 "联机应该看所有人的状态而不是房主的状态".

### D40 Wishes and shared prompts go through agreement by both
- Now: accepting a wish needs both players to reach it, including wishes a key dialogue begins without a prompt (`CoopSave.WishRead`). Shared yes/no prompts agree on two yeses at the same prompt key (`CoopSave.WishConfirm`). Anyone may open a wish board at any time. At a turn-in the first to arrive turns in, the partner is asked there, and each gets their own reward. A donation at a board asks every member in a box of ours wherever they are (WishConfirm kind "donate", `DonationHold` in `CoopSave.WishCopyCheck`; 11-01, 0.4.143), as using up what the story keeps already did (kind "item", the desk of the shared house among them); talking about a wish with a character and turning one in still need the members there. A box that has to wait to be shown logs why, and again when that changes.
- Replaced: the one-FSM special case `PartnerAccepts` (3e671fd, 0.4.83, replaced by 4b965c9); the board refusal "X 正在用这块板子" (removed 2350c64); a no shown when nobody gave one (acdee91); the partner's money and items kept in this game to judge a turn-in (`_partnerWishProgress` for what is used up, the CanComplete hook with `PartnerLacksCopy`, `NoticeMissingCopy`, `_localCopiesOnly`; USER 10-01 "为啥要存队友的资源状态啊", then asked at the turn-in); a donation that needed every member close by the board and asked their games what they carry (`CanDonateTogether` with `GetBoardAbsence`, the WishCopyCheck question for donations; until 11-01).
- Old-way signs: a wish or prompt branch for one quest or one FSM; refusals of one player because the other is using something; a donation or other shared spend refused because a member isn't close by.
- Exceptions: a player who comes to a board while a member's turn-in or donation is going on there opens it without turning in, and a donation of theirs waits until that is done (`StartBoardTalk`, `CanDonateTogether`), so that one turn-in at a board goes at a time. That is my addition: the user took away only the refusal to open (10-01).
- Source: wish-dialogue.md, dialogue-wish-copied-accept.md, shared-prompt-agreement.md, fix-the-mechanism-not-one-object.md; 4b965c9, acdee91, 79abcc9, 2350c64, 5a8f17a. USER 10-08 "如果那段对话说完游戏会让我接下一个祈愿，那为什么不按照接祈愿的流程来"; USER 10-01 "先把一个人看祈愿板另一个人看不了删了"; USER 11-01 "与角色聊愿望需要队友到，不然会错过对话，但是捐钱修建可以改成远程弹窗，交愿望……我觉得最好是还是要求到场".

### D41 Each player reads the story at their own pace
- Now: each player goes through dialogue and story at their own pace. Only the step that cannot happen twice waits for both.
- Replaced: the lockstep "both watch the story together", with lines copied to a frozen partner (f3edb47, e4e97c5).
- Old-way signs: a hold on the end of a dialogue until the other players finish reading. Its dead code (`SharedDialogue.HoldsEnd`, `CoopSave.HoldsSharedTalkEnd`, `TryHoldDialogueEnd`, `EndHeld`, `ReadingMessage`, `DialogueTimeout`, the held half of `ClearDialogues`) went in dba83cf and 24d1e54 (11-01).
- Exceptions: the boss entity's lines are copied to the reader (`BossRoomCoop.OnStartDialogue`); delivery talks (`CoopSave.IsSharedTalk`). `Waiting`/`Done` of `TryHoldDialogueEnd` still decide re-sharing to an entrant, so they must stay.
- Source: shared-dialogue-copy-status.md.

### D42 A boss fight starts only when everyone is inside
- Now: boss rooms, and the 9 boss arenas, start only once every player is in. Until then the boss gets no player signs at all: they are dropped, not held. Everyone finishes the dialogue first. Records and rewards count for all. Arena bosses raise `BossRoomCoop.BossFightStartedEvent` too (776fad8). `/giveup` only opens a stuck arena's gates.
- Replaced: "either player can go first" for boss rooms (09-14); holding a taunt and starting the fight from it later (before d41e904); `/giveup` for boss rooms and story scenes (before 947625d).
- Old-way signs: queues of player signs replayed into a boss FSM; a second start event besides `BossFightStartedEvent`.
- Exceptions: ordinary arenas follow D43.
- Source: boss-room-design.md, boss-arena-hold.md, same-rule-audit-todo.md item 5; d41e904, 93ec7ab, 776fad8, 947625d. USER 10-14 "只要两人不到齐，boss就收不到这些消息".

### D83 A member who drops out ends a boss fight at its door for everyone
- Now: a boss fight that begins with every player saves the game at its start and remembers the door each player came in through (`CoopSave.Checkpoint`, on `BossRoomCoop.BossFightStartedEvent`, raised by the boss arenas too since 776fad8). If a member drops out, every player leaves through that door and the boss waits as before a fight; the one who dropped is put at the door once they load again. The checkpoint ends with the win or when the players leave the room.
- Replaced: nothing that was built. The user turned down a boss that stops and can't be hurt mid-fight until the one who dropped comes back (09-15).
- Old-way signs: a boss or creature held, or made unable to be hurt, in the middle of a fight because a member is missing.
- Exceptions: the hold before a fight starts (`BossRoomCoop.Wait`, D42).
- Source: boss-disconnect-checkpoint.md; e6ddcfb, aec7b7e, 776fad8. USER 09-15 "就按这个方案，然后战死保持原版" (a death was then the game's own; the cocoon of D12 came later).

### D87 The end of a creature that takes a player's character is watched by the others, hidden
- Now: when an action of the FSM of a creature with no health left takes the local character's control (an end played with a parry and a last blow), that game tells the room (`BossRoomUpdateKind.EndingTook`). Every other player there who is not down or dead hides their own character with its name and holds it where it is (`BossRoomCoop.Endings`), until that character has control again (`EndingGaveBack`, sent from the RegainControl hook of `BossRoomCoop.Wait`), its player leaves the room, or the room changes. Meanwhile what creatures draw of a player anywhere in the room is drawn in the watched player's look, with no 2-unit reach (`HeldLook.EndingFigure`). Control taken for the watch is given back only if `HeroController.ControlVersion` still matches, as `MinigameCoop.LetHeroGo` does.
- Replaced: the other players' characters standing beside the end while their copy of the creature skipped what it does to "the player" (D17), and the stand-in drawn in the local player's look more than 2 units from the hidden player (6439982, 0.4.148). The end of a creature that took a player who lay down went unwatched (24e486c, mine), taken out as that player stands up at the blow that kills the boss (D12; 35e381a, USER 11-05 "而不是让我不藏").
- Old-way signs: a watcher left free and shown during such an end; hiding or holding a watcher in code for one boss; a stand-in of a watched end dressed by distance alone.
- Exceptions: a watcher who is down or dead is left as they are. The camera still follows the local player's own hidden character (not changed). Mine, not the user's: the end of the watch when the player leaves the room or the room changes, leaving a watcher down or dead alone, and the ControlVersion check.
- Source: 0598893 (SSMP 0.4.150). USER 11-05 "那个幽影最后的动画我只看到一半……只能看到我朋友在演，但是我在旁边站着", then chose "都看房主演" (offered as "整段都是房主的大黄蜂在演，前半段替身也画成她的样子；另一个人的大黄蜂先藏起来，演完再出来，只看不演"). I told the user it also applies to later bosses whose end takes a character the same way. USER 11-05 "如果她躺在茧里我打死boss，她应该立刻被释放出来，而不是让我不藏" (35e381a).

### D43 An arena one player won is won for both
- Now: a battle scene won by one player counts as won for every player, in the save and live in the room. The 7 boss-titled arenas count as bosses (D42).
- Replaced: the earlier default in arena-coop-design.md (one player's win was only theirs).
- Old-way signs: arena completion kept in one save only.
- Exceptions: the prison arena (`PERSONAL_ARENAS` slab_16) and `memory_*` arenas stay personal (clothes return).
- Source: arena-win-shared-rule.md; 93ec7ab. USER 10-02 "可是一个人打了就应该代表两个人都打了，不然两个人不就不是一个世界了吗", "按首领算".

### D44 Arrival and door scenes play for each player; meetings wait like bosses
- Now: one-time story meetings wait for both like boss rooms. Door, arrival and load-decided cutscenes play for each player on their own arrival ("各看各的"). A meeting spread over two rooms counts either trigger.
- Replaced: moments that ran only in the first arriver's game (0.4.79).
- Old-way signs: a cutscene skipped in the second game because its flag was copied.
- Exceptions: the menu style is per machine.
- Source: first-arrival-moments.md; 742afe7. USER 10-05 "剧情那个去改吧，改成首领一样的"; USER 10-06 "我记得有一类是各看各的".

### D45 Things made personal by choice show nothing of the partner's use
- Now: the things made per player by choice, so co-op is not too hard, show nothing of the partner's use: regrowing crumble floors, bounce pods and balloons, the lava-race bounce flies, peaks drifters, crystal drifters, float crystals, wish pustules, and minigame rounds. The list is per-player-by-choice-list.md, for a later key that turns their sync back on. The switch points are `PersonalPlaces.IsThing/Contains` and `CoopHits.IsPersonal`. Minigames start together and each player plays their own round.
- Replaced: the look sync of pods and balloons (`CoopHits.Bounces`, `CoopHitKind.BounceLook`, ec4f704, removed 2bc36ba 10-30); the death look of personal creatures (`DeathLook`, removed 0.4.131); shared crumble floors (`CoopSave.WorldTriggers` rows, removed 0.4.67); shared flea games (removed a55f9e9).
- Old-way signs: any partner-use look on a personal thing; the `BounceLook`/`DeathLook` handlers in `CoopHits.cs` and their enum values (see D56); the `CoopHits.Bounces.cs` comment that pods follow the crumble rule.
- Exceptions: `MinigameMirror` (a spectator view after the own round, kept); music-box bells are pods but shared (`CoopSave.PodSequences`); floors that stay broken stay shared; Crystal Drifter Giant stays shared.
- Source: per-player-by-choice-list.md, bounce-look-sync.md, crumble-platform-sync.md, flea-minigame-coop-design.md, bounce-flies-per-player.md, personal-creatures-peaks.md; 2bc36ba, a55f9e9. USER 10-30 "直接去掉样子同步，在任何地方都不要留样子同步但是各用各的了"; USER 10-01 "还是把会长回来的改成各管各的吧"; USER 10-22 "一起开始但是各玩各的".

### D46 Room machines and loops follow the scene host's clock
- Now: boxes, debris chutes, grinders, steam vents and saunas follow the scene host's rounds (`CoopSave.PacedMachines`). Rail cogs and other never-culled loops follow its clock (`CoopSave.RoomClocks`). The act 3 world rumble is host-paced. A catch or a death stops the machine in both games (grinders `MachineStop`/`MachineGo`, cogs `CogHold`). A hazard respawn does not reset them.
- Replaced: each game running its own machines and loops, with a reset on death (0.4.79, 0.4.96 1f6b77e, 2d3eb24, c1867de).
- Old-way signs: a room machine that runs freely in the client game; a hold done in the caught player's game only.
- Exceptions: none known.
- Source: paced-room-machines.md, room-clocks.md, world-rumble-facts.md, same-rule-audit-todo.md item 6; 1f6b77e, 4c57d07, 2d3eb24, c1867de. USER 10-06 "两个人不管谁死都让两边都停", "不建议保留死亡重置".

### D47 Plates are plates, things are things
- Now: every `PressurePlateBase` goes through one plate channel, from touch and leave, and a partner's plate stands on itself. Lifts hear only their own calls. Trap plates press in every game, with a stander rule (fd8e3fd + c0a6af0).
- Replaced: the TempGate-only filter and lifts coupled to their plates (45108c6, 09-27); a press sent only as the plate went down (`Activate`, played on the partner with `StartDrop(true)`, about 1 s late; 675224a), now sent the moment the plate reacts, with a word that takes it back if the partner steps off.
- Old-way signs: a mechanism read off its plate's state; plate handling inside lift code.
- Exceptions: none known.
- Source: pressure-plate-sync.md, lift-sync-facts.md; 45108c6, fd8e3fd. USER 09-27 "我不喜欢不统一的接口 … 踏板归踏板，物件归物件，两个都独立收发指令", "就不能压板响应的一瞬间就发送消息吗，如果队友后面下来了，再发送取消".

### D48 "Held while anyone holds it" is one helper (decided, not built)
- Now: door plates, trap plates, pull rings, hatches and toll benches should share one helper (hold, release, forget member, forget room, walk-in resend), and each object keeps its own apply callback.
- Replaced: five separate implementations. `OnPlayerEnterScene` resends only lasting triggers, so a walk-in never hears of a held plate or ring, and `OnPlayerLeaveScene` forgets rings and trap plates but not door plates.
- Old-way signs: per-kind held-member sets in the plate, ring, Trapdoor and `CoopSave.TollBenches` code.
- Exceptions: none known.
- Source: same-rule-audit-todo.md item 4 (approved by "慢慢做吧，记一下后面都做", 10-22).

### D49 The carriage sends only button changes (decided, not built)
- Now: the carriage driver should send only on a button change, a release or a stop, and each game runs the motion, as lifts send one move per ride. A message's age is one way: (sender RTT + own RTT) / 2000, as `RoomClocks` has it.
- Replaced: reliable `LiftDrive` every 1/15 s while driving (`ManualLift`, CoopSave carriage code); lifts and carriage ageing words by their own RTT/2000.
- Old-way signs: `LiftDrive` sent on a timer; `rtt / 2000` from one side only.
- Exceptions: `CoopHits` uses (partner + own) / 1000, a full round trip, on purpose (the copy lags one way and the input travels one way).
- Source: same-rule-audit-todo.md item 3.

### D50 Box passages turn to the side of whoever is put back
- Now: the dropping-box passages keep the order they are set for. When a player is put back at a safe point beside them, the order turns to the side they are put back on (v2, 565b5c4).
- Replaced: v1 (13b1387, 10-25), which dropped boxes for the player who was crossing, judged by the ranges at the ends.
- Old-way signs: crossing state or "player among the boxes" checks in `GamePatcher.BoxPassages.cs`.
- Exceptions: none known.
- Source: factory-box-passages.md; 13b1387, 565b5c4. USER 10-25 "只要有人掉血了回到安全点就把机关调到她的位置。这样虽然第一次不一定合理，但是后续都是合理的", "做吧".

### D51 In a lava chase, a player stands up beside the partner
- Now: a player who dies in the lava chase stands up beside the partner, on the newest place the partner stood on, with the lava set below it for every game. A player lying in a cocoon in that room stands up where they lie once the fight is won or the lava starts. The lava holds until 1 s after they can move.
- Replaced: waiting for the partner to stand 6 above the lava (before 6b9ed04); standing up beside a player 3 s after the lava started (0ae62c0, replaced by d90a23d).
- Old-way signs: a height margin above the lava; timers after the lava start in `CoopSave.LavaChase`.
- Exceptions: none known.
- Source: rising-lava-chase-facts.md; 6b9ed04, 0ae62c0, d90a23d, 07932e6. USER 10-23 (6b9ed04 body) and USER 10-21 (hold until 1 s after moving).

### D52 Zones near NPCs count every player
- Now: the zones by which an NPC shows, hides, wakes or waves count every player. A zone is left when the last player in it leaves (`GamePatcher.NpcZones.cs`).
- Replaced: zones that counted the local hero only (d4c9ff7, 10-23).
- Old-way signs: `TriggerEnterEvent` / `TrackTriggerObjects` checks on the hero alone for NPC presence.
- Exceptions: zones whose events lead to something personal to the player stay local.
- Source: npc-zones-every-player.md; d4c9ff7.

### D53 Anyone can do any interaction from their own game
- Now: talks, prompts, levers, pickups and triggers work from either player's game. When the thing runs in the scene host's game, the interaction runs in the local game on the copy (`Entity.TalkHere`), and only the outcome goes to the host.
- Replaced: a design where only the scene host's player could talk to a synced creature and the other was told so in chat (10-10, replaced before release).
- Old-way signs: "only the host can" refusals or chat lines; checks on `IsSceneHost` that gate a player's own interaction.
- Exceptions: none known.
- Source: both-players-can-interact.md, creature-talk-parts.md, desync-is-a-bug.md. USER 10-10 "凭什么只能由先进房的角色触发啊，另一个人就看不到了？".

### D54 The host's save goes to a joiner whatever the host is doing
- Now: the host's game sends its save to a joining player whatever it does, waiting only for a menu, a room change or a save being written. The joiner is told what keeps it from coming. A new save that joins starts with the opening of a new game. A joiner at the start of act 3 gets the save before the host wakes (62ba3c3).
- Replaced: waits that only a host in a room could end (240c039, 2833d06); placing a new joiner at the respawn place (6df90d0).
- Old-way signs: conditions on the host's state before sending the save, beyond `GetHostSaveWait`.
- Exceptions: none known.
- Source: host-save-wait-reasons.md; 240c039, 6df90d0, 62ba3c3.

### D55 The co-op save hold has no stop-waiting key
- Now: the hold shows only who is awaited. Reloading stops waiting for players who left (the roster is per session). `CoopLeave` (K / L3) is only for giving up a rescue wait.
- Replaced: the "不等了" key, which a host pressed by accident and turned their save normal (040a808, 0.4.100; `DropMissingMembers`, `TakeBackDropped`, `_droppedMembers`, `_leaveKeyHeld` deleted).
- Old-way signs: those names; a key in the save hold that changes the save.
- Exceptions: none known.
- Source: no-leave-key.md; 040a808, e38fdf9. USER 10-14 "能不能把不等了删掉，现在的情况根本不需要这个不等了".

### D72 The shared house has one set of furnishings and constructions for both
- Now: what the players' house has is the same in every save: the furnishings and paint one player buys (`CoopSave.SharedPurchases`) and what the desk puts together (`ConstructedMaterium`, `ConstructedFarsight`), as shared-world flags that a check keeps if either save has them (`OVERRIDES` and `HOUSE_RECORDS` in tools/coop_story_flags.py, SSMP/Resource/coop-story-flags.json).
- Replaced: furnishings bought per player (until 10-18), and desk constructions per player (personal-reward by the `Constructed\w+` name rule; until 11-01, 0.4.143), with which a member who agreed in the desk's box gave up their item and their house got nothing.
- Old-way signs: a flag of the house (`Belltown*` furnishing or paint, `Constructed*` of the desk) classed personal; a house thing bought or built in each save.
- Exceptions: none known.
- Source: house-furniture-shop.md, shared-prompt-agreement.md. USER 10-18 rule of one payer for the house; USER 11-01 "修建物质库之类的操作也需要另一个人到才能建" and the plan "一个人装好，两个人的钟居都算装好，跟家具一样" ("就这么改吧").

## D. Network, UI and the rest

### D56 Both games run the same build; no code for older versions
- Now: a joining game and its host tell each other their mod versions, and a join of another version is turned away with the reason (66a5731, 0.4.133). Every game in a session runs the same build.
- Replaced: staying readable by older builds: new enum kinds only appended at the end, old message kinds kept "so that what a game of those versions sends still reads" (steam-relay-transport.md wire rule).
- Old-way signs: comments like "Sent by 0.4.x only", "games of 0.4.x still send", "Kept so that what a game of those versions sends still reads" (about 44 lines in `*.cs`); handlers for kinds no build sends (`CoopHitKind.BounceLook`, `DeathLook` in `CoopHits.cs`).
- Exceptions: the version handshake itself (`Util/ModVersion`, lobby data) must read across versions. An enum value in the middle of a byte enum cannot be removed without renumbering the ones after it, so mark it unused rather than delete it.
- Source: 66a5731; the task text of the tidy workflow ("the mod refuses to connect two different ones"). The user did not state the consequence in words; this entry follows from the version check (see notes).

### D57 The Steam tab goes over Valve's relay network
- Now: Steam connections use `SteamNetworkingMessages` over the relay (`Networking/Transport/SteamRelay/`). On the relay, fragmentation is unreachable, because `MaxPacketSize` is the transport's own. The server reassembles each client's packets in its own buffer.
- Replaced: the legacy Steam P2P transport and MMS hole punching for the Steam tab (1a47ee1); headerless 1200-byte pieces reassembled by arrival order (b4d9347); one shared leftover buffer for all players (68a3f2c).
- Old-way signs: hardcoded 1200-byte packet sizes; shared reassembly state across clients.
- Exceptions: UDP, hole punch and Steam P2P keep 1200 and SSMP's own resending (`RequiresReliability`). `GetOrAddClient` still revives an unknown Steam ID on purpose.
- Source: steam-relay-transport.md, packet-fragmentation-seam.md, ssmp-shared-reassembly-bug.md; 1a47ee1, b4d9347, 68a3f2c.

### D58 A link ends only when the transport and the silence both say so
- Now: before dropping a quiet connection, the game asks whether the transport still has the session (`_transportSessionUp`). A host that Steam says left the lobby ends the game only after 3 s with nothing from the host. A closed session is not counted as leaving.
- Replaced: a drop after 5 s of silence (09c32b6); ending on Steam's lobby word alone (82fa0e4); session close counted as leaving (7f96237).
- Old-way signs: timeouts that read one signal only; a disconnect taken from a lobby callback in `SteamManager` alone.
- Exceptions: none known.
- Source: relay-connection-timeout.md, host-leave-disconnect.md; 09c32b6, 82fa0e4, 7f96237. USER 10-25 "这个steam连接好奇怪，明明刚才还有看到我朋友在动，下一秒就断开连接了".

### D76 A copy goes on the way it was going while the link is quiet
- Now: when no position of a copy (a creature or a player) has come and nothing at all has come on the link, it is the link that is quiet: the copy goes on at its last speed and direction, full for 0.25 s and easing to a stop by 0.4 s, at most about a body length, stopping at walls; when other things keep coming but not its position, it really stopped and holds its last place. A late position is pulled in slowly (`LinkWatch`, `PredictiveInterpolation`, log [Smoothing]).
- Replaced: a copy that stopped and stepped back as soon as two or three positions were late, then jumped on (c443536, 0.4.143).
- Old-way signs: a copy stopped, or snapped back, on a late position without asking whether the link is quiet.
- Exceptions: a quiet spell longer than 0.4 s still stops the copy; the partner's game freezing is not seen as a quiet link.
- Source: c443536. USER 11-01 "丢包的时候怪一停一停的", "想让没收到位置的时候怪也能朝比较可能的方向移动".

### D59 No Steam queries per frame, and the game pumps Steam's callbacks
- Now: Steam networking queries are read on the send loop and cached (`GetSessionConnectionInfo` / round trip, twice a second). The game pumps Steam's callbacks, and the mod's timer (`PumpGate`) stands in only after 1 s without a game pump.
- Replaced: `AverageRtt` read per copy per frame (581445d); the mod's own timer pumping callbacks alongside the game's (33ef6a6).
- Old-way signs: `SteamNetworking*` calls in Update or per-entity code; `RunCallbacks` outside `PumpGate`.
- Exceptions: none known.
- Source: per-frame-steam-calls.md, steam-callback-pump.md; 581445d, 33ef6a6. USER chose "游戏为主，mod补位".

### D60 Every host opens the same waiting room
- Now: lobby-ID and direct-IP hosts open the same waiting room as a Steam lobby, and everyone says they are ready before a save is chosen.
- Replaced: the original author's flow, which went straight into the save menu for those two (b335eca, 0.4.130).
- Old-way signs: a connect path that opens the save menu without the room.
- Exceptions: none known.
- Source: waiting-room-every-host.md; b335eca, 1940ca4. USER 10-25 "直连这部分是不是也没有等待房间之类的？用的还是原作者视觉效果不太好的那套", "按1做".

### D61 The log writes one line per co-op event, with the time
- Now: co-op features log each event as it happens: each strike, what it struck, who, where, and whether each game counted it (`CoopHits.NoteTraffic`, `[Strikes]`). Every line SSMP writes starts with the time to the millisecond. Only per-frame things are throttled.
- Replaced: per-hit lines replaced by printing only when a state check finds a lasting difference (c28d202, 09-25); lines without times (before 3d270a3).
- Old-way signs: events kept in memory and printed only on a difference; new log lines that bypass the timed logger.
- Exceptions: per-frame events are throttled.
- Source: log-every-event.md, boss-idle-watch.md; c28d202, c52f61f, 3d270a3. USER 10-30 "日志还是回到每刀劈在什么上面都记比较好"; USER 10-21 "为啥不打时间戳".

### D62 Every text a player reads is bilingual through Lang.Pick
- Now: all the mod's player-facing text goes through `SSMP/Util/Lang.cs` (`Lang.Pick`), with Chinese for a Chinese game, and the UI font picks a Chinese face for it.
- Replaced: English-only texts by an earlier decision (until 09-16).
- Old-way signs: `AddMessage(` or UI labels with a literal and no `Lang.Pick`.
- Exceptions: usernames above heads use `TrajanPro-Bold SDF` (both players use English names, decided 09-17). The replies of the server's admin commands (`SSMP/Game/Command/Server/*`) stay English only (USER 11-01 "只回英文就行", asked after the first tidy).
- Source: chinese-ui-lang.md, coop-save-design.md; 8614b07, 05271ec.

### D63 Player text says what a thing comes to, never how, and names no story
- Now: in-game notes and messages say what a setting or event comes to, not how it is implemented. Chinese text names no bosses, areas, characters or story; words like 队友, 怪, 房间, 椅子 and 梦境 are fine.
- Replaced: notes that explained the mechanism (10-20).
- Old-way signs: Chinese strings naming implementation (消息, 同步方式, 主机判定...) or a boss, area or character.
- Exceptions: internal log lines and comments may name classes and scenes.
- Source: ui-text-outcome-only.md, no-spoilers.md. USER 10-20 "不需要写做法".

### D64 A full settings page scrolls instead of getting a new tab
- Now: when a settings page is too full, it scrolls down, with a scroll bar at its right.
- Replaced: a fourth tab for the cloak colour (f3e7599, 10-24).
- Old-way signs: new tabs added for overflow; fixed-height pages that clip.
- Exceptions: none known.
- Source: ui-scroll-not-tabs.md; f3e7599. USER 10-24 "放不下就滚动向下呐".

### D65 Everything in the mod works with a gamepad
- Now: every control of the mod's menus can be reached and used with a gamepad (navigation linked by hand where Unity's choice fails, strips that slide with the stick). The selection frame shows only while the game takes the player to be on a gamepad. A key that a pad cannot press does not count as a way to do something, and every new key is checked against the game's keys and the pad.
- Replaced: keyboard-only menus (09-17 "只有键盘能进存档"); a frame shown whenever a pad was plugged in (cc2f395); colour strips that stepped at the menu's repeat pace (18b9f3e).
- Old-way signs: actions bound only to keyboard keys; text boxes as the only way to set something; `Input.GetKey` for mod features.
- Exceptions: typing in chat needs a keyboard (chat read-only on a pad).
- Source: user-plays-with-gamepad.md, input-and-gamepad-facts.md, MEMORY.md ("a key a pad can't press doesn't exist"); cc2f395, 8a0f7a1, 18b9f3e.

### D66 Co-op save messages use the CoopMessageBox
- Now: two-player save messages and waits show in `CoopMessageBox`, a copy of the game's inspect-text box (`DialogueBox` look) with the menu's buttons. No button is chosen by default to close it at once. Waits get 继续等 and 不等了/不开了.
- Replaced: the game's error box with 确认 selected by default (02f9aa6 + 10bd976, 0.4.104).
- Old-way signs: new co-op dialogs built on the error box or a bare menu prompt over the save slots.
- Exceptions: none known.
- Source: coop-message-box.md; 02f9aa6, 10bd976. USER 10-17 "那个按键'确认'是默认选中的，也就是默认点一下就关掉，这个逻辑非常的诡异".

### D67 Chinese text breaks lines where Chinese breaks
- Now: the mod's own texts use `WrappingText` / `ChineseLineBreaks`, which put tiny spaces where Chinese may break.
- Replaced: Unity's legacy `Text`, which broke only at spaces (6dde683, 0.4.133).
- Old-way signs: new `Text` components with mixed Chinese and English not going through the wrapper.
- Exceptions: none known.
- Source: unity-text-cjk-line-breaks.md; 6dde683. USER 10-25 "文字排版好像有问题".

### D68 World time keeps running while connected
- Now: while connected, the pause menu and the backpack leave world time running. Chat opens in cutscenes.
- Replaced: a backpack that froze the world and the player's figure (a3d71ab, 10-23).
- Old-way signs: `Time.timeScale = 0` or the game's pause paths taken while connected. Still so in the pause menu: the game's `PauseGameToggle` stops the time and `PauseManager` puts it back in the same step (the `HeroController.Pause` and `TogglePauseGame` hooks), where the backpack leaves `SetTimeScale` out (`_keepTimeRunning`). Nothing shows of it; it is the way to bring over to the backpack's.
- Exceptions: none known.
- Source: coop-pauses.md, dont-run-then-undo.md; a3d71ab, 00fa369. USER 10-23 "队友会停在看地图的一个过度动作上".

### D69 Per-frame paths make no garbage and scans are event-driven
- Now: per-frame, per-packet and per-hook paths make no garbage: reused handlers instead of closures, no enum name lookups per packet, and an object's name asked once (71a5e49). Hooks on shared game methods (`tk2dSpriteAnimator.Play`, `ObjectPool.Recycle`, `ActivateGameObject.OnEnter`, `HealthManager.Die`) are made once for all creatures and look the creature up. A room's fight FSMs are scanned once at load and then only for FSMs made later (`PlayMakerFSM.Init` / `Preprocess` hooks).
- Replaced: garbage that made GC stop the game every 10-30 s in fights (f6f80a4); per-entity hooks put on and taken off (b960cd3); `ScanScene` over every FSM every 5 s (dfe1c78).
- Old-way signs: lambdas capturing locals in Update paths; `Enum.GetName`/`ToString` per packet; `new Hook(` per entity; periodic full FSM scans.
- Exceptions: none known.
- Source: gc-garbage-hot-paths.md, lag-triage-windows-events.md, room-load-fsm-error-spam.md; f6f80a4, b960cd3, dfe1c78, 71a5e49. USER 10-18 "单人不卡、联机打 boss 卡".

### D70 Scene event handlers run one at a time
- Now: Unity's scene event handlers are called one at a time through `SceneEventGuard`, so a handler that throws is logged and the later ones still run.
- Replaced: Unity calling all handlers in one go, where the first one that threw stopped the rest (b261f18). The guard wraps Unity's own invoke, so subscribing with `SceneManager.sceneLoaded +=` is fine.
- Old-way signs: handlers that catch and swallow everything themselves to protect the others; a second guard or invoke loop of their own.
- Exceptions: none known.
- Source: scene-event-guard.md; b261f18.

### D71 Temporary debug commands come out once they have served
- Now: debug commands made for one test are removed when the test is done.
- Replaced: the act 3 and dream-entrance commands /0 and /1-/3 (9b63e46, 10-31), removed 95e2f4b (11-01).
- Old-way signs: chat commands or code paths only those commands reached (the /0 branch in the bench wake fix 0a3c332).
- Exceptions: lasting commands the user asked for (`/copy`, `/memory`, `/giveup`, `/coopsave off`).
- Source: debug-dream-commands.md; 9b63e46, 95e2f4b, 0a3c332.

### D77 No defensive code
- Now: code does what the case needs and no more. No try/catch "just in case" around calls that do not throw, no check for a null or a state that cannot happen there, no second way through for a case nobody has seen. A guard stays only for a state that really comes about: seen in a log, or made by the game's own code. Where a thing needs a fallback, it gets one, at the point that every way through it passes, and that one catches whatever went wrong, whatever the cause - not one fallback per rare case.
- Replaced: guards added by habit or at a review's word, like the try/catch and the IsNone check that 4d2de23 took out of TakeOffTheSilkMarks (d1cb4cf).
- Old-way signs: a try/catch that only logs and goes on around game calls that do not throw; `?.`, `!= null` or `TryGetValue` on what the same method or Awake has just set; a log line for what "should never happen"; a fallback branch with no case known that reaches it; several fallbacks for one thing, each for one rare case, where one at the common point would catch them all.
- Exceptions: what comes over the network (a packet may be cut or come late); objects the game destroys while the mod still holds them (Unity's null); handlers that must not stop the game's own loop (D70).
- Source: USER 11-01 "还有你那越来越多的防御性编程，我真是没招了", "兜底不代表乱想啊…非要预言一些不可能发生的情况", "一件事情，一个兜底就行了，兜底的意义就是出现意料之外的情况也能兜住。你都快变成一件事情，每个少数情况一个兜底了"; 4d2de23.
