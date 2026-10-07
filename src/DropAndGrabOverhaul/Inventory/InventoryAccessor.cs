using System.Collections.Generic;
using DropAndGrabOverhaul.Compatibility;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Inventory;

// The one place that enumerates a player's inventory; drop-all and auto-place go through here
// instead of indexing player.ItemSlots, so inventory-mod integrations only touch this class.
internal static class InventoryAccessor
{
    // Every non-null (slot, item) pair in slot order, recomputed on each call; hold on to the
    // list for a multi-frame sequence. With includeReservedSlots false, ReservedItemSlotCore's
    // reserved slots are left out.
    public static List<(int Slot, GrabbableObject Item)> GetItemSlots(PlayerControllerB? player, bool includeReservedSlots = true)
    {
        var result = new List<(int, GrabbableObject)>();

        if (player?.ItemSlots == null)
            return result;

        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            GrabbableObject item = player.ItemSlots[i];
            if (item == null)
                continue;

            if (!includeReservedSlots && IsReservedSlot(player, i))
                continue;

            result.Add((i, item));
        }

        return result;
    }

    // Always false when ReservedItemSlotCore isn't installed.
    public static bool IsReservedSlot(PlayerControllerB? player, int slot)
        => ReservedItemSlotCompat.IsReservedSlot(player, slot);
}
