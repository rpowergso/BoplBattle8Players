using System;
using HarmonyLib;
using Steamworks;
using Lobby = Steamworks.Data.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    // The original local selector owns input, keybinds, ability choices and ready state.
    // The roster mirrors lobby state without changing how the game synchronizes it.
    internal sealed class OnlinePlayerRoster : MonoBehaviour
    {
        const float PanelWidth = 1540;
        const float PanelHeight = 1240;
        internal CharacterSelectHandler_online Handler;
        internal Button AddPlayerButton;
        internal int OpenSlots { get; private set; }
        internal RectTransform LocalEditor { get; private set; }
        internal static Action<int> SendKick = index => SteamManager.instance.KickPlayer(index);
#if BOPL8_UI_SMOKE
        internal static string LocalNameOverride;
#endif
        internal Card[] Cards { get; private set; }
        internal Sprite RoundedFill { get; private set; }
        GameObject root;
        RectTransform canvas;
        RectTransform panel;
        TextMeshProUGUI summary;
        NamedSpriteList abilities;
        Image startFill;
        Image startOutline;
        Image nativeStartFill;
        Sprite roundedBorder;
        Texture2D fillTexture, borderTexture;

        internal sealed class Card
        {
            internal RectTransform Root;
            internal TextMeshProUGUI Name, State;
            internal Image Border, Fill, Portrait;
            internal Button KickButton;
            internal Image[] AbilityIcons = new Image[3];
            internal TextMeshProUGUI[] EmptyIcons = new TextMeshProUGUI[3];
        }

        void Start()
        {
            var ui = Handler.GetComponent<OnlineInviteMenu>();
            canvas = (RectTransform)Handler.GetComponentInParent<Canvas>().transform;
            panel = OnlineInviteMenu.Rect("PlayerRoster", canvas, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            panel.anchorMin = panel.anchorMax = new Vector2(0.385f, 0.445f);
            root = panel.gameObject;
            LocalEditor = (RectTransform)Handler.characterSelectBox.transform;
            PositionLocalEditor();
            abilities = Handler.characterSelectBox.abilityGrid.abilityIcons;
            RoundedFill = MakeRoundedSprite(false, out fillTexture);
            roundedBorder = MakeRoundedSprite(true, out borderTexture);
            RestyleStartButton();
            summary = ui.Text("RosterTitle", panel, new Vector2(0, 570), new Vector2(1400, 88), "PLAYERS", 54);
            OpenSlots = Math.Min(4, Constants.MAX_PLAYERS);
            Cards = new Card[Constants.MAX_PLAYERS];
            var native = Handler.networkPlayerBoxes[0];
            for (int i = 0; i < Cards.Length; i++)
            {
                var position = new Vector2(-570 + (i % 4) * 380, 275 - (i / 4) * 465);
                Cards[i] = BuildCard(ui, native, i, position);
            }

            AddPlayerButton = ui.Button("AddPlayer", panel, new Vector2(0, -545), new Vector2(630, 90), "+ ADD PLAYER", AddPlayer);
            ApplySprite(AddPlayerButton.GetComponent<Image>(), RoundedFill, 0.4f);

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

        Card BuildCard(OnlineInviteMenu ui, CSBox_online native, int index, Vector2 position)
        {
            var card = new Card();
            card.Root = OnlineInviteMenu.Rect("PlayerSlot" + (index + 1), panel, position, new Vector2(340, 256));
            card.Fill = card.Root.gameObject.AddComponent<Image>();
            ApplySprite(card.Fill, RoundedFill, 0.4f);
            card.Fill.raycastTarget = false;
            card.Root.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            var source = native.characters[native.characters.Length - 1];
            var portrait = OnlineInviteMenu.Rect("Character", card.Root, new Vector2(0, -28), new Vector2(268, 211));
            card.Portrait = portrait.gameObject.AddComponent<Image>();
            card.Portrait.sprite = source.sprite;
            card.Portrait.preserveAspect = true;
            card.Portrait.raycastTarget = false;

            var borderRect = OnlineInviteMenu.Rect("SlotBorder", card.Root, Vector2.zero, card.Root.sizeDelta);
            card.Border = borderRect.gameObject.AddComponent<Image>();
            ApplySprite(card.Border, roundedBorder, 0.4f);
            card.Border.raycastTarget = false;
            card.State = ui.Text("PlayerState", card.Root, new Vector2(0, 84), new Vector2(255, 46), "", 28);

            // Labels and abilities live outside the portrait clipping area.
            card.Name = ui.Text("PlayerName", panel, position + new Vector2(0, 164), new Vector2(350, 66), "", 40);
            card.Name.enableAutoSizing = true;
            card.Name.fontSizeMin = 24;
            card.Name.fontSizeMax = 40;
            for (int i = 0; i < card.AbilityIcons.Length; i++)
            {
                var iconPosition = position + new Vector2((i - 1) * 102, -183);
                var tile = OnlineInviteMenu.Rect("AbilitySlot" + (i + 1), panel, iconPosition, new Vector2(84, 82));
                var background = tile.gameObject.AddComponent<Image>();
                ApplySprite(background, RoundedFill, 1.0f);
                background.color = new Color(0.05f, 0.23f, 0.38f, 0.82f);
                background.raycastTarget = false;
                var icon = OnlineInviteMenu.Rect("Icon", tile, Vector2.zero, new Vector2(67, 65)).gameObject.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                card.AbilityIcons[i] = icon;
                card.EmptyIcons[i] = ui.Text("Empty", tile, Vector2.zero, new Vector2(60, 60), "–", 32);
            }

            if (index > 0)
            {
                card.KickButton = ui.Button("KickPlayer" + (index + 1), card.Root,
                    new Vector2(125, 83), new Vector2(55, 49), "×", () => Kick(GetPeer(index)));
                ApplySprite(card.KickButton.GetComponent<Image>(), RoundedFill, 1.5f);
            }
            return card;
        }

        void PositionLocalEditor()
        {
            var position = canvas.InverseTransformPoint(LocalEditor.position);
            position.x = Mathf.Lerp(canvas.rect.xMin, canvas.rect.xMax, 0.795f);
            LocalEditor.position = canvas.TransformPoint(position);
        }

        void RestyleStartButton()
        {
            var border = Handler.startBorder;
            nativeStartFill = Handler.startButton.GetComponent<Image>();
            // Preserve native graphics and raycasts for input and hover state.
            if (nativeStartFill != null) nativeStartFill.canvasRenderer.cullTransparentMesh = false;
            border.canvasRenderer.cullTransparentMesh = false;
            startFill = BuildStartGraphic("StartFill", RoundedFill, 0);
            startOutline = BuildStartGraphic("StartOutline", roundedBorder, 1);
        }

        Image BuildStartGraphic(string name, Sprite sprite, int sibling)
        {
            var rect = OnlineInviteMenu.Rect(name, Handler.startButton.transform, Vector2.zero, Vector2.zero);
            rect.SetSiblingIndex(sibling);
            rect.anchorMin = new Vector2(0.22f, 0);
            rect.anchorMax = new Vector2(0.78f, 1);
            var image = rect.gameObject.AddComponent<Image>();
            ApplySprite(image, sprite, 0.42f);
            image.raycastTarget = false;
            return image;
        }

        void RefreshStartGraphics()
        {
            var border = Handler.startBorder;
            var stateColor = border.color;
            startFill.color = nativeStartFill != null && nativeStartFill != border ? nativeStartFill.color : stateColor;
            startOutline.color = new Color(Handler.darkBlue.r, Handler.darkBlue.g, Handler.darkBlue.b, stateColor.a);
            if (nativeStartFill != null) nativeStartFill.canvasRenderer.SetAlpha(0);
            border.canvasRenderer.SetAlpha(0);
        }

        static void ApplySprite(Image image, Sprite sprite, float pixelsPerUnitMultiplier)
        {
            image.sprite = sprite;
            image.overrideSprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        }

        static Sprite MakeRoundedSprite(bool border, out Texture2D texture)
        {
            const int size = 64;
            const float cornerRadius = 10f;
            const float stroke = 2.5f;
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
                pixels[y * size + x] = new Color(1f, 1f, 1f, opacity);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var slice = new Vector4(12, 12, 12, 12);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, slice);
        }

        internal void AddPlayer()
        {
            if (!OnlineInviteFriends.Available(Handler)) return;
            OpenSlots = Math.Min(Constants.MAX_PLAYERS, OpenSlots + 1);
            OnlineInviteFriends.Invite(Handler);
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
            float scale = Mathf.Min(canvas.rect.width * 0.58f / PanelWidth, canvas.rect.height * 0.61f / PanelHeight);
            panel.localScale = Vector3.one * scale;
            bool active = Handler.goingBackOperation == null && !Handler.isStartingAGame;
            root.SetActive(active);
            if (!active) return;
            PositionLocalEditor();
            RefreshStartGraphics();

            int present = 0;
            for (int i = 0; i < Cards.Length; i++)
            {
                var peer = GetPeer(i);
                bool exists = i == 0 || peer != null && peer.Connected;
                if (exists)
                {
                    present++;
                    OpenSlots = Math.Max(OpenSlots, i + 1);
                }
                UpdateCard(Cards[i], i, peer, exists);
            }
            summary.text = "PLAYERS  " + present + " / " + Constants.MAX_PLAYERS;
            AddPlayerButton.interactable = OnlineInviteFriends.Available(Handler);
            AddPlayerButton.GetComponentInChildren<TextMeshProUGUI>().text = OpenSlots < Constants.MAX_PLAYERS ? "+ ADD PLAYER" : "INVITE PLAYERS";
        }

        void UpdateCard(Card card, int index, SteamConnection peer, bool exists)
        {
            bool visible = index < OpenSlots;
            bool local = index == 0;
            card.Root.gameObject.SetActive(visible);
            card.Name.gameObject.SetActive(visible);
            card.Portrait.enabled = exists;
            if (card.KickButton != null) card.KickButton.gameObject.SetActive(CanKick(peer));

            bool ready = exists && (local ? CharacterSelectHandler_online.IsLocalPlayerReady() : peer.lobby_isReady);
            var kit = Handler.characterSelectBox.playerInit;
            string localName = SteamClient.Name;
#if BOPL8_UI_SMOKE
            localName = LocalNameOverride ?? localName;
#endif
            card.Name.text = local ? localName + " (YOU)" : exists ? peer.steamName : "PLAYER " + (index + 1);
            card.State.text = exists ? (ready ? "READY" : "CHOOSING KIT") : "OPEN SLOT";
            card.State.rectTransform.anchoredPosition = new Vector2(0, exists ? 84 : 0);
            if (exists)
            {
                int color = local ? kit.color : peer.lobby_color;
                for (int i = 0; i < Handler.playerColors.Length; i++)
                    if (Handler.playerColors[i].colorIndex == color) card.Portrait.material = Handler.playerColors[i].uiMaterial;
                int teamIndex = local ? kit.team : peer.lobby_team;
                var teamColors = Handler.networkPlayerBoxes[0].teamColors;
                var team = teamColors.teamColors[Mathf.Clamp(teamIndex, 0, teamColors.Length - 1)];
                card.Fill.color = team.fill;
                card.Border.color = card.Name.color = card.State.color = team.border;
            }
            else
            {
                card.Fill.color = Handler.disabledWhite;
                card.Border.color = Handler.disabledBlue;
                card.Name.color = card.State.color = Handler.darkBlue;
            }

            int abilityCount = Settings.Get().NumberOfAbilities;
            for (int i = 0; i < card.AbilityIcons.Length; i++)
            {
                int ability = -1;
                if (local)
                    ability = i == 0 ? kit.ability0 : i == 1 ? kit.ability1 : kit.ability2;
                else if (ready)
                    ability = i == 0 ? peer.lobby_ability1 : i == 1 ? peer.lobby_ability2 : peer.lobby_ability3;
                bool known = i < abilityCount && abilities != null && ability >= 0 && ability < abilities.sprites.Count;
                var icon = card.AbilityIcons[i];
                icon.transform.parent.gameObject.SetActive(visible);
                icon.sprite = known ? abilities.sprites[ability].sprite : null;
                icon.enabled = known;
                card.EmptyIcons[i].enabled = !known;
            }
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (RoundedFill != null) Destroy(RoundedFill);
            if (roundedBorder != null) Destroy(roundedBorder);
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
        internal static Action<SteamManager, string> SendMessage = (manager, message) => manager.currentLobby.SendChatString(message);
        internal static bool Prefix(SteamManager __instance, int connectedPlayerIndex)
        {
            if (!SteamClient.IsValid || !__instance.currentLobby.IsOwnedBy(SteamClient.SteamId)
                || !GameSession.inMenus || connectedPlayerIndex < 0 || connectedPlayerIndex >= __instance.connectedPlayers.Count) return false;
            var target = __instance.connectedPlayers[connectedPlayerIndex];
            if (target.id == SteamClient.SteamId || !target.Connected || target.beingKicked) return false;
            // Vanilla kicks by display name; Steam identity avoids kicking two
            // people who happen to use the same name.
            SendMessage(__instance, MessagePrefix + (ulong)target.id);
            target.beingKicked = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamManager), "OnChatMessageCallback")]
    internal static class HostKickMessagePatch
    {
        internal static Action Leave = () => GameSessionHandler.LeaveGame(abandonLobbyEntirely: false, sendOthersBackToAbilitySelect: true);
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
