// Public, locomotive-neutral boundary. All methods/events are Unity-main-thread only.
using System;
using UnityEngine;
using CatenaryDC.catenary;

namespace CatenaryDC.Api;

public static class CatenaryApi
{
    public static int ApiVersion => 1;
    public static bool IsReady => overhead_equipment.is_ready;
    public static string? LastError { get; internal set; }
    public static int LoadedObjectCount => overhead_equipment.loaded_object_count;

    /// <summary>Called once per physics tick, BEFORE the substation load is committed.
    /// A vehicle adapter should sample each active collector once, using its share of current.</summary>
    public static event Action? SimulationStep;
    public static event Action? WorldActivated;
    public static event Action? WorldDeactivated;

    public static (float? contact_height, float contact_voltage) QueryRelativeContact(
        Transform vehicle, Transform pantographBase, Transform stripEnd1, Transform stripEnd2, float loadCurrent)
    {
        if (vehicle == null || pantographBase == null || stripEnd1 == null || stripEnd2 == null)
            throw new ArgumentNullException("Collector transforms must exist.");
        if (float.IsNaN(loadCurrent) || float.IsInfinity(loadCurrent))
            throw new ArgumentOutOfRangeException(nameof(loadCurrent));
        if (!IsReady) return (null, 0.0f);
        return overhead_equipment.system.relative_wire_height_and_voltage(
            vehicle, pantographBase, stripEnd1, stripEnd2, loadCurrent);
    }

    private static void Notify(Action? callbacks, string label)
    {
        if (callbacks == null) return;
        foreach (Action callback in callbacks.GetInvocationList())
        {
            try { callback(); }
            catch (Exception error) { Main.release_log("Adapter callback failed (" + label + "): " + error); }
        }
    }
    internal static void RaiseSimulationStep() => Notify(SimulationStep, "SimulationStep");
    internal static void RaiseActivated() => Notify(WorldActivated, "WorldActivated");
    internal static void RaiseDeactivated() => Notify(WorldDeactivated, "WorldDeactivated");
}
