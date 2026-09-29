using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration;

// Items that double-tap drop skips (force drop ignores this list).
internal static class ItemBlacklist
{
    private static ItemNameList items = null!;

    public static void Initialize(ConfigFile config)
    {
        items = new ItemNameList(
            config,
            section: "Items",
            key: "BlacklistedItems",
            description: "Comma-separated list of item names that should NOT be dropped (Walkie-talkie, Flashlight, Shovel, etc.)",
            label: "blacklisted");
    }

    public static bool IsBlacklisted(string itemName) => items.Contains(itemName);
}
