using System.Text;

namespace ID6ModeTray;

internal sealed class ModeCatalog
{
    private readonly Func<string> fingerprintProvider;
    private readonly Func<IReadOnlyList<ModeDefinition>> loader;
    private string fingerprint;

    public ModeCatalog()
        : this(ComputeFingerprint, ModeDefinition.Load)
    {
    }

    internal ModeCatalog(
        Func<string> fingerprintProvider,
        Func<IReadOnlyList<ModeDefinition>> loader)
    {
        this.fingerprintProvider = fingerprintProvider;
        this.loader = loader;
        Modes = loader();
        try
        {
            fingerprint = fingerprintProvider();
        }
        catch (Exception ex)
        {
            fingerprint = string.Empty;
            DiagnosticLog.Write($"Initial mode catalog fingerprint failed: {ex.Message}");
        }
    }

    public IReadOnlyList<ModeDefinition> Modes { get; private set; }

    public bool Refresh()
    {
        try
        {
            var before = fingerprintProvider();
            if (before == fingerprint)
            {
                return false;
            }

            var updated = loader();
            var after = fingerprintProvider();
            Modes = updated;
            fingerprint = before == after ? after : string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Mode catalog refresh failed: {ex.Message}");
            return false;
        }
    }

    private static string ComputeFingerprint()
    {
        var builder = new StringBuilder();
        AppendFile(builder, Path.Combine(AppContext.BaseDirectory, "profiles.json"));
        AppendFile(builder, ControlCenterProfileNames.SettingsPath);
        foreach (var profile in ControlCenterPaths.CustomProfiles())
        {
            AppendFile(builder, profile.Path);
        }

        return builder.ToString();
    }

    private static void AppendFile(StringBuilder builder, string path)
    {
        builder.Append(path).Append('|');
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            builder.Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
        }
        else
        {
            builder.Append("missing");
        }

        builder.AppendLine();
    }
}
