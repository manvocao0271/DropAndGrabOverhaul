using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;
using DropAndGrabOverhaul.Input;
using DropAndGrabOverhaul.Inventory;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Compatibility;
using GameNetcodeStuff;
using UnityEngine.InputSystem;

namespace DropAndGrabOverhaul;

// Here are some basic resources on code style and naming conventions to help
// you in your first CSharp plugin!
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
// https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces

// The BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you!
// For more info, see https://github.com/Hamunii/BepInEx.AutoPlugin

/// <summary>
/// The BepInEx plugin class of DropAndGrabOverhaul.
/// </summary>
[BepInAutoPlugin]
[BepInDependency("com.rune580.LethalCompanyInputUtils", BepInDependency.DependencyFlags.HardDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private static Harmony? harmonyInstance;
    private static UpdateRunner? runner;
    private static Coroutine? autoSellCoroutine;
    private static Coroutine? dropAllCoroutine;
    private static Coroutine? storeInChuteCoroutine;
    private static bool isPlacingOnCounter;
    private static bool isDroppingAll;

    // How long to wait for a real, player-initiated grab (isGrabbingObjectAnimation) to finish
    // before giving up and processing the item anyway (see StoreAllInChuteCoroutine) - bounds the
    // wait so a stuck flag from some unrelated bug can't hang the auto-store sequence forever.
    private const float GrabAnimationWaitTimeout = 3f;

    // How long after any chute-store activity (including the coroutine being cut short by a
    // jump/fall) to keep suppressing generic force-drop/double-tap/single-tap detection. Without
    // this, a G press landing right after the coroutine stops - while the player is still trying
    // to interact with the chute but happens to not be hovering it that exact frame (e.g. mid-jump,
    // or aim drifted off while scrolling the hotbar) - gets read as a fresh, genuine tap by the
    // generic detectors and can trigger an unintended drop-all.
    private const float ChuteActivityGraceWindow = 0.5f;
    private static float lastChuteActivityTime = -999f;

    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        Log = Logger;

        // Initialize configurations
        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);
        GrabConfiguration.Initialize(Config);
        SellConfiguration.Initialize(Config);
        ShipInventoryConfiguration.Initialize(Config);

        // Apply Harmony patch to intercept drop behavior
        // Our patches are annotated directly on their methods with no wrapping class carrying its
        // own [HarmonyPatch], so the parameterless PatchAll() silently skips them - it only scans
        // types that already have a class-level [HarmonyPatch] attribute. Passing typeof(Plugin)
        // uses PatchAll(Type) instead, which allows unannotated container types.
        harmonyInstance = new Harmony("com.github.manvocao0271.dropandgraboverhaul");
        harmonyInstance.PatchAll(typeof(Plugin));

        // Anything we create this early gets destroyed by a scene transition that happens
        // shortly after chainloader startup, so the runner is created lazily once StartOfRound
        // exists instead (see StartOfRoundAwakePostfix below).

        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    // Dedicated MonoBehaviour on its own GameObject - anything created in Plugin.Awake() gets
    // destroyed by a scene transition shortly after chainloader startup, taking Update() with it.
    private sealed class UpdateRunner : MonoBehaviour
    {
        private void Update()
        {
            Plugin.RunUpdate();
        }
    }

    private static void RunUpdate()
    {
        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
            return;

        // ShipInventoryUpdated compatibility: pressing the drop key once while hovering its chute
        // stores the whole inventory (including anything grabbed afterwards) until it's empty, if
        // that mod happens to be installed. The player doesn't need to keep looking at the chute
        // for this to keep working - AcquireHoveredChute captures a reusable handle up front, and
        // the coroutine's own stop condition is player.isInHangarShipRoom rather than requiring
        // continued hover (see StoreAllInChuteCoroutine's remarks for why that's the right check).
        if (ShipInventoryCompat.IsLoaded && ShipInventoryCompat.IsHoveringChute(player) && InputHandler.IsDropKeyPressed())
        {
            // Only start the coroutine if one isn't already running, otherwise it never
            // gets past the first item's delay before being killed and restarted
            if (storeInChuteCoroutine == null)
            {
                object chuteHandle = ShipInventoryCompat.AcquireHoveredChute(player)!;
                storeInChuteCoroutine = runner!.StartCoroutine(StoreAllInChuteCoroutine(player, chuteHandle));
            }
            lastChuteActivityTime = Time.time;
            InputHandler.ResetDropKeyTracking();
            return;
        }

        // Recently active at the chute (including the coroutine having just been cut short by a
        // jump/fall) - keep suppressing generic drop detection a bit longer, see
        // ChuteActivityGraceWindow.
        if (Time.time - lastChuteActivityTime < ChuteActivityGraceWindow)
        {
            InputHandler.ResetDropKeyTracking();
            return;
        }

        // Check if player is looking at the company desk's interact trigger (same condition
        // the vanilla game uses to show the "Sell item : [E]" hover tip)
        DepositItemsDesk? desk = UnityEngine.Object.FindObjectOfType<DepositItemsDesk>();
        bool atDesk = desk != null && desk.triggerScript != null && player.hoveringOverTrigger == desk.triggerScript;

        // Auto-sell inventory if enabled and drop key is pressed near the counter
        if (SellConfiguration.AutoSellInventory && atDesk && InputHandler.IsDropKeyPressed() && desk != null)
        {
            // Only start the coroutine if one isn't already running, otherwise it never
            // gets past the first item's delay before being killed and restarted
            if (autoSellCoroutine == null)
                autoSellCoroutine = runner!.StartCoroutine(AutoSellInventoryCoroutine(player, desk));
            InputHandler.ResetDropKeyTracking();
            return;
        }

        // Don't allow drop-all actions if player is at desk and auto-sell is enabled
        if (atDesk && SellConfiguration.AutoSellInventory)
        {
            InputHandler.ResetDropKeyTracking();
            return;
        }

        // Check for force drop (holding key) first
        bool isForceDropping = InputHandler.IsForceDropHeld();
        
        // Check if drop key was double-tapped (only if not force dropping)
        bool isDoubleTap = !isForceDropping && InputHandler.IsDoubleTapDrop();

        if (!isForceDropping && !isDoubleTap)
        {
            // DiscardHeldObjectPrefix suppresses every vanilla drop triggered by G, including a
            // plain single tap, so a normal single-item drop has to be replicated here. It fires
            // immediately rather than waiting out the double-tap window, so a following second
            // tap only drops whatever remains - the held item is already gone by then.
            if (Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame && player.currentlyHeldObjectServer != null)
            {
                Plugin.Log.LogInfo("Single tap detected - dropping held item immediately");
                isDroppingAll = true;
                player.DiscardHeldObject();
                isDroppingAll = false;
            }
            return;
        }

        Plugin.Log.LogInfo(isForceDropping ? "Force drop detected - dropping ALL items (ignoring blacklist)" : "Double-tap drop detected - dropping all items");

        if (player.ItemSlots == null || player.ItemSlots.Length == 0)
        {
            Plugin.Log.LogInfo("No item slots available");
            return;
        }

        // Collect all items to drop (excluding null slots)
        var itemsToDropList = new List<GrabbableObject>();
        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            if (player.ItemSlots[i] != null)
            {
                itemsToDropList.Add(player.ItemSlots[i]);
            }
        }

        if (itemsToDropList.Count == 0)
        {
            Plugin.Log.LogInfo("No items to drop");
            return;
        }

        // NOTE: we intentionally don't use the vanilla PlayerControllerB.DropAllHeldItemsAndSync
        // here (the method KillPlayer uses to drop everything on death). It also drops
        // ItemOnlySlot - the utility "tab" slot item (walkie-talkie, compass, etc.) - with no
        // way to exclude it, so using it would toss that item too. Looping player.ItemSlots
        // ourselves naturally leaves ItemOnlySlot untouched, for both double-tap and force drop
        // today. Force drop is SUPPOSED to be the one exception (drop everything, no exceptions,
        // including the utility slot) - see the NOT IMPLEMENTED YET note at the end of
        // DropAllItemsCoroutine for why that isn't wired in yet.
        // Only start the coroutine if one isn't already running, otherwise it never
        // gets past the first item's delay before being killed and restarted
        if (dropAllCoroutine == null)
            dropAllCoroutine = runner!.StartCoroutine(DropAllItemsCoroutine(player, itemsToDropList, isForceDropping));
    }

    private static System.Collections.IEnumerator DropAllItemsCoroutine(PlayerControllerB player, List<GrabbableObject> itemsToDrop, bool isForceDropping)
    {
        // Wrapped in try/finally so an uncaught exception partway through (e.g. a stale slot
        // index from another mod resizing ItemSlots) can't leave dropAllCoroutine stuck non-null
        // forever - that would silently and permanently disable double-tap/hold-drop for the rest
        // of the session, since RunUpdate only ever starts a new one when this is null.
        try
        {
            // Save the current item slot to restore later
            int originalSlot = player.currentItemSlot;

            // Drop each item by equipping it first, then dropping it
            int droppedCount = 0;
            foreach (GrabbableObject item in itemsToDrop)
            {
                if (item != null)
                {
                    string itemName = item.itemProperties.itemName;

                    // Find the slot index
                    int slotIndex = System.Array.IndexOf(player.ItemSlots, item);

                    // Blacklist only applies to the double-tap drop; force drop ignores it
                    if (!isForceDropping && ItemBlacklist.IsBlacklisted(itemName))
                    {
                        Plugin.Log.LogInfo($"Skipping blacklisted item: {itemName}");
                        continue;
                    }

                    // Switch to this slot to equip it
                    player.SwitchToItemSlot(slotIndex);
                    // Now drop it, suppressing the drop-key patch in case this still lands on a G press frame
                    isDroppingAll = true;
                    player.DiscardHeldObject();
                    isDroppingAll = false;
                    droppedCount++;
                    Plugin.Log.LogInfo($"Dropped item: {itemName}");

                    // currentlyHeldObjectServer is only cleared once the ThrowObjectClientRpc echo
                    // arrives back on this client. Switching slots before that lands makes
                    // SwitchToItemSlot overwrite the reference while the throw is still in flight -
                    // the delayed echo then finds a mismatched currentlyHeldObjectServer and the
                    // vanilla ThrowObjectClientRpc safety check logs "not the same as
                    // currentlyHeldObjectServer" and skips clearing it, leaving the item stuck
                    // invisible/undropped for everyone. Wait for the reference to actually clear,
                    // with a timeout so a dropped RPC can't hang us - but if it does time out (e.g.
                    // a laggy host), stop here instead of switching slots anyway, so the still-
                    // in-flight echo can still land on a matching reference later.
                    float waitStart = UnityEngine.Time.time;
                    while (player.currentlyHeldObjectServer != null && UnityEngine.Time.time - waitStart < 2f)
                    {
                        yield return null;
                    }

                    if (player.currentlyHeldObjectServer != null)
                    {
                        Plugin.Log.LogWarning($"Timed out waiting for '{itemName}' to finish dropping over the network - stopping drop-all early to avoid desyncing the rest.");
                        break;
                    }
                }
            }

            // Restore the original hotbar slot index, even if it's now empty - but only if we
            // actually switched away from it and it's currently safe to do so. SwitchToItemSlot
            // unconditionally overwrites currentlyHeldObjectServer, so calling it unconditionally
            // here reproduces the exact same desync race the per-item wait above guards against:
            // it would clobber the reference for a throw that's still in flight, whether that's
            // our own timed-out item above, or a wholly unrelated drop (e.g. the immediate
            // single-tap drop) that happened to be in progress concurrently on another item.
            if (player.currentItemSlot != originalSlot && player.currentlyHeldObjectServer == null)
            {
                player.SwitchToItemSlot(originalSlot);
            }

            Plugin.Log.LogInfo($"Dropped {droppedCount} items total");

            // NOT IMPLEMENTED YET: force drop is supposed to also drop ItemOnlySlot, the utility
            // "tab" slot item (walkie-talkie, compass, etc.) that itemsToDropList deliberately
            // excludes above (see the NOTE where it's built from player.ItemSlots) so double-tap
            // and auto-store never touch it. ItemOnlySlot isn't addressable via SwitchToItemSlot -
            // it isn't part of player.ItemSlots at all - so the plan was to reuse vanilla's own
            // DropAllHeldItemsAndSync (the method player death already relies on to correctly
            // drop that slot - see the NOTE above), called here once player.ItemSlots is already
            // empty so it would only find ItemOnlySlot left to act on.
            //
            // Reverted: DropAllHeldItemsAndSync actually takes 5 Vector3 arguments (the first
            // named playerPosition, per the compiler's own error - the other 4 unnamed). KillPlayer
            // uses this to scatter a dead player's held items around their corpse, so the other 4
            // are most likely per-ItemSlot landing positions, but that's inferred, not confirmed -
            // I couldn't find this method's actual decompiled body, and I don't know if ItemOnlySlot
            // is even covered by one of those 4 or handled separately inside the method. Guessing
            // placeholder positions risks the item landing somewhere wrong (or not spawning at all)
            // with no compiler error to catch it - worse than just not having this yet.
            //
            // To finish this: either share PlayerControllerB's decompiled DropAllHeldItemsAndSync
            // (or just its signature/parameter names), or confirm what the 4 positions represent,
            // and I'll wire in the real call.
        }
        finally
        {
            dropAllCoroutine = null;
        }
    }

    private static System.Collections.IEnumerator AutoSellInventoryCoroutine(PlayerControllerB player, DepositItemsDesk desk)
    {
        // See DropAllItemsCoroutine's try/finally comment - same reasoning applies here so an
        // uncaught exception can't leave autoSellCoroutine stuck non-null forever.
        try
        {
            int soldCount = 0;
            for (int i = 0; i < player.ItemSlots.Length; i++)
            {
                if (player.ItemSlots[i] != null && player.ItemSlots[i].itemProperties.isScrap)
                {
                    string itemName = player.ItemSlots[i].itemProperties.itemName;

                    if (SellConfiguration.IsSellBlacklisted(itemName))
                    {
                        Plugin.Log.LogInfo($"Skipping sell-blacklisted item: {itemName}");
                        continue;
                    }

                    player.SwitchToItemSlot(i);
                    // Suppress the drop-key patch from swallowing this call if it lands on a G press frame
                    isPlacingOnCounter = true;
                    desk.PlaceItemOnCounter(player);
                    isPlacingOnCounter = false;
                    soldCount++;
                    Plugin.Log.LogInfo($"Sold item: {itemName}");
                    // Wait for the grab animation to complete before selling the next item
                    yield return new WaitForSeconds(0.2f);
                }
            }
            Plugin.Log.LogInfo($"Sold {soldCount} items total");
        }
        finally
        {
            autoSellCoroutine = null;
        }
    }

    // Redesigned around one idea: make each item's auto-store indistinguishable from the player
    // walking up, equipping it, and holding [E] themselves. That means every item - whether it
    // was already equipped or sitting pocketed - gets equipped first (a manual store can't work
    // any other way either), and the actual store is a single call into
    // ShipInventoryCompat.AttemptStore, which checks ChuteTrigger's own
    // interactable gate (blacklist/Store-Permission/Only-In-Orbit - see that method's remarks for
    // why this check, not anything downstream, is what enforces those) and then invokes
    // ShipInventoryUpdated's real Interact() path: BeltBagItem-aware conversion, the ownerless-RPC
    // Inventory.Add, and player.DestroyItemInSlotAndSync to clear the slot and despawn - on
    // whichever client's player is doing the storing, host or not - so there's nothing left here
    // to relay to the host, retry, or roll back. See ShipInventoryCompat's class remarks for the
    // full chain, confirmed against ShipInventoryUpdated's actual ChuteStore.cs/ChuteTrigger.cs.
    //
    // The player doesn't need to keep hovering the chute for this to keep running: chuteHandle is
    // acquired once, up front (see RunUpdate), and reused across every iteration instead of
    // re-deriving it from player.hoveringOverTrigger each time - which is what previously forced
    // the player to stand still staring at the chute the whole time. The stop condition is now
    // player.isInHangarShipRoom instead of continued hover, matching the exact field
    // ChuteTrigger.Update() itself gates its own interactable-refresh on (confirmed in
    // ChuteTrigger.cs) - so as long as the player stays in that room, both ChuteTrigger's own
    // gating and our read of it remain live and consistent, whatever they're looking at. This
    // means the player can walk away from the chute mid-sequence to grab more scrap, and it'll
    // keep getting picked up by GetStorableItems and stored in the background, as long as they
    // don't leave the hangar room entirely.
    //
    // This rewrite no longer adjusts player.carryWeight or calls SendChangedWeightEvent() itself:
    // ChuteStore.StoreHeldItem's real body ends with player.DestroyItemInSlotAndSync(player.
    // currentItemSlot), the same vanilla method the game itself uses elsewhere to destroy/consume
    // a slotted item, which is expected to fix carry weight and sync it as part of clearing the
    // slot - the same as it must for today's manual, working, non-host-safe store. Still worth a
    // quick real-multiplayer sanity check after this change; if weight/speed doesn't update, add
    // back `player.carryWeight = Mathf.Clamp(player.carryWeight - (item.itemProperties.weight -
    // 1f), 1f, 10f);` plus a trailing `StartOfRound.Instance.SendChangedWeightEvent()` once the
    // whole batch is done.
    private static System.Collections.IEnumerator StoreAllInChuteCoroutine(PlayerControllerB player, object chuteHandle)
    {
        // See DropAllItemsCoroutine's try/finally comment - same reasoning applies here so an
        // uncaught exception can't leave storeInChuteCoroutine stuck non-null forever.
        try
        {
        // Restored on every exit path (see stoppedEarly: below) so the player's hotbar doesn't
        // end up resting on whichever slot the last-processed item happened to occupy.
        int originalSlot = player.currentItemSlot;

        // Re-check the player's slots every iteration (instead of snapshotting once up front) so
        // items grabbed after this coroutine already started still get caught and stored too,
        // rather than only ever processing whatever was held at the very start. Items are tracked
        // by reference (not slot index) once claimed since their slot doesn't actually go null
        // until ShipInventoryUpdated's own Interact() call clears it - without this, the same
        // still-pending item would keep getting reselected every iteration until then.
        var processedItems = new HashSet<GrabbableObject>();
        while (true)
        {
            // Always stops the sequence, regardless of StopOnJump - unlike jump/fall this isn't a
            // configurable preference. Vanilla's own KillPlayer calls DropAllHeldItemsAndSync (see
            // DropAllItemsCoroutine's NOTE) to forcibly drop everything the player is holding,
            // including whatever this coroutine is mid-processing. If that races with our own
            // Interact()/despawn-confirm sequence for the same item, the two aren't coordinated -
            // worst case the item gets both added to the ship inventory (Inventory.Add already
            // ran) and dropped back into the world as a live pickup by the death handling,
            // duplicating it. Bailing out the moment we see the player is dead narrows that
            // window as much as we can from this side.
            if (player.isPlayerDead)
            {
                Plugin.Log.LogInfo("Player died - stopping the rest of the chute auto-store sequence");
                break;
            }

            // Configurable early-out: jumping or falling (e.g. off the elevated ship) while items
            // are still being stored cancels the rest of the sequence rather than continuing to
            // store items out from under the player.
            if (IsJumpingOrFalling(player))
            {
                Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                break;
            }

            // The player leaving the hangar room entirely stops the sequence - this is the same
            // field ChuteTrigger.Update() itself gates its interactable-refresh on (see
            // ChuteTrigger.cs), so it's the natural boundary for "am I still somewhere this can
            // keep working", not "am I still looking at the chute". Deliberately more lenient
            // than the old hover-only check: the player can turn around, walk off to grab more
            // scrap, and come back (or not) while this keeps running in the background, as long
            // as they stay in the room.
            if (!player.isInHangarShipRoom)
            {
                Plugin.Log.LogInfo("Left the hangar ship room - stopping the rest of the chute auto-store sequence");
                break;
            }

            // Defensive: the cached handle could in principle go stale mid-sequence (e.g. a scene
            // transition destroying it - see the round-end edge case discussed separately). A
            // fresh player.hoveringOverTrigger read wouldn't need this, but a handle held across
            // many frames can outlive the object it pointed to.
            if (!ShipInventoryCompat.IsChuteHandleValid(chuteHandle))
            {
                Plugin.Log.LogWarning("Lost the chute reference mid-sequence - stopping the rest of the chute auto-store sequence");
                break;
            }

            List<(int Slot, GrabbableObject Item)> items = ShipInventoryCompat.GetStorableItems(player);

            GrabbableObject? item = null;
            int slot = -1;
            foreach ((int candidateSlot, GrabbableObject candidateItem) in items)
            {
                if (!processedItems.Contains(candidateItem))
                {
                    item = candidateItem;
                    slot = candidateSlot;
                    break;
                }
            }

            if (item == null)
                break;

            // A brand-new grab can already show up in ItemSlots/GetStorableItems before vanilla's
            // own GrabObject() coroutine has finished waiting out its animation and cleared
            // isGrabbingObjectAnimation. Switching slots out from under that coroutine right now
            // would break the condition that coroutine's own wait loop is watching
            // (`currentlyGrabbingObject != currentlyHeldObjectServer`) and it would never become
            // true again - leaving isGrabbingObjectAnimation stuck true forever, which freezes the
            // hotbar since scrolling/dropping/activating all gate on that flag. Wait it out first,
            // bounded so a stuck flag can't hang this coroutine forever either.
            float grabWait = 0f;
            while (player.isGrabbingObjectAnimation && player.currentlyGrabbingObject == item && grabWait < GrabAnimationWaitTimeout)
            {
                yield return null;
                grabWait += Time.deltaTime;
            }

            processedItems.Add(item);
            string itemName = item.itemProperties.itemName;
            float itemProcessingStart = Time.time;

            // A real manual store always starts from having the item equipped - you can't
            // interact with something you're not holding - so pocketed items get switched to
            // first. (An earlier version of this comment doubted whether this switch was truly
            // synchronous and suspected it as the cause of an "every other item" report - it
            // wasn't. Confirmed cause: ChuteTrigger's own inherited InteractTrigger cooldown, see
            // the wait below.)
            if (player.currentlyHeldObjectServer != item)
                player.SwitchToItemSlot(slot);

            // Waits for two things, either of which can take more than a single frame: (a)
            // ChuteTrigger's own Update() re-evaluating `interactable` against the item just
            // equipped, and (b) any cooldown left over from the previous successful Interact()
            // call clearing (see ShipInventoryCompat.IsChuteOnCooldown's remarks) - confirmed to
            // be the actual cause of an "every other item" report: with StoreDelayOrbit's short
            // 0.2s default, the next Interact() call landed while still on the trigger's own
            // cooldown, which silently does nothing (no exception, no store) rather than queuing
            // or erroring, and the item then sat through the full despawn-confirm wait below
            // before being logged as failed and abandoned. Waiting for the cooldown here first
            // makes our own configured pacing delay a floor rather than the only gate, so a
            // short delay no longer costs an item outright, just some throughput. Bounded and
            // re-checks the same early-outs as the main loop, since this can now take a bit
            // longer than the single frame it used to be.
            float cooldownWaitStart = Time.time;
            const float chuteCooldownTimeout = 3f;
            do
            {
                if (player.isPlayerDead)
                {
                    Plugin.Log.LogInfo("Player died - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                if (IsJumpingOrFalling(player))
                {
                    Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                if (!player.isInHangarShipRoom)
                {
                    Plugin.Log.LogInfo("Left the hangar ship room - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                yield return null;
            }
            while (ShipInventoryCompat.IsChuteOnCooldown(chuteHandle) && Time.time - cooldownWaitStart < chuteCooldownTimeout);
            float cooldownWaitDuration = Time.time - cooldownWaitStart;

            if (ShipInventoryCompat.IsChuteOnCooldown(chuteHandle))
            {
                Plugin.Log.LogWarning($"Chute stayed on cooldown for {chuteCooldownTimeout:F0}s - skipping '{itemName}' for now rather than waiting longer.");
                continue;
            }

            ShipInventoryCompat.ChuteStoreAttempt attempt = ShipInventoryCompat.AttemptStore(chuteHandle, player, out System.Exception? storeException);

            if (attempt == ShipInventoryCompat.ChuteStoreAttempt.NotAllowed)
            {
                Plugin.Log.LogInfo($"Skipping '{itemName}' - the chute won't currently accept it (blacklisted, permission, or orbit setting).");
                continue;
            }

            if (attempt == ShipInventoryCompat.ChuteStoreAttempt.Threw)
            {
                // Inventory.Add runs before the despawn step inside StoreHeldItem, so a duplicate
                // is a real possibility here, not just "it failed, try again" - see
                // ShipInventoryCompat.ChuteStoreAttempt.Threw's remarks for why. Deliberately
                // doesn't guess at a cause: this isn't the utility-slot DestroyItemInSlot bug
                // (auto-store never selects that slot - see the Threw enum member's remarks), and
                // nothing else about the cause is confirmed, so naming a specific known issue here
                // would just send people looking in the wrong place.
                Plugin.Log.LogWarning(
                    $"Storing '{itemName}' threw ({storeException!.GetType().Name}: {storeException.Message}) - " +
                    "it may already be counted in the ship inventory even though it's still in your hands, so check " +
                    "for a duplicate before storing it again rather than retrying.");
                continue;
            }

            // Interact() returned without throwing, but on a non-host client the actual
            // Inventory.Add/DestroyItemInSlotAndSync round trip this kicked off may still be in
            // flight - give it a short window to land before trusting it actually worked, rather
            // than assuming success the instant the call returns. `item == null` here relies on
            // Unity's overridden equality for a destroyed/despawned object, not the C# reference
            // itself going null. Deliberately short (unlike the old design's 20s confirm-and-
            // retry loop): we're not the one doing the despawning anymore, this is just a sanity
            // check on a call that already works for a manual store today.
            const float despawnConfirmTimeout = 2f;
            float confirmWait = 0f;
            while (item != null && confirmWait < despawnConfirmTimeout)
            {
                yield return null;
                confirmWait += Time.deltaTime;
            }

            if (item != null)
            {
                Plugin.Log.LogWarning(
                    $"'{itemName}' hasn't left your hands {despawnConfirmTimeout:F0}s after being sent to the chute - " +
                    "it may already be counted in the ship inventory even though it's still here. Check for a " +
                    "duplicate before storing it again, or store it manually via the chute's [E] interact.");
                continue;
            }

            // Logged with a timing breakdown (not just success/failure) specifically to check a
            // suspected additional delay beyond the cooldown wait above - e.g. some other equip/
            // swap delay between switching slots and the item actually being ready to store -
            // rather than guessing at another wait to add without evidence it's needed. grabWait
            // covers waiting out a brand-new grab's animation (usually 0 here, since chute items
            // are already in ItemSlots, not freshly grabbed); cooldownWaitDuration is time spent
            // in the wait above (includes the mandatory single frame, so a very short number here
            // is expected and fine); confirmWait is the despawn-confirmation wait below; total is
            // measured from right after this item was selected. If total noticeably exceeds the
            // sum of the other three, that gap is time this comment can't currently explain -
            // worth checking whether it lines up with a per-item pacing/equip delay elsewhere.
            Plugin.Log.LogInfo($"Stored item into ship inventory chute: {itemName} (grab wait {grabWait:F2}s, cooldown wait {cooldownWaitDuration:F2}s, despawn confirm {confirmWait:F2}s, total {Time.time - itemProcessingStart:F2}s)");

            // Waited out frame-by-frame (instead of a single WaitForSeconds) so a jump/fall or
            // death can still be caught mid-wait - isJumping only stays true for ~0.25s after the
            // jump key is pressed, easy to miss entirely if pacingDelay is longer than that and
            // this only polled once per item.
            float pacingDelay = StartOfRound.Instance.shipHasLanded ? ShipInventoryConfiguration.StoreDelayLanded : ShipInventoryConfiguration.StoreDelayOrbit;
            float waited = 0f;
            while (waited < pacingDelay)
            {
                if (player.isPlayerDead)
                {
                    Plugin.Log.LogInfo("Player died - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                if (IsJumpingOrFalling(player))
                {
                    Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                yield return null;
                waited += Time.deltaTime;
            }
        }

        stoppedEarly:
        // Return to whatever was equipped before this sequence started, so the hotbar doesn't
        // end up resting on an arbitrary slot - whichever item happened to be processed last -
        // once this finishes. Matters more now that the player is expected to be doing other
        // things (like grabbing more scrap) while this runs in the background: vanilla's own
        // logic for "which slot does a newly grabbed item land in" reads currentItemSlot as its
        // starting point, so leaving it on a stale, now-empty slot instead of a predictable one
        // made freshly-grabbed items land somewhere unexpected.
        //
        // Mirrors DropAllItemsCoroutine's own restore condition: skipped if something is
        // currently held (a store still resolving, or the player having grabbed something new
        // in the meantime) to avoid clobbering an in-flight state. Wrapped since player could in
        // principle already be a stale reference by this point (e.g. the round-end scene-
        // transition edge case discussed separately) - this shouldn't throw on top of whatever
        // already caused the sequence to end.
        try
        {
            if (player.currentItemSlot != originalSlot && player.currentlyHeldObjectServer == null)
            {
                player.SwitchToItemSlot(originalSlot);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Restoring the original hotbar slot after chute auto-store threw ({e.GetType().Name}: {e.Message}) - harmless, just means the hotbar might rest on the last-processed slot instead.");
        }
        }
        finally
        {
            storeInChuteCoroutine = null;
            lastChuteActivityTime = Time.time;
        }
    }

    // isJumping is only true for the initial ~0.25s takeoff window (see the game's own PlayerJump
    // coroutine); isFallingFromJump covers the rest of that arc until landing; isFallingNoJump
    // covers falling off a ledge/the elevated ship without jumping at all.
    private static bool IsJumpingOrFalling(PlayerControllerB player)
    {
        return ShipInventoryConfiguration.StopOnJump && (player.isJumping || player.isFallingFromJump || player.isFallingNoJump);
    }

    // Anything created in Plugin.Awake() gets destroyed by an early scene transition, so the
    // runner is created lazily here instead, once StartOfRound exists and that transition is over
    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    [HarmonyPostfix]
    private static void StartOfRoundAwakePostfix()
    {
        if (runner != null)
            return;

        GameObject runnerObject = new GameObject("DropAndGrabOverhaulRunner");
        UnityEngine.Object.DontDestroyOnLoad(runnerObject);
        runner = runnerObject.AddComponent<UpdateRunner>();
        Plugin.Log.LogInfo("UpdateRunner created via StartOfRound.Awake postfix");

        // No named-message handler to register anymore: StoreAllInChuteCoroutine now calls
        // ShipInventoryCompat.AttemptStore directly on whichever client is doing the storing,
        // instead of relaying a finalize request to the host.
    }

    // Harmony patch to intercept the game's default drop call
    [HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
    [HarmonyPrefix]
    private static bool DiscardHeldObjectPrefix(PlayerControllerB __instance)
    {
        // Never suppress our own sell-placement or drop-all calls. Chute-storing no longer calls
        // DiscardHeldObject at all (see StoreAllInChuteCoroutine), so there's nothing to exempt
        // for it here anymore.
        if (isPlacingOnCounter || isDroppingAll)
            return true;

        // Supress ALL drops triggered by the G key (single-tap, double-tap, or hold)
        // Let the Update() method handle all drop logic with proper blacklist checks
        if (Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame)
        {
            Plugin.Log.LogInfo($"DiscardHeldObjectPrefix: suppressing vanilla drop on G press, frame: {Time.frameCount}");
            return false;
        }

        return true;
    }

    // Harmony patch to shorten the vanilla 0.2s grab cooldown to the configured delay, and (if
    // ShipInventoryUpdated is loaded) the chute's own InteractTrigger cooldown to our configured
    // chute pacing delay. InteractTrigger.cooldownTime is what Interact() copies into
    // currentCooldownValue on every successful interact, so overriding it here - before the
    // original method runs - is enough; no need to fight the cooldown elsewhere.
    //
    // The chute branch exists because ShipInventoryUpdated's own chute prefab ships with a
    // cooldownTime around 1 second (confirmed empirically: AttemptStore's cooldown-wait loop was
    // consistently landing on either ~0.02s or ~0.79-0.80s, never in between, which is the
    // signature of a real ~1s cooldown against a 0.2s pacing delay, not the 0.2s originally
    // suspected). ChuteStore.cs never touches cooldownTime itself - it only sets timeToHold, a
    // separate field entirely - so that ~1s default was never something StoreDelayLanded/Orbit
    // could reach on their own no matter how low they were set, regardless of AttemptStore's own
    // cooldown-wait already preventing the item loss that mismatch used to cause. Overriding it
    // here makes the configured pacing delay the actual source of truth for both auto-store and
    // a real player manually holding [E] repeatedly, rather than a hardcoded prefab default.
    [HarmonyPatch(typeof(InteractTrigger), "Interact")]
    [HarmonyPrefix]
    private static void InteractTriggerInteractPrefix(InteractTrigger __instance)
    {
        if (__instance.GetComponentInParent<GrabbableObject>() != null)
        {
            __instance.cooldownTime = GrabConfiguration.GrabDelay;
        }
        else if (ShipInventoryCompat.IsLoaded && ShipInventoryCompat.IsChuteTriggerInstance(__instance))
        {
            __instance.cooldownTime = StartOfRound.Instance.shipHasLanded
                ? ShipInventoryConfiguration.StoreDelayLanded
                : ShipInventoryConfiguration.StoreDelayOrbit;
        }
    }

    // Harmony transpiler to shrink the two fixed waits inside PlayerControllerB's GrabObject()
    // coroutine - WaitForSeconds(0.1f) and WaitForSeconds(grabObjectAnimationTime - 0.2f) - which
    // is what actually keeps isGrabbingObjectAnimation true and blocks further grabs/interacts.
    // Patching BeginGrabObject or clearing the flag directly doesn't work: a second press calls
    // StopCoroutine() on the still-running first grab before it finishes, corrupting it. Rewriting
    // the coroutine's own IL constants (same approach as the NoGrabDelay mod) shortens the wait
    // without touching the flag or the server-sync loop in between.
    [HarmonyPatch(typeof(PlayerControllerB), "GrabObject", MethodType.Enumerator)]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> GrabObjectTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getDecrease = typeof(GrabConfiguration).GetMethod(nameof(GrabConfiguration.GetInteractionCooldownDecrease));
        List<CodeInstruction> instr = new List<CodeInstruction>(instructions);
        int patchedCount = 0;
        for (int i = 0; i < instr.Count; i++)
        {
            CodeInstruction instruction = instr[i];
            yield return instruction;

            // WaitForSeconds(0.1f) -> WaitForSeconds(0.1f - decrease / 2f)
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.1f
                && i + 1 < instr.Count && instr[i + 1].opcode == OpCodes.Newobj)
            {
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 2f);
                yield return new CodeInstruction(OpCodes.Div);
                yield return new CodeInstruction(OpCodes.Sub);
                patchedCount++;
            }

            // (grabObjectAnimationTime - 0.2f) -> (grabObjectAnimationTime - 0.2f) - decrease
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.2f
                && i + 1 < instr.Count && instr[i + 1].opcode == OpCodes.Sub)
            {
                yield return new CodeInstruction(OpCodes.Sub);
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                patchedCount++;
            }
        }

        // Should log 2 - if this logs 0, GrabObject()'s IL no longer matches these patterns
        // (e.g. game update) and the transpiler silently did nothing.
        Plugin.Log.LogInfo($"GrabObjectTranspiler patched {patchedCount} delay checkpoint(s)");
    }
}