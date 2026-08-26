# Drop And Grab Overhaul

A Lethal Company mod that allows players to quickly drop multiple items from their inventory with a configurable action.

> **Status:** Early development

## Planned Features

* Drop all eligible items currently held in the player's hotbar
* Configurable item exclusions
* Support for custom hotbars and additional inventory slots
* Optional double-tap activation using Lethal Company's default drop key
* Compatibility with HotbarPlus
* Compatibility with ReservedItemSlot
* Optional integration with ship inventory chute mods
* Configurable behavior for reserved items and non-scrap items

## Planned Activation Modes

### Dedicated Key

Press a configurable key to drop all eligible items.

### Double-Tap Drop

Use Lethal Company's normal drop key twice within a configurable time window.

For example:

```text
G       → Normal item drop
G + G   → Drop all eligible items
```

The goal is to preserve the game's normal drop behavior while providing a quick way to empty the player's inventory.

## Configuration

Configuration options will be added as development progresses.

Planned options include:

```text
Activation mode
Activation key
Double-tap window
Drop scrap only
Excluded items
Drop reserved items
Drop tools
Item destination
```

## Compatibility

DropAndGrabOverhaul is being designed with compatibility in mind.

The inventory system will use an abstraction layer so that the core drop logic does not depend directly on a specific hotbar or inventory implementation.

Planned integrations include:

* HotbarPlus
* ReservedItemSlot
* Ship inventory chute mods

Optional integrations will not be required for the base mod to function.

## Development

This project is built using:

* C#
* .NET
* BepInEx
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

As development progresses, the source will be organized into separate systems for:

* Configuration
* Input handling
* Inventory access
* Item filtering
* Mod compatibility

## Development Roadmap

* [x] Create BepInEx plugin project
* [x] Configure .NET build environment
* [x] Set up GitHub repository
* [ ] Verify plugin loads in Lethal Company
* [ ] Identify vanilla inventory/drop APIs
* [ ] Implement basic drop-all functionality
* [ ] Add scrap filtering
* [ ] Add item exclusions
* [ ] Add configurable activation modes
* [ ] Add double-tap drop
* [ ] Add HotbarPlus compatibility
* [ ] Add ReservedItemSlot compatibility
* [ ] Add ship chute integration
* [ ] Package for Thunderstore
* [ ] Publish initial release

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
