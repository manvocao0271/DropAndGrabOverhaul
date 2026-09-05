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
        // that mod happens to be installed
        if (ShipInventoryCompat.IsLoaded && ShipInventoryCompat.IsHoveringChute(player) && InputHandler.IsDropKeyPressed())
        {
            // Only start the coroutine if one isn't already running, otherwise it never
            // gets past the first item's delay before being killed and restarted
            if (storeInChuteCoroutine == null)
                storeInChuteCoroutine = runner!.StartCoroutine(StoreAllInChuteCoroutine(player));
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
    // This rewrite no longer adjusts player.carryWeight or calls SendChangedWeightEvent() itself:
    // ChuteStore.StoreHeldItem's real body ends with player.DestroyItemInSlotAndSync(player.
    // currentItemSlot), the same vanilla method the game itself uses elsewhere to destroy/consume
    // a slotted item, which is expected to fix carry weight and sync it as part of clearing the
    // slot - the same as it must for today's manual, working, non-host-safe store. Still worth a
    // quick real-multiplayer sanity check after this change; if weight/speed doesn't update, add
    // back `player.carryWeight = Mathf.Clamp(player.carryWeight - (item.itemProperties.weight -
    // 1f), 1f, 10f);` plus a trailing `StartOfRound.Instance.SendChangedWeightEvent()` once the
    // whole batch is done.
    private static System.Collections.IEnumerator StoreAllInChuteCoroutine(PlayerControllerB player)
    {
        // See DropAllItemsCoroutine's try/finally comment - same reasoning applies here so an
        // uncaught exception can't leave storeInChuteCoroutine stuck non-null forever.
        try
        {
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

            // The player walking away from the chute mid-sequence should stop it too, the same as
            // a manual store would be interrupted by stepping back from the trigger. The old
            // design never re-checked this once the coroutine started.
            if (!ShipInventoryCompat.IsHoveringChute(player))
            {
                Plugin.Log.LogInfo("No longer hovering the chute - stopping the rest of the chute auto-store sequence");
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

            // A real manual store always starts from having the item equipped - you can't
            // interact with something you're not holding - so pocketed items get switched to
            // first. This is a purely local, synchronous call (unlike grabbing a new object from
            // the world), so no extra wait is needed before the item is considered "held".
            if (player.currentlyHeldObjectServer != item)
                player.SwitchToItemSlot(slot);

            // Give ChuteTrigger's own Update() a frame to re-evaluate `interactable` against the
            // item we just equipped - that's the same flag a real player's hover prompt reflects,
            // and AttemptStore trusts it as the final gate (blacklist, Store Permission, Only In
            // Orbit) rather than us re-deriving those rules ourselves.
            yield return null;

            ShipInventoryCompat.ChuteStoreAttempt attempt = ShipInventoryCompat.AttemptStore(player, out System.Exception? storeException);

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

            Plugin.Log.LogInfo($"Stored item into ship inventory chute: {itemName}");

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
        ;
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

    // Harmony patch to shorten the vanilla 0.2s grab cooldown to the configured delay.
    // InteractTrigger.cooldownTime is what Interact() copies into currentCooldownValue on every
    // successful grab, so overriding it here is enough - no need to fight the cooldown elsewhere.
    [HarmonyPatch(typeof(InteractTrigger), "Interact")]
    [HarmonyPrefix]
    private static void InteractTriggerInteractPrefix(InteractTrigger __instance)
    {
        if (__instance.GetComponentInParent<GrabbableObject>() != null)
        {
            __instance.cooldownTime = GrabConfiguration.GrabDelay;
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