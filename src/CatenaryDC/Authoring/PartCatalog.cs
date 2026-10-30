// CC0. Local prefab geometry, CSVs and anchor-child transforms for offline placement.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    private IEnumerator export_catalog(string modPath)
    {
        string destination = survey_destination(modPath, "catalog_", ".jsonl");
        var seen = new HashSet<int>(); int vertices = 0, meshes = 0, parts = 0, omitted = 0, errors = 0;
        using (var output = new StreamWriter(new FileStream(destination + ".partial", FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
        {
            _survey_writer = output;
            JObject header = survey_header(modPath); header["kind"] = "catalog_header";
            header["catalog_coordinates"] = "Prefab-local. Mesh-local vertices -> mesh_to_prefab row-major matrix. NO world-origin subtraction.";
            survey_write(output, header);
            foreach (var item in _survey_definitions.OrderBy(p => p.Key, StringComparer.Ordinal))
                survey_write(output, new JObject { ["kind"] = "definition_csv", ["name"] = item.Key, ["text"] = item.Value });
            foreach (var item in _templates.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                survey_check(); GameObject template = item.Value;
                if (template == null) continue;
                try
                {
                    var nodes = new JArray();
                    foreach (Transform child in template.GetComponentsInChildren<Transform>(true))
                    {
                        Matrix4x4 relative = template.transform.worldToLocalMatrix * child.localToWorldMatrix;
                        var matrix = new JArray(); for (int row = 0; row < 4; ++row) for (int col = 0; col < 4; ++col) matrix.Add(relative[row, col]);
                        var node = new JObject { ["path"] = survey_path(child), ["sibling_index"] = child.GetSiblingIndex(),
                            ["position_local_m"] = survey_xyz(child.localPosition), ["rotation_local"] = survey_rotation(child.localRotation),
                            ["scale_local"] = survey_xyz(child.localScale), ["mesh_to_prefab_row_major"] = matrix };
                        MeshFilter? filter = child.GetComponent<MeshFilter>(); Mesh? mesh = filter == null ? null : filter.sharedMesh;
                        if (mesh != null)
                        {
                            int id = mesh.GetInstanceID(); node["mesh_id"] = id;
                            if (seen.Add(id))
                            {
                                var data = new JObject { ["kind"] = "mesh_geometry", ["mesh_id"] = id, ["name"] = mesh.name,
                                    ["vertex_count"] = mesh.vertexCount, ["readable"] = mesh.isReadable, ["exported"] = false,
                                    ["bounds_center_local_m"] = survey_xyz(mesh.bounds.center), ["bounds_size_local_m"] = survey_xyz(mesh.bounds.size) };
                                try
                                {
                                    if (!_survey_mesh_enabled_for_job) { data["omission"] = "Mesh export not enabled."; omitted++; }
                                    else if (!mesh.isReadable) { data["omission"] = "Mesh is not readable; no runtime setting changed."; omitted++; }
                                    else if (mesh.vertexCount > survey_mesh_vertex_limit || vertices + mesh.vertexCount > survey_mesh_total_vertex_limit)
                                    { data["omission"] = "Vertex budget."; omitted++; }
                                    else
                                    {
                                        int[] triangles = mesh.triangles;
                                        if (triangles.Length > 120000) { data["omission"] = "Triangle index budget."; omitted++; }
                                        else
                                        {
                                            data["vertices_local_m"] = new JArray(mesh.vertices.Select(survey_xyz));
                                            data["triangle_indices"] = new JArray(triangles); data["exported"] = true; vertices += mesh.vertexCount;
                                        }
                                    }
                                }
                                catch (Exception e) { data["error"] = e.GetBaseException().Message; errors++; }
                                survey_write(output, data); meshes++;
                            }
                        }
                        Renderer? renderer = child.GetComponent<Renderer>();
                        if (renderer != null) node["material_names"] = new JArray(renderer.sharedMaterials.Select(m => m == null ? "<null>" : m.name));
                        nodes.Add(node);
                    }
                    survey_write(output, new JObject { ["kind"] = "part", ["template_name"] = item.Key, ["root_local_scale"] = survey_xyz(template.transform.localScale),
                        ["asset_path"] = _all_parts.TryGetValue(item.Key, out string? path) ? path : "", ["nodes"] = nodes });
                    parts++;
                }
                catch (Exception e) { survey_write(output, new JObject { ["kind"] = "record_error", ["part"] = item.Key, ["error"] = e.GetBaseException().Message }); errors++; }
                _survey_status = "Catalog: " + parts + "/" + _templates.Count; yield return null;
            }
            survey_check();
            survey_write(output, new JObject { ["kind"] = "footer", ["export_complete"] = true, ["parts"] = parts, ["mesh_records"] = meshes,
                ["mesh_vertices_exported"] = vertices, ["mesh_omissions"] = omitted, ["record_errors"] = errors, ["all_meshes_available"] = omitted == 0 && errors == 0 });
        }
        _survey_writer = null; File.Move(destination + ".partial", destination);
        _survey_status = "Catalog written: " + destination + "; parts=" + parts + "; omitted meshes=" + omitted + "; errors=" + errors;
        Main.release_log(_survey_status);
    }
}
