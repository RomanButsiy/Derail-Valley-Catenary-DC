using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    // Old files say "electric_sim.catenary.overhead_equipment+wire, electric_sim".
    // Resolve these seven known data classes locally WITHOUT loading electric_sim.dll.
    private sealed class SceneryBinder : ISerializationBinder
    {
        private const string LegacyPrefix = "electric_sim.catenary.overhead_equipment+";
        private const string LocalPrefix = "CatenaryDC.catenary.overhead_equipment+";
        private static readonly Dictionary<string, Type> Types = new(StringComparer.Ordinal)
        {
            ["pole"] = typeof(pole), ["gantry"] = typeof(gantry),
            ["cantilever"] = typeof(cantilever), ["wire"] = typeof(wire),
            ["substation"] = typeof(substation), ["miscellaneous_object"] = typeof(miscellaneous_object),
            ["side_rail_pole"] = typeof(side_rail_pole)
        };
        public Type BindToType(string? assemblyName, string typeName)
        {
            string simpleAssembly = (assemblyName ?? "").Split(',')[0].Trim();
            if (simpleAssembly != "electric_sim" && simpleAssembly != "CatenaryDC")
                throw new JsonSerializationException("Unsupported scenery assembly: " + assemblyName);
            string suffix;
            if (typeName.StartsWith(LegacyPrefix, StringComparison.Ordinal)) suffix = typeName.Substring(LegacyPrefix.Length);
            else if (typeName.StartsWith(LocalPrefix, StringComparison.Ordinal)) suffix = typeName.Substring(LocalPrefix.Length);
            else throw new JsonSerializationException("Unsupported scenery type: " + typeName);
            if (!Types.TryGetValue(suffix, out Type? result))
                throw new JsonSerializationException("Unsupported scenery class: " + suffix);
            return result;
        }
        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            foreach (var pair in Types)
                if (pair.Value == serializedType)
                {
                    assemblyName = "electric_sim";
                    typeName = LegacyPrefix + pair.Key;
                    return;
                }
            throw new JsonSerializationException("Cannot export non-scenery type: " + serializedType);
        }
    }
    private static JsonSerializerSettings scenery_json_settings() => new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = new SceneryBinder(),
        MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
        MaxDepth = 32
    };
}
