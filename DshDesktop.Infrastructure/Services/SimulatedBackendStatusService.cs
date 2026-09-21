using DshDesktop.Core.Models;
using DshDesktop.Core.Services;

namespace DshDesktop.Infrastructure.Services;

/// <summary>模拟后端：状态在启动后短暂切换为已连接，不产生真实进程。</summary>
public sealed class SimulatedBackendStatusService : IBackendHostService
{
    public BackendStatus Status { get; private set; } = BackendStatus.Offline;

    public string? LastError => null;

    public event EventHandler? StatusChanged;

    public async Task<BackendConnectionInfo> StartAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(BackendStatus.Starting);
        await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        SetStatus(BackendStatus.Connected);
        return new BackendConnectionInfo(new Uri("http://127.0.0.1:0/"));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        SetStatus(BackendStatus.Offline);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SetStatus(BackendStatus.Offline);
        return ValueTask.CompletedTask;
    }

    private void SetStatus(BackendStatus status)
    {
        if (Status == status) return;

        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
