// Distributed under terms and conditions of CC0 licence. See LICENSE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using UnityEngine;

using DV.Utils;

using CatenaryDC.catenary_editor;
using CatenaryDC.Api;
using CatenaryDC.Configuration;
using CatenaryDC.utilities;

using static CatenaryDC.utilities.world_position;
using static UnityModManagerNet.UnityModManager;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    const int scenery_tree_objects_per_node = 64, wires_tree_objects_per_node = 4;
    
    public enum pole_kind { Ground, Bridge, Tunnel, Bracket, SideRail, PlatformArch, BridgeInsulators };
    public enum cantilever_kind { Inner, OutwardsInner, MiddleInner, Middle, InwardsOuter, Outer };
    public enum steady_arm_kind { cantilever, gantry, truss_gantry, tunnel, trolley, intermediate };
    public enum wire_kind { plain_dual, plain_single, plain_quad, end_anchor_dual, end_anchor_single, end_anchor_quad, 
        wall_anchor_single, middle_anchor_dual, middle_anchor_single, middle_anchor_quad, side_rail, termination_rail,
        trolley, trolley_anchor, plain_half1_dual, plain_half2_dual, plain_half1_single, plain_half2_single,
        plain_half1_quad, plain_half2_quad, wall_anchor_half_single };

    public const float default_pole_offset      = 2.2f;
    public const int   minimum_redering_distance = 10;

    private static readonly Dictionary<string, string> _all_parts = new(StringComparer.Ordinal)
    {
#if DEBUG
        ["GantryArrow"] = "Gantries/GantryArrow"
#endif
    };

    private static overhead_equipment? _system;
    private static int                 _default_rendering_distance = 500;

    private int  _last_x, _last_z;
    private int  _visible_distance_fixed = _default_rendering_distance * fixed_divider, _visibility_check_distance = (_default_rendering_distance * fixed_divider) >> 3;
    //private float _remaining_time = 1.0f;
    private bool _scenery_changed = true;

    private readonly Dictionary<string, GameObject> _templates = [];
    private readonly string _file_path;
    private readonly List<catenary_object> _all_objects = [], _nearby_wires = [];
#if DEBUG
    private readonly List<catenary_object> _freshly_added_objects = [];
#endif

    private GameObject? _OCS_ticker;

    private List<    AssetBundle> _loaded_bundles = [];
    private List<catenary_object> _previously_visible_objects = [], _currently_visible_objects = [];
    private quad_tree _object_tree = new([], scenery_tree_objects_per_node), _wires_tree = new([], wires_tree_objects_per_node);

    private substation[]? _all_substations = null;

    public static string? alternate_parts_file { get; set; } = null;
    public static bool is_ready => _system != null && _system._ready;
    public static int loaded_object_count => _system?._all_objects.Count ?? 0;
    private bool _ready;
    private WorldMover? _floating_origin;
    private int _wire_search_half_area = 60 * fixed_divider;

    public static overhead_equipment system => _system ?? throw new InvalidOperationException("Catenary not present");

    
    public static int rendering_distance
    {
        get => _default_rendering_distance;
        set
        {
            value = Math.Max(minimum_redering_distance, Math.Min(10000, value));
            _default_rendering_distance = value;
            if (_system != null)
            {
                _system._visible_distance_fixed    = value * fixed_divider;
                _system._visibility_check_distance = _system._visible_distance_fixed >> 3;
                _system._scenery_changed           = true;
            }
        }
    }

    private static CSV_struct<_type_> load_part_definitions<_type_>(AssetBundle parts, string definition_file) 
        where _type_: struct, catenary_object_template
    {
        string raw_part_definitions = parts.LoadAsset<TextAsset>($"Assets/Catenary/{definition_file}.csv")?.text
            ?? throw new FileNotFoundException($"No definition {definition_file} found");
        var CSV_contents = new CSV_struct<_type_>(raw_part_definitions);
        for (int index = CSV_contents.row_count; index > 0; --index)
        {
            _type_ definition = CSV_contents.get_row(index);
            if (!string.IsNullOrWhiteSpace(definition.template_name) && !string.IsNullOrWhiteSpace(definition.asset_path))
                _all_parts[definition.template_name] = definition.asset_path;
            Main.log($"OCSD '{definition.template_name}' '{definition.asset_path}'");
        }
        return CSV_contents;
    }
    
    private overhead_equipment(ModEntry mod)
    {
        _file_path = mod.Path;
    }

    private void load_templates()
    {
        _all_parts.Clear();
#if DEBUG
        _all_parts["GantryArrow"] = "Gantries/GantryArrow";
#endif
        AssetBundle? catenary_assets = null;
        if (!string.IsNullOrEmpty(alternate_parts_file))
            catenary_assets = AssetBundle.LoadFromFile(alternate_parts_file);
        if (catenary_assets == null)
        {
            catenary_assets = AssetBundle.LoadFromFile(Path.Combine(_file_path, "catenary_parts"))
                           ?? throw new FileNotFoundException("Not found " + Path.Combine(_file_path, "catenary_parts"));
        }
        
        // Register ownership before parsing: failure rollback must unload this bundle.
        _loaded_bundles.Add(catenary_assets);
        load_part_definitions<miscellaneous_object_template>(catenary_assets, "miscellaneous");
        pole.set_up_templates          (load_part_definitions<      pole_template>(catenary_assets,       "poles"));
        side_rail_pole.set_up_templates(load_part_definitions< side_pole_template>(catenary_assets,  "side_poles"));
        gantry.set_up_templates        (load_part_definitions<    gantry_template>(catenary_assets,    "gantries"));
        cantilever.set_up_templates    (load_part_definitions<cantilever_template>(catenary_assets, "cantilevers"));
        wire.set_up_templates          (load_part_definitions<      wire_template>(catenary_assets,       "wires"));
        
        foreach (KeyValuePair<string, string> current_part in _all_parts)
        {
            _templates[current_part.Key] = catenary_assets.LoadAsset<GameObject>($"Assets/Catenary/{current_part.Value}.prefab")
                ?? throw new FileNotFoundException($"No {current_part.Value} prefab");
        }

        _floating_origin = SingletonBehaviour<WorldMover>.Instance;
        if (_floating_origin == null) throw new InvalidOperationException("WorldMover is not ready.");
        _floating_origin.WorldMoved += floating_origin_shift;
    }

    private void stuff_scenery(string raw_scenery, bool no_saving)
    {
        List<catenary_object>? loaded_objects = JsonConvert.DeserializeObject<List<catenary_object>>(raw_scenery, 
            scenery_json_settings());
        if (loaded_objects == null || loaded_objects.Any(item => item == null))
            throw new InvalidDataException("Scenery must be an array of non-null catenary objects.");
        if (no_saving)
            foreach (catenary_object item in loaded_objects) item.placed_procedurally = true;
        _all_objects.AddRange(loaded_objects);

        _scenery_changed = _all_objects.Count > 0;
    }

    private void load_selected_scenery(AssetBundle bundle)
    {
        NetworkProfile profile = NetworkProfile.Load(_file_path);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string asset in bundle.GetAllAssetNames().OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(asset);
            if (!NetworkProfile.IsSceneryName(name)) continue;
            if (sources.ContainsKey(name)) throw new InvalidDataException("Duplicate bundled scenery: " + name);
            sources.Add(name, asset);
        }
        var external = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string directory = Path.Combine(_file_path, "Scenery");
        if (profile.load_external_scenery && Directory.Exists(directory))
        {
            foreach (string file in Directory.GetFiles(directory).OrderBy(x => x, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                if (!NetworkProfile.IsSceneryName(name)) continue;
                if (external.ContainsKey(name)) throw new InvalidDataException("Duplicate external scenery: " + name);
                external.Add(name, file);
            }
        }
        string[] selected = profile.included_scenery ?? sources.Keys.Union(external.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string name in selected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string raw;
            if (external.TryGetValue(name, out string? file)) raw = File.ReadAllText(file);
            else if (sources.TryGetValue(name, out string? asset))
                raw = bundle.LoadAsset<TextAsset>(asset)?.text ?? throw new FileNotFoundException("Missing scenery asset " + asset);
            else throw new FileNotFoundException("Profile requests absent scenery: " + name);
            try { stuff_scenery(raw, no_saving: true); }
            catch (Exception error) { throw new InvalidDataException("Invalid scenery " + name + ": " + error.Message, error); }
        }
        if (Directory.GetFiles(_file_path, "scenery*.json").Length != 0)
            Main.release_log("Root-level scenery*.json overrides are ignored. Move reviewed overrides to Scenery/. Debug workspace is UserData/.");
        Main.release_log("Loaded network profile: " + profile.name + ", scenery files: " + selected.Length);
    }

    internal static void set_up(ModEntry mod)
    {
        if (_system != null)
            throw new InvalidOperationException("Attempt to create a duplicate catenary in the world");
        if (Type.GetType("DoubleTrack.TrackPlacerEntry, DoubleTrack", throwOnError: false) != null)
        {
            CatenaryApi.LastError = "Double-Tracked Valley is unsupported by this layout.";
            Main.release_log(CatenaryApi.LastError);
            return;
        }

        CatenaryApi.LastError = null;
        _system = new overhead_equipment(mod);
        try
        {
            _system.load_templates();
            // "catenary_object" and its derivaties require "system" property to be initialised,
            // which precludes doing loading inside constructor
            AssetBundle catenary = AssetBundle.LoadFromFile(Path.Combine(system._file_path, "catenary"))
                ?? throw new FileNotFoundException("Not found " + Path.Combine(system._file_path, "catenary"));
            _system._loaded_bundles.Add(catenary);
            _system.load_selected_scenery(catenary);
#if DEBUG        
            _system.load_scenery_from_file();   
#endif
            if (_system._scenery_changed)
                _system.reconstruct_tree();

            List<substation> all_substations =
            [..
                from   current_object in _system._all_objects
                let    current_substation = current_object as substation
                where  current_substation != null
                select current_substation
            ];
            substation neutral_connection = new("NEUTRAL", 0.0f, 100000.0f, false, 0, 0, 0.0f, Quaternion.identity);
            all_substations.Add(neutral_connection);
            int last_substation = all_substations.Count - 1;
            (all_substations[0], all_substations[last_substation]) = (all_substations[last_substation], all_substations[0]);
            _system._all_substations                   = new substation[last_substation + 1];
            Dictionary<string, int> substation_indices = [];
            for (int index = 0; index <= last_substation; ++index)
            {
                _system._all_substations[index] = all_substations[index];
                string id = all_substations[index].map_location;
                if (substation_indices.ContainsKey(id))
                    throw new InvalidDataException("Duplicate substation identifier: " + id);
                substation_indices.Add(id, index);
            }

            List<wire> all_wires = 
            [..
                from   current_object in _system._all_objects
                let    current_wire = current_object as wire
                where  current_wire != null
                select current_wire
            ];
            foreach (wire current_wire in all_wires)
            {
                if (!substation_indices.TryGetValue(current_wire.substation, out int substation_index))
                    throw new Exception($"Non-existent substation connection {current_wire.substation}");
                current_wire.substation_index = substation_index;
            }
            _system._wire_search_half_area = float_to_fixed((all_wires.Count == 0 ? 0 : all_wires.Max(w => w.length)) + 5.0f);
            _system._wires_tree = new(
                [..
                    from   current_wire in all_wires
                    select current_wire
                ], 
                wires_tree_objects_per_node
            );

            _system._OCS_ticker = new GameObject("CatenaryDC.OCS.PlayerTracker", typeof(OCS_ticker));
            var tracker = _system._OCS_ticker.GetComponent<OCS_ticker>();
            PlayerManager.PlayerTeleportStarted  += tracker.suspend_tracker;
            PlayerManager.PlayerTeleportFinished += tracker.resume_tracker;
            PlayerManager.PlayerChanged          += _system.restart_tracker;
            tracker.resume_tracker();
            _system._ready = true;
            CatenaryApi.RaiseActivated();
            Main.release_log("Catenary ready: " + _system._all_objects.Count + " objects, " + all_wires.Count + " wire sections.");
        }
        catch (Exception error)
        {
            CatenaryApi.LastError = error.ToString();
            // Do not save a half-loaded editor scene when startup fails.
            dispose(saveEditor: false);
            throw;
        }
    }

    private void restart_tracker()
    {
        _OCS_ticker?.GetComponent<OCS_ticker>().resume_tracker();
    }

    internal static void dispose(bool saveEditor = true)
    {
        overhead_equipment? old = _system;
        if (old == null) return;
        bool wasReady = old._ready;
        old._ready = false;
#if DEBUG
        editor.disable();
        if (saveEditor && wasReady)
        {
            try { old.store_scenery(); }
            catch (Exception error) { Main.release_log("Could not save editor workspace: " + error); }
        }
#endif
        if (old._OCS_ticker != null)
        {
            var tracker = old._OCS_ticker.GetComponent<OCS_ticker>();
            PlayerManager.PlayerTeleportStarted -= tracker.suspend_tracker;
            PlayerManager.PlayerTeleportFinished -= tracker.resume_tracker;
            PlayerManager.PlayerChanged -= old.restart_tracker;
            GameObject.Destroy(old._OCS_ticker);
            old._OCS_ticker = null;
        }
        if (old._floating_origin != null) old._floating_origin.WorldMoved -= old.floating_origin_shift;
        old._floating_origin = null;
        // Include transient/editor-created objects, not only the latest visibility list.
        try
        {
            foreach (catenary_object item in old._all_objects)
            {
                try { item.is_visible = false; item.hide_when_out_of_view(); }
                catch (Exception error) { Main.release_log("Could not hide catenary object: " + error.Message); }
            }
            foreach (AssetBundle bundle in old._loaded_bundles)
            {
                try { bundle.Unload(unloadAllLoadedObjects: true); }
                catch (Exception error) { Main.release_log("Could not unload catenary bundle: " + error.Message); }
            }
        }
        finally
        {
            old._loaded_bundles.Clear();
            _system = null;
            if (wasReady) CatenaryApi.RaiseDeactivated();
        }
    }

    private void find_objects_within_region(List<catenary_object> found_objects, quad_tree all_objects,
        bool do_bounds_check, int left, int top, int right, int bottom, bool search_tree_only = false)
    {
        found_objects.Clear();
        all_objects.find_objects(found_objects, do_bounds_check, left, top, right, bottom);
    #if DEBUG
        if (!search_tree_only)
        {
            foreach (catenary_object current_object in _freshly_added_objects)
            {
                if (current_object.x >= left && current_object.x <= right && current_object.z >= top && current_object.z <= bottom)
                    found_objects.Add(current_object);
            }
        }
    #endif
    }

    internal void handle_scenery_visibility(Vector3 relative_postion)
    {
        //const float visible_distance       = 500.0f;
        //const int   _visible_distance_fixed = (int) (visible_distance * fixed_multiplier);

        /*
        _remaining_time -= Time.deltaTime;
        if (_remaining_time > 0.0f)
            return;
        _remaining_time = 1.0f;
        */
        (int x, int z) = get_absolute_position(relative_postion);
        if (!_scenery_changed && Math.Abs(x - _last_x) + Math.Abs(z - _last_z) <= _visibility_check_distance)
            return;
        _last_x = x;
        _last_z = z;
        _scenery_changed           = false;
        int visible_distance_fixed = _visible_distance_fixed;

        (_previously_visible_objects, _currently_visible_objects) = (_currently_visible_objects, _previously_visible_objects);
        List<catenary_object> visible_objects = _previously_visible_objects;
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
            visible_objects[object_index].is_visible = false;
        visible_objects = _currently_visible_objects;
        find_objects_within_region(visible_objects, _object_tree, do_bounds_check: false,
            x - visible_distance_fixed, z - visible_distance_fixed,x + visible_distance_fixed, z + visible_distance_fixed);
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
        {
            bool is_invisible = !visible_objects[object_index].reveal();
            if (is_invisible)
                visible_objects.FastRemoveAt(object_index);
        }

        visible_objects = _previously_visible_objects;
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
            visible_objects[object_index].hide_when_out_of_view();

#if DEBUG        
        foreach (var current_object in _currently_visible_objects)
            assert.test(current_object.entity != null);
#endif
    }

    private void reconstruct_tree()
    {
        _object_tree = new quad_tree(_all_objects, scenery_tree_objects_per_node);
#if DEBUG
        _freshly_added_objects.Clear();
#endif
    }

    private void floating_origin_shift(WorldMover floating_origin, Vector3 shift)
    {
#if DEBUG
        if (_freshly_added_objects.Count >= _object_tree.node_objects_limit)
            reconstruct_tree();
#endif
        
        List<catenary_object> visible_objects = _currently_visible_objects;
        for (int index = visible_objects.Count - 1; index >= 0; --index)
            visible_objects[index].entity!.transform.position -= shift;
    }

    private _type_ add_scenery_object<_type_>(Func<int, int, float, Quaternion, _type_> constructor, 
        int x, int z, float y, Quaternion orientation)
        where _type_: catenary_object
    {
        _type_ new_object = constructor(x, z, y, orientation);
        _all_objects.Add(new_object);
#if DEBUG
        _freshly_added_objects.Add(new_object);
#endif
        //Main.log($"x={new_object.x / fixed_multiplier} z={new_object.z / fixed_multiplier} y={new_object.y} c={_all_objects.Count}");
        _scenery_changed = true;
        return new_object;
    }

    private void simulate_all_substations_load()
    {
        foreach (substation current_substation in _all_substations!)
            current_substation.simulate_load();
    }

    internal (float? contact_height, float contact_voltage) 
        absolute_wire_height_and_voltage(int strip_end1_x, int strip_end1_z, int strip_end2_x, int strip_end2_z, 
        float pantograph_base_y, float load_current)
    {
        int wire_search_half_area = _wire_search_half_area;
        
        int strip_centre_x = (strip_end1_x + strip_end2_x) >> 1;
        int strip_centre_z = (strip_end1_z + strip_end2_z) >> 1;
        _nearby_wires.Clear();
        find_objects_within_region(_nearby_wires, _wires_tree, do_bounds_check: true, 
            strip_centre_x - wire_search_half_area, strip_centre_z - wire_search_half_area, 
            strip_centre_x + wire_search_half_area, strip_centre_z + wire_search_half_area, search_tree_only: true);
        float lowest_height = float.MaxValue, lowest_energised_height = float.MaxValue;
        //Main.log($"{_nearby_wires.Count} wires");
        wire? wire_in_contact = null, energised_wire = null;
        foreach (catenary_object current_object in _nearby_wires)
        {
            var    current_wire   = (wire) current_object;
            float? contact_height = current_wire.contact_height(strip_end1_x, strip_end1_z, strip_end2_x, strip_end2_z, pantograph_base_y);
            if (contact_height != null)
            {
                float wire_height = (float) contact_height;
                if (lowest_height > wire_height)
                {
                    lowest_height   = wire_height;
                    wire_in_contact = current_wire;
                }
                if (current_wire.substation_index > 0 && lowest_energised_height > wire_height)
                {
                    lowest_energised_height = wire_height;
                    energised_wire          = current_wire;
                }
            }
        }

        if (wire_in_contact == null)
            return (null, 0.0f);
        if (energised_wire == null || lowest_energised_height - lowest_height >= 0.2f)
            return (lowest_height, 0.0f);
        substation supplying_substation = _all_substations![energised_wire.substation_index];
        return (lowest_height, supplying_substation.wire_voltage(energised_wire.x, energised_wire.z, 
            energised_wire.y, load_current, energised_wire.length_1m_resistance));
    }

    public (float? contact_height, float contact_voltage) 
        relative_wire_height_and_voltage(Transform unit_location, Transform pantograph_base, 
                                         Transform    strip_end1, Transform      strip_end2, float load_current) 
    { 
        (int strip_end1_x, int stripe_end1_z) = get_absolute_position(strip_end1.position);
        (int strip_end2_x, int stripe_end2_z) = get_absolute_position(strip_end2.position);
        //Main.log($"Contact ({strip_end1_x}, {stripe_end1_z})-({strip_end2_x}, {stripe_end2_z}) {pantograph_base.position.y}");
        Vector3 target_head_world_position = pantograph_base.position;
        (float? contact_height, float contact_voltage) = absolute_wire_height_and_voltage(strip_end1_x, stripe_end1_z, 
                                                                                          strip_end2_x, stripe_end2_z, 
                                                                                          target_head_world_position.y, load_current);
        if (contact_height == null)
            return (null, 0.0f);
        target_head_world_position.y = (float) contact_height;
        return (unit_location.InverseTransformPoint(target_head_world_position).y, contact_voltage);
    }
}
