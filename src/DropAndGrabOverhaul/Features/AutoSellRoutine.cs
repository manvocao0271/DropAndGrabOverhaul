using System.Collections;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Inventory;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul.Features;

internal static class AutoSellRoutine
{
    private const float DelayBetweenSalesSeconds = 0.2f;

    // Places every eligible scrap item on the company counter, one at a time. Only the main
    // hotbar is walked: ReservedItemSlotCore's reserved slots are never sold.
    // Concurrency is handled by the caller's CoroutineGate.
    public static IEnumerator Run(PlayerControllerB player, DepositItemsDesk desk)
    {
        int soldCount = 0;

        foreach ((int slot, GrabbableObject item) in InventoryAccessor.GetItemSlots(player, includeReservedSlots: false))
        {
            // GetItemSlots snapshots the inventory once up front, but this loop yields
            // between sales - re-confirm the slot still holds this exact item before
            // acting on it, in case something else changed slot contents mid-sequence.
            if (item == null || slot >= player.ItemSlots.Length || player.ItemSlots[slot] != item || !item.itemProperties.isScrap)
                continue;

            string itemName = item.itemProperties.itemName;

            if (SellConfiguration.IsSellBlacklisted(itemName))
            {
                ModLog.Info($"Skipping sell-blacklisted item: {itemName}");
                continue;
            }

            player.SwitchToItemSlot(slot);
            desk.PlaceItemOnCounter(player);
            soldCount++;
            ModLog.Info($"Sold item: {itemName}");

            yield return new WaitForSeconds(DelayBetweenSalesSeconds);
        }

        ModLog.Info($"Sold {soldCount} items total");
    }
}
