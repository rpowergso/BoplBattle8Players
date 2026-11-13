#if BOPL8_UI_SMOKE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MorePlayers
{
    internal sealed class OnlineLobbySmokeTest : MonoBehaviour
    {
        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            StartCoroutine(Run());
        }
        IEnumerator Run()
        {
            Main.Log.LogInfo("[UI smoke] started");
            OnlinePlayerRoster.LocalNameOverride = "Test player 1";
            OnlineInviteMenu.TestFriends = Enumerable.Range(1, 3)
                .Select(i => new Steamworks.Friend((Steamworks.SteamId)(76561197960265729UL + (ulong)i))).ToList();
            HostPatch.InputBuffer.Enqueue(new MultiInputPacket());
            HostPatch.previousInputPacket = new MultiInputPacket();
            HostPatch.previousInputPacket.inputPackets.Add(1, new InputPacket());
            HostPatch_Init.Prefix();
            if (HostPatch.InputBuffer.Count != 0 || HostPatch.previousInputPacket.inputPackets.Count != 0)
                throw new Exception("Round reset retained stale custom inputs");
            if (SteamConnectionStats.Delivery(0.975f) != (97.5f).ToString("0.0") + "%" ||
                SteamConnectionStats.Delivery(-1) != "n/a" || SteamConnectionStats.Delivery(float.NaN) != "n/a")
                throw new Exception("Steam delivery formatting is incorrect");
            Main.Log.LogInfo("[UI smoke] PASS: custom round input reset and delivery formatting");
            var steamPeer = new SteamConnection { id = (Steamworks.SteamId)76561197960265729UL };
            var gameplayClients = new List<Client> { new Client(5, steamPeer) };
            if (!SteamSocketPatch_OnMessage.MatchesGameplaySender(gameplayClients, steamPeer.id, 5) ||
                SteamSocketPatch_OnMessage.MatchesGameplaySender(gameplayClients, steamPeer.id, 6) ||
                SteamSocketPatch_OnMessage.MatchesGameplaySender(gameplayClients, steamPeer.id, 0) ||
                SteamSocketPatch_OnMessage.MatchesGameplaySender(gameplayClients, (Steamworks.SteamId)76561197960265730UL, 5))
                throw new Exception("Gameplay packet attribution did not match the transport sender");
            Main.Log.LogInfo("[UI smoke] PASS: gameplay sender-to-slot attribution rejects spoofed and unknown players");
            byte[] movementPacket = { 77, 66, 80, 50, 1, 5, 255, 255, 200, 46, 17 };
            if (!LobbyMotion.TryDecode(movementPacket, out ushort order, out Vector2 offset, out byte? respawns, out bool spawning) ||
                order != ushort.MaxValue || respawns != 17 || spawning || Vector2.Distance(offset, new Vector2(1, -0.5f)) > 0.001f ||
                !LobbyMotion.IsNewer(0, ushort.MaxValue) || LobbyMotion.IsNewer(ushort.MaxValue, 0))
                throw new Exception("Lobby movement packet decoding or sequence wrap is incorrect");
            byte[] oldMovementPacket = { 77, 66, 80, 50, 1, 2, 0, 0, 100, 100 };
            if (!LobbyMotion.TryDecode(oldMovementPacket, out _, out Vector2 oldOffset) || oldOffset != new Vector2(0, 0.5f))
                throw new Exception("Lobby movement stopped accepting the earlier preview's coordinates");
            oldMovementPacket[5] = 3;
            oldMovementPacket[9] = 5;
            if (!LobbyMotion.TryDecode(oldMovementPacket, out _, out oldOffset) || oldOffset != new Vector2(0, -0.5f))
                throw new Exception("Lobby movement stopped accepting the first signed coordinates");
            oldMovementPacket[5] = 4;
            oldMovementPacket[9] = 46;
            if (!LobbyMotion.TryDecode(oldMovementPacket, out _, out oldOffset, out respawns, out spawning) ||
                oldOffset != new Vector2(0, -0.5f) || respawns.HasValue || spawning)
                throw new Exception("Lobby movement stopped accepting positions from the last preview");
            movementPacket[8] = 100;
            movementPacket[9] = 255;
            if (!LobbyMotion.TryDecode(movementPacket, out _, out offset, out respawns, out spawning) ||
                offset != Vector2.zero || respawns != 17 || !spawning)
                throw new Exception("An active respawn heartbeat did not decode at the anchor");
            movementPacket[8] = 201;
            if (LobbyMotion.TryDecode(movementPacket, out _, out _) || LobbyMotion.TryDecode(new byte[6], out _, out _))
                throw new Exception("Lobby movement accepted invalid payloads");
            byte[] nativeMovement = { 77, 66, 80, 50, 1, 6, 7, 0, 100, 96, 0, 127 };
            if (!LobbyMotion.TryDecode(nativeMovement, out order, out offset, out respawns, out spawning, out float? angle) ||
                order != 7 || offset != Vector2.zero || respawns != 0 || spawning || angle != 0)
                throw new Exception("Current movement packets did not decode a level ground pose");
            nativeMovement[11] = 255;
            if (!LobbyMotion.TryDecode(nativeMovement, out _, out _, out _, out _, out angle) || angle.HasValue)
                throw new Exception("Current movement packets did not preserve an airborne pose");
            nativeMovement[8] = 200;
            if (!LobbyMotion.TryDecode(nativeMovement, out _, out offset) || Mathf.Abs(offset.x - 2) > 0.001f)
                throw new Exception("Current movement packets clipped the native airborne horizontal travel range");
            byte[] smoothMovement = { 77, 66, 80, 50, 1, 7, 9, 0, 0xe1, 0x93, 0x48, 0x01, 5, 127, 0, 0 };
            if (!LobbyMotion.TryDecode(smoothMovement, out order, out offset, out respawns, out spawning, out angle) ||
                order != 9 || respawns != 5 || spawning || angle != 0 ||
                Vector2.Distance(offset, new Vector2(-27679f / 16000, 328f / 16000)) > 0.00001f)
                throw new Exception("The higher-precision motion packet lost a signed position or rotation");
            smoothMovement[8] = smoothMovement[9] = 0;
            smoothMovement[10] = 255;
            smoothMovement[11] = 127;
            if (!LobbyMotion.TryDecode(smoothMovement, out _, out offset, out _, out spawning, out _) ||
                !spawning || offset != Vector2.zero)
                throw new Exception("The higher-precision motion packet lost the respawn anchor marker");
            smoothMovement[15] = 1;
            if (LobbyMotion.TryDecode(smoothMovement, out _, out _))
                throw new Exception("The higher-precision decoder accepted an unknown reserved flag");
            Main.Log.LogInfo("[UI smoke] PASS: signed movement packets, sequence wrap, and respawn heartbeats");
            yield return new WaitForSecondsRealtime(2);
            while (SteamManager.instance == null || (ulong)SteamManager.instance.currentLobby.Id == 0)
                yield return null;
            var homeVersion = UnityEngine.Object.FindObjectOfType<printText>().GetComponent<TMPro.TextMeshProUGUI>();
            homeVersion.ForceMeshUpdate();
            var homeCanvas = homeVersion.GetComponentInParent<Canvas>();
            var renderedBounds = homeVersion.textBounds;
            var homeCamera = homeCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : homeCanvas.worldCamera ?? Camera.main;
            var footerRight = RectTransformUtility.WorldToScreenPoint(homeCamera,
                homeVersion.rectTransform.TransformPoint(new Vector3(renderedBounds.max.x, 0)));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-main-menu.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            if (homeVersion.text != "2.5.1" || footerRight.x >= Screen.width - 2)
                throw new Exception($"Main menu version extends beyond the right edge ({footerRight.x:0}/{Screen.width}, {homeVersion.text})");
            // Keep desktop input out of the test while it drives the game's menu.
            foreach (var device in InputSystem.devices.ToArray())
                if (device is Keyboard || device is Mouse || device is Gamepad)
                    InputSystem.RemoveDevice(device);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            Screen.SetResolution(1440, 810, FullScreenMode.Windowed);
            SceneManager.LoadScene("ChSelect_online");
            yield return new WaitForSecondsRealtime(2);
            var handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
            if (UnityEngine.Object.FindObjectsOfType<ConnectionDiagnostics>().Length != 1)
                throw new Exception("Persistent connection overlay missing or duplicated");
            var frame = UnityEngine.Object.FindObjectOfType<SteamFrame>();
            int expected = Constants.MAX_PLAYERS - 1;
            if (handler.networkPlayerBoxes.Length != expected || handler.loadingCircles.Length != expected || frame.squares.Count != expected)
                throw new Exception("Incorrect online slot count");
            if (SteamManager.instance.currentLobby.MaxMembers != Constants.MAX_PLAYERS || !CharacterSelectHandler_online.hasMods)
                throw new Exception("Steam capacity or mod matchmaking guard is incorrect");
            if (Mathf.Abs(handler.characterSelectBox.transform.localScale.x - 0.95f) > 0.02f)
                throw new Exception("Local selector was shrunk");
            var originalOverlay = OnlineInviteFriends.OpenOverlay;
            Steamworks.SteamId invitedLobby = default;
            OnlineInviteFriends.OpenOverlay = lobby => invitedLobby = lobby;
            try
            {
                handler.ClickFindButton();
                var inviteMenu = handler.GetComponent<OnlineInviteMenu>();
                if (!inviteMenu.IsOpen || handler.characterSelectBox.enabled)
                    throw new Exception("In-game invitation menu failed to open or capture input");
                var steamButton = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>().First(button => button.name == "SteamInviteWindow");
                var friendPills = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>().Where(image => image.name == "FriendNamePill").ToArray();
                var close = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>().First(button => button.name == "Close");
                var steamLabel = steamButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (friendPills.Length != 3 || friendPills.Any(image => image.type != UnityEngine.UI.Image.Type.Sliced || image.sprite.border == Vector4.zero) ||
                    steamButton.transform.Find("ButtonBorder") == null || steamLabel.enableWordWrapping ||
                    close.GetComponentInChildren<TMPro.TextMeshProUGUI>().text != "X" || close.colors.normalColor.r < 0.5f)
                    throw new Exception("Invite modal lacks fixed-radius styling for friend names and controls");
                var friendList = steamButton.transform.parent.Find("FriendList").GetComponent<UnityEngine.UI.ScrollRect>();
                var listBorder = friendList.transform.Find("FriendListBorder")?.GetComponent<UnityEngine.UI.Image>();
                if (listBorder == null || !listBorder.isActiveAndEnabled || listBorder.raycastTarget ||
                    listBorder.type != UnityEngine.UI.Image.Type.Sliced || listBorder.sprite == null || listBorder.sprite.border == Vector4.zero ||
                    listBorder.transform.GetSiblingIndex() <= friendList.viewport.GetSiblingIndex() ||
                    Vector2.Distance(listBorder.rectTransform.rect.size, friendList.viewport.rect.size) > 0.1f ||
                    friendList.content.GetComponentsInChildren<UnityEngine.UI.Button>().Count(button => button.name == "Invite") != 3)
                    throw new Exception("The friend list lacks a rounded outline over its scrollable Invite controls");
                yield return new WaitForSecondsRealtime(0.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-invite-modal.png"));
                yield return new WaitForSecondsRealtime(0.4f);
                steamButton.onClick.Invoke();
                inviteMenu.Close();
            }
            finally { OnlineInviteFriends.OpenOverlay = originalOverlay; }
            if ((ulong)invitedLobby != (ulong)SteamManager.instance.currentLobby.Id || handler.findPlayersText.text != "INVITE PLAYERS")
                throw new Exception($"Native invite mismatch: actual={(ulong)invitedLobby}, expected={(ulong)SteamManager.instance.currentLobby.Id}, label={handler.findPlayersText.text}");
            Main.Log.LogInfo("[UI smoke] PASS: in-game invite menu opens with an outlined friend list; Steam window targets current lobby (overlay mocked)");
            for (int i = 0; i < expected; i++)
            {
                var box = handler.networkPlayerBoxes[i];
                if (box == null || box.playerNameText == null || box.readyText == null || box.pingText == null)
                    throw new Exception("Incomplete remote slot");
                var animation = box.GetComponent<AnimateInOutUI>();
                if (expected > 3 && box.GetComponentInParent<UnityEngine.UI.RectMask2D>() == null)
                    throw new Exception("Remote card does not have clipping");
                if (animation.originalHeights[0] != handler.networkPlayerBoxes[0].GetComponent<AnimateInOutUI>().originalHeights[0])
                    throw new Exception("Remote slot animation target was not preserved");
                Main.Log.LogInfo($"[UI smoke] slot={i + 2} x={((RectTransform)box.transform).anchoredPosition.x} scale={box.transform.localScale.x} animationTarget={animation.originalHeights[0]}");
            }
            Main.Log.LogInfo($"[UI smoke] PASS: {expected + 1} lobby slots, {expected} loading circles, {frame.squares.Count + 1} avatars; hasMods={CharacterSelectHandler_online.hasMods}; Steam lobby capacity={SteamManager.instance.currentLobby.MaxMembers}");
            var initialRoster = handler.GetComponent<OnlinePlayerRoster>();
            if (initialRoster == null || initialRoster.Cards.Length != Constants.MAX_PLAYERS)
                throw new Exception("Initial roster slot count incorrect");
            if (initialRoster.Cards.Any(card => !card.Root.gameObject.activeSelf || card.Root.sizeDelta != initialRoster.Cards[0].Root.sizeDelta) ||
                initialRoster.Cards[0].Root.sizeDelta.x != initialRoster.Cards[0].Root.sizeDelta.y ||
                initialRoster.Cards.Skip(1).Any(card => card.PracticeAvatar.gameObject.activeSelf || card.AbilityIcons.Any(icon => icon.transform.parent.gameObject.activeSelf)) ||
                UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>().Any(button => button.name == "AddPlayer"))
                throw new Exception("Open slots do not match occupied slots or a duplicate Invite control remains");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-lobby-open.png"));
            yield return new WaitForSecondsRealtime(0.15f);
            Main.Log.LogInfo("[UI smoke] PASS: empty and occupied slots share square geometry and only the native Invite button opens the modal");
            var fakePlayers = new UiTestConnection[expected];
            for (int i = 0; i < expected; i++)
            {
                int color = handler.playerColors[(i + 1) % handler.playerColors.Length].colorIndex;
                fakePlayers[i] = new UiTestConnection(i, color);
                handler.networkPlayerBoxes[i].Init(fakePlayers[i]);
            }
            handler.characterSelectBox.GetComponentInChildren<CharSelectClickToJoin>(true).OnPointerClick(null);
            yield return new WaitForSecondsRealtime(2);
            for (int i = 0; i < expected; i++)
            {
                var box = handler.networkPlayerBoxes[i];
                var rect = (RectTransform)box.transform;
                var animation = box.GetComponent<AnimateInOutUI>();
                if (Mathf.Abs(rect.anchoredPosition.y - animation.originalHeights[0]) > 2 || box.GetComponent<CanvasGroup>().alpha < 0.99f)
                    throw new Exception("Occupied remote card failed to animate into its viewport");
            }
            yield return new WaitForSecondsRealtime(1);
            Main.Log.LogInfo("[UI smoke] PASS: seven synthetic display cards animate in; local selector remains full size");
            var roster = handler.GetComponent<OnlinePlayerRoster>();
            if (roster == null) throw new Exception("Player roster missing");
            var sharedIsland = GameObject.Find("SharedPracticeIsland");
            var viewport = sharedIsland?.transform.Find("PracticeViewport") as RectTransform;
            var artwork = viewport?.Find("PracticePlatform/PlatformArtwork")?.GetComponent<UnityEngine.UI.Image>();
            var water = viewport?.Find("PracticeWater")?.GetComponent<UnityEngine.UI.RawImage>();
            var stageBorder = sharedIsland?.transform.Find("PracticeFrame")?.GetComponent<UnityEngine.UI.Image>();
            if (artwork == null || artwork.sprite?.name != "LongRect" || !artwork.preserveAspect ||
                artwork.GetComponentInParent<UnityEngine.UI.RectMask2D>() == null ||
                water == null || !water.isActiveAndEnabled || !(water.texture is RenderTexture waterSurface) ||
                !waterSurface.IsCreated() || waterSurface.name != "Bopl Lobby WaterGradient" || viewport.Find("PracticeTitle") != null)
                throw new Exception("The viewport does not clip the native island above the water, or still shows its title");
            float spriteAspect = artwork.sprite.rect.width / artwork.sprite.rect.height;
            float islandAspect = artwork.rectTransform.rect.width / artwork.rectTransform.rect.height;
            if (Mathf.Abs(spriteAspect - islandAspect) > 0.001f)
                throw new Exception("The native island's proportions changed");
            if (stageBorder == null || !stageBorder.isActiveAndEnabled || stageBorder.sprite == null ||
                stageBorder.sprite.border == Vector4.zero || viewport.rect.width <= 1480)
                throw new Exception("The expanded play area is missing its border");
            VerifyArenaFits(stageBorder.rectTransform, water.rectTransform);
            foreach (string waveName in new[] { "ShoreWaveLeft", "ShoreWaveRight" })
            {
                var wave = water.transform.Find(waveName)?.GetComponent<UnityEngine.UI.Image>();
                if (wave == null || !wave.isActiveAndEnabled || wave.sprite == null ||
                    !wave.sprite.name.StartsWith("WaterWave_", StringComparison.Ordinal))
                    throw new Exception("Waterline lacks the native " + waveName + " animation");
            }
            if (viewport.GetComponentsInChildren<RectTransform>(true).Any(rect => rect.name == "PracticeNameTag"))
                throw new Exception("The shared play area still has floating player tags");
            var anchor = viewport.Find("RespawnAnchor")?.GetComponent<UnityEngine.UI.RawImage>();
            var arrival = viewport.Find("RespawnArrival")?.GetComponent<UnityEngine.UI.Image>();
            var reviveAssets = Resources.FindObjectsOfTypeAll<AnimationData>();
            var reviveWisp = reviveAssets.First(data => data.name == "Revive").GetAnimation("wisp");
            var reviveArrival = reviveAssets.First(data => data.name == "ReviveEffect").GetAnimation("spawn");
            var nativeSlime = Resources.FindObjectsOfTypeAll<SlimeController>()
                .First(player => !player.gameObject.scene.IsValid() && player.animData?.name == "MCAnimData");
            var nativeSpawn = nativeSlime.animData.GetAnimation("spawn");
            yield return VerifyIdleReviveIndicator(viewport, anchor);
            Main.Log.LogInfo("[UI smoke] PASS: wider, bordered play area preserves the native island aspect ratio and removes floating player tags");
            Main.Log.LogInfo("[UI smoke] PASS: the viewport renders native water and shoreline waves");
            VerifyPracticePhysics();
            var motion = handler.GetComponent<LobbyMotion>();
            if (!motion.UseLongPlatform)
                throw new Exception("The wider native art is using the old capsule collider");
            var selector = handler.characterSelectBox;
            var colorSelector = selector.selectables.OfType<SelectColor>().Single();
            var nativeAbilities = selector.selectables.OfType<SelectAbility>().ToArray();
            selector.SetSelected(selector.selectables.IndexOf(colorSelector));
            int colorBeforeEdit = colorSelector.selectedIndex;
            var keyboard = Keyboard.current;
            if (roster.Cards[0].PracticeAvatar.gameObject.activeSelf)
                throw new Exception("The local player was on the island before pressing Ready");
            yield return TapKey(keyboard, Key.D);
            if (colorSelector.selectedIndex == colorBeforeEdit)
                throw new Exception("D did not edit the selected color before Ready");
            yield return TapKey(keyboard, Key.A);
            if (colorSelector.selectedIndex != colorBeforeEdit)
                throw new Exception("A did not edit the selected color before Ready");
            yield return TapKey(keyboard, Key.S);
            if (selector.selectables[selector.selectedIndex] == colorSelector)
                throw new Exception("S did not navigate the native loadout");
            yield return TapKey(keyboard, Key.W);
            if (selector.selectables[selector.selectedIndex] != colorSelector)
                throw new Exception("W did not return to the native color picker");
            var editorIcons = nativeAbilities
                .OrderByDescending(ability => ScreenBounds(ability.abilityIconRenderer.rectTransform).center.y)
                .Select(ability => ability.abilityIconRenderer.overrideSprite ?? ability.abilityIconRenderer.sprite)
                .ToArray();
            var referenceRing = nativeAbilities[0].BorderRenderer;
            var referenceIcon = nativeAbilities[0].abilityIconRenderer;
            float nativeRatio = ScreenBounds(referenceIcon.rectTransform).width / ScreenBounds(referenceRing.rectTransform).width;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-lobby-editing.png"));
            yield return new WaitForSecondsRealtime(0.15f);
            Main.Log.LogInfo("[UI smoke] PASS: before Ready, WASD edits color and navigates the loadout, and the local avatar is hidden");
            selector.GetComponentInChildren<ReadyButton>(true).OnPointerClick(null);
            yield return new WaitForSecondsRealtime(0.45f);
            if (!CharacterSelectHandler_online.IsLocalPlayerReady() || !roster.Cards[0].PracticeAvatar.gameObject.activeInHierarchy)
                throw new Exception("Pressing Ready did not put the local player on the shared platform");
            if (roster.Cards[0].State.fontSize <= 41 || roster.Cards[0].Name.text.Length == 0)
                throw new Exception("The Ready label was not enlarged or the roster name was removed");
            var idlePortrait = roster.Cards[0].PracticePortrait;
            float platformUnitsPerWorldUnit = artwork.rectTransform.rect.width * artwork.sprite.pixelsPerUnit / artwork.sprite.rect.width;
            float blobUnitsPerWorldUnit = idlePortrait.rectTransform.rect.width * idlePortrait.sprite.pixelsPerUnit / idlePortrait.sprite.rect.width;
            if (Mathf.Abs(platformUnitsPerWorldUnit - blobUnitsPerWorldUnit) > 0.1f)
                throw new Exception($"Platform and player use different world scales ({platformUnitsPerWorldUnit:F2} vs {blobUnitsPerWorldUnit:F2})");
            Main.Log.LogInfo($"[UI smoke] PASS: native platform and player share the same world scale ({platformUnitsPerWorldUnit:F2} canvas units per world unit)");
            var waterline = (Vector2)viewport.InverseTransformPoint(water.rectTransform.TransformPoint(
                new Vector3(0, water.rectTransform.rect.yMax)));
            var orb = waterline + Vector2.up * ((LobbyMotion.ReviveCenterY - motion.WaterHeight) * platformUnitsPerWorldUnit);
            if (orb.y > viewport.rect.yMax - 15 || orb.y < viewport.rect.yMin + viewport.rect.height * 0.8f ||
                Vector2.Distance(orb, WaitingOrbCenter(viewport, anchor)) > 16)
                throw new Exception($"The actual respawn point does not line up with the elevated orb ({orb})");
            int readyColor = selector.playerInit.color;
            var readyMaterial = roster.Cards[0].PracticePortrait.material;
            float startX = motion.GetOffset(0, null).x;
            var moveKey = startX > 0 ? Key.A : Key.D;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(moveKey));
            float walkDeadline = Time.realtimeSinceStartup + 0.45f;
            while (!roster.Cards[0].PracticePortrait.sprite.name.StartsWith("run_CLEAN_", StringComparison.Ordinal) &&
                Time.realtimeSinceStartup < walkDeadline)
                yield return null;
            var walkSprite = roster.Cards[0].PracticePortrait.sprite;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-lobby-walk.png"));
            while (roster.Cards[0].PracticePortrait.sprite == walkSprite && Time.realtimeSinceStartup < walkDeadline)
                yield return null;
            var nextWalkSprite = roster.Cards[0].PracticePortrait.sprite;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.12f);
            if (!walkSprite.name.StartsWith("run_CLEAN_", StringComparison.Ordinal) ||
                !nextWalkSprite.name.StartsWith("run_CLEAN_", StringComparison.Ordinal) || nextWalkSprite == walkSprite)
                throw new Exception("The blob did not advance through Bopl's native run frames");
            if (selector.playerInit.color != readyColor || roster.Cards[0].PracticePortrait.material != readyMaterial ||
                Mathf.Abs(motion.GetOffset(0, null).x - startX) < 0.02f)
                throw new Exception("Keyboard movement after Ready failed or changed the selected color");
            foreach (Key edge in new[] { Key.A, Key.D })
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(edge));
                float deadline = Time.realtimeSinceStartup + 5f;
                bool reachedWater = false;
                bool steeringDown = false;
                while (Time.realtimeSinceStartup < deadline)
                {
                    Vector2 pose = motion.GetPose(0, null, out _, out _, out float bodyAngle);
                    if (motion.TouchesWater(pose, bodyAngle))
                    {
                        reachedWater = true;
                        break;
                    }
                    if (!steeringDown && Mathf.Abs(Mathf.DeltaAngle(0, bodyAngle)) >= 25)
                    {
                        // Native ground controls project the input onto the platform tangent.
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(edge, Key.S));
                        steeringDown = true;
                    }
                    yield return null;
                }
                if (!reachedWater) throw new Exception("Keyboard movement did not reach the water at " + edge);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return VerifyReviveSequence(motion, 0, null, roster.Cards[0], viewport, waterline, orb,
                    platformUnitsPerWorldUnit, arrival, reviveWisp, reviveArrival, nativeSpawn, capture: edge == Key.D);
            }
            if (anchor.color.a < 0.99f)
                throw new Exception("The waiting indicator did not return to full opacity after a respawn");
            yield return VerifyDirectionalJump(keyboard, Key.A, nativeSlime, viewport, roster.Cards[0], motion, "ui-lobby-jump-left.png");
            yield return VerifyDirectionalJump(keyboard, Key.D, nativeSlime, viewport, roster.Cards[0], motion, "ui-lobby-jump.png");
            yield return TapKey(keyboard, Key.Backspace);
            if (roster.Cards[0].PracticeAvatar.gameObject.activeSelf || selector.menuState != CharSelectMenu.select)
                throw new Exception("Unready did not return to loadout editing and hide the island avatar");
            selector.GetComponentInChildren<ReadyButton>(true).OnPointerClick(null);
            yield return new WaitForSecondsRealtime(0.45f);
            Main.Log.LogInfo("[UI smoke] PASS: keyboard runs, jumps, splashes at the waterline, and revives at the orb after falling from either edge");
            var cardRest = roster.Cards[1].Portrait.rectTransform.anchoredPosition;
            foreach (var peer in fakePlayers) peer.lobby_isReady = true;
            yield return new WaitForSecondsRealtime(0.15f);
            var startOnIsland = roster.Cards[1].PracticeAvatar.anchoredPosition;
            var now = Time.unscaledTime;
            for (int i = 0; i < fakePlayers.Length; i++)
            {
                float x = Mathf.Lerp(-0.18f, 0.18f, i / (fakePlayers.Length - 1f));
                motion.Receive(fakePlayers[i].id, 1, new Vector2(x, i == 0 ? 0.35f : 0), now);
            }
            yield return new WaitForSecondsRealtime(0.24f);
            if (roster.Cards[1].Portrait.rectTransform.anchoredPosition != cardRest ||
                roster.Cards[1].PracticeAvatar.anchoredPosition.y <= startOnIsland.y + 10)
                throw new Exception("Shared island movement also moved the roster portrait or failed to move the practice avatar");
            motion.Receive(fakePlayers[0].id, 2, new Vector2(-0.18f, 0), Time.unscaledTime);
            var fallingPeer = fakePlayers[1];
            float waterOffset = motion.WaterHeight / LobbyMotion.JumpViewportHeight;
            motion.Receive(fallingPeer.id, 2, new Vector2(1.25f, waterOffset), Time.unscaledTime);
            float peerFallDeadline = Time.realtimeSinceStartup + 0.6f;
            while (roster.Cards[2].PracticeAvatar.anchoredPosition.y >= roster.Cards[1].PracticeAvatar.anchoredPosition.y - 35 &&
                Time.realtimeSinceStartup < peerFallDeadline)
                yield return null;
            if (roster.Cards[2].PracticeAvatar.anchoredPosition.y >= roster.Cards[1].PracticeAvatar.anchoredPosition.y - 35)
                throw new Exception("A falling peer does not render below the shared island");
            var respawningPeer = fallingPeer;
            motion.Receive(respawningPeer.id, 3, new Vector2(1.25f, waterOffset - 0.08f), Time.unscaledTime, byte.MaxValue);
            var peerWaterPose = new Vector2(1.25f, waterOffset);
            motion.Receive(respawningPeer.id, 4, peerWaterPose, Time.unscaledTime, 0, hasPose: true);
            motion.GetRevive(2, respawningPeer, out var peerOrigin, out float peerRespawnAge, out float peerArrivalAge);
            if (peerArrivalAge >= 0 || Mathf.Abs(peerOrigin.x - 1.25f * LobbyMotion.IslandHalfWidth) > 0.2f)
                throw new Exception($"Remote respawn did not start its wisp from the last water pose ({peerOrigin}, age {peerArrivalAge:F2})");
            yield return new WaitForSecondsRealtime(0.12f);
            motion.Receive(respawningPeer.id, 5, peerWaterPose, Time.unscaledTime, 0, hasPose: true);
            motion.Receive(respawningPeer.id, 4, new Vector2(1.25f, waterOffset - 0.08f), Time.unscaledTime, byte.MaxValue);
            motion.GetRevive(2, respawningPeer, out var afterDuplicateOrigin, out float afterDuplicateAge, out _);
            if (afterDuplicateAge - peerRespawnAge < 0.09f || afterDuplicateOrigin != peerOrigin)
                throw new Exception("Repeated or stale packets restarted a remote respawn animation");
            yield return VerifyReviveSequence(motion, 2, respawningPeer, roster.Cards[2], viewport, waterline, orb,
                platformUnitsPerWorldUnit, arrival, reviveWisp, reviveArrival, nativeSpawn, confirmPeerArrival: () =>
                {
                    motion.GetRevive(2, respawningPeer, out _, out float before, out _);
                    motion.Receive(respawningPeer.id, 6, Vector2.zero, Time.unscaledTime, 0, respawning: true, hasPose: true);
                    motion.GetRevive(2, respawningPeer, out _, out float after, out _);
                    if (after < before) throw new Exception("The peer's arrival heartbeat restarted its revive sequence");
                });
            motion.Receive(respawningPeer.id, 7, new Vector2(-0.12f, 0), Time.unscaledTime, 0);
            Main.Log.LogInfo("[UI smoke] PASS: simulated remote respawn sends one wisp to the elevated orb through counter wrap and stale packets");
            motion.Receive(fakePlayers[0].id, 3, new Vector2(-0.25f, -0.2f), Time.unscaledTime, 0, false, 70f);
            bool peerGrounded;
            float peerAngle;
            float peerPoseDeadline = Time.realtimeSinceStartup + 0.6f;
            do
            {
                yield return null;
                motion.GetPose(1, fakePlayers[0], out _, out peerGrounded, out peerAngle);
            } while ((!peerGrounded || Mathf.Abs(Mathf.DeltaAngle(peerAngle, 70)) > 3) &&
                Time.realtimeSinceStartup < peerPoseDeadline);
            yield return null;
            float portraitAngle = roster.Cards[1].PracticePortrait.rectTransform.localEulerAngles.z;
            if (!peerGrounded || Mathf.Abs(Mathf.DeltaAngle(peerAngle, portraitAngle)) > 2 ||
                Mathf.Abs(Mathf.DeltaAngle(peerAngle, 70)) > 3)
                throw new Exception("The remote portrait did not follow the received native platform rotation");
            yield return VerifyPeerInterpolation(motion, fakePlayers[fakePlayers.Length - 1], fakePlayers.Length);
            // Spread the simulated players across the flat grass, the slopes,
            // the air, and the water for the eight-player layout screenshot.
            var previewPositions = new[]
            {
                new Vector2(-0.42f, 0), new Vector2(0.38f, 0),
                new Vector2(-1.08f, -0.14f), new Vector2(1.145f, -0.30f),
                new Vector2(-0.72f, 0.48f), new Vector2(0.66f, 0.35f),
                new Vector2(1.27f, waterOffset)
            };
            var previewAngles = new float?[] { 0, 0, 35, -54, null, null, null };
            for (int i = 0; i < fakePlayers.Length; i++)
                motion.Receive(fakePlayers[i].id, 8, previewPositions[i], Time.unscaledTime, 0, false, previewAngles[i], true);
            yield return new WaitForSecondsRealtime(0.16f);
            handler.ClickFindButton();
            if (!handler.GetComponent<OnlineInviteMenu>().IsOpen) throw new Exception("Native Invite button did not open invitations");
            handler.GetComponent<OnlineInviteMenu>().Close();
            yield return new WaitForSecondsRealtime(0.2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, $"ui-{Main.ModVersion}.png"));
            if (roster.LocalEditor != handler.characterSelectBox.transform ||
                handler.characterSelectBox.abilityGrid == null || handler.characterSelectBox.rebindImageControllerImage == null ||
                handler.characterSelectBox.animateReady == null || !handler.characterSelectBox.isActiveAndEnabled)
                throw new Exception($"Original local editor missing: transform={roster.LocalEditor == handler.characterSelectBox.transform}, grid={handler.characterSelectBox.abilityGrid != null}, keybind={handler.characterSelectBox.rebindImageControllerImage != null}, ready={handler.characterSelectBox.animateReady != null}, enabled={handler.characterSelectBox.isActiveAndEnabled}");
            var editorPosition = roster.LocalEditor.GetComponentInParent<Canvas>().transform.InverseTransformPoint(roster.LocalEditor.position);
            if (editorPosition.x <= 0 || handler.startBorder.sprite != handler.findPlayersBorder.sprite ||
                handler.startText.font != handler.findPlayersText.font || handler.findPlayersButton.transform.Find("InviteOutline") != null)
                throw new Exception("Native editor is outside its right panel or Start and Invite do not match");
            for (int i = 0; i < roster.Cards.Length; i++)
            {
                var card = roster.Cards[i];
                if (card.Root.GetComponent<UnityEngine.UI.Button>() != null || card.Border.sprite.border == Vector4.zero)
                    throw new Exception("Roster card is interactive or lacks rounded corners");
                if (!card.PracticeAvatar.gameObject.activeInHierarchy || card.Name.text.Length == 0 ||
                    card.PracticeAvatar.GetComponentInParent<UnityEngine.UI.RectMask2D>() == null)
                    throw new Exception("Occupied player is missing from the shared practice island");
                if (card.AbilityIcons.Length != 3 || card.AbilityIcons.Any(icon => !icon.enabled || icon.sprite == null))
                    throw new Exception("Ready player did not show three abilities under the card");
                for (int slot = 0; slot < card.AbilityIcons.Length; slot++)
                {
                    var icon = card.AbilityIcons[slot];
                    var tile = (RectTransform)icon.transform.parent;
                    var ring = tile.Find("AbilityCircle")?.GetComponent<UnityEngine.UI.Image>();
                    if (ring == null || !ring.isActiveAndEnabled || tile.GetComponent<UnityEngine.UI.Graphic>() != null ||
                        tile.Find("TileBorder") != null || ring.sprite != (referenceRing.overrideSprite ?? referenceRing.sprite))
                        throw new Exception("The roster icon adds a frame or uses the wrong native circle");
                    float rosterRatio = icon.rectTransform.rect.width / ring.rectTransform.rect.width;
                    if (Mathf.Abs(nativeRatio - rosterRatio) > 0.02f)
                        throw new Exception($"Ability icon proportions differ from the selector ({rosterRatio:F3} vs {nativeRatio:F3})");
                    if (i == 0 && icon.sprite != editorIcons[slot])
                        throw new Exception("The roster abilities are not in the native editor's visible order");
                }
            }
            Main.Log.LogInfo("[UI smoke] PASS: larger Ready labels, roster names, and the editor's native circular ability icons");
            var manager = SteamManager.instance;
            var originalKick = OnlinePlayerRoster.SendKick;
            int kickedIndex = -1;
            try
            {
                manager.connectedPlayers.AddRange(fakePlayers);
                var diagnostics = UnityEngine.Object.FindObjectOfType<ConnectionDiagnostics>();
                diagnostics.enabled = false;
                yield return null;
                if (!roster.Cards[5].KickButton.gameObject.activeInHierarchy || !roster.Cards[5].KickButton.IsInteractable())
                    throw new Exception("Host kick is not visible and clickable");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, $"ui-{Main.ModVersion}.png"));
                yield return new WaitForSecondsRealtime(0.2f);
                yield return VerifyResizedArena(stageBorder.rectTransform, water.rectTransform, 1725, 942, 1366, 746,
                    "ui-lobby-attached-aspect.png");
                yield return VerifyResizedArena(stageBorder.rectTransform, water.rectTransform, 1280, 640, 0, 0,
                    "ui-lobby-short-window.png");
                yield return ResizeWindow(1440, 810);
                if (!roster.CanKick(fakePlayers[4])) throw new Exception("Host cannot kick this remote player");
                manager.connectedPlayers.Remove(fakePlayers[0]);
                int expectedKickIndex = manager.connectedPlayers.IndexOf(fakePlayers[4]);
                OnlinePlayerRoster.SendKick = index => kickedIndex = index;
                roster.Cards[5].KickButton.onClick.Invoke();
                if (kickedIndex != expectedKickIndex)
                    throw new Exception("Kick did not resolve current player index");
                if (roster.CanKick(null) || roster.Cards[0].KickButton != null || HostOnlyKickPatch.Prefix(manager, -1) ||
                    HostOnlyKickPatch.Prefix(manager, manager.connectedPlayers.Count))
                    throw new Exception("Kick guard allowed local player or invalid index");
                var originalSend = HostOnlyKickPatch.SendMessage;
                string kickMessage = null;
                try
                {
                    HostOnlyKickPatch.SendMessage = (state, message) => false;
                    HostOnlyKickPatch.Prefix(manager, expectedKickIndex);
                    if (fakePlayers[4].beingKicked) throw new Exception("Failed Steam kick blocked a retry");
                    HostOnlyKickPatch.SendMessage = (state, message) => { kickMessage = message; return true; };
                    HostOnlyKickPatch.Prefix(manager, expectedKickIndex);
                    if (kickMessage != HostOnlyKickPatch.MessagePrefix + (ulong)fakePlayers[4].id || !fakePlayers[4].beingKicked)
                        throw new Exception("Host kick did not use Steam identity");
                    string localKick = HostOnlyKickPatch.MessagePrefix + (ulong)Steamworks.SteamClient.SteamId;
                    if (!HostKickMessagePatch.Accept(manager, manager.currentLobby, manager.currentLobby.Owner.Id, localKick)
                        || HostKickMessagePatch.Accept(manager, manager.currentLobby, fakePlayers[4].id, localKick)
                        || HostKickMessagePatch.Accept(manager, manager.currentLobby, manager.currentLobby.Owner.Id, kickMessage))
                        throw new Exception("Kick message accepted wrong sender or target");
                }
                finally { HostOnlyKickPatch.SendMessage = originalSend; }
            }
            finally
            {
                OnlinePlayerRoster.SendKick = originalKick;
                foreach (var peer in fakePlayers) manager.connectedPlayers.Remove(peer);
                UnityEngine.Object.FindObjectOfType<ConnectionDiagnostics>().enabled = true;
            }
            Main.Log.LogInfo("[UI smoke] PASS: passive rounded slots, three ability icons per player, native editor and Start controls, and Steam-ID kicks (send mocked)");
            yield return new WaitForSecondsRealtime(1);
            for (int i = 0; i < expected; i++) fakePlayers[i].Disconnect();
            yield return new WaitForSecondsRealtime(1);
            for (int i = 0; i < expected; i++)
                if (handler.networkPlayerBoxes[i].GetComponent<CanvasGroup>().alpha > 0.01f)
                    throw new Exception("Disconnected display did not hide");
            Main.Log.LogInfo("[UI smoke] PASS: disconnected cards hide cleanly");
            if (roster.Cards.Skip(1).Any(card => card.AbilityIcons.Any(icon => icon.enabled))) throw new Exception("Roster retained a disconnected peer's kit");
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                selector.OnEnterSelect();
                selector.OnEnterJoin();
                selector.currentGamePad = pad;
                selector.OnEnterSelect();
                selector.SetSelected(selector.selectables.IndexOf(colorSelector));
                int padColor = colorSelector.selectedIndex;
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right });
                yield return new WaitForSecondsRealtime(0.12f);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return new WaitForSecondsRealtime(0.12f);
                if (colorSelector.selectedIndex == padColor)
                    throw new Exception("Controller stick did not edit color before Ready");
                selector.SetSelected(selector.selectables.FindIndex(item => item is ReadyButton));
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
                yield return new WaitForSecondsRealtime(0.12f);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return new WaitForSecondsRealtime(0.25f);
                if (!CharacterSelectHandler_online.IsLocalPlayerReady())
                    throw new Exception("Controller south button did not Ready");
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
                yield return new WaitForSecondsRealtime(0.14f);
                if (motion.GetOffset(0, null).y < 0.18f || handler.GetComponent<OnlineInviteMenu>().IsOpen)
                    throw new Exception("Controller south button failed to jump or opened Invite");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                float padLandDeadline = Time.realtimeSinceStartup + 2f;
                Vector2 padPose;
                bool padGrounded;
                do
                {
                    yield return null;
                    padPose = motion.GetPose(0, null, out _, out padGrounded, out _);
                } while ((!padGrounded || Mathf.Abs(padPose.y) > 0.003f) && Time.realtimeSinceStartup < padLandDeadline);
                if (!padGrounded || Mathf.Abs(padPose.y) > 0.003f)
                    throw new Exception($"The controller-controlled blob did not land on the platform (position {padPose}, grounded {padGrounded})");
                float padX = padPose.x;
                float padDirection = padX > 0 ? -1 : 1;
                padColor = selector.playerInit.color;
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(padDirection, 0) });
                yield return new WaitForSecondsRealtime(0.14f);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return new WaitForSecondsRealtime(0.12f);
                if ((motion.GetOffset(0, null).x - padX) * padDirection <= 0.02f || selector.playerInit.color != padColor)
                    throw new Exception("Controller stick failed to move or changed color after Ready");
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East));
                yield return new WaitForSecondsRealtime(0.16f);
                if (selector.menuState != CharSelectMenu.select || roster.Cards[0].PracticeAvatar.gameObject.activeSelf)
                    throw new Exception("Controller east button failed to unready");
                Main.Log.LogInfo("[UI smoke] PASS: simulated controller edits color before Ready, then moves and jumps, and can Unready");
            }
            finally
            {
                selector.currentGamePad = null;
                InputSystem.RemoveDevice(pad);
            }
            SceneManager.LoadScene("MainMenu");
            yield return new WaitForSecondsRealtime(1);
            SceneManager.LoadScene("ChSelect_online");
            yield return new WaitForSecondsRealtime(1);
            handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
            if (handler.networkPlayerBoxes.Length != expected || (expected > 3 && handler.GetComponent<OnlineLobbyEmptySlots>() == null))
                throw new Exception("UI patch failed after scene re-entry");
            Main.Log.LogInfo("[UI smoke] PASS: online scene re-entry");
            // BepInEx's disk listener flushes periodically; retain the final result.
            yield return new WaitForSecondsRealtime(3);
            Application.Quit();
        }

        static IEnumerator TapKey(Keyboard keyboard, Key key, float hold = 0.08f)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return new WaitForSecondsRealtime(hold);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.1f);
        }

        static IEnumerator VerifyIdleReviveIndicator(RectTransform viewport, UnityEngine.UI.RawImage anchor)
        {
            var native = Resources.FindObjectsOfTypeAll<RevivePositionIndicator>()
                .FirstOrDefault(indicator => !indicator.gameObject.scene.IsValid());
            var preview = GameObject.Find("Lobby Revive Preview")?.transform.Find("Lobby Revive Indicator");
            if (native == null || preview == null || anchor == null || !anchor.isActiveAndEnabled ||
                !(anchor.texture is Texture2D surface) || surface.name != "Lobby Revive Indicator")
                throw new Exception("The lobby is not rendering Bopl's native waiting revive indicator");

            var nativeParticles = native.GetComponentsInChildren<ParticleSystem>(true);
            var previewParticles = preview.GetComponentsInChildren<ParticleSystem>(true);
            if (previewParticles.Length != nativeParticles.Length || previewParticles.Length != 3 ||
                preview.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                throw new Exception("The lobby revive indicator does not isolate the three native particle systems");
            for (int i = 0; i < previewParticles.Length; i++)
            {
                var captured = previewParticles[i].GetComponent<ParticleSystemRenderer>();
                var original = nativeParticles[i].GetComponent<ParticleSystemRenderer>();
                if (previewParticles[i].main.loop != nativeParticles[i].main.loop || captured.sharedMaterial != original.sharedMaterial)
                    throw new Exception("A waiting revive particle system changed its native appearance or loop");
            }

            var rect = anchor.rectTransform;
            var position = rect.anchoredPosition;
            var rotation = rect.localRotation;
            var top = viewport.InverseTransformPoint(rect.TransformPoint(new Vector3(0, rect.rect.yMax, 0))).y;
            float topInset = viewport.rect.yMax - top;
            if (Mathf.Abs(position.x) > 1 || position.y < viewport.rect.yMin + viewport.rect.height * 0.75f ||
                topInset < 2 || topInset > 32)
                throw new Exception($"The waiting revive indicator is not centered near the top of the viewport ({position}, top inset {topInset:F1})");

            var first = surface.GetPixels32();
            var particleTimes = previewParticles.Select(system => system.time).ToArray();
            yield return new WaitForSecondsRealtime(0.24f);
            var second = surface.GetPixels32();
            if (rect.anchoredPosition != position || rect.localRotation != rotation || anchor.color.a < 0.99f)
                throw new Exception("The idle revive indicator moved, rotated, or faded while waiting");
            int firstVisible = first.Count(pixel => pixel.a > 8);
            int secondVisible = second.Count(pixel => pixel.a > 8);
            int changed = first.Where((pixel, i) => !pixel.Equals(second[i])).Count();
            if (!previewParticles.Any(system => system.particleCount > 0) ||
                !previewParticles.Where((system, i) => Mathf.Abs(system.time - particleTimes[i]) > 0.02f).Any() ||
                firstVisible < 8 || secondVisible < 8 || changed < 8)
            {
                var systems = previewParticles.Select((system, i) =>
                {
                    var bounds = system.GetComponent<ParticleSystemRenderer>().bounds;
                    var particle = new ParticleSystem.Particle[1];
                    float life = system.GetParticles(particle) > 0 ? particle[0].remainingLifetime : 0;
                    return $"{system.name} playing={system.isPlaying}, time={particleTimes[i]:F2}->{system.time:F2}, " +
                        $"count={system.particleCount}, lifetime={life:F2}, bounds={bounds}";
                });
                throw new Exception($"The native revive particles did not visibly animate while waiting. " +
                    $"Visible pixels={firstVisible}/{secondVisible}, changed={changed}, max alpha={second.Max(pixel => pixel.a)}, " +
                    $"max RGB={second.Max(pixel => Mathf.Max(pixel.r, pixel.g, pixel.b))}. {string.Join("; ", systems)}");
            }

            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-lobby-orb-idle.png"));
            Main.Log.LogInfo($"[UI smoke] PASS: all three native revive particle systems animate at a fixed point near the top of the viewport (inset {topInset:F1})");
        }

        static IEnumerator VerifyReviveSequence(LobbyMotion motion, int slot, SteamConnection peer, OnlinePlayerRoster.Card card,
            RectTransform viewport, Vector2 waterline, Vector2 orb, float units, UnityEngine.UI.Image arrival,
            SpriteFrame[] wispFrames, SpriteFrame[] arrivalFrames, SpriteFrame[] spawnFrames, bool capture = false,
            Action confirmPeerArrival = null)
        {
            var splashSprites = new HashSet<Sprite>();
            var wispSprites = new HashSet<Sprite>();
            var nativeWisp = new HashSet<Sprite>(wispFrames.Select(frame => frame.sprite));
            var nativeArrival = new HashSet<Sprite>(arrivalFrames.Select(frame => frame.sprite));
            var nativeSpawn = new HashSet<Sprite>(spawnFrames.Select(frame => frame.sprite));
            bool sawArrival = false;
            bool sawSpawn = false;
            bool sawFall = false;
            bool finished = false;
            bool capturedFlight = false;
            bool capturedArrival = false;
            bool capturedSpawn = false;
            int flightSteps = 0;
            float flightDistance = float.PositiveInfinity;
            float deadline = Time.realtimeSinceStartup + 5.5f;
            while (Time.realtimeSinceStartup < deadline && !finished)
            {
                yield return null;
                Vector2 position = motion.GetPose(slot, peer, out float spawnAge, out bool grounded, out _);
                bool reviving = motion.GetRevive(slot, peer, out Vector2 origin, out float age, out float arrivalAge);
                if (card.Splash.isActiveAndEnabled && card.Splash.sprite != null)
                {
                    var splash = card.Splash;
                    var splashPosition = ViewportPivot(viewport, splash.rectTransform);
                    if (!splash.sprite.name.StartsWith("WaterSplash_", StringComparison.Ordinal) ||
                        Mathf.Abs(splashPosition.y - waterline.y) > 2 ||
                        reviving && Mathf.Abs(splashPosition.x - origin.x * units) > 20)
                        throw new Exception($"Slot {slot} splash is detached from the death waterline ({splashPosition}, {origin})");
                    if (splashSprites.Add(splash.sprite) && splashSprites.Count == 2 && capture)
                        CaptureLobbyFrame("ui-lobby-water.png");
                }

                var wisp = card.ReviveWisp;
                if (wisp != null && wisp.isActiveAndEnabled && nativeWisp.Contains(wisp.sprite))
                {
                    Vector2 expected = waterline + origin * units - Vector2.up * (motion.WaterHeight * units);
                    if (!reviving || arrivalAge > 0.04f || Vector2.Distance(ViewportPivot(viewport, wisp.rectTransform), expected) > 8)
                        throw new Exception($"Slot {slot} wisp did not begin at the water death ({ViewportPivot(viewport, wisp.rectTransform)} vs {expected})");
                    if (age > 0.06f && card.PracticePortrait.isActiveAndEnabled)
                        throw new Exception($"Slot {slot} blob remained visible while waiting for the revive wisp");
                    wispSprites.Add(wisp.sprite);
                }

                var flight = card.ReviveFlight;
                if (flight != null && flight.isActiveAndEnabled && flight.sprite != null)
                {
                    Vector2 point = ViewportPivot(viewport, flight.rectTransform);
                    float distance = Vector2.Distance(point, orb);
                    if (distance > flightDistance + 4 || point.y < waterline.y - 5 || point.y > orb.y + 5)
                        throw new Exception($"Slot {slot} revive projectile moved away from the orb ({point}, {flightDistance:F1}->{distance:F1})");
                    if (distance < flightDistance - 2) flightSteps++;
                    if (capture && !capturedFlight && Mathf.InverseLerp(waterline.y, orb.y, point.y) > 0.5f)
                    {
                        CaptureLobbyFrame("ui-lobby-revive-flight.png");
                        capturedFlight = true;
                    }
                    flightDistance = distance;
                }

                if (arrival != null && arrival.isActiveAndEnabled && nativeArrival.Contains(arrival.sprite))
                {
                    if (Vector2.Distance(ViewportPivot(viewport, arrival.rectTransform), orb) > 4)
                        throw new Exception($"Slot {slot} native arrival effect missed the orb");
                    if (!sawArrival) confirmPeerArrival?.Invoke();
                    if (capture && !capturedArrival && arrivalAge > 0.2f)
                    {
                        CaptureLobbyFrame("ui-lobby-respawn.png");
                        capturedArrival = true;
                    }
                    sawArrival = true;
                }

                if (spawnAge >= 0 && spawnAge < LobbyMotion.RespawnSeconds - 0.05f &&
                    card.PracticePortrait.isActiveAndEnabled && nativeSpawn.Contains(card.PracticePortrait.sprite))
                {
                    Vector2 portrait = ViewportPivot(viewport, card.PracticePortrait.rectTransform);
                    float expectedFeet = (LobbyMotion.ReviveCenterY - motion.PlayerRadius) / LobbyMotion.JumpViewportHeight;
                    if (grounded || Mathf.Abs(position.x) > 0.02f || Mathf.Abs(position.y - expectedFeet) > 0.02f ||
                        Vector2.Distance(portrait, orb) > 4)
                        throw new Exception($"Slot {slot} blob did not spawn centered at the orb ({position}, center {portrait} vs {orb})");
                    if (capture && !capturedSpawn && arrivalAge > 0.95f)
                    {
                        CaptureLobbyFrame("ui-lobby-respawn-late.png");
                        capturedSpawn = true;
                    }
                    sawSpawn = true;
                }

                if (sawSpawn && !reviving && !grounded && position.y > 0.12f &&
                    position.y * LobbyMotion.JumpViewportHeight < LobbyMotion.ReviveCenterY - motion.PlayerRadius - 0.5f)
                    sawFall = true;
                finished = sawSpawn && !reviving && (slot != 0 || sawFall && grounded && Mathf.Abs(position.y) < 0.01f);
            }

            if (splashSprites.Count < 2 || wispSprites.Count < 3 || flightSteps < 3 || flightDistance > 75 ||
                !sawArrival || !sawSpawn || !finished || capture && !(capturedFlight && capturedArrival && capturedSpawn))
                throw new Exception($"Slot {slot} did not complete the native revive sequence. Splash frames={splashSprites.Count}, " +
                    $"wisp frames={wispSprites.Count}, travel steps={flightSteps}, final orb gap={flightDistance:F1}, " +
                    $"arrival={sawArrival}, spawn={sawSpawn}, fall={sawFall}, finished={finished}, " +
                    $"screenshots={capturedFlight}/{capturedArrival}/{capturedSpawn}");
        }

        static Vector2 ViewportPivot(RectTransform viewport, RectTransform effect) =>
            viewport.InverseTransformPoint(effect.position);

        static Vector2 WaitingOrbCenter(RectTransform viewport, UnityEngine.UI.RawImage anchor)
        {
            var texture = (Texture2D)anchor.texture;
            var pixels = texture.GetPixels32();
            Vector2 center = Vector2.zero;
            float weight = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 64 || pixels[i].g < 180) continue;
                center += new Vector2(i % texture.width + 0.5f, i / texture.width + 0.5f) * pixels[i].a;
                weight += pixels[i].a;
            }
            if (weight == 0) throw new Exception("The waiting orb texture has no visible center");
            center /= weight;
            var rect = anchor.rectTransform.rect;
            var local = new Vector2(rect.xMin + rect.width * center.x / texture.width,
                rect.yMin + rect.height * center.y / texture.height);
            return viewport.InverseTransformPoint(anchor.rectTransform.TransformPoint(local));
        }

        static void CaptureLobbyFrame(string filename) =>
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, filename));

        static IEnumerator VerifyDirectionalJump(Keyboard keyboard, Key direction, SlimeController nativeSlime,
            RectTransform viewport, OnlinePlayerRoster.Card card, LobbyMotion motion, string screenshot)
        {
            var native = nativeSlime.animData;
            var air = new HashSet<Sprite>(native.GetVelocityMap("fallLeft").Select(frame => frame.sprite));
            var loop = new HashSet<Sprite>(native.GetAnimation("fallLeftLoop").Select(frame => frame.sprite));
            var land = new HashSet<Sprite>(native.GetAnimation("land").Select(frame => frame.sprite));
            var rect = card.PracticePortrait.rectTransform;
            var flip = nativeSlime.GetComponentInChildren<FlippingTransform>(true);
            if (flip == null) throw new Exception("Bopl's native facing transform is missing");
            float facing = Mathf.Sign(direction == Key.A ? flip.faceLeft.x : flip.faceRight.x);
            var renderedFrame = new WaitForEndOfFrame();

            // Establish facing on the ground, then jump without horizontal input.
            yield return TapKey(keyboard, direction, direction == Key.A ? 0.22f : 0.33f);
            yield return renderedFrame;
            if (Mathf.Sign(rect.localScale.x) != facing)
                throw new Exception($"The blob did not face {direction} on the ground before jumping");

            float groundY = card.PracticeAvatar.anchoredPosition.y;
            float previousY = groundY;
            float lastFrameTime = Time.unscaledTime;
            float deadline = Time.realtimeSinceStartup + 2;
            var ascentFrames = new HashSet<Sprite>();
            var descentFrames = new HashSet<Sprite>();
            int ascentSteps = 0;
            int descentSteps = 0;
            bool sawApex = false;
            bool sawLanding = false;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            try
            {
                while (Time.realtimeSinceStartup < deadline && !sawLanding)
                {
                    yield return renderedFrame;
                    float y = card.PracticeAvatar.anchoredPosition.y;
                    float delta = y - previousY;
                    float elapsed = Time.unscaledTime - lastFrameTime;
                    lastFrameTime = Time.unscaledTime;
                    if (y - groundY > 30 && previousY - groundY > 30)
                    {
                        var sprite = card.PracticePortrait.sprite;
                        if (air.Contains(sprite))
                        {
                            if (Mathf.Sign(rect.localScale.x) != facing)
                                throw new Exception($"The blob turned away from {direction} during its jump ({sprite.name})");
                            if (delta > 0.1f) ascentFrames.Add(sprite);
                            if (delta < -0.1f) descentFrames.Add(sprite);
                        }
                        else if (!loop.Contains(sprite))
                            throw new Exception($"The airborne blob skipped to a non-airborne sprite ({sprite?.name})");

                        if (Mathf.Abs(delta) > 2100 * elapsed + 4 || (sawApex && delta > 0.1f))
                            throw new Exception($"The jump for {direction} had an uneven rendered step ({delta:F2} units in {elapsed:F3} seconds)");
                        if (delta > 0.1f) ascentSteps++;
                        if (delta < -0.1f)
                        {
                            descentSteps++;
                            if (!sawApex)
                            {
                                sawApex = true;
                                var upperInset = UnityEngine.Sprites.DataUtility.GetPadding(sprite).w;
                                var visibleTop = rect.rect.yMax - upperInset * rect.rect.height / sprite.rect.height;
                                var topInViewport = viewport.InverseTransformPoint(rect.TransformPoint(new Vector3(0, visibleTop, 0))).y;
                                if (topInViewport > viewport.rect.yMax - 3)
                                    throw new Exception($"The native jump is clipped at the top ({topInViewport:F1} vs {viewport.rect.yMax:F1})");
                                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, screenshot));
                            }
                        }
                    }

                    motion.GetPose(0, null, out _, out bool grounded, out _);
                    sawLanding = sawApex && grounded && Mathf.Abs(y - groundY) < 1 && land.Contains(card.PracticePortrait.sprite);
                    previousY = y;
                }
            }
            finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }

            if (!sawLanding || ascentSteps < 4 || descentSteps < 4 ||
                ascentFrames.Count < 2 || descentFrames.Count < 2)
                throw new Exception($"The {direction} jump did not complete with native rising, falling, and landing frames " +
                    $"({ascentFrames.Count}/{descentFrames.Count} unique frames, {ascentSteps}/{descentSteps} moving steps, landed {sawLanding})");
            Main.Log.LogInfo($"[UI smoke] PASS: {direction} jump kept its native facing, used {ascentFrames.Count}/{descentFrames.Count} distinct native rise/fall sprites over {ascentSteps}/{descentSteps} moving frames, and played native landing");
        }

        static IEnumerator VerifyResizedArena(RectTransform stage, RectTransform water, int width, int height,
            int fallbackWidth, int fallbackHeight, string screenshot)
        {
            yield return ResizeWindow(width, height);
            if ((Screen.width != width || Screen.height != height) && fallbackWidth > 0)
                yield return ResizeWindow(fallbackWidth, fallbackHeight);
            if (Mathf.Abs((float)Screen.width / Screen.height - (float)width / height) > 0.02f)
                throw new Exception($"Unity did not reach the requested test aspect {width}x{height} (actual {Screen.width}x{Screen.height})");
            VerifyArenaFits(stage, water);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, screenshot));
            yield return new WaitForSecondsRealtime(0.2f);
            Main.Log.LogInfo($"[UI smoke] PASS: framed island and entire waterline fit at {Screen.width}x{Screen.height}");
        }

        static IEnumerator ResizeWindow(int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            float deadline = Time.realtimeSinceStartup + 0.8f;
            while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return new WaitForSecondsRealtime(0.15f);
        }

        static IEnumerator VerifyPeerInterpolation(LobbyMotion motion, SteamConnection peer, int slot)
        {
            float start = Time.unscaledTime - 0.10f;
            motion.Receive(peer.id, 4, new Vector2(-0.45f, 0), start, 0, false, 0, true);
            motion.Receive(peer.id, 5, new Vector2(-0.40f, 0), start + 0.045f, 0, false, 0, true);
            motion.Receive(peer.id, 6, new Vector2(-0.35f, 0), start + 0.09f, 0, false, 0, true);
            float previousX = motion.GetOffset(slot, peer).x;
            float deadline = Time.realtimeSinceStartup + 0.24f;
            int advances = 0;
            while (previousX < -0.351f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                float x = motion.GetOffset(slot, peer).x;
                if (x < previousX - 0.002f || x - previousX > 0.079f)
                    throw new Exception($"The simulated peer jumped between samples ({previousX:F3} to {x:F3})");
                if (x > previousX + 0.001f) advances++;
                previousX = x;
            }
            if (advances < 2 || previousX < -0.351f)
                throw new Exception($"The simulated peer did not interpolate between motion packets ({advances} rendered steps)");
            Main.Log.LogInfo($"[UI smoke] PASS: the simulated peer advanced smoothly over {advances} frames between packets");
        }

        static void VerifyArenaFits(RectTransform stage, RectTransform water)
        {
            Rect stageBounds = ScreenBounds(stage);
            Rect waterBounds = ScreenBounds(water);
            if (stageBounds.xMin < 5 || stageBounds.xMax > Screen.width - 5 ||
                stageBounds.yMin < 5 || stageBounds.yMax > Screen.height - 5)
                throw new Exception($"The arena border overflows the screen ({stageBounds}, {Screen.width}x{Screen.height})");
            if (waterBounds.yMin < 2 || waterBounds.yMax - stageBounds.yMin < 12 ||
                waterBounds.xMin > stageBounds.xMin + 3 || waterBounds.xMax < stageBounds.xMax - 3)
                throw new Exception($"The entire shoreline and water band do not fit inside the arena ({waterBounds}, arena {stageBounds})");
        }

        static Rect ScreenBounds(RectTransform transform)
        {
            var canvas = transform.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera ?? Camera.main;
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            var points = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(camera, corner)).ToArray();
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
                points.Max(point => point.x), points.Max(point => point.y));
        }

        static void VerifyPracticePhysics()
        {
            using var island = new LobbyMotion.PracticePhysics(useLongPlatform: true);
            var slime = Resources.FindObjectsOfTypeAll<SlimeController>()
                .FirstOrDefault(player => !player.gameObject.scene.IsValid() && player.animData?.name == "MCAnimData");
            if (slime == null)
                throw new Exception("Bopl's native player prefab is missing from the online scene");
            var native = slime.GetComponent<PlayerPhysics>();
            if (Mathf.Abs(island.MaxSpeed - (float)native.Speed) > 0.001f ||
                Mathf.Abs(island.Acceleration - (float)native.accel) > 0.001f)
                throw new Exception("The lobby did not use the native player prefab's max speed and acceleration");

            island.Reset(0);
            float expectedSpeed = 0;
            for (int tick = 0; tick < 6; tick++)
            {
                island.Step(Vector2.right, false);
                expectedSpeed = Mathf.Min(island.MaxSpeed, expectedSpeed + island.Acceleration * LobbyMotion.FixedStep);
                if (!island.IsGrounded || Mathf.Abs(island.Velocity.x - expectedSpeed) > 0.02f)
                    throw new Exception($"Ground acceleration diverged from Bopl on frame {tick + 1}: velocity {island.Velocity.x:F3} vs {expectedSpeed:F3}, position {island.Position}, grounded {island.IsGrounded}");
            }
            island.Step(Vector2.left, false);
            expectedSpeed = -Mathf.Min(island.MaxSpeed, island.Acceleration * LobbyMotion.FixedStep);
            if (Mathf.Abs(island.Velocity.x - expectedSpeed) > 0.02f)
                throw new Exception("Reversing direction did not use Bopl's native turn acceleration");

            island.Reset(0);
            island.Step(Vector2.zero, true);
            if (island.IsGrounded || island.Position.y <= 0 || island.Velocity.y <= 0)
                throw new Exception("The lobby blob did not jump");
            for (int tick = 0; tick < 180 && !island.IsGrounded; tick++)
                island.Step(Vector2.zero, false);
            if (!island.IsGrounded || island.Position.sqrMagnitude > 0.0025f)
                throw new Exception("The lobby blob did not land back on the island");

            foreach (float direction in new[] { -1f, 1f })
            {
                island.Reset(0);
                bool touchedWater = false;
                bool stuckToShoulder = false;
                bool respawned = false;
                for (int tick = 0; tick < 240; tick++)
                {
                    var input = Mathf.Abs(Mathf.DeltaAngle(0, island.Angle)) >= 25
                        ? new Vector2(direction, -1) : Vector2.right * direction;
                    respawned = island.Step(input, false);
                    stuckToShoulder |= island.IsGrounded && island.Position.y < -0.1f;
                    touchedWater |= island.TouchesWater;
                    if (respawned) break;
                }
                if (!stuckToShoulder || !touchedWater || !respawned || !island.TouchesWater)
                    throw new Exception($"Rounded edge test failed for direction {direction}: shoulder {stuckToShoulder}, water {touchedWater}, respawned {respawned}, position {island.Position}, angle {island.Angle:F1}");
                island.PlaceAtReviveOrb();
                if (island.TouchesWater || island.IsGrounded || Mathf.Abs(island.Position.x) > 0.02f ||
                    Mathf.Abs(island.Position.y + island.Radius - LobbyMotion.ReviveCenterY) > 0.02f)
                    throw new Exception($"Practice physics did not place the body at the elevated orb ({island.Position})");
                island.LeaveReviveOrb();
                island.Step(Vector2.zero, false);
                if (island.IsGrounded || island.Velocity.y <= 0)
                    throw new Exception("Practice physics did not play the native revive hop from the elevated orb");
                for (int tick = 0; tick < 180 && !island.IsGrounded; tick++)
                    island.Step(Vector2.zero, false);
                if (!island.IsGrounded || island.Position.sqrMagnitude > 0.0025f)
                    throw new Exception("The revived blob did not fall from the orb to the island");
            }
            Main.Log.LogInfo("[UI smoke] PASS: native player tuning, six acceleration frames, turn, jump, landing, both rounded edges, and falling from the elevated respawn orb");
        }
    }
    internal sealed class UiTestConnection : SteamConnection
    {
        internal UiTestConnection(int index, int color)
        {
            Connected = true;
            id = (ulong)(index + 1);
            steamName = "Test player " + (index + 2);
            lobby_color = color;
            lobby_team = (byte)(index % 4);
            lobby_ability1 = 1;
            lobby_ability2 = 2;
            lobby_ability3 = 3;
        }
        internal void Disconnect() { Connected = false; }
    }
    [HarmonyPatch(typeof(CSBox_online), "updatePing")]
    internal static class UiTestSuppressPing
    {
        static bool Prefix(CSBox_online __instance)
        {
            return !(__instance.connectedPlayer is UiTestConnection);
        }
    }
    [HarmonyPatch(typeof(MainMenu), "Start")]
    internal static class OnlineLobbySmokeMenuPatch
    {
        static bool scheduled;
        static void Postfix()
        {
            if (scheduled) return;
            scheduled = true;
            new GameObject("Bopl8 UI test").AddComponent<OnlineLobbySmokeTest>();
        }
    }
}
#endif
