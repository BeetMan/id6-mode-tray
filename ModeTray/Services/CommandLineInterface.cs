using System.Text.Json;

namespace ID6ModeTray;

internal static class CommandLineInterface
{
    internal static int Execute(
        string[] args,
        Action<string> output,
        IReadOnlyList<ModeDefinition>? modes = null,
        ModeReadResult? stateOverride = null)
    {
        var json = args.Count(arg => arg.Equals("--json", StringComparison.OrdinalIgnoreCase)) == 1;
        if (args.Length != (json ? 2 : 1))
        {
            throw new ArgumentException("用法：--status [--json] 或 --list [--json]。");
        }

        var command = args.SingleOrDefault(arg =>
            arg.Equals("--status", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--list", StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            throw new ArgumentException("查询命令应为 --status 或 --list。");
        }

        var modeList = modes ?? ModeDefinition.Load();
        var read = stateOverride ?? ModeState.Read();
        var active = read.State is null
            ? null
            : modeList.FirstOrDefault(mode => mode.Matches(read.State.Value));
        var listedModes = modeList
            .Where(mode => mode.CommandId is not null)
            .GroupBy(mode => mode.CommandId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var exitCode = read.Error is null ? 0 : 1;

        if (command.Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            if (json)
            {
                output(JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    modes = listedModes.Select(mode => new
                    {
                        id = mode.CommandId,
                        label = mode.Label,
                        operatingMode = mode.OperatingMode,
                        profileIndex = mode.ProfileIndex,
                        silent = mode.Silent,
                        extreme = mode.Extreme,
                        active = mode == active
                    }).ToArray(),
                    error = ErrorObject(read.Error)
                }));
            }
            else
            {
                foreach (var mode in listedModes)
                {
                    output($"{(mode == active ? "* " : string.Empty)}{mode.CommandId}\t{mode.Label}");
                }
                if (read.Error is not null)
                {
                    output(read.Error.UserMessage);
                }
            }
            return exitCode;
        }

        if (json)
        {
            output(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                ok = read.Error is null,
                mode = active?.CommandId,
                label = active?.Label,
                state = read.State is null ? (object?)null : new
                {
                    operatingMode = read.State.Value.OperatingMode,
                    customProfileIndex = read.State.Value.CustomProfileIndex,
                    turboProfileIndex = read.State.Value.TurboProfileIndex,
                    turboSilent = read.State.Value.TurboSilent,
                    turboExtreme = read.State.Value.TurboExtreme,
                    turboGpuOcOffset = read.State.Value.TurboGpuOcOffset,
                    turboOcActive = read.State.Value.OperatingMode == 2 && read.State.Value.TurboGpuOcOffset == 150,
                    fanBoostEnabled = read.State.Value.FanBoostEnabled == 1
                },
                error = ErrorObject(read.Error)
            }));
        }
        else if (read.Error is not null)
        {
            output(read.Error.UserMessage);
        }
        else
        {
            var value = read.State!.Value;
            output(active is null ? "当前：未识别模式" : $"当前：{active.Label}");
            output($"GPU OC：{value.TurboGpuOcOffset} MHz");
            output($"一键强冷：{(value.FanBoostEnabled == 1 ? "开启" : "关闭")}");
        }
        return exitCode;
    }

    internal static string ExceptionJson(Exception exception) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        ok = false,
        error = new { kind = "Exception", message = exception.Message }
    });

    private static object? ErrorObject(ModeStateError? error) => error is null
        ? null
        : new { kind = error.Kind.ToString(), message = error.UserMessage };
}
