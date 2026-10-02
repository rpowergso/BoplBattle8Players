using System;
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
        if (size <= 0 || size > 2048)
        {
            return false;
        }

        byte[] messageBuffer = new byte[size];
        System.Runtime.InteropServices.Marshal.Copy(data, messageBuffer, 0, size);

        // Vanilla identifies six-byte packets by player id. Player ids 5 and 6
        // collide with its lobby ping/ack markers. During gameplay, send all
        // six-byte packets directly to the initialized lockstep host.
        if (size == 6 && SteamManager.networkClientHandle != null &&
            SteamManager.networkClientHandle.hasBeenInitialized)
        {
            SteamManager.networkClientHandle.ReadPacket(messageBuffer, size);
            return false;
        }

        // Leave every vanilla or third-party packet alone unless it carries our
        // explicit magic, protocol version, message type, and exact length.
        if (!NetworkToolsExtensions.IsMultiStartRequest(messageBuffer))
        {
            return true;
        }

        if (!identity.SteamId.IsValid)
        {
            Main.Log.LogWarning("Ignored MorePlayers packet from an invalid Steam id.");
            return false;
        }

        SteamId steamId = identity.SteamId;
        if (!new Friend(steamId).IsIn(SteamManager.instance.currentLobby.Id) ||
            !SteamManager.instance.currentLobby.IsOwnedBy(steamId))
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
}
