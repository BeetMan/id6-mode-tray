using System.Text.Json;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace ID6ModeTray;

internal static class ModeSwitcher
{
    public static void Switch(ModeDefinition mode)
    {
        var client = new MqttClient(
            "127.0.0.1",
            13688,
            false,
            null,
            null,
            MqttSslProtocols.None);
        try
        {
            var result = client.Connect(
                "PluginClient_19",
                "PluginClient_User_19",
                "PluginClient_Pwd888881772688_19");
            DiagnosticLog.Write($"MQTT connect result={result}, connected={client.IsConnected}");
            if (result != 0 || !client.IsConnected)
            {
                throw new InvalidOperationException($"无法连接 GCUBridge，MQTT 返回码 {result}。");
            }

            PublishAndWait(client, new
            {
                Action = mode.Action,
                ProfileIndex = mode.ProfileIndex
            });

            if (mode.OperatingMode == 2 && mode.Silent is not null && mode.Extreme is not null)
            {
                WaitForState(state => state.OperatingMode == 2, TimeSpan.FromSeconds(5));
                if (mode.Silent == 1)
                {
                    PublishAndWait(client, new
                    {
                        Action = "SET_CPU_CORE_OFFSET_SILENT",
                        SILENT = 1
                    });
                }
                else
                {
                    PublishAndWait(client, new
                    {
                        Action = "SET_CPU_CORE_OFFSET_EXTREME",
                        EXTREME = 1
                    });
                }
            }

            WaitForState(mode.Matches, TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    public static void SetTurboOc(bool enabled)
    {
        var read = ModeState.Read();
        if (read.Error is not null)
        {
            throw new InvalidOperationException(read.Error.UserMessage);
        }

        var state = read.State;
        if (state is null || state.Value.OperatingMode != 2)
        {
            throw new InvalidOperationException("GPU OC 只能在狂暴模式下切换。");
        }

        var client = new MqttClient(
            "127.0.0.1",
            13688,
            false,
            null,
            null,
            MqttSslProtocols.None);
        try
        {
            var result = client.Connect(
                "PluginClient_19",
                "PluginClient_User_19",
                "PluginClient_Pwd888881772688_19");
            DiagnosticLog.Write($"MQTT OC connect result={result}, connected={client.IsConnected}");
            if (result != 0 || !client.IsConnected)
            {
                throw new InvalidOperationException($"无法连接 GCUBridge，MQTT 返回码 {result}。");
            }

            var target = enabled ? 150 : 0;
            PublishAndWait(client, new
            {
                Action = "SET_OPERATING_MODE_DETAIL",
                GpuCoreClockOffsetOC = target
            });
            WaitForState(
                current => current.OperatingMode == 2 && current.TurboGpuOcOffset == target,
                TimeSpan.FromSeconds(5));
            DiagnosticLog.Write($"GPU OC switched to {target} MHz");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    public static void SetFanBoost(bool enabled)
    {
        var client = new MqttClient(
            "127.0.0.1",
            13688,
            false,
            null,
            null,
            MqttSslProtocols.None);
        try
        {
            var result = client.Connect(
                "PluginClient_19",
                "PluginClient_User_19",
                "PluginClient_Pwd888881772688_19");
            DiagnosticLog.Write($"MQTT fan boost connect result={result}, connected={client.IsConnected}");
            if (result != 0 || !client.IsConnected)
            {
                throw new InvalidOperationException($"无法连接 GCUBridge，MQTT 返回码 {result}。");
            }

            PublishAndWait(client, new
            {
                Action = enabled ? "FAN_BOOST_ON" : "FAN_BOOST_OFF"
            });
            WaitForState(
                state => state.FanBoostEnabled == (enabled ? 1 : 0),
                TimeSpan.FromSeconds(5));
            DiagnosticLog.Write($"Fan boost switched to {(enabled ? "on" : "off")}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    private static void PublishAndWait(MqttClient client, object message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            using var published = new ManualResetEventSlim(false);
            ushort messageId = 0;
            client.MqttMsgPublished += (_, args) =>
            {
                if (args.MessageId == messageId && args.IsPublished)
                {
                    published.Set();
                }
            };
            messageId = client.Publish(
                "Fan/Control",
                payload,
                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                false);
            if (!published.Wait(TimeSpan.FromSeconds(3)))
            {
                throw new TimeoutException("GCUBridge 未确认收到切换命令。");
            }
    }

    private static void WaitForState(Func<ModeState, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        ModeStateError? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            var read = ModeState.Read();
            if (read.State is not null && predicate(read.State.Value))
            {
                return;
            }

            lastError = read.Error;
            Thread.Sleep(100);
        }

        if (lastError is not null)
        {
            throw new TimeoutException($"命令已发送，但{lastError.UserMessage}。");
        }

        throw new TimeoutException("命令已发送，但模式状态没有在 5 秒内更新。");
    }
}
