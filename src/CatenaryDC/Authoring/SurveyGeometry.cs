// CC0. Loaded-scene observations; no forced streaming, Mesh mutation or UnityEditor dependency.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    private static JArray survey_transform_chain(Transform transform)
    {
        var nodes = new JArray(); int count = 0;
        for (Transform? t = transform; t != null && count++ < 32; t = t.parent)
            nodes.Add(new JObject { ["name"] = t.name, ["sibling_index"] = t.GetSiblingIndex(),
                ["position_local_m"] = survey_xyz(t.localPosition), ["rotation_local"] = survey_rotation(t.localRotation),
                ["scale_local"] = survey_xyz(t.localScale), ["active_self"] = t.gameObject.activeSelf });
        return nodes;
    }
    private static JObject survey_shallow_fields(object value, int depth)
    {
        var result = new JObject { ["type"] = value.GetType().FullName };
        // No traversal of arbitrary property getters. The two ID properties are deliberate game readers.
        foreach (string name in new[] { "ID", "id" })
            try
            {
                object? id = survey_member(value, name);
                if (id != null && (id is string || id.GetType().IsPrimitive)) result[name] = new JValue(id);
            }
            catch (Exception e) { result[name + "_error"] = e.GetBaseException().Message; }
        if (depth < 0) return result;
        int count = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (Type? t = value.GetType(); t != null && t != typeof(object) && t != typeof(UnityEngine.Object) && t != typeof(Component) && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (FieldInfo field in t.GetFields(flags))
            {
                if (count++ >= 80) { result["fields_truncated"] = true; return result; }
                try { result[t.Name + "." + field.Name] = survey_field_value(field.GetValue(value), depth); }
                catch (Exception e) { result[t.Name + "." + field.Name] = new JObject { ["error"] = e.GetBaseException().Message }; }
            }
        return result;
    }
    private static JToken survey_field_value(object? value, int depth)
    {
        if (value == null) return JValue.CreateNull();
        Type type = value.GetType();
        if (value is string || value is bool || type.IsPrimitive || value is decimal) return new JValue(value);
        if (value is Enum) return new JValue(value.ToString());
        if (value is Vector3 v) return survey_xyz(v);
        if (value is Quaternion q) return survey_rotation(q);
        if (value is Component component)
            return new JObject { ["type"] = type.FullName, ["scene_path"] = survey_path(component.transform), ["instance_id"] = component.GetInstanceID() };
        if (value is UnityEngine.Object asset)
            return new JObject { ["type"] = type.FullName, ["name"] = asset.name, ["instance_id"] = asset.GetInstanceID() };
        if (value is IEnumerable items)
        {
            var list = new JArray(); int count = 0;
            if (depth <= 0) return new JObject { ["type"] = type.FullName, ["omitted"] = "Depth limit" };
            foreach (object? item in items)
            {
                if (count++ >= 64) { list.Add(new JObject { ["truncated"] = true }); break; }
                list.Add(survey_field_value(item, depth - 1));
            }
            return list;
        }
        return depth > 0 ? survey_shallow_fields(value, depth - 1) : new JObject { ["type"] = type.FullName, ["omitted"] = "Depth limit" };
    }
    private static double survey_polyline_length(JArray points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; ++i)
        {
            double dx = (double)points[i]["x"]! - (double)points[i - 1]["x"]!;
            double dy = (double)points[i]["y"]! - (double)points[i - 1]["y"]!;
            double dz = (double)points[i]["z"]! - (double)points[i - 1]["z"]!;
            length += Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        return length;
    }
    private static JObject survey_collider(Collider c)
    {
        var record = new JObject { ["kind"] = "collider", ["path"] = survey_path(c.transform), ["scene"] = c.gameObject.scene.name,
            ["instance_id"] = c.GetInstanceID(), ["collider_type"] = c.GetType().FullName, ["enabled"] = c.enabled,
            ["active"] = c.gameObject.activeInHierarchy, ["is_trigger"] = c.isTrigger, ["layer"] = c.gameObject.layer,
            ["bounds_center_m"] = survey_xyz(survey_absolute(c.bounds.center)), ["bounds_size_m"] = survey_xyz(c.bounds.size),
            ["local_to_world_row_major"] = survey_matrix(c.transform), ["origin_shift"] = survey_xyz(DV.OriginShift.OriginShift.currentMove),
            ["transform_chain"] = survey_transform_chain(c.transform) };
        if (c is BoxCollider box) { record["center_local_m"] = survey_xyz(box.center); record["size_local_m"] = survey_xyz(box.size); }
        else if (c is CapsuleCollider capsule) { record["center_local_m"] = survey_xyz(capsule.center); record["radius_local_m"] = capsule.radius; record["height_local_m"] = capsule.height; record["direction"] = capsule.direction; }
        else if (c is SphereCollider sphere) { record["center_local_m"] = survey_xyz(sphere.center); record["radius_local_m"] = sphere.radius; }
        else if (c is MeshCollider mesh)
        {
            record["convex"] = mesh.convex;
            record["mesh_name"] = mesh.sharedMesh == null ? null : mesh.sharedMesh.name;
            record["mesh_id"] = mesh.sharedMesh == null ? 0 : mesh.sharedMesh.GetInstanceID();
            record["mesh_readable"] = mesh.sharedMesh != null && mesh.sharedMesh.isReadable;
            record["mesh_geometry_note"] = "Collider dimensions/bounds only; mesh geometry is exported separately for matching readable renderer candidates.";
        }
        // Unity returns empty bounds for disabled colliders. Keep enabled/active above so consumers can reject such bounds.
        return record;
    }
    private JObject survey_terrain(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position, size = data.size;
        float xmin = Mathf.Max(origin.x, _survey_player.x - _survey_radius_for_job);
        float xmax = Mathf.Min(origin.x + size.x, _survey_player.x + _survey_radius_for_job);
        float zmin = Mathf.Max(origin.z, _survey_player.z - _survey_radius_for_job);
        float zmax = Mathf.Min(origin.z + size.z, _survey_player.z + _survey_radius_for_job);
        int nx = Math.Max(1, Math.Min(96, (int)Math.Ceiling((xmax - xmin) / 10f)));
        int nz = Math.Max(1, Math.Min(96, (int)Math.Ceiling((zmax - zmin) / 10f)));
        var heights = new JArray();
        for (int z = 0; z <= nz; ++z)
        {
            var row = new JArray();
            for (int x = 0; x <= nx; ++x)
            {
                Vector3 sample = new Vector3(Mathf.Lerp(xmin, xmax, x / (float)nx), 0, Mathf.Lerp(zmin, zmax, z / (float)nz));
                row.Add(terrain.SampleHeight(sample) + origin.y);
            }
            heights.Add(row);
        }
        return new JObject { ["kind"] = "terrain_sample", ["path"] = survey_path(terrain.transform),
            ["origin_absolute_m"] = survey_xyz(survey_absolute(new Vector3(xmin, origin.y, zmin))),
            ["dx_m"] = (xmax - xmin) / nx, ["dz_m"] = (zmax - zmin) / nz, ["nx"] = nx + 1, ["nz"] = nz + 1,
            ["heights_y_m_rows_z_columns_x"] = heights, ["heightmap_resolution"] = data.heightmapResolution,
            ["approximate"] = true, ["note"] = "Terrain sample only. Does not include bridge decks, rocks/building meshes, tunnels or vegetation." };
    }
}
