using DeepseekHarnessDesktop.Core.Models;

namespace DeepseekHarnessDesktop.Core.Services;

/// <summary>拥有后端 Host 生命周期的服务；状态部分复用 <see cref="IBackendStatusService" />。</summary>
public interface IBackendHostService : IBackendStatusService, IAsyncDisposable
{
    /// <summary>启动后端并等待就绪；重复调用返回同一结果。</summary>
    Task<BackendConnectionInfo> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>请求正常停止并等待进程退出；超时会升级终止，保证不遗留 Host 进程。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
