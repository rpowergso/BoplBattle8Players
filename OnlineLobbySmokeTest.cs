#if BOPL8_UI_SMOKE
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using UnityEngine.InputSystem;

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
            yield return new WaitForSecondsRealtime(2);
            while (SteamManager.instance == null || (ulong)SteamManager.instance.currentLobby.Id == 0)
                yield return null;
            // The game's menus assume these devices exist, even in batch mode.
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
            SceneManager.LoadScene("ChSelect_online");
            yield return new WaitForSecondsRealtime(2);
            var handler = UnityEngine.Object.FindObjectOfType<CharacterSelectHandler_online>();
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
                steamButton.onClick.Invoke();
                inviteMenu.Close();
            }
            finally { OnlineInviteFriends.OpenOverlay = originalOverlay; }
            if ((ulong)invitedLobby != (ulong)SteamManager.instance.currentLobby.Id || handler.findPlayersText.text != "INVITE PLAYERS")
                throw new Exception("Native invite button did not target the current lobby");
            Main.Log.LogInfo("[UI smoke] PASS: in-game invite menu opens; Steam window targets current lobby (overlay mocked)");
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
            var fakePlayers = new UiTestConnection[expected];
            for (int i = 0; i < expected; i++)
            {
                fakePlayers[i] = new UiTestConnection(i);
                handler.networkPlayerBoxes[i].Init(fakePlayers[i]);
            }
            handler.characterSelectBox.OnEnterSelect();
            yield return new WaitForSecondsRealtime(2);
            for (int i = 0; i < expected; i++)
            {
                var box = handler.networkPlayerBoxes[i];
                var rect = (RectTransform)box.transform;
                var animation = box.GetComponent<AnimateInOutUI>();
                if (Mathf.Abs(rect.anchoredPosition.y - animation.originalHeights[0]) > 2 || box.GetComponent<CanvasGroup>().alpha < 0.99f)
                    throw new Exception("Occupied remote card failed to animate into its viewport");
                fakePlayers[i].lobby_isReady = true;
            }
            yield return new WaitForSecondsRealtime(1);
            Main.Log.LogInfo("[UI smoke] PASS: seven synthetic display cards animate in and switch ready state; local selector remains full size");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ui-0.2.2.png"));
            yield return new WaitForSecondsRealtime(1);
            for (int i = 0; i < expected; i++) fakePlayers[i].Disconnect();
            yield return new WaitForSecondsRealtime(1);
            for (int i = 0; i < expected; i++)
                if (handler.networkPlayerBoxes[i].GetComponent<CanvasGroup>().alpha > 0.01f)
                    throw new Exception("Disconnected display did not hide");
            Main.Log.LogInfo("[UI smoke] PASS: disconnected cards hide cleanly");
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
    }
    internal sealed class UiTestConnection : SteamConnection
    {
        internal UiTestConnection(int index)
        {
            Connected = true;
            id = (ulong)(index + 1);
            steamName = "Test player " + (index + 2);
            lobby_color = 0;
            lobby_team = (byte)(index % 4);
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
