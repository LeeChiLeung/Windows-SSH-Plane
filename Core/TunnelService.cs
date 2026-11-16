using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace WindowsSshPlane.Core;

/// <summary>
/// 一条 SSH 连接上管理多条远端端口转发（等价 ssh -N -T -R ...）。
/// 采用进程内 SSH 客户端（SSH.NET），因此可以：
///  1) 用窗口接收密码而不是依赖控制台交互；
///  2) 在保持连接的同时动态增删转发端口（真实 ssh 命令做不到这一点）。
/// </summary>
public sealed class TunnelService : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ForwardedPortRemote> _ports = new();
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private SshClient? _client;
    private IReadOnlyList<TunnelRule> _rules = Array.Empty<TunnelRule>();
    private System.Threading.Timer? _watchdog;
    private int _reconnectCount;
    private string _password = string.Empty;
    private bool _disposed;

    /// <summary>把回调切回 UI 线程用的派发器，由界面层注入。</summary>
    public Action<Action>? UiDispatch { get; set; }

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; } = 22;

    public string User { get; private set; } = string.Empty;

    /// <summary>保活间隔（对应 ServerAliveInterval）。</summary>
    public int KeepAliveSeconds { get; private set; } = 30;

    /// <summary>最大自动重连次数（对应 ServerAliveCountMax）。</summary>
    public int KeepAliveCountMax { get; private set; } = 3;

    public string ServerSummary => $"{User}@{Host}:{Port}";

    /// <summary>日志输出，可能在后台线程触发。</summary>
    public event EventHandler<string>? LogReceived;

    /// <summary>连接状态变化，可能在后台线程触发。</summary>
    public event EventHandler? StateChanged;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _client is { IsConnected: true };
            }
        }
    }

    /// <summary>写入连接参数与密码，密码仅保存在内存中。</summary>
    public void SetCredentials(AppConfig config, string password)
    {
        Host = config.ServerHost.Trim();
        Port = config.ServerPort;
        User = config.ServerUser.Trim();
        KeepAliveSeconds = Math.Max(5, config.KeepAliveSeconds);
        KeepAliveCountMax = Math.Max(1, config.KeepAliveCountMax);
        _password = password;
    }

    /// <summary>建立连接并启动所有启用状态的转发规则。返回 null 表示成功。</summary>
    public async Task<string?> ConnectAsync(IReadOnlyList<TunnelRule> rules)
    {
        await _opLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _rules = rules;
            _reconnectCount = 0;
            var error = DoConnect();
            RaiseStateChanged();
            return error;
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <summary>主动断开连接。</summary>
    public async Task DisconnectAsync()
    {
        await _opLock.WaitAsync().ConfigureAwait(false);
        try
        {
            DoDisconnect("手动断开");
        }
        finally
        {
            _opLock.Release();
        }

        RaiseStateChanged();
    }

    /// <summary>启动一条转发，返回 null 表示成功。</summary>
    public Task<string?> StartRuleAsync(TunnelRule rule)
    {
        return Task.Run(() =>
        {
            var error = TryStartPort(rule);
            RaiseStateChanged();
            return error;
        });
    }

    /// <summary>停止一条转发。</summary>
    public Task StopRuleAsync(TunnelRule rule)
    {
        return Task.Run(() =>
        {
            ForwardedPortRemote? port = null;
            lock (_sync)
            {
                if (_ports.TryGetValue(rule.Id, out var existing))
                {
                    port = existing;
                    _ports.Remove(rule.Id);
                }
            }

            if (port is null)
            {
                SetStatus(rule, rule.Enabled ? "未运行" : "已停用");
                return;
            }

            SshClient? client;
            lock (_sync)
            {
                client = _client;
            }

            try
            {
                if (port.IsStarted)
                {
                    port.Stop();
                }
            }
            catch (Exception ex)
            {
                Log($"停止转发时出现异常（{rule.Summary}）：{ex.Message}");
            }

            try
            {
                client?.RemoveForwardedPort(port);
            }
            catch
            {
                // 连接已失效时移除会失败，忽略
            }

            try
            {
                port.Dispose();
            }
            catch
            {
                // 忽略释放异常
            }

            SetStatus(rule, rule.Enabled ? "已停止" : "已停用");
            Log($"转发已停止：{rule.ForwardArgument}");
            RaiseStateChanged();
        });
    }

    private string? DoConnect()
    {
        DoDisconnect("重新建立连接");

        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(User))
        {
            return "服务器地址或用户名不能为空";
        }

        var connectionInfo = new PasswordConnectionInfo(Host, Port, User, _password)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        var client = new SshClient(connectionInfo)
        {
            // 对应 -o ServerAliveInterval=30
            KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveSeconds),
        };
        client.ErrorOccurred += OnClientError;

        lock (_sync)
        {
            _client = client;
        }

        Log($"正在连接 {ServerSummary} …");
        try
        {
            client.Connect();
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _client = null;
            }

            client.ErrorOccurred -= OnClientError;
            try
            {
                client.Dispose();
            }
            catch
            {
                // 忽略
            }

            var message = Describe(ex);
            Log("连接失败：" + message);
            return message;
        }

        Log($"连接成功：{User}@{Host}:{Port}（保活 {KeepAliveSeconds}s，最多自动重连 {KeepAliveCountMax} 次）");

        // 等价 -o ExitOnForwardFailure=yes：任一端口转发建立失败，就整体断开
        var failures = new List<string>();
        foreach (var rule in _rules.Where(r => r.Enabled))
        {
            var error = TryStartPort(rule);
            if (!string.IsNullOrEmpty(error))
            {
                failures.Add($"{rule.Summary}：{error}");
            }
        }

        if (failures.Count > 0)
        {
            var message = "端口转发失败，已按 ExitOnForwardFailure 语义断开连接：" + string.Join("；", failures);
            Log(message);
            DoDisconnect("端口转发失败");
            return message;
        }

        StartWatchdog();
        return null;
    }

    private string? TryStartPort(TunnelRule rule)
    {
        if (!rule.Validate(out var invalid))
        {
            SetStatus(rule, "参数无效", invalid);
            Log($"跳过无效规则（{rule.Summary}）：{invalid}");
            return invalid;
        }

        SshClient? client;
        lock (_sync)
        {
            client = _client;
        }

        if (client is null || !client.IsConnected)
        {
            SetStatus(rule, "未运行", "连接已断开");
            return "连接已断开";
        }

        if (!rule.Enabled)
        {
            SetStatus(rule, "已停用");
            return null;
        }

        ForwardedPortRemote port;
        try
        {
            port = new ForwardedPortRemote(
                rule.BindHost.Trim(),
                (uint)rule.BindPort,
                rule.TargetHost.Trim(),
                (uint)rule.TargetPort);
        }
        catch (Exception ex)
        {
            var message = Describe(ex);
            SetStatus(rule, "启动失败", message);
            return message;
        }

port.Exception += (_, e) =>
        {
            var message = e.Exception.Message;
            SetStatus(rule, "转发异常", message);
            Log($"转发异常（{rule.Summary}）：{message}");
        };

        try
        {
            client.AddForwardedPort(port);
            port.Start();
        }
        catch (Exception ex)
        {
            var message = Describe(ex);
            try
            {
                client.RemoveForwardedPort(port);
                port.Dispose();
            }
            catch
            {
                // 忽略清理异常
            }

            SetStatus(rule, "启动失败", message);
            Log($"转发启动失败（{rule.Summary}）：{message}");
            return message;
        }

        lock (_sync)
        {
            _ports[rule.Id] = port;
        }

        SetStatus(rule, "转发中", string.Empty);
        Log($"转发已启动：{rule.ForwardArgument}（服务器 {rule.BindHost}:{rule.BindPort} → 本机 {rule.TargetHost}:{rule.TargetPort}）");
        return null;
    }

    private void StartWatchdog()
    {
        StopWatchdog();
        var period = TimeSpan.FromSeconds(Math.Max(5, KeepAliveSeconds / 3));
        _watchdog = new System.Threading.Timer(_ => CheckConnection(), null, period, period);
    }

    private void StopWatchdog()
    {
        var timer = _watchdog;
        _watchdog = null;
        try
        {
            timer?.Dispose();
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>看门狗：发现断线后按 ServerAliveCountMax 的次数上限自动重连。</summary>
    private void CheckConnection()
    {
        if (_disposed)
        {
            return;
        }

        SshClient? client;
        lock (_sync)
        {
            client = _client;
        }

        if (client is null)
        {
            return;
        }

        bool connected;
        try
        {
            connected = client.IsConnected;
        }
        catch
        {
            connected = false;
        }

        if (connected)
        {
            Interlocked.Exchange(ref _reconnectCount, 0);
            return;
        }

        // 有正在进行的连接操作时不抢占
        if (!_opLock.Wait(0))
        {
            return;
        }

        try
        {
            if (_reconnectCount >= KeepAliveCountMax)
            {
                Log($"连接已断开，且已达到最大自动重连次数 {KeepAliveCountMax} 次，停止重试（可点击“重新连接”手动恢复）");
                DoDisconnect("连接已断开");
                RaiseStateChanged();
                return;
            }

            _reconnectCount++;
            Log($"检测到连接已断开，正在执行第 {_reconnectCount}/{KeepAliveCountMax} 次自动重连 …");
            var error = DoConnect();
            if (string.IsNullOrEmpty(error))
            {
                _reconnectCount = 0;
            }

            RaiseStateChanged();
        }
        finally
        {
            _opLock.Release();
        }
    }

    private void DoDisconnect(string reason)
    {
        StopWatchdog();

        SshClient? client;
        List<KeyValuePair<Guid, ForwardedPortRemote>> ports;
        lock (_sync)
        {
            client = _client;
            _client = null;
            ports = _ports.ToList();
            _ports.Clear();
        }

        foreach (var (_, port) in ports)
        {
            try
            {
                if (port.IsStarted)
                {
                    port.Stop();
                }
            }
            catch
            {
                // 忽略
            }

            try
            {
                client?.RemoveForwardedPort(port);
            }
            catch
            {
                // 忽略
            }

            try
            {
                port.Dispose();
            }
            catch
            {
                // 忽略
            }
        }

        if (client is not null)
        {
            client.ErrorOccurred -= OnClientError;
            try
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
            catch
            {
                // 忽略
            }

            try
            {
                client.Dispose();
            }
            catch
            {
                // 忽略
            }

            Log($"已断开连接（{reason}）");
        }

        foreach (var rule in _rules)
        {
            SetStatus(rule, rule.Enabled ? "已停止" : "已停用");
        }
    }

    private void OnClientError(object? sender, ExceptionEventArgs e)
    {
        Log("连接层错误：" + e.Exception.Message);
        RaiseStateChanged();
    }

    private void SetStatus(TunnelRule rule, string status, string error = "")
    {
        Dispatch(() =>
        {
            rule.Status = status;
            rule.LastError = error;
        });
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Dispatch(() => LogReceived?.Invoke(this, line));
    }

    private void RaiseStateChanged()
    {
        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void Dispatch(Action action)
    {
        var dispatcher = UiDispatch;
        if (dispatcher is null)
        {
            action();
            return;
        }

        try
        {
            dispatcher(action);
        }
        catch
        {
            // 窗口已销毁时退回直接执行
            action();
        }
    }

    private static string Describe(Exception ex)
    {
        return ex switch
        {
            SshAuthenticationException => "认证失败：用户名或密码不正确，或服务器禁止密码登录",
            SshConnectionException connectionEx => "SSH 连接错误：" + connectionEx.Message,
            SocketException socketEx => "网络错误：" + socketEx.Message,
            System.Security.Authentication.AuthenticationException authEx => "认证/加密协商失败：" + authEx.Message,
            _ => ex.GetType().Name + "：" + ex.Message,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DoDisconnect("程序退出");
        try
        {
            _opLock.Dispose();
        }
        catch
        {
            // 忽略
        }
    }
}