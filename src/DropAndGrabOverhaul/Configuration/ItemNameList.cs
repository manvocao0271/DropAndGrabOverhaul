using System.Collections.Generic;
using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration;

// A comma-separated item-name list bound to a config entry. Names are matched exactly and
// case-sensitively, on purpose. Re-parsed on SettingChanged so in-game edits apply immediately.
internal sealed class ItemNameList
{
    // Default for every list: the vanilla store items and other gear worth keeping, plus some
    // items from popular mods.
    public const string DefaultNames =
        "Walkie-talkie, Flashlight, Shovel, Lockpicker, Pro-flashlight, Stun grenade, Boombox, TZP-Inhalant, Zap gun, Jetpack, Extension ladder, Radar-booster, Spray paint, Weed killer, Belt bag, Stop sign, Yield sign, Kitchen knife, Shotgun, Ammo, Key, Medic Bag, Night Vision Goggles, Cat, Cat Food, IFireAxe, IBaseball bat, Plunger, Mega Flashlight, Firework Crate, Firework Rocket, Beacon, Emergency Flare, Pile of Glowsticks, Flip Lighter, Bullet Lighter, Impact flash, Jammer, Light crowbar, Military shotgun, Single shotgun, Gohei, Pink Guitar, Antique Candle, Industrial Flashlight";

    private readonly ConfigEntry<string> entry;
    private HashSet<string> names = new();

    public ItemNameList(ConfigFile config, string section, string key, string description)
    {
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
    }
}
