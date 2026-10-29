// Only catenary bundles; no CCL/locomotive export. Unity 2019.4 compatible syntax.
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;

public static class CatenaryBundleBuild
{
    [Serializable] private class Entry { public string path; public string sha256; }
    [Serializable] private class Receipt
    {
        public string project = "Derail-Valley-Catenary-DC";
        public string unity_version;
        public string utc_time;
        public Entry[] source_files;
        public Entry[] bundles;
    }
    private static string Sha(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static Entry MakeEntry(string root, string path)
    {
        string full = Path.GetFullPath(path);
        return new Entry { path = full.Substring(root.Length + 1).Replace('\\', '/'), sha256 = Sha(full) };
    }
    [MenuItem("Catenary/Export OCS")]
    public static void Build()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        const string output = "Assets/AssetBundles";
        Directory.CreateDirectory(output);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        string[] names = { "catenary", "catenary_parts" };
        var builds = new List<AssetBundleBuild>();
        foreach (string name in names)
        {
            string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(name);
            if (paths.Length == 0) throw new InvalidOperationException("No assets assigned to " + name);
            if (paths.Any(p => !p.StartsWith("Assets/Catenary/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Unexpected non-catenary asset in " + name);
            builds.Add(new AssetBundleBuild { assetBundleName = name, assetNames = paths.OrderBy(x => x, StringComparer.Ordinal).ToArray() });
        }
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output, builds.ToArray(),
            BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new InvalidOperationException("Export failed; inspect the Unity Console.");
        string[] emitted = manifest.GetAllAssetBundles().OrderBy(x => x).ToArray();
        if (!emitted.SequenceEqual(names)) throw new InvalidOperationException("Unexpected exported bundle set.");
        var sources = Directory.GetFiles(Path.Combine(Application.dataPath, "Catenary"), "*", SearchOption.AllDirectories).ToList();
        sources.Add(Path.Combine(Application.dataPath, "Catenary.meta"));
        sources.Add(Path.Combine(Application.dataPath, "Editor", "CatenaryBundleBuild.cs"));
        var receipt = new Receipt {
            unity_version = Application.unityVersion,
            utc_time = DateTime.UtcNow.ToString("o"),
            source_files = sources.OrderBy(x => x, StringComparer.Ordinal).Select(p => MakeEntry(root, p)).ToArray(),
            bundles = names.Select(n => MakeEntry(root, Path.Combine(root, output, n))).ToArray()
        };
        File.WriteAllText(Path.Combine(root, output, "catenarydc-export.json"), JsonUtility.ToJson(receipt, true));
        Debug.Log("OCS export complete. Two catenary-only Windows bundles and a source receipt are in " + output);
    }
}
