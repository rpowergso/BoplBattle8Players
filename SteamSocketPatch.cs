using System;
using System.Collections.Generic;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;

namespace MorePlayers;

[HarmonyPatch(typeof(SteamSocket), "OnMessage")]
public static class SteamSocketPatch_OnMessage
{
    private static byte[] UlongConversionArray = new byte[8];
    private static byte[] UintConversionArray = new byte[4];
    private static byte[] UshortConversionArray = new byte[2];

    static bool Prefix(NetIdentity identity, IntPtr data, int size)
    {
        if (data == IntPtr.Zero || size <= 0 || size > 2048)
        {
            return false;
        }

        byte[] messageBuffer = new byte[size];
        System.Runtime.InteropServices.Marshal.Copy(data, messageBuffer, 0, size);

        var steamManager = SteamManager.instance;
        var sender = identity.SteamId;
        var lobbyId = steamManager == null ? default : steamManager.currentLobby.Id;
        if (!sender.IsValid || !lobbyId.IsValid || !new Friend(sender).IsIn(lobbyId))
        {
            return false;
        }

        if (LobbyMotion.TryHandlePacket(steamManager, sender, messageBuffer))
        {
            return false;
        }

        var lockstepHost = SteamManager.networkClientHandle;
        if (!GameSession.inMenus && lockstepHost != null && lockstepHost.hasBeenInitialized &&
            IsGameplayPacketSize(size))
        {
            // The game routes these packets by their first byte. Match it to the
            // authenticated transport sender before passing the packet through.
            if (MatchesGameplaySender(lockstepHost.clients, sender, messageBuffer[0]))
            {
                lockstepHost.ReadPacket(messageBuffer, size);
            }
            return false;
        }

        // Leave every vanilla or third-party packet alone unless it carries our
        // explicit magic, protocol version, message type, and exact length.
        if (!NetworkToolsExtensions.IsMultiStartRequest(messageBuffer))
        {
            return true;
        }

        if (!steamManager.currentLobby.IsOwnedBy(sender))
        {
            Main.Log.LogWarning("Ignored MorePlayers start packet from a non-host lobby member.");
            return false;
        }

        try
        {
            SteamManagerExtended.startParameters = NetworkToolsExtensions.ReadMultiStartRequest(
                messageBuffer, ref UintConversionArray, ref UlongConversionArray, ref UshortConversionArray);

            if (GameSession.inMenus)
            {
                CharacterSelectHandler_online.ForceStartGame();
            }
            else
            {
                SteamManager.ForceLoadNextLevel();
            }
        }
        catch (Exception exception)
        {
            Main.Log.LogError($"Rejected invalid MorePlayers start packet: {exception}");
        }

        return false;
    }

    private static bool IsGameplayPacketSize(int size)
    {
        // The six-byte ACKs for player slots five and six share the vanilla
        // lobby ping markers. The other sizes are checksums and input updates.
        return size == 6 || size == 14 || size == 67 || size == 83;
    }

    internal static bool MatchesGameplaySender(List<Client> clients, SteamId sender, byte playerId)
    {
        return playerId > 0 && playerId <= Constants.MAX_PLAYERS && clients != null &&
            clients.Exists(client => client.Id == sender && client.PlayerId == playerId);
    }
}
