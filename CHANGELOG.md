# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.2.9] - 2026-10-06
- fixed the drop key acting while typing in chat, in menus or the terminal, or in other states vanilla ignores it
- fixed double-tap drop-all desyncing the first item when the tap's drop was still syncing over the network
- fixed drop-all permanently stopping if a grab animation never finished (e.g. dying mid-grab)

## [0.2.8] - 2026-10-03
- fixed drop-all aborting early when a grab animation was still playing
- fixed a tap after releasing a force-drop hold counting as a double-tap

## [0.2.7] - 2026-09-29
- added ReservedItemSlotCore support

## [0.2.6] - 2026-09-27
- removed ShipInventoryUpdated support
- added rebindable drop key support (change your keybind in global configs)