using DshDesktop.Core.Models;
using DshDesktop.Core.Services;

namespace DshDesktop.Infrastructure.Services;

/// <summary>模拟工作区服务：静态登记两个工作区，演示按工作区分组视图。</summary>
public sealed class SimulatedWorkspaceService : IWorkspaceService
{
    private static readonly IReadOnlyList<WorkspaceSummary> Workspaces =
    [
        new("workspace-sample", "示例工作区", "C:/Code/Sample", ["session-native", "session-history"],
            DateTimeOffset.Now.AddHours(-2)),
        new("workspace-docs", "文档整理", "C:/Code/Docs", [], DateTimeOffset.Now.AddHours(-3))
    ];

    public event EventHandler? WorkspacesChanged
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<WorkspaceSummary>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Workspaces);
    }
}
