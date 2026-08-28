# Drop And Grab Overhaul

Overhauls item dropping, grabbing, and selling in Lethal Company — drop or sell your whole inventory in one motion, and optionally remove the vanilla grab cooldown.

> **Status:** Early development

## Features

* **Double-tap drop** — tap the drop key once to instantly drop whatever you're holding; tap it again within the window to also drop the rest of your inventory, skipping any blacklisted items
* **Force drop** — hold the drop key to drop your entire inventory at once, ignoring the blacklist entirely
* **Drop blacklist** — a configurable list of items that double-tap/force-drop will never touch, so you don't accidentally toss your tools down a ravine
* **Auto-sell at the company desk** — press the drop key once while looking at the counter to place all your sellable scrap on it automatically, one item at a time
* **Sell blacklist** — a separate configurable list of items the auto-sell feature will always skip
* **Configurable grab delay** — shrink the vanilla delay between picking up items down to as little as 0.01s for rapid-fire grabbing
* **ShipInventoryUpdated chute auto-store** *(optional add-on, requires [ShipInventoryUpdated](https://thunderstore.io/c/lethal-company/p/BobDaBiscuit/ShipInventory/))* — press the drop key once while hovering over the ship's chute to automatically feed your entire inventory into it, one item at a time, including anything you grab while it's still running

Your originally selected hotbar slot is always restored afterward, even if it ends up empty.

## Blacklists

Both the drop blacklist and the sell blacklist default to all of the **store-purchasable grabbable items** (Walkie-talkie, Flashlight, Shovel, Lockpicker, Pro-flashlight, Stun grenade, Boombox, TZP-Inhalant, Zap gun, Jetpack, Extension Ladder, Radar-booster, Spray paint, Weed killer, Belt bag, Kitchen knife, Shotgun, Ammo, Key) — so out of the box, quick-drop and auto-sell only ever touch scrap, and your equipped tools stay safely in your hands. Both lists are fully configurable and independent of each other.

## Compatibility

Designed to play nicely with other inventory/hotbar mods. Optional integrations are never required for the base mod to function.

* **ShipInventoryUpdated** — soft dependency, adds chute auto-storing support

Planned: HotbarPlus, ReservedItemSlot.

## Feedback

Found a bug or have a suggestion? Please report it on the [GitHub repository](https://github.com/manvocao0271/DropAndGrabOverhaul), including your Lethal Company version, DropAndGrabOverhaul version, BepInEx version, and any other inventory/hotbar mods installed.
