using System.Text.Json;
using System.Text.Json.Serialization;

using Application = System.Windows.Forms.Application;
using MessageBox = System.Windows.Forms.MessageBox;

namespace RestartWindowsService;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            var config = LoadConfig();
            var serviceName = config?.Windows?.Service?.Name?.Trim();
            var serviceDisplayName = config?.Windows?.Service?.DisplayName?.Trim();
            var autoStart = config?.Windows?.Service?.AutoStart ?? true;

            if (string.IsNullOrWhiteSpace(serviceName))
            {
                MessageBox.Show(
                    "未读取到配置：appsettings.json 中的 windows.service.name 为空。",
                    "重启 Windows 服务",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            var displayName = string.IsNullOrWhiteSpace(serviceDisplayName) ? serviceName : serviceDisplayName;
            Application.Run(new MainForm(serviceName, displayName, autoStart));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"执行失败：{ex.Message}",
                "重启 Windows 服务",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 99;
        }
    }

    private static AppConfig? LoadConfig()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("未找到 appsettings.json（需与 exe 同目录）。", configPath);
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }
}

internal sealed class AppConfig
{
    [JsonPropertyName("windows")]
    public WindowsConfig? Windows { get; init; }
}

internal sealed class WindowsConfig
{
    [JsonPropertyName("service")]
    public ServiceConfig? Service { get; init; }
}

internal sealed class ServiceConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("autoStart")]
    public bool? AutoStart { get; init; }
}

