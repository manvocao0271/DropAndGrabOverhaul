using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul.Compatibility;

/// <summary>
/// Optional integration with the ShipInventoryUpdated mod (adds a "chute" players can store
/// items in). This is a soft dependency: DropAndGrabOverhaul compiles against
/// ShipInventoryUpdated's assembly (see the PackageReference in the .csproj, which is
/// compile-only and never bundled or listed as a Thunderstore dependency), but every member here
/// that touches its types must only ever be called after checking <see cref="IsLoaded"/>. The
/// .NET JIT only resolves a method's type references when that method is actually invoked, so as
/// long as callers respect that guard, this class never faults for users who don't have
/// ShipInventoryUpdated installed.
///
/// DESIGN: this class used to reimplement the store transaction itself (build an ItemData,
/// call Inventory.Add, flip isInShipRoom/scrapPersistedThroughRounds, despawn) and relied on
/// Plugin.cs to run that host-side and relay non-host requests to the host with a retry timeout.
/// That's gone, now that we've seen ShipInventoryUpdated's actual source (ChuteStore.cs,
/// ChuteTrigger.cs, Inventory.cs):
///
/// ChuteTrigger doesn't override Interact() at all (only Start/Update, for its hover tooltip and
/// per-frame InteractionHelper.SetTriggerStatus gating) - it inherits vanilla InteractTrigger's
/// Interact(Transform) unmodified (confirmed independently - see AttemptStore's remarks; a first
/// guess that it took a PlayerControllerB directly didn't compile). ChuteStore.Start() is what
/// wires the actual behaviour in: `trigger.onInteract.AddListener(StoreHeldItem)`. So a completed
/// [E]-hold really runs vanilla's own Interact(), which resolves a PlayerControllerB from the
/// Transform it's given and invokes onInteract with it, which calls
/// ChuteStore.StoreHeldItem(player) - a private static method we can't call directly, but don't
/// need to: calling Interact() on the same trigger the player is hovering fires the exact same
/// listener.
///
/// Confirmed from ChuteStore.StoreHeldItem's actual body: it does ItemConverter.Convert (returns
/// ItemData[], confirming it still special-cases BeltBagItem the way our old hand-rolled ItemData
/// construction couldn't) -> Inventory.Add (confirmed `[ServerRpc(RequireOwnership = false)]` in
/// Inventory.cs, so any client can call it) -> player.SetItemInElevator(true, true, item) -> then
/// SEPARATELY player.DestroyItemInSlotAndSync(player.currentItemSlot) to actually clear the slot,
/// fix carry weight, and despawn/sync the item - the same vanilla method used elsewhere for
/// destroying/consuming a slotted item. That whole chain runs unconditionally from whichever
/// client calls Interact(), host or not, because that's what a manual store already does today.
///
/// Also confirmed: StoreHeldItem performs NO blacklist/Store-Permission/Only-In-Orbit check of its
/// own - those are only ever enforced upstream, by ChuteTrigger.Update() calling
/// InteractionHelper.SetTriggerStatus every frame to keep the trigger's own `interactable` flag
/// (and hover tooltip) in sync with whatever the player currently holds. That makes our own
/// `interactable` check in AttemptStore below load-bearing, not just a nicety -
/// skip it and we'd store blacklisted items unconditionally, since nothing downstream would catch
/// that for us.
/// </summary>
internal static class ShipInventoryCompat
{
    private const string PluginGuid = "org.lethalcompanymodding.shipinventoryupdated";

    public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(PluginGuid);

    /// <summary>Whether the player is currently hovering the ship inventory chute's interact trigger.</summary>
    public static bool IsHoveringChute(PlayerControllerB player)
    {
        return player.hoveringOverTrigger is ShipInventoryUpdated.Scripts.ChuteTrigger;
    }

    /// <summary>
    /// True if the given trigger is specifically ShipInventoryUpdated's chute - lets Plugin's
    /// global InteractTriggerInteractPrefix patch (which fires for every InteractTrigger in the
    /// game, not just the chute) decide whether to also override this one's cooldownTime,
    /// without that patch needing to reference ShipInventoryUpdated's types directly. Callers
    /// should check IsLoaded first, same as everywhere else in this class.
    /// </summary>
    public static bool IsChuteTriggerInstance(InteractTrigger trigger)
    {
        return trigger is ShipInventoryUpdated.Scripts.ChuteTrigger;
    }

    /// <summary>
    /// Captures a reusable handle to the chute the player is currently hovering, if any, so a
    /// caller can keep calling <see cref="AttemptStore"/> against the same chute across many
    /// frames without the player needing to keep looking at it - only this initial acquisition
    /// needs the hover. Typed as `object` rather than ChuteTrigger specifically so Plugin.cs,
    /// which this class exists to shield from ever referencing ShipInventoryUpdated's types
    /// directly, can hold and pass one around without its own IL ever mentioning ChuteTrigger -
    /// preserving the IsLoaded-gated JIT safety described in this class's remarks.
    /// </summary>
    public static object? AcquireHoveredChute(PlayerControllerB player)
    {
        return player.hoveringOverTrigger as ShipInventoryUpdated.Scripts.ChuteTrigger;
    }

    /// <summary>
    /// True if a handle from <see cref="AcquireHoveredChute"/> still points at a live chute.
    /// Unity's own destroyed-object equality check covers the object being despawned/replaced
    /// (e.g. a scene transition) out from under a caller holding this across several frames -
    /// something a fresh player.hoveringOverTrigger read wouldn't need to worry about, since it
    /// re-derives the reference every time rather than holding one.
    /// </summary>
    public static bool IsChuteHandleValid(object? chuteHandle)
    {
        return chuteHandle is ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger && chuteTrigger != null;
    }

    /// <summary>
    /// True if the chute's own vanilla InteractTrigger cooldown is still counting down from a
    /// previous successful Interact() call. cooldownTime/currentCooldownValue are plain vanilla
    /// InteractTrigger fields, not ShipInventoryUpdated-specific - already confirmed to exist and
    /// be directly accessible by Plugin's own InteractTriggerInteractPrefix patch, which reads
    /// and writes __instance.cooldownTime on this same base type. Interact() sets
    /// currentCooldownValue from cooldownTime on every successful call; calling Interact() again
    /// before that clears is a silent no-op - no exception, no store - which is what produced an
    /// "every other item" pattern once a configured pacing delay (StoreDelayOrbit's short 0.2s
    /// default especially) turned out to be shorter than this cooldown. Callers should wait for
    /// this to clear before calling AttemptStore again, rather than relying on their own pacing
    /// delay alone to happen to be long enough.
    /// </summary>
    public static bool IsChuteOnCooldown(object? chuteHandle)
    {
        return chuteHandle is ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger
            && chuteTrigger != null
            && chuteTrigger.currentCooldownValue > 0f;
    }

    /// <summary>
    /// Every item currently in the player's slots that isn't obviously blacklisted, paired with
    /// its slot index, in slot order. This is only a cheap pre-filter to avoid equipping and
    /// hovering-checking an item we already know is rejected - it intentionally doesn't need to
    /// be exhaustive, because <see cref="AttemptStore"/> re-checks the real
    /// ChuteTrigger.interactable flag (which also covers Store Permission and Only In Orbit,
    /// neither of which this regex-based pre-filter knows about) right before actually storing.
    /// </summary>
    public static List<(int Slot, GrabbableObject Item)> GetStorableItems(PlayerControllerB player)
    {
        string? blacklist = GetChuteBlacklistRaw();
        var result = new List<(int, GrabbableObject)>();

        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            GrabbableObject item = player.ItemSlots[i];
            if (item == null)
                continue;

            if (IsChuteBlacklisted(item.itemProperties.itemName, blacklist))
                continue;

            result.Add((i, item));
        }

        return result;
    }

    /// <summary>
    /// Whether <see cref="AttemptStore"/> actually invoked ChuteTrigger's real store transaction,
    /// and if so, whether it completed without throwing. Deliberately doesn't promise the item
    /// was actually removed - Interact() can return (or throw) before a non-host client's
    /// Inventory.Add/DestroyItemInSlotAndSync round trip has actually landed, so even
    /// <see cref="Called"/> callers still need to watch the item for a short window afterward.
    /// </summary>
    public enum ChuteStoreAttempt
    {
        /// <summary>The chute won't currently accept this item (blacklist, Store Permission, Only In Orbit, or the handle is no longer valid) - Interact() was never called.</summary>
        NotAllowed,

        /// <summary>Interact() was called and returned normally.</summary>
        Called,

        /// <summary>
        /// Interact() itself threw. Inventory.Add runs before the despawn step inside
        /// StoreHeldItem (see class remarks), so if the exception came from that later half, the
        /// item may already be logically stored even though it's still physically in the
        /// player's hands - callers should warn rather than silently retrying it, since that
        /// risks a second Inventory.Add for the same physical item. Not the utility-slot
        /// DestroyItemInSlot bug JacobG5's DestroyItemInSlotFix patches - GetStorableItems only
        /// ever selects items from player.ItemSlots, which doesn't include the utility slot, so
        /// player.currentItemSlot can't be pointing at it by the time this runs. The real cause
        /// is unconfirmed; log whatever the exception says rather than guessing at one.
        /// </summary>
        Threw
    }

    /// <summary>
    /// Stores whatever the player is currently holding, exactly as if they'd hovered the chute
    /// and held [E] until it completed. Takes a handle from <see cref="AcquireHoveredChute"/>
    /// rather than re-deriving one from player.hoveringOverTrigger itself, specifically so this
    /// keeps working across many calls without the player needing to keep looking at the chute -
    /// callers should still check <see cref="IsChuteHandleValid"/> first. Callers are also
    /// responsible for having already equipped the item (see StoreAllInChuteCoroutine) - a manual
    /// store requires that too, since ChuteStore.StoreHeldItem reads
    /// player.currentlyHeldObjectServer, not any item we could pass in directly.
    ///
    /// STILL WORTH CONFIRMING (we have ChuteStore.cs/ChuteTrigger.cs but not
    /// InteractionHelper.cs, which owns the actual gating logic):
    /// - The exact field InteractionHelper.SetTriggerStatus writes to communicate "not currently
    ///   allowed" - assumed to be `interactable`, since that's the field vanilla's own hover/
    ///   highlight system already reads and it's inherited from InteractTrigger, not
    ///   ShipInventoryUpdated-specific.
    /// - ChuteTrigger.Update() only refreshes `interactable` while player.isInHangarShipRoom is
    ///   true (confirmed from ChuteTrigger.cs) - which is also why the caller's loop now checks
    ///   that same field to decide when to stop, instead of requiring the player to keep hovering.
    ///
    /// CONFIRMED (previously assumed, now verified against real signatures/decompiled callers):
    /// - Interact takes a Transform, not a PlayerControllerB - see the call site below for the
    ///   independent confirmation. The compiler caught the first wrong guess here (a
    ///   PlayerControllerB overload doesn't exist) before it could ship.
    /// </summary>
    public static ChuteStoreAttempt AttemptStore(object chuteHandle, PlayerControllerB player, out Exception? exception)
    {
        exception = null;

        if (chuteHandle is not ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger || chuteTrigger == null)
            return ChuteStoreAttempt.NotAllowed;

        // Load-bearing, not just a mirror of "would a real hover prompt allow this": StoreHeldItem
        // has no blacklist/Store-Permission/Only-In-Orbit check of its own (confirmed from its
        // actual body) - this flag, kept in sync every frame (while player.isInHangarShipRoom -
        // see remarks above) by ChuteTrigger.Update() calling InteractionHelper.SetTriggerStatus,
        // is the only place any of those rules are enforced.
        if (!chuteTrigger.interactable)
            return ChuteStoreAttempt.NotAllowed;

        // The exact call a completed manual hold makes. ChuteTrigger doesn't override this -
        // vanilla's own Interact(Transform) runs, invokes onInteract, and ChuteStore.StoreHeldItem
        // (a listener registered on that same event in ChuteStore.Start()) does the rest: converts
        // the item ShipInventoryUpdated's own way (preserving its BeltBagItem special case),
        // calls its own ownerless-RPC Inventory.Add, then player.DestroyItemInSlotAndSync to
        // clear the slot, fix carry weight, and despawn - the same for any caller, host or not.
        //
        // Interact takes the interacting player's Transform, not the PlayerControllerB itself -
        // confirmed via ShipVoiceCommands' decompiled source, which calls this exact vanilla
        // method the same way for an unrelated purpose:
        // trigger.Interact(((Component)GameNetworkManager.Instance.localPlayerController).transform)
        // onInteract's own listener (StoreHeldItem) still receives a PlayerControllerB - Interact
        // resolves that internally from the Transform we hand it, most likely via GetComponent
        // since PlayerControllerB lives on the same GameObject as its own transform.
        //
        // Wrapped because we're calling this programmatically rather than through the input path
        // it's normally only reached from, and because DestroyItemInSlotAndSync is a real,
        // documented vanilla footgun (see ChuteStoreAttempt.Threw) - an uncaught exception here
        // would otherwise kill this whole coroutine mid-batch, silently abandoning every item
        // still waiting behind whichever one triggered it.
        try
        {
            chuteTrigger.Interact(player.transform);
            return ChuteStoreAttempt.Called;
        }
        catch (Exception e)
        {
            exception = e;
            return ChuteStoreAttempt.Threw;
        }
    }

    // Mirrors ShipInventoryUpdated.Helpers.API.InteractionHelper.IsAllowed (internal, and only
    // ever enforced there by disabling the chute's InteractTrigger for the single currently-held
    // item) so our own batch-store path respects the same user-configured blacklist.
    private static bool IsChuteBlacklisted(string itemName, string? blacklistRaw)
    {
        if (string.IsNullOrEmpty(blacklistRaw))
            return false;

        string name = itemName.ToLowerInvariant();
        foreach (string entry in blacklistRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string pattern = "^" + entry.Trim().ToLowerInvariant() + "$";
            try
            {
                if (Regex.IsMatch(name, pattern))
                    return true;
            }
            catch (ArgumentException)
            {
                // Malformed regex in the user's own ShipInventoryUpdated config - skip it rather
                // than throwing, same as a no-match would behave.
            }
        }

        return false;
    }

    // Configuration/ChuteConfig are internal types in ShipInventoryUpdated, so their instance and
    // fields have to be fetched via reflection rather than typed access - reflection ignores
    // C#-level accessibility, so this still works despite that.
    private static string? GetChuteBlacklistRaw()
    {
        try
        {
            Type? configType = Type.GetType("ShipInventoryUpdated.Configurations.Configuration, ShipInventoryUpdated");
            // Instance is a public static field, not a property - GetProperty silently returns
            // null here, which was making every item look non-blacklisted.
            object? instance = configType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object? chute = instance != null
                ? configType!.GetField("Chute", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance)
                : null;
            object? blacklistEntry = chute?.GetType().GetField("Blacklist", BindingFlags.Public | BindingFlags.Instance)?.GetValue(chute);
            return blacklistEntry?.GetType().GetProperty("Value")?.GetValue(blacklistEntry) as string;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not read ShipInventoryUpdated's chute blacklist config, treating it as empty: {e.Message}");
            return null;
        }
    }
}