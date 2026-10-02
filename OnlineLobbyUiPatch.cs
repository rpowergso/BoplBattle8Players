using System;
using HarmonyLib;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    [HarmonyPatch(typeof(CharacterSelectHandler_online), "Start")]
    internal static class OnlineLobbyUiPatch
    {
        static void Postfix(CharacterSelectHandler_online __instance)
        {
            var boxes = __instance.networkPlayerBoxes;
            int originalCount = boxes.Length;
            int remoteCount = Constants.MAX_PLAYERS - 1;
            if (originalCount >= remoteCount) return;

            // Only clone the remote display, never the local input/ability selector.
            var template = boxes[originalCount - 1];
            var templateCircle = __instance.loadingCircles[originalCount - 1];
            var localRect = (RectTransform)__instance.characterSelectBox.transform;
            float originalStep = ((RectTransform)boxes[0].transform).anchoredPosition.x - localRect.anchoredPosition.x;
            // Keep the local ability selector at its native size. Remote cards
            // occupy the existing right-hand area, four columns by two rows.
            const float scale = 0.68f;
            float step = originalStep * 0.70f;

            Array.Resize(ref boxes, remoteCount);
            var circles = __instance.loadingCircles;
            Array.Resize(ref circles, remoteCount);
            for (int i = originalCount; i < remoteCount; i++)
            {
                boxes[i] = UnityEngine.Object.Instantiate(template, template.transform.parent);
                boxes[i].name = "OnlinePlayerBox_P" + (i + 2);
                // Instantiate invokes Awake on active clones. Copy the original
                // animation targets, not the template's current off-screen pose.
                var sourceAnimations = template.GetComponentsInChildren<AnimateInOutUI>(true);
                var cloneAnimations = boxes[i].GetComponentsInChildren<AnimateInOutUI>(true);
                for (int j = 0; j < sourceAnimations.Length; j++)
                    if (sourceAnimations[j].originalHeights != null)
                        cloneAnimations[j].originalHeights = (float[])sourceAnimations[j].originalHeights.Clone();
                boxes[i].connectedPlayer = null;
                boxes[i].isVisible = false;
                circles[i] = UnityEngine.Object.Instantiate(templateCircle, templateCircle.transform.parent);
                circles[i].name = "WaitingCircle_P" + (i + 2);
                circles[i].enabled = false;
            }
            __instance.networkPlayerBoxes = boxes;
            __instance.loadingCircles = circles;

            var emptySlots = new GameObject[remoteCount];
            for (int i = 0; i < remoteCount; i++)
            {
                float x = localRect.anchoredPosition.x + originalStep * 0.90f + step * (i % 4);
                float y = localRect.anchoredPosition.y + 300f - 730f * (i / 4);
                var boxRect = (RectTransform)boxes[i].transform;
                float animationTarget = boxes[i].GetComponent<AnimateInOutUI>().originalHeights[0];
                var oldPosition = boxRect.anchoredPosition;
                var oldScale = boxRect.localScale;
                var wrapper = new GameObject("RemoteSlot_P" + (i + 2), typeof(RectTransform));
                var wrapperRect = (RectTransform)wrapper.transform;
                wrapperRect.SetParent(boxRect.parent, false);
                wrapperRect.anchorMin = boxRect.anchorMin;
                wrapperRect.anchorMax = boxRect.anchorMax;
                wrapperRect.pivot = boxRect.pivot;
                wrapperRect.sizeDelta = boxRect.sizeDelta;
                wrapperRect.anchoredPosition = new Vector2(x, y - animationTarget * scale);
                wrapperRect.localScale = Vector3.one * scale;
                // Native cards animate their children well outside the panel.
                // A clip viewport keeps those hidden states out of neighboring
                // cards even when the remote display is smaller than vanilla.
                var clip = new GameObject("CardViewport", typeof(RectTransform), typeof(RectMask2D));
                var clipRect = (RectTransform)clip.transform;
                clipRect.SetParent(wrapperRect, false);
                clipRect.anchorMin = clipRect.anchorMax = new Vector2(0.5f, 0.5f);
                clipRect.sizeDelta = new Vector2(700, 1100);
                clipRect.anchoredPosition = new Vector2(0, animationTarget - 175);
                boxRect.SetParent(clipRect, false);
                boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f);
                boxRect.anchoredPosition = new Vector2(0, oldPosition.y - clipRect.anchoredPosition.y);
                boxRect.localScale = oldScale;
                // Rebase root animation coordinates into the viewport. Nested
                // choosing/ready animations stay in their native coordinates.
                var rootAnimation = boxes[i].GetComponent<AnimateInOutUI>();
                rootAnimation.originalHeights[0] -= clipRect.anchoredPosition.y;
                rootAnimation.startHeight -= clipRect.anchoredPosition.y;
                rootAnimation.endHeight -= clipRect.anchoredPosition.y;
                var occupiedGroup = boxes[i].gameObject.AddComponent<CanvasGroup>();
                occupiedGroup.alpha = 0;
                occupiedGroup.interactable = false;
                occupiedGroup.blocksRaycasts = false;

                circles[i].rectTransform.SetParent(wrapperRect, false);
                circles[i].rectTransform.anchorMin = circles[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                circles[i].rectTransform.anchoredPosition = new Vector2(0, animationTarget);

                var empty = new GameObject("InviteSlot_P" + (i + 2), typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)empty.transform;
                rect.SetParent(wrapperRect, false);
                rect.anchorMin = boxRect.anchorMin;
                rect.anchorMax = boxRect.anchorMax;
                rect.pivot = boxRect.pivot;
                rect.sizeDelta = boxRect.sizeDelta;
                rect.anchoredPosition = new Vector2(0, animationTarget);
                rect.localScale = boxRect.localScale;
                var border = empty.GetComponent<Image>();
                border.sprite = template.borderImages[0].sprite;
                border.type = template.borderImages[0].type;
                border.color = new Color(__instance.blue.r, __instance.blue.g, __instance.blue.b, 0.3f);
                border.raycastTarget = false;

                var text = UnityEngine.Object.Instantiate(template.playerNameText, rect);
                text.name = "InviteLabel";
                text.text = "Waiting for player";
                text.color = __instance.disabledWhite;
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                text.rectTransform.anchoredPosition = Vector2.zero;
                text.rectTransform.sizeDelta = rect.sizeDelta * 0.8f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 20;
                text.fontSizeMax = template.playerNameText.fontSize;
                var group = empty.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                emptySlots[i] = empty;
            }
            var view = __instance.gameObject.AddComponent<OnlineLobbyEmptySlots>();
            view.Boxes = boxes;
            view.EmptySlots = emptySlots;
            view.Handler = __instance;
            Main.Log.LogInfo($"Online lobby UI ready: 1 local + {boxes.Length} remote slots; {circles.Length} loading indicators.");
        }

    }

    internal sealed class OnlineLobbyEmptySlots : MonoBehaviour
    {
        internal CSBox_online[] Boxes;
        internal GameObject[] EmptySlots;
        internal CharacterSelectHandler_online Handler;
        void LateUpdate()
        {
            for (int i = 0; i < Boxes.Length; i++)
            {
                var group = EmptySlots[i].GetComponent<CanvasGroup>();
                bool show = !Boxes[i].isVisible && Handler.goingBackOperation == null && !Handler.isStartingAGame;
                group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 6f);
                var occupied = Boxes[i].GetComponent<CanvasGroup>();
                bool present = Boxes[i].isVisible && Handler.goingBackOperation == null;
                occupied.alpha = Mathf.MoveTowards(occupied.alpha, present ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            }
        }
    }

    // Reuse the native Find Players control, including its hover animation and
    // controller selection path. Never enable public vanilla matchmaking.
    [HarmonyPatch(typeof(CharacterSelectHandler_online), "Start")]
    internal static class OnlineInviteLabelPatch
    {
        static void Postfix(CharacterSelectHandler_online __instance)
        {
            __instance.findPlayersText.text = "INVITE PLAYERS";
            __instance.findPlayersStopText.text = "INVITE PLAYERS";
            __instance.gameObject.AddComponent<OnlineInviteMenu>().Handler = __instance;
        }
    }

    internal static class OnlineInviteFriends
    {
        internal static Action<SteamId> OpenOverlay = SteamFriends.OpenGameInviteOverlay;
        internal static bool Available(CharacterSelectHandler_online handler)
        {
            var manager = SteamManager.instance;
            return manager != null && SteamClient.IsValid && SteamClient.IsLoggedOn
                && (ulong)manager.currentLobby.Id != 0
                && manager.currentLobby.MemberCount < manager.currentLobby.MaxMembers
                && !handler.isStartingAGame && handler.goingBackOperation == null;
        }
        internal static void Invite(CharacterSelectHandler_online handler)
        {
            if (!Available(handler)) return;
            handler.GetComponent<OnlineInviteMenu>().Open();
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "CanClickFindPlayers")]
    internal static class OnlineInviteAvailabilityPatch
    {
        static bool Prefix(CharacterSelectHandler_online __instance, ref bool __result)
        {
            __result = OnlineInviteFriends.Available(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "ClickFindButton")]
    internal static class OnlineInviteClickPatch
    {
        static bool Prefix(CharacterSelectHandler_online __instance)
        {
            OnlineInviteFriends.Invite(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "TryFindPlayers")]
    internal static class OnlineInviteKeyboardPatch
    {
        static bool Prefix()
        {
            var handler = CharacterSelectHandler_online.selfRef;
            if (handler != null) OnlineInviteFriends.Invite(handler);
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterSelectHandler_online), "CheckForMods")]
    internal static class OnlineLobbyModCheckPatch
    {
        static void Postfix()
        {
            // Our custom packets require every participant to have this mod.
            CharacterSelectHandler_online.hasMods = true;
        }
    }

    [HarmonyPatch(typeof(SteamFrame), "Awake")]
    internal static class SteamFrameExtraSlotsPatch
    {
        static void Postfix(SteamFrame __instance)
        {
            if (__instance != SteamFrame.selfRef) return;
            int required = Constants.MAX_PLAYERS - 1;
            int count = __instance.squares.Count;
            if (count >= required || count == 0) return;
            var template = __instance.squares[count - 1];
            var circles = __instance.lookingForPlayersCircles;
            var circleTemplate = circles[count - 1];
            var templateRect = (RectTransform)template.transform;
            Vector2 step = count > 1
                ? templateRect.anchoredPosition - ((RectTransform)__instance.squares[count - 2].transform).anchoredPosition
                : new Vector2(__instance.buttonSeparation, 0);
            Vector2 circleStep = count > 1
                ? circleTemplate.rectTransform.anchoredPosition - circles[count - 2].rectTransform.anchoredPosition
                : step;
            Array.Resize(ref circles, required);
            for (int i = count; i < required; i++)
            {
                var square = UnityEngine.Object.Instantiate(template, template.transform.parent);
                square.name = "SteamPlayer_P" + (i + 2);
                ((RectTransform)square.transform).anchoredPosition = templateRect.anchoredPosition + step * (i - count + 1);
                square.image.material = new Material(template.image.material);
                int index = i;
                square.button.onClick = new Button.ButtonClickedEvent();
                square.button.onClick.AddListener(() => __instance.ClickKickPlayer(index));
                __instance.squares.Add(square);
                circles[i] = UnityEngine.Object.Instantiate(circleTemplate, circleTemplate.transform.parent);
                circles[i].rectTransform.anchoredPosition = circleTemplate.rectTransform.anchoredPosition + circleStep * (i - count + 1);
                circles[i].enabled = false;
                square.gameObject.SetActive(false);
            }
            __instance.lookingForPlayersCircles = circles;
            var colors = new int[required];
            for (int i = 0; i < colors.Length; i++) colors[i] = -1;
            __instance.currentSquareColors = colors;
            Main.Log.LogInfo($"Steam avatar UI ready: 1 local + {__instance.squares.Count} remote slots.");
        }
    }
}
