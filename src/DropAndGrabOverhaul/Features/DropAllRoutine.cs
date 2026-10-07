using System.Collections;
using System.Collections.Generic;
using DropAndGrabOverhaul.Compatibility;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Inventory;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul.Features;

internal static class DropAllRoutine
{
    // How long to wait for the player to become idle (no grab animation, no drop still waiting for
    // its network echo - CLAUDE.md gotcha #5) before giving up on the rest of the sequence. Both
    // waits are bounded so a stuck flag can never leave the caller's CoroutineGate busy forever.
    private const float IdleTimeoutSeconds = 3f;

    // Drops every (slot, item) pair in order, then restores the player's original hotbar slot.
    // Concurrency is handled by the caller's CoroutineGate.
    public static IEnumerator Run(PlayerControllerB player, List<(int Slot, GrabbableObject Item)> items, bool ignoreBlacklist)
    {
        int originalSlot = player.currentItemSlot;
        int droppedCount = 0;
        bool droppedReservedItem = false;
        bool abortedEarly = false;

        foreach ((int slot, GrabbableObject item) in items)
        {
            // Wait *before* acting, not after: the previous drop (this loop's, or the single tap
            // that opened a double-tap) may still be waiting for its ThrowObjectClientRpc echo, and
            // switching slots before it lands corrupts currentlyHeldObjectServer. Also lets a
            // grab animation finish first, since vanilla defers a discard made mid-grab.
            float waitStart = Time.time;
            while (IsBusy(player) && !IsPlayerGone(player) && Time.time - waitStart < IdleTimeoutSeconds)
                yield return null;

            if (IsPlayerGone(player))
            {
                ModLog.Info("Player died or lost control - stopping drop-all");
                yield break;
            }

            if (IsBusy(player))
            {
                ModLog.Warning("Timed out waiting for the previous grab/drop to finish - stopping drop-all early to avoid desyncing the rest.");
                abortedEarly = true;
                break;
            }

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
            player.DiscardHeldObject();
            droppedCount++;
            ModLog.Info($"Dropped item: {itemName}");
        }

        // The last drop's echo is still outstanding. Let it land before touching the slot again.
        if (!abortedEarly)
        {
            float waitStart = Time.time;
            while (IsBusy(player) && !IsPlayerGone(player) && Time.time - waitStart < IdleTimeoutSeconds)
                yield return null;

            if (IsPlayerGone(player))
            {
                ModLog.Info("Player died or lost control - stopping drop-all");
                yield break;
            }

            if (IsBusy(player))
            {
                ModLog.Warning("Timed out waiting for the last drop to finish over the network - leaving the selected slot alone.");
                abortedEarly = true;
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

        if (!abortedEarly)
            RestoreSlot(player, originalSlot);

        // RIS only refreshes its reserved-slot HUD for the first reserved item dropped in this
        // sequence (see ReservedItemSlotCompat.RefreshHudAfterReservedDrop for why); force one
        // more refresh now that every drop and the slot restore are done, so slots this loop
        // emptied after the first don't keep showing a stale frame.
        if (droppedReservedItem)
            ReservedItemSlotCompat.RefreshHudAfterReservedDrop();

        ModLog.Info($"Dropped {droppedCount} items total");
    }

    // A grab animation is playing, or a drop is still waiting for its network echo
    // (PlayerControllerB.throwingObject is set by DiscardHeldObject and cleared by the owner's
    // ThrowObjectClientRpc).
    private static bool IsBusy(PlayerControllerB player)
        => player.isGrabbingObjectAnimation || player.throwingObject;

    private static bool IsPlayerGone(PlayerControllerB player)
        => player == null || player.isPlayerDead || !player.isPlayerControlled;

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

        if (player.currentItemSlot != restoreSlot && player.currentlyHeldObjectServer == null && !player.throwingObject)
            player.SwitchToItemSlot(restoreSlot);
    }
}
