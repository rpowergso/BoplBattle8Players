# Bopl 8 Players

An experimental BepInEx mod that expands Bopl Battle online Steam lobbies from four to eight players.

This repository updates the original [MoreBoplPlayers](https://github.com/AbstractMelon/MoreBoplPlayers) code for Bopl Battle 2.5.1. The original work is credited to AbstractMelon, suppergerrie2, and the Bopl Battle modding community. The project remains under the BSD 3-Clause license in `LICENSE`.

## Status

Version 0.2.1 builds against Bopl Battle 2.5.1 with BepInEx 5.4.23.5. An automated in-game check validates eight online lobby slots, seven remote loading indicators, eight Steam avatar slots, an eight-member Steam lobby, and returning to the online selection screen.

The 0.2.1 fix also keeps the static Harmony patches installed when Unity destroys the bootstrap plugin object. Version 0.2.0 could log a successful startup and then remove its own patches before the main menu appeared.

It is **alpha software**, not a bug-free release yet. A real five-to-eight-player Steam session is still required to validate full rounds, score synchronization, reconnects, ability selection, and transitions between levels. Every player must use the same build.

Improvements over the deprecated build include:

- compatibility with the 2.5.1 game assemblies;
- a versioned custom-packet header and exact packet validation;
- a targeted fix for the player 5/6 gameplay-packet collision with vanilla lobby ping packets;
- a corrected plugin namespace and version display;
- a bounded four-to-eight-player configuration;
- removal of references to DLLs no longer shipped with the game.
- seven remote-player panels and invite placeholders alongside the local selector;
- expanded Steam avatar, loading-indicator and kick-button arrays;
- preservation of cloned panels' animation targets and patches across scene changes.

## Build

Requirements:

- Bopl Battle installed through Steam;
- a Thunderstore/r2modman BepInEx profile;
- .NET 8 SDK or newer.

From PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Prepare-GameAssembly.ps1
dotnet build .\MorePlayers.csproj -c Release
```

If Bopl Battle is installed somewhere else, pass `-GameDirectory` to the preparation script and `-p:BoplBattleDir=...` to `dotnet build`.

The compiled plugin is written to:

```text
bin/Release/net46/Bopl8Players.dll
```

`Assembly-CSharp.publicized.dll` is generated locally and deliberately excluded from Git because it belongs to the game.

## Install for testing

Create a clean Bopl Battle profile in Thunderstore Mod Manager or r2modman, install BepInExPack, and place `Bopl8Players.dll` in that profile's `BepInEx/plugins/Bopl8Players` directory. All participants must install the exact same DLL and launch the game modded.

The generated configuration file is:

```text
BepInEx/config/com.rpowergso.bopl8players.cfg
```

`MaxPlayers` defaults to 8 and accepts values from 4 through 8.

For the local development installation on this computer, select the **Bopl8Dev** profile and use **Start modded**. The **Default** profile contains FixedMoreBopl, a different mod. Do not install both player-count mods in the same profile.

In Online Play, the eight-player layout has your local selection panel and seven smaller remote/invite panels. Invite friends through Steam; everyone must install this same version. Public vanilla matchmaking remains disabled because the network protocol requires the mod on every computer.

### Automated UI check

Build with `-p:DefineConstants=BOPL8_UI_SMOKE` to include a test harness. In an isolated BepInEx profile, it waits for Steam initialization, enters the real online selection scene, validates the UI and lobby capacity, leaves and re-enters the scene, logs `[UI smoke] PASS`, and exits. These checks do not simulate actual remote clients or prove gameplay synchronization. Rebuild normally before distributing the DLL; normal builds exclude the harness.

## Known limitations

- Only four vanilla teams are currently available.
- Replay recording is disabled because vanilla replay packets only support four players.
- Full multiplayer behavior cannot be proven by a one-computer startup test.
- Compatibility with other networking or player-count mods is not expected.
