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
    /// Every non-blacklisted item currently in the player's slots, paired with its slot index, in
    /// slot order. Doesn't touch the ShipInventoryUpdated inventory or despawn anything itself -
    /// callers drive the actual storing (see <see cref="FinalizeStoredItem"/>) one item at a time.
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
    /// Adds a single item's data to the ship inventory, mirroring what
    /// ShipInventoryUpdated.Scripts.ChuteStore.StoreHeldItem does for a single held item when
    /// holding [E] on the chute. Assumes the caller has already handled any visual/weight/HUD
    /// side effects for taking the item out of the player's hands - this only owns the
    /// ShipInventoryUpdated-side bookkeeping. Split out from the actual despawn (see
    /// <see cref="TryDestroyItemInSlot"/>) so a caller retrying/awaiting the despawn never risks
    /// adding the same item's data twice.
    /// </summary>
    public static void AddToShipInventory(GrabbableObject item, PlayerControllerB player)
    {
        // Using ItemData's public constructor directly instead of ShipInventoryUpdated's own
        // (internal) ItemConverter.Convert - loses its BeltBagItem unpacking special-case,
        // but covers the general "store this item" path the same way.
        var data = new ShipInventoryUpdated.Objects.ItemData(item, addSaveData: true);
        ShipInventoryUpdated.Scripts.Inventory.Add(new[] { data });

        // Order matters: forces SetItemInElevator to see a state change, but with
        // scrapPersistedThroughRounds already true it skips awarding profit/quota credit -
        // storing in the chute isn't the same as delivering it to be sold. Same order
        // ChuteStore.StoreHeldItem uses.
        item.isInShipRoom = false;
        item.scrapPersistedThroughRounds = true;
        player.SetItemInElevator(true, true, item);
    }

    /// <summary>
    /// Despawns an already-detached item by slot index, mirroring what
    /// ShipInventoryUpdated.Scripts.ChuteStore.StoreHeldItem does. Unlike DiscardHeldObject, this
    /// doesn't touch currentlyHeldObjectServer, so there's no equip-first/network-echo race to
    /// worry about here (see repo memory on the drop-all netcode race for contrast) - but the
    /// despawn itself still depends on a client -> host -> everyone network round trip
    /// (DestroyItemInSlotAndSync only despawns immediately for the host; every other client
    /// waits on that round trip), which can silently fail to complete under lag. Callers are
    /// expected to wait for the item to actually go away and log/handle it if it doesn't (see
    /// StoreAllInChuteCoroutine's queue in Plugin.cs), rather than assuming this call alone is
    /// enough.
    /// </summary>
    /// <returns>
    /// False if the item could no longer be found in any of the player's slots (e.g. the slot
    /// index went stale across a delay) - in that case nothing was destroyed, and the caller
    /// should not expect the item to ever disappear.
    /// </returns>
    public static bool TryDestroyItemInSlot(PlayerControllerB player, GrabbableObject item, int slot)
    {
        // The slot captured when this item started falling might not hold it anymore by the time
        // this actually runs - resolve its real current slot instead of blindly trusting the
        // stale index, so this can never end up destroying whatever unrelated item happens to
        // occupy that slot number now.
        int actualSlot = slot >= 0 && slot < player.ItemSlots.Length && player.ItemSlots[slot] == item
            ? slot
            : Array.IndexOf(player.ItemSlots, item);

        if (actualSlot < 0)
            return false;

        player.DestroyItemInSlotAndSync(actualSlot);
        return true;
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
