namespace WindowsSshPlane.Core;

/// <summary>
/// 空闲计时器：记录“最近一次数据传输”的时间，判断是否已超过空闲阈值。
/// 时钟可注入，便于自检里用假时间验证行为，而不必真的等 30 分钟。
/// </summary>
public sealed class IdleMonitor
{
    private readonly object _sync = new();
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset _lastActivity;
    private long _totalBytes;
    private long _activityEvents;

    public IdleMonitor(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lastActivity = _clock();
    }

    /// <summary>累计观测到的通道数据字节数。</summary>
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    /// <summary>累计观测到的数据事件次数。</summary>
    public long ActivityEvents => Interlocked.Read(ref _activityEvents);

    /// <summary>最近一次数据活动时间（UTC）。</summary>
    public DateTimeOffset LastActivity
    {
        get
        {
            lock (_sync)
            {
                return _lastActivity;
            }
        }
    }

    /// <summary>记一次活动；bytes 为本次传输字节数。</summary>
    public void NotifyActivity(int bytes = 0)
    {
        Interlocked.Add(ref _totalBytes, bytes);
        Interlocked.Increment(ref _activityEvents);
        var now = _clock();
        lock (_sync)
        {
            _lastActivity = now;
        }
    }

    /// <summary>距最近一次活动已经过去多久。</summary>
    public TimeSpan IdleFor(DateTimeOffset now)
    {
        var last = LastActivity;
        return now > last ? now - last : TimeSpan.Zero;
    }

    /// <summary>按阈值判断是否已超时；timeoutMinutes 为 0 表示不启用。</summary>
    public bool IsExpired(DateTimeOffset now, int timeoutMinutes)
    {
        if (timeoutMinutes <= 0)
        {
            return false;
        }

        return IdleFor(now) >= TimeSpan.FromMinutes(timeoutMinutes);
    }

    /// <summary>把时长格式化成“X 分 Y 秒”便于日志阅读。</summary>
    public static string Describe(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        var seconds = span.Seconds;
        if (hours > 0)
        {
            return $"{hours} 小时 {minutes} 分";
        }

        return minutes > 0 ? $"{minutes} 分 {seconds} 秒" : $"{seconds} 秒";
    }
}