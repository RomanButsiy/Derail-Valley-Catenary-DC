// Derived from the upstream MIT-licensed mod entry point. See LICENSE_MIT.txt.
using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using CatenaryDC.catenary_editor;
using CatenaryDC.Api;
using static UnityModManagerNet.UnityModManager;

namespace CatenaryDC;

public static class Main
{
    private static ModEntry.ModLogger? _logger;
    public static ModEntry? mod_info { get; private set; }

    [Conditional("DEBUG")]
    public static void log(string message) => _logger?.Log(message);
    public static void release_log(string message) => _logger?.Log(message);

    internal static void assert_no_original_core()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "electric_sim" &&
                assembly.GetType("electric_sim.catenary.overhead_equipment", false) != null)
                throw new InvalidOperationException("The original electric_sim/Catenary-DC is loaded. " +
                    "Do not run two catenary simulations. Move the old Catenary-DC folder out of Mods and restart. " +
                    "The optional Legacy2WE3 bridge is a separate controller, not the old Catenary-DC mod.");
        }
    }

    public static bool Load(ModEntry mod)
    {
        _logger = mod.Logger;
        Harmony? harmony = null;
        try
        {
            assert_no_original_core();
            mod_info = mod;
            editor_settings.set_up(mod);
            harmony = new Harmony(mod.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            release_log("Derail-Valley-Catenary-DC 0.2.0: catenary-only core. No locomotive controller is included.");
            // This engine is session-bound; hot disabling can strand a raised pantograph.
            mod.OnToggle = (_, enabled) => enabled;
            return true;
        }
        catch (Exception error)
        {
            CatenaryApi.LastError = error.ToString();
            _logger.LogException("Catenary core could not load", error);
            harmony?.UnpatchAll(mod.Info.Id);
            mod_info = null;
            return false;
        }
    }
}
