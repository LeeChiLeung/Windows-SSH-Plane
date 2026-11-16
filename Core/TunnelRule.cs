using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SshTunnelManager.Core;

/// <summary>
/// 一条反向端口转发规则，等价于 ssh 的
/// -R {BindHost}:{BindPort}:{TargetHost}:{TargetPort}
/// </summary>
public sealed class TunnelRule : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _bindHost = "127.0.0.1";
    private int _bindPort = 14000;
    private string _targetHost = "127.0.0.1";
    private int _targetPort = 4000;
    private string _status = "未启动";
    private string _lastError = string.Empty;

    /// <summary>规则唯一标识，运行期使用，不写入配置文件。</summary>
    [JsonIgnore]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>是否启用该转发。停用的规则不会随连接启动。</summary>
    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>服务器侧的绑定地址，普通场景固定 127.0.0.1。</summary>
    public string BindHost
    {
        get => _bindHost;
        set => Set(ref _bindHost, value);
    }

    /// <summary>服务器侧监听的端口，例如 14000。</summary>
    public int BindPort
    {
        get => _bindPort;
        set => Set(ref _bindPort, value);
    }

    /// <summary>目标主机，指本机（被转发的这一端）上的地址，例如 127.0.0.1。</summary>
    public string TargetHost
    {
        get => _targetHost;
        set => Set(ref _targetHost, value);
    }

    /// <summary>目标端口，例如本地服务 4000。</summary>
    public int TargetPort
    {
        get => _targetPort;
        set => Set(ref _targetPort, value);
    }

    /// <summary>运行期状态：未启动 / 转发中 / 已停止 / 启动失败 …</summary>
    [JsonIgnore]
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>最近一次错误说明。</summary>
    [JsonIgnore]
    public string LastError
    {
        get => _lastError;
        set => Set(ref _lastError, value);
    }

    /// <summary>等价命令行参数，用于界面提示与日志。</summary>
    [JsonIgnore]
    public string ForwardArgument => $"-R {BindHost}:{BindPort}:{TargetHost}:{TargetPort}";

    /// <summary>简短描述，例如 127.0.0.1:14000 → 127.0.0.1:4000。</summary>
    [JsonIgnore]
    public string Summary => $"{BindHost}:{BindPort} → {TargetHost}:{TargetPort}";

    /// <summary>校验规则字段是否合法。</summary>
    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(BindHost))
        {
            error = "服务器侧绑定地址不能为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetHost))
        {
            error = "目标地址不能为空";
            return false;
        }

        if (BindPort < 1 || BindPort > 65535)
        {
            error = "服务器侧端口必须在 1-65535 之间";
            return false;
        }

        if (TargetPort < 1 || TargetPort > 65535)
        {
            error = "目标端口必须在 1-65535 之间";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>把另一条规则的字段复制到当前对象（保持 Id 与运行状态不变）。</summary>
    public void CopyFrom(TunnelRule other)
    {
        Enabled = other.Enabled;
        BindHost = other.BindHost;
        BindPort = other.BindPort;
        TargetHost = other.TargetHost;
        TargetPort = other.TargetPort;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}