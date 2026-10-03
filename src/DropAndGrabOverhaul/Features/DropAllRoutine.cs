using System.Collections;
using System.Collections.Generic;
using DropAndGrabOverhaul.Compatibility;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Inventory;
using DropAndGrabOverhaul.Patches;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul.Features;

internal static class DropAllRoutine
{
    // How long to wait for the network echo that clears currentlyHeldObjectServer after a drop
    // (CLAUDE.md gotcha #5) before giving up on the rest of the sequence.
    private const float ThrowEchoTimeoutSeconds = 2f;

    // Drops every (slot, item) pair in order, then restores the player's original hotbar slot.
    // Concurrency is handled by the caller's CoroutineGate.
    public static IEnumerator Run(PlayerControllerB player, List<(int Slot, GrabbableObject Item)> items, bool ignoreBlacklist)
    {
        int originalSlot = player.currentItemSlot;
        int droppedCount = 0;
        bool droppedReservedItem = false;

        foreach ((int slot, GrabbableObject item) in items)
        {
            // Vanilla defers a discard made mid-grab-animation, which would leave the echo wait
            // below to time out and abort the sequence - let the grab finish first.
            while (player.isGrabbingObjectAnimation)
                yield return null;

            // The list is a snapshot, but this loop yields between drops - re-confirm the slot
            // still holds this exact item before acting on it.
            if (item == null || slot >= player.ItemSlots.Length || player.ItemSlots[slot] != item)
                continue;

            string itemName = item.itemProperties.itemName;

            if (!ignoreBlacklist && ItemBlacklist.IsBlacklisted(itemName))
            {
                ModLog.Info($"Skipping blacklisted item: {itemName}");
                continue;
            }

            droppedReservedItem |= InventoryAccessor.IsReservedSlot(player, slot);

            player.SwitchToItemSlot(slot);
            using (VanillaDiscard.Allow())
            {
                player.DiscardHeldObject();
            }
            droppedCount++;
            ModLog.Info($"Dropped item: {itemName}");

            float waitStart = Time.time;
            while (player.currentlyHeldObjectServer != null && Time.time - waitStart < ThrowEchoTimeoutSeconds)
                yield return null;

            if (player.currentlyHeldObjectServer != null)
            {
                ModLog.Warning($"Timed out waiting for '{itemName}' to finish dropping over the network - stopping drop-all early to avoid desyncing the rest.");
                break;
            }
        }

        // ReservedItemSlotCore reacts to a dropped reserved item by scheduling a switch to the
        // next reserved slot (or back to the hotbar). Those switches only run once the player's
        // hands are empty again - i.e. right after the last drop above - and would move the
        // player off the slot restored below. Let them run first.
        if (droppedReservedItem)
        {
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        RestoreSlot(player, originalSlot);

        // RIS only refreshes its reserved-slot HUD for the first reserved item dropped in this
        // sequence (see ReservedItemSlotCompat.RefreshHudAfterReservedDrop for why); force one
        // more refresh now that every drop and the slot restore are done, so slots this loop
        // emptied after the first don't keep showing a stale frame.
        if (droppedReservedItem)
            ReservedItemSlotCompat.RefreshHudAfterReservedDrop();

        ModLog.Info($"Dropped {droppedCount} items total");
    }

    private static void RestoreSlot(PlayerControllerB player, int originalSlot)
    {
        int restoreSlot = originalSlot;

        // Don't go back to a reserved slot that this drop just emptied; use the first hotbar slot.
        if (restoreSlot >= 0 && restoreSlot < player.ItemSlots.Length
            && InventoryAccessor.IsReservedSlot(player, restoreSlot)
            && player.ItemSlots[restoreSlot] == null)
        {
            restoreSlot = 0;
        }

        if (player.currentItemSlot != restoreSlot && player.currentlyHeldObjectServer == null)
            player.SwitchToItemSlot(restoreSlot);
    }
}
