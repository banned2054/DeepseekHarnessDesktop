using DshDesktop.Core.Models;

namespace DshDesktop.ViewModels;

/// <summary>悬浮面板审批横幅的一个待决条目。</summary>
public sealed class PendingApprovalViewModel(PendingApproval approval)
{
    public PendingApproval Approval { get; } = approval;

    public string EventId => Approval.EventId;

    public string SessionId => Approval.SessionId;

    public string ToolName => Approval.ToolName;

    /// <summary>横幅标题：有理由用理由，否则回退到「工具 X 请求授权」。</summary>
    public string HeadlineText => Approval.Reason is { Length: > 0 } reason
        ? reason
        : $"工具 {Approval.ToolName} 请求执行授权";

    public string ToolNameText => Approval.CallId is { Length: > 0 }
        ? $"{Approval.ToolName} · {Approval.CallId}"
        : Approval.ToolName;
}
