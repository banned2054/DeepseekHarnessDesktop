namespace DeepseekHarnessDesktop.Core.Models;

/// <summary>
///     一个待决的工具审批请求（approval/request 瀑布）。权威裁决在后端：
///     客户端回答后以 approval/decided 事件落盘；请求可能被后端取消（cancel 帧）。
/// </summary>
public sealed record PendingApproval(
    string         EventId,
    string         SessionId,
    string         ToolName,
    string?        CallId,
    string?        Reason,
    DateTimeOffset ReceivedAt);
