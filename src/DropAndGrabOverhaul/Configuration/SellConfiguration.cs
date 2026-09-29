using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration;

internal static class SellConfiguration
{
    private static ConfigEntry<bool> autoSellInventory = null!;
    private static ItemNameList sellBlacklist = null!;

    public static bool AutoSellInventory => autoSellInventory.Value;

    public static void Initialize(ConfigFile config)
    {
        autoSellInventory = config.Bind(
            section: "Sell",
            key: "AutoSellInventory",
            defaultValue: true,
            description: "Pressing the drop key once while at the company counter will automatically put all sellable items in the inventory on the counter."
        );

        sellBlacklist = new ItemNameList(
            config,
            section: "Sell",
            key: "SellBlacklistedItems",
            description: "Comma-separated list of item names that should NOT be automatically sold (Walkie-talkie, Flashlight, Shovel, etc.)",
            label: "sell-blacklisted");

        ModLog.Info($"Auto sell inventory: {AutoSellInventory}");
    }

    public static bool IsSellBlacklisted(string itemName) => sellBlacklist.Contains(itemName);
}
