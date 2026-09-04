## Design Philosophy

Currently, players manually cycle through their inventory to drop their items and the same tedious work goes for selling on the company counter. Luckily, this does not have to be a permanent problem. The solution is to eliminate the monotonous actions of scrap dropping, grabbing, selling, and storing.

DropAndGrabOverhaul provides a balanced (or at least not overly powerful) alternative to easily do these repetitive and time-consuming actions. This mod lets players immediately drop all looted scrap which favors more time spent in the building. Additionally, high quotas can often lead to large piles of scrap so near-instant grabbing to transport faster becomes a necessity. This is useful with support for the ShipInventoryUpdated mod that lets players store large quantities of scrap quicker.

> **Status:** Early development

## Features

* **Double-tap drop** — tap the drop key once to instantly drop whatever you're holding; tap it again within the window to also drop the rest of your inventory, skipping any blacklisted items
* **Force drop** — hold the drop key to drop your entire inventory at once, ignoring the blacklist entirely
* **Drop blacklist** — a configurable list of items that double-tap/force-drop will never touch, so you don't accidentally toss your tools down a ravine
* **Auto-sell at the company desk** — press the drop key once while looking at the counter to place all your sellable scrap on it automatically, one item at a time
* **Sell blacklist** — a separate configurable list of items the auto-sell feature will always skip
* **Configurable grab delay** — shrink the vanilla delay between picking up items down to as little as 0.01s for rapid-fire grabbing
* **ShipInventoryUpdated chute auto-store** *(optional add-on, requires [ShipInventoryUpdated](https://thunderstore.io/c/lethal-company/p/BobDaBiscuit/ShipInventory/))* — press the drop key once while hovering over the ship's chute to automatically feed your entire inventory into it, one item at a time, including anything you grab while it's still running. Jumping or falling (e.g. off the elevated ship) cancels the rest of the sequence by default (configurable). **Limitation:** non-host clients depend on a network round-trip for item despawn confirmation; heavy host load from other mods may cause brief delays. In rare cases with extreme lag or mod conflicts, non-host clients may see "ghost items" (HUD icons/sounds persisting after the item appears to drop) until the host processes the finalize request.

Your originally selected hotbar slot is always restored afterward, even if it ends up empty.

## Blacklists

Both the drop blacklist and the sell blacklist default to all of the **store-purchasable grabbable items** (Walkie-talkie, Flashlight, Shovel, Lockpicker, Pro-flashlight, Stun grenade, Boombox, TZP-Inhalant, Zap gun, Jetpack, Extension Ladder, Radar-booster, Spray paint, Weed killer, Belt bag, Kitchen knife, Shotgun, Ammo, Key) — so out of the box, quick-drop and auto-sell only ever touch scrap, and your equipped tools stay safely in your hands. Both lists are fully configurable and independent of each other.

## Compatibility

Designed to play nicely with other inventory/hotbar mods. Optional integrations are never required for the base mod to function.

* **ShipInventoryUpdated** — soft dependency, adds chute auto-storing support (current works on host only)

Planned: ReservedItemSlot, LethalCompany_InputUtils (for custom drop key rebind)

## Feedback

Found a bug or have a suggestion? Please report it on the [GitHub repository](https://github.com/manvocao0271/DropAndGrabOverhaul), including your Lethal Company version, DropAndGrabOverhaul version, BepInEx version, and any other inventory/hotbar mods installed.

#### Special thanks to Wooper.exe and taetae for helping me playtest the mod!