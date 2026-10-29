// Catenary-only world integration. No 2WE3 class, fuse or traction port is used here.
using System;
using HarmonyLib;
using CatenaryDC.Api;
using CatenaryDC.catenary;
#if DEBUG
using DV.ThingTypes;
using CatenaryDC.catenary_editor;
#endif

namespace CatenaryDC;

[HarmonyPatch(typeof(CarSpawner), "Awake")]
internal static class WorldLifecycle
{
#if DEBUG
    private static CarSpawner? _spawner;
    private static TrainCar? _editorVehicle;
    private static mow_follower? _follower;
#endif
    public static void Postfix(CarSpawner __instance)
    {
        UnloadWatcher.UnloadRequested -= EndWorld;
        UnloadWatcher.UnloadRequested += EndWorld;
        try
        {
            Main.assert_no_original_core();
            if (Main.mod_info == null) throw new InvalidOperationException("Mod metadata is unavailable.");
            overhead_equipment.set_up(Main.mod_info);
#if DEBUG
            if (_spawner != null) _spawner.CarSpawned -= OnCarSpawned;
            _spawner = __instance;
            _spawner.CarSpawned += OnCarSpawned;
#endif
        }
        catch (Exception error)
        {
            CatenaryApi.LastError = error.ToString();
            Main.release_log("Catenary initialization FAILED; no partial layout is kept. " + error);
        }
    }

#if DEBUG
    private static void OnCarSpawned(TrainCar car)
    {
        if (!CatenaryApi.IsReady || car == null || !car.IsLoco || _follower != null) return;
        if (car.carType != TrainCarType.LocoDM1U && car.carType != TrainCarType.LocoMicroshunter) return;
        _editorVehicle = car;
        _follower = new mow_follower(overhead_equipment.system, car);
        car.OnDestroyCar += OnEditorDestroyed;
        Main.log("Editor vehicle attached: " + car.ID);
    }

    private static void OnEditorDestroyed(TrainCar car)
    {
        if (_editorVehicle != car) return;
        car.OnDestroyCar -= OnEditorDestroyed;
        _follower?.Dispose();
        _follower = null;
        _editorVehicle = null;
    }
#endif

    private static void EndWorld()
    {
        UnloadWatcher.UnloadRequested -= EndWorld;
#if DEBUG
        if (_editorVehicle != null) OnEditorDestroyed(_editorVehicle);
        if (_spawner != null) _spawner.CarSpawned -= OnCarSpawned;
        _spawner = null;
#endif
        overhead_equipment.dispose();
    }
}
