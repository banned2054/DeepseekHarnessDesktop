namespace DeepseekHarnessDesktop.Core.Models;

/// <summary>工作区概要：登记的目录与会话记账；权威状态由 Harness 后端持有。</summary>
public sealed record WorkspaceSummary(
    string                Id,
    string                Title,
    string                Path,
    IReadOnlyList<string> SessionIds,
    DateTimeOffset        UpdatedAt);
