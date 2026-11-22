// Only the Unity transforms and the world below the production API are simulated.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CatenaryDC.Api;
using UnityEngine;

[assembly: InternalsVisibleTo("CclContact")]

namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator /(Vector3 a, float b) => new(a.x/b, a.y/b, a.z/b);
    }
    public sealed class Transform
    {
        public Vector3 position;
        public Vector3 InverseTransformPoint(Vector3 point) => point - position;
    }
}
namespace CatenaryDC
{
    internal static class Main { public static void release_log(string message) => Console.WriteLine(message); }
}
namespace CatenaryDC.catenary
{
    public sealed class overhead_equipment
    {
        public static bool is_ready { get; private set; }
        public static int loaded_object_count => 0;
        public static readonly overhead_equipment system = new();
        public static (float?, float) Contact;
        public static readonly List<(Transform, Transform, Transform, Transform, float)> Samples = new();

        public static void Activate() { is_ready = true; CatenaryApi.RaiseActivated(); }
        public static void Deactivate() { is_ready = false; CatenaryApi.RaiseDeactivated(); }
        public (float?, float) relative_wire_height_and_voltage(Transform car, Transform pan, Transform a, Transform b, float amps)
        {
            Samples.Add((car, pan, a, b, amps));
            return Contact;
        }
    }
}
