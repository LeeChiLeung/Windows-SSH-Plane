namespace WindowsSshPlane.Core;

/// <summary>程序配置：服务器连接参数 + 转发规则列表。密码不落盘。</summary>
public sealed class AppConfig
{
/// <summary>SSH 服务器地址。仓库内不预置任何真实地址，由使用者在登录框填写，保存在本机配置文件中。</summary>
    public string ServerHost { get; set; } = string.Empty;

    /// <summary>SSH 服务器端口，对应 ssh -p；示例值 9787，使用者按自己的服务器修改。</summary>
    public int ServerPort { get; set; } = 9787;

    /// <summary>登录用户名，例如 root。</summary>
    public string ServerUser { get; set; } = "root";

    /// <summary>保活间隔秒数，对应 -o ServerAliveInterval=30。</summary>
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>断线后最多自动重连次数，对应 -o ServerAliveCountMax=3。</summary>
    public int KeepAliveCountMax { get; set; } = 3;

    /// <summary>转发规则列表。</summary>
    public List<TunnelRule> Rules { get; set; } = new();

    /// <summary>默认配置：预置用户给出的 14000 → 127.0.0.1:4000 转发。</summary>
    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Rules = new List<TunnelRule>
            {
                new TunnelRule
                {
                    Enabled = true,
                    BindHost = "127.0.0.1",
                    BindPort = 14000,
                    TargetHost = "127.0.0.1",
                    TargetPort = 4000,
                },
            },
        };
    }

    /// <summary>补齐可能缺失的配置项。</summary>
    public void Normalize()
    {
// 不做任何地址兜底：留空即由登录框要求使用者填写，避免把真实服务器写进代码
        ServerHost = (ServerHost ?? string.Empty).Trim();

        if (ServerPort < 1 || ServerPort > 65535)
        {
            ServerPort = 9787;
        }

        if (string.IsNullOrWhiteSpace(ServerUser))
        {
            ServerUser = "root";
        }

        if (KeepAliveSeconds < 5)
        {
            KeepAliveSeconds = 30;
        }

        if (KeepAliveCountMax < 1)
        {
            KeepAliveCountMax = 3;
        }

        Rules ??= new List<TunnelRule>();
        foreach (var rule in Rules)
        {
            rule.Id = Guid.NewGuid();
            rule.Status = "未启动";
            rule.LastError = string.Empty;
        }
    }
}