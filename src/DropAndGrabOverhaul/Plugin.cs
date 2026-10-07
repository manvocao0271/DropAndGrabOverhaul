using System.Reflection;
using BepInEx;
using DropAndGrabOverhaul.Configuration;
using HarmonyLib;

namespace DropAndGrabOverhaul;

// Bootstrap only: config, then Harmony. Behaviour lives elsewhere:
//   UpdateRunner.cs        per-frame input handling (created by StartOfRoundPatch)
//   Features/Routines.cs   the drop-all and auto-place coroutines
//   Patches/Patches.cs     every Harmony patch, one class each
[BepInAutoPlugin]
[BepInDependency("FlipMods.ReservedItemSlotCore", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        ModLog.Initialize(Logger);

        // LoggingConfiguration first, so the log-level gate is set up before anything else logs.
        LoggingConfiguration.Initialize(Config);
        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);
        GrabConfiguration.Initialize(Config);
        PlaceConfiguration.Initialize(Config);

        // Applies every class that carries a class-level [HarmonyPatch] (CLAUDE.md gotcha #1).
        new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

        ModLog.Info($"Plugin {Name} is loaded!");
    }
}
