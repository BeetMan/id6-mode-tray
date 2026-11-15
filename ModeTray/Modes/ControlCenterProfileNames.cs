using System.Text;
using System.Text.Json.Nodes;

namespace ID6ModeTray;

internal static class ControlCenterProfileNames
{
    private const string PackageFamilyName = "CCU.WinUI_wrbgcf7aesyd8";
    private static IReadOnlyDictionary<int, string> cachedAliases =
        new Dictionary<int, string>();

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages",
        PackageFamilyName,
        "Settings",
        "settings.dat");

    public static IReadOnlyDictionary<int, string> Load() => Load(SettingsPath);

    internal static IReadOnlyDictionary<int, string> Load(string path)
    {
        var aliases = new Dictionary<int, string>();
        IReadOnlyDictionary<int, string> Succeed()
        {
            cachedAliases = aliases;
            return aliases;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var hiveText = Encoding.Unicode.GetString(memory.ToArray());
            const string customMarker = "\"Mode\": \"Custom\"";
            var markerIndex = hiveText.IndexOf(customMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return Succeed();
            }

            var arrayStart = hiveText.LastIndexOf('[', markerIndex);
            var arrayEnd = hiveText.IndexOf(']', markerIndex);
            if (arrayStart < 0 || arrayEnd <= arrayStart)
            {
                return Succeed();
            }

            var profiles = JsonNode.Parse(hiveText[arrayStart..(arrayEnd + 1)])?.AsArray();
            if (profiles is null)
            {
                return Succeed();
            }

            foreach (var profile in profiles)
            {
                if (!string.Equals(
                        profile?["Mode"]?.GetValue<string>(),
                        "Custom",
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(profile?["Index"]?.GetValue<string>(), out var index))
                {
                    continue;
                }

                var alias = profile?["AliasName"]?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    aliases[index] = alias;
                }
            }

            DiagnosticLog.Write(
                $"Loaded {aliases.Count} custom profile aliases from Control Center settings.");
            cachedAliases = aliases;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"Control Center profile alias discovery failed; using {cachedAliases.Count} cached aliases: {ex.Message}");
            return cachedAliases;
        }

        return aliases;
    }
}
