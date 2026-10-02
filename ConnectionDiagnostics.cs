using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MorePlayers;

// Use the game's existing Steam wrapper/ABI rather than adding native imports.
internal static class SteamConnectionStats
{
    private static readonly PropertyInfo Api = AccessTools.Property(typeof(SteamNetworkingSockets), "Internal");
    private static readonly Type StatusType = typeof(Connection).Assembly.GetType("Steamworks.Data.SteamNetworkingQuickConnectionStatus");
    private static readonly MethodInfo Query = Api == null ? null : AccessTools.Method(Api.PropertyType, "GetQuickConnectionStatus");
    private static bool warned;

    internal static object Read(Connection connection)
    {
        try
        {
            if (Query == null || StatusType == null) return null;
            object api = Api.GetValue(null, null);
            if (api == null) return null;
            object[] args = { connection, Activator.CreateInstance(StatusType) };
            return (bool)Query.Invoke(api, args) ? args[1] : null;
        }
        catch (Exception ex)
        {
            if (!warned) Main.Log.LogWarning("Steam connection telemetry unavailable: " + ex.GetType().Name);
            warned = true;
            return null;
        }
    }

    internal static T Field<T>(object status, string name) =>
        (T)AccessTools.Field(StatusType, name).GetValue(status);

    // Steam quality includes out-of-order delivery; do not call this exact packet loss.
    internal static string Delivery(float quality) =>
        float.IsNaN(quality) || float.IsInfinity(quality) || quality < 0 || quality > 1
            ? "n/a" : (quality * 100).ToString("0.0") + "%";
}

[HarmonyPatch(typeof(MainMenu), "Start")]
internal static class ConnectionDiagnosticsStartup
{
    internal static void Postfix() => ConnectionDiagnostics.EnsureCreated();
}

internal sealed class ConnectionDiagnostics : MonoBehaviour
{
    internal static ConfigEntry<bool> Enabled;
    private static ConnectionDiagnostics instance;
    private readonly List<string> rows = new();
    private float nextSample;
    private float frameTime;
    private string header;
    private GUIStyle style;

    internal static void EnsureCreated()
    {
        if (instance != null) return;
        var root = new GameObject("Bopl8 Connection Diagnostics");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<ConnectionDiagnostics>();
    }

    private void Update()
    {
        frameTime = Mathf.Lerp(frameTime, Time.unscaledDeltaTime, 0.05f);
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            Enabled.Value = !Enabled.Value;
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + 0.5f;
        rows.Clear();
        var manager = SteamManager.instance;
        if (manager == null || !GameLobby.isOnlineGame) return;
        var host = manager.networkClient;
        bool inRound = !GameSession.inMenus && host != null && host.hasBeenInitialized;
        header = $"CONNECTIONS (F8 hide)   Your FPS: {(frameTime > 0 ? 1 / frameTime : 0):0}";
        foreach (var peer in manager.connectedPlayers)
        {
            var status = peer.Connected ? SteamConnectionStats.Read(peer.Connection) : null;
            string ping = status == null ? "n/a" : SteamConnectionStats.Field<int>(status, "Ping") < 0
                ? "n/a" : SteamConnectionStats.Field<int>(status, "Ping") + " ms";
            string delivery = status == null ? "n/a / n/a" :
                SteamConnectionStats.Delivery(SteamConnectionStats.Field<float>(status, "ConnectionQualityLocal")) + " / " +
                SteamConnectionStats.Delivery(SteamConnectionStats.Field<float>(status, "ConnectionQualityRemote"));
            string input = "";
            if (inRound && host.clients != null)
            {
                var client = host.clients.Find(c => c.steamConnection == peer);
                if (client != null)
                {
                    float age = Mathf.Max(0, Time.unscaledTime - client.inputReceivedTimeStamp);
                    bool waiting = host.allClientsSynced && HostPatch.InputBuffer.Count == 0 && client.inputHistory.Count == 0;
                    input = $"   inputs {age * 1000:0} ms ago" + (waiting ? " [WAITING]" : "");
                }
            }
            string name = (peer.steamName ?? "Unknown").Replace('\n', ' ').Replace('\r', ' ');
            if (name.Length > 24) name = name.Substring(0, 24);
            rows.Add($"{name}: {ping}   delivery {delivery}{input}");
        }
    }

    private void OnGUI()
    {
        if (Enabled == null || !Enabled.Value || !GameLobby.isOnlineGame || rows.Count == 0) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };
        float width = Mathf.Min(780, Screen.width - 24);
        var rect = new Rect(Screen.width - width - 12, 12, width, 62 + rows.Count * 23);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 10, rect.y + 6, width - 20, 23), header, style);
        for (int i = 0; i < rows.Count; i++)
            GUI.Label(new Rect(rect.x + 10, rect.y + 29 + i * 23, width - 20, 23), rows[i], style);
        GUI.Label(new Rect(rect.x + 10, rect.y + 31 + rows.Count * 23, width - 20, 25),
            "Delivery = in-order success: received here / reported by peer. Poor link ≠ proof whose internet is at fault.", style);
    }
}
