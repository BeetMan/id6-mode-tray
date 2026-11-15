using System.Windows.Forms;

namespace ID6ModeTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly ModePopup popup;
    private readonly ModeToast toast;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly ModeCatalog modeCatalog;
    private bool operationInProgress;
    private ModeStateError? lastStateError;

    public TrayApplicationContext()
    {
        modeCatalog = new ModeCatalog();
        popup = new ModePopup(modeCatalog.Modes, SwitchMode, SetTurboOc, SetFanBoost, Refresh, ExitThread);
        toast = new ModeToast();
        notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "ID6 Mode Tray",
            Visible = true
        };
        notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button is MouseButtons.Left or MouseButtons.Right)
            {
                TogglePopup();
            }
        };

        refreshTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        refreshTimer.Tick += (_, _) => RefreshState();
        refreshTimer.Start();
        RefreshState();
        DiagnosticLog.Write("Tray application context initialized.");
    }

    private void TogglePopup()
    {
        if (popup.Visible)
        {
            popup.Hide();
            return;
        }

        Refresh();
        popup.ShowNearTray();
    }

    private void Refresh()
    {
        if (modeCatalog.Refresh())
        {
            popup.SetModes(modeCatalog.Modes);
        }

        RefreshState();
    }

    private void SwitchMode(ModeDefinition mode)
    {
        _ = RunOperationAsync(
            () => ModeSwitcher.Switch(mode),
            "\u6a21\u5f0f\u5df2\u5207\u6362",
            mode.Label,
            "\u5207\u6362\u5931\u8d25");
    }

    private void SetTurboOc(bool enabled)
    {
        _ = RunOperationAsync(
            () => ModeSwitcher.SetTurboOc(enabled),
            enabled ? "GPU OC 已开启" : "GPU OC 已关闭",
            enabled ? "核心频率偏移 +150 MHz" : "核心频率偏移已恢复为 0",
            "GPU OC 切换失败");
    }

    private void SetFanBoost(bool enabled)
    {
        _ = RunOperationAsync(
            () => ModeSwitcher.SetFanBoost(enabled),
            enabled ? "一键强冷已开启" : "一键强冷已关闭",
            enabled ? "CPU 与 GPU 风扇已进入强冷状态" : "风扇已恢复当前模式控制",
            "一键强冷切换失败");
    }

    private async Task RunOperationAsync(
        Action operation,
        string successTitle,
        string successMessage,
        string failureTitle)
    {
        if (operationInProgress)
        {
            return;
        }

        operationInProgress = true;
        popup.SetBusy(true);
        try
        {
            await Task.Run(operation);
            RefreshState();
            toast.ShowMessage(successTitle, successMessage, false);
        }
        catch (Exception ex)
        {
            toast.ShowMessage(failureTitle, ex.Message, true);
        }
        finally
        {
            operationInProgress = false;
            popup.SetBusy(false);
            RefreshState();
        }
    }

    private void RefreshState()
    {
        var read = ModeState.Read();
        popup.UpdateState(read.State, read.Error, operationInProgress);
        if (!Equals(lastStateError, read.Error))
        {
            if (read.Error is not null)
            {
                DiagnosticLog.Write($"Mode state read failed ({read.Error.Kind}): {read.Error.Detail}");
            }
            else if (lastStateError is not null)
            {
                DiagnosticLog.Write("Mode state recovered.");
            }

            lastStateError = read.Error;
        }

        var active = modeCatalog.Modes.FirstOrDefault(
            mode => read.State is not null && mode.Matches(read.State.Value));
        notifyIcon.Text = active is null ? "ID6 Mode Tray" : $"ID6 - {active.Label}";
    }

    protected override void ExitThreadCore()
    {
        DiagnosticLog.Write("Tray exit requested.");
        refreshTimer.Stop();
        refreshTimer.Dispose();
        toast.Dispose();
        popup.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}
