using HarmonyLib;
using Steamworks.Data;

namespace MorePlayers
{
    internal static class SteamLobbyJoinPatch
    {
        private static ulong joiningLobbyId;
        private static Lobby? earlyLobbyEntered;

        [HarmonyPatch(typeof(SteamManager), "JoinLobby")]
        private static class JoinLobby
        {
            private static bool Prefix(SteamManager __instance, Lobby lobby)
            {
                ulong lobbyId = lobby.Id;
                if (lobbyId == 0 || lobbyId == (ulong)__instance.currentLobby.Id || SteamManager.isJoiningAlobbyAtm)
                    return false;

                joiningLobbyId = lobbyId;
                earlyLobbyEntered = null;
                return true;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "OnLobbyEnteredCallback")]
        private static class LobbyEntered
        {
            private static bool Prefix(SteamManager __instance, Lobby lobby)
            {
                ulong lobbyId = lobby.Id;
                if (joiningLobbyId == 0 || lobbyId != joiningLobbyId)
                    return true;

                if (lobbyId == (ulong)__instance.currentLobby.Id)
                {
                    joiningLobbyId = 0;
                    earlyLobbyEntered = null;
                    return true;
                }

                // Steam can deliver this callback before JoinLobby's async continuation
                // sets currentLobby. The game's callback would discard it in that case.
                earlyLobbyEntered = lobby;
                return false;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "Update")]
        private static class FinishJoin
        {
            private static void Postfix(SteamManager __instance)
            {
                if (joiningLobbyId == 0 || SteamManager.isJoiningAlobbyAtm)
                    return;

                var lobby = earlyLobbyEntered;
                bool joined = joiningLobbyId == (ulong)__instance.currentLobby.Id;
                joiningLobbyId = 0;
                earlyLobbyEntered = null;

                if (joined && lobby.HasValue)
                {
                    Main.Log.LogInfo("Processing Steam lobby entry after the join completed.");
                    __instance.OnLobbyEnteredCallback(lobby.Value);
                }
            }
        }
    }
}
