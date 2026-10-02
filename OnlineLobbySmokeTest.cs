#if BOPL8_UI_SMOKE
using System;
using System.Collections;
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
            for (int i = 0; i < expected; i++)
            {
                var box = handler.networkPlayerBoxes[i];
                if (box == null || box.playerNameText == null || box.readyText == null || box.pingText == null)
                    throw new Exception("Incomplete remote slot");
                var animation = box.GetComponent<AnimateInOutUI>();
                if (animation.originalHeights[0] != handler.networkPlayerBoxes[0].GetComponent<AnimateInOutUI>().originalHeights[0])
                    throw new Exception("Remote slot animation target was not preserved");
                Main.Log.LogInfo($"[UI smoke] slot={i + 2} x={((RectTransform)box.transform).anchoredPosition.x} scale={box.transform.localScale.x} animationTarget={animation.originalHeights[0]}");
            }
            Main.Log.LogInfo($"[UI smoke] PASS: {expected + 1} lobby slots, {expected} loading circles, {frame.squares.Count + 1} avatars; hasMods={CharacterSelectHandler_online.hasMods}; Steam lobby capacity={SteamManager.instance.currentLobby.MaxMembers}");
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
