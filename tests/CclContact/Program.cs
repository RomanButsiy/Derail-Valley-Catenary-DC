// Drives the actual CCL-OCS pantograph controller through its normal Init/Tick paths.
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using catenary_for_CCL.importer;
using catenary_for_CCL.types;
using CatenaryDC.Api;
using LocoSim.Implementations;
using UnityEngine;
using World = CatenaryDC.catenary.overhead_equipment;

internal static class Program
{
    private sealed class Collector
    {
        public readonly SimulationFlow Flow = new();
        public readonly pantograph_sim_controller_proxy Proxy = new();
        public readonly pantograph_sim_controller Controller = new();
        public Collector(TrainCar car)
        {
            foreach (string port in new[] {"initial", "head", "wire", "volts", "contact", "amps"})
                Flow.Ports.Add(port, new Port());
            Move(0, 6);
            Controller.gameObject = car;
            Controller.map_from(Proxy);
            Controller.Init(car, Flow);
        }
        public void Move(float distance, float height)
        {
            Proxy.ContactStripFirstEnd.position = new Vector3(-0.9f, height, distance);
            Proxy.ContactStripSecondEnd.position = new Vector3(0.9f, height, distance);
        }
        public float this[string port] => Flow.Ports[port].Value;
        public void Tick() => Controller.Tick(0.02f);
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    public static void Main()
    {
        var car = new TrainCar();
        var front = new Collector(car);
        front.Tick();
        Check(front["volts"] == 0 && World.Samples.Count == 0, "Spawn before network activation");
        World.Contact = (6, 1500);
        World.Activate();
        front.Move(1, 6);
        front.Tick();
        Check(front["wire"] == 6 && front["volts"] == 1500 && front["contact"] == 1, "Live overhead");
        var rear = new Collector(car);
        rear.Move(2, 6);
        rear.Tick();
        front.Flow.Ports["amps"].Value = rear.Flow.Ports["amps"].Value = 125;
        World.Samples.Clear();
        front.Tick();
        rear.Tick();
        var p = front.Proxy;
        Check(World.Samples.Count == 2 && World.Samples.Sum(s => s.Item5) == 250 &&
              World.Samples[0] == (car.transform, p.PantographBase, p.ContactStripFirstEnd, p.ContactStripSecondEnd, 125f),
              "Both collectors forward their own load and geometry once");
        World.Contact = (6, 0);
        front.Tick();
        Check(front["volts"] == 0 && front["contact"] == 1, "Dead conductor retains contact");
        World.Contact = (null, 0);
        front.Tick();
        Check(front["volts"] == 0 && front["contact"] == 0 && front["wire"] == -1, "Physical gap");
        World.Deactivate();
        Check(rear["volts"] == 0, "World unload clears the second collector");
        World.Samples.Clear();
        front.Tick();
        rear.Tick();
        Check(World.Samples.Count == 0, "Inactive world receives no queries");
        World.Contact = (6, 750);
        World.Activate();
        front.Move(3, 6);
        front.Tick();
        Check(front["volts"] == 750 && World.Samples.Count == 1, "Reconnection on world reload");
        Check(!VehicleBridgeAttribute.IsOriginalNetwork(Type.GetType("electric_sim.catenary.overhead_equipment, electric_sim", true)!.Assembly),
              "Core accepts compatibility assembly");
        var orig = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("electric_sim"), AssemblyBuilderAccess.Run);
        orig.DefineDynamicModule("original").DefineType("electric_sim.catenary.overhead_equipment", TypeAttributes.Public).CreateType();
        Check(VehicleBridgeAttribute.IsOriginalNetwork(orig), "Core rejects original network assembly");
    }
}
