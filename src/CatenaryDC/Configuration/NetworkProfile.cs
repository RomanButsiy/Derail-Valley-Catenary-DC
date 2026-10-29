using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CatenaryDC.Configuration;

public sealed class NetworkProfile
{
    public int schema_version = 1;
    public string name = "author-reference";
    // null means all bundled and external basenames; an explicit array is an allowlist.
    public string[]? included_scenery;
    public bool load_external_scenery = true;

    public static NetworkProfile Load(string modDirectory)
    {
        string path = Path.Combine(modDirectory, "network.json");
        NetworkProfile profile = File.Exists(path)
            ? JsonConvert.DeserializeObject<NetworkProfile>(File.ReadAllText(path))
              ?? throw new InvalidDataException("network.json must contain an object.")
            : new NetworkProfile();
        if (profile.schema_version != 1) throw new InvalidDataException("Unsupported network.json schema.");
        if (profile.included_scenery != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in profile.included_scenery)
            {
                if (!IsSceneryName(entry) || !seen.Add(entry))
                    throw new InvalidDataException("Invalid/duplicate scenery basename in profile: " + entry);
            }
        }
        return profile;
    }

    public static bool IsSceneryName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) &&
        name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0 &&
        name.StartsWith("scenery_", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && name.Length > 13;
}
