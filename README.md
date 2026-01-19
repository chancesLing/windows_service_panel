# restart_windows_fuwu

## 配置

编辑 `appsettings.json`：

```json
{
  "windows": {
    "service": {
      "name": "BLW_Weld",
      "displayName": "博路威焊接服务",
      "autoStart": true
    }
  }
}
```

`name` 填 Windows 服务名（服务管理器中“服务名称”，不是显示名称）。
`displayName` 为界面显示名称（例如：博路威焊接服务）。
`autoStart` 为 true 时：程序打开后会自动启动该服务（默认 true）。

## 发布成单文件 exe（建议 x64）

在仓库根目录执行：

```powershell
dotnet restore
dotnet publish .\RestartWindowsService\RestartWindowsService.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true
```

发布产物路径：

`RestartWindowsService\bin\Release\net8.0-windows\win-x64\publish\windows_service_controller.exe`

把同目录的 `appsettings.json` 一并带上（已配置为自动复制到输出目录）。

## 使用

双击 exe → 打开控制面板（默认会自动启动服务）：

- 显示当前服务运行状态
- 启动 / 重启 / 停止 按钮
- 最小化或关闭窗口会缩到托盘（右下角通知区域），程序继续后台运行
- 退出程序不会关闭服务（服务保持原状态）

如果提示权限不足：右键 exe → “以管理员身份运行”。

