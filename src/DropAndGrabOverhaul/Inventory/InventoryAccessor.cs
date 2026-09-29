using System.Collections.Generic;
using DropAndGrabOverhaul.Compatibility;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Inventory
{
    // Single place that knows how to enumerate a player's current inventory slots.
    // Everything that needs to walk "the player's current items" (drop-all, auto-sell)
    // should route through here instead of indexing
    // player.ItemSlots directly, so a future HotbarPlus/ReservedItemSlot integration
    // only has to change this one class - see README's Compatibility section.
    public static class InventoryAccessor
    {
        /// <summary>
        /// Returns every non-null (slot index, item) pair currently held by
        /// <paramref name="player"/>, in slot order. Recomputed fresh on each call -
        /// callers that need a stable view across multiple frames should hold on to
        /// the returned list themselves rather than calling this again mid-sequence.
        /// </summary>
        public static List<(int Slot, GrabbableObject Item)> GetItemSlots(PlayerControllerB? player)
        {
            var result = new List<(int, GrabbableObject)>();

            if (player?.ItemSlots == null)
                return result;

            for (int i = 0; i < player.ItemSlots.Length; i++)
            {
                GrabbableObject item = player.ItemSlots[i];
                if (item != null)
                {
                    result.Add((i, item));
                }
            }

            return result;
        }

        /// <summary>
        /// Convenience wrapper over <see cref="GetItemSlots"/> for callers that only need
        /// the items themselves (e.g. drop-all), not their slot indices.
        /// </summary>
        public static List<GrabbableObject> GetDroppableItems(PlayerControllerB? player)
        {
            var items = new List<GrabbableObject>();
            foreach ((int _, GrabbableObject item) in GetItemSlots(player))
            {
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Items in the main hotbar: every slot except ReservedItemSlotCore's reserved slots.
        /// Without that mod installed this is the same as <see cref="GetDroppableItems"/>.
        /// </summary>
        public static List<GrabbableObject> GetMainHotbarItems(PlayerControllerB? player)
        {
            var items = new List<GrabbableObject>();
            foreach ((int slot, GrabbableObject item) in GetItemSlots(player))
            {
                if (!IsReservedSlot(player, slot))
                {
                    items.Add(item);
                }
            }

            return items;
        }

        /// <summary>
        /// True if <paramref name="slot"/> is a ReservedItemSlotCore reserved slot for
        /// <paramref name="player"/>. Always false when that mod isn't installed.
        /// </summary>
        public static bool IsReservedSlot(PlayerControllerB? player, int slot)
        {
            return ReservedItemSlotCompat.IsReservedSlot(player, slot);
        }
    }
}
