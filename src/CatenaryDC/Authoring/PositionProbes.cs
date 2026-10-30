// CC0. Explicit offline-planner probes of loaded colliders. Does not position objects.
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CatenaryDC.utilities;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    private IEnumerator export_probes(string modPath)
    {
        string input = Path.Combine(authoring_root(modPath), "Inbox", "probes.json");
        if (!File.Exists(input)) throw new FileNotFoundException("Place planner probes here first: " + input);
        if (new FileInfo(input).Length > 4 * 1024 * 1024) throw new InvalidDataException("Probe input exceeds 4 MiB.");
        JObject request;
        using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(input))) { MaxDepth = 12 })
            request = JObject.Load(reader);
        if ((int?)request["schema_version"] != 1 || !(request["points"] is JArray points) || points.Count > 2048)
            throw new InvalidDataException("Expected schema_version=1 and points array (max 2048). See docs/AUTHORING_TOOLS_UK.md.");
        string destination = survey_destination(modPath, "probes_", ".jsonl");
        int errors = 0;
        using (var output = new StreamWriter(new FileStream(destination + ".partial", FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
        {
            _survey_writer = output;
            JObject header = survey_header(modPath); header["kind"] = "probe_header";
            header["input_sha256"] = survey_fingerprint(input)["sha256"];
            header["note"] = "Vertical loaded-collider observations only. No hits does not prove free space. Trees/wires/render-only objects may have no colliders.";
            survey_write(output, header);
            for (int i = 0; i < points.Count; ++i)
            {
                survey_check();
                try
                {
                    JObject point = points[i] as JObject ?? throw new InvalidDataException("Probe must be an object.");
                    JObject pos = point["position_m"] as JObject ?? throw new InvalidDataException("position_m object required.");
                    float x = probe_number(pos, "x", -1000000, 1000000), y = probe_number(pos, "y", -10000, 10000), z = probe_number(pos, "z", -1000000, 1000000);
                    float above = point["above_m"] == null ? 20 : probe_number(point, "above_m", 1, 200);
                    float below = point["below_m"] == null ? 50 : probe_number(point, "below_m", 1, 200);
                    Vector3 relative = world_position.get_relative_position(world_position.float_to_fixed(x), world_position.float_to_fixed(z), y);
                    RaycastHit[] hits = Physics.RaycastAll(relative + Vector3.up * above, Vector3.down, above + below, ~0, QueryTriggerInteraction.Ignore);
                    var records = new JArray();
                    foreach (RaycastHit hit in hits.OrderBy(h => h.distance))
                    {
                        if (hit.collider == null) continue;
                        records.Add(new JObject { ["point_absolute_m"] = survey_xyz(survey_absolute(hit.point)), ["normal"] = survey_xyz(hit.normal),
                            ["distance_from_ray_origin_m"] = hit.distance, ["collider_path"] = survey_path(hit.collider.transform),
                            ["collider_type"] = hit.collider.GetType().FullName, ["layer"] = hit.collider.gameObject.layer });
                    }
                    survey_write(output, new JObject { ["kind"] = "position_probe", ["index"] = i, ["id"] = (string?)point["id"] ?? i.ToString(CultureInfo.InvariantCulture),
                        ["requested_absolute_m"] = survey_xyz(new Vector3(x, y, z)), ["above_m"] = above, ["below_m"] = below,
                        ["hits"] = records, ["scene_geometry_complete"] = false });
                }
                catch (Exception e) { errors++; survey_write(output, new JObject { ["kind"] = "record_error", ["index"] = i, ["error"] = e.GetBaseException().Message }); }
                if (i % 16 == 0) { _survey_status = "Position probes: " + i + "/" + points.Count; yield return null; }
            }
            survey_check(); survey_write(output, new JObject { ["kind"] = "footer", ["export_complete"] = true, ["probes"] = points.Count, ["record_errors"] = errors });
        }
        _survey_writer = null; File.Move(destination + ".partial", destination);
        _survey_status = "Position probes saved: " + destination + "; errors=" + errors;
        Main.release_log(_survey_status);
    }
    private static float probe_number(JObject obj, string name, float minimum, float maximum)
    {
        JToken? value = obj[name];
        if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) throw new InvalidDataException("Numeric " + name + " required.");
        double number = (double)value;
        if (double.IsNaN(number) || double.IsInfinity(number) || number < minimum || number > maximum) throw new InvalidDataException("Out-of-range " + name);
        return (float)number;
    }
}
