# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.6] - 2026-08-28 (BETA - fix unverified in live multiplayer)

### Fixed

- Fixed items still disappearing in the trailing restore after a double-tap drop-all. The coroutine's final `player.SwitchToItemSlot(originalSlot)` was unconditional and ran even when no items were actually dropped (blacklisted) or when the loop had timed out waiting for a network echo. Since `SwitchToItemSlot` unconditionally overwrites `currentlyHeldObjectServer`, this reproduced the exact same desync race the per-item wait was written to prevent: clobbering the reference for a throw still in flight (from a timeout-interrupted item in the loop above, or from an unrelated concurrent single-tap drop), causing `ThrowObjectClientRpc` to find a mismatch and skip placement. Now only restores the slot when both necessary (actually switched away from it) and safe (`currentlyHeldObjectServer` is already clear).

## [0.1.5] - 2026-08-27 (BETA - fix unverified in live multiplayer)

### Fixed

- Fixed items disappearing (or the whole drop-all batch vanishing) when dropping multiple items back-to-back with double-tap/force-drop, reported live over multiplayer. `DropAllItemsCoroutine` waited for `currentlyHeldObjectServer` to clear before switching to the next item, but on a laggy connection that wait could time out before the previous item's `ThrowObjectClientRpc` echo actually arrived; switching slots anyway then overwrote `currentlyHeldObjectServer` out from under the still in-flight throw, so the delayed echo found a mismatched reference (logged by the game as `ThrowObjectClientRpc called for an object which is not the same as currentlyHeldObjectServer`) and never finished clearing/placing the item. The coroutine now stops dropping further items if that wait times out instead of switching anyway, so the pending echo can still land on a matching reference. **Note:** The defensive timeout break has been implemented and tested in a debug session, but the overall fix has not yet been validated in a live multiplayer server — users should report any remaining desync issues.

## [0.1.4] - 2026-08-27

### Changed

- A plain tap of the drop key now drops the held item immediately instead of after a deferred wait, eliminating the ~0.2-0.3s of added latency on every drop. A second tap within the double-tap window drops the rest of your eligible items instead of upgrading a still-pending single drop. As a tradeoff, the blacklist can no longer exempt the currently-held item specifically in a double-tap - it's already gone by the time the second tap registers, the same as it would be for a lone tap.

## [0.1.3] - 2026-08-27

### Fixed

- Fixed a bug where every Harmony patch in the plugin (grab delay, discard-held-object, and the drop cooldown override) was silently never being applied. `Harmony.PatchAll()` only scans types that carry their own class-level `[HarmonyPatch]` attribute; since all patches are annotated directly on their methods inside the plain `Plugin` class, `PatchAll()` skipped them without any error or log output. Switched to `PatchAll(typeof(Plugin))`, which patches unannotated container types correctly.
- Replaced the previous grab-cooldown-removal patches (which risked corrupting an in-flight grab via `StopCoroutine`) with a Harmony transpiler that directly shortens the two fixed waits inside `PlayerControllerB.GrabObject()`, driven by the new `GrabDelay` config value.
- Fixed regular single-item dropping no longer working now that the `DiscardHeldObject` patch above is actually active: it suppresses every vanilla drop triggered by G, including a plain single tap, with nothing previously replacing that behavior. A plain tap now performs a deferred single-item drop (delayed by the double-tap window) so a following second tap can still upgrade it into a drop-all.
- Fixed the plugin's drop logic never actually running: the `Plugin` instance's own `Update()` (and any GameObject created in `Awake()`, even with `DontDestroyOnLoad`) was being torn down by a scene transition shortly after chainloader startup, before `Start()`/`Update()` ever fired once. The Harmony patches kept working throughout since they patch target methods' IL directly and don't depend on any live instance, which masked the issue. Moved the drop logic to a dedicated runner component created lazily via a `StartOfRound.Awake` postfix, well after that transition has settled.

## [0.1.2] - 2026-08-27

### Fixed

- **[BETA - unverified in live multiplayer]** Fixed multiplayer issue where non-host clients would not see dropped items visually appear on their screen after double-tapping or force-dropping, despite items being correctly networked and scannable for all players.

## [0.1.1] - 2026-08-26

### Changed

- Updated package description.

## [0.1.0] - 2026-08-26

### Added

- Drop all held/inventory items with a single key (double-tap or hold-to-force-drop).
- Configurable drop blacklist to exclude specific items from being dropped.
- Auto-sell inventory scrap automatically when standing at the company desk.
- Configurable sell blacklist to exclude specific items from being auto-sold.
- Option to remove the grab cooldown between picking up items.

