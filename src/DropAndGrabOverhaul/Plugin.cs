using System.Reflection;
using BepInEx;
using DropAndGrabOverhaul.Configuration;
using HarmonyLib;

namespace DropAndGrabOverhaul;

// Bootstrap only: config, then Harmony. Behaviour lives elsewhere:
//   UpdateRunner.cs   per-frame input handling (created by Patches/StartOfRoundPatch.cs)
//   Features/         the drop-all and auto-sell coroutines
//   Patches/          every Harmony patch, one class each
[BepInAutoPlugin]
[BepInDependency("FlipMods.ReservedItemSlotCore", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        ModLog.Initialize(Logger);

        // LoggingConfiguration first: every other config class logs at startup, and those
        // lines are gated by whatever it resolves to.
        LoggingConfiguration.Initialize(Config);
        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);
        GrabConfiguration.Initialize(Config);
        SellConfiguration.Initialize(Config);

        // Every patch class carries its own [HarmonyPatch] target, so scanning the assembly
        // finds them all (see CLAUDE.md gotcha #1 for why this must not be PatchAll()).
        new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

        ModLog.Info($"Plugin {Name} is loaded!");
    }
}
