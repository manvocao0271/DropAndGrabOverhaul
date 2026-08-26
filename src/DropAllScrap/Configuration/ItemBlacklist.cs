using BepInEx.Configuration;
using System.Collections.Generic;

namespace DropAllScrap.Configuration
{
    public static class ItemBlacklist
    {
        private static ConfigEntry<string> blacklistedItemsConfig = null!;
        private static HashSet<string> blacklistedItems = new();

        public static void Initialize(ConfigFile config)
        {
            blacklistedItemsConfig = config.Bind(
                section: "Items",
                key: "BlacklistedItems",
                defaultValue: "Walkie-talkie, Flashlight, Shovel, Lockpicker, Pro-flashlight, Stun grenade, Boombox, TZP-Inhalant, Zap gun, Jetpack, Extension Ladder, Radar-booster, Spray paint, Weed killer, Belt bag, Kitchen knife, Shotgun, Ammo",
                description: "Comma-separated list of item names that should NOT be dropped (Walkie-talkie, Flashlight, Shovel, etc.)"
            );

            // Parse the comma-separated list into a HashSet for fast lookup
            RefreshBlacklist();
        }

        public static void RefreshBlacklist()
        {
            blacklistedItems.Clear();
            
            if (string.IsNullOrWhiteSpace(blacklistedItemsConfig.Value))
                return;

            var items = blacklistedItemsConfig.Value.Split(',');
            foreach (var item in items)
            {
                string trimmedItem = item.Trim();
                if (!string.IsNullOrEmpty(trimmedItem))
                {
                    blacklistedItems.Add(trimmedItem);
                }
            }

            Plugin.Log.LogInfo($"Loaded {blacklistedItems.Count} blacklisted items: {string.Join(", ", blacklistedItems)}");
        }

        public static bool IsBlacklisted(string itemName)
        {
            bool isBlacklisted = blacklistedItems.Contains(itemName);
            if (isBlacklisted)
            {
                Plugin.Log.LogInfo($"Item '{itemName}' is blacklisted");
            }
            return isBlacklisted;
        }

        public static HashSet<string> GetBlacklistedItems()
        {
            return new HashSet<string>(blacklistedItems);
        }
    }
}
