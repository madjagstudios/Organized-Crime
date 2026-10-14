[![Polyfill](https://polyfill.doomods.com/badge/organized-crime.svg)](https://polyfill.doomods.com/mod/organized-crime)
# Organized Crime

Organized Crime is a story mod for Schedule I. Version 1.0 brings a crime family with a contact who offers you six jobs, an enforcer who calls when you fail, a hidden headquarters you earn, and a police chief who will sell you your own file. 2.0 adds Chapter 3's repeatable jobs, the Syndicate Warehouse, and police checkpoints, patrols, sweeps, and raids. Built on MelonLoader and S1API, using the game's own phone, quests, storage, and curfew.

## Requirements

- MelonLoader 0.7.3 or newer
- S1API Forked 3.2.1 or newer, by ifBars
- Schedule I 0.4.7, single player

## Install

Install through Thunderstore using r2modman (or an equivalent mod manager), or build the mod yourself and drop `OrganizedCrime.dll` into your Schedule I `Mods` folder.

## Building from source

You need the dotnet 6 SDK.

The project file reads your Schedule I install folder from the `ScheduleOnePath` MSBuild property. It defaults to:

```
C:\Program Files (x86)\Steam\steamapps\common\Schedule I
```

If your game is installed somewhere else, pass the path explicitly:

```
dotnet build tools/OrganizedCrime/OrganizedCrime.csproj -c Release -p:ScheduleOnePath="<your game folder>"
```

Run the tests with:

```
dotnet test tests/OrganizedCrime.Tests -c Release
```

## Packaging

To build the distributable package (Thunderstore and Nexus share the same zip):

```
powershell -ExecutionPolicy Bypass -File packaging/build-package.ps1
```

## Saves

Organized Crime keeps its own data in an `OrganizedCrime` folder inside your save slot. It never reads or writes the game's own save files.

## Release notes

See [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md) for what is in this release and what is not.

## License

MIT. See [LICENSE](LICENSE).
