using System;
using HarmonyLib;
using Steamworks;
using Lobby = Steamworks.Data.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MorePlayers
{
    // The original local selector owns input, keybinds, ability choices and ready state.
    // The roster mirrors lobby state without changing how the game synchronizes it.
    internal sealed class OnlinePlayerRoster : MonoBehaviour
    {
        const float PanelWidth = 1720;
        static readonly Vector2 SlotSize = new Vector2(320, 320);
        const float CardGrass = -96;
        const float PracticeUnits = 32;
        const float PracticeGround = -45;
        const float PracticeWidth = 1650;
        const float PracticeHeight = 565;
        const float PracticeCenter = -596;
        const float RosterTop = 751;
        const float RosterBottom = PracticeCenter - PracticeHeight / 2;
        // The card uses the central portion of the SmallCapsule sprite.
        const float PlatformGrassTop = 469f / 594;
        internal CharacterSelectHandler_online Handler;
        internal RectTransform LocalEditor { get; private set; }
        internal static Action<int> SendKick = index => SteamManager.instance.KickPlayer(index);
#if BOPL8_UI_SMOKE
        internal static string LocalNameOverride;
#endif
        internal Card[] Cards { get; private set; }
        internal Sprite RoundedFill { get; private set; }
        internal Sprite RoundedBorder { get; private set; }
        GameObject root;
        RectTransform canvas;
        Canvas lobbyCanvas;
        RectTransform panel;
        TextMeshProUGUI summary;
        NamedSpriteList abilities;
        LobbyMotion motion;
        Texture2D fillTexture, borderTexture;
        Graphic respawnAnchor;
        GameObject respawnPreview;
        Camera respawnCamera;
        RenderTexture respawnBlack, respawnWhite;
        Texture2D respawnTexture, respawnReadback;
        Color32[] respawnBlackPixels, respawnWhitePixels;
        int respawnReadsPending;
        bool respawnAsyncReadback, respawnReadError, respawnDisposed;
        float nextRespawnCapture;
        Image respawnArrival;
        SpriteFrame[] idleFrames, runFrames, fallFrames, landFrames, spawnFrames;
        VelocityFrame[] airFrames;
        Vector3 faceLeft = new Vector3(-1, 1, 1), faceRight = Vector3.one;
        float landAnimationTicks, reviveShootTicks, arrivalTicks, splashTicks;
        SpriteFrame[] anchorFrames, reviveShootFrames, arrivalFrames, splashAnimation;
        Sprite reviveFlightSprite;
        Sprite[] splashFrames;
        float latestArrival;

        internal sealed class Card
        {
            internal RectTransform Root;
            internal TextMeshProUGUI Name, State;
            internal Image Border, Fill, Portrait;
            internal WobbleAnimation Wobble;
            internal RectTransform CardArtwork;
            internal RectTransform PracticeAvatar;
            internal Image PracticePortrait;
            internal Image Splash;
            internal Image ReviveWisp, ReviveFlight;
            internal Image[] ReviveTrail;
            internal Vector2 SplashOrigin;
            internal float Facing = 1;
            internal float PracticeFallLoopTicks;
            internal float SplashStarted = float.NegativeInfinity;
            internal SpriteFrame[] PracticeAnimation;
            internal float PracticeAnimationStarted;
            internal int? ColorIndex;
            internal Material ColorMaterial;
            internal bool WasReady;
            internal bool InWater;
            internal Button KickButton;
            internal Image[] AbilityIcons = new Image[3];
        }

        void Start()
        {
            var ui = Handler.GetComponent<OnlineInviteMenu>();
            lobbyCanvas = Handler.GetComponentInParent<Canvas>();
            canvas = (RectTransform)lobbyCanvas.transform;
            panel = OnlineInviteMenu.Rect("PlayerRoster", canvas, Vector2.zero, new Vector2(PanelWidth, RosterTop - RosterBottom));
            panel.anchorMin = panel.anchorMax = canvas.pivot;
            root = panel.gameObject;
            LocalEditor = (RectTransform)Handler.characterSelectBox.transform;
            PositionLocalEditor();
            var readyLabel = Array.Find(Handler.characterSelectBox.readyColor.GetComponentsInChildren<TextMeshProUGUI>(true),
                label => string.Equals(label.text.Trim(), "ready!", StringComparison.OrdinalIgnoreCase));
            if (readyLabel != null)
            {
                readyLabel.fontSize *= 1.07f;
                readyLabel.fontSizeMax *= 1.07f;
            }
            abilities = Handler.characterSelectBox.abilityGrid.abilityIcons;
            RoundedFill = MakeRoundedSprite(false, out fillTexture);
            RoundedBorder = MakeRoundedSprite(true, out borderTexture);
            motion = gameObject.AddComponent<LobbyMotion>();
            motion.Handler = Handler;
            MatchNativeInviteStyle();
            summary = ui.Text("RosterTitle", panel, new Vector2(0, 722), new Vector2(1400, 58), "PLAYERS", 42);
            Cards = new Card[Constants.MAX_PLAYERS];
            var native = Handler.networkPlayerBoxes[0];
            var nativeImages = LocalEditor.GetComponentsInChildren<Image>(true);
            var nativeWobble = LocalEditor.GetComponentInChildren<WobbleAnimation>(true);
            var nativeIdle = nativeWobble != null ? nativeWobble.idleImage : native.characters[native.characters.Length - 1];
            var nativeAbility = LocalEditor.GetComponentInChildren<SelectAbility>(true);
            var island = Array.Find(nativeImages, image => image.sprite != null && image.name.Equals("platform", StringComparison.OrdinalIgnoreCase));
            var platform = island != null ? island.sprite : null;
            var slime = Array.Find(Resources.FindObjectsOfTypeAll<SlimeController>(),
                player => !player.gameObject.scene.IsValid() && player.animData != null && player.animData.name == "MCAnimData");
            if (slime != null)
            {
                var data = slime.animData;
                idleFrames = data.GetAnimation("idle");
                runFrames = data.GetAnimation("run");
                fallFrames = data.GetAnimation("fallLeftLoop");
                airFrames = data.GetVelocityMap("fallLeft");
                landFrames = data.GetAnimation("land");
                spawnFrames = data.GetAnimation("spawn");
                foreach (var frame in landFrames) landAnimationTicks += Mathf.Max(0, frame.frameDuration);
                var flip = slime.GetComponentInChildren<FlippingTransform>(true);
                if (flip != null)
                {
                    faceLeft = flip.faceLeft;
                    faceRight = flip.faceRight;
                }
            }
            foreach (var data in Resources.FindObjectsOfTypeAll<AnimationData>())
            {
                switch (data.name)
                {
                    case "Revive":
                        anchorFrames = data.GetAnimation("wisp");
                        reviveShootFrames = data.GetAnimation("shoot");
                        break;
                    case "ReviveEffect": arrivalFrames = data.GetAnimation("spawn"); break;
                    case "WaterSplash": splashAnimation = data.GetAnimation("enter"); break;
                }
            }
            reviveShootTicks = AnimationTicks(reviveShootFrames);
            arrivalTicks = AnimationTicks(arrivalFrames);
            splashTicks = AnimationTicks(splashAnimation);
            var reviveParticle = Array.Find(Resources.FindObjectsOfTypeAll<ReviveParticle>(),
                particle => !particle.gameObject.scene.IsValid());
            if (reviveParticle != null) reviveFlightSprite = reviveParticle.GetComponent<SpriteRenderer>()?.sprite;
            splashFrames = new Sprite[23];
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (reviveFlightSprite == null && sprite.name == "GreenWhiteFuzzySphere") reviveFlightSprite = sprite;
                const string prefix = "WaterSplash_";
                if (sprite.name.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(sprite.name.Substring(prefix.Length), out int frame) && frame >= 0 && frame < splashFrames.Length)
                    splashFrames[frame] = sprite;
            }
            var widePlatform = LobbyPlatformAssets.GetLongPlatform();
            motion.UseLongPlatform = widePlatform != null;
            var practiceViewport = BuildPracticeArea(widePlatform != null ? widePlatform : platform);
            for (int i = 0; i < Cards.Length; i++)
            {
                var position = new Vector2(-600 + (i % 4) * 400, 470 - (i / 4) * 508);
                Cards[i] = BuildCard(ui, practiceViewport, nativeIdle, nativeWobble, nativeAbility, platform, i, position);
            }
            BuildWater(practiceViewport);
            foreach (var card in Cards)
            {
                card.Splash = BuildPracticeEffect(practiceViewport, "WaterSplash");
                card.ReviveWisp = BuildPracticeEffect(practiceViewport, "ReviveWisp");
                card.ReviveTrail = new Image[5];
                for (int i = card.ReviveTrail.Length - 1; i >= 0; i--)
                    card.ReviveTrail[i] = BuildPracticeEffect(practiceViewport, "ReviveTrail");
                card.ReviveFlight = BuildPracticeEffect(practiceViewport, "ReviveFlight");
            }
            respawnArrival = BuildPracticeEffect(practiceViewport, "RespawnArrival");
            var border = OnlineInviteMenu.Rect("PracticeFrame", practiceViewport.parent, Vector2.zero, practiceViewport.sizeDelta)
                .gameObject.AddComponent<Image>();
            ApplySprite(border, RoundedBorder, 0.7f);
            border.color = Handler.darkBlue;
            border.raycastTarget = false;

            // Keep native remote boxes and animations available to the game.
            foreach (var box in Handler.networkPlayerBoxes)
            {
                var viewport = box.GetComponentInParent<RectMask2D>();
                var visual = viewport != null ? viewport.transform.parent.gameObject : box.gameObject;
                var group = visual.GetComponent<CanvasGroup>() ?? visual.AddComponent<CanvasGroup>();
                group.alpha = 0;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }

        RectTransform BuildPracticeArea(Sprite islandSprite)
        {
            var size = new Vector2(PracticeWidth, PracticeHeight);
            var area = OnlineInviteMenu.Rect("SharedPracticeIsland", panel, new Vector2(0, PracticeCenter), size);
            var backdrop = area.gameObject.AddComponent<Image>();
            ApplySprite(backdrop, RoundedFill, 0.7f);
            backdrop.color = new Color(0.36f, 0.74f, 0.94f, 0.96f);
            backdrop.raycastTarget = false;
            area.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var viewport = OnlineInviteMenu.Rect("PracticeViewport", area, Vector2.zero, size);
            viewport.gameObject.AddComponent<RectMask2D>();
            var platform = OnlineInviteMenu.Rect("PracticePlatform", viewport, Vector2.zero, size);
            if (islandSprite != null)
            {
                var artwork = BuildPortrait(platform, "PlatformArtwork", PracticeY(motion.PlatformCenterY),
                    islandSprite.rect.size * (PracticeUnits / islandSprite.pixelsPerUnit), islandSprite);
                artwork.rectTransform.pivot = islandSprite.pivot / islandSprite.rect.size;
            }
            respawnAnchor = BuildRespawnAnchor(viewport);
            return viewport;
        }

        static Image BuildPracticeEffect(RectTransform parent, string name)
        {
            var image = BuildPortrait(parent, name, 0, Vector2.zero, null);
            image.preserveAspect = false;
            image.enabled = false;
            return image;
        }

        static void DrawPracticeEffect(Image image, Sprite sprite, Vector2 point, float scale = 1,
            float angle = 0, float alpha = 1, float pivotLift = 0)
        {
            image.enabled = sprite != null;
            if (sprite == null) return;
            var rect = image.rectTransform;
            rect.sizeDelta = sprite.rect.size * (PracticeUnits * scale / sprite.pixelsPerUnit);
            rect.pivot = sprite.pivot / sprite.rect.size + Vector2.up * pivotLift;
            rect.anchoredPosition = point;
            rect.localRotation = Quaternion.Euler(0, 0, angle);
            image.sprite = sprite;
            image.color = new Color(1, 1, 1, alpha);
        }

        Graphic BuildRespawnAnchor(RectTransform viewport)
        {
            const float topInset = 10;
            var indicator = Array.Find(Resources.FindObjectsOfTypeAll<RevivePositionIndicator>(),
                marker => !marker.gameObject.scene.IsValid());
            if (indicator == null)
            {
                var fallback = BuildPortrait(viewport, "RespawnAnchor", viewport.rect.yMax - topInset - 84,
                    new Vector2(68, 84), anchorFrames != null && anchorFrames.Length > 0 ? anchorFrames[0].sprite : null);
                fallback.enabled = fallback.sprite != null;
                return fallback;
            }

            const float previewWorldSize = 6;
            float size = previewWorldSize * PracticeUnits;
            var rect = OnlineInviteMenu.Rect("RespawnAnchor", viewport,
                new Vector2(0, viewport.rect.yMax - topInset - size / 2), Vector2.one * size);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;

            respawnPreview = new GameObject("Lobby Revive Preview");
            respawnPreview.SetActive(false);
            respawnPreview.transform.position = new Vector3(2048, 2048, 0);
            // Keep the prefab inactive until the match components are removed. The lobby only needs its three particle systems.
            var visual = Instantiate(indicator.gameObject, respawnPreview.transform, false);
            visual.name = "Lobby Revive Indicator";
            DestroyImmediate(visual.GetComponent<RevivePositionIndicator>());
            DestroyImmediate(visual.GetComponent<BoplBody>());
            DestroyImmediate(visual.GetComponent<DPhysicsCircle>());
            DestroyImmediate(visual.GetComponent<FixTransform>());
            var particles = visual.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var system in particles)
            {
                var main = system.main;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.useUnscaledTime = true;
                main.stopAction = ParticleSystemStopAction.None;
            }

            const int previewPixels = 64;
            respawnBlack = new RenderTexture(previewPixels, previewPixels, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            respawnWhite = new RenderTexture(previewPixels, previewPixels, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            respawnBlackPixels = new Color32[previewPixels * previewPixels];
            respawnWhitePixels = new Color32[previewPixels * previewPixels];
            respawnAsyncReadback = SystemInfo.supportsAsyncGPUReadback;
            respawnTexture = new Texture2D(previewPixels, previewPixels, TextureFormat.RGBA32, false, true)
            {
                name = "Lobby Revive Indicator",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            respawnTexture.SetPixels32(respawnBlackPixels);
            respawnTexture.Apply(false);
            image.texture = respawnTexture;
            var cameraObject = new GameObject("Lobby Revive Camera");
            cameraObject.transform.SetParent(respawnPreview.transform, false);
            cameraObject.transform.localPosition = Vector3.back * 10;
            respawnCamera = cameraObject.AddComponent<Camera>();
            respawnCamera.enabled = false;
            respawnCamera.orthographic = true;
            respawnCamera.orthographicSize = previewWorldSize / 2;
            respawnCamera.farClipPlane = 20;
            respawnCamera.clearFlags = CameraClearFlags.SolidColor;
            respawnCamera.cullingMask = 1 << visual.layer;
            respawnCamera.useOcclusionCulling = false;
            respawnCamera.allowHDR = false;
            respawnCamera.allowMSAA = false;
            respawnPreview.SetActive(true);
            foreach (var system in particles)
            {
                system.Simulate(0.5f, false, true, false);
                system.Play(false);
            }
            return image;
        }

        void CaptureRespawnAnchor()
        {
            float now = Time.unscaledTime;
            if (respawnReadsPending != 0 || now < nextRespawnCapture) return;
            nextRespawnCapture = now + (respawnAsyncReadback ? 1f / 30 : 1f / 15);

            // The native particle shader leaves alpha untouched. Two backgrounds let us recover the coverage
            // while keeping all three native materials and their blend modes.
            respawnCamera.targetTexture = respawnBlack;
            respawnCamera.backgroundColor = Color.black;
            respawnCamera.Render();
            respawnCamera.targetTexture = respawnWhite;
            respawnCamera.backgroundColor = Color.white;
            respawnCamera.Render();
            respawnCamera.targetTexture = null;

            if (respawnAsyncReadback)
            {
                respawnReadsPending = 2;
                const UnityEngine.Experimental.Rendering.GraphicsFormat format =
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
                AsyncGPUReadback.Request(respawnBlack, 0, format, ReadRespawnBlack);
                AsyncGPUReadback.Request(respawnWhite, 0, format, ReadRespawnWhite);
                return;
            }

            if (respawnReadback == null)
                respawnReadback = new Texture2D(respawnTexture.width, respawnTexture.height, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                var bounds = new Rect(0, 0, respawnTexture.width, respawnTexture.height);
                RenderTexture.active = respawnBlack;
                respawnReadback.ReadPixels(bounds, 0, 0, false);
                respawnReadback.GetRawTextureData<Color32>().CopyTo(respawnBlackPixels);
                RenderTexture.active = respawnWhite;
                respawnReadback.ReadPixels(bounds, 0, 0, false);
                respawnReadback.GetRawTextureData<Color32>().CopyTo(respawnWhitePixels);
                ComposeRespawnAnchor();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        void ReadRespawnBlack(AsyncGPUReadbackRequest request) => CompleteRespawnRead(request, respawnBlackPixels);

        void ReadRespawnWhite(AsyncGPUReadbackRequest request) => CompleteRespawnRead(request, respawnWhitePixels);

        void CompleteRespawnRead(AsyncGPUReadbackRequest request, Color32[] pixels)
        {
            if (!respawnDisposed && !request.hasError) request.GetData<Color32>().CopyTo(pixels);
            respawnReadError |= request.hasError;
            if (--respawnReadsPending != 0) return;
            if (respawnDisposed)
            {
                Destroy(respawnBlack);
                Destroy(respawnWhite);
            }
            else if (respawnReadError)
                respawnAsyncReadback = false;
            else
                ComposeRespawnAnchor();
        }

        void ComposeRespawnAnchor()
        {
            var output = respawnTexture.GetRawTextureData<Color32>();
            for (int i = 0; i < output.Length; i++)
            {
                var black = respawnBlackPixels[i];
                var white = respawnWhitePixels[i];
                int transmission = (white.r - black.r + white.g - black.g + white.b - black.b + 1) / 3;
                int alpha = Mathf.Clamp(255 - transmission, Mathf.Max(black.r, Mathf.Max(black.g, black.b)), 255);
                output[i] = alpha == 0 ? default : new Color32((byte)(black.r * 255 / alpha),
                    (byte)(black.g * 255 / alpha), (byte)(black.b * 255 / alpha), (byte)alpha);
            }
            respawnTexture.Apply(false);
        }

        static float PracticeY(float worldY) => PracticeGround + worldY * PracticeUnits;

        void BuildIsland(RectTransform parent, Sprite sprite, float grassHeight, float spriteWidth)
        {
            float spriteHeight = sprite != null ? spriteWidth * sprite.rect.height / sprite.rect.width : 190;
            var imageRect = OnlineInviteMenu.Rect("PlatformArtwork", parent,
                new Vector2(0, grassHeight - (PlatformGrassTop - 0.5f) * spriteHeight),
                new Vector2(spriteWidth, spriteHeight));
            var image = imageRect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (sprite == null)
            {
                ApplySprite(image, RoundedFill, 0.4f);
                image.color = new Color(0.36f, 0.73f, 0.21f);
            }
        }

        void BuildWater(RectTransform viewport)
        {
            float waterTop = PracticeY(motion.WaterHeight);
            float waterBottom = -PracticeHeight / 2 - 4;
            float radius = motion.UseLongPlatform ? 4.4f : 2.65f;
            float halfBox = motion.PlatformHalfWidth - radius;
            float fromCenter = motion.WaterHeight - motion.PlatformCenterY;
            float shoreline = halfBox + Mathf.Sqrt(Mathf.Max(0, radius * radius - fromCenter * fromCenter));
            LobbyWaterView.Build(viewport, new Vector2(0, (waterTop + waterBottom) / 2),
                new Vector2(PracticeWidth, waterTop - waterBottom), shoreline * PracticeUnits, PracticeUnits);
        }

        Card BuildCard(OnlineInviteMenu ui, RectTransform practice, Image nativeIdle, WobbleAnimation nativeWobble,
            SelectAbility nativeAbility, Sprite platform, int index, Vector2 position)
        {
            var card = new Card();
            card.Root = OnlineInviteMenu.Rect("PlayerSlot" + (index + 1), panel, position, SlotSize);
            card.Fill = card.Root.gameObject.AddComponent<Image>();
            ApplySprite(card.Fill, RoundedFill, 0.4f);
            card.Fill.raycastTarget = false;
            card.Root.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            card.CardArtwork = OnlineInviteMenu.Rect("CardArtwork", card.Root, Vector2.zero, SlotSize);
            BuildIsland(card.CardArtwork, platform, CardGrass, 820);
            card.Portrait = BuildPortrait(card.CardArtwork, "Character", CardGrass - 2, new Vector2(181, 125), nativeIdle.sprite);
            if (nativeWobble != null)
            {
                card.CardArtwork.gameObject.SetActive(false);
                card.Wobble = card.CardArtwork.gameObject.AddComponent<WobbleAnimation>();
                card.Wobble.idleImage = card.Portrait;
                card.Wobble.wobbleImage = BuildPortrait(card.CardArtwork, "Wobble", CardGrass - 2, new Vector2(250, 166), nativeWobble.wobbleImage.sprite);
                card.Wobble.idleAnimation = nativeWobble.idleAnimation;
                card.Wobble.wobbleAnimation = nativeWobble.wobbleAnimation;
                card.Wobble.idle_timePerFrame = nativeWobble.idle_timePerFrame;
                card.Wobble.wobble_timePerFrame = nativeWobble.wobble_timePerFrame;
                card.Wobble.tallSpriteIndex = nativeWobble.tallSpriteIndex;
                card.Wobble.tallSpriteDuration = nativeWobble.tallSpriteDuration;
                card.CardArtwork.gameObject.SetActive(true);
            }

            var borderRect = OnlineInviteMenu.Rect("SlotBorder", card.Root, Vector2.zero, card.Root.sizeDelta);
            card.Border = borderRect.gameObject.AddComponent<Image>();
            ApplySprite(card.Border, RoundedBorder, 0.4f);
            card.Border.raycastTarget = false;
            card.State = ui.Text("PlayerState", card.Root, new Vector2(0, 108), new Vector2(268, 57), "", 41);

            // Labels and abilities live outside the portrait clipping area.
            card.Name = ui.Text("PlayerName", panel, position + new Vector2(0, 193), new Vector2(378, 55), "", 41);
            card.Name.enableAutoSizing = true;
            card.Name.enableWordWrapping = false;
            card.Name.fontSizeMin = 24;
            card.Name.fontSizeMax = 41;
            for (int i = 0; i < card.AbilityIcons.Length; i++)
            {
                var iconPosition = position + new Vector2((i - 1) * 124, -225);
                var tile = OnlineInviteMenu.Rect("AbilitySlot" + (i + 1), panel, iconPosition, new Vector2(120, 120));
                if (nativeAbility != null && nativeAbility.BorderRenderer != null && nativeAbility.abilityIconRenderer != null)
                {
                    var circle = nativeAbility.BorderRenderer;
                    Vector3 center = circle.rectTransform.TransformPoint(circle.rectTransform.rect.center);
                    Transform reference = nativeAbility.transform;
                    float size = reference.InverseTransformVector(circle.rectTransform.TransformVector(Vector3.right * circle.rectTransform.rect.width)).magnitude;
                    float scale = 101 / Mathf.Max(size, 1);
                    CopyAbilityGraphic(tile, "AbilityCircle", circle, reference, center, scale);
                    card.AbilityIcons[i] = CopyAbilityGraphic(tile, "Icon", nativeAbility.abilityIconRenderer, reference, center, scale);
                }
                else
                {
                    var icon = OnlineInviteMenu.Rect("Icon", tile, Vector2.zero, new Vector2(108, 108)).gameObject.AddComponent<Image>();
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    card.AbilityIcons[i] = icon;
                }
            }

            card.PracticeAvatar = OnlineInviteMenu.Rect("PracticePlayer" + (index + 1), practice, Vector2.zero, new Vector2(170, 180));
            card.PracticePortrait = BuildPortrait(card.PracticeAvatar, "Character", -3, new Vector2(101, 70), nativeIdle.sprite);
            card.PracticePortrait.preserveAspect = false;

            if (index > 0)
            {
                card.KickButton = ui.Button("KickPlayer" + (index + 1), card.Root,
                    new Vector2(72, -90), new Vector2(102, 54), "KICK", () => Kick(GetPeer(index)));
                ui.StyleButton(card.KickButton);
                var kickColors = card.KickButton.colors;
                kickColors.normalColor = Handler.darkBlue;
                kickColors.highlightedColor = Handler.blue;
                kickColors.pressedColor = Handler.blue;
                card.KickButton.colors = kickColors;
                card.KickButton.targetGraphic.canvasRenderer.SetColor(kickColors.normalColor);
                var kickLabel = card.KickButton.GetComponentInChildren<TextMeshProUGUI>();
                kickLabel.color = Color.white;
                kickLabel.fontSizeMin = 24;
                kickLabel.fontSizeMax = 28;
                card.KickButton.transform.Find("ButtonBorder").gameObject.SetActive(false);
            }
            return card;
        }

        static Image CopyAbilityGraphic(RectTransform tile, string name, Image source, Transform reference,
            Vector3 center, float scale)
        {
            var sourceRect = source.rectTransform;
            Vector3 offset = reference.InverseTransformVector(sourceRect.TransformPoint(sourceRect.rect.center) - center);
            float width = reference.InverseTransformVector(sourceRect.TransformVector(Vector3.right * sourceRect.rect.width)).magnitude;
            float height = reference.InverseTransformVector(sourceRect.TransformVector(Vector3.up * sourceRect.rect.height)).magnitude;
            var copy = OnlineInviteMenu.Rect(name, tile, (Vector2)offset * scale, new Vector2(width, height) * scale)
                .gameObject.AddComponent<Image>();
            OnlineInviteMenu.CopyImageStyle(copy, source);
            if (name == "Icon") copy.overrideSprite = null;
            copy.color = source.color;
            copy.material = source.material;
            copy.raycastTarget = false;
            return copy;
        }

        static Image BuildPortrait(RectTransform parent, string name, float feet, Vector2 size, Sprite sprite)
        {
            var portrait = OnlineInviteMenu.Rect(name, parent, new Vector2(0, feet), size);
            portrait.pivot = new Vector2(0.5f, 0);
            portrait.anchoredPosition = new Vector2(0, feet);
            var image = portrait.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        void PositionLocalEditor()
        {
            var position = canvas.InverseTransformPoint(LocalEditor.position);
            position.x = Mathf.Lerp(canvas.rect.xMin, canvas.rect.xMax, 0.795f);
            LocalEditor.position = canvas.TransformPoint(position);
        }

        void MatchNativeInviteStyle()
        {
            OnlineInviteMenu.CopyImageStyle(Handler.startBorder, Handler.findPlayersBorder);
            var startFill = Handler.startButton.GetComponent<Image>();
            var inviteFill = Handler.findPlayersButton.GetComponent<Image>();
            if (startFill != null && inviteFill != null && startFill != Handler.startBorder)
                OnlineInviteMenu.CopyImageStyle(startFill, inviteFill);
            Handler.startText.font = Handler.findPlayersText.font;
            Handler.startText.fontSharedMaterial = Handler.findPlayersText.fontSharedMaterial;
            Handler.startText.fontSize = Handler.findPlayersText.fontSize;
        }

        internal static void ApplySprite(Image image, Sprite sprite, float pixelsPerUnitMultiplier)
        {
            image.sprite = sprite;
            image.overrideSprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        }

        static Sprite MakeRoundedSprite(bool border, out Texture2D texture)
        {
            const int size = 64;
            const float stroke = 6f;
            const float cornerRadius = 18f;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = border ? "BoplRoundedBorder" : "BoplRoundedFill";
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2(Mathf.Abs(x + 0.5f - size / 2f), Mathf.Abs(y + 0.5f - size / 2f));
                var distance = point - Vector2.one * (size / 2f - cornerRadius);
                float edge = new Vector2(Mathf.Max(0, distance.x), Mathf.Max(0, distance.y)).magnitude
                    + Mathf.Min(Mathf.Max(distance.x, distance.y), 0) - cornerRadius;
                float opacity = Mathf.Clamp01(0.5f - edge);
                if (border) opacity *= Mathf.Clamp01(edge + stroke + 0.5f);
                pixels[y * size + x] = new Color(1, 1, 1, opacity);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, Vector4.one * 22);
        }

        SteamConnection GetPeer(int index) => index == 0 ? null : Handler.networkPlayerBoxes[index - 1].connectedPlayer;

        internal bool CanKick(SteamConnection peer)
        {
            var manager = SteamManager.instance;
            return manager != null && SteamClient.IsValid && manager.currentLobby.IsOwnedBy(SteamClient.SteamId)
                && !Handler.isStartingAGame && Handler.goingBackOperation == null && peer != null
                && peer.Connected && !peer.beingKicked && peer.id != SteamClient.SteamId
                && manager.connectedPlayers.Contains(peer);
        }

        internal void Kick(SteamConnection peer)
        {
            if (CanKick(peer)) SendKick(SteamManager.instance.connectedPlayers.IndexOf(peer));
        }

        void LateUpdate()
        {
            if (root == null) return;
            bool active = Handler.goingBackOperation == null && !Handler.isStartingAGame;
            root.SetActive(active);
            if (respawnPreview != null && respawnPreview.activeSelf != active)
                respawnPreview.SetActive(active);
            if (!active) return;
            FitRosterToWindow();
            PositionLocalEditor();
            int present = 0;
            latestArrival = float.PositiveInfinity;
            for (int i = 0; i < Cards.Length; i++)
            {
                var peer = GetPeer(i);
                bool exists = i == 0 || peer != null && peer.Connected;
                if (exists) present++;
                UpdateCard(Cards[i], i, peer, exists);
            }
            summary.text = "PLAYERS  " + present + " / " + Constants.MAX_PLAYERS;
            if (respawnAnchor.enabled)
            {
                if (respawnCamera != null) CaptureRespawnAnchor();
                else if (respawnAnchor is Image fallback)
                    fallback.sprite = AnimationSprite(anchorFrames, Time.unscaledTime * 60, true);
                respawnAnchor.color = new Color(1, 1, 1, Mathf.Clamp01((latestArrival - 0.65f) / 0.35f));
            }
            float arrivalFrame = latestArrival * 60;
            DrawPracticeEffect(respawnArrival, arrivalFrame < arrivalTicks ? AnimationSprite(arrivalFrames, arrivalFrame, false) : null,
                PracticePoint(0, LobbyMotion.ReviveCenterY));
        }

        void FitRosterToWindow()
        {
            Camera camera = lobbyCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : lobbyCanvas.worldCamera;
            Rect visible = canvas.rect;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, Vector2.zero, camera, out Vector2 low) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, new Vector2(Screen.width, Screen.height), camera, out Vector2 high) &&
                high.x > low.x && high.y > low.y)
            {
                visible = Rect.MinMaxRect(Mathf.Max(low.x, visible.xMin), Mathf.Max(low.y, visible.yMin),
                    Mathf.Min(high.x, visible.xMax), Mathf.Min(high.y, visible.yMax));
            }
            float top = Mathf.Lerp(visible.yMin, visible.yMax, 0.808f);
            float bottom = Mathf.Lerp(visible.yMin, visible.yMax, 0.062f);
            float scale = Mathf.Min(visible.width * 0.63f / PanelWidth, (top - bottom) / (RosterTop - RosterBottom));
            panel.localScale = Vector3.one * scale;
            panel.anchoredPosition = new Vector2(Mathf.Lerp(visible.xMin, visible.xMax, 0.393f), top - RosterTop * scale);
        }

        void UpdateCard(Card card, int index, SteamConnection peer, bool exists)
        {
            bool local = index == 0;
            card.CardArtwork.gameObject.SetActive(exists);
            if (card.KickButton != null) card.KickButton.gameObject.SetActive(CanKick(peer));

            bool ready = exists && (local ? CharacterSelectHandler_online.IsLocalPlayerReady() : peer.lobby_isReady);
            card.PracticeAvatar.gameObject.SetActive(ready);
            var kit = Handler.characterSelectBox.playerInit;
            string localName = SteamClient.Name;
#if BOPL8_UI_SMOKE
            localName = LocalNameOverride ?? localName;
#endif
            card.Name.text = local ? localName + " (YOU)" : exists ? peer.steamName : "PLAYER " + (index + 1);
            card.State.text = exists ? (ready ? "READY" : "CHOOSING") : "OPEN SLOT";
            card.State.fontSize = ready ? 47 : 41;
            card.State.rectTransform.anchoredPosition = new Vector2(0, exists ? 108 : 0);
            if (exists)
            {
                int color = local ? kit.color : peer.lobby_color;
                if (card.ColorIndex != color)
                {
                    if (card.ColorIndex.HasValue) card.Wobble?.PlayWobbleAnimation();
                    card.ColorIndex = color;
                    card.ColorMaterial = null;
                    for (int i = 0; i < Handler.playerColors.Length; i++)
                        if (Handler.playerColors[i].colorIndex == color)
                        {
                            card.ColorMaterial = Handler.playerColors[i].uiMaterial;
                            break;
                        }
                    card.PracticePortrait.material = card.Portrait.material = card.ColorMaterial;
                    if (card.Wobble != null) card.Wobble.wobbleImage.material = card.ColorMaterial;
                }
                int teamIndex = local ? kit.team : peer.lobby_team;
                var teamColors = Handler.networkPlayerBoxes[0].teamColors;
                var team = teamColors.teamColors[Mathf.Clamp(teamIndex, 0, teamColors.Length - 1)];
                card.Fill.color = team.fill;
                card.Border.color = card.Name.color = card.State.color = team.border;
                if (ready)
                {
                    if (!card.WasReady) card.Wobble?.PlayWobbleAnimation();
                    UpdatePracticePlayer(card, index, peer);
                }
            }
            else
            {
                card.Fill.color = Color.Lerp(Handler.blue, Color.white, 0.35f);
                card.Border.color = Color.Lerp(Handler.darkBlue, Handler.blue, 0.32f);
                card.Name.color = Handler.darkBlue;
                card.State.color = Handler.darkBlue;
            }
            if (!ready)
            {
                card.Splash.enabled = false;
                HideReviveFlight(card);
                card.SplashStarted = float.NegativeInfinity;
                card.PracticeAnimation = null;
                card.PracticeFallLoopTicks = 0;
                card.Facing = 1;
                card.InWater = false;
            }
            card.WasReady = ready;

            int abilityCount = Settings.Get().NumberOfAbilities;
            for (int i = 0; i < card.AbilityIcons.Length; i++)
            {
                int ability = -1;
                // With three abilities the native editor shows serialized slots 0, 2, 1.
                int displayedSlot = abilityCount == 3 && i > 0 ? 3 - i : i;
                if (local)
                    ability = displayedSlot == 0 ? kit.ability0 : displayedSlot == 1 ? kit.ability1 : kit.ability2;
                else if (ready)
                    ability = displayedSlot == 0 ? peer.lobby_ability1 : displayedSlot == 1 ? peer.lobby_ability2 : peer.lobby_ability3;
                bool known = (!local || Handler.characterSelectBox.menuState != CharSelectMenu.join) &&
                    i < abilityCount && abilities != null && ability >= 0 && ability < abilities.sprites.Count &&
                    !string.Equals(abilities.sprites[ability].name, "None", StringComparison.OrdinalIgnoreCase);
                var icon = card.AbilityIcons[i];
                icon.transform.parent.gameObject.SetActive(known);
                icon.sprite = known ? abilities.sprites[ability].sprite : null;
                icon.enabled = known;
            }
        }

        void UpdatePracticePlayer(Card card, int slot, SteamConnection peer)
        {
            Vector2 position = motion.GetPose(slot, peer, out _, out bool grounded, out float angle,
                out Vector2 velocity, out float facing);
            float now = Time.unscaledTime;
            bool reviving = motion.GetRevive(slot, peer, out Vector2 origin, out float age, out float arrivalAge);
            bool respawning = reviving && arrivalAge >= 0;
            bool inTransit = reviving && !respawning;
            if (respawning)
            {
                latestArrival = Mathf.Min(latestArrival, arrivalAge);
                if (slot == 0 && card.PracticeAnimation != spawnFrames) AudioManager.Get()?.Play("reviveDing");
            }
            card.PracticePortrait.enabled = !inTransit;
            float radians = angle * Mathf.Deg2Rad;
            float travel = grounded
                ? Vector2.Dot(velocity, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)))
                : velocity.x;
            if (respawning) card.Facing = 1;
            else if (facing != 0) card.Facing = facing;
            else if (Mathf.Abs(travel) > 0.35f) card.Facing = Mathf.Sign(travel);
            float worldX = position.x * LobbyMotion.IslandHalfWidth;
            card.PracticeAvatar.anchoredPosition = PracticePoint(worldX, position.y * LobbyMotion.JumpViewportHeight);
            bool landing = grounded && landAnimationTicks > 0 && (card.PracticeAnimation == fallFrames ||
                card.PracticeAnimation == landFrames && (now - card.PracticeAnimationStarted) * 60 < landAnimationTicks);
            var frames = respawning ? spawnFrames : !grounded ? fallFrames : landing ? landFrames :
                Mathf.Abs(travel) > 1.8f ? runFrames : idleFrames;
            if (!inTransit) UpdatePracticeAnimation(card, frames, respawning, grounded, angle, velocity.y, arrivalAge, now);
            if (inTransit) UpdateReviveFlight(card, origin, age, arrivalAge);
            else HideReviveFlight(card);

            bool inWater = !reviving && motion.TouchesWater(position, angle);
            if (card.WasReady && (inWater || inTransit) && !card.InWater)
            {
                card.SplashStarted = inTransit ? now - age : now;
                float impactX = inTransit ? origin.x : worldX - Mathf.Sin(radians) * motion.PlayerRadius;
                card.SplashOrigin = PracticePoint(impactX, motion.WaterHeight);
                if (slot == 0) AudioManager.Get()?.Play("waterSplash");
            }
            card.InWater = inWater || inTransit;
            float splashFrame = (now - card.SplashStarted) * 60;
            Sprite splash = splashFrame < splashTicks ? AnimationSprite(splashAnimation, splashFrame, false) : null;
            if (splashTicks == 0 && splashFrame < splashFrames.Length * 6)
                splash = splashFrames[Mathf.Max(0, (int)(splashFrame / 6))];
            // The painted splash begins above the atlas frame's bottom pivot.
            DrawPracticeEffect(card.Splash, splash, card.SplashOrigin, 0.75f, pivotLift: 0.1f);
        }

        static Vector2 PracticePoint(float worldX, float worldY) => new Vector2(worldX * PracticeUnits, PracticeY(worldY));

        static void HideReviveFlight(Card card)
        {
            card.ReviveWisp.enabled = card.ReviveFlight.enabled = false;
            foreach (var trail in card.ReviveTrail) trail.enabled = false;
        }

        void UpdateReviveFlight(Card card, Vector2 origin, float age, float arrivalAge)
        {
            Vector2 start = PracticePoint(origin.x, origin.y);
            Vector2 destination = PracticePoint(0, LobbyMotion.ReviveCenterY);
            Vector2 direction = destination - start;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float flightAge = age - LobbyMotion.ReviveWispSeconds;
            float flightTime = flightAge - arrivalAge;
            Sprite wisp = null;
            if (flightAge < 0) wisp = AnimationSprite(anchorFrames, age * 60, false);
            else if (flightAge * 60 < reviveShootTicks)
                wisp = AnimationSprite(reviveShootFrames, flightAge * 60, false);
            DrawPracticeEffect(card.ReviveWisp, wisp, start, angle: flightAge < 0 ? 0 : angle);

            bool flying = flightAge >= 0 && flightTime > 0 && reviveFlightSprite != null;
            DrawPracticeEffect(card.ReviveFlight, flying ? reviveFlightSprite : null,
                Vector2.Lerp(start, destination, flying ? flightAge / flightTime : 0), angle: angle);
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
            for (int i = 0; i < card.ReviveTrail.Length; i++)
            {
                float trailAge = flightAge - (i + 1) * 0.045f;
                bool visible = flying && trailAge >= 0;
                Vector2 point = Vector2.Lerp(start, destination, visible ? trailAge / flightTime : 0);
                point += normal * Mathf.Sin(trailAge * 28 + i) * (4 + i * 2);
                DrawPracticeEffect(card.ReviveTrail[i], visible ? reviveFlightSprite : null, point,
                    0.36f - i * 0.042f, angle, 0.65f - i * 0.10f);
            }
        }

        void UpdatePracticeAnimation(Card card, SpriteFrame[] frames, bool respawning, bool grounded,
            float angle, float verticalSpeed, float respawnAge, float now)
        {
            var image = card.PracticePortrait;
            var rect = image.rectTransform;
            if (card.PracticeAnimation != frames)
            {
                card.PracticeAnimation = frames;
                card.PracticeAnimationStarted = now;
                card.PracticeFallLoopTicks = 0;
            }
            if (frames != null && frames.Length > 0)
            {
                float frameTime = (respawning ? respawnAge : now - card.PracticeAnimationStarted) * 60;
                Sprite sprite = null;
                if (!grounded && !respawning)
                {
                    sprite = AirSprite(verticalSpeed);
                    if (sprite == null)
                    {
                        frameTime = card.PracticeFallLoopTicks;
                        card.PracticeFallLoopTicks += Time.unscaledDeltaTime * 60;
                    }
                }
                if (sprite == null) sprite = AnimationSprite(frames, frameTime, !respawning && frames != landFrames);
                float scale = PracticeUnits / sprite.pixelsPerUnit;
                rect.sizeDelta = sprite.rect.size * scale;
                rect.pivot = sprite.pivot / sprite.rect.size;
                image.sprite = sprite;
            }
            else image.sprite = card.Portrait.sprite;
            var rotation = Quaternion.Euler(0, 0, respawning ? 0 : angle);
            rect.anchoredPosition = rotation * (Vector3.up * (motion.PlayerRadius * PracticeUnits));
            rect.localRotation = rotation;
            rect.localScale = respawning || card.Facing > 0 ? faceRight : faceLeft;
        }

        Sprite AirSprite(float speed)
        {
            if (airFrames == null || airFrames.Length == 0) return null;
            if (speed > (float)airFrames[0].speedTo) return airFrames[0].sprite;
            foreach (var frame in airFrames)
                if (speed >= (float)frame.speedFrom && speed <= (float)frame.speedTo)
                    return frame.sprite;
            return null;
        }

        static Sprite AnimationSprite(SpriteFrame[] frames, float ticks, bool loop)
        {
            if (frames == null || frames.Length == 0) return null;
            if (loop)
            {
                float duration = AnimationTicks(frames);
                if (duration > 0) ticks %= duration;
            }
            Sprite sprite = frames[0].sprite;
            foreach (var frame in frames)
            {
                if (frame.frameDuration <= 0) continue;
                sprite = frame.sprite;
                if (ticks < frame.frameDuration) break;
                ticks -= frame.frameDuration;
            }
            return sprite;
        }

        static float AnimationTicks(SpriteFrame[] frames)
        {
            float duration = 0;
            if (frames != null)
                foreach (var frame in frames) duration += Mathf.Max(0, frame.frameDuration);
            return duration;
        }

        void OnDestroy()
        {
            respawnDisposed = true;
            if (respawnReadsPending == 0)
            {
                Destroy(respawnBlack);
                Destroy(respawnWhite);
            }
            if (respawnPreview != null) Destroy(respawnPreview);
            if (respawnTexture != null) Destroy(respawnTexture);
            if (respawnReadback != null) Destroy(respawnReadback);
            if (root != null) Destroy(root);
            if (RoundedFill != null) Destroy(RoundedFill);
            if (RoundedBorder != null) Destroy(RoundedBorder);
            if (fillTexture != null) Destroy(fillTexture);
            if (borderTexture != null) Destroy(borderTexture);
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "Start")]
    internal static class OnlinePlayerRosterPatch
    {
        static void Postfix(CharacterSelectHandler_online __instance)
        {
            __instance.gameObject.AddComponent<OnlinePlayerRoster>().Handler = __instance;
        }
    }

    [HarmonyPatch(typeof(SteamManager), "KickPlayer")]
    internal static class HostOnlyKickPatch
    {
        internal const string MessagePrefix = "bopl8:kick:";
        internal static Func<SteamManager, string, bool> SendMessage = (manager, message) => manager.currentLobby.SendChatString(message);
        internal static bool Prefix(SteamManager __instance, int connectedPlayerIndex)
        {
            if (!SteamClient.IsValid || !__instance.currentLobby.IsOwnedBy(SteamClient.SteamId)
                || !GameSession.inMenus || connectedPlayerIndex < 0 || connectedPlayerIndex >= __instance.connectedPlayers.Count) return false;
            var target = __instance.connectedPlayers[connectedPlayerIndex];
            if (target.id == SteamClient.SteamId || !target.Connected || target.beingKicked) return false;
            // Vanilla kicks by display name; Steam identity avoids kicking two
            // people who happen to use the same name.
            if (SendMessage(__instance, MessagePrefix + (ulong)target.id)) target.beingKicked = true;
            else Main.Log.LogWarning("Steam could not deliver the lobby kick; the host can retry.");
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamManager), "OnChatMessageCallback")]
    internal static class HostKickMessagePatch
    {
        internal static Action Leave = () => GameSessionHandler.LeaveGame(abandonLobbyEntirely: false, sendOthersBackToAbilitySelect: false);
        internal static bool Accept(SteamManager manager, Lobby lobby, SteamId sender, string message)
        {
            return message != null && message.StartsWith(HostOnlyKickPatch.MessagePrefix, StringComparison.Ordinal)
                && (ulong)manager.currentLobby.Id == (ulong)lobby.Id && lobby.IsOwnedBy(sender)
                && ulong.TryParse(message.Substring(HostOnlyKickPatch.MessagePrefix.Length), out var target)
                && target == (ulong)SteamClient.SteamId && GameSession.inMenus;
        }
        static bool Prefix(SteamManager __instance, Lobby lobby, Friend sender, string msg)
        {
            if (msg == null || !msg.StartsWith(HostOnlyKickPatch.MessagePrefix, StringComparison.Ordinal)) return true;
            if (Accept(__instance, lobby, sender.Id, msg)) Leave();
            return false;
        }
    }
}
