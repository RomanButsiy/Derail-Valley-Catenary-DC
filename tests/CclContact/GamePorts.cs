using System;
using System.Collections.Generic;
using UnityEngine;
using LocoSim.Implementations;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public object gameObject { get; set; } = new();
        protected static void Destroy(object target) => throw new InvalidOperationException("Invalid test vehicle");
    }
    public sealed class SerializeField : Attribute { }
    public static class Mathf { public static float Abs(float n) => Math.Abs(n); }
    public static class Debug { public static void LogError(string msg, object sender) => throw new Exception(msg); }
}
public sealed class TrainCar
{
    public string name => "EU07";
    public readonly Transform transform = new();
    public static TrainCar? Resolve(object obj) => obj as TrainCar;
}
namespace DV.Simulation.Controllers
{
    public abstract class ASimInitializedController : MonoBehaviour
    {
        public abstract bool ExternalTick { get; }
        public abstract void Init(TrainCar car, SimulationFlow flow);
        public abstract void Tick(float deltaTime);
    }
}
namespace LocoSim.Definitions
{
    public enum PortType { EXTERNAL_IN }
    public enum PortValueType { GENERIC, VOLTS, STATE, AMPS }
}
namespace LocoSim.Attributes
{
    using LocoSim.Definitions;
    public sealed class PortIdAttribute : Attribute
    {
        public PortIdAttribute(PortType t, PortValueType v, bool local) { }
        public PortIdAttribute(PortValueType v) { }
    }
}
namespace LocoSim.Implementations
{
    public sealed class Port { public float Value; }
    public sealed class SimulationFlow
    {
        public readonly Dictionary<string,Port> Ports = new();
        public bool TryGetPort(string id, out Port? port) => Ports.TryGetValue(id, out port);
    }
}
namespace catenary_for_CCL.importer
{
    internal sealed class editor_proxy : Attribute { public editor_proxy(Type type) { } }
    internal interface electric_component_defition { void map_from(MonoBehaviour proxy); }
    internal static class Main
    {
        public static void log(string msg) { }
        public static void release_log(string msg) => Console.WriteLine(msg);
    }
}
namespace catenary_for_CCL.types
{
    public class pantograph_sim_controller_proxy : MonoBehaviour
    {
        public readonly Transform PantographBase = new(), ContactStripFirstEnd = new(), ContactStripSecondEnd = new();
        public float ContactTolerance = 0.2f;
        public string InitialHeightPortId = "initial", HeadHeightPortId = "head", WireHeightPortId = "wire",
                      WireVoltagePortId = "volts", IsInContactPortId = "contact", InputCurrentPortId = "amps";
    }
}
