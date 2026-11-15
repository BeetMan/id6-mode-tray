
namespace ID6ModeTray;

internal static class ControlCenterPaths
{
    private static string? install;

    private static string Install => install ??= Directory.GetDirectories(@"C:\Program Files\OEM")
        .Select(path => Path.Combine(path, "AiStoneService", "MyControlCenter"))
        .FirstOrDefault(Directory.Exists)
        ?? throw new DirectoryNotFoundException("没有找到智控中心配置目录。");

    public static string MainOption
    {
        get => Path.Combine(Install, "UserPofiles", "MainOption.json");
    }

    public static string TurboProfile(int index) =>
        Path.Combine(Install, "UserPofiles", $"Mode3_Profile{index + 1}.json");

    public static IEnumerable<(int Index, string Path)> CustomProfiles()
    {
        var directory = Path.Combine(Install, "UserPofiles");
        return Directory.EnumerateFiles(directory, "Mode4_Profile*.json")
            .Select(path => (
                Path: path,
                Suffix: Path.GetFileNameWithoutExtension(path)["Mode4_Profile".Length..]))
            .Where(profile => int.TryParse(profile.Suffix, out var number) && number >= 1)
            .Select(profile => (
                Index: int.Parse(profile.Suffix) - 1,
                profile.Path))
            .OrderBy(profile => profile.Index);
    }
}
