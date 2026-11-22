// CatenaryForCCL 1.0/1.1 discovers these exact names by reflection and binds
// the instance method to a closed delegate for each pantograph.
using System;
using CatenaryDC.Api;
using UnityEngine;

[assembly: VehicleBridge]

namespace electric_sim.catenary;

public sealed class overhead_equipment
{
    private static readonly overhead_equipment ContactProvider = new();
    private overhead_equipment() { }

    public static overhead_equipment? system => CatenaryApi.IsReady ? ContactProvider : null;

    public static event Action catenary_activated
    {
        add => CatenaryApi.WorldActivated += value;
        remove => CatenaryApi.WorldActivated -= value;
    }

    public static event Action catenary_deactivated
    {
        add => CatenaryApi.WorldDeactivated += value;
        remove => CatenaryApi.WorldDeactivated -= value;
    }

    public (float?, float) relative_wire_height_and_voltage(
        Transform vehicle, Transform pantographBase, Transform stripEnd1, Transform stripEnd2, float loadCurrent)
        => CatenaryApi.QueryRelativeContact(vehicle, pantographBase, stripEnd1, stripEnd2, loadCurrent);
}
