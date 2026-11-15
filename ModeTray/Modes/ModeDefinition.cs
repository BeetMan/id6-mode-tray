using System.Text.Json;
using System.Text.Json.Nodes;

namespace ID6ModeTray;

internal sealed record ModeDefinition(
    string Label,
    string Action,
    int OperatingMode,
    int ProfileIndex,
    int? Silent = null,
    int? Extreme = null)
{
    public static IReadOnlyList<ModeDefinition> Load() =>
        Load(
            Path.Combine(AppContext.BaseDirectory, "profiles.json"),
            ControlCenterPaths.CustomProfiles(),
            ControlCenterProfileNames.Load());

    internal static IReadOnlyList<ModeDefinition> Load(
        string configuredPath,
        IEnumerable<(int Index, string Path)> customProfiles,
        IReadOnlyDictionary<int, string> controlCenterAliases)
    {
        var configured = File.Exists(configuredPath)
            ? JsonSerializer.Deserialize<List<ModeDefinition>>(
                File.ReadAllText(configuredPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;
        configured ??= Defaults.ToList();

        var modes = configured.Where(mode => mode.OperatingMode != 3).ToList();
        var customOverrides = configured
            .Where(mode => mode.OperatingMode == 3)
            .GroupBy(mode => mode.ProfileIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var discovered = new List<ModeDefinition>();

        try
        {
            foreach (var profile in customProfiles)
            {
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(profile.Path));
                    if (json?["Activated"]?.GetValue<bool>() != true)
                    {
                        continue;
                    }

                    customOverrides.TryGetValue(profile.Index, out var profileOverride);
                    var customizeName = json?["CustomizeName"]?.GetValue<string>()?.Trim();
                    var oemLabel = !string.IsNullOrWhiteSpace(customizeName) &&
                                   !int.TryParse(customizeName, out _)
                        ? customizeName
                        : null;
                    var alias = controlCenterAliases.GetValueOrDefault(profile.Index);
                    var label = !string.IsNullOrWhiteSpace(alias)
                        ? $"Profile {profile.Index + 1} - {alias}"
                        : profileOverride?.Label;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = oemLabel is null
                            ? $"Profile {profile.Index + 1}"
                            : $"Profile {profile.Index + 1} - {oemLabel}";
                    }

                    discovered.Add(new ModeDefinition(
                        label,
                        "OPERATING_CUSTOM_MODE",
                        3,
                        profile.Index));
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Custom profile skipped: {profile.Path}: {ex.Message}");
                }
            }

            modes.AddRange(discovered);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Custom profile discovery failed: {ex}");
            modes.AddRange(configured.Where(mode => mode.OperatingMode == 3));
        }

        return modes;
    }

    public bool Matches(ModeState state) =>
        state.OperatingMode == OperatingMode &&
        (OperatingMode != 3 || state.CustomProfileIndex == ProfileIndex) &&
        (OperatingMode != 2 ||
            (state.TurboProfileIndex == ProfileIndex &&
             (Silent is null || state.TurboSilent == Silent) &&
             (Extreme is null || state.TurboExtreme == Extreme)));

    public static ModeDefinition Parse(string value)
    {
        var parts = value.Split(':', 2);
        var modes = Load();
        return value.ToLowerInvariant() switch
        {
            "office" => modes.First(mode => mode.OperatingMode == 0),
            "gaming" => modes.First(mode => mode.OperatingMode == 1),
            "turbo" => modes.First(mode => mode.OperatingMode == 2 && mode.Extreme == 1),
            "turbo:silent" => modes.First(mode => mode.OperatingMode == 2 && mode.Silent == 1),
            "turbo:extreme" => modes.First(mode => mode.OperatingMode == 2 && mode.Extreme == 1),
            _ when parts[0].Equals("custom", StringComparison.OrdinalIgnoreCase) &&
                   parts.Length == 2 &&
                   int.TryParse(parts[1], out var slot) &&
                   slot >= 1 =>
                modes.FirstOrDefault(mode =>
                    mode.OperatingMode == 3 &&
                    mode.ProfileIndex == slot - 1)
                ?? throw new ArgumentException($"自定义 Profile {slot} 未启用或不存在。"),
            _ => throw new ArgumentException("模式格式应为 office、gaming、turbo 或 custom:N。")
        };
    }

    private static readonly IReadOnlyList<ModeDefinition> Defaults =
    [
        new("办公模式", "OPERATING_OFFICE_MODE", 0, 0),
        new("游戏模式", "OPERATING_GAMING_MODE", 1, 0),
        new("狂暴 · 静技", "OPERATING_TURBO_MODE", 2, 0, 1, 0),
        new("狂暴 · 极速", "OPERATING_TURBO_MODE", 2, 0, 0, 1),
        new("Profile 1 - 静音高性能", "OPERATING_CUSTOM_MODE", 3, 0),
        new("Profile 2 - 静音办公", "OPERATING_CUSTOM_MODE", 3, 1),
        new("Profile 3 - 静音游戏", "OPERATING_CUSTOM_MODE", 3, 2),
        new("Profile 4", "OPERATING_CUSTOM_MODE", 3, 3)
    ];
}
