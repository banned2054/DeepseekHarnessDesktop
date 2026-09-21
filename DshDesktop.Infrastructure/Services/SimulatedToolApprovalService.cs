using DshDesktop.Core.Models;
using DshDesktop.Core.Services;

namespace DshDesktop.Infrastructure.Services;

/// <summary>模拟审批服务：内存待决列表 + 手动推送钩子，行为对齐真实服务的交互语义。</summary>
public sealed class SimulatedToolApprovalService : IToolApprovalService
{
    private readonly List<(string EventId, bool Allowed)> _decisions = [];
    private readonly Lock                                 _sync      = new();

    private List<PendingApproval> _pending = [];

    /// <summary>已下达的裁决（按到达顺序）；供测试断言。</summary>
    public IReadOnlyList<(string EventId, bool Allowed)> Decisions
    {
        get
        {
            lock (_sync)
            {
                return _decisions.ToArray();
            }
        }
    }

    public event EventHandler? ApprovalsChanged;

    public IReadOnlyList<PendingApproval> Pending
    {
        get
        {
            lock (_sync)
            {
                return _pending.ToArray();
            }
        }
    }

    public Task RespondAsync(string eventId, bool allowed, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _pending = _pending.Where(approval => approval.EventId != eventId).ToList();
            _decisions.Add((eventId, allowed));
        }

        RaiseApprovalsChanged();
        return Task.CompletedTask;
    }

    /// <summary>推送一个待决审批（eventId 由调用方保证唯一）；重复 eventId 忽略。</summary>
    public PendingApproval PushRequest(string sessionId, string toolName, string? reason = null, string? callId = null)
    {
        PendingApproval approval = new($"approval-{Guid.NewGuid():N}", sessionId, toolName, callId, reason,
                                       DateTimeOffset.Now);
        lock (_sync)
        {
            _pending = [.. _pending, approval];
        }

        RaiseApprovalsChanged();
        return approval;
    }

    /// <summary>模拟后端取消一个待决请求（会话取消等）。</summary>
    public void CancelRequest(string eventId)
    {
        lock (_sync)
        {
            _pending = _pending.Where(approval => approval.EventId != eventId).ToList();
        }

        RaiseApprovalsChanged();
    }

    /// <summary>模拟连接代重置：清空待决列表。</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _pending = [];
        }

        RaiseApprovalsChanged();
    }

    private void RaiseApprovalsChanged()
    {
        try
        {
            ApprovalsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // 模拟服务的事件异常无需处理。
        }
    }
}
