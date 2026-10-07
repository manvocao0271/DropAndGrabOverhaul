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

// Helpers shared by the two routines.
internal static class RoutineSupport
{
    // Bounded so a stuck flag can never leave the CoroutineGate busy forever.
    private const float IdleTimeoutSeconds = 3f;

    // A grab animation is playing, or a drop/placement is still waiting for its network echo.
    public static bool IsBusy(PlayerControllerB player)
        => player.isGrabbingObjectAnimation || player.throwingObject;

    public static bool IsPlayerGone(PlayerControllerB player)
        => player == null || player.isPlayerDead || !player.isPlayerControlled;

    public static bool ShouldKeepWaiting(PlayerControllerB player, float waitStart)
        => IsBusy(player) && !IsPlayerGone(player) && Time.time - waitStart < IdleTimeoutSeconds;

    // Switches slot locally and tells the other clients the absolute slot, the same pair vanilla's
    // utility-slot key uses. A bare local SwitchToItemSlot leaves their copy of this player on the
    // old slot, so their ThrowObjectClientRpc finds a mismatched held object (CLAUDE.md gotcha #5).
    public static void SwitchToSlot(PlayerControllerB player, int slot)
    {
        bool changed = player.currentItemSlot != slot;
        player.SwitchToItemSlot(slot);

        if (changed)
            player.SwitchToSlotServerRpc(slot);
    }
}

internal static class AutoPlaceRoutine
{
    private const float DelayBetweenPlacementsSeconds = 0.2f;

    // Places every eligible scrap item on the counter, one at a time. Main hotbar only: reserved
    // slots are never placed. Leaves you on the slot of the last item placed.
    public static IEnumerator Run(PlayerControllerB player, DepositItemsDesk desk)
    {
        foreach ((int slot, GrabbableObject item) in InventoryAccessor.GetItemSlots(player, includeReservedSlots: false))
        {
            // Wait *before* acting: the previous placement may still be waiting for its
            // PlaceObjectClientRpc echo, and switching slots first corrupts
            // currentlyHeldObjectServer (CLAUDE.md gotcha #5).
            float waitStart = Time.time;
            while (RoutineSupport.ShouldKeepWaiting(player, waitStart))
                yield return null;

            if (RoutineSupport.IsPlayerGone(player) || desk == null)
                yield break;

            if (RoutineSupport.IsBusy(player))
            {
                ModLog.Warning("Timed out waiting for the previous grab/placement to finish - stopping auto-place early to avoid desyncing the rest.");
                yield break;
            }

            // The list is a snapshot but this loop yields: re-check the slot still holds this item.
            if (item == null || slot >= player.ItemSlots.Length || player.ItemSlots[slot] != item || !item.itemProperties.isScrap)
                continue;

            if (PlaceConfiguration.IsPlaceBlacklisted(item.itemProperties.itemName))
                continue;

            RoutineSupport.SwitchToSlot(player, slot);
            desk.PlaceItemOnCounter(player);

            yield return new WaitForSeconds(DelayBetweenPlacementsSeconds);
        }
    }
}

internal static class DropAllRoutine
{
    // Drops every (slot, item) pair in order. Leaves you on the slot of the last item dropped.
    public static IEnumerator Run(PlayerControllerB player, List<(int Slot, GrabbableObject Item)> items, bool ignoreBlacklist)
    {
        bool droppedReservedItem = false;

        foreach ((int slot, GrabbableObject item) in items)
        {
            // Wait *before* acting: the previous drop may still be waiting for its
            // ThrowObjectClientRpc echo, and switching slots first corrupts
            // currentlyHeldObjectServer (CLAUDE.md gotcha #5).
            float waitStart = Time.time;
            while (RoutineSupport.ShouldKeepWaiting(player, waitStart))
                yield return null;

            if (RoutineSupport.IsPlayerGone(player))
                yield break;

            if (RoutineSupport.IsBusy(player))
            {
                ModLog.Warning("Timed out waiting for the previous grab/drop to finish - stopping drop-all early to avoid desyncing the rest.");
                break;
            }

            // The list is a snapshot but this loop yields: re-check the slot still holds this item.
            if (item == null || slot >= player.ItemSlots.Length || player.ItemSlots[slot] != item)
                continue;

            if (!ignoreBlacklist && ItemBlacklist.IsBlacklisted(item.itemProperties.itemName))
                continue;

            droppedReservedItem |= InventoryAccessor.IsReservedSlot(player, slot);

            RoutineSupport.SwitchToSlot(player, slot);
            player.DiscardHeldObject();
        }

        if (!droppedReservedItem)
            yield break;

        // ReservedItemSlotCore schedules its own slot switch after a reserved drop and only
        // refreshes its reserved-slot HUD for the first reserved drop of a sequence (CLAUDE.md
        // gotcha #10). Let the last drop's echo and that switch land, then refresh once.
        float lastWaitStart = Time.time;
        while (RoutineSupport.ShouldKeepWaiting(player, lastWaitStart))
            yield return null;

        if (RoutineSupport.IsPlayerGone(player))
            yield break;

        yield return new WaitForEndOfFrame();
        yield return null;

        ReservedItemSlotCompat.RefreshHudAfterReservedDrop();
    }
}
