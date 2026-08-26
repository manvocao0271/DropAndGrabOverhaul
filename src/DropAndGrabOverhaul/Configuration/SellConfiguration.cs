using BepInEx.Configuration;
using System.Collections.Generic;

namespace DropAndGrabOverhaul.Configuration
{
    public static class SellConfiguration
    {
        private static ConfigEntry<bool> autoSellInventoryConfig = null!;
        private static ConfigEntry<string> sellBlacklistConfig = null!;
        private static HashSet<string> sellBlacklistedItems = new();

        public static bool AutoSellInventory { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            autoSellInventoryConfig = config.Bind(
                section: "Sell",
                key: "AutoSellInventory",
                defaultValue: true,
                description: "Holding the drop key while at the company counter will automatically sell all items in the inventory."
            );

            sellBlacklistConfig = config.Bind(
                section: "Sell",
                key: "SellBlacklistedItems",
                defaultValue: "Walkie-talkie, Flashlight, Shovel, Lockpicker, Pro-flashlight, Stun grenade, Boombox, TZP-Inhalant, Zap gun, Jetpack, Extension Ladder, Radar-booster, Spray paint, Weed killer, Belt bag, Kitchen knife, Shotgun, Ammo, Key",
                description: "Comma-separated list of item names that should NOT be automatically sold by the auto-sell feature."
            );

            AutoSellInventory = autoSellInventoryConfig.Value;
            Plugin.Log.LogInfo($"Auto sell inventory: {AutoSellInventory}");

            RefreshSellBlacklist();
        }

        public static void RefreshSellBlacklist()
        {
            sellBlacklistedItems.Clear();

            if (string.IsNullOrWhiteSpace(sellBlacklistConfig.Value))
                return;

            var items = sellBlacklistConfig.Value.Split(',');
            foreach (var item in items)
            {
                string trimmedItem = item.Trim();
                if (!string.IsNullOrEmpty(trimmedItem))
                {
                    sellBlacklistedItems.Add(trimmedItem);
                }
            }

            Plugin.Log.LogInfo($"Loaded {sellBlacklistedItems.Count} sell-blacklisted items: {string.Join(", ", sellBlacklistedItems)}");
        }

        public static bool IsSellBlacklisted(string itemName)
        {
            return sellBlacklistedItems.Contains(itemName);
        }
    }
}

