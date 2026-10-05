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
        internal bool IsOpen => root != null && root.activeSelf;
        internal bool BlocksInput => IsOpen || closedFrame == Time.frameCount;
        internal int FriendCount => rows.Count;
        GameObject root;
        RectTransform content;
        TextMeshProUGUI status;
        bool previousInputEnabled;
        int closedFrame = -1;
        GameObject previousSelection;
        readonly List<Tuple<Button, bool>> rows = new List<Tuple<Button, bool>>();

        internal void Open()
        {
            if (IsOpen || !OnlineInviteFriends.Available(Handler)) return;
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
            StartCoroutine(RestoreInput());
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(previousSelection);
        }

        IEnumerator RestoreInput()
        {
            yield return null;
            if (!IsOpen && Handler != null) Handler.characterSelectBox.enabled = previousInputEnabled;
        }

        void Build()
        {
            var canvas = Handler.GetComponentInParent<Canvas>();
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

            var panel = Rect("InvitePanel", rootRect, Vector2.zero, new Vector2(1000, 1200));
            panel.gameObject.AddComponent<Image>().color = Handler.darkBlue;
            Text("Title", panel, new Vector2(-50, 505), new Vector2(780, 100), "INVITE PLAYERS", 64);
            Button("Close", panel, new Vector2(400, 505), new Vector2(130, 85), "CLOSE", Close);
            status = Text("LobbyStatus", panel, new Vector2(0, 400), new Vector2(900, 75), "", 38);

            var scrollRect = Rect("FriendList", panel, new Vector2(0, 15), new Vector2(890, 660));
            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 70;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", scrollRect, Vector2.zero, Vector2.zero);
            Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Friends", viewport, Vector2.zero, new Vector2(890, 660));
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1);
            content.pivot = new Vector2(0.5f, 1);
            scroll.viewport = viewport;
            scroll.content = content;

            Text("VersionReminder", panel, new Vector2(0, -385), new Vector2(900, 80), "Friends need Bopl 8 Players 0.3.2 installed.", 34);
            Button("Refresh", panel, new Vector2(-285, -505), new Vector2(290, 95), "REFRESH", Refresh);
            Button("SteamInviteWindow", panel, new Vector2(165, -505), new Vector2(530, 95), "STEAM INVITE WINDOW", () =>
            {
                if (OnlineInviteFriends.Available(Handler)) OnlineInviteFriends.OpenOverlay(SteamManager.instance.currentLobby.Id);
            });
        }

        internal void Refresh()
        {
            foreach (Transform child in content) Destroy(child.gameObject);
            rows.Clear();
            var lobbyMembers = new HashSet<ulong>(SteamManager.instance.currentLobby.Members.Select(friend => (ulong)friend.Id));
            var friends = SteamFriends.GetFriends().Where(friend => friend.IsOnline && !friend.IsMe && !lobbyMembers.Contains((ulong)friend.Id))
                .OrderByDescending(friend => friend.IsPlayingThisGame).ThenBy(friend => friend.Name).ToList();
            content.sizeDelta = new Vector2(890, Mathf.Max(660, friends.Count * 110));
            content.anchoredPosition = Vector2.zero;
            for (int i = 0; i < friends.Count; i++)
            {
                var friend = friends[i];
                var row = Rect("FriendRow", content, new Vector2(0, -55 - i * 110), new Vector2(875, 95));
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 1);
                row.gameObject.AddComponent<Image>().color = Handler.blue * new Color(1, 1, 1, 0.25f);
                Text("Name", row, new Vector2(-130, 0), new Vector2(565, 85), friend.Name, 42);
                Button invite = null;
                invite = Button("Invite", row, new Vector2(320, 0), new Vector2(220, 80), "INVITE", () =>
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
                rows.Add(Tuple.Create(invite, false));
            }
            if (friends.Count == 0)
            {
                var empty = Text("NoFriends", content, new Vector2(0, -160), new Vector2(820, 240), "No online friends to invite.\nTry the Steam invite window below.", 42);
                empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(0.5f, 1);
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
