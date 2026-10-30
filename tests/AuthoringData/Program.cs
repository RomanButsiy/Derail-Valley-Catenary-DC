// Exercises the production C# validator without game/Unity assemblies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatenaryDC.Authoring;
using Newtonsoft.Json.Linq;

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
string assets = Path.Combine(root, "unity_project", "Assets", "Catenary");
var tables = Directory.GetFiles(assets, "*.csv").ToDictionary(p => Path.GetFileNameWithoutExtension(p)!, File.ReadAllText);
var sources = Directory.GetFiles(Path.Combine(assets, "Scenery"), "scenery_*.json").ToDictionary(p => Path.GetFileName(p)!, File.ReadAllText);
int passed = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
JObject Read(JArray data) => SceneryReview.Validate(new Dictionary<string, string> { ["scenery_test.json"] = data.ToString() }, tables!);
JObject Obj(string kind) => new JObject { ["$type"] = "electric_sim.catenary.overhead_equipment+" + kind + ", electric_sim",
    ["x"] = 1000000, ["z"] = 2000000, ["y"] = 100.0, ["orientation"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 0, ["w"] = 1 } };
JObject station = Obj("substation"); station["map_location"] = "TEST"; station["supply_voltage"] = 1650; station["maximum_load"] = 4000; station["has_inverter"] = true;
JObject wire = Obj("wire"); wire["substation"] = "TEST"; wire["wire_type"] = "plain_single"; wire["length"] = 40; wire["previous_pole_vertical_offset"] = 0;
JObject report = SceneryReview.Validate(sources!, tables!);
Check((bool)report["data_valid"]!, "All reference source JSON passes constructor-free production validator");
Check((int)report["objects"]! == 11069, "Reference object count");
Check((bool)Read(new JArray(station, wire))["data_valid"]!, "Valid source and wire");
Check(!(bool)Read(new JArray(wire))["data_valid"]!, "Unknown source rejected");
Check(!(bool)Read(new JArray(station, station.DeepClone()))["data_valid"]!, "Duplicate source rejected");
JObject changed = (JObject)wire.DeepClone(); changed["length"] = 0;
Check(!(bool)Read(new JArray(station, changed))["data_valid"]!, "Zero length rejected");
changed = (JObject)wire.DeepClone(); changed["$type"] = "System.String, mscorlib";
Check(!(bool)Read(new JArray(changed))["data_valid"]!, "Foreign CLR type rejected");
changed = (JObject)wire.DeepClone(); changed["$ref"] = "x";
Check(!(bool)Read(new JArray(changed))["data_valid"]!, "Reference metadata rejected");
changed = (JObject)wire.DeepClone(); changed["orientation"]!["w"] = 0;
Check(!(bool)Read(new JArray(station, changed))["data_valid"]!, "Invalid quaternion rejected");
changed = (JObject)wire.DeepClone(); changed["length"] = 20; changed["wire_type"] = "plain_half1_single";
Check((bool)Read(new JArray(station, changed))["data_valid"]!, "Valid half-section accepted");
Console.WriteLine($"{passed} production C# authoring-data checks passed. No Unity/game was used.");
