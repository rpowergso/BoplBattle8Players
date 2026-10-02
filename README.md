# Bopl 8 Players

An experimental BepInEx mod that expands Bopl Battle online Steam lobbies from four to eight players.

This repository updates the original [MoreBoplPlayers](https://github.com/AbstractMelon/MoreBoplPlayers) code for Bopl Battle 2.5.1. The original work is credited to AbstractMelon, suppergerrie2, and the Bopl Battle modding community. The project remains under the BSD 3-Clause license in `LICENSE`.

## Status

Version 0.2.0 builds against and starts cleanly on Bopl Battle 2.5.1 with BepInEx 5.4.23.5. The Harmony patches all apply during startup.

It is **alpha software**, not a bug-free release yet. A real five-to-eight-player Steam session is still required to validate full rounds, score synchronization, reconnects, ability selection, and transitions between levels. Every player must use the same build.

Improvements over the deprecated build include:

- compatibility with the 2.5.1 game assemblies;
- a versioned custom-packet header and exact packet validation;
- a targeted fix for the player 5/6 gameplay-packet collision with vanilla lobby ping packets;
- a corrected plugin namespace and version display;
- a bounded four-to-eight-player configuration;
- removal of references to DLLs no longer shipped with the game.

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

## Known limitations

- Only four vanilla teams are currently available.
- Replay recording is disabled because vanilla replay packets only support four players.
- Full multiplayer behavior cannot be proven by a one-computer startup test.
- Compatibility with other networking or player-count mods is not expected.
