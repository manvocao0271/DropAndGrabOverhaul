# Changelog

## [0.2.9] - 2026-10-06
- fixed the drop key acting while typing in chat, in menus or the terminal, or in other states vanilla ignores it
- fixed double-tap drop-all desyncing the first item when the tap's drop was still syncing over the network
- fixed drop-all permanently stopping if a grab animation never finished (e.g. dying mid-grab)
- the mod now replaces vanilla's drop-key handler instead of suppressing any drop on a key-press frame
- vanilla's drop key is left alone if the mod can't read the key or isn't running
- tapping the drop key while a held item is over the company counter places it on the counter (as vanilla does), even with auto-sell off
- the company desk is now looked up once per scene instead of about once per second on every moon
- auto-sell no longer sells items from ReservedItemSlotCore reserved slots (main hotbar only)
- the drop key no longer cancels ship build mode during a grab animation or right after switching slots, same as vanilla

## [0.2.8] - 2026-10-03
- fixed drop-all aborting early when a grab animation was still playing
- fixed a tap after releasing a force-drop hold counting as a double-tap

## [0.2.7] - 2026-09-29
- added ReservedItemSlotCore support

## [0.2.6] - 2026-09-27
- removed ShipInventoryUpdated support
- added rebindable drop key support (change your keybind in global configs)