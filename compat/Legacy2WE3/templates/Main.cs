// Entry-point adaptation: retain upstream MIT terms; see LICENSE_MIT.txt.
// Optional adapter entry point. This assembly contains no catenary engine.
using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LocoSim.Implementations;
using CatenaryDC.Api;
using electric_sim.catenary_editor;
using static UnityModManagerNet.UnityModManager;
namespace electric_sim;
public static class Main
{
    private static ModEntry.ModLogger? _logger;
    public static Port? diagnostics { get; set; }
    public static Port? diagnostics2 { get; set; }
    public static ModEntry? mod_info { get; private set; }
    [Conditional("DEBUG")] public static void log(string message) => _logger?.Log(message);
    [Conditional("DEBUG")] public static void background_log(string message) => Console.WriteLine("[Legacy2WE3] " + message);
    public static void release_log(string message) => _logger?.Log(message);
    public static bool Load(ModEntry mod)
    {
        _logger = mod.Logger;
        Harmony? harmony = null;
        try
        {
            // Avoid binding to a second copy of the original vehicle controller.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "electric_sim")
                    throw new InvalidOperationException("Remove the original Catenary-DC/electric_sim before enabling this bridge.");
            if (CatenaryApi.ApiVersion != 1) throw new InvalidOperationException("Unsupported catenary API.");
            mod_info = mod;
            editor_settings.set_up(mod);
            harmony = new Harmony(mod.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            mod.OnToggle = (_, enabled) => enabled;
            release_log("Optional Legacy2WE3 adapter loaded. Uses ONE CatenaryDC network; 2WE3CCL models must be installed separately.");
            return true;
        }
        catch (Exception error)
        {
            _logger.LogException("Legacy2WE3 adapter failed", error);
            harmony?.UnpatchAll(mod.Info.Id);
            mod_info = null;
            return false;
        }
    }
}
