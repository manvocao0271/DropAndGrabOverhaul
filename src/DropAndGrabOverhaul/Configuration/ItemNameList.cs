using System.Collections.Generic;
using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration;

// A comma-separated list of item names bound to a config entry, parsed into a set for fast
// lookup. Re-parses whenever the entry changes, so edits made in-game (e.g. via
// ConfigurationManager) or by hot-reloading the config file take effect immediately.
internal sealed class ItemNameList
{
    // Default for every list: all of the store-purchasable grabbable items.
    public const string DefaultNames =
        "Walkie-talkie, Flashlight, Shovel, Lockpicker, Pro-flashlight, Stun grenade, Boombox, TZP-Inhalant, Zap gun, Jetpack, Extension Ladder, Radar-booster, Spray paint, Weed killer, Belt bag, Kitchen knife, Shotgun, Ammo, Key, Medic bag, Night Vision Goggles, Cat, Cat Food, IFireAxe, IBaseball bat, Plunger";

    private readonly ConfigEntry<string> entry;
    private readonly string label;
    private HashSet<string> names = new();

    // label only appears in the log line, e.g. "blacklisted" -> "Loaded 26 blacklisted items: ...".
    public ItemNameList(ConfigFile config, string section, string key, string description, string label)
    {
        this.label = label;
        entry = config.Bind(section, key, DefaultNames, description);
        entry.SettingChanged += (_, _) => Reload();
        Reload();
    }

    public bool Contains(string itemName) => names.Contains(itemName);

    private void Reload()
    {
        var parsed = new HashSet<string>();

        if (!string.IsNullOrWhiteSpace(entry.Value))
        {
            foreach (string part in entry.Value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    parsed.Add(trimmed);
            }
        }

        names = parsed;
        ModLog.Info($"Loaded {names.Count} {label} items: {string.Join(", ", names)}");
    }
}
