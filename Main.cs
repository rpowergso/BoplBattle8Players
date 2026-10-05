using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using static Mono.Security.X509.X520;

namespace MorePlayers
{
    [BepInPlugin("com.rpowergso.bopl8players", "Bopl 8 Players", "0.3.2")]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony harmony;
        private bool isVisible = true;
        private ConfigEntry<int> maxPlayers;
        private ConfigEntry<bool> showStatsOverlay;

        private static IEnumerable<CodeInstruction> SteamManagerCreateFriendLobbyPatch(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsConstant(4))
                {
                    Log.LogMessage($"Found create lobby instruction to patch from 4 to {Constants.MAX_PLAYERS}");
                    yield return new CodeInstruction(OpCodes.Ldc_I4, Constants.MAX_PLAYERS);
                    continue;
                }

                yield return instruction;
            }
        }

        public static IEnumerable<CodeInstruction> PatchFieldLoad(FieldInfo fromA, FieldInfo fromB, FieldInfo toA,
            FieldInfo toB, IEnumerable<CodeInstruction> instructions)
        {
            bool patched = false;

            var enumerator = instructions.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var instruction = enumerator.Current;

                if (instruction.LoadsField(fromA, true))
                {
                    if (!enumerator.MoveNext())
                    {
                        Log.LogError(
                            $"Expected to find next instruction after {fromA} load instruction");
                        yield return instruction;
                    }

                    var nextInstruction = enumerator.Current;

                    if (!nextInstruction.LoadsField(fromB))
                    {
                        Log.LogInfo(
                            $"Candidate patch instruction is not loading {fromB} field: {nextInstruction.opcode}:{nextInstruction.operand}");
                        yield return instruction;
                        yield return nextInstruction;
                        continue;
                    }

                    Log.LogInfo($"Found {fromA} and {fromB} field load instructions to patch");

                    yield return new CodeInstruction(instruction.opcode, toA);
                    yield return new CodeInstruction(nextInstruction.opcode, toB);

                    patched = true;
                }
                else
                {
                    yield return instruction;
                }
            }

            if (!patched)
            {
                Log.LogError("Failed to patch GameSessionHandler.LoadNextLevelScene");
            }
        }

        void OnGUI()
        {
            if (showStatsOverlay == null || !showStatsOverlay.Value || GameSession.inMenus || SteamManager.instance == null)
                return;
            var players = PlayerHandler.Get().NumberOfPlayers();
            var playerInfoList = PlayerHandler.Get().PlayerList();

            GUI.color = new UnityEngine.Color(0, 0, 0, 0.7f);

            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = UnityEngine.Color.white;
            style.alignment = TextAnchor.MiddleLeft;
            style.padding = new RectOffset(10, 10, 5, 5);

            if (GUI.Button(new Rect(50, 135 + playerInfoList.Count * 30, 150, 40), "Toggle Visibility"))
            {
                isVisible = !isVisible;
            }

            GUI.color = UnityEngine.Color.white;



            if (isVisible)
            {
                GUI.Box(new Rect(20, 90, 640, 40 + playerInfoList.Count * 30), GUIContent.none);

                GUIStyle headerStyle = new GUIStyle(style);
                headerStyle.fontStyle = FontStyle.Bold;

                GUI.Label(new Rect(25, 95, 300, 30), $"MoreBopl Leaderboard \n{players} Player(s)", headerStyle);



                for (int i = 0; i < playerInfoList.Count; i++)
                {
                    string userColor = playerInfoList[i].Color.ToString().Replace("Slime (UnityEngine.Material)", "");
                    string fixedUserColor = char.ToUpper(userColor[0]) + userColor.Substring(1);
                    string causeOfDeath = playerInfoList[i].CauseOfDeath.ToString();

                    if (causeOfDeath == "NotDeadYet")
                    {
                        causeOfDeath = "Alive";
                    }

                    float yPosition = 130 + i * 30;

                    GUI.Label(new Rect(70, yPosition, 600, 30), $"{fixedUserColor}: Kills: {playerInfoList[i].Kills}, Deaths: {playerInfoList[i].Deaths}, Cause of Death: {causeOfDeath}", style);
                }
            }
        }
        Sprite ConvertTexture2DToSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = col;
            }
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Bopl 8 Players 0.3.2: compact player roster, kit inspector and host kicks");
            Log.LogInfo("Logger Loaded");

            // Configuration
            maxPlayers = Config.Bind("General", "MaxPlayers", 8, "The maximum number of players allowed in a lobby (4-8).");
            showStatsOverlay = Config.Bind("UI", "ShowStatsOverlay", false, "Show the optional statistics overlay during rounds. Hidden in menus.");
            ConnectionDiagnostics.Enabled = Config.Bind("UI", "ShowConnectionStats", true, "Show per-peer Steam ping, in-order delivery quality and input age in Online Play. Toggle with F8.");
            Constants.MAX_PLAYERS = Math.Max(4, Math.Min(8, maxPlayers.Value));


            Host.recordReplay = false; // Disable replay recording since I'm lazy to implement it
            Logger.LogInfo("Disabled replay recording");

            harmony = new Harmony("com.rpowergso.bopl8players");

            harmony.PatchAll();

            var targetMethod =
                typeof(SteamManager).GetMethod("CreateFriendLobby", BindingFlags.Instance | BindingFlags.Public);
            if (targetMethod == null)
            {
                Logger.LogError("Failed to find SteamManager::CreateFriendLobby!");
                return;
            }

            var stateMachineAttr = targetMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
            var moveNextMethod =
                stateMachineAttr.StateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
            var startTranspiler = typeof(Main).GetMethod(nameof(SteamManagerCreateFriendLobbyPatch),
                BindingFlags.Static | BindingFlags.NonPublic);

            var patcher = harmony.CreateProcessor(moveNextMethod);
            patcher.AddTranspiler(startTranspiler);
            patcher.Patch();

            Logger.LogInfo($"More players acquired! Max players: {Constants.MAX_PLAYERS}");
        }

        private void OnDestroy()
        {
            // Unity can destroy the bootstrap plugin object before the first menu
            // loads. BepInEx 5 plugins are process-scoped; keep the static patches
            // installed until the game exits, including across scene transitions.
            Log.LogInfo("Plugin object destroyed; eight-player patches remain installed for this game session.");
        }

        private void Update()
        {
            if (SteamClient.Name == "Noob" || !SteamClient.IsValid)
            {
                Main.Log.LogFatal("PIRATED GAME DETECTED, PLEASE INSTALL THE FULL GAME! MODS WILL NOT WORK WITHOUT IT AND YOU WILL SUPPORT THE DEVELOPER!");
                Application.OpenURL("https://store.steampowered.com/app/1686940/Bopl_Battle/");
                Application.Quit();
            }
        }

    }


    [HarmonyPatch(typeof(printText))]
    [HarmonyPatch("Awake")]
    public static class PatchVersion
    {
        public static void Postfix(printText __instance)
        {
            if (!Constants.version.Contains("Bopl 8 Players"))
            {
                Constants.version = $"{Constants.version} - Bopl 8 Players 0.3.2";
            }
            __instance.GetComponent<TextMeshProUGUI>().text = Constants.version;
        }
    }
}
