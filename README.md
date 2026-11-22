# Bopl 8 Players

Play Bopl Battle with up to 8 players in a private Steam lobby. The host can set the lobby size from 4 to 8.

[Install from Thunderstore](https://thunderstore.io/c/bopl-battle/p/rpowergso/Bopl8Players/) · [GitHub release and download](https://github.com/rpowergso/BoplBattle8Players/releases/tag/v0.3.4-alpha)

[Report a bug](https://github.com/rpowergso/BoplBattle8Players/issues/new?template=bug_report.yml) · [Share feedback or suggest a feature](https://github.com/rpowergso/BoplBattle8Players/issues/new?template=feedback.yml)

## Features

- Invite Steam friends in-game. Hosts can remove players before a match.
- See who’s ready and use the standard loadout and keybind controls. Hosts can turn on **Show Items** to display ability icons on the roster for everyone. Icons are hidden by default.
- Practice movement and jumping on a shared island after you ready up. Supports keyboard and controller.
- Press **F8** to see FPS, peer ping, and connection stats.

## Install

1. Install **Bopl8Players** in a Bopl Battle profile in Thunderstore Mod Manager or r2modman. The manager installs BepInExPack with it.
2. Have everyone install the same mod version and choose **Start modded** once to set up the mod loader.
3. The host opens **Online Play** → **Invite Players**, then selects **Invite** next to a friend or opens **Steam Invite Window**. Friends click the invite in Steam to join while their modded game is running.

For manual installation, download the ZIP from the [GitHub release](https://github.com/rpowergso/BoplBattle8Players/releases/tag/v0.3.4-alpha), then place `Bopl8Players.dll` in the modded profile’s `BepInEx/plugins/Bopl8Players/` folder.

### Accept Steam invites with the game closed

Each friend can set Steam to use their Bopl8Players profile when an invite starts the game.

1. In Thunderstore Mod Manager or r2modman, select the Bopl Battle profile with **Bopl8Players** and start it modded once. Then close Bopl Battle.
2. In the manager’s **Help** menu, copy the arguments under **Launching from Steam** or **Launching the game from outside the mod manager**. If you need to generate them from the profile folder, use [Get-SteamLaunchOptions.ps1](scripts/Get-SteamLaunchOptions.ps1).
3. In Steam, right-click **Bopl Battle** → **Properties** → **General** and paste the arguments into **Launch Options**. The path points to your own modded profile, so each friend uses their own arguments.
4. Click the Steam invite. Steam starts Bopl Battle with the modded profile and passes the invited lobby to the game.

To check your setup before someone invites you, start Bopl Battle from Steam and look for **Invite Players** under **Online Play**. With these launch options set, the Steam **Play** button also uses that profile. Clear the launch options to play vanilla through Steam. If you prefer to leave Steam’s settings alone, use **Start modded** and then accept the invite.

## Compatibility

Requires Bopl Battle 2.5.1 on Windows (Steam) and BepInExPack 5.4.2305. Public matchmaking and replay recording are disabled. The game’s four teams are available. Use a profile without other player-count or networking mods.

This is an alpha. The lobby was tested with Steam connected and simulated peers. Full 5–8 player matches and reconnects still need live testing.

<details>
<summary>Configuration and build</summary>

- Configuration lives in `BepInEx/config/com.rpowergso.bopl8players.cfg`. `General.MaxPlayers` defaults to 8 and accepts 4–8.
- `UI.ShowConnectionStats` controls the F8 overlay. It shows FPS, per-peer ping, in-order packet delivery, and gameplay input delay. `UI.ShowStatsOverlay` enables the optional in-match statistics overlay.
- The **Steam Invite Window** button requires Steam Overlay.
- To build, install the game, BepInExPack, and .NET 8 SDK or newer. Run the commands below from PowerShell. For a custom game path, use `-GameDirectory` with the script and `-p:BoplBattleDir=...` with the build.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Prepare-GameAssembly.ps1
dotnet build .\MorePlayers.csproj -c Release
```

The DLL is written to `bin/Release/net46/Bopl8Players.dll`. Run `scripts/Test-OnlineLobbyUi.ps1` with the game closed to build and run the isolated UI check. Game and BepInEx logs may contain Steam IDs.

</details>

Based on [MoreBoplPlayers](https://github.com/AbstractMelon/MoreBoplPlayers) by AbstractMelon, suppergerrie2, and contributors. [BSD 3-Clause license](LICENSE).
