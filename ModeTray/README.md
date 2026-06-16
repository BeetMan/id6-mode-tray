# ID6 Mode Tray

轻量的 Windows 托盘模式切换器，通过智控中心现有的 `GCUBridge` MQTT 接口切换模式。

## 使用

运行 `ID6ModeTray.exe` 后，在系统托盘右键图标即可切换：

- 办公、游戏、狂暴模式
- 自定义 Profile 1–4

自定义 Profile 会从智控中心的 `Mode4_Profile*.json` 自动发现，只显示
`Activated=true` 的槽位。`profiles.json` 中相同 `profileIndex` 的 `label`
可用于覆盖显示名称。

命令行也可直接切换：

```powershell
ID6ModeTray.exe --switch office
ID6ModeTray.exe --switch gaming
ID6ModeTray.exe --switch turbo
ID6ModeTray.exe --switch custom:2
```

程序不会修改配置文件，也不需要管理员权限；`GCUBridge` 服务必须处于运行状态。
