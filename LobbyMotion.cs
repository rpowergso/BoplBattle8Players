using System;
using System.Collections.Generic;
using BoplFixedMath;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MorePlayers;

internal sealed class LobbyMotion : MonoBehaviour
{
    private const float SendInterval = 1f / 30f;
    private const float HeartbeatInterval = 0.4f;
    private const float RemoteTimeout = 1.5f;
    private const int LegacyPositionPacketSize = 10;
    // Keep this size distinct from the match's fourteen-byte packet so an
    // earlier mod build does not hand an unrecognized lobby pose to lockstep.
    private const int PositionPacketSize = 16;
    private const byte MessageType = 7;
    private const byte PoseMessageType = 6;
    private const byte RespawnMessageType = 5;
    private const byte FirstSignedMessageType = 3;
    private const byte OldMessageType = 2;
    private const byte HorizontalSteps = 200;
    private const float NativeHorizontalExtent = 2;
    private const float VerticalSteps = 100f;
    private const int VerticalOrigin = 96;
    private const byte RespawningY = byte.MaxValue;
    private const byte Airborne = byte.MaxValue;
    private const float NativeVerticalSteps = 60f;
    private const float PositionSteps = 16000f;
    private const int PositionLimit = 32000;
    private const short RespawnPosition = short.MaxValue;
    private const float RemoteBuffer = 0.1f;
    private const float RemotePrediction = 0.05f;
    internal const float FixedStep = 1f / 60f;
    internal const float IslandHalfWidth = 18f;
    internal const float JumpViewportHeight = 6f;
    internal const float SmallPlatformWaterHeight = -3.15f;
    internal const float ReviveCenterY = 7.921875f;
    internal const float ReviveWispSeconds = 60 * FixedStep;
    internal const float RespawnSeconds = 105 * FixedStep;
    private const float ReviveParticleSpeed = 30;
    private const float RespawnDelay = 0.25f;
    private const float PlatformHalfHeight = 0.01f;
    private const float DefaultPlayerRadius = 0.76f;
    private static readonly PlatformGeometry SmallPlatform = new PlatformGeometry(3.16f, 2.65f, SmallPlatformWaterHeight);
    private static readonly PlatformGeometry LongPlatform = new PlatformGeometry(17.02f, 4.4f, -4.8f);

    private sealed class PlatformGeometry
    {
        internal readonly float FlatHalfWidth;
        internal readonly float Radius;
        internal readonly float WaterHeight;
        internal readonly RoundedRect Shape;
        internal float CenterY => -Radius - PlatformHalfHeight;
        internal float HalfWidth => FlatHalfWidth + Radius;

        internal PlatformGeometry(float flatHalfWidth, float radius, float waterHeight)
        {
            FlatHalfWidth = flatHalfWidth;
            Radius = radius;
            WaterHeight = waterHeight;
            Shape = new RoundedRect
            {
                box = new Box
                {
                    center = (Vec2)new Vector2(0, CenterY),
                    right = (Vec2)new Vector2(flatHalfWidth, 0),
                    up = (Vec2)new Vector2(0, PlatformHalfHeight),
                    inverseExtents = (Vec2)new Vector2(1 / flatHalfWidth, 1 / PlatformHalfHeight)
                },
                radius = (Fix)radius
            };
        }
    }

    internal CharacterSelectHandler_online Handler;
    internal bool UseLongPlatform;
    private PlatformGeometry Geometry => UseLongPlatform ? LongPlatform : SmallPlatform;
    internal float WaterHeight => Geometry.WaterHeight;
    internal float PlatformCenterY => Geometry.CenterY;
    internal float PlatformHalfWidth => Geometry.HalfWidth;
    private readonly Dictionary<SteamId, RemoteMotion> remote = new Dictionary<SteamId, RemoteMotion>();
    private readonly byte[] outgoing = { (byte)'M', (byte)'B', (byte)'P', (byte)'2', 1, MessageType, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private PracticePhysics physics;
    private SteamId lobbyId;
    private MotionPose previousLocal;
    private MotionPose local;
    private float spawnX;
    private float accumulator;
    private bool wasReady;
    private bool previousJump;
    private bool jumpQueued;
    private float nextSend;
    private float nextHeartbeat;
    private bool sentMovement;
    private ushort sequence;
    private byte respawnCount;
    private ReviveSequence localRevive;
    internal float PlayerRadius => physics != null ? physics.Radius : DefaultPlayerRadius;

    internal static LobbyMotion ForSelector(CharacterSelectBox selector)
    {
        var handler = CharacterSelectHandler_online.selfRef;
        return InLobby(handler) && handler.characterSelectBox == selector ? handler.GetComponent<LobbyMotion>() : null;
    }

    private sealed class RemoteMotion
    {
        internal ushort Sequence;
        internal readonly List<MotionSample> Samples = new List<MotionSample>(8);
        internal byte? RespawnCount;
        internal ReviveSequence Revive;
        internal bool HoldingRespawn;
    }

    private readonly struct ReviveSequence
    {
        internal readonly Vector2 Origin;
        internal readonly float Started;
        internal readonly float Arrives;

        internal ReviveSequence(Vector2 origin, float started)
        {
            Origin = origin;
            Started = started;
            // Bopl's wisp charges in place, then the projectile flies at its native world speed.
            float distance = Vector2.Distance(origin, new Vector2(0, ReviveCenterY));
            Arrives = started + ReviveWispSeconds + Mathf.Ceil(distance / (ReviveParticleSpeed * FixedStep)) * FixedStep;
        }

        internal bool Exists => Arrives > Started;
        internal bool IsActive(float time) => Exists && time >= Started && time < Arrives + RespawnSeconds;
    }

    private readonly struct MotionSample
    {
        internal readonly float Time;
        internal readonly MotionPose Pose;

        internal MotionSample(float time, MotionPose pose)
        {
            Time = time;
            Pose = pose;
        }
    }

    private readonly struct MotionPose
    {
        internal readonly Vector2 Position;
        internal readonly Vector2 Velocity;
        internal readonly bool Grounded;
        internal readonly float Angle;
        internal readonly float Facing;

        internal MotionPose(Vector2 position, bool grounded, float angle, Vector2 velocity = default, float facing = 0)
        {
            Position = position;
            Velocity = velocity;
            Grounded = grounded;
            Angle = angle;
            Facing = facing;
        }

        internal static MotionPose Interpolate(MotionPose from, MotionPose to, float blend)
        {
            var animationPose = blend < 0.5f ? from : to;
            var velocity = from.Grounded == to.Grounded ? Vector2.Lerp(from.Velocity, to.Velocity, blend) : animationPose.Velocity;
            return new MotionPose(Vector2.Lerp(from.Position, to.Position, blend), animationPose.Grounded,
                Mathf.LerpAngle(from.Angle, to.Angle, blend), velocity, animationPose.Facing);
        }
    }

    private void Update()
    {
        if (!InLobby(Handler)) return;
        var manager = SteamManager.instance;
        if (manager == null || !SteamClient.IsValid || !manager.currentLobby.Id.IsValid) return;
        MatchLobby(manager.currentLobby.Id);

        bool ready = CharacterSelectHandler_online.IsLocalPlayerReady();
        if (ready != wasReady)
        {
            ResetLocalMotion();
            wasReady = ready;
        }
        if (!ready) return;
        float now = Time.unscaledTime;
        Vector2 input = Vector2.zero;
        bool jump = false;
        if (CanUseControls())
            ReadControls(Handler.characterSelectBox, out input, out jump);

        jumpQueued |= jump && !previousJump;
        previousJump = jump;
        if (localRevive.Exists)
        {
            jumpQueued = false;
            if (now >= localRevive.Arrives && physics.TouchesWater)
            {
                physics.PlaceAtReviveOrb();
                local = previousLocal = ReadLocalPose();
                nextSend = 0;
            }
            if (localRevive.IsActive(now))
            {
                SendMotion(manager.connectedPlayers, now);
                return;
            }
            physics.LeaveReviveOrb();
            localRevive = default;
        }
        accumulator = Mathf.Min(accumulator + Time.unscaledDeltaTime, 0.1f);
        while (accumulator >= FixedStep)
        {
            previousLocal = local;
            bool respawned = physics.Step(input, jumpQueued);
            local = ReadLocalPose();
            jumpQueued = false;
            accumulator -= FixedStep;
            if (respawned)
            {
                localRevive = new ReviveSequence(new Vector2(physics.Position.x, WaterHeight), now);
                previousLocal = local;
                respawnCount = unchecked((byte)(respawnCount + 1));
                accumulator = nextSend = 0;
                break;
            }
        }
        SendMotion(manager.connectedPlayers, now);
    }

    private MotionPose ReadLocalPose() => new MotionPose(Normalize(physics.Position), physics.IsGrounded, physics.Angle,
        physics.AnimationVelocity, physics.Facing);

    private bool CanUseControls()
    {
        if (!InLobby(Handler)) return false;
        var selector = Handler.characterSelectBox;
        var invite = Handler.GetComponent<OnlineInviteMenu>();
        return selector != null && selector.enabled && selector.menuState == CharSelectMenu.trueReady &&
            (invite == null || !invite.BlocksInput);
    }

    internal bool ClaimsMenuButton(ButtonControl button)
    {
        if (!CanUseControls()) return false;
        var selector = Handler.characterSelectBox;
        var bindings = selector.playerInit.keybindOverride;
        if (!selector.UseskeyboardMouse)
        {
            var pad = selector.currentGamePad;
            return pad != null &&
                (button == pad.buttonSouth || button == BoundButton(bindings, 0, pad.buttonSouth) ||
                 button == pad.leftStick.left || button == pad.leftStick.right);
        }

        var keyboard = Keyboard.current;
        return button == BoundButton(bindings, 4, keyboard?.wKey) ||
            button == BoundButton(bindings, 5, keyboard?.aKey) ||
            button == BoundButton(bindings, 6, keyboard?.sKey) ||
            button == BoundButton(bindings, 7, keyboard?.dKey) ||
            button == BoundButton(bindings, 0, keyboard?.spaceKey);
    }

    private static bool InLobby(CharacterSelectHandler_online handler)
    {
        return GameSession.inMenus && handler != null && handler.isActiveAndEnabled &&
            !handler.isStartingAGame && handler.goingBackOperation == null;
    }

    private void MatchLobby(SteamId current)
    {
        if (lobbyId == current) return;
        remote.Clear();
        respawnCount = 0;
        spawnX = SpawnOffset(SteamClient.SteamId).x * IslandHalfWidth;
        wasReady = false;
        ResetLocalMotion();
        lobbyId = current;
    }

    private void ResetLocalMotion()
    {
        if (physics == null) physics = new PracticePhysics(transform, UseLongPlatform);
        physics.Reset(spawnX);
        local = previousLocal = ReadLocalPose();
        accumulator = 0;
        jumpQueued = previousJump = false;
        nextSend = nextHeartbeat = 0;
        sentMovement = false;
        localRevive = default;
    }

    private static void ReadControls(CharacterSelectBox selector, out Vector2 input, out bool jump)
    {
        var bindings = selector.playerInit.keybindOverride;
        if (selector.UseskeyboardMouse)
        {
            var keyboard = Keyboard.current;
            input = new Vector2(
                Pressed(BoundButton(bindings, 7, keyboard?.dKey)) - Pressed(BoundButton(bindings, 5, keyboard?.aKey)),
                Pressed(BoundButton(bindings, 4, keyboard?.wKey)) - Pressed(BoundButton(bindings, 6, keyboard?.sKey)));
            jump = BoundButton(bindings, 0, keyboard?.spaceKey)?.isPressed == true;
            return;
        }

        var pad = selector.currentGamePad;
        if (pad == null)
        {
            input = Vector2.zero;
            jump = false;
            return;
        }
        var stick = pad.leftStick.ReadValue();
        if (Player.InputProfile.inputVectors == null) Player.InputProfile.GenerateInputVectorTable();
        byte angle = Player.InputProfile.JoyStickAngleByteFromVector((Vec2)stick);
        input = (Vector2)Player.InputProfile.inputVectorFromByte(angle);
        jump = BoundButton(bindings, 0, pad.buttonSouth)?.isPressed == true;
    }

    private static int Pressed(ButtonControl button) => button?.isPressed == true ? 1 : 0;

    private static ButtonControl BoundButton(InputControl[] bindings, int index, ButtonControl fallback)
    {
        var bound = bindings != null && index < bindings.Length ? bindings[index] as ButtonControl : null;
        return bound ?? fallback;
    }

    // Inactive GameObjects let us use the game's fixed-point components and prefab
    // settings without running Awake or registering them in the match simulation.
    internal sealed class PracticePhysics : IDisposable, IPlayerIdHolder
    {
        private readonly GameObject root;
        private readonly PlayerPhysics player;
        private readonly PlayerBody body;
        private readonly StickyRoundedRectangle platform;
        private readonly PlatformGeometry geometry;
        private float submerged;
        private Vector2 sunkPosition;
        private readonly bool playSounds;

        internal float Radius => (float)player.radius;
        internal float WaterHeight => geometry.WaterHeight;
        internal float MaxSpeed => (float)player.Speed;
        internal float Acceleration => (float)player.accel;
        internal bool IsGrounded => submerged == 0 && player.IsGrounded();
        internal float Angle => IsGrounded ? (float)body.rotation * Mathf.Rad2Deg : 0;
        internal bool TouchesWater => submerged > 0;
        internal Vector2 Position => submerged > 0 ? sunkPosition : (Vector2)(body.position - body.up * player.radius);
        internal Vector2 Velocity => IsGrounded
            ? (Vector2)(platform.forwardsDirection(body) * player.groundedSpeed)
            : (Vector2)body.Velocity;
        // The match's SpeedContainer reads the slime's own velocity for jump frames.
        internal Vector2 AnimationVelocity => IsGrounded ? Velocity : (Vector2)body.selfImposedVelocity;
        internal float Facing { get; private set; }

        internal PracticePhysics(Transform parent = null, bool useLongPlatform = false)
        {
            playSounds = parent != null;
            geometry = useLongPlatform ? LongPlatform : SmallPlatform;
            root = new GameObject("Lobby motion simulation");
            root.SetActive(false);
            if (parent != null) root.transform.SetParent(parent, false);

            var platformObject = new GameObject(useLongPlatform ? "LongRect" : "SmallCapsule");
            platformObject.transform.SetParent(root.transform, false);
            var platformTransform = platformObject.AddComponent<FixTransform>();
            platformTransform.position = geometry.Shape.box.center;
            platformTransform.rotation = Fix.Zero;
            var shape = platformObject.AddComponent<DPhysicsRoundedRect>();
            shape.startExtents = new Vec2((Fix)geometry.FlatHalfWidth, (Fix)PlatformHalfHeight);
            shape.startRadius = (Fix)geometry.Radius;
            shape.fixTrans = platformTransform;
            var platformBody = platformObject.AddComponent<BoplBody>();
            platformBody.fixTrans = platformTransform;
            shape.body = platformBody;
            platform = platformObject.AddComponent<StickyRoundedRectangle>();
            platform.groundBody = platformBody;
            platform.rr = shape;
            platform.platformType = PlatformType.grass;
            platform.UpdateBounds();

            // The prefab supplies both the serialized movement settings and
            // the IPlayerIdHolder required by Unity. Its inactive hierarchy
            // prevents Awake on all its components.
            var prefab = Array.Find(Resources.FindObjectsOfTypeAll<SlimeController>(),
                slime => !slime.gameObject.scene.IsValid() && slime.animData != null && slime.animData.name == "MCAnimData");
            if (prefab == null)
            {
                UnityEngine.Object.Destroy(root);
                throw new InvalidOperationException("Bopl's player prefab is unavailable in the online lobby.");
            }
            var clone = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            player = clone.GetComponent<PlayerPhysics>();
            body = clone.GetComponent<PlayerBody>();
            body.fixTransform = clone.GetComponent<FixTransform>();
            body.fixTransform.scale = Fix.One;
            body.rotation = Fix.Zero;
            player.body = body;
            player.playerIdHolder = this;
            body.physics = player;
            Reset(0);
        }

        internal void Reset(float spawnX)
        {
            submerged = 0;
            Facing = 1;
            body.rotation = Fix.Zero;
            body.position = new Vec2((Fix)spawnX, player.radius);
            player.AttachToGround(platform, body.position);
            player.groundedSpeed = Fix.Zero;
        }

        internal void PlaceAtReviveOrb()
        {
            submerged = 0;
            Facing = 1;
            player.attachedGround = null;
            player.UnGround(true, false);
            body.position = new Vec2(Fix.Zero, (Fix)ReviveCenterY);
            body.externalVelocity = body.selfImposedVelocity = Vec2.zero;
        }

        internal void LeaveReviveOrb()
        {
            // SlimeController.Spawn releases the blob with one quarter of its jump strength.
            body.selfImposedVelocity = Vec2.up * player.jumpStrength * (Fix)0.25f;
        }

        internal bool Step(Vector2 input, bool jump)
        {
            if (submerged > 0)
            {
                submerged += FixedStep;
                sunkPosition.y = Mathf.Max(WaterHeight - 1.1f, sunkPosition.y - 12 * FixedStep);
                return submerged >= RespawnDelay;
            }

            Fix tick = GameTime.FixedTimeStep;
            bool wasGrounded = player.IsGrounded();
            var priorContext = activePhysics;
            activePhysics = this;
            try
            {
                // Match component order: PlayerPhysics adds gravity before
                // SlimeController reads jump/move, then PlayerBody integrates.
                if (!wasGrounded)
                {
                    player.AddGravityFactor();
                    player.VelocityBasedRaycasts(true, tick);
                }
                bool onGround = player.IsGrounded();
                bool upsideDown = onGround && body.up.y < Fix.Zero;
                if (onGround)
                {
                    Vec2 direction = (Vec2)input;
                    Fix alignment = player.InputGroundAlignment(Vec2.NormalizedSafe(direction));
                    bool movingIntoSurface = Vec2.Dot(direction, body.up) < Fix.Zero;
                    if (Vec2.Magnitude(direction) >= Constants.DEADZONE &&
                        (Fix.Abs(alignment) >= Constants.GROUNDALIGNMENTDEADZONE ||
                         movingIntoSurface && Fix.Abs(alignment) >= (Fix)0.01f))
                        Facing = Fix.Sign(alignment);
                    if (jump) player.Jump();
                }

                player.Move((Vec2)input, tick, false);
                if (player.IsGrounded())
                {
                    // Use the native ground acceleration and friction. The
                    // no-blockers path follows the actual sticky perimeter.
                    player.LocalPlatformPos = platform.moveAlongSurface(body, player.radius, player.groundedSpeed,
                        tick, player.LocalPlatformPos);
                }

                body.UpdateSim(tick);
                player.LateUpdateSim(tick);
                if (!player.IsGrounded())
                {
                    if (upsideDown) Facing = -Facing;
                    if (Mathf.Abs(input.x) > (float)Constants.MIN_TURNSPEED) Facing = Mathf.Sign(input.x);
                }
                else if (!wasGrounded && body.up.y < Fix.Zero) Facing = -Facing;
            }
            finally
            {
                activePhysics = priorContext;
            }

            if ((float)(body.position.y - player.radius) <= WaterHeight)
            {
                sunkPosition = new Vector2((float)body.position.x, WaterHeight);
                submerged = FixedStep;
            }
            return false;
        }

        public int GetPlayerId() => 1;
        public void SetPlayerId(int playerId) { }
        public Material GetPlayerMaterial() => null;
        public void Dispose() => UnityEngine.Object.Destroy(root);

        internal RaycastInformation Raycast(Vec2 origin, Vec2 direction, Fix distance)
        {
            if (!global::Raycast.RayCastRoundedRect(origin, direction, geometry.Shape, out Fix near, out Fix far) ||
                near > distance)
                return new RaycastInformation { nearDist = Fix.MaxValue };

            return new RaycastInformation
            {
                nearDist = near,
                farDist = far,
                nearPos = origin + near * direction,
                farPos = origin + far * direction,
                pp = new PhysicsParent { fixTrans = platform.rr.fixTrans }
            };
        }

        internal bool Owns(PlayerPhysics component) => component == player;
        internal bool ShouldPlaySound => playSounds && AudioManager.instance != null &&
            Array.Exists(AudioManager.instance.sounds, sound => sound.name == "slimeJump1" && sound.source != null);
    }

    [ThreadStatic] private static PracticePhysics activePhysics;

    // Only the inactive lobby player uses its isolated rounded collider. All
    // match components retain their original terrain and sound code.
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.checkForWall))]
    private static class PracticeTerrainPatch
    {
        private static bool Prefix(PlayerPhysics __instance, Vec2 __0, Vec2 __1, Fix __2, ref RaycastInformation __result)
        {
            if (activePhysics == null || !activePhysics.Owns(__instance)) return true;
            __result = activePhysics.Raycast(__0, __1, __2);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.UnGround))]
    private static class PracticeJumpSoundPatch
    {
        private static void Prefix(PlayerPhysics __instance, ref bool __1)
        {
            if (activePhysics != null && activePhysics.Owns(__instance) && !activePhysics.ShouldPlaySound)
                __1 = false;
        }
    }

    private void OnDestroy() => physics?.Dispose();

    private static Vector2 Normalize(Vector2 world) => new Vector2(world.x / IslandHalfWidth, world.y / JumpViewportHeight);

    private MotionPose ReviveOrbPose => new MotionPose(Normalize(new Vector2(0, ReviveCenterY - PlayerRadius)), false, 0);

    internal bool TouchesWater(Vector2 offset, float angle) =>
        offset.y * JumpViewportHeight + PlayerRadius * (Mathf.Cos(angle * Mathf.Deg2Rad) - 1) <= WaterHeight + 0.03f;

    internal static bool OnSurface(Vector2 offset, out float angle)
        => OnSurface(offset, SmallPlatform, out angle);

    private static bool OnSurface(Vector2 offset, PlatformGeometry geometry, out float angle)
    {
        var feet = (Vec2)new Vector2(offset.x * IslandHalfWidth, offset.y * JumpViewportHeight);
        var closest = geometry.Shape.GetClosestPoint(feet);
        angle = 0;
        if ((float)Vec2.Magnitude(feet - closest) > 0.13f) return false;
        float flatHalfWidth = geometry.FlatHalfWidth;
        float side = Mathf.Clamp((float)closest.x - Mathf.Clamp((float)closest.x, -flatHalfWidth, flatHalfWidth),
            -geometry.Radius, geometry.Radius);
        float y = (float)closest.y - Mathf.Clamp((float)closest.y,
            geometry.CenterY - PlatformHalfHeight, geometry.CenterY + PlatformHalfHeight);
        angle = -Mathf.Atan2(side, y) * Mathf.Rad2Deg;
        return true;
    }

    internal Vector2 GetOffset(int slot, SteamConnection peer) => GetOffset(slot, peer, out _);

    internal Vector2 GetOffset(int slot, SteamConnection peer, out float respawnAge) =>
        GetPose(slot, peer, out respawnAge, out _, out _);

    internal Vector2 GetPose(int slot, SteamConnection peer, out float respawnAge, out bool grounded, out float angle) =>
        GetPose(slot, peer, out respawnAge, out grounded, out angle, out _, out _);

    internal Vector2 GetPose(int slot, SteamConnection peer, out float respawnAge, out bool grounded, out float angle,
        out Vector2 velocity, out float facing)
    {
        float now = Time.unscaledTime;
        respawnAge = float.PositiveInfinity;
        MotionPose pose;
        if (slot == 0)
        {
            if (localRevive.IsActive(now) && now >= localRevive.Arrives) respawnAge = now - localRevive.Arrives;
            pose = MotionPose.Interpolate(previousLocal, local, accumulator / FixedStep);
        }
        else if (peer != null && peer.Connected && remote.TryGetValue(peer.id, out var state))
        {
            float renderTime = now - RemoteBuffer;
            var revive = state.Revive;
            if (!revive.IsActive(renderTime)) pose = GetRemotePose(state, renderTime);
            else if (renderTime < revive.Arrives) pose = new MotionPose(Normalize(revive.Origin), false, 0);
            else
            {
                respawnAge = renderTime - revive.Arrives;
                pose = ReviveOrbPose;
            }
        }
        else pose = new MotionPose(peer != null && peer.Connected ? SpawnOffset(peer.id) : Vector2.zero, true, 0);

        grounded = pose.Grounded;
        angle = pose.Angle;
        velocity = pose.Velocity;
        facing = pose.Facing;
        return pose.Position;
    }

    internal bool GetRevive(int slot, SteamConnection peer, out Vector2 origin, out float age, out float arrivalAge)
    {
        float now = Time.unscaledTime;
        ReviveSequence revive;
        if (slot == 0) revive = localRevive;
        else if (peer != null && peer.Connected && remote.TryGetValue(peer.id, out var state))
        {
            revive = state.Revive;
            now -= RemoteBuffer;
        }
        else revive = default;

        origin = revive.Origin;
        age = now - revive.Started;
        arrivalAge = now - revive.Arrives;
        return revive.IsActive(now);
    }

    private static MotionPose GetRemotePose(RemoteMotion state, float renderTime)
    {
        var samples = state.Samples;
        while (samples.Count > 2 && samples[1].Time <= renderTime) samples.RemoveAt(0);
        if (samples.Count == 1) return samples[0].Pose;

        var from = samples[0];
        var to = samples[1];
        float interval = to.Time - from.Time;
        if (interval <= 0 || interval > RemoteTimeout) return to.Pose;
        var displacement = to.Pose.Position - from.Pose.Position;
        var velocity = new Vector2(displacement.x * IslandHalfWidth, displacement.y * JumpViewportHeight) / interval;
        if (renderTime <= to.Time)
        {
            var pose = MotionPose.Interpolate(from.Pose, to.Pose, Mathf.InverseLerp(from.Time, to.Time, renderTime));
            return new MotionPose(pose.Position, pose.Grounded, pose.Angle, velocity);
        }
        if (state.HoldingRespawn || interval > RemoteBuffer || from.Pose.Grounded != to.Pose.Grounded)
            return to.Pose;

        // Continue through a short delivery gap and then stop. A respawn always
        // clears this history, so prediction never traverses the water-to-anchor jump.
        float continuation = Mathf.Min(renderTime - to.Time, RemotePrediction) / interval;
        Vector2 position = to.Pose.Position + displacement * continuation;
        float angle = to.Pose.Angle + Mathf.DeltaAngle(from.Pose.Angle, to.Pose.Angle) * continuation;
        return new MotionPose(position, to.Pose.Grounded, angle, renderTime - to.Time < RemotePrediction ? velocity : Vector2.zero);
    }

    private Vector2 SpawnOffset(SteamId player)
    {
        ulong id = player;
        ulong position = unchecked(id * 11400714819323198485UL) >> 56;
        float spawnWidth = Mathf.Min(Geometry.FlatHalfWidth, 9f);
        return new Vector2((position / 255f - 0.5f) * 1.6f * spawnWidth / IslandHalfWidth, 0);
    }

    private void SendMotion(List<SteamConnection> peers, float now)
    {
        if (peers == null || peers.Count == 0 || now < nextSend) return;
        var x = EncodePosition(local.Position.x);
        bool atOrb = localRevive.IsActive(now) && now >= localRevive.Arrives;
        short y = atOrb ? RespawnPosition : EncodePosition(local.Position.y);
        byte angle = local.Grounded
            ? (byte)Mathf.RoundToInt(127 * (Mathf.DeltaAngle(0, local.Angle) / 180 + 1))
            : Airborne;
        bool changed = x != ReadPosition(outgoing, 8) || y != ReadPosition(outgoing, 10) ||
            respawnCount != outgoing[12] || angle != outgoing[13];
        if (!changed && !sentMovement && now < nextHeartbeat) return;

        sequence++;
        outgoing[6] = (byte)sequence;
        outgoing[7] = (byte)(sequence >> 8);
        WritePosition(outgoing, 8, x);
        WritePosition(outgoing, 10, y);
        outgoing[12] = respawnCount;
        outgoing[13] = angle;
        sentMovement = changed;
        nextSend = now + SendInterval;
        nextHeartbeat = now + HeartbeatInterval;
        foreach (var peer in peers)
        {
            if (peer.Connected && !peer.beingKicked)
                peer.Connection.SendMessage(outgoing, SendType.Unreliable | SendType.NoDelay);
        }
    }

    // Called only after SteamSocket has authenticated the sender as a lobby member.
    internal static bool TryHandlePacket(SteamManager manager, SteamId sender, byte[] packet)
    {
        if (!HasMotionHeader(packet)) return false;
        var handler = CharacterSelectHandler_online.selfRef;
        if (!InLobby(handler) || sender == SteamClient.SteamId ||
            !manager.connectedPlayers.Exists(peer => peer.id == sender && peer.Connected && !peer.beingKicked) ||
            !TryDecode(packet, out ushort order, out Vector2 offset, out byte? respawns, out bool respawning, out float? groundAngle))
            return true;

        var motion = handler.GetComponent<LobbyMotion>();
        if (motion != null)
        {
            motion.MatchLobby(manager.currentLobby.Id);
            motion.Receive(sender, order, offset, Time.unscaledTime, respawns, respawning, groundAngle, packet[5] >= PoseMessageType);
        }
        return true;
    }

    internal void Receive(SteamId sender, ushort order, Vector2 offset, float now, byte? respawns = null,
        bool respawning = false, float? groundAngle = null, bool hasPose = false)
    {
        if (!remote.TryGetValue(sender, out var state))
        {
            state = new RemoteMotion();
            remote.Add(sender, state);
        }
        var samples = state.Samples;
        var previous = samples.Count > 0 ? samples[samples.Count - 1] : default;
        if (samples.Count > 0 && now - previous.Time < RemoteTimeout && !IsNewer(order, state.Sequence)) return;

        bool startedInWater = !respawning && offset.y * JumpViewportHeight <= WaterHeight + 0.04f;
        bool previousInWater = samples.Count > 0 && previous.Pose.Position.y * JumpViewportHeight <= WaterHeight + 0.04f;
        bool newRespawn = respawns.HasValue && state.RespawnCount.HasValue
            ? respawns != state.RespawnCount
            : !state.HoldingRespawn && respawning || (!respawns.HasValue && previousInWater && offset.y >= 0);
        if (newRespawn)
        {
            float originX = startedInWater ? offset.x : previous.Pose.Position.x;
            state.Revive = new ReviveSequence(new Vector2(originX * IslandHalfWidth, WaterHeight), now);
            if (!startedInWater) samples.Clear();
        }
        else if (respawning && !state.HoldingRespawn) samples.Clear();
        float angle = groundAngle.GetValueOrDefault();
        bool grounded = groundAngle.HasValue || !hasPose && OnSurface(offset, Geometry, out angle);
        state.HoldingRespawn = respawning;
        samples.Add(new MotionSample(now, respawning ? ReviveOrbPose : new MotionPose(offset, grounded, angle)));
        if (samples.Count > 8) samples.RemoveAt(0);
        state.RespawnCount = respawns;
        state.Sequence = order;
    }

    private static bool HasMotionHeader(byte[] packet)
    {
        return packet.Length >= 6 && packet[0] == 'M' && packet[1] == 'B' && packet[2] == 'P' &&
            packet[3] == '2' && packet[4] == 1 && packet[5] >= OldMessageType && packet[5] <= MessageType;
    }

    internal static bool TryDecode(byte[] packet, out ushort order, out Vector2 offset)
        => TryDecode(packet, out order, out offset, out _, out _, out _);

    internal static bool TryDecode(byte[] packet, out ushort order, out Vector2 offset, out byte? respawns, out bool respawning)
        => TryDecode(packet, out order, out offset, out respawns, out respawning, out _);

    internal static bool TryDecode(byte[] packet, out ushort order, out Vector2 offset, out byte? respawns, out bool respawning,
        out float? groundAngle)
    {
        order = 0;
        offset = Vector2.zero;
        respawns = null;
        respawning = false;
        groundAngle = null;
        if (!HasMotionHeader(packet)) return false;
        if (packet[5] == MessageType)
        {
            if (packet.Length != PositionPacketSize || packet[14] != 0 || packet[15] != 0) return false;
            short preciseX = ReadPosition(packet, 8);
            short preciseY = ReadPosition(packet, 10);
            bool anchor = preciseX == 0 && preciseY == RespawnPosition;
            if (Mathf.Abs((int)preciseX) > PositionLimit || (!anchor && Mathf.Abs((int)preciseY) > PositionLimit))
                return false;

            order = (ushort)(packet[6] | (packet[7] << 8));
            offset = new Vector2(preciseX / PositionSteps, anchor ? 0 : preciseY / PositionSteps);
            respawns = packet[12];
            respawning = anchor;
            if (packet[13] != Airborne) groundAngle = DecodeAngle(packet[13]);
            return true;
        }

        bool hasPose = packet[5] == PoseMessageType;
        bool hasRespawnCounter = packet[5] >= RespawnMessageType;
        int size = LegacyPositionPacketSize + (hasRespawnCounter ? 1 : 0) + (hasPose ? 1 : 0);
        if (packet.Length != size || packet[8] > HorizontalSteps) return false;

        bool atAnchor = hasRespawnCounter && packet[8] == HorizontalSteps / 2 && packet[9] == RespawningY;
        int origin = packet[5] == FirstSignedMessageType ? 55 : VerticalOrigin;
        if (!atAnchor && packet[9] > (packet[5] == OldMessageType ? 200 : origin + 120)) return false;
        order = (ushort)(packet[6] | (packet[7] << 8));
        float y = atAnchor ? 0 : packet[5] == OldMessageType ? packet[9] / 200f
            : (packet[9] - origin) / (hasPose ? NativeVerticalSteps : VerticalSteps);
        float x = (packet[8] * (2f / HorizontalSteps) - 1) * (hasPose ? NativeHorizontalExtent : 1);
        offset = new Vector2(x, y);
        if (hasRespawnCounter) respawns = packet[10];
        respawning = atAnchor;
        if (hasPose && packet[11] != Airborne) groundAngle = DecodeAngle(packet[11]);
        return true;
    }

    private static short EncodePosition(float position) =>
        (short)Mathf.Clamp(Mathf.RoundToInt(position * PositionSteps), -PositionLimit, PositionLimit);

    private static short ReadPosition(byte[] packet, int index) =>
        unchecked((short)(packet[index] | packet[index + 1] << 8));

    private static void WritePosition(byte[] packet, int index, short position)
    {
        packet[index] = (byte)position;
        packet[index + 1] = (byte)(position >> 8);
    }

    private static float DecodeAngle(byte angle) => (angle / 127f - 1) * 180;

    internal static bool IsNewer(ushort order, ushort previous)
    {
        ushort distance = unchecked((ushort)(order - previous));
        return distance > 0 && distance < 0x8000;
    }
}
