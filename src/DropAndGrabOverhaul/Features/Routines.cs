using System.Collections;
using System.Collections.Generic;
using DropAndGrabOverhaul.Compatibility;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Inventory;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul.Features;

// Allows at most one instance of a routine at a time. The flag is set before the coroutine starts
// and cleared in a finally, never derived from the Coroutine handle: a routine that finishes
// without yielding completes inside StartCoroutine and would leave a handle-based gate stuck
// "busy" (CLAUDE.md gotcha #6).
internal sealed class CoroutineGate
{
    private bool running;

    public bool IsRunning => running;

    public void Start(MonoBehaviour host, IEnumerator routine)
    {
        if (running || !host.isActiveAndEnabled)
            return;

        running = true;
        try
        {
            host.StartCoroutine(Run(routine));
        }
        catch
        {
            running = false;
            throw;
        }
    }

    private IEnumerator Run(IEnumerator routine)
    {
        try
        {
            while (routine.MoveNext())
                yield return routine.Current;
        }
        finally
        {
            running = false;
        }
    }
}

internal static class AutoSellRoutine
{
    private const float DelayBetweenSalesSeconds = 0.2f;

    // Places every eligible scrap item on the counter, one at a time. Main hotbar only:
    // reserved slots are never sold.
    public static IEnumerator Run(PlayerControllerB player, DepositItemsDesk desk)
    {
        foreach ((int slot, GrabbableObject item) in InventoryAccessor.GetItemSlots(player, includeReservedSlots: false))
        {
            // The list is a snapshot but this loop yields: re-check the slot still holds this item.
            if (item == null || slot >= player.ItemSlots.Length || player.ItemSlots[slot] != item || !item.itemProperties.isScrap)
                continue;

            if (SellConfiguration.IsSellBlacklisted(item.itemProperties.itemName))
                continue;

            player.SwitchToItemSlot(slot);
            desk.PlaceItemOnCounter(player);

            yield return new WaitForSeconds(DelayBetweenSalesSeconds);
        }
    }
}

internal static class DropAllRoutine
{
    // Both idle waits are bounded so a stuck flag can never leave the CoroutineGate busy forever.
    private const float IdleTimeoutSeconds = 3f;

    // Drops every (slot, item) pair in order, then restores the original hotbar slot.
    public static IEnumerator Run(PlayerControllerB player, List<(int Slot, GrabbableObject Item)> items, bool ignoreBlacklist)
    {
        int originalSlot = player.currentItemSlot;
        bool droppedReservedItem = false;
        bool abortedEarly = false;

        foreach ((int slot, GrabbableObject item) in items)
        {
            // Wait *before* acting: the previous drop may still be waiting for its
            // ThrowObjectClientRpc echo, and switching slots first corrupts
            // currentlyHeldObjectServer (CLAUDE.md gotcha #5).
            float waitStart = Time.time;
            while (IsBusy(player) && !IsPlayerGone(player) && Time.time - waitStart < IdleTimeoutSeconds)
                yield return null;

            if (IsPlayerGone(player))
                yield break;

            if (IsBusy(player))
            {
                ModLog.Warning("Timed out waiting for the previous grab/drop to finish - stopping drop-all early to avoid desyncing the rest.");
                abortedEarly = true;
                break;
            }

            // The list is a snapshot but this loop yields: re-check the slot still holds this item.
            if (item == null || slot >= player.ItemSlots.Length || player.ItemSlots[slot] != item)
                continue;

            if (!ignoreBlacklist && ItemBlacklist.IsBlacklisted(item.itemProperties.itemName))
                continue;

            droppedReservedItem |= InventoryAccessor.IsReservedSlot(player, slot);

            player.SwitchToItemSlot(slot);
            player.DiscardHeldObject();
        }

        // The last drop's echo is still outstanding; let it land before touching the slot again.
        if (!abortedEarly)
        {
            float waitStart = Time.time;
            while (IsBusy(player) && !IsPlayerGone(player) && Time.time - waitStart < IdleTimeoutSeconds)
                yield return null;

            if (IsPlayerGone(player))
                yield break;

            if (IsBusy(player))
            {
                ModLog.Warning("Timed out waiting for the last drop to finish over the network - leaving the selected slot alone.");
                abortedEarly = true;
            }
        }

        // ReservedItemSlotCore schedules its own slot switch after a reserved drop; let it run
        // first so it can't override the restore below (CLAUDE.md gotcha #10).
        if (droppedReservedItem)
        {
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        if (!abortedEarly)
            RestoreSlot(player, originalSlot);

        // RIS only refreshes its reserved-slot HUD for the first reserved drop of a sequence.
        if (droppedReservedItem)
            ReservedItemSlotCompat.RefreshHudAfterReservedDrop();
    }

    // A grab animation is playing, or a drop is still waiting for its network echo.
    private static bool IsBusy(PlayerControllerB player)
        => player.isGrabbingObjectAnimation || player.throwingObject;

    private static bool IsPlayerGone(PlayerControllerB player)
        => player == null || player.isPlayerDead || !player.isPlayerControlled;

    private static void RestoreSlot(PlayerControllerB player, int originalSlot)
    {
        int restoreSlot = originalSlot;

        // Don't go back to a reserved slot this drop just emptied; use the first hotbar slot.
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
