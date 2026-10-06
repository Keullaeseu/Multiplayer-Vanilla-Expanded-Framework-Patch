# Multiplayer Vanilla Expanded Framework Patch

A RimWorld Multiplayer compatibility patch for [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013).

This mod is designed to improve multiplayer synchronization when playing with the [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013) mod and RimWorld Multiplayer.

## Features

- Adds multiplayer compatibility for [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013).
- Syncs PipeSystem advanced resource processors (process queues, repeat modes, paste, overclock).
- Syncs VEF abilities, apparel abilities, and Drafted AI toggles.
- Syncs Multi-Verb Combat Framework (MVCF) verbs and gizmos.
- Syncs Outposts (creation, take/give items, gizmos, gear tab).
- Syncs hireable factions dialogs, faction discovery, moving bases, and quest-chain dev tools.
- Syncs furniture, glowers, building graphics selection, door teleporters, and graphic customization.
- Applies RNG and async-time safeguards across VEF subsystems.

## Requirements

- RimWorld
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- RimWorld Multiplayer
  - [GitHub version](https://github.com/rwmt/Multiplayer) or [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2606448745) version
- [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013)

The host and every connected player must use compatible versions of all required mods.

## Installation

### Steam Workshop

Subscribe to the required mods and add them to your RimWorld mod list in the following order:

1. Harmony
2. Core
3. Royalty, Ideology, Biotech, and Anomaly, if applicable
4. RimWorld Multiplayer
5. [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013)
6. [Multiplayer Vanilla Expanded Framework Patch](https://github.com/Keullaeseu/Multiplayer-Vanilla-Expanded-Framework-Patch/releases/latest)

The patch should load after both RimWorld Multiplayer and [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013).

### Manual Installation

1. Download the latest release from the [**Releases**](https://github.com/Keullaeseu/Multiplayer-Vanilla-Expanded-Framework-Patch/releases/latest) section.
2. Extract the mod folder into your RimWorld `Mods` directory.
3. Enable the required mods in RimWorld.
4. Use the recommended load order listed above.
5. Make sure every multiplayer player has the same mod list, configuration, and load order.

## Multiplayer Usage

All players should have the following mods installed and enabled:

- RimWorld Multiplayer
- [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013)
- [Multiplayer Vanilla Expanded Framework Patch](https://github.com/Keullaeseu/Multiplayer-Vanilla-Expanded-Framework-Patch/releases/latest)
- All required Vanilla Expanded Framework dependencies

The host and all connected clients should use the same:

- RimWorld version
- RimWorld Multiplayer version
- Vanilla Expanded Framework version
- Multiplayer Vanilla Expanded Framework Patch version
- Mod configuration
- Mod load order

Do not add, remove, update, or reorder mods while players are connected to the same multiplayer session.

## Compatibility

This patch is intended to provide multiplayer compatibility for [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013).

It does not replace:

- [RimWorld Multiplayer](https://steamcommunity.com/sharedfiles/filedetails/?id=2606448745)
- [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013)

## Known Limitations

- Compatibility may be affected by future RimWorld updates.
- Compatibility may be affected by future updates to RimWorld Multiplayer or Vanilla Expanded Framework.
- Some Vanilla Expanded Framework debug gizmos are synced in debug mode only.

## Credits

- [RimWorld Multiplayer on GitHub](https://github.com/rwmt/Multiplayer)
- [RimWorld Multiplayer on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2606448745)
- [Vanilla Expanded Framework on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013)
- [Multiplayer Vanilla Expanded Framework Patch](https://github.com/Keullaeseu)
