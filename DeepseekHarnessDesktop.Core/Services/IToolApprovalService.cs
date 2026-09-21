using DeepseekHarnessDesktop.Core.Models;

namespace DeepseekHarnessDesktop.Core.Services;

/// <summary>
///     工具审批交互（approval/request 瀑布的客户端闭环）：呈现待决请求并把
///     用户裁决回执给后端。审批与生成通信分离——本契约只承载裁决通道。
/// </summary>
public interface IToolApprovalService
{
    /// <summary>当前待决的审批（跨会话；界面按会话过滤展示）。</summary>
    IReadOnlyList<PendingApproval> Pending { get; }

    /// <summary>待决请求增删（新请求到达、后端取消、连接代重置）时触发。</summary>
    event EventHandler? ApprovalsChanged;

    /// <summary>
    ///     回复一次审批（$events/result：allowed-once / rejected）。
    ///     幂等：对已取消或已回复的请求回复是安全的（后端按 eventId 对账，失配被忽略）。
    /// </summary>
    Task RespondAsync(string eventId, bool allowed, CancellationToken cancellationToken = default);
}
