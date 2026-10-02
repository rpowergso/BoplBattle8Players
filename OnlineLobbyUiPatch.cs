using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    [HarmonyPatch(typeof(CharacterSelectHandler_online), "Awake")]
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
            float center = localRect.anchoredPosition.x + originalStep * originalCount / 2f;
            float scale = (originalCount + 1f) / Constants.MAX_PLAYERS;
            float step = originalStep * scale;

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

            Position(localRect, center - step * remoteCount / 2f, scale);
            var emptySlots = new GameObject[remoteCount];
            for (int i = 0; i < remoteCount; i++)
            {
                float x = center + step * (i + 1 - remoteCount / 2f);
                Position((RectTransform)boxes[i].transform, x, scale);
                Position(circles[i].rectTransform, x, scale);

                var empty = new GameObject("InviteSlot_P" + (i + 2), typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)empty.transform;
                rect.SetParent(template.transform.parent, false);
                var boxRect = (RectTransform)boxes[i].transform;
                rect.anchorMin = boxRect.anchorMin;
                rect.anchorMax = boxRect.anchorMax;
                rect.pivot = boxRect.pivot;
                rect.sizeDelta = boxRect.sizeDelta;
                rect.anchoredPosition = new Vector2(x, circles[i].rectTransform.anchoredPosition.y);
                rect.localScale = boxRect.localScale;
                var border = empty.GetComponent<Image>();
                border.sprite = template.borderImages[0].sprite;
                border.type = template.borderImages[0].type;
                border.color = __instance.disabledBlue;
                border.raycastTarget = false;

                var text = UnityEngine.Object.Instantiate(template.playerNameText, rect);
                text.name = "InviteLabel";
                text.text = "Invite friend\n" + (i + 2) + " / " + Constants.MAX_PLAYERS;
                text.color = __instance.disabledWhite;
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                text.rectTransform.anchoredPosition = Vector2.zero;
                text.rectTransform.sizeDelta = rect.sizeDelta * 0.8f;
                emptySlots[i] = empty;
            }
            var view = __instance.gameObject.AddComponent<OnlineLobbyEmptySlots>();
            view.Boxes = boxes;
            view.EmptySlots = emptySlots;
            Main.Log.LogInfo($"Online lobby UI ready: 1 local + {boxes.Length} remote slots; {circles.Length} loading indicators.");
        }

        static void Position(RectTransform rect, float x, float scale)
        {
            // Preserve Y: AnimateInOutUI owns its cached animation heights.
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            rect.localScale *= scale;
        }
    }

    internal sealed class OnlineLobbyEmptySlots : MonoBehaviour
    {
        internal CSBox_online[] Boxes;
        internal GameObject[] EmptySlots;
        void LateUpdate()
        {
            for (int i = 0; i < Boxes.Length; i++)
                EmptySlots[i].SetActive(!Boxes[i].isVisible);
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
