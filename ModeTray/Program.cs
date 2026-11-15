namespace ID6ModeTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.ThreadException += (_, eventArgs) =>
            DiagnosticLog.Write($"Unhandled UI exception: {eventArgs.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            DiagnosticLog.Write($"Unhandled process exception: {eventArgs.ExceptionObject}");

        if (args.Length == 2 && args[0].Equals("--fan-boost", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var enabled = args[1].ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException("一键强冷参数应为 on 或 off。")
                };
                ModeSwitcher.SetFanBoost(enabled);
                DiagnosticLog.Write($"Command-line fan boost switch completed: {args[1]}");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Command-line fan boost switch failed: {ex}");
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length == 2 && args[0].Equals("--oc", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var enabled = args[1].ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException("OC 参数应为 on 或 off。")
                };
                ModeSwitcher.SetTurboOc(enabled);
                DiagnosticLog.Write($"Command-line OC switch completed: {args[1]}");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Command-line OC switch failed: {ex}");
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length == 2 && args[0].Equals("--switch", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ModeSwitcher.Switch(ModeDefinition.Parse(args[1]));
                DiagnosticLog.Write($"Command-line switch completed: {args[1]}");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Command-line switch failed: {ex}");
                Environment.ExitCode = 1;
            }
            return;
        }

        using var mutex = new Mutex(true, @"Local\ID6ModeTray", out var createdNew);
        if (!createdNew)
        {
            DiagnosticLog.Write("Tray startup skipped because another instance owns the mutex.");
            MessageBox.Show("\u6a21\u5f0f\u5207\u6362\u5668\u5df2\u7ecf\u5728\u8fd0\u884c\u3002", "ID6 Mode Tray");
            return;
        }

        try
        {
            DiagnosticLog.Write($"Tray startup begin: {Environment.ProcessPath}");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
            DiagnosticLog.Write("Tray message loop exited normally.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Tray startup failed: {ex}");
            MessageBox.Show(
                $"托盘程序启动失败。\n\n{ex.Message}\n\n详细信息已写入 ID6ModeTray.log。",
                "ID6 Mode Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
