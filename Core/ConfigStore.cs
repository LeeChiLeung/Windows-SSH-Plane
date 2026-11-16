using System.Text.Json;

namespace SshTunnelManager.Core;

/// <summary>配置读写：默认放在 %APPDATA%\SshTunnelManager\config.json。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>数据目录。</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SshTunnelManager");

    /// <summary>默认配置文件路径。</summary>
    public static string DefaultPath => Path.Combine(DataDirectory, "config.json");

    /// <summary>日志文件路径。</summary>
    public static string LogPath => Path.Combine(DataDirectory, "app.log");

    /// <summary>读取配置；文件不存在或损坏时返回默认配置。</summary>
    public static AppConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
            {
                var fresh = AppConfig.CreateDefault();
                fresh.Normalize();
                Save(fresh, file);
                return fresh;
            }

            var json = File.ReadAllText(file);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
            if (config is null)
            {
                throw new InvalidDataException("配置文件内容为空");
            }

            config.Normalize();
            if (config.Rules.Count == 0)
            {
                config.Rules.Add(new TunnelRule
                {
                    BindHost = "127.0.0.1",
                    BindPort = 14000,
                    TargetHost = "127.0.0.1",
                    TargetPort = 4000,
                });
            }

            return config;
        }
        catch (Exception ex)
        {
            // 配置损坏不应阻塞启动：备份后回落到默认配置
            TryBackupBrokenFile(file);
            var fallback = AppConfig.CreateDefault();
            fallback.Normalize();
            fallback.Rules[0].LastError = "配置文件读取失败已重置：" + ex.Message;
            return fallback;
        }
    }

    /// <summary>保存配置（不含任何密码信息）。</summary>
    public static bool Save(AppConfig config, string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(file, JsonSerializer.Serialize(config, Options));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryBackupBrokenFile(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Copy(file, file + ".bak", overwrite: true);
            }
        }
        catch
        {
            // 备份失败可忽略
        }
    }
}