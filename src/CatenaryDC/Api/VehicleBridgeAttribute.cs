using System;
using System.Reflection;

namespace CatenaryDC.Api;

// CCL resolves the vehicle bridge by the original assembly name. The original
// OCS also uses that name, but must never be started alongside this core.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class VehicleBridgeAttribute : Attribute
{
    internal static bool IsOriginalNetwork(Assembly assembly) =>
        assembly.GetName().Name == "electric_sim" &&
        !assembly.IsDefined(typeof(VehicleBridgeAttribute), inherit: false) &&
        assembly.GetType("electric_sim.catenary.overhead_equipment", throwOnError: false) != null;
}
