# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.2.0-beta] - 2026-08-29

### Changed (Major Redesign)

- **Chute auto-store despawn mechanism completely redesigned for non-host reliability**: replaced the fragile `PlayerControllerB.DestroyItemInSlotAndSync` RPC round-trip (which failed intermittently for non-host clients under lag) with a host-authoritative finalize step. All clients still initiate the store, but finalization (adding to ship inventory + despawning the `NetworkObject`) now happens entirely on the host via a lightweight `CustomMessagingManager` named message, sidestepping the ownership constraints that made `DestroyItemInSlotAndSync` unreliable for non-owners. The host routes despawn requests through `NetworkObject.Despawn()` (which requires only `IsServer`, never ownership) instead of relying on vanilla's ownership-gated `DestroyItemInSlotServerRpc`.

### Fixed

- Fixed hotbar freeze when picking up new items while chute auto-store is running: previously, if you grabbed a new item while `StoreAllInChuteCoroutine` was detaching items on the same frame, the detach logic would null `player.currentlyHeldObjectServer` while vanilla's own `GrabObject()` coroutine was mid-animation waiting for that field to match the newly-grabbed item, permanently breaking the wait condition and freezing `isGrabbingObjectAnimation` (blocking scroll/drop/activate for the rest of the session). Now the auto-store sequence waits for any in-flight grab animation to complete before detaching each item.
- Fixed ghost HUD item name appearing without visual hand or drop ability while auto-store runs: same root cause as the hotbar freeze—eliminated by the grab-animation guard above.
- Fixed ship inventory count inflation when chute-store items fail to despawn: when an item's despawn RPC silently failed, the item was already registered in the ship inventory (via `Inventory.Add`) but never physically removed, permanently inflating `Inventory.Count` and eventually blocking the chute from accepting new items for the whole lobby once the cap was hit. The new host-only finalize design keeps `Inventory.Add` and `Despawn()` back-to-back in a single uninterrupted host-local call with a try/catch rollback, so a failed despawn never orphans an inventory entry.
- Removed the now-obsolete `ClearStaleSlotIcon` workaround and `GrabAnimationWaitTimeout` from `ProcessChuteFinalizeQueue` (it had been waiting for `isGrabbingObjectAnimation` to clear, but the real race was in `StoreAllInChuteCoroutine`'s detach step, which is now guarded correctly at its source).

### Build Verification

- ✅ Compiles with 0 errors/warnings
- ⏳ **UNVERIFIED in live multiplayer** — build-verified only; needs testing with multiple players to confirm non-host despawn reliability and absence of ghost items

## [0.1.9] - 2026-08-28

### Fixed

- Fixed chute auto-store carryWeight not resetting after storing all items. After storing items, the player's displayed weight would remain at stale high values (e.g. "37 lbs" when empty). Root cause: fixing the chute-store NRE bug made vanilla's `DestroyItemInSlot` reliably subtract weight for every item, but `StoreAllInChuteCoroutine` was still also manually subtracting at detach time and `ProcessChuteFinalizeQueue` was adding it back before equipping—a lossy dance via `Mathf.Clamp(_, 1f, 10f)` that left residual weight. Now `carryWeight` is untouched at detach time and only modified once per item by vanilla's subtraction during the equip-and-destroy step.

## [0.1.8] - 2026-08-28

### Fixed

- Fixed the chute auto-store `StopOnJump` feature never actually stopping: it only checked `player.isJumping` once per item (gated behind a single `WaitForSeconds`), so a brief jump window (only ~0.25s) could be easily missed. Replaced with per-frame polling that checks `isJumping`, `isFallingFromJump`, and `isFallingNoJump` every frame during the delay between items.
- Extended the stop condition to cover falling without jumping (e.g. stepping off the elevated ship), not just intentional jump input, via the new `isFallingNoJump` flag check.
- Fixed chute auto-store item duplication bug: items would sometimes remain physicallly in the world and grabbable even after being stored in inventory. Root cause was `PlayerControllerB.DestroyItemInSlotAndSync` only immediately despawns the `NetworkObject` for the host; all other clients must wait for a client→host→everyone RPC round trip. The old implementation fired independent fire-and-forget coroutines per item with no despawn confirmation and trusted slot indices captured before delayed destruction. Under lag, the RPC could silently fail, leaving data stored while the physical pickup remained—a duplicate. Replaced with a single serialized `ProcessChuteFinalizeQueue` coroutine that confirms each despawn with polling before moving to the next item.

## [0.1.7] - 2026-08-28

### Added

- Added ShipInventoryUpdated chute auto-store integration (optional, requires [ShipInventoryUpdated](https://thunderstore.io/c/lethal-company/)): pressing the drop key once while hovering the chute's interact trigger now stores your whole inventory into it, one item at a time (with its own fall animation and drop sound), including anything you grab while the sequence is still running. Blacklisted items (via ShipInventoryUpdated's own config) are skipped.
- Added `[ShipInventoryUpdated]` config section with `StoreDelayLanded` (default 1s) and `StoreDelayOrbit` (default 0.2s) to control the pacing between items stored, differentiated by whether the ship has landed.
- Added `THUNDERSTORE.md`, a feature-focused listing description used for the Thunderstore package page, separate from the GitHub `README.md`.

### Fixed

- Fixed the chute auto-store sequence cutting off the drop sound early when the configured delay was short. The pacing delay between items is now decoupled from a minimum settle time, so each item stays visible/audible for at least 1 second regardless of how short the configured delay is.
- Fixed the chute auto-store feature never respecting its own blacklist: `ShipInventoryUpdated.Configurations.Configuration.Instance` is a public static field, not a property, so reflecting it via `GetProperty` always returned null and silently treated every item as non-blacklisted. Switched to `GetField`.

### Changed

- Documentation clarified across the README, in-code comments, and BepInEx config descriptions to consistently describe both auto-sell and the chute auto-store as triggered by pressing the drop key once, not holding it.

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

