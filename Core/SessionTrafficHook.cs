using System.Linq.Expressions;
using System.Reflection;
using Renci.SshNet;

namespace WindowsSshPlane.Core;

/// <summary>
/// 统计 SSH 会话上的真实通道数据量，用于判断隧道是否还有数据传输。
/// SSH.NET 没有公开流量计数，这里订阅其内部 ISession 的 ChannelDataReceived 事件；
/// 包版本固定在工程文件中，若内部结构变化则返回 null，由调用方降级为按连接建立打点。
/// </summary>
internal sealed class SessionTrafficHook : IDisposable
{
    private readonly object _session;
    private readonly EventInfo _dataEvent;
    private readonly Delegate _handler;
    private bool _disposed;

    private SessionTrafficHook(object session, EventInfo dataEvent, Delegate handler)
    {
        _session = session;
        _dataEvent = dataEvent;
        _handler = handler;
    }

    /// <summary>尝试挂载流量观测；失败时返回 null 并给出原因。</summary>
    public static SessionTrafficHook? TryAttach(SshClient client, Action<int> onData, out string? error)
    {
        try
        {
            var sessionProperty = typeof(SshClient).BaseType?
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name == "Session");
            if (sessionProperty is null)
            {
                error = "未找到 SSH 会话对象";
                return null;
            }

            var session = sessionProperty.GetValue(client);
            if (session is null)
            {
                error = "SSH 会话尚未建立";
                return null;
            }

            var sessionType = typeof(SshClient).Assembly.GetType("Renci.SshNet.ISession");
            var dataEvent = sessionType?.GetEvent(
                "ChannelDataReceived",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dataEvent is null)
            {
                error = "未找到通道数据事件";
                return null;
            }

            var handler = BuildHandler(dataEvent, onData);
            dataEvent.AddEventHandler(session, handler);
            error = null;
            return new SessionTrafficHook(session, dataEvent, handler);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + "：" + ex.Message;
            return null;
        }
    }

    private static Delegate BuildHandler(EventInfo dataEvent, Action<int> onData)
    {
        var handlerType = dataEvent.EventHandlerType
            ?? throw new InvalidOperationException("事件缺少处理程序类型");
        var invoke = handlerType.GetMethod("Invoke")
            ?? throw new InvalidOperationException("事件处理程序缺少 Invoke");
        var parameters = invoke.GetParameters();

        var sender = Expression.Parameter(parameters[0].ParameterType, "sender");
        var args = Expression.Parameter(parameters[1].ParameterType, "e");
        var messageProperty = parameters[1].ParameterType.GetProperty("Message")
            ?? throw new InvalidOperationException("事件参数缺少 Message");
        var sizeProperty = messageProperty.PropertyType.GetProperty("Size")
            ?? throw new InvalidOperationException("数据消息缺少 Size");

        var body = Expression.Call(
            Expression.Constant(onData),
            typeof(Action<int>).GetMethod(nameof(Action<int>.Invoke))!,
            Expression.Property(Expression.Property(args, messageProperty), sizeProperty));

        return Expression.Lambda(handlerType, body, sender, args).Compile();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _dataEvent.RemoveEventHandler(_session, _handler);
        }
        catch
        {
            // 会话已销毁时移除会失败，忽略
        }
    }
}