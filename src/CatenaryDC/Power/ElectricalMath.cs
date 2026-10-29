using System;
namespace CatenaryDC.Power;

internal static class ElectricalMath
{
    // X/Z are millimetres; Y is metres. Use double before subtraction/squaring.
    internal static float Distance(int x0, int z0, float y0, int x1, int z1, float y1)
    {
        double dx = ((double)x0 - x1) / 1000.0;
        double dz = ((double)z0 - z1) / 1000.0;
        double dy = (double)y0 - y1;
        return (float)Math.Sqrt(dx * dx + dz * dz + dy * dy);
    }
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
