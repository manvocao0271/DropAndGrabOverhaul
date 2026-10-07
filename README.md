# Drop And Grab Overhaul

A Lethal Company mod that overhauls item dropping, grabbing, and selling: quickly drop or sell your whole inventory, and optionally remove the vanilla item-grab cooldown.

## Features

* **Double-tap drop** — tap the drop key (G by default, or whatever you've rebound it to in the vanilla keybinds menu) to drop the held item immediately; tap again within the window to also drop the rest of your eligible items, skipping blacklisted ones
* **Force drop** — hold the drop key to drop everything in your hotbar, ignoring the blacklist
* **Reserved slot drop** *(optional, requires [ReservedItemSlotCore](https://thunderstore.io/c/lethal-company/p/FlipMods/ReservedItemSlotCore/))* — keep holding a little longer to also drop the items in your reserved item slots
* **Drop blacklist** — configurable list of items that are never dropped by double-tap/force-drop
* **Auto-sell at the company desk** — press the drop key once while looking at the counter to automatically place all sellable items from your main hotbar on it, one at a time
* **Sell blacklist** — configurable list of items that auto-sell should skip, separate from the drop blacklist
* **Configurable grab delay** — lower the delay between picking up items (down to 0.01s) so you can grab in rapid succession

After a double-tap or force drop, the player's originally selected hotbar slot is restored afterward, even if it ends up empty (except a reserved slot this emptied - you're returned to your first hotbar slot instead). Auto-sell does not restore it: you stay on the slot of the last item it sold.

## Activation

### Double-Tap Drop

Use Lethal Company's normal drop key twice within a configurable time window. The first tap
drops whatever you're holding immediately (blacklist doesn't apply to it, same as a vanilla
drop); a second tap within the window drops the rest of your eligible items too.

```text
G       → Drop the held item immediately
G + G   → Also drop the rest of your eligible items (blacklist respected)
```

### Force Drop

Hold the drop key for a configurable duration to drop everything in your hotbar, including blacklisted items.

If [ReservedItemSlotCore](https://thunderstore.io/c/lethal-company/p/FlipMods/ReservedItemSlotCore/) is installed, the first hold only drops your main hotbar. Keep holding for a configurable extra duration to drop the items in your reserved item slots too. Double-tap never drops reserved slots.

### Auto-Sell

While looking at the company desk's counter, press the drop key once to put all eligible scrap items from your main hotbar onto the counter. Items in [ReservedItemSlotCore](https://thunderstore.io/c/lethal-company/p/FlipMods/ReservedItemSlotCore/) reserved slots are never sold.

## Configuration

Config is generated on first run under BepInEx's `config/` folder. Sections and keys:

```text
[Input]
DoubleTapWindow          Time window in seconds to detect a double-tap (default: 0.2)
ForceDropHoldDuration    Hold duration in seconds to force-drop everything (default: 0.5)
ReservedSlotsHoldDuration Extra hold time in seconds, after the force drop starts, to also drop reserved item slots - only with ReservedItemSlotCore (default: 0.5)

[Items]
BlacklistedItems         Comma-separated item names never dropped by double-tap/force-drop

[Grab]
GrabDelay                Delay in seconds between grabbing items, vanilla is 0.2 (default: 0.01)

[Sell]
AutoSellInventory        Enables pressing the drop key once at the counter to auto-sell (default: true)
SellBlacklistedItems     Comma-separated item names that auto-sell should skip

[Logging]
LogLevels                Which of this mod's own log levels to show, comma-separated (default: Fatal, Error, Warning)
                          Acceptable values: None, Fatal, Error, Warning, Message, Info, Debug, All
```

Item names in both blacklists must match the in-game item name exactly, including capitalization (`Walkie-talkie`, not `walkie-talkie`). Both lists start from the same default.

## Compatibility

DropAndGrabOverhaul is being designed with compatibility in mind.

The inventory system uses an abstraction layer ([Inventory/InventoryAccessor.cs](src/DropAndGrabOverhaul/Inventory/InventoryAccessor.cs)) so that the core drop logic does not depend directly on a specific hotbar or inventory implementation.

Implemented integrations:

* ReservedItemSlotCore (soft dependency, no reference needed - see [Compatibility/ReservedItemSlotCompat.cs](src/DropAndGrabOverhaul/Compatibility/ReservedItemSlotCompat.cs))

Planned integrations include:

* HotbarPlus

Optional integrations will not be required for the base mod to function.

## Development

This project is built using:

* C#
* .NET
* BepInEx
* HarmonyX
* Unity
* Lethal Company

### Building

Clone the repository and build the project:

```bash
git clone https://github.com/manvocao0271/DropAndGrabOverhaul.git
cd DropAndGrabOverhaul
dotnet build
```

The compiled plugin is generated under:

```text
artifacts/bin/DropAndGrabOverhaul/
```

### Packaging for Thunderstore

`dotnet build -c Release` also builds the Thunderstore package into `artifacts/thunderstore/`. The package contains only the mod: `manifest.json`, `icon.png`, `README.md` (from `THUNDERSTORE.md`), `CHANGELOG.md`, `LICENSE` and `plugins/bobabulkerENTERPRISE.DropAndGrabOverhaul.dll`. The build fails if the zip holds anything else, or is missing any of those. Add `-p:PublishTS=true` to upload it after that check passes (needs `TCLI_AUTH_TOKEN`).

When releasing, set the same version in `DropAndGrabOverhaul.csproj`, `thunderstore.toml` and a new `CHANGELOG.md` entry.

## Project Structure

```text
DropAndGrabOverhaul/
├── .config/
│   └── dotnet-tools.json
├── src/
│   └── DropAndGrabOverhaul/
│       ├── Compatibility/
│       │   └── ReservedItemSlotCompat.cs
│       ├── Configuration/
│       │   ├── GrabConfiguration.cs
│       │   ├── InputConfiguration.cs
│       │   ├── ItemBlacklist.cs
│       │   ├── ItemNameList.cs
│       │   ├── LoggingConfiguration.cs
│       │   └── SellConfiguration.cs
│       ├── Features/
│       │   ├── AutoSellRoutine.cs
│       │   ├── CoroutineGate.cs
│       │   └── DropAllRoutine.cs
│       ├── Inputs/
│       │   ├── DropGestureTracker.cs
│       │   ├── DropGuard.cs
│       │   └── InputHandler.cs
│       ├── Inventory/
│       │   └── InventoryAccessor.cs
│       ├── Patches/
│       │   ├── DiscardPerformedPatch.cs
│       │   ├── GrabCooldownPatch.cs
│       │   ├── GrabObjectDelayPatch.cs
│       │   └── StartOfRoundPatch.cs
│       ├── DropAndGrabOverhaul.csproj
│       ├── ModLog.cs
│       ├── Plugin.cs
│       ├── UpdateRunner.cs
│       └── thunderstore.toml
├── CHANGELOG.md
├── Directory.Build.props
├── Directory.Build.targets
├── DropAndGrabOverhaul.slnx
├── LICENSE
├── README.md
├── THUNDERSTORE.md
├── global.json
└── icon.png
```

## Development Roadmap

* [x] Create BepInEx plugin project
* [x] Configure .NET build environment
* [x] Set up GitHub repository
* [x] Verify plugin loads in Lethal Company
* [x] Implement double-tap drop-all with blacklist support
* [x] Implement force-drop (hold key, ignores blacklist)
* [x] Implement auto-sell at the company desk with its own blacklist
* [x] Implement optional grab cooldown removal
* [x] Add ReservedItemSlotCore support (hold longer to drop reserved slots)
* [x] Package for Thunderstore
* [x] Publish initial release

## Contributing

Issues, suggestions, and pull requests are welcome.

When reporting a compatibility issue, please include:

* Lethal Company version
* DropAndGrabOverhaul version
* BepInEx version
* Other inventory/hotbar mods installed
* BepInEx log output relevant to the issue

## License

See [LICENSE](LICENSE) for the project's license.