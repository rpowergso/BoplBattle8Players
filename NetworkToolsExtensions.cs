using System;
using HarmonyLib;
using UnityEngine;

namespace MorePlayers;

public static class NetworkToolsExtensions
{
    public const int HeaderSize = 6;
    private const int PayloadBaseSize = 11;
    private const byte ProtocolVersion = 1;
    private const byte StartRequestMessage = 1;
    private static readonly byte[] Magic = { (byte)'M', (byte)'B', (byte)'P', (byte)'2' };

    public static bool IsMultiStartRequest(byte[] data)
    {
        if (data == null || data.Length < HeaderSize + PayloadBaseSize)
        {
            return false;
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i])
            {
                return false;
            }
        }

        if (data[4] != ProtocolVersion || data[5] != StartRequestMessage)
        {
            return false;
        }

        int playerCount = data[HeaderSize + 6];
        return playerCount >= 1 && playerCount <= Constants.MAX_PLAYERS &&
               data.Length == HeaderSize + PayloadBaseSize + playerCount * 13;
    }

    public static MultiStartRequestPacket ReadMultiStartRequest(byte[] data, ref byte[] uintConversionHelperArray,
        ref byte[] ulongConversionHelperArray, ref byte[] ushortConversionHelperArray)
    {
	    if (!IsMultiStartRequest(data))
	    {
		    throw new ArgumentException("Invalid MorePlayers start-request packet.", nameof(data));
	    }

	    Main.Log.LogInfo($"Decoding MultiStartRequestPacket, size of array: {data.Length}");
	    
	    MultiStartRequestPacket result = default(MultiStartRequestPacket);
		int num = HeaderSize;
		ushortConversionHelperArray[0] = data[num++];
		ushortConversionHelperArray[1] = data[num++];
		result.seqNum = NetworkTools.SwapBytesIfLittleEndian(BitConverter.ToUInt16(ushortConversionHelperArray, 0));
		
		Main.Log.LogInfo($"Decoded seqNum: {result.seqNum} (byte: {num})");
		
		uintConversionHelperArray[0] = data[num++];
		uintConversionHelperArray[1] = data[num++];
		uintConversionHelperArray[2] = data[num++];
		uintConversionHelperArray[3] = data[num++];
		result.seed = NetworkTools.SwapBytesIfLittleEndian(BitConverter.ToUInt32(uintConversionHelperArray, 0));
		
		Main.Log.LogInfo($"Decoded seed: {result.seed} (byte: {num})");
		
		result.nrOfPlayers = data[num++];
		result.nrOfAbilites = data[num++];
		result.currentLevel = data[num++];
		result.frameBufferSize = data[num++];
		result.isDemoMask = data[num++];
		
		Main.Log.LogInfo($"Decoded nrOfPlayers: {result.nrOfPlayers}, nrOfAbilities: {result.nrOfAbilites}, currentLevel: {result.currentLevel}, frameBufferSize: {result.frameBufferSize}, isDemoMask: {result.isDemoMask} (byte: {num})");
		
		result.Initialize(result.nrOfPlayers);

		for (int i = 0; i < result.nrOfPlayers; i++)
		{
			ulongConversionHelperArray[0] = data[num++];
			ulongConversionHelperArray[1] = data[num++];
			ulongConversionHelperArray[2] = data[num++];
			ulongConversionHelperArray[3] = data[num++];
			ulongConversionHelperArray[4] = data[num++];
			ulongConversionHelperArray[5] = data[num++];
			ulongConversionHelperArray[6] = data[num++];
			ulongConversionHelperArray[7] = data[num++];
			result.p_ids[i] = NetworkTools.SwapBytesIfLittleEndian(BitConverter.ToUInt64(ulongConversionHelperArray, 0));
			Main.Log.LogInfo($"Decoded p_ids[{i}]: {result.p_ids[i]} (byte: {num})");
		}

		for (int i = 0; i < result.nrOfPlayers; i++)
		{
			result.p_colors[i] = data[num++];
			Main.Log.LogInfo($"Decoded p_colors[{i}]: {result.p_colors[i]} (byte: {num})");
		}

		for (int i = 0; i < result.nrOfPlayers; i++)
		{
			result.p_teams[i] = data[num++];
			Main.Log.LogInfo($"Decoded p_teams[{i}]: {result.p_teams[i]} (byte: {num})");
		}

		for (int i = 0; i < result.nrOfPlayers; i++)
		{
			result.p_ability1s[i] = data[num++];
			Main.Log.LogInfo($"Decoded p_ability1s[{i}]: {result.p_ability1s[i]} (byte: {num})");
		}

		for (int i = 0; i < result.nrOfPlayers; i++)
		{
			result.p_ability2s[i] = data[num++];
			Main.Log.LogInfo($"Decoded p_ability2s[{i}]: {result.p_ability2s[i]} (byte: {num})");
		}

		for (int i = 0; i < result.nrOfPlayers; i++)
		{
			result.p_ability3s[i] = data[num++];
			Main.Log.LogInfo($"Decoded p_ability3s[{i}]: {result.p_ability3s[i]} (byte: {num})");
		}
		
		return result;
    }

    public static void EncodeMultiStartRequest(ref byte[] data, MultiStartRequestPacket p)
    {
	    int requiredSize = GetMultiStartRequestSize(p);
	    if (data == null || data.Length != requiredSize)
	    {
		    data = new byte[requiredSize];
	    }

	    Main.Log.LogInfo($"Encoding MultiStartRequestPacket, size of array: {data.Length}");
	    int num = 0;
	    for (int i = 0; i < Magic.Length; i++)
	    {
		    data[num++] = Magic[i];
	    }
	    data[num++] = ProtocolVersion;
	    data[num++] = StartRequestMessage;
	
	    Main.Log.LogInfo($"Encoding seqNum: {p.seqNum} (byte: {num})");
	    p.seqNum = NetworkTools.SwapBytesIfLittleEndian(p.seqNum);
		byte[] bytes = BitConverter.GetBytes(p.seqNum);
		data[num++] = bytes[0];
		data[num++] = bytes[1];
		
		Main.Log.LogInfo($"Encoding seed: {p.seed} (byte: {num})");
		p.seed = NetworkTools.SwapBytesIfLittleEndian(p.seed);
		bytes = BitConverter.GetBytes(p.seed);
		data[num++] = bytes[0];
		data[num++] = bytes[1];
		data[num++] = bytes[2];
		data[num++] = bytes[3];
		
		data[num++] = p.nrOfPlayers;
		data[num++] = p.nrOfAbilites;
		data[num++] = p.currentLevel;
		data[num++] = p.frameBufferSize;
		data[num++] = p.isDemoMask;
		
		Main.Log.LogInfo($"Encoded nrOfPlayers: {p.nrOfPlayers}, nrOfAbilities: {p.nrOfAbilites}, currentLevel: {p.currentLevel}, frameBufferSize: {p.frameBufferSize}, isDemoMask: {p.isDemoMask} (byte: {num})");

		for (int i = 0; i < p.nrOfPlayers; i++)
		{
			Main.Log.LogInfo($"Encoding p_ids[{i}]: {p.p_ids[i]} (byte: {num})");
			var pId = NetworkTools.SwapBytesIfLittleEndian(p.p_ids[i]);
			bytes = BitConverter.GetBytes(pId);
			data[num++] = bytes[0];
			data[num++] = bytes[1];
			data[num++] = bytes[2];
			data[num++] = bytes[3];
			data[num++] = bytes[4];
			data[num++] = bytes[5];
			data[num++] = bytes[6];
			data[num++] = bytes[7];
			
		}
		
		for (int i = 0; i < p.nrOfPlayers; i++)
		{
			data[num++] = p.p_colors[i];
			Main.Log.LogInfo($"Encoded p_colors[{i}]: {p.p_colors[i]} (byte: {num})");
		}
		
		
		for (int i = 0; i < p.nrOfPlayers; i++)
		{
			data[num++] = p.p_teams[i];
			Main.Log.LogInfo($"Encoded p_teams[{i}]: {p.p_teams[i]} (byte: {num})");
		}
		
		for (int i = 0; i < p.nrOfPlayers; i++)
		{
			data[num++] = p.p_ability1s[i];
			Main.Log.LogInfo($"Encoded p_ability1s[{i}]: {p.p_ability1s[i]} (byte: {num})");
		}
		
		for (int i = 0; i < p.nrOfPlayers; i++)
		{
			data[num++] = p.p_ability2s[i];
			Main.Log.LogInfo($"Encoded p_ability2s[{i}]: {p.p_ability2s[i]} (byte: {num})");
		}
		
		for (int i = 0; i < p.nrOfPlayers; i++)
		{
			data[num++] = p.p_ability3s[i];
			Main.Log.LogInfo($"Encoded p_ability3s[{i}]: {p.p_ability3s[i]} (byte: {num})");
		}
    }

    public static int GetMultiStartRequestSize(MultiStartRequestPacket startParameters)
    {
	    return HeaderSize + PayloadBaseSize + startParameters.nrOfPlayers * 13;
    }
}

[HarmonyPatch(typeof(NetworkTools))]
[HarmonyPatch("ReadLobbyReadyPacket")]
public static class NetworkTools_ReadLobbyReadyPacket
{
	static void Postfix(ref LobbyReadyPacket __result)
	{
		Debug.Log("ReadLobbyReadyPacket");
		Debug.Log(__result.steamId);
		Debug.Log(__result.ability1);
		Debug.Log(__result.ability2);
		Debug.Log(__result.ability3);
		Debug.Log(__result.color);
		Debug.Log(__result.team);
		Debug.Log(__result.ownsFullGame);
		Debug.Log(__result.usesKeyboardAndMouse);
		
		Debug.Log("Connected Players");
		foreach (var player in SteamManager.instance.connectedPlayers)
		{
			Debug.Log(player.id);
		}
	}
}
