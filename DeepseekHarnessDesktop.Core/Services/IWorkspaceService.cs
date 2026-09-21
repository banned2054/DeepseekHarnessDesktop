using DeepseekHarnessDesktop.Core.Models;

namespace DeepseekHarnessDesktop.Core.Services;

/// <summary>
///     工作区注册表：哪些目录被登记为工作区、每个工作区记账了哪些会话。
///     实现负责订阅后端状态流并在断线恢复后重读基线；客户端只消费投影，
///     不在工作区记账之外维护第二套归属状态。
/// </summary>
public interface IWorkspaceService
{
    /// <summary>工作区集合变化（登记、移除、重排或会话记账变化）时触发。</summary>
    event EventHandler? WorkspacesChanged;

    /// <summary>
    ///     当前工作区列表（后端维护的顺序）。首次调用可能返回空集合
    ///     （基线尚未到达），消费方应订阅 <see cref="WorkspacesChanged" /> 增量更新。
    /// </summary>
    Task<IReadOnlyList<WorkspaceSummary>> GetWorkspacesAsync(CancellationToken cancellationToken = default);
}
