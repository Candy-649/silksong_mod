# 丝之歌 × SSMP 实体同步缺口审计

- 生成时间：2026-09-13 22:23
- SSMP：`entity-sync` @ `00b6d81`
- 游戏程序集：`D:\STEAM\steamapps\common\Hollow Knight Silksong\Hollow Knight Silksong_Data\Managed`
- 重新生成：`dotnet run -c Release --project tools/SyncAudit`

> 这是静态分析：只看每种状态机动作的代码「会调用什么」，不知道哪些敌人真的用了它（那需要解析场景和预制体资源）。
> 所以这是一份待排查清单，不是确定的 bug 列表。分类依据是调用的 API 名字，会有误报，证据见同目录的 JSON。

## 背景：SSMP 怎么同步状态机动作

- 非主机玩家电脑上，已登记敌人的状态机整个被关掉（`fsm.enabled = false`）。
- 主机上，只有 `EntityFsmActions` 里写了 Get/Apply 处理函数的动作类型，才会在 `OnEnter` 时被拦截、发给其他人重放。
- 没有处理函数的动作，其效果在其他玩家那边不会发生，除非恰好被位置、动画、血量等组件同步覆盖。

## 总览

| 项目 | 数量 |
|---|---|
| 游戏里的状态机动作类型 | 2328 |
| SSMP 写了处理函数的类型 | 74（在游戏动作里找到 74） |
| SSMP 用 IL 钩子改写的类型 | 5 |
| `action-registry.json` 条目 | 80（`targeted_fsm` 16） |
| 生成物体 | 38（SSMP 未处理 30） |
| 播放音效 | 47（SSMP 未处理 39） |
| 粒子特效 | 12（SSMP 未处理 9） |
| 镜头 | 24（SSMP 未处理 24） |
| 直接调用主角的方法 | 57（SSMP 未处理 57） |
| 时间缩放/顿帧 | 3（SSMP 未处理 3） |
| 血量/死亡/无敌（HealthManager 组件同步可能已覆盖） | 19（SSMP 未处理 18） |
| 开关物体/渲染器 | 52（SSMP 未处理 45） |
| 播放动画 | 46（SSMP 未处理 43） |
| 移动/物理 | 220（SSMP 未处理 207） |
| 消耗随机数 | 112（SSMP 未处理 104） |
| 直接取 HeroController.instance | 58（SSMP 未处理 57） |
| 每帧用 deltaTime 计时 | 131（SSMP 未处理 128） |

## A. 其他玩家那边不会发生的效果（160）

SSMP 没有处理函数，并且代码会影响实体自身以外的东西。一个动作可能出现在多个分类里。

### 生成物体（30）

`AudioPlayerOneShotSingleV2`、`CreateChildWithParentedOffset`、`CreateEmptyObject`、`CreateObjectV2`、`CreateObjectsRandom`、`CreatePoolObjects`、`CreateUIMsgGetItem`、`EnemySingControl`、`FlingFlashingGeo`、`FlingObjectsFromGlobalPoolV2`、`FlingObjectsFromGlobalPoolV3`、`FlingObjectsFromGlobalPoolVelTime`、`ShowBossChallengeUI`、`ShowBossDoorChallengeUI`、`ShowPromptMarker`、`SpawnObjectFromGlobalPoolDelay`、`SpawnObjectFromGlobalPoolOverTime`、`SpawnObjectFromGlobalPoolOverTimeV2`、`SpawnObjectFromGlobalPoolV2`、`SpawnProjectile`、`SpawnProjectileV2`、`SpawnRandomObjects`、`SpawnRandomObjectsOverTime`、`SpawnRandomObjectsOverTimeV2`、`SpawnRandomObjectsRadial`、`SpawnRandomObjectsRadialRandom`、`SpawnRandomObjectsRadialV2`、`SpawnRandomObjectsV2`、`SpawnRandomObjectsVelocity`、`SpawnRunEffects`

### 播放音效（39）

`AudioPlayDelay`、`AudioPlayInStateConditional`、`AudioPlayRandomSingle`、`AudioPlayRandomVoice`、`AudioPlayRandomVoiceFromTable`、`AudioPlayRandomVoiceFromTableBool`、`AudioPlayRandomVoiceFromTableV2`、`AudioPlaySimpleBool`、`AudioPlaySimpleV2`、`AudioPlaySynced`、`AudioPlayV2`、`AudioPlayerOneShotSingleV2`、`AudioStopV2`、`AudioSyncAction`、`EnemySingControl`、`FaceObjectTk2dSpriteScale`、`FaceObjectV4`、`FaceObjectV5`、`LimitedPlayAudioClipOnSource`、`LimitedPlayAudioClipSpawn`、`LimitedPlayAudioLooped`、`LimitedRemoveAudioLooped`、`PlayAudioEvent`、`PlayAudioEventDelayed`、`PlayAudioEventRandom`、`PlayAudioEventRandomBool`、`PlayAudioEventRandomV2`、`PlayAudioEventV2`、`PlayAudioSyncedVibration`、`PlayEnemySingAudio`、`PlayOneShotOnAudioSource`、`PlayRandomAudioClipTable`、`PlayRandomAudioClipTableLooped`、`PlayRandomAudioClipTableV2`、`PlayRandomAudioClipTableV3`、`PlayRandomSound`、`PlaySound`、`SetRandomAudioClipFromTable`、`WalkLeftRight`

### 粒子特效（9）

`ParticleSystemPlay`、`PlayParticleEmitterChildren`、`PlayParticleEmitterConditional`、`PlayParticleEmitterInStateV2`、`StopParticleEmitterOnExit`、`StopParticleEmittersInChildren`、`StopParticleEmittersInChildrenV2`、`WaitParticleSystem`、`WaitParticleSystemsInChildren`

### 镜头（24）

`CameraFollowInState`、`CameraFollowYInState`、`CameraFreezeInPlace`、`CameraRepositionToHero`、`CameraRepositionToHeroV2`、`CameraRumbleSequence`、`CameraStopFreeze`、`CancelCameraShake`、`DoCameraShake`、`DoCameraShakeRepeating`、`DoCameraShakeRepeatingV2`、`DoCameraShakeV2`、`DoCameraShakeV3`、`DoCameraShakeV4`、`EndFreeCameraMode`、`ScreenFlash`、`ScreenFlashBomb`、`ScreenFlashLifeblood`、`ScreenFlashTrobbio`、`SetBloomForced`、`SetCameraIgnoringXOffset`、`SetMainCameraFovOffset`、`StartFreeCameraMode`、`StartFreeCameraModeV2`

### 直接调用主角的方法（57）

`AddHeroInputBlocker`、`AddSilk`、`AddSilkV2`、`AllowMantle`、`CanHeroBeGrabbed`、`CanHeroBeGrabbedV2`、`CanHeroTakeDamage`、`CanHeroTakeDamageIgnoreInvul`、`CheckHeroCanSprint`、`CheckIsCharacterGrounded`、`ClearHeroEffects`、`ClearHeroEffectsInstant`、`ClearHeroEffectsLite`、`ClearSpoolMossChunks`、`DamageHeroDirectly`、`DamageHeroDirectlyV2`、`DoHeroMovement`、`DoHeroRecoil`、`DockClamberCheck`、`ExitFromTransitionGate`、`FaceDirectionUmbrella`、`GetHeroCState`、`GetHeroCStateEvent`、`GetWasButtonPressedQueued`、`HeroAddExtraAirMoveVelocity`、`HeroAddExtraAirMoveVelocityV2`、`HeroCheckForBump`、`HeroCheckForBumpV2`、`HeroCheckForBumpVertical`、`HeroClampFallVelocity`、`HeroClampFallVelocityV2`、`HeroControllerMethods`、`HeroFaceInstant`、`HeroInvulnerability`、`HeroLockState`、`HeroRegenGainHealth`、`HeroRegenGainSilk`、`HeroRelinquishControlDynamic`、`HeroSetWandererCrestState`、`HeroTurnToFace`、`HeroTurnToFaceV2`、`HeroWallJumpBrollyCheck`、`ListenForToolThrow`、`MoveHeroToPosX`、`RemoveHeroInputBlocker`、`SetFrostVignette`、`SetHeroAffectedByGravity`、`SetHeroCState`、`SetHeroCStateDelay`、`SetHeroMaggoted`、`SetHeroParent`、`SetHeroStunned`、`StartRosaryCannonCharge`、`StopRosaryCannonCharge`、`TakeSilk`、`TakeSilkDelayed`、`TakeSilkV2`

### 时间缩放/顿帧（3）

`FreezeMoment`、`FreezeMomentV2`、`ScaleTime`

### 可能只影响实体自身、由组件同步兜底（需人工确认）

- **血量/死亡/无敌（HealthManager 组件同步可能已覆盖）**（18）：`AddHP`、`CancelAllLagHits`、`CheckInvincibility`、`DirectionalInvincibility`、`GetHealthManagerPhysicalPusher`、`GetIsDead`、`HasTakenDamage`、`HeroStealEnemyCurrency`、`InstaDeath`、`PreventInvincibleEffect`、`SetBattleScene`、`SetDamageOverride`、`SetGeoDrop`、`SetInvincible`、`SetInvincibleDelay`、`SetIsDead`、`SetSendKilledToObject`、`SetShardDrop`
- **开关物体/渲染器**（45）：`ActivateAllChildren`、`ActivateAllChildrenV2`、`ActivateBoxCollider2D`、`ActivateGameObjectDelay`、`ActivateInventoryPaneInput`、`ActivateSolo`、`ActivateTrailRenderer`、`AnimatorPauseAtTime`、`Blink`、`CreateObjectV2`、`CreateObjectsRandom`、`CreatePoolObjects`、`CreateUIMsgGetItem`、`CutToCamera`、`DestroyAllChildren`、`DisplayBossTitle`、`DisplayNPCTitle`、`EaseSpriteColor`、`EnableBehaviour`、`EnableFSM`、`EnableFsmSelf`、`EnableGUI`、`Flicker`、`GetNextPreSpawnedGameObject`、`GetPreInstantiatedGameObject`、`SendEventEnableFsm`、`SetAnimator`、`SetAudioSource`、`SetColliderV2`、`SetCurrentRaceTrack`、`SetDamageEnemies`、`SetDamageHero`、`SetDamageHeroOnExit`、`SetMeshRendererChildren`、`SetMeshRendererEveryFrame`、`SetNeedolinTextOwner`、`SetProjectileVelocityManager`、`SetSpriteRendererByColor`、`SetVisibility`、`ShowBossChallengeUI`、`ShowBossDoorChallengeUI`、`SpawnFromPool`、`SpawnFromPoolV2`、`StartClimber`、`StopClimber`
- **播放动画**（43）：`AnimatorCrossFade`、`AnimatorPauseAtTime`、`AnimatorPlay`、`AnimatorPlayStateWait`、`ChaseObjectGround`、`DistanceWalk`、`DistanceWalkServitor`、`DistanceWalkServitorV`、`DistanceWalkVertical`、`FaceDirection`、`FaceDirectionUmbrella`、`FaceDirectionV2`、`FaceObject`、`FaceObjectAnim`、`FaceObjectTk2dSpriteScale`、`FaceObjectV2`、`FaceObjectV3`、`FaceObjectV4`、`FaceObjectV5`、`GrimmChildFly`、`HeroTurnToFace`、`HeroTurnToFaceV2`、`RunAway`、`SetAnimatorBool`、`SetAnimatorFloat`、`SetAnimatorInt`、`SetAnimatorTrigger`、`SyncAnimatorBoolToState`、`Tk2DAnimationByDirection`、`Tk2dPlayAnimationChildren`、`Tk2dPlayAnimationChildrenRandom`、`Tk2dPlayAnimationDelay`、`Tk2dPlayAnimationV2`、`Tk2dPlayAnimationWait`、`Tk2dPlayAnimationWaitV2`、`Tk2dPlayAnimationWithEventsV2`、`Tk2dPlayAnimationWithEventsV3`、`Tk2dPlayRandomAnimationWithEvents`、`WalkLeftRight`、`tk2dPlayAnimAfterPreviousComplete`、`tk2dPlayAnimAfterPreviousCompleteV2`、`tk2dPlayAnimationConditional`、`tk2dPlayAnimationOptionalReset`
- **移动/物理**（207）：`AccelerateTo`、`AccelerateToX`、`AccelerateToXByScale`、`AccelerateToY`、`AccelerateVelocity`、`AddForce2d`、`AddForce2dAsAngle`、`AddForce2dConditional`、`AddForce2dV2`、`AddTorque2d`、`AdjustColliderPosToEdge`、`AdjustColliderPosToEdgeV2`、`AlignToDirection`、`AnimatePositionBy`、`AnimatePositionTo`、`AnimatePositionToV2`、`AnimateRigidBody2DPositionTo`、`AnimateRigidBody2DPositionToV2`、`AnimateRigidBodyYPositionTo`、`AnimateRotationTo`、`AnimateRotationToV2`、`AnimateScaleTo`、`AnimateXPositionTo`、`AnimateYPositionTo`、`AnimateZPositionTo`、`ChaseObject`、`ChaseObjectGround`、`ChaseObjectSpread`、`ChaseObjectV2`、`ChaseObjectV3`、`ChaseObjectVertical`、`ChaseObjectWisp`、`ClampOrthographicView`、`ClampPosition`、`ClampRotation`、`ClampSpeed`、`ClampVelocity2D`、`ConstrainMovement`、`ControllerCrouch`、`CreateChildWithParentedOffset`、`CreateEmptyObject`、`CreateObjectV2`、`CreateObjectsRandom`、`CreatePoolObjects`、`Decelerate`、`DecelerateV2`、`DecelerateXY`、`DecelerateXYConditional`、`DirectlyFlyTo`、`DistanceFly`、`DistanceFlyHorizontal`、`DistanceFlySmooth`、`DistanceFlyV2`、`DistanceFlyV3`、`DistanceWalk`、`DistanceWalkServitor`、`DistanceWalkServitorV`、`DistanceWalkVertical`、`EasePosX`、`EdgeSlowdown`、`EnemySingControl`、`FaceAngle`、`FaceAngleV2`、`FaceAngleV3`、`FaceDirection`、`FaceDirectionV2`、`FaceObject`、`FaceObjectV2`、`FaceObjectV3`、`FaceObjectV4`、`FaceObjectV5`、`FlingFlashingGeo`、`FlingObject`、`FlingObjects`、`FlingObjectsFromGlobalPoolV2`、`FlingObjectsFromGlobalPoolV3`、`FlingObjectsFromGlobalPoolVelTime`、`FlingObjectsV2`、`FlipScale`、`FlipScaleDelay`、`FlipScaleOnExit`、`GetAnimatorBody`、`GetAnimatorIKGoal`、`GetAnimatorRoot`、`GetAnimatorTarget`、`GetNextPreSpawnedGameObject`、`GhostMovement`、`GrimmChildFly`、`HeroClampFallVelocity`、`HeroClampFallVelocityV2`、`HeroMaintainWallDistance`、`IdleBuzz`、`IdleBuzzV2`、`IdleBuzzV3`、`IdleBuzzV4`、`JumpTranslateTo`、`LerpObjectPosition`、`LookAt2d`、`LookAt2dGameObject`、`LookAt2dGameObjectSmooth`、`LookAtDirection`、`MatchScaleSign`、`MatchScaleSignV2`、`MouseLook`、`MouseLook2`、`MoveObject`、`MovePosition2d`、`MovePosition2dV2`、`MoveTowards`、`MultiplyGravity`、`NavMeshAgentAnimatorSynchronizer`、`ObjectJitter`、`ObjectJitterLocal`、`ObjectJitterOnRender`、`ProjectileSquash`、`ProjectileSquashV2`、`RandomlyFlipScale`、`RandomlyFlipYScale`、`RectTransformSetLocalPosition`、`RectTransformSetLocalRotation`、`RectTransformSetScreenPosition`、`RectTransformSetScreenRectFromPoints`、`RestoreGameObjectPositions`、`RigidBody2DSetPositionSafe`、`Rigidbody2DMoveBy`、`Rotate`、`RotateTo`、`RunAway`、`ScaleTo`、`ScreenWrap`、`SetAngularVelocity2d`、`SetGravity2dScaleV2`、`SetPosition2d`、`SetPositionTemp`、`SetPositionToObject`、`SetPositionToObject2D`、`SetPositionToObject2DLateUpdate`、`SetPositionToObject2DV2`、`SetPositionToObjectDelay`、`SetRandomRotation`、`SetRotation2DLerp`、`SetRotationDelay`、`SetRotationTemp`、`SetRotationTimer`、`SetScaleDelay`、`SetScaleTemp`、`SetTransformParent`、`SetVelocity2dBool`、`SetVelocity2dConditional`、`SetVelocity2dIfFalse`、`SetVelocity2dLate`、`SetVelocityByScale`、`ShakePosition`、`ShakePositionV2`、`ShoveFromBouncer`、`ShoveFromPlayer`、`ShoveFromWall`、`ShowBossChallengeUI`、`SilkBossIdleBuzz`、`SimpleLook`、`SimpleTiltByVelocityX`、`SimpleTiltOtherByVelocityX`、`SineMovement`、`SmoothFlyTo`、`SmoothFollowAction`、`SmoothFollowTarget2D`、`SmoothLookAt`、`SmoothLookAt2d`、`SmoothLookAtDirection`、`SpawnFromPool`、`SpawnFromPoolV2`、`SpawnObjectFromGlobalPoolDelay`、`SpawnObjectFromGlobalPoolOverTimeV2`、`SpawnObjectFromGlobalPoolV2`、`SpawnProjectile`、`SpawnProjectileV2`、`SpawnRandomObjects`、`SpawnRandomObjectsOverTime`、`SpawnRandomObjectsOverTimeV2`、`SpawnRandomObjectsRadial`、`SpawnRandomObjectsRadialRandom`、`SpawnRandomObjectsRadialV2`、`SpawnRandomObjectsV2`、`SpawnRandomObjectsVelocity`、`SpawnRunEffects`、`StartRoarEmitter`、`StartRoarEmitterV2`、`Swoop`、`TiltBySpeed`、`TiltBySpeedV2`、`TiltBySpeedY`、`Tk2DAnimationByDirection`、`Translate`、`TranslateContinuous`、`TranslatePosition2d`、`TranslateRandom`、`TranslateV2`、`TranslateVelocity2dConditional`、`TweenPosition`、`TweenPunch`、`TweenRotation`、`TweenScale`、`TweenVelocity2D`、`UmbrellaMotionX`、`UmbrellaMotionXV2`、`WalkLeftRight`、`WispBuzz`

## B. 只认主机那只大黄蜂的动作（57）

代码里直接取 `HeroController.instance`，但不在 `targeted_fsm` 列表里，也没有处理函数或 GamePatcher 补丁。放在主机上跑时，它们只会考虑主机玩家。
其中不少是主角自己的状态机用的（回血、加丝、纹章状态等），和敌人有关的要结合资源数据判断。

`AddHeroInputBlocker`、`CanHeroBeGrabbed`、`CanHeroBeGrabbedV2`、`CanHeroTakeDamage`、`CanHeroTakeDamageIgnoreInvul`、`CheckHeroCanSprint`、`CheckHeroNailImbuement`、`ClearHeroEffects`、`ClearHeroEffectsInstant`、`ClearHeroEffectsLite`、`ClearSpoolMossChunks`、`DamageHeroDirectly`、`DamageHeroDirectlyV2`、`DoHeroMovement`、`DoHeroRecoil`、`ExitFromTransitionGate`、`GetHeroCState`、`GetHeroCStateEvent`、`GetHeroConfigVariable`、`HeroAddExtraAirMoveVelocity`、`HeroAddExtraAirMoveVelocityV2`、`HeroBoxControl`、`HeroBoxControlV2`、`HeroCheckForBump`、`HeroCheckForBumpV2`、`HeroCheckForBumpVertical`、`HeroClampFallVelocityV2`、`HeroControllerMethods`、`HeroFaceInstant`、`HeroGetWarriorCrestState`、`HeroInvulnerability`、`HeroLockState`、`HeroMaintainWallDistance`、`HeroPlayLookDownAnim`、`HeroPlayLookUpAnim`、`HeroRegenGainHealth`、`HeroRegenGainSilk`、`HeroRelinquishControlDynamic`、`HeroSetWandererCrestState`、`HeroTurnToFace`、`HeroTurnToFaceV2`、`IsRefillSoundSuppressed`、`ListenForToolThrow`、`RemoveHeroInputBlocker`、`SetFrostVignette`、`SetHeroAffectedByGravity`、`SetHeroCState`、`SetHeroCStateDelay`、`SetHeroMaggoted`、`SetHeroParent`、`SetHeroStunned`、`StartRoarEmitter`、`StartRoarEmitterV2`、`StartRosaryCannonCharge`、`StopRosaryCannonCharge`、`TakeSilkDelayed`、`WaitForHeroInPosition`

## C. 随机结果可能没有同步的动作

消耗随机数，并且结果会影响生成、运动、特效或音效。

### C1. 没有处理函数（50）

`AudioPlayRandomSingle`、`AudioPlayRandomVoice`、`AudioPlayerOneShotSingleV2`、`ChaseObject`、`ChaseObjectSpread`、`CreateObjectsRandom`、`CreatePoolObjects`、`DistanceWalkServitor`、`DistanceWalkServitorV`、`DistanceWalkVertical`、`EnemySingControl`、`FlingFlashingGeo`、`FlingObject`、`FlingObjects`、`FlingObjectsFromGlobalPoolV2`、`FlingObjectsFromGlobalPoolV3`、`FlingObjectsFromGlobalPoolVelTime`、`FlingObjectsV2`、`IdleBuzz`、`IdleBuzzV2`、`IdleBuzzV3`、`IdleBuzzV4`、`ObjectJitter`、`ObjectJitterLocal`、`ObjectJitterOnRender`、`PlayEnemySingAudio`、`PlayRandomAudioClipTableLooped`、`PlayRandomSound`、`RandomlyFlipScale`、`RandomlyFlipYScale`、`SetRandomRotation`、`ShakePosition`、`ShakePositionV2`、`SilkBossIdleBuzz`、`SpawnFromPool`、`SpawnFromPoolV2`、`SpawnObjectFromGlobalPoolDelay`、`SpawnObjectFromGlobalPoolOverTimeV2`、`SpawnProjectileV2`、`SpawnRandomObjects`、`SpawnRandomObjectsOverTime`、`SpawnRandomObjectsOverTimeV2`、`SpawnRandomObjectsRadial`、`SpawnRandomObjectsRadialRandom`、`SpawnRandomObjectsRadialV2`、`SpawnRandomObjectsV2`、`SpawnRandomObjectsVelocity`、`TranslateRandom`、`WalkLeftRight`、`WispBuzz`

### C2. 有处理函数，但随机数调用没有被 IL 钩子拦截（4）

需要看处理函数是把主机的结果发过去，还是在客户端重新抽了一次随机数。

`AudioPlayRandom`、`AudioPlayerOneShot`、`AudioPlayerOneShotSingle`、`FireAtTarget`

## D. 疑似漏掉的变体（34 组）

SSMP 处理了某个动作，但游戏里还有同一动作的其他版本（V2/V3、Delay、Conditional 等后缀）没被处理。

- `ActivateGameObject` → `ActivateGameObjectDelay`
- `AddComponent` → `AddComponentIfNotPresent`
- `AudioPlay` → `AudioPlayDelay`、`AudioPlaySimpleV2`、`AudioPlayV2`
- `AudioPlayInState` → `AudioPlayInStateConditional`
- `AudioPlayRandom` → `AudioPlayRandomSingle`
- `AudioPlaySimple` → `AudioPlaySimpleBool`、`AudioPlaySimpleV2`
- `AudioPlayerOneShot` → `AudioPlayerOneShotSingleV2`
- `AudioPlayerOneShotSingle` → `AudioPlayerOneShotSingleV2`
- `AudioStop` → `AudioStopV2`
- `CreateObject` → `CreateObjectV2`
- `DestroyObject` → `DestroyObjects`
- `FindChild` → `FindChildRecursive`
- `FlingObjectsFromGlobalPool` → `FlingObjectsFromGlobalPoolV2`、`FlingObjectsFromGlobalPoolV3`、`FlingObjectsFromGlobalPoolVelTime`
- `FlingObjectsFromGlobalPoolVel` → `FlingObjectsFromGlobalPoolVelTime`
- `GetPosition` → `GetPosition2D`、`GetPosition2d`
- `PlayParticleEmitter` → `PlayParticleEmitterChildren`、`PlayParticleEmitterConditional`
- `PlayParticleEmitterInState` → `PlayParticleEmitterInStateV2`
- `SendEventByName` → `SendEventByNameOnExit`、`SendEventByNameUpwards`、`SendEventByNameV3`
- `SendMessage` → `SendMessageDelay`、`SendMessageOnExit`、`SendMessageV2`
- `SetCollider` → `SetColliderV2`
- `SetGameObject` → `SetGameObjectIfNull`
- `SetGravity2dScale` → `SetGravity2dScaleV2`
- `SetMeshRenderer` → `SetMeshRendererChildren`
- `SetParticleEmission` → `SetParticleEmissionChildren`、`SetParticleEmissionTemp`
- `SetPosition` → `SetPosition2D`、`SetPosition2DV2`、`SetPosition2d`、`SetPositionTemp`
- `SetProperty` → `SetPropertyV2`
- `SetRotation` → `SetRotationDelay`、`SetRotationTemp`、`SetRotationTimer`
- `SetScale` → `SetScaleDelay`、`SetScaleTemp`
- `SetStringValue` → `SetStringValueBool`
- `SetVelocity2d` → `SetVelocity2dBool`、`SetVelocity2dConditional`、`SetVelocity2dIfFalse`、`SetVelocity2dLate`
- `SpawnObjectFromGlobalPool` → `SpawnObjectFromGlobalPoolDelay`、`SpawnObjectFromGlobalPoolOverTime`、`SpawnObjectFromGlobalPoolOverTimeV2`、`SpawnObjectFromGlobalPoolV2`
- `StopParticleEmitter` → `StopParticleEmitterOnExit`
- `Tk2dPlayAnimation` → `Tk2dPlayAnimationChildren`、`Tk2dPlayAnimationDelay`、`Tk2dPlayAnimationV2`
- `Tk2dPlayAnimationWithEvents` → `Tk2dPlayAnimationWithEventsV2`、`Tk2dPlayAnimationWithEventsV3`

## E. 只在进入状态时同步、但会持续每帧生效的已处理动作（10）

SSMP 只钩了 `OnEnter`；这些动作有 `everyFrame` 字段或在每帧函数里计时，并且有可见效果。进入状态之后的持续效果要确认有没有别的同步手段。

`ActivateGameObject`、`AudioPlayerOneShot`、`AudioPlayerOneShotSingle`、`FireAtTarget`、`FlingObjectsFromGlobalPoolTime`、`SetPosition`、`SetRotation`、`SetScale`、`SetVelocity2d`、`SetVelocityAsAngle`

## F. 在游戏动作类型里找不到的 SSMP 条目

- `action-registry.json`：游戏里没有这个类型（1）：`GetHeroObject`；类型存在但不是可实例化的状态机动作（0）：（无）
- 处理函数：游戏里没有这个类型（0）：（无）；类型存在但不是可实例化的状态机动作（0）：（无）
- IL 钩子：游戏里没有这个类型（0）：（无）；类型存在但不是可实例化的状态机动作（0）：（无）
