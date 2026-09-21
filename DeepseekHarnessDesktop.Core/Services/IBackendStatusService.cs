using DeepseekHarnessDesktop.Core.Models;

namespace DeepseekHarnessDesktop.Core.Services;

public interface IBackendStatusService
{
    BackendStatus Status { get; }

    /// <summary>最近一次后端失败的可读原因；无失败时为 null。</summary>
    string? LastError { get; }

    event EventHandler? StatusChanged;
}
