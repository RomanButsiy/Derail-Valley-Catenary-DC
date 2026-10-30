// CC0. Data-only validation: NEVER instantiate scenery classes or change the live network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CatenaryDC.utilities;

namespace CatenaryDC.Authoring;

internal static class SceneryReview
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
        { "wire", "pole", "cantilever", "gantry", "side_rail_pole", "substation", "miscellaneous_object" };
    private static readonly HashSet<string> Assemblies = new(StringComparer.Ordinal) { "electric_sim", "CatenaryDC" };
    private static readonly string[] Prefixes = { "electric_sim.catenary.overhead_equipment+", "CatenaryDC.catenary.overhead_equipment+" };

    internal static JObject Validate(IDictionary<string, string> sources, IDictionary<string, string> definitions)
    {
        var errors = new JArray(); var warnings = new JArray(); int errorCount = 0, warningCount = 0, count = 0;
        void Error(string source, int index, string message) { errorCount++; if (errors.Count < 300) errors.Add(new JObject { ["source"] = source, ["index"] = index, ["message"] = message }); }
        void Warn(string source, int index, string message) { warningCount++; if (warnings.Count < 300) warnings.Add(new JObject { ["source"] = source, ["index"] = index, ["message"] = message }); }
        var tables = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (string name in new[] { "poles", "side_poles", "cantilevers", "wires", "gantries", "miscellaneous" })
            try
            {
                if (!definitions.TryGetValue(name, out string? text)) throw new InvalidDataException("Missing CSV definition: " + name);
                var rows = CsvTable.Read(text);
                if (rows.Count < 2) throw new InvalidDataException("Empty CSV: " + name);
                var header = rows[0]; var list = new List<Dictionary<string, string>>();
                foreach (var row in rows.Skip(1))
                {
                    if (row.Count != header.Count) throw new InvalidDataException("CSV width mismatch: " + name);
                    var data = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int j = 0; j < header.Count; ++j) data[header[j]] = row[j];
                    list.Add(data);
                }
                tables[name] = list;
            }
            catch (Exception e) { Error("CSV", -1, e.Message); }
        var knownSubstations = new HashSet<string>(StringComparer.Ordinal) { "NEUTRAL" };
        var wires = new List<(string source, int index, string feed)>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in sources.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            JArray objects;
            try
            {
                using (var input = new StringReader(pair.Value))
                using (var reader = new JsonTextReader(input) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
                {
                    JToken value = JToken.ReadFrom(reader);
                    objects = value as JArray ?? throw new InvalidDataException("Expected a JSON array, not a survey/envelope.");
                    while (reader.Read()) if (reader.TokenType != JsonToken.Comment) throw new InvalidDataException("Trailing JSON content.");
                }
                if (objects.Count > 100000) throw new InvalidDataException("More than 100000 objects in a source.");
            }
            catch (Exception e) { Error(pair.Key, -1, e.Message); continue; }
            for (int i = 0; i < objects.Count; ++i)
            {
                count++;
                try
                {
                    JObject obj = objects[i] as JObject ?? throw new InvalidDataException("Object record expected.");
                    foreach (JProperty prop in obj.DescendantsAndSelf().OfType<JProperty>())
                        if (prop.Name == "$ref" || prop.Name == "$id" || prop.Name == "$values" || (prop.Name == "$type" && !ReferenceEquals(prop.Parent, obj)))
                            throw new InvalidDataException("Reference/nested type metadata is not permitted.");
                    string kind = TypeKind((string?)obj["$type"]);
                    Integer(obj, "x"); Integer(obj, "z"); Number(obj, "y");
                    JObject orientation = obj["orientation"] as JObject ?? throw new InvalidDataException("Missing orientation object.");
                    double norm = 0;
                    foreach (string axis in new[] { "x", "y", "z", "w" }) { double n = Number(orientation, axis); norm += n * n; }
                    if (Math.Abs(norm - 1) > .02) throw new InvalidDataException("Quaternion must be normalized (squared norm within 0.02 of 1).");
                    string signature = Canonical(obj).ToString(Formatting.None);
                    if (!signatures.Add(signature)) Warn(pair.Key, i, "Exact repeated object data in the combined candidate network.");
                    switch (kind)
                    {
                        case "wire":
                            string wireType = Text(obj, "wire_type"), feed = Text(obj, "substation");
                            var definition = Row(tables, "wires", r => r["Type"] == wireType);
                            double length = Number(obj, "length"); Number(obj, "previous_pole_vertical_offset");
                            if (length <= 0 || length > 2000) throw new InvalidDataException("Wire length must be in (0,2000] m.");
                            if (bool.Parse(definition["IsEndAnchor"]) && length <= 4) throw new InvalidDataException("End anchor must exceed its 4 m non-contact tail.");
                            double fixedLength = double.Parse(definition["FixedPartLength"], CultureInfo.InvariantCulture);
                            if (!string.IsNullOrWhiteSpace(definition["FixedPart"]) && length <= fixedLength) throw new InvalidDataException("Span is shorter than the fixed anchor model.");
                            if (wireType.StartsWith("plain_", StringComparison.Ordinal) && !wireType.Contains("plain_half") && length < 25)
                                Warn(pair.Key, i, "Short plain span: Debug displays ShortWireMarker. Consider a correct half-section pair, not a renamed full span.");
                            wires.Add((pair.Key, i, feed));
                            break;
                        case "pole":
                            string poleKind = (string?)obj["kind"] ?? "default", placement = Text(obj, "pole_type");
                            Row(tables, "poles", r => r["Kind"] == poleKind && r["Placement"] == placement);
                            break;
                        case "side_rail_pole":
                            string sideKind = (string?)obj["kind"] ?? "SideRail";
                            Row(tables, "side_poles", r => r["Kind"] == sideKind);
                            break;
                        case "cantilever":
                            string arm = Text(obj, "steady_arm_type"), direction = Text(obj, "cantilever_type");
                            bool dual = Boolean(obj, "dual_wire", false);
                            Row(tables, "cantilevers", r => r["SteadyArmType"] == arm && r["CantileverType"] == direction && bool.Parse(r["DualWire"]) == dual);
                            break;
                        case "gantry":
                            int tracks = Integer(obj, "tracks");
                            string gantryKind = (string?)obj["kind"] ?? (tracks == 0 ? "invisible" : tracks >= 1 && tracks <= 4 ? "slim" : tracks == 6 ? "truss" : "<invalid>");
                            Row(tables, "gantries", r => r["Kind"] == gantryKind && int.Parse(r["Tracks"], CultureInfo.InvariantCulture) == tracks);
                            double stretch = obj["stretch"] == null ? 1 : Number(obj, "stretch");
                            if (stretch <= 0) throw new InvalidDataException("Gantry stretch must be positive.");
                            break;
                        case "substation":
                            string id = Text(obj, "map_location");
                            if (!knownSubstations.Add(id)) throw new InvalidDataException("Duplicate/reserved substation ID: " + id);
                            if (Number(obj, "supply_voltage") < 0 || Number(obj, "maximum_load") <= 0) throw new InvalidDataException("Invalid substation voltage/load.");
                            Boolean(obj, "has_inverter", false);
                            break;
                        case "miscellaneous_object":
                            string template = Text(obj, "template_name");
                            if (template == "GantryArrow") Warn(pair.Key, i, "Explicit diagnostic marker; do not use it as a production scenery part.");
                            else Row(tables, "miscellaneous", r => r["Template"] == template);
                            break;
                    }
                }
                catch (Exception e) { Error(pair.Key, i, e.GetBaseException().Message); }
            }
        }
        foreach (var w in wires) if (!knownSubstations.Contains(w.feed)) Error(w.source, w.index, "Unknown substation: " + w.feed);
        return new JObject { ["schema_version"] = 1, ["kind"] = "scenery_preflight", ["scope"] = "combined source-level data validation",
            ["objects"] = count, ["sources"] = sources.Count, ["error_count"] = errorCount, ["warning_count"] = warningCount,
            ["data_valid"] = errorCount == 0, ["errors"] = errors, ["warnings"] = warnings,
            ["not_checked"] = new JArray("rail coverage", "mast/terrain collisions", "mechanical wire-run continuity", "registration-arm force", "neutral pantograph bridging", "in-game rendering"),
            ["constructors_executed"] = false, ["scene_changed"] = false };
    }
    private static Dictionary<string, string> Row(Dictionary<string, List<Dictionary<string, string>>> tables, string table, Func<Dictionary<string, string>, bool> predicate)
    {
        if (!tables.TryGetValue(table, out var rows)) throw new InvalidDataException("CSV missing: " + table);
        return rows.FirstOrDefault(predicate) ?? throw new InvalidDataException("Unknown model/type combination in " + table + ".csv");
    }
    private static string TypeKind(string? text)
    {
        if (text == null) throw new InvalidDataException("Missing $type.");
        string[] parts = text.Split(',');
        if (parts.Length < 2 || !Assemblies.Contains(parts[1].Trim())) throw new InvalidDataException("Unapproved assembly in $type.");
        string type = parts[0].Trim();
        string? prefix = Prefixes.FirstOrDefault(p => type.StartsWith(p, StringComparison.Ordinal));
        if (prefix == null || !Kinds.Contains(type.Substring(prefix.Length))) throw new InvalidDataException("Unapproved scenery class.");
        return type.Substring(prefix.Length);
    }
    private static double Number(JObject obj, string name)
    {
        JToken? token = obj[name];
        if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new InvalidDataException("Numeric field required: " + name);
        double n = (double)token;
        if (double.IsNaN(n) || double.IsInfinity(n)) throw new InvalidDataException("Non-finite " + name);
        return n;
    }
    private static int Integer(JObject obj, string name)
    {
        if (obj[name]?.Type != JTokenType.Integer) throw new InvalidDataException("Integer required: " + name);
        double n = Number(obj, name);
        if (n < int.MinValue || n > int.MaxValue) throw new InvalidDataException("Integer out of range: " + name);
        return (int)n;
    }
    private static string Text(JObject obj, string name)
    {
        if (obj[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)obj[name])) throw new InvalidDataException("Nonempty string required: " + name);
        return (string)obj[name]!;
    }
    private static bool Boolean(JObject obj, string name, bool fallback)
    {
        if (obj[name] == null) return fallback;
        if (obj[name]!.Type != JTokenType.Boolean) throw new InvalidDataException("Boolean required: " + name);
        return (bool)obj[name]!;
    }
    private static JToken Canonical(JToken token)
    {
        if (token is JObject obj) return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value))));
        if (token is JArray array) return new JArray(array.Select(Canonical));
        return token.DeepClone();
    }
}
