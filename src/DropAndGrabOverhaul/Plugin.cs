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
using Unity.Netcode;

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
    private static Coroutine? chuteFinalizeQueueCoroutine;
    private static bool isPlacingOnCounter;
    private static bool isDroppingAll;
    private static bool isStoringInChute;

    // Floor for how long a chute-stored item stays visible/audible before despawning, regardless
    // of how short the configured pacing delay is, so its drop SFX never gets cut off.
    private const float MinSettleTime = 1f;

    // How long to wait for a chute-stored item to actually despawn over the network before giving
    // up and logging it as a potential leftover duplicate (see ProcessChuteFinalizeQueue).
    private const float ChuteDestroyConfirmTimeout = 5f;

    // If the item hasn't despawned this long after the finalize request was (re)sent, assume that
    // message was lost and resend it, rather than waiting out the full ChuteDestroyConfirmTimeout
    // on a single lost packet.
    private const float ChuteDestroyRetryInterval = 1f;
    private const int ChuteDestroyMaxRetries = 3;

    // How long to wait for a real, player-initiated grab (isGrabbingObjectAnimation) to finish
    // before giving up and processing the item anyway (see StoreAllInChuteCoroutine) - bounds the
    // wait so a stuck flag from some unrelated bug can't hang the auto-store sequence forever.
    private const float GrabAnimationWaitTimeout = 3f;

    // Named message used to ask the host to add a chute-stored item to the ship inventory and
    // despawn it (see HostFinalizeChuteItem) - despawning a NetworkObject only ever requires
    // being the server, never ownership of the object, so routing this through the host avoids
    // the unreliable client -> host -> everyone DestroyItemInSlotAndSync round trip non-host
    // owners depended on before.
    private const string ChuteFinalizeMessageName = "DropAndGrabOverhaul_ChuteFinalize";

    // Items waiting to be added to the ship inventory and despawned once their settle time is up,
    // processed one at a time by ProcessChuteFinalizeQueue regardless of how fast new items are
    // still falling in StoreAllInChuteCoroutine. CarryWeightAlreadyDeducted is true for the item
    // that was actually equipped when detached (its weight was already subtracted by vanilla's own
    // DiscardHeldObject/SetObjectAsNoLongerHeld - see StoreAllInChuteCoroutine).
    private static readonly Queue<(GrabbableObject Item, int Slot, string ItemName, float ReadyAtTime, bool CarryWeightAlreadyDeducted)> chuteFinalizeQueue = new();

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
            return;
        }

        // Don't allow drop-all actions if player is at desk and auto-sell is enabled
        if (atDesk && SellConfiguration.AutoSellInventory)
            return;

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
        // ourselves naturally leaves ItemOnlySlot untouched.
        // Only start the coroutine if one isn't already running, otherwise it never
        // gets past the first item's delay before being killed and restarted
        if (dropAllCoroutine == null)
            dropAllCoroutine = runner!.StartCoroutine(DropAllItemsCoroutine(player, itemsToDropList, isForceDropping));
    }

    private static System.Collections.IEnumerator DropAllItemsCoroutine(PlayerControllerB player, List<GrabbableObject> itemsToDrop, bool isForceDropping)
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
        dropAllCoroutine = null;
    }

    private static System.Collections.IEnumerator AutoSellInventoryCoroutine(PlayerControllerB player, DepositItemsDesk desk)
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
        autoSellCoroutine = null;
    }

    private static System.Collections.IEnumerator StoreAllInChuteCoroutine(PlayerControllerB player)
    {
        Vector3 chutePosition = player.hoveringOverTrigger.transform.position;

        int storedCount = 0;
        // Re-check the player's slots every iteration (instead of snapshotting once up front) so
        // items grabbed after this coroutine already started still get caught and stored too,
        // rather than only ever processing whatever was held at the very start. Items are tracked
        // by reference (not slot index) once claimed since their slot doesn't actually go null
        // until ProcessChuteFinalizeQueue destroys it later - without this, the same still-pending
        // item would keep getting reselected every iteration until then.
        var processedItems = new HashSet<GrabbableObject>();
        while (true)
        {
            // Configurable early-out: jumping or falling (e.g. off the elevated ship) while items
            // are still being stored cancels the rest of the sequence rather than continuing to
            // store items out from under the player.
            if (IsJumpingOrFalling(player))
            {
                Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
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
            // isGrabbingObjectAnimation. Detaching it out from under that coroutine right now
            // (below, via currentlyHeldObjectServer = null) would break the condition that
            // coroutine's own wait loop is watching (`currentlyGrabbingObject != currentlyHeldObjectServer`)
            // and it would never become true again - leaving isGrabbingObjectAnimation stuck true
            // forever, which freezes the hotbar since scrolling/dropping/activating all gate on
            // that flag. Wait it out first, bounded so a stuck flag can't hang this coroutine
            // forever either.
            float grabWait = 0f;
            while (player.isGrabbingObjectAnimation && player.currentlyGrabbingObject == item && grabWait < GrabAnimationWaitTimeout)
            {
                yield return null;
                grabWait += Time.deltaTime;
            }

            processedItems.Add(item);

            string itemName = item.itemProperties.itemName;
            bool isEquippedItem = player.currentlyHeldObjectServer == item;

            if (isEquippedItem)
            {
                // Routes through the real vanilla drop/place path (the same call this mod's own
                // auto-sell placement already uses) instead of manually mimicking its side effects
                // - only this path actually networks the detach (ItemSlots, isHeld, physics/mesh,
                // animator pose, icons, control tip, twoHanded, carryWeight) to every other client
                // via ThrowObjectServerRpc/ThrowObjectClientRpc. Manually mimicking those fields
                // only ever changed them on the local dropper's own client, so every other client
                // (including the host) kept seeing this player still holding the item in their hand
                // - reported as "the host sees a non-host player holding an item, but that player
                // says they aren't holding anything". isStoringInChute bypasses
                // DiscardHeldObjectPrefix, which would otherwise suppress this call the same way it
                // suppresses a real G-press.
                isStoringInChute = true;
                try
                {
                    player.DiscardHeldObject(placeObject: true, parentObjectTo: null, placePosition: chutePosition, matchRotationOfParent: false);
                }
                finally
                {
                    isStoringInChute = false;
                }
            }
            else
            {
                // Not currently equipped, so nobody but this client could ever see it anyway -
                // vanilla only ever renders whichever single item is actively equipped; every other
                // slot's item has its meshes disabled for every client, the same before and after
                // this mod's involvement. Manually revealing+dropping it is purely a cosmetic
                // preview for the local dropper's own view, matching how it behaved before.
                item.playerHeldBy = null;
                item.isHeld = false;
                item.isPocketed = false;
                item.parentObject = null;
                item.transform.SetParent(null, true);
                item.EnablePhysics(true);
                item.EnableItemMeshes(true);
                item.startFallingPosition = item.transform.position;
                item.targetFloorPosition = chutePosition;
                item.fallTime = 0f;
                HUDManager.Instance.itemSlotIcons[slot].enabled = false;

                // Vanilla's DestroyItemInSlot only resets `twoHanded` (and its HUD icon) when the
                // destroyed item is the actively-equipped one - since only one two-handed item can
                // ever be held at a time, storing one here without this would leave `twoHanded`
                // stuck true forever, permanently blocking BeginGrabObject() from grabbing anything
                // else at all - "hands full" forever. Not a concern in the isEquippedItem branch
                // above: DiscardHeldObject's own SetObjectAsNoLongerHeld already resets it there.
                if (item.itemProperties.twoHanded)
                {
                    player.twoHanded = false;
                    player.twoHandedAnimation = false;
                    HUDManager.Instance.holdingTwoHandedItem.enabled = false;
                }
            }

            // Pacing before the next item starts falling can be configured very short, but the
            // item itself still needs enough time on screen for its fall animation and drop SFX to
            // finish before it's destroyed - so that part always waits at least MinSettleTime.
            float pacingDelay = StartOfRound.Instance.shipHasLanded ? ShipInventoryConfiguration.StoreDelayLanded : ShipInventoryConfiguration.StoreDelayOrbit;
            float settleDelay = Mathf.Max(pacingDelay, MinSettleTime);
            chuteFinalizeQueue.Enqueue((item, slot, itemName, Time.time + settleDelay, isEquippedItem));
            if (chuteFinalizeQueueCoroutine == null)
                chuteFinalizeQueueCoroutine = runner!.StartCoroutine(ProcessChuteFinalizeQueue(player));
            storedCount++;

            // Waited out frame-by-frame (instead of a single WaitForSeconds) so a jump/fall can
            // still be caught mid-wait - isJumping only stays true for ~0.25s after the jump key
            // is pressed, easy to miss entirely if pacingDelay is longer than that and this only
            // polled once per item.
            float waited = 0f;
            while (waited < pacingDelay)
            {
                if (IsJumpingOrFalling(player))
                {
                    Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                    goto stoppedByJump;
                }
                yield return null;
                waited += Time.deltaTime;
            }
        }

        stoppedByJump:
        if (storedCount > 0)
            StartOfRound.Instance.SendChangedWeightEvent();

        storeInChuteCoroutine = null;
    }

    // isJumping is only true for the initial ~0.25s takeoff window (see the game's own PlayerJump
    // coroutine); isFallingFromJump covers the rest of that arc until landing; isFallingNoJump
    // covers falling off a ledge/the elevated ship without jumping at all.
    private static bool IsJumpingOrFalling(PlayerControllerB player)
    {
        return ShipInventoryConfiguration.StopOnJump && (player.isJumping || player.isFallingFromJump || player.isFallingNoJump);
    }

    // Finalizes queued chute items one at a time, regardless of how fast StoreAllInChuteCoroutine
    // paces new items falling. Actually adding to the ship inventory and despawning now happens
    // entirely on the host (see HostFinalizeChuteItem) - this only sends the request and waits to
    // confirm the item actually disappeared, resending if it hasn't within a timeout, since a
    // reliable-channel message can still be lost if e.g. this client's connection is dying.
    private static System.Collections.IEnumerator ProcessChuteFinalizeQueue(PlayerControllerB player)
    {
        while (chuteFinalizeQueue.Count > 0)
        {
            (GrabbableObject item, int _, string itemName, float readyAtTime, bool carryWeightAlreadyDeducted) = chuteFinalizeQueue.Dequeue();

            float wait = readyAtTime - Time.time;
            if (wait > 0f)
                yield return new WaitForSeconds(wait);

            if (item == null)
                continue;

            // Items that were actually equipped when detached already had their weight subtracted
            // by vanilla's own DiscardHeldObject/SetObjectAsNoLongerHeld (see
            // StoreAllInChuteCoroutine) - only non-equipped (pocketed) items still need it here.
            if (!carryWeightAlreadyDeducted)
                player.carryWeight = Mathf.Clamp(player.carryWeight - (item.itemProperties.weight - 1f), 1f, 10f);

            SendChuteFinalizeRequest(item);

            float elapsed = 0f;
            int retries = 0;
            while (item != null && elapsed < ChuteDestroyConfirmTimeout)
            {
                yield return null;
                elapsed += Time.deltaTime;

                if (item != null && retries < ChuteDestroyMaxRetries && elapsed >= (retries + 1) * ChuteDestroyRetryInterval)
                {
                    retries++;
                    Plugin.Log.LogWarning($"'{itemName}' hasn't despawned {elapsed:F1}s after requesting chute storage - resending finalize request (attempt {retries}/{ChuteDestroyMaxRetries})");
                    SendChuteFinalizeRequest(item);
                }
            }

            if (item != null)
                Plugin.Log.LogWarning($"'{itemName}' did not despawn within {ChuteDestroyConfirmTimeout}s of requesting chute storage - it may remain visible/pickup-able; try storing it manually via the chute's [E] interact.");
            else
                Plugin.Log.LogInfo($"Confirmed '{itemName}' was stored into the ship inventory chute.");
        }

        chuteFinalizeQueueCoroutine = null;
    }

    // Asks the host to add the item to the ship inventory and despawn it (see
    // HostFinalizeChuteItem). Safe to call more than once for the same item, e.g. a retry - the
    // host's handler is a no-op for anything already despawned. Skips the network message
    // entirely when this client already is the host, since there's nothing to round-trip.
    private static void SendChuteFinalizeRequest(GrabbableObject item)
    {
        if (NetworkManager.Singleton == null || item.NetworkObject == null)
            return;

        if (NetworkManager.Singleton.IsHost)
        {
            HostFinalizeChuteItem(item);
            return;
        }

        using FastBufferWriter writer = new(sizeof(ulong), Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(item.NetworkObject.NetworkObjectId);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(ChuteFinalizeMessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
    }

    // Host-only handler for ChuteFinalizeMessageName (see StartOfRoundAwakePostfix, only
    // registered when IsHost). Resolves the requested item and hands it to
    // HostFinalizeChuteItem - never touches PlayerControllerB.ItemSlots/DestroyItemInSlotAndSync
    // at all, sidestepping their ownership requirement entirely (see CLAUDE.md).
    private static void OnChuteFinalizeMessageReceived(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong networkObjectId);

        // Not found means either a duplicate resend arriving after the first request already
        // despawned it, or a genuinely stale/invalid id - either way, nothing to do.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            return;

        GrabbableObject? item = netObj.GetComponent<GrabbableObject>();
        if (item != null)
            HostFinalizeChuteItem(item);
    }

    // Adds the item to the ship inventory and despawns it, entirely locally on the host - no
    // network round trip needed, since NetworkObject.Despawn only ever requires being the server
    // (never ownership of the object), and Inventory.Add already routes through its own ownerless
    // ServerRpc regardless of who's calling it.
    private static void HostFinalizeChuteItem(GrabbableObject item)
    {
        // isHeld is only trustworthy to read here because a real grab always goes through
        // GrabObjectServerRpc/ClientRpc, which the host does see - unlike the plain field writes
        // StoreAllInChuteCoroutine makes locally on the detaching client (see CLAUDE.md).
        if (item.isHeld)
        {
            Plugin.Log.LogInfo($"Skipping chute finalize for '{item.itemProperties.itemName}' - it's been picked back up since the request was sent.");
            return;
        }

        string itemName = item.itemProperties.itemName;
        ShipInventoryUpdated.Objects.ItemData shipInventoryData;
        try
        {
            shipInventoryData = ShipInventoryCompat.AddToShipInventory(item, StartOfRound.Instance.localPlayerController);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Adding '{itemName}' to the ship inventory threw ({e.GetType().Name}: {e.Message}) - it was left in the world instead of being stored.");
            return;
        }

        try
        {
            item.NetworkObject.Despawn();
            Plugin.Log.LogInfo($"Stored item into ship inventory chute: {itemName}");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"'{itemName}' was added to the ship inventory but despawning it threw ({e.GetType().Name}: {e.Message}) - it may remain visible/pickup-able as a duplicate.");
            ShipInventoryCompat.RemoveFromShipInventory(shipInventoryData);
        }
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

        // Only the host ever needs to receive chute finalize requests - registering this on
        // every client would be harmless (it would just never get called) but there's no reason
        // to. Safe to call again on every level load: it just replaces the same handler.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(ChuteFinalizeMessageName, OnChuteFinalizeMessageReceived);
        }
    }

    // Harmony patch to intercept the game's default drop call
    [HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
    [HarmonyPrefix]
    private static bool DiscardHeldObjectPrefix(PlayerControllerB __instance)
    {
        // Never suppress our own sell-placement, drop-all, or chute-store calls
        if (isPlacingOnCounter || isDroppingAll || isStoringInChute)
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
