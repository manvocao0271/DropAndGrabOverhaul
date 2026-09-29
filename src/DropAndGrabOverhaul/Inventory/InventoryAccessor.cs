using System.Collections.Generic;
using DropAndGrabOverhaul.Compatibility;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Inventory;

// Single place that knows how to enumerate a player's current inventory slots.
// Everything that needs to walk "the player's current items" (drop-all, auto-sell)
// should route through here instead of indexing player.ItemSlots directly, so a
// future HotbarPlus/ReservedItemSlot integration only has to change this one class -
// see README's Compatibility section.
internal static class InventoryAccessor
{
    /// <summary>
    /// Returns every non-null (slot index, item) pair currently held by
    /// <paramref name="player"/>, in slot order. Recomputed fresh on each call -
    /// callers that need a stable view across multiple frames should hold on to
    /// the returned list themselves rather than calling this again mid-sequence.
    /// </summary>
    /// <param name="includeReservedSlots">
    /// When false, ReservedItemSlotCore's reserved slots are left out, leaving only the main
    /// hotbar. Without that mod installed the flag makes no difference.
    /// </param>
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

    /// <summary>
    /// True if <paramref name="slot"/> is a ReservedItemSlotCore reserved slot for
    /// <paramref name="player"/>. Always false when that mod isn't installed.
    /// </summary>
    public static bool IsReservedSlot(PlayerControllerB? player, int slot)
        => ReservedItemSlotCompat.IsReservedSlot(player, slot);
}
