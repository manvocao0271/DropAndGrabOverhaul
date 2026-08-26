using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAllScrap.Inventory
{
    public static class InventoryAccessor
    {
        public static List<GrabbableObject> GetDroppableItems()
        {
            var droppableItems = new List<GrabbableObject>();

            // Get the local player
            PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
            if (player == null)
            {
                Plugin.Log.LogWarning("Could not access local player controller");
                return droppableItems;
            }

            // Iterate through all item slots
            if (player.ItemSlots == null || player.ItemSlots.Length == 0)
            {
                return droppableItems;
            }

            foreach (GrabbableObject item in player.ItemSlots)
            {
                if (item == null) continue;

                // Add the item to droppable list (we'll drop all items initially)
                droppableItems.Add(item);
            }

            return droppableItems;
        }
    }
}