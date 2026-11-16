using System.Net.Sockets;
using System.Text;

namespace SshTunnelManager.Core;

/// <summary>无界面自检：用 SshTunnelManager.exe --selftest 运行，供构建后验证核心逻辑。</summary>
public static class SelfTest
{
    /// <summary>执行自检；返回 0 表示全部通过。</summary>
    public static int Run(string? reportPath = null)
    {
        var failures = 0;
        var lines = new List<string>();

        void Out(string text)
        {
            lines.Add(text);
            Console.WriteLine(text);
        }

        void Check(string name, bool ok, string detail = "")
        {
            Out($"{(ok ? "[通过]" : "[失败]")} {name}{(string.IsNullOrEmpty(detail) ? string.Empty : "  " + detail)}");
            if (!ok)
            {
                failures++;
            }
        }

        // 1. 默认配置
        var defaults = AppConfig.CreateDefault();
        defaults.Normalize();
Check("默认不预置任何服务器地址", defaults.ServerHost.Length == 0, string.IsNullOrEmpty(defaults.ServerHost) ? "（空）" : defaults.ServerHost);
        Check("默认配置不含任何 IP 字面量", !System.Text.RegularExpressions.Regex.IsMatch(defaults.ServerHost, @"^\d{1,3}(\.\d{1,3}){3}$"));
        Check("默认服务器端口 9787", defaults.ServerPort == 9787, defaults.ServerPort.ToString());
        Check("默认用户名 root", defaults.ServerUser == "root", defaults.ServerUser);
        Check("默认保活 30s × 3", defaults.KeepAliveSeconds == 30 && defaults.KeepAliveCountMax == 3);
        Check("默认含 1 条转发规则", defaults.Rules.Count == 1, defaults.Rules.Count.ToString());

        var rule = defaults.Rules[0];
        Check(
            "默认规则 127.0.0.1:14000 → 127.0.0.1:4000",
            rule.BindHost == "127.0.0.1" && rule.BindPort == 14000 && rule.TargetHost == "127.0.0.1" && rule.TargetPort == 4000,
            rule.Summary);
        Check(
            "命令行参数等价 -R 127.0.0.1:14000:127.0.0.1:4000",
            rule.ForwardArgument == "-R 127.0.0.1:14000:127.0.0.1:4000",
            rule.ForwardArgument);

        // 2. 规则校验
        var badPort = new TunnelRule { BindHost = "127.0.0.1", BindPort = 70000, TargetHost = "127.0.0.1", TargetPort = 4000 };
        Check("端口越界被拒绝", !badPort.Validate(out _));
        var emptyHost = new TunnelRule { BindHost = " ", BindPort = 14000, TargetHost = "127.0.0.1", TargetPort = 4000 };
        Check("绑定地址为空被拒绝", !emptyHost.Validate(out _));
        var okRule = new TunnelRule { BindHost = "127.0.0.1", BindPort = 15000, TargetHost = "127.0.0.1", TargetPort = 5000 };
        Check("合法规则通过校验", okRule.Validate(out _));

        // 3. 配置读写往返（临时目录，不影响用户配置）
        var tempDir = Path.Combine(Path.GetTempPath(), "SshTunnelManagerSelfTest_" + Guid.NewGuid().ToString("N"));
        var tempFile = Path.Combine(tempDir, "config.json");
        try
        {
            var config = AppConfig.CreateDefault();
            config.Rules.Add(new TunnelRule
            {
                BindHost = "127.0.0.1",
                BindPort = 22000,
                TargetHost = "127.0.0.1",
                TargetPort = 22,
                Enabled = false,
            });

            Check("配置保存成功", ConfigStore.Save(config, tempFile), tempFile);
            var loaded = ConfigStore.Load(tempFile);
            Check("配置读回规则数量一致", loaded.Rules.Count == 2, loaded.Rules.Count.ToString());
            Check("配置读回服务器端口一致", loaded.ServerPort == 9787);
            Check("配置读回停用状态一致", loaded.Rules.Any(r => r.BindPort == 22000 && !r.Enabled));
            var raw = File.ReadAllText(tempFile);
            Check("配置文件中不含密码字段", !raw.Contains("password", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Check("配置读写往返", false, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // 忽略
            }
        }

// 4. 网络可达性（仅信息项，不作为失败依据）
        // 待探测地址取自本机配置文件，不来自代码；输出做脱敏，避免地址被复制传播
        string Mask(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return "（空）";
            }

            if (host.Length <= 6)
            {
                return host[..1] + "***";
            }

            return host[..3] + "***" + host[^3..];
        }

        var local = ConfigStore.Load();
        if (string.IsNullOrWhiteSpace(local.ServerHost))
        {
            Out("[信息] 本机配置尚未填写服务器地址，跳过 TCP 连通性探测");
        }
        else
        {
            try
            {
                using var tcp = new TcpClient();
                var task = tcp.ConnectAsync(local.ServerHost, local.ServerPort);
                var reachable = task.Wait(TimeSpan.FromSeconds(6)) && tcp.Connected;
                Out($"[信息] 本机配置的服务器 {Mask(local.ServerHost)}:{local.ServerPort} TCP 连通性：{(reachable ? "可达" : "不可达（不影响本机自检结论）")}");
            }
            catch (Exception ex)
            {
                Out($"[信息] 本机配置的服务器 {Mask(local.ServerHost)}:{local.ServerPort} TCP 探测失败：{ex.Message}");
            }
        }

        Out(failures == 0 ? "自检全部通过" : $"自检存在 {failures} 项失败");

        if (!string.IsNullOrEmpty(reportPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(reportPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 报告写入失败不影响退出码
            }
        }

        return failures == 0 ? 0 : 1;
    }
}