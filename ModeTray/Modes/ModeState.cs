using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ID6ModeTray;

internal enum ModeStateErrorKind
{
    NotFound,
    IO,
    InvalidData,
    Unknown
}

internal sealed record ModeStateError(ModeStateErrorKind Kind, string Detail)
{
    public string UserMessage => Kind switch
    {
        ModeStateErrorKind.NotFound => "未找到智控中心状态文件",
        ModeStateErrorKind.IO => "智控中心状态文件暂时不可读",
        ModeStateErrorKind.InvalidData => "智控中心状态数据异常",
        _ => "读取智控中心状态失败"
    };
}

internal readonly record struct ModeReadResult(ModeState? State, ModeStateError? Error);

internal readonly record struct ModeState(
    int OperatingMode,
    int CustomProfileIndex,
    int TurboProfileIndex,
    int TurboSilent,
    int TurboExtreme,
    int TurboGpuOcOffset,
    int FanBoostEnabled)
{
    public static ModeReadResult Read()
    {
        try
        {
            return ReadFrom(ControlCenterPaths.MainOption, ControlCenterPaths.TurboProfile);
        }
        catch (Exception ex)
        {
            return new ModeReadResult(null, CreateError(ex));
        }
    }

    internal static ModeReadResult ReadFrom(
        string mainOptionPath,
        Func<int, string> turboProfilePath,
        int maxAttempts = 3)
    {
        maxAttempts = Math.Max(1, maxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var json = ReadJson(mainOptionPath);
                var operatingMode = json["OperatingMode"]?.GetValue<int>() ?? -1;
                if (operatingMode is < 0 or > 3)
                {
                    throw new InvalidDataException("OperatingMode is missing or invalid.");
                }

                var turboIndex = json["TurboProfileIndex"]?.GetValue<int>() ?? 0;
                var turboSilent = -1;
                var turboExtreme = -1;
                if (operatingMode == 2)
                {
                    var profile = ReadJson(turboProfilePath(turboIndex));
                    turboSilent = profile["Silent"]?.GetValue<int>() ?? -1;
                    turboExtreme = profile["Extreme"]?.GetValue<int>() ?? -1;
                }

                return new ModeReadResult(
                    new ModeState(
                        operatingMode,
                        json["CustomProfileIndex"]?.GetValue<int>() ?? -1,
                        turboIndex,
                        turboSilent,
                        turboExtreme,
                        json["TurboGPUOCOffset"]?.GetValue<int>() ?? 0,
                        json["FanBoostEnable"]?.GetValue<int>() ?? 0),
                    null);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                return new ModeReadResult(null, CreateError(ex));
            }
        }
    }

    public static ModeState? TryRead() => Read().State;

    private static JsonNode ReadJson(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())
            ?? throw new InvalidDataException("The state file is empty.");
    }

    private static bool IsTransient(Exception exception) =>
        exception is JsonException or InvalidDataException ||
        exception is IOException and not FileNotFoundException and not DirectoryNotFoundException;

    private static ModeStateError CreateError(Exception exception)
    {
        var kind = exception switch
        {
            FileNotFoundException or DirectoryNotFoundException => ModeStateErrorKind.NotFound,
            UnauthorizedAccessException or IOException => ModeStateErrorKind.IO,
            JsonException or InvalidDataException or FormatException or InvalidOperationException =>
                ModeStateErrorKind.InvalidData,
            _ => ModeStateErrorKind.Unknown
        };
        return new ModeStateError(kind, exception.Message);
    }
}
