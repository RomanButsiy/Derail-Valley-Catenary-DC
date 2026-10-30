// CC0. Read-only, user-triggered authoring survey for CatenaryDC (Debug AND Release).
// Does not create scenery, change power supplies, modify saves or load asset bundles.
using System;
using System.Collections;
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
using CatenaryDC.utilities;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    private const string survey_version = "catenarydc-devtools.1";
    private const string survey_base_commit = "4d1a6a691415b6f08f9a76fbafdc8357485e277a";
    private const int survey_mesh_vertex_limit = 20000;
    private const int survey_mesh_total_vertex_limit = 1000000;
    private bool _survey_running, _survey_cancel_requested;
    private StreamWriter? _survey_writer;
    private Vector3 _survey_origin;
    private static string _survey_status = "No survey exported in this game process.";
    private static readonly Dictionary<string, MethodInfo?> _survey_methods = new();

    // Export is explicitly requested by the user; provenance bookkeeping does not change source selection.
    private static string survey_state()
    {
        if (Type.GetType("DoubleTrack.TrackPlacerEntry, DoubleTrack", throwOnError: false) != null)
            return "DOUBLETRACK_DETECTED: This layout disables catenary when DoubleTrack is installed.";
        overhead_equipment? current = _system;
        if (current == null || !current._ready)
            return "NO_CATENARY: load a world. If the world is already loaded, check Player.log for the FIRST loading exception.";
        if (current._OCS_ticker == null)
            return "INITIALIZATION_INCOMPLETE: catenary exists but its update object was not created. Check Player.log.";
        OCS_ticker? host = current._OCS_ticker.GetComponent<OCS_ticker>();
        if (host == null || !host.isActiveAndEnabled)
            return "HOST_UNAVAILABLE: the catenary update component is missing or inactive.";
        if (current._survey_running)
            return "EXPORTING: remain still; do not teleport or reload the world.";
        return "READY: loaded catenary objects: " + current._all_objects.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void survey_start(string mod_path, bool catalog = false, bool probes = false)
    {
        if (_survey_running) return;
        try
        {
            OCS_ticker? host = _OCS_ticker == null ? null : _OCS_ticker.GetComponent<OCS_ticker>();
            if (host == null || !host.isActiveAndEnabled)
                throw new InvalidOperationException("Catenary coroutine host is not available.");
            _survey_running = true;
            _survey_cancel_requested = false;
            _survey_origin = DV.OriginShift.OriginShift.currentMove;
            _survey_mesh_enabled_for_job = _authoring_meshes;
            _survey_radius_for_job = _authoring_radius;
            Transform? player = authoring_player();
            _survey_player = player == null ? Vector3.zero : player.position;
            _survey_status = "Starting export. Remain still; no scene objects will be changed.";
            UnloadWatcher.UnloadRequested += survey_on_unload;
            host.StartCoroutine(survey_guard(probes ? export_probes(mod_path) : catalog ? export_catalog(mod_path) : export_survey(mod_path)));
        }
        catch (Exception e)
        {
            survey_cleanup();
            survey_failure("Could not start survey", e);
        }
    }

    private static void survey_failure(string prefix, Exception e)
    {
        _survey_status = prefix + ": " + e.GetBaseException().Message + " See Player.log. A .partial file is NOT a completed export.";
        Main.release_log(_survey_status + "\n" + e);
    }

    private void survey_check()
    {
        if (_survey_cancel_requested || !ReferenceEquals(_system, this))
            throw new OperationCanceledException("Export cancelled or world unloaded.");
        Transform? player = authoring_player();
        if (player != null && (player.position - _survey_player).sqrMagnitude > 4)
            throw new OperationCanceledException("Player moved more than 2 m during export; stop the train and try again.");
        Vector3 origin = DV.OriginShift.OriginShift.currentMove;
        if ((origin - _survey_origin).sqrMagnitude > 0.000001f)
            throw new OperationCanceledException("World origin changed during export. Remain still and export again.");
    }

    private void survey_on_unload()
    {
        _survey_cancel_requested = true;
        _survey_status = "Export interrupted by world unload. Any .partial file is incomplete.";
        survey_cleanup(); // Unity may stop a coroutine when its host is destroyed.
    }

    private void survey_cleanup()
    {
        UnloadWatcher.UnloadRequested -= survey_on_unload;
        StreamWriter? writer = _survey_writer;
        _survey_writer = null;
        _survey_running = false;
        if (writer != null)
        {
            try { writer.Dispose(); }
            catch (Exception e) { Main.release_log("Survey writer cleanup failed: " + e); }
        }
    }

    private IEnumerator survey_guard(IEnumerator work)
    {
        try
        {
            while (true)
            {
                bool next;
                try { survey_check(); next = work.MoveNext(); }
                catch (Exception e) { survey_failure("Survey stopped", e); break; }
                if (!next) break;
                yield return work.Current;
            }
        }
        finally
        {
            try { (work as IDisposable)?.Dispose(); }
            finally { survey_cleanup(); }
        }
    }

    private static string survey_destination(string mod_path, string prefix, string suffix)
    {
        string directory = Path.Combine(mod_path, "UserData", "Authoring", "Exports");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, prefix + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
            + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + suffix);
    }

    private static JObject survey_xyz(Vector3 v) => new() { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
    private static JObject survey_rotation(Quaternion q) => new() { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };
    private static Vector3 survey_absolute(Vector3 relative)
    {
        Vector3 shift = DV.OriginShift.OriginShift.currentMove;
        return new Vector3(relative.x - shift.x, relative.y, relative.z - shift.z);
    }
    private static JArray survey_matrix(Transform transform)
    {
        Matrix4x4 matrix = transform.localToWorldMatrix;
        var values = new JArray();
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++) values.Add(matrix[row, column]);
        return values;
    }
    private static string survey_path(Transform value)
    {
        var parts = new List<string>();
        for (Transform? t = value; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
    private static object? survey_member(object value, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type? type = value.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(value, null);
            FieldInfo? field = type.GetField(name, flags);
            if (field != null) return field.GetValue(value);
        }
        return null;
    }
    // Resolve the game's readers without adding a dependency on a particular PointSet DLL.
    private static object? survey_call(object receiver, string name)
    {
        string key = receiver.GetType().AssemblyQualifiedName + ":" + name;
        if (!_survey_methods.TryGetValue(key, out MethodInfo? method))
        {
            method = receiver.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (method == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string an = assembly.GetName().Name ?? "";
                    if (!an.StartsWith("Assembly-CSharp", StringComparison.Ordinal) && !an.StartsWith("DV.", StringComparison.Ordinal)) continue;
                    Type?[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types; }
                    foreach (Type? type in types)
                    {
                        if (type == null || !type.IsAbstract || !type.IsSealed) continue;
                        method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            .FirstOrDefault(m => m.Name == name && !m.ContainsGenericParameters && m.GetParameters().Length == 1
                                && m.GetParameters()[0].ParameterType.IsInstanceOfType(receiver));
                        if (method != null) break;
                    }
                    if (method != null) break;
                }
            }
            _survey_methods[key] = method; // Cache missing methods as well as successful lookups.
        }
        if (method == null)
        {
            object? member = survey_member(receiver, name);
            return member ?? throw new MissingMethodException(receiver.GetType().FullName, name);
        }
        return method.Invoke(method.IsStatic ? null : receiver, method.IsStatic ? new[] { receiver } : null);
    }
    private static JObject survey_native_xyz(object position)
    {
        var result = new JObject();
        foreach (string axis in new[] { "x", "y", "z" })
        {
            object value = survey_member(position, axis) ?? throw new InvalidDataException("Missing coordinate " + axis);
            double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new InvalidDataException("Non-finite track point.");
            result[axis] = d;
        }
        return result;
    }
    private static JObject survey_fingerprint(string path)
    {
        var result = new JObject { ["name"] = Path.GetFileName(path), ["exists"] = File.Exists(path) };
        if (!File.Exists(path)) return result;
        try
        {
            result["bytes"] = new FileInfo(path).Length;
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = File.OpenRead(path))
                result["sha256"] = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception e) { result["error"] = e.Message; }
        return result;
    }
    private static JObject survey_header(string mod_path)
    {
        Transform? player = authoring_player();
        var assets = new JArray
        {
            survey_fingerprint(Path.Combine(mod_path, "CatenaryDC.dll")),
            survey_fingerprint(Path.Combine(mod_path, "catenary")),
            survey_fingerprint(Path.Combine(mod_path, "catenary_parts"))
        };
        if (!string.IsNullOrEmpty(alternate_parts_file)) assets.Add(survey_fingerprint(alternate_parts_file!));
        foreach (string name in new[] { "network.json", "build-info.json" })
            assets.Add(survey_fingerprint(Path.Combine(mod_path, name)));
        return new JObject
        {
            ["kind"] = "header", ["schema_version"] = 3, ["exporter_version"] = survey_version, ["base_commit"] = survey_base_commit,
            ["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ["game_version"] = Application.version,
            ["unity_version"] = Application.unityVersion, ["railtrack_assembly"] = typeof(RailTrack).Assembly.FullName,
            ["scene_geometry_complete"] = false, ["track_geometry_complete"] = false,
            ["origin_shift_at_start"] = survey_xyz(DV.OriginShift.OriginShift.currentMove),
            ["coordinates"] = "Scene *_m coordinates: metres, world X/Z minus OriginShift, Y unchanged. Scenery x/z are fixed millimetres; scenery y is metres. Do not infer a height offset from x/z units.",
            ["pointset_coordinates"] = "points_native_m are native game values: verify their coordinate frame against known tracks before placement.",
            ["mesh_coordinates"] = "vertices_local_m -> local_to_world_row_major -> subtract origin_shift.x/z only; Y unchanged.",
            ["player_absolute_m"] = player == null ? JValue.CreateNull() : (JToken)survey_xyz(survey_absolute(player.position)),
            ["scene_radius_m"] = _system?._survey_radius_for_job ?? _authoring_radius,
            ["build_configuration"] = authoring_configuration,
            ["assets"] = assets, ["alternate_parts_active"] = !string.IsNullOrEmpty(alternate_parts_file),
            ["mesh_limits"] = new JObject { ["vertices_per_mesh"] = survey_mesh_vertex_limit, ["total_vertices"] = survey_mesh_total_vertex_limit },
            ["notes"] = "Loaded objects only; not a full map or a clearance certificate. Keyword matches do not prove a power-line crossing. Physical gaps remain unchanged."
        };
    }
    private static void survey_write(TextWriter output, JObject data) => output.WriteLine(data.ToString(Formatting.None));

    // Read-only contact geometry; simulation methods remain unchanged.
    private partial class wire
    {
        public JObject survey_record()
        {
            wire_template info = _wire_sections[wire_type];
            float dead_length = info.end_anchor ? end_anchor_dead_length : 0.0f;
            Vector3 offset = orientation * Vector3.forward * (length - dead_length);
            Vector3 start = new(x / world_position.fixed_multiplier, y + _contact_height, z / world_position.fixed_multiplier);
            Vector3 end = new((x + world_position.float_to_fixed(offset.x)) / world_position.fixed_multiplier,
                _other_end_y + _contact_height, (z + world_position.float_to_fixed(offset.z)) / world_position.fixed_multiplier);
            return new JObject
            {
                ["kind"] = "existing_wire", ["wire_type"] = wire_type.ToString(), ["substation"] = substation,
                ["is_neutral"] = string.Equals(substation, "NEUTRAL", StringComparison.Ordinal),
                ["x_mm"] = x, ["z_mm"] = z, ["base_y_m"] = y, ["orientation"] = survey_rotation(orientation),
                ["authored_length_m"] = length, ["previous_pole_vertical_offset_m"] = previous_pole_vertical_offset,
                ["anchor_dead_length_m"] = dead_length, ["contact_start_m"] = survey_xyz(start), ["contact_end_m"] = survey_xyz(end),
                ["geometry_meaning"] = "Runtime pantograph contact centreline, including runtime end-anchor shortening and height; not the rendered mesh or its full clearance envelope."
            };
        }
    }

    private IEnumerator export_survey(string mod_path)
    {
        string destination = survey_destination(mod_path, "survey_", ".jsonl");
        string temporary = destination + ".partial";
        int track_ok = 0, track_records = 0, errors = 0, renderer_count = 0, wire_count = 0, neutral_count = 0;
        int scenery_count = 0, mesh_count = 0, mesh_vertices = 0, colliders_exported = 0, terrains_exported = 0;
        var seen_meshes = new HashSet<int>();
        using (var output = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
        {
            _survey_writer = output;
            survey_write(output, survey_header(mod_path));
            // The exact CSVs and loaded root-source JSON allow an offline authoring tool
            // to validate a candidate without instantiating any Unity objects.
            foreach (var item in _survey_definitions.OrderBy(x => x.Key, StringComparer.Ordinal))
                survey_write(output, new JObject { ["kind"] = "definition_csv", ["name"] = item.Key, ["text"] = item.Value });
            foreach (var item in _survey_input_json.OrderBy(x => x.Key, StringComparer.Ordinal))
                survey_write(output, new JObject { ["kind"] = "source_layout", ["name"] = item.Key, ["sha256_utf8"] = authoring_hash(item.Value), ["objects"] = JArray.Parse(item.Value) });
            RailTrack[] tracks = Resources.FindObjectsOfTypeAll<RailTrack>();
            for (int i = 0; i < tracks.Length; i++)
            {
                survey_check();
                RailTrack track = tracks[i];
                if (track == null) continue;
                var data = new JObject { ["kind"] = "track", ["instance_id"] = track.GetInstanceID() };
                try
                {
                    if (!track.gameObject.scene.IsValid() || !track.gameObject.scene.isLoaded) continue;
                    data["path"] = survey_path(track.transform);
                    data["scene"] = track.gameObject.scene.name;
                    data["transform_absolute_m"] = survey_xyz(survey_absolute(track.transform.position));
                    data["local_to_world_row_major"] = survey_matrix(track.transform);
                    data["origin_shift"] = survey_xyz(DV.OriginShift.OriginShift.currentMove);
                    try
                    {
                        object logic = survey_call(track, "LogicTrack") ?? throw new InvalidDataException("Missing LogicTrack.");
                        string? id = Convert.ToString(survey_member(logic, "ID"), CultureInfo.InvariantCulture);
                        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Empty track ID.");
                        data["id"] = id;
                        data["logic_snapshot"] = survey_shallow_fields(logic, 2);
                        data["logic_snapshot_note"] = "Observed fields/references only; adjacency is not guessed from proximity.";
                    }
                    catch (Exception e) { data["id_error"] = e.GetBaseException().Message; errors++; }
                    object set = survey_call(track, "GetKinkedPointSet") ?? throw new InvalidDataException("Missing track point set.");
                    data["pointset_type"] = set.GetType().FullName;
                    var points = survey_member(set, "points") as IEnumerable ?? throw new InvalidDataException("Point set is not enumerable.");
                    var native = new JArray();
                    foreach (object point in points) native.Add(survey_native_xyz(survey_member(point, "position") ?? point));
                    if (native.Count < 2) throw new InvalidDataException("Fewer than two track points.");
                    data["points_native_m"] = native;
                    data["component_snapshot"] = survey_shallow_fields(track, 1);
                    data["transform_chain"] = survey_transform_chain(track.transform);
                    data["native_polyline_length_m"] = survey_polyline_length(native);
                    data["native_frame_confirmed"] = false;
                    track_ok++;
                }
                catch (Exception e) { data["error"] = e.GetBaseException().Message; errors++; }
                survey_write(output, data);
                track_records++;
                if (i % 10 == 0) { _survey_status = $"Survey: tracks {i + 1}/{tracks.Length}"; yield return null; }
            }
            catenary_object[] scenery = _all_objects.ToArray();
            for (int i = 0; i < scenery.Length; i++)
            {
                survey_check();
                catenary_object obj = scenery[i];
                try
                {
                    JObject data = obj is wire w ? w.survey_record() : new JObject { ["kind"] = "existing_scenery" };
                    data["object_index"] = i;
                    data["object_type"] = obj.GetType().Name;
                    data["data"] = authoring_object_json(obj);
                    data["provenance"] = authoring_provenance(obj);
                    data["authored_root"] = authoring_is_root(obj);
                    data["position_absolute_m"] = survey_xyz(new Vector3(obj.x / 1000.0f, obj.y, obj.z / 1000.0f));
                    if (obj is pole_user p) data["physical_mast_absolute_m"] = survey_xyz(survey_absolute(p.get_pole_true_position()));
                    if (obj is cantilever c) data["registration_base_m"] = survey_xyz(c.authoring_registration_base());
                    if (obj is wire)
                    {
                        wire_count++;
                        if (string.Equals(((wire)obj).substation, "NEUTRAL", StringComparison.Ordinal)) neutral_count++;
                    }
                    else scenery_count++;
                    survey_write(output, data);
                }
                catch (Exception e)
                {
                    survey_write(output, new JObject { ["kind"] = "record_error", ["stage"] = "catenary", ["error"] = e.GetBaseException().Message });
                    errors++;
                }
                if (i % 100 == 0) { _survey_status = $"Survey: existing scenery {i + 1}/{scenery.Length}"; yield return null; }
            }
            Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            string[] keywords = { "power", "electric", "transmission", "pylon", "voltage", "utility", "cable", "wire", "tower", "dalekovod", "bridge", "tunnel", "wall", "signal", "buffer", "platform" };
            for (int i = 0; i < renderers.Length; i++)
            {
                survey_check();
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                var data = new JObject { ["kind"] = "renderer", ["instance_id"] = renderer.GetInstanceID() };
                try
                {
                    if (!renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded) continue;
                    if (!survey_in_radius(renderer.bounds)) continue;
                    string path = survey_path(renderer.transform);
                    string lower = path.ToLowerInvariant();
                    bool candidate = keywords.Any(lower.Contains);
                    data["path"] = path;
                    data["scene"] = renderer.gameObject.scene.name;
                    data["renderer_type"] = renderer.GetType().FullName;
                    data["active"] = renderer.gameObject.activeInHierarchy;
                    data["enabled"] = renderer.enabled;
                    data["bounds_center_m"] = survey_xyz(survey_absolute(renderer.bounds.center));
                    data["bounds_size_m"] = survey_xyz(renderer.bounds.size);
                    data["keyword_candidate_only"] = candidate;
                    data["transform_chain"] = survey_transform_chain(renderer.transform);
                    data["component_types"] = new JArray(renderer.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName));
                    data["local_to_world_row_major"] = survey_matrix(renderer.transform);
                    data["origin_shift"] = survey_xyz(DV.OriginShift.OriginShift.currentMove);
                    if (candidate)
                    {
                        data["local_to_world_row_major"] = survey_matrix(renderer.transform);
                        data["origin_shift"] = survey_xyz(DV.OriginShift.OriginShift.currentMove);
                        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
                        Mesh? mesh = filter == null ? null : filter.sharedMesh;
                        data["mesh_readable"] = mesh != null && mesh.isReadable;
                        if (mesh == null) data["mesh_omission"] = "No MeshFilter/sharedMesh; bounds only (e.g. line or skinned renderer).";
                        else
                        {
                            int id = mesh.GetInstanceID();
                            data["mesh_id"] = id;
                            if (seen_meshes.Add(id))
                            {
                                var mesh_data = new JObject { ["kind"] = "mesh_geometry", ["mesh_id"] = id, ["name"] = mesh.name,
                                    ["vertex_count"] = mesh.vertexCount, ["readable"] = mesh.isReadable, ["exported"] = false };
                                try
                                {
                                    if (!_survey_mesh_enabled_for_job) mesh_data["omission"] = "Mesh export was not enabled.";
                                    else if (!mesh.isReadable) mesh_data["omission"] = "Mesh is not readable.";
                                    else if (mesh.vertexCount > survey_mesh_vertex_limit || mesh_vertices + mesh.vertexCount > survey_mesh_total_vertex_limit)
                                        mesh_data["omission"] = "Vertex budget limit; bounds only.";
                                    else
                                    {
                                        int[] triangles = mesh.triangles;
                                        if (triangles.Length > 120000) mesh_data["omission"] = "Triangle index budget limit; bounds only.";
                                        else
                                        {
                                            mesh_data["vertices_local_m"] = new JArray(mesh.vertices.Select(survey_xyz));
                                            mesh_data["triangle_indices"] = new JArray(triangles);
                                            mesh_data["exported"] = true;
                                            mesh_vertices += mesh.vertexCount;
                                        }
                                    }
                                }
                                catch (Exception e) { mesh_data["error"] = e.GetBaseException().Message; errors++; }
                                survey_write(output, mesh_data);
                                mesh_count++;
                            }
                        }
                    }
                }
                catch (Exception e) { data["error"] = e.GetBaseException().Message; errors++; }
                survey_write(output, data);
                renderer_count++;
                if (i % 100 == 0) { _survey_status = $"Survey: loaded renderers {i + 1}/{renderers.Length}"; yield return null; }
            }
            Collider[] colliders = Resources.FindObjectsOfTypeAll<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                survey_check();
                Collider c = colliders[i];
                if (c == null || !c.gameObject.scene.IsValid() || !c.gameObject.scene.isLoaded || !survey_in_radius(c.bounds)) continue;
                try
                {
                    JObject record = survey_collider(c);
                    survey_write(output, record); colliders_exported++;
                }
                catch (Exception e) { survey_write(output, new JObject { ["kind"] = "record_error", ["stage"] = "collider", ["error"] = e.GetBaseException().Message }); errors++; }
                if (i % 100 == 0) { _survey_status = "Survey: colliders " + i + "/" + colliders.Length; yield return null; }
            }
            Terrain[] terrains = Resources.FindObjectsOfTypeAll<Terrain>();
            foreach (Terrain terrain in terrains)
            {
                survey_check();
                if (terrain == null || !terrain.gameObject.scene.IsValid() || !terrain.gameObject.scene.isLoaded) continue;
                try
                {
                    TerrainData td = terrain.terrainData;
                    if (td != null)
                    {
                        Bounds bounds = new Bounds(terrain.transform.position + td.size * .5f, td.size);
                        if (survey_in_radius(bounds)) { survey_write(output, survey_terrain(terrain)); terrains_exported++; }
                    }
                }
                catch (Exception e) { survey_write(output, new JObject { ["kind"] = "record_error", ["stage"] = "terrain", ["error"] = e.GetBaseException().Message }); errors++; }
                yield return null;
            }
            survey_check();
            survey_write(output, new JObject
            {
                ["kind"] = "footer", ["export_complete"] = true, ["scene_geometry_complete"] = false, ["track_geometry_complete"] = false,
                ["track_records"] = track_records, ["tracks_exported"] = track_ok, ["existing_wires"] = wire_count, ["neutral_wires"] = neutral_count,
                ["existing_scenery"] = scenery_count, ["renderers_exported"] = renderer_count, ["colliders_exported"] = colliders_exported, ["terrains_exported"] = terrains_exported, ["mesh_records"] = mesh_count,
                ["mesh_vertices_exported"] = mesh_vertices, ["record_errors"] = errors,
                ["origin_shift_at_end"] = survey_xyz(DV.OriginShift.OriginShift.currentMove),
                ["track_review_possible"] = track_ok > 0, ["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            });
        }
        _survey_writer = null;
        File.Move(temporary, destination); // Only a fully written and closed export receives the .jsonl suffix.
        _survey_status = $"Survey written: {destination}. Tracks: {track_ok}; wires: {wire_count}; record errors: {errors}. Loaded scene only.";
        if (track_ok == 0) _survey_status += " WARNING: no usable tracks; send this file for diagnosis before collecting more surveys.";
        Main.release_log(_survey_status);
    }
}
