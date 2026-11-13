using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MorePlayers
{
    internal sealed class OnlineInviteMenu : MonoBehaviour
    {
        internal CharacterSelectHandler_online Handler;
        internal static Func<SteamId, bool> SendInvite = id => SteamManager.instance.currentLobby.InviteFriend(id);
#if BOPL8_UI_SMOKE
        internal static List<Friend> TestFriends;
#endif
        internal bool IsOpen => root != null && root.activeSelf;
        internal bool BlocksInput => IsOpen || restoringInput || closedFrame == Time.frameCount;
        internal int FriendCount => rows.Count;
        GameObject root;
        RectTransform content;
        TextMeshProUGUI status;
        bool previousInputEnabled;
        bool restoringInput;
        int closedFrame = -1;
        GameObject previousSelection;
        readonly List<Tuple<Button, bool>> rows = new List<Tuple<Button, bool>>();

        internal void Open()
        {
            if (IsOpen || !OnlineInviteFriends.Available(Handler)) return;
            if (!restoringInput)
                previousInputEnabled = Handler.characterSelectBox.enabled;
            Handler.characterSelectBox.enabled = false;
            previousSelection = EventSystem.current?.currentSelectedGameObject;
            if (root == null) Build();
            Refresh();
            root.SetActive(true);
        }

        internal void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            closedFrame = Time.frameCount;
            restoringInput = true;
            StartCoroutine(RestoreInput());
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(previousSelection);
        }

        IEnumerator RestoreInput()
        {
            yield return null;
            if (IsOpen || Handler == null) yield break;
            Handler.characterSelectBox.enabled = previousInputEnabled;
            restoringInput = false;
        }

        void Build()
        {
            var canvas = Handler.GetComponentInParent<Canvas>();
            var roster = Handler.GetComponent<OnlinePlayerRoster>();
            root = new GameObject("OnlineInvitePlayersMenu", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvas.transform, false);
            Stretch(rootRect);
            var overlayCanvas = root.GetComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = 30000;
            var dim = Rect("Backdrop", rootRect, Vector2.zero, Vector2.zero);
            Stretch(dim);
            var dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0.02f, 0.04f, 0.08f, 0.80f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            var panel = Rect("InvitePanel", rootRect, Vector2.zero, new Vector2(1320, 1180));
            StylePanel(panel, Handler.blue);
            var inner = Rect("InvitePanelInset", panel, Vector2.zero, panel.sizeDelta - new Vector2(43, 43)).gameObject.AddComponent<Image>();
            OnlinePlayerRoster.ApplySprite(inner, roster.RoundedBorder, 0.65f);
            inner.color = Handler.darkBlue * new Color(1, 1, 1, 0.48f);
            inner.raycastTarget = false;
            Text("Title", panel, new Vector2(0, 490), new Vector2(1000, 100), "INVITE PLAYERS", 64).color = Handler.darkBlue;
            var close = Button("Close", panel, new Vector2(540, 486), new Vector2(108, 102), "X", Close);
            StyleButton(close);
            var closeColors = close.colors;
            closeColors.normalColor = new Color(0.75f, 0.15f, 0.20f);
            closeColors.highlightedColor = new Color(0.95f, 0.22f, 0.29f);
            closeColors.pressedColor = new Color(0.60f, 0.08f, 0.14f);
            close.colors = closeColors;
            close.targetGraphic.canvasRenderer.SetColor(closeColors.normalColor);
            close.GetComponentInChildren<TextMeshProUGUI>().color = Color.white;
            status = Text("LobbyStatus", panel, new Vector2(0, 398), new Vector2(900, 75), "", 40);
            status.color = Handler.darkBlue;

            var scrollRect = Rect("FriendList", panel, new Vector2(0, 60), new Vector2(1120, 570));
            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 70;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", scrollRect, Vector2.zero, Vector2.zero);
            Stretch(viewport);
            var listFill = viewport.gameObject.AddComponent<Image>();
            OnlinePlayerRoster.ApplySprite(listFill, roster.RoundedFill, 0.65f);
            listFill.color = new Color(0, 0, 0, 0.04f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Friends", viewport, Vector2.zero, new Vector2(1120, 570));
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1);
            content.pivot = new Vector2(0.5f, 1);
            scroll.viewport = viewport;
            scroll.content = content;
            var listBorder = Rect("FriendListBorder", scrollRect, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            Stretch(listBorder.rectTransform);
            OnlinePlayerRoster.ApplySprite(listBorder, roster.RoundedBorder, 0.65f);
            listBorder.color = Handler.darkBlue;
            listBorder.raycastTarget = false;

            Text("VersionReminder", panel, new Vector2(0, -292), new Vector2(1120, 70), $"Friends need Bopl 8 Players {Main.ModVersion} installed.", 35).color = Handler.darkBlue;
            StyleButton(Button("Refresh", panel, new Vector2(-355, -430), new Vector2(300, 112), "REFRESH", Refresh));
            var steamWindow = Button("SteamInviteWindow", panel, new Vector2(206, -430), new Vector2(665, 112), "STEAM INVITE WINDOW", () =>
            {
                if (OnlineInviteFriends.Available(Handler)) OnlineInviteFriends.OpenOverlay(SteamManager.instance.currentLobby.Id);
            });
            StyleButton(steamWindow);
            steamWindow.GetComponentInChildren<TextMeshProUGUI>().enableWordWrapping = false;
        }

        internal void Refresh()
        {
            foreach (Transform child in content) Destroy(child.gameObject);
            rows.Clear();
            var lobbyMembers = new HashSet<ulong>(SteamManager.instance.currentLobby.Members.Select(friend => (ulong)friend.Id));
            var friends = SteamFriends.GetFriends().Where(friend => friend.IsOnline && !friend.IsMe && !lobbyMembers.Contains((ulong)friend.Id))
                .OrderByDescending(friend => friend.IsPlayingThisGame).ThenBy(friend => friend.Name).ToList();
#if BOPL8_UI_SMOKE
            if (TestFriends != null) friends = TestFriends;
#endif
            content.sizeDelta = new Vector2(1120, Mathf.Max(570, friends.Count * 150));
            content.anchoredPosition = Vector2.zero;
            for (int i = 0; i < friends.Count; i++)
            {
                var friend = friends[i];
                var row = Rect("FriendRow", content, new Vector2(0, -75 - i * 150), new Vector2(1080, 140));
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 1);
                var namePill = Rect("FriendNamePill", row, new Vector2(-170, 0), new Vector2(720, 128));
                StylePanel(namePill, Handler.blue);
                string friendName = friend.Name;
#if BOPL8_UI_SMOKE
                if (TestFriends != null) friendName = "Test friend " + (i + 1);
#endif
                var name = Text("Name", namePill, Vector2.zero, new Vector2(630, 96), friendName, 46);
                name.color = Handler.darkBlue;
                name.enableAutoSizing = true;
                name.fontSizeMin = 28;
                name.fontSizeMax = 46;
                Button invite = null;
                invite = Button("Invite", row, new Vector2(395, 0), new Vector2(270, 128), "INVITE", () =>
                {
                    if (!OnlineInviteFriends.Available(Handler)) return;
                    bool sent = SendInvite(friend.Id);
                    invite.GetComponentInChildren<TextMeshProUGUI>().text = sent ? "INVITED" : "RETRY";
                    if (sent)
                    {
                        int index = rows.FindIndex(entry => entry.Item1 == invite);
                        if (index >= 0) rows[index] = Tuple.Create(invite, true);
                        invite.interactable = false;
                    }
                });
                StyleButton(invite);
                rows.Add(Tuple.Create(invite, false));
            }
            if (friends.Count == 0)
            {
                var empty = Text("NoFriends", content, new Vector2(0, -160), new Vector2(770, 240), "No online friends to invite.\nTry the Steam invite window below.", 42);
                empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(0.5f, 1);
                empty.color = Handler.darkBlue;
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            if (Handler.goingBackOperation != null || Handler.isStartingAGame) { Close(); return; }
            status.text = SteamManager.instance.currentLobby.MemberCount + " / " + SteamManager.instance.currentLobby.MaxMembers + " PLAYERS";
            bool available = OnlineInviteFriends.Available(Handler);
            foreach (var row in rows) row.Item1.interactable = available && !row.Item2;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.all.Any(pad => pad.bButton.wasPressedThisFrame)) Close();
        }

        void OnDestroy() { if (root != null) Destroy(root); }
        internal static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }
        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }
        internal TextMeshProUGUI Text(string name, Transform parent, Vector2 position, Vector2 size, string value, float fontSize)
        {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Handler.findPlayersText.font;
            text.fontSharedMaterial = Handler.findPlayersText.fontSharedMaterial;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.richText = false;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }
        internal Button Button(string name, Transform parent, Vector2 position, Vector2 size, string label, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(name, parent, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Handler.blue;
            colors.highlightedColor = Handler.orange;
            colors.pressedColor = Handler.darkBlue;
            button.colors = colors;
            button.onClick.AddListener(action);
            var text = Text("Label", rect, Vector2.zero, size * 0.92f, label, 38);
            text.enableAutoSizing = true;
            text.fontSizeMin = 25; text.fontSizeMax = 38;
            return button;
        }

        internal static void CopyImageStyle(Image image, Image reference)
        {
            image.sprite = reference.sprite;
            image.overrideSprite = reference.overrideSprite;
            image.type = reference.type;
            image.preserveAspect = reference.preserveAspect;
            image.pixelsPerUnitMultiplier = reference.pixelsPerUnitMultiplier;
        }

        void StylePanel(RectTransform rect, Color color)
        {
            var roster = Handler.GetComponent<OnlinePlayerRoster>();
            var background = rect.gameObject.AddComponent<Image>();
            OnlinePlayerRoster.ApplySprite(background, roster.RoundedFill, 0.4f);
            background.color = color;
            var border = Rect("PanelBorder", rect, Vector2.zero, rect.sizeDelta).gameObject.AddComponent<Image>();
            OnlinePlayerRoster.ApplySprite(border, roster.RoundedBorder, 0.4f);
            border.color = Handler.darkBlue;
            border.raycastTarget = false;
        }

        internal void StyleButton(Button button)
        {
            var image = button.GetComponent<Image>();
            var rect = (RectTransform)button.transform;
            var roster = Handler.GetComponent<OnlinePlayerRoster>();
            OnlinePlayerRoster.ApplySprite(image, roster.RoundedFill, 0.4f);
            var border = Rect("ButtonBorder", rect, Vector2.zero, rect.sizeDelta).gameObject.AddComponent<Image>();
            OnlinePlayerRoster.ApplySprite(border, roster.RoundedBorder, 0.4f);
            border.color = Handler.darkBlue;
            border.raycastTarget = false;
            border.rectTransform.SetAsFirstSibling();
            var colors = button.colors;
            colors.normalColor = Handler.blue;
            colors.highlightedColor = Handler.orange;
            colors.pressedColor = Handler.orange;
            colors.disabledColor = Handler.disabledBlue;
            button.colors = colors;
            image.canvasRenderer.SetColor(colors.normalColor);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.color = Handler.darkBlue;
            label.fontSizeMax = 44;
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "Update")]
    internal static class OnlineInviteModalInputPatch
    {
        static bool Prefix(CharacterSelectHandler_online __instance)
        {
            var menu = __instance.GetComponent<OnlineInviteMenu>();
            return menu == null || !menu.BlocksInput;
        }
    }
}
