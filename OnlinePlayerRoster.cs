using System;
using HarmonyLib;
using Steamworks;
using Lobby = Steamworks.Data.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    // Keep native remote boxes alive for networking/animations; render a compact
    // roster independently so selecting a player never edits their loadout.
    internal sealed class OnlinePlayerRoster : MonoBehaviour
    {
        internal CharacterSelectHandler_online Handler;
        internal SteamConnection SelectedPlayer;
        internal bool LocalSelected;
        internal Button AddPlayerButton;
        internal Button KickButton;
        internal TextMeshProUGUI DetailName;
        internal TextMeshProUGUI[] AbilityNames = new TextMeshProUGUI[3];
        internal int OpenSlots { get; private set; }
        internal static Action<int> SendKick = index => SteamManager.instance.KickPlayer(index);
        GameObject root;
        RectTransform panel;
        TextMeshProUGUI summary, detailStatus;
        Button[] cards;
        TextMeshProUGUI[] names, states;
        Image[] portraits, cardBorders, icons = new Image[3];
        NamedSpriteList abilities;

        void Start()
        {
            var ui = Handler.GetComponent<OnlineInviteMenu>();
            var canvas = (RectTransform)Handler.GetComponentInParent<Canvas>().transform;
            panel = OnlineInviteMenu.Rect("PlayerRoster", canvas, Vector2.zero, new Vector2(2200, 1250));
            panel.anchorMin = panel.anchorMax = new Vector2(0.605f, 0.445f);
            root = panel.gameObject;
            abilities = Handler.characterSelectBox.abilityGrid.abilityIcons;
            summary = ui.Text("RosterTitle", panel, new Vector2(-400, 585), new Vector2(1400, 90), "PLAYERS", 55);
            int count = Constants.MAX_PLAYERS;
            OpenSlots = Math.Min(4, count);
            cards = new Button[count]; names = new TextMeshProUGUI[count];
            states = new TextMeshProUGUI[count]; portraits = new Image[count];
            cardBorders = new Image[count];
            var native = Handler.networkPlayerBoxes[0];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var position = new Vector2(-870 + (i % 4) * 355, 265 - (i / 4) * 480);
                cards[i] = ui.Button("InspectPlayer" + i, panel, position, new Vector2(325, 330), "", () => Select(index));
                // Reuse the game's outlined, rounded player badge rather than a
                // rectangular UI button. Keep the outline separate from its fill.
                var border = cards[i].GetComponent<Image>();
                CopySprite(border, native.borderImages[native.borderImages.Length - 1]);
                cardBorders[i] = border;
                var fillRect = OnlineInviteMenu.Rect("BadgeFill", cards[i].transform, Vector2.zero, new Vector2(307, 312));
                fillRect.SetAsFirstSibling();
                var fill = fillRect.gameObject.AddComponent<Image>();
                CopySprite(fill, native.fillImages[native.fillImages.Length - 1]);
                fill.raycastTarget = false;
                fillRect.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                cards[i].targetGraphic = fill;
                names[i] = ui.Text("PlayerName", cards[i].transform, new Vector2(0, 205), new Vector2(330, 80), "", 40);
                names[i].enableAutoSizing = true; names[i].fontSizeMin = 24; names[i].fontSizeMax = 40;
                var originalPortrait = native.characters[native.characters.Length - 1];
                var originalBorder = native.borderImages[native.borderImages.Length - 1];
                Vector2 badgeRatio = new Vector2(307 / originalBorder.rectTransform.rect.width, 312 / originalBorder.rectTransform.rect.height);
                var portrait = OnlineInviteMenu.Rect("Character", fillRect,
                    Vector2.Scale(originalPortrait.rectTransform.anchoredPosition, badgeRatio),
                    Vector2.Scale(originalPortrait.rectTransform.rect.size, badgeRatio));
                portraits[i] = portrait.gameObject.AddComponent<Image>();
                CopySprite(portraits[i], originalPortrait);
                portraits[i].preserveAspect = true; portraits[i].raycastTarget = false;
                states[i] = ui.Text("PlayerState", cards[i].transform, new Vector2(0, 115), new Vector2(290, 70), "", 32);
            }
            AddPlayerButton = ui.Button("AddPlayer", panel, new Vector2(-390, -525), new Vector2(670, 100), "+ ADD PLAYER", AddPlayer);
            CopySprite(AddPlayerButton.GetComponent<Image>(), Handler.findPlayersBorder);
            var detail = OnlineInviteMenu.Rect("PlayerKit", panel, new Vector2(730, 0), new Vector2(720, 1150));
            var detailBackground = detail.gameObject.AddComponent<Image>();
            CopySprite(detailBackground, native.fillImages[native.fillImages.Length - 1]);
            detailBackground.color = Handler.darkBlue;
            DetailName = ui.Text("SelectedName", detail, new Vector2(0, 460), new Vector2(655, 160), "SELECT A PLAYER", 55);
            DetailName.enableAutoSizing = true; DetailName.fontSizeMin = 28; DetailName.fontSizeMax = 55;
            detailStatus = ui.Text("SelectedStatus", detail, new Vector2(0, 290), new Vector2(650, 160), "Click a card to view their full kit.", 36);
            for (int i = 0; i < 3; i++)
            {
                var rect = OnlineInviteMenu.Rect("AbilityIcon" + i, detail, new Vector2(-220, 90 - i * 160), new Vector2(125, 125));
                icons[i] = rect.gameObject.AddComponent<Image>(); icons[i].preserveAspect = true; icons[i].raycastTarget = false;
                AbilityNames[i] = ui.Text("AbilityName" + i, detail, new Vector2(85, 90 - i * 160), new Vector2(400, 130), "", 40);
                AbilityNames[i].enableAutoSizing = true; AbilityNames[i].fontSizeMin = 24; AbilityNames[i].fontSizeMax = 40;
            }
            KickButton = ui.Button("KickSelectedPlayer", detail, new Vector2(0, -440), new Vector2(620, 100), "KICK PLAYER", KickSelected);
            CopySprite(KickButton.GetComponent<Image>(), Handler.findPlayersBorder);
            KickButton.gameObject.SetActive(false);
            // Suppress the old visual layout without stopping its Update methods.
            foreach (var box in Handler.networkPlayerBoxes)
            {
                var viewport = box.GetComponentInParent<RectMask2D>();
                var visual = viewport != null ? viewport.transform.parent.gameObject : box.gameObject;
                var group = visual.GetComponent<CanvasGroup>() ?? visual.AddComponent<CanvasGroup>();
                group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
            }
        }

        static void CopySprite(Image destination, Image source)
        {
            destination.sprite = source.sprite;
            destination.type = source.type;
            destination.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }

        internal void Select(int index)
        {
            if (index < 0 || index >= cards.Length) return;
            LocalSelected = index == 0;
            SelectedPlayer = index > 0 && index <= Handler.networkPlayerBoxes.Length
                ? Handler.networkPlayerBoxes[index - 1].connectedPlayer : null;
            if (!LocalSelected && (SelectedPlayer == null || !SelectedPlayer.Connected))
            {
                SelectedPlayer = null;
                OnlineInviteFriends.Invite(Handler);
            }
        }

        internal void AddPlayer()
        {
            if (!OnlineInviteFriends.Available(Handler)) return;
            OpenSlots = Math.Min(Constants.MAX_PLAYERS, OpenSlots + 1);
            OnlineInviteFriends.Invite(Handler);
        }

        internal bool CanKickSelected()
        {
            var manager = SteamManager.instance;
            return manager != null && SteamClient.IsValid && manager.currentLobby.IsOwnedBy(SteamClient.SteamId)
                && !Handler.isStartingAGame && Handler.goingBackOperation == null && SelectedPlayer != null
                && SelectedPlayer.Connected && !SelectedPlayer.beingKicked && SelectedPlayer.id != SteamClient.SteamId
                && manager.connectedPlayers.Contains(SelectedPlayer);
        }

        internal void KickSelected()
        {
            if (!CanKickSelected()) return;
            // Resolve identity at click time: disconnected peers can reorder the list.
            int index = SteamManager.instance.connectedPlayers.IndexOf(SelectedPlayer);
            SendKick(index);
            SelectedPlayer = null;
        }

        void LateUpdate()
        {
            if (root == null) return;
            var canvas = (RectTransform)panel.parent;
            float scale = Mathf.Min(canvas.rect.width * 0.59f / 2200, canvas.rect.height * 0.63f / 1250);
            panel.localScale = Vector3.one * scale;
            bool active = Handler.goingBackOperation == null && !Handler.isStartingAGame;
            root.SetActive(active);
            if (!active) return;
            int present = 1;
            for (int i = 0; i < cards.Length; i++)
            {
                var peer = i == 0 ? null : Handler.networkPlayerBoxes[i - 1].connectedPlayer;
                bool exists = i == 0 || peer != null && peer.Connected;
                if (exists) OpenSlots = Math.Max(OpenSlots, i + 1);
                cards[i].gameObject.SetActive(exists || i < OpenSlots);
                portraits[i].enabled = exists;
                if (!exists)
                {
                    names[i].text = "PLAYER " + (i + 1);
                    states[i].text = "+ INVITE PLAYER";
                    states[i].rectTransform.anchoredPosition = Vector2.zero;
                    states[i].color = names[i].color = Handler.darkBlue;
                    cardBorders[i].color = Handler.disabledBlue;
                    cards[i].interactable = OnlineInviteFriends.Available(Handler);
                    var emptyPalette = cards[i].colors;
                    emptyPalette.normalColor = Handler.disabledWhite;
                    cards[i].colors = emptyPalette;
                    continue;
                }
                cards[i].interactable = true;
                if (i > 0) present++;
                names[i].text = i == 0 ? SteamClient.Name + " (YOU)" : peer.steamName;
                bool ready = i == 0 ? CharacterSelectHandler_online.IsLocalPlayerReady() : peer.lobby_isReady;
                states[i].text = ready ? "READY" : "CHOOSING KIT";
                states[i].rectTransform.anchoredPosition = new Vector2(0, 115);
                int color = i == 0 ? Handler.characterSelectBox.playerInit.color : peer.lobby_color;
                for (int c = 0; c < Handler.playerColors.Length; c++)
                    if (Handler.playerColors[c].colorIndex == color) portraits[i].material = Handler.playerColors[c].uiMaterial;
                var palette = cards[i].colors;
                int teamIndex = i == 0 ? Handler.characterSelectBox.playerInit.team : peer.lobby_team;
                var teamColors = Handler.networkPlayerBoxes[0].teamColors;
                var teamColor = teamColors.teamColors[Mathf.Clamp(teamIndex, 0, teamColors.Length - 1)];
                bool selectedCard = i == 0 && LocalSelected || peer != null && peer == SelectedPlayer;
                cardBorders[i].color = selectedCard ? Handler.orange : teamColor.border;
                states[i].color = names[i].color = teamColor.border;
                palette.normalColor = teamColor.fill;
                palette.highlightedColor = teamColor.saturated;
                cards[i].colors = palette;
            }
            summary.text = "PLAYERS  " + present + " / " + Constants.MAX_PLAYERS;
            AddPlayerButton.interactable = OnlineInviteFriends.Available(Handler);
            AddPlayerButton.GetComponentInChildren<TextMeshProUGUI>().text = OpenSlots < Constants.MAX_PLAYERS ? "+ ADD PLAYER" : "INVITE PLAYERS";
            if (SelectedPlayer != null && !SelectedPlayer.Connected) SelectedPlayer = null;
            bool selected = LocalSelected || SelectedPlayer != null;
            DetailName.text = LocalSelected ? SteamClient.Name + " (YOU)" : SelectedPlayer?.steamName ?? "SELECT A PLAYER";
            int team = LocalSelected ? Handler.characterSelectBox.playerInit.team : SelectedPlayer?.lobby_team ?? 0;
            bool selectedReady = LocalSelected ? CharacterSelectHandler_online.IsLocalPlayerReady() : SelectedPlayer?.lobby_isReady == true;
            detailStatus.text = selected ? (selectedReady ? "READY" : "CHOOSING KIT") + "  /  TEAM " + (team + 1) : "Click a card to view their full kit.";
            for (int i = 0; i < 3; i++)
            {
                bool show = selected && i < Settings.Get().NumberOfAbilities;
                icons[i].enabled = show;
                AbilityNames[i].gameObject.SetActive(show);
                if (!show) continue;
                var local = Handler.characterSelectBox.playerInit;
                int ability = LocalSelected ? (i == 0 ? local.ability0 : i == 1 ? local.ability1 : local.ability2)
                    : (i == 0 ? SelectedPlayer.lobby_ability1 : i == 1 ? SelectedPlayer.lobby_ability2 : SelectedPlayer.lobby_ability3);
                bool known = (LocalSelected || selectedReady) && abilities != null && ability >= 0 && ability < abilities.sprites.Count;
                icons[i].sprite = known ? abilities.sprites[ability].sprite : null;
                icons[i].enabled = known;
                AbilityNames[i].text = known ? LocalizedText.localizationTable.GetText(abilities.sprites[ability].name, Settings.Get().Language) : "Choosing...";
            }
            KickButton.gameObject.SetActive(CanKickSelected());
        }

        void OnDestroy() { if (root != null) Destroy(root); }
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
