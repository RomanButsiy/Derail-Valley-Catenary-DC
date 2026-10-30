// CC0. Explicit, main-thread authoring operations. Nothing here installs a layout.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;
using CatenaryDC.Configuration;
using CatenaryDC.utilities;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
#if DEBUG
    private const string authoring_configuration = "Debug";
#else
    private const string authoring_configuration = "Release";
#endif
    internal static bool authoring_busy => _system != null && _system._survey_running;
    private sealed class SurveySource
    {
        internal readonly string Name, Kind;
        internal readonly int Index;
        internal SurveySource(string name, string kind, int index) { Name = name; Kind = kind; Index = index; }
    }
    private readonly Dictionary<catenary_object, SurveySource> _survey_sources = new();
    private readonly Dictionary<string, string> _survey_definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _survey_input_json = new(StringComparer.OrdinalIgnoreCase);
    private static bool _authoring_open, _authoring_meshes;
    private static float _authoring_radius = 800;
    private bool _survey_mesh_enabled_for_job;
    private float _survey_radius_for_job;
    private Vector3 _survey_player;
    private static string _authoring_label = "HB-CS-CMS", _authoring_candidate = "scenery_phase1.json";
    private static int _authoring_marker_kind;
    private static readonly string[] MarkerKinds = { "route_point", "route_start", "route_end", "no_wire_begin", "no_wire_end", "neutral_candidate", "substation_candidate", "clearance_issue" };
    private StreamWriter? _route_writer;
    private string? _route_destination;
    private int _route_samples;
    private float _route_next_time;
    private Vector3 _route_previous;

    private static Transform? authoring_player()
    {
        try { return PlayerManager.PlayerTransform; }
        catch { return null; } // Diagnostics must be usable before a player/world exists.
    }
    private static string authoring_root(string modPath) => Path.Combine(modPath, "UserData", "Authoring");
    private static string authoring_hash(string text)
    {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }
    private bool authoring_is_root(catenary_object item) => _survey_sources.ContainsKey(item) || !item.placed_procedurally;
    private JObject authoring_provenance(catenary_object item)
    {
        if (_survey_sources.TryGetValue(item, out SurveySource? source))
            return new JObject { ["source"] = source.Name, ["source_kind"] = source.Kind, ["source_index"] = source.Index,
                ["object_key"] = source.Name + "#" + source.Index, ["generated_child"] = false };
        return new JObject { ["source_kind"] = item.placed_procedurally ? "generated-child" : "editor-unsaved",
            ["generated_child"] = item.placed_procedurally, ["object_key"] = JValue.CreateNull() };
    }

    // Do NOT roundtrip a catenary object through its constructor just to inspect it:
    // pole/gantry constructors add foundations and far-side poles to the world.
    // Export only the declared JsonProperty members. This also avoids traversing Unity objects.
    private static JObject authoring_object_json(catenary_object item)
    {
        var result = new JObject { ["$type"] = "electric_sim.catenary.overhead_equipment+" + item.GetType().Name + ", electric_sim" };
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (Type? type = item.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo f in type.GetFields(flags))
                if (f.GetCustomAttributes(typeof(JsonPropertyAttribute), true).Length > 0) result[f.Name] = authoring_scalar(f.GetValue(item));
            foreach (PropertyInfo p in type.GetProperties(flags))
                if (p.GetIndexParameters().Length == 0 && p.GetCustomAttributes(typeof(JsonPropertyAttribute), true).Length > 0)
                    result[p.Name] = authoring_scalar(p.GetValue(item, null));
        }
        return result;
    }
    private static JToken authoring_scalar(object? value)
    {
        if (value == null) return JValue.CreateNull();
        if (value is Quaternion q) return survey_rotation(q);
        if (value is Vector3 v) return survey_xyz(v);
        if (value is Enum) return new JValue(value.ToString());
        if (value is string || value is bool || value.GetType().IsPrimitive || value is decimal) return new JValue(value);
        throw new InvalidDataException("Unexpected scenery data member: " + value.GetType().FullName);
    }

    public static void draw_authoring_gui(UnityModManager.ModEntry mod)
    {
        _authoring_open = GUILayout.Toggle(_authoring_open, "Authoring / JSON / survey tools (read-only exports)");
        if (!_authoring_open) return;
        GUILayout.Label("CatenaryDC devtools.1 | " + authoring_configuration + " | " + survey_state());
        GUILayout.Label("Exports: " + Path.Combine(authoring_root(mod.Path), "Exports"));
        GUILayout.Label(_survey_status);
        GUILayout.Label("Scene radius: " + _authoring_radius.ToString("0", CultureInfo.InvariantCulture) + " m. Tracks/network: all loaded, not a guaranteed full map.");
        _authoring_radius = Mathf.Round(GUILayout.HorizontalSlider(_authoring_radius, 100, 2000) / 50) * 50;
        _authoring_meshes = GUILayout.Toggle(_authoring_meshes, "Include readable candidate mesh vertices (larger export; limits apply)");
        overhead_equipment? current = _system;
        bool enabled = GUI.enabled;
        try
        {
            GUI.enabled = enabled && current != null && current._ready && !current._survey_running;
            if (GUILayout.Button("Export loaded world for electrification review")) current!.survey_start(mod.Path);
            if (GUILayout.Button("Export part catalog (CSV + prefab geometry)")) current!.survey_start(mod.Path, catalog: true);
            if (GUILayout.Button("Export loaded catenary JSON (authored roots only)")) authoring_action(() => current!.authoring_export_json(false));
            if (GUILayout.Button("Export nearby catenary JSON + inspect nearest object")) authoring_action(() => current!.authoring_export_json(true));
            GUILayout.Label("Marker label (also copied into a route recording):");
            _authoring_label = GUILayout.TextField(_authoring_label, 160);
            _authoring_marker_kind = GUILayout.SelectionGrid(_authoring_marker_kind, MarkerKinds, 2);
            if (GUILayout.Button("Save position / constraint marker")) authoring_action(() => current!.authoring_mark());
            if (current != null && current._route_writer != null)
            {
                GUILayout.Label("Recording player trajectory: " + current._route_samples + " points (not a rail centreline).");
                if (GUILayout.Button("Stop and save route recording")) authoring_action(() => current.authoring_stop_route(true));
            }
            else if (GUILayout.Button("Start route recording (player position, 5 m step)")) authoring_action(() => current!.authoring_start_route());
            GUILayout.Label("Optional candidate filename in UserData/Authoring/Inbox (validation ONLY):");
            _authoring_candidate = GUILayout.TextField(_authoring_candidate, 120);
            if (GUILayout.Button("Validate candidate JSON (do NOT apply)")) authoring_action(() => current!.authoring_validate());
            if (GUILayout.Button("Probe saved positions (Inbox/probes.json, no placement)")) current!.survey_start(mod.Path, probes: true);
#if DEBUG
            GUILayout.Label("The author editor remains below. Keep Part placement=Disabled and Eraser=false while surveying.");
            if (GUILayout.Button("Save editor workspace to UserData (Debug editor action)")) authoring_action(() =>
            {
                current!.store_scenery_now();
                _survey_status = "Saved editor workspace to UserData/scenery.json. This is a deliberate editor action, not a survey.";
            });
#endif
            GUI.enabled = enabled;
            if (current != null && current._survey_running && GUILayout.Button("Cancel export (retain incomplete .partial file)")) current._survey_cancel_requested = true;
            if ((current == null || !current._survey_running) && GUILayout.Button("Write diagnostics (also works before world load)")) authoring_action(() =>
            {
                string path = survey_destination(mod.Path, "diagnostics_", ".json");
                JObject data = survey_header(mod.Path); data["kind"] = "diagnostics"; data["state"] = survey_state();
                data["last_error"] = CatenaryDC.Api.CatenaryApi.LastError; data["last_status"] = _survey_status;
                authoring_write_new(path, data.ToString(Formatting.Indented));
                Directory.CreateDirectory(Path.Combine(authoring_root(mod.Path), "Inbox"));
                _survey_status = "Diagnostics written: " + path;
            });
        }
        finally { GUI.enabled = enabled; }
    }
    private static void authoring_action(Action action)
    {
        try { action(); Main.release_log(_survey_status); }
        catch (Exception error) { survey_failure("Authoring operation failed (no active layout installed)", error); }
    }
    private static void authoring_write_new(string path, string text)
    {
        // Unique destination: never overwrite an input, a layout, or a previous export.
        using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))) writer.Write(text);
    }

    private void authoring_export_json(bool nearby)
    {
        Transform? player = authoring_player();
        if (nearby && player == null) throw new InvalidOperationException("No player transform.");
        Vector3 center = player == null ? Vector3.zero : survey_absolute(player.position);
        var objects = new JArray(); var index = new JArray();
        int generated = 0;
        catenary_object? nearest = null; float nearestD = float.MaxValue;
        foreach (catenary_object item in _all_objects)
        {
            if (!authoring_is_root(item)) { generated++; continue; }
            float d = authoring_distance(item, center);
            if (d < nearestD) { nearest = item; nearestD = d; }
            if (nearby && d > _authoring_radius) continue;
            JObject data = authoring_object_json(item);
            JObject provenance = authoring_provenance(item); provenance["export_index"] = objects.Count;
            provenance["object_sha256_utf8"] = authoring_hash(data.ToString(Formatting.None));
            objects.Add(data); index.Add(provenance);
        }
        string path = survey_destination(_file_path, nearby ? "nearby_roots_" : "network_roots_", ".json");
        authoring_write_new(path, objects.ToString(Formatting.Indented));
        var meta = new JObject { ["schema_version"] = 1, ["kind"] = "scenery_snapshot_index", ["snapshot"] = Path.GetFileName(path),
            ["authored_roots"] = objects.Count, ["generated_children_excluded"] = generated,
            ["scope"] = nearby ? "nearby-root-subset" : "all-loaded-authored-roots",
            ["not_a_safe_additive_import"] = true, ["note"] = "This snapshot contains existing world objects. Do not install alongside their original sources. Nearby subsets may reference substations outside the subset.",
            ["objects"] = index };
        if (nearest != null)
            meta["nearest"] = new JObject { ["distance_m_horizontal"] = nearestD, ["data"] = authoring_object_json(nearest), ["provenance"] = authoring_provenance(nearest) };
        authoring_write_new(path + ".index.json", meta.ToString(Formatting.Indented));
        _survey_status = "JSON snapshot: " + path + "; roots=" + objects.Count + "; generated excluded=" + generated;
    }
    private static float authoring_distance(catenary_object obj, Vector3 center)
    {
        Vector3 a = new Vector3(obj.x / 1000f, obj.y, obj.z / 1000f), b = a;
        if (obj is wire w) b = a + obj.orientation * Vector3.forward * w.length;
        Vector3 delta = b - a; delta.y = 0;
        Vector3 from = center - a; from.y = 0;
        float fraction = delta.sqrMagnitude < .000001f ? 0 : Mathf.Clamp01(Vector3.Dot(from, delta) / delta.sqrMagnitude);
        Vector3 diff = from - delta * fraction;
        return diff.magnitude;
    }
    private void authoring_mark()
    {
        Transform? player = authoring_player();
        if (player == null) throw new InvalidOperationException("Load a world first.");
        var data = new JObject { ["schema_version"] = 1, ["kind"] = "authoring_marker", ["marker_kind"] = MarkerKinds[_authoring_marker_kind],
            ["label"] = _authoring_label, ["utc"] = DateTime.UtcNow.ToString("o"), ["position_absolute_m"] = survey_xyz(survey_absolute(player.position)),
            ["orientation"] = survey_rotation(player.rotation), ["origin_shift"] = survey_xyz(DV.OriginShift.OriginShift.currentMove),
            ["note"] = "Observed player position, NOT snapped to rail. A no-wire range needs both boundary markers and geometry review." };
        string path = survey_destination(_file_path, "marker_", ".json");
        authoring_write_new(path, data.ToString(Formatting.Indented)); _survey_status = "Marker saved: " + path;
    }
    private void authoring_start_route()
    {
        if (_route_writer != null) return;
        if (authoring_player() == null) throw new InvalidOperationException("No player.");
        _route_destination = survey_destination(_file_path, "route_", ".jsonl");
        _route_writer = new StreamWriter(new FileStream(_route_destination + ".partial", FileMode.CreateNew), new UTF8Encoding(false));
        _route_samples = 0; _route_next_time = 0;
        survey_write(_route_writer, new JObject { ["kind"] = "route_header", ["schema_version"] = 1, ["label"] = _authoring_label,
            ["step_m"] = 5, ["coordinate_frame"] = "absolute X/Z metres (minus OriginShift), Y unchanged",
            ["not_track_geometry"] = true, ["purpose"] = "Route selection and order only. Use RailTrack pointsets for track geometry." });
        _survey_status = "Route recording started. Travel the intended through route; stop/save at the endpoint.";
        authoring_tick();
    }
    internal void authoring_tick()
    {
        if (_route_writer == null || Time.realtimeSinceStartup < _route_next_time) return;
        _route_next_time = Time.realtimeSinceStartup + .25f;
        Transform? player = authoring_player();
        if (player == null) return;
        try
        {
            Vector3 position = survey_absolute(player.position);
            float distance = _route_samples == 0 ? 0 : Vector3.Distance(position, _route_previous);
            if (_route_samples > 0 && distance < 5) return;
            survey_write(_route_writer, new JObject { ["kind"] = "route_sample", ["index"] = _route_samples++,
                ["position_absolute_m"] = survey_xyz(position), ["orientation"] = survey_rotation(player.rotation),
                ["jump_candidate"] = distance > 100, ["distance_since_previous_m"] = distance,
                ["origin_shift"] = survey_xyz(DV.OriginShift.OriginShift.currentMove) });
            _route_previous = position; if (_route_samples % 50 == 0) _route_writer.Flush();
        }
        catch (Exception error)
        {
            try { authoring_stop_route(false); } catch (Exception cleanup) { Main.release_log(cleanup.Message); }
            survey_failure("Route recording failed", error);
        }
    }
    private void authoring_stop_route(bool complete)
    {
        StreamWriter? writer = _route_writer; _route_writer = null;
        if (writer == null) return;
        try { survey_write(writer, new JObject { ["kind"] = "route_footer", ["export_complete"] = complete, ["samples"] = _route_samples }); }
        finally { writer.Dispose(); }
        if (complete && _route_destination != null)
        {
            File.Move(_route_destination + ".partial", _route_destination);
            _survey_status = "Route saved: " + _route_destination;
        }
    }
    private void authoring_stop()
    {
        _survey_cancel_requested = true;
        survey_cleanup();
        try { authoring_stop_route(false); }
        catch (Exception error) { Main.release_log("Authoring cleanup: " + error.Message); }
    }
    private bool survey_in_radius(Bounds bounds)
    {
        Vector3 closest = bounds.ClosestPoint(_survey_player), d = closest - _survey_player;
        d.y = 0;
        return d.sqrMagnitude <= _survey_radius_for_job * _survey_radius_for_job;
    }
    private void authoring_validate()
    {
        if (!NetworkProfile.IsSceneryName(_authoring_candidate)) throw new InvalidDataException("Use a basename such as scenery_phase1.json, without directory separators.");
        string inbox = Path.Combine(authoring_root(_file_path), "Inbox"); Directory.CreateDirectory(inbox);
        string input = Path.Combine(inbox, _authoring_candidate);
        if (!File.Exists(input)) throw new FileNotFoundException("Put the candidate here first: " + input);
        if (new FileInfo(input).Length > 16 * 1024 * 1024) throw new InvalidDataException("Candidate exceeds 16 MiB.");
        string raw = File.ReadAllText(input);
        var combined = new Dictionary<string, string>(_survey_input_json, StringComparer.OrdinalIgnoreCase) { [_authoring_candidate] = raw };
        JObject report = CatenaryDC.Authoring.SceneryReview.Validate(combined, _survey_definitions);
        report["candidate"] = _authoring_candidate; report["installed"] = false;
        NetworkProfile profile = NetworkProfile.Load(_file_path);
        report["selected_if_installed"] = profile.load_external_scenery && (profile.included_scenery == null
            || profile.included_scenery.Contains(_authoring_candidate, StringComparer.OrdinalIgnoreCase));
        report["comparison_basis"] = "Selected source files captured at world load; unsaved Debug edits are not included.";
        report["loaded_object_count_before_after"] = _all_objects.Count;
        report["input_sha256_utf8"] = authoring_hash(raw);
        string output = survey_destination(_file_path, "validation_", ".json");
        authoring_write_new(output, report.ToString(Formatting.Indented));
        _survey_status = "Validation only: errors=" + report["error_count"] + "; report: " + output;
    }
}
