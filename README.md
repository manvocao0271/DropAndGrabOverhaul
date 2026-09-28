# Drop And Grab Overhaul

A Lethal Company mod that overhauls item dropping, grabbing, and selling: quickly drop or sell your whole inventory, and optionally remove the vanilla item-grab cooldown.

## Features

* **Double-tap drop** — tap the drop key (G by default, or whatever you've rebound it to in the vanilla keybinds menu) to drop the held item immediately; tap again within the window to also drop the rest of your eligible items, skipping blacklisted ones
* **Force drop** — hold the drop key to drop everything, ignoring the blacklist
* **Drop blacklist** — configurable list of items that are never dropped by double-tap/force-drop
* **Auto-sell at the company desk** — press the drop key once while looking at the counter to automatically place all sellable items on it, one at a time
* **Sell blacklist** — configurable list of items that auto-sell should skip, separate from the drop blacklist
* **Configurable grab delay** — lower the delay between picking up items (down to 0.01s) so you can grab in rapid succession

In all cases, the player's originally selected hotbar slot is restored afterward, even if it ends up empty.

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

Hold the drop key for a configurable duration to drop everything, including blacklisted items.

### Auto-Sell

While looking at the company desk's counter, press the drop key once to put all eligible scrap items from your inventory onto the counter.

## Configuration

Config is generated on first run under BepInEx's `config/` folder. Sections and keys:

```text
[Input]
DoubleTapWindow          Time window in seconds to detect a double-tap (default: 0.2)
ForceDropHoldDuration    Hold duration in seconds to force-drop everything (default: 0.5)

[Items]
BlacklistedItems         Comma-separated item names never dropped by double-tap/force-drop

[Grab]
GrabDelay                Delay in seconds between grabbing items, vanilla is 0.2 (default: 0.01)

[Sell]
AutoSellInventory        Enables pressing the drop key once at the counter to auto-sell (default: true)
SellBlacklistedItems     Comma-separated item names that auto-sell should skip
```

## Compatibility

DropAndGrabOverhaul is being designed with compatibility in mind.

The inventory system uses an abstraction layer ([Inventory/InventoryAccessor.cs](src/DropAndGrabOverhaul/Inventory/InventoryAccessor.cs)) so that the core drop logic does not depend directly on a specific hotbar or inventory implementation.

Planned integrations include:

* HotbarPlus
* ReservedItemSlot

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

## Project Structure

```text
DropAndGrabOverhaul/
├── src/
│   └── DropAndGrabOverhaul/
│       ├── Configuration/
│       │   ├── GrabConfiguration.cs
│       │   ├── InputConfiguration.cs
│       │   ├── ItemBlacklist.cs
│       │   └── SellConfiguration.cs
│       ├── Input/
│       │   └── InputHandler.cs
│       ├── Inventory/
│       │   └── InventoryAccessor.cs
│       ├── DropAndGrabOverhaul.csproj
│       ├── Plugin.cs
│       └── thunderstore.toml
├── CHANGELOG.md
├── Directory.Build.props
├── Directory.Build.targets
├── DropAndGrabOverhaul.slnx
├── LICENSE
├── README.md
└── global.json
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