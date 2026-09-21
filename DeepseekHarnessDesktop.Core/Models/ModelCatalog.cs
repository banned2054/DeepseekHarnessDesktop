namespace DeepseekHarnessDesktop.Core.Models;

/// <summary>一次完整的模型选型（provider/model，可选推理档位）。</summary>
public sealed record ModelSelection(string Provider, string Model, string? ReasoningEffort = null);

/// <summary>模型目录：默认选型、各提供方可选模型与装载失败项。</summary>
public sealed record ModelCatalog(
    ModelSelection?                    Default,
    IReadOnlyList<ModelProviderGroup>  Groups,
    IReadOnlyList<ModelCatalogFailure> Failures);

/// <summary>一个提供方及其成功装载的模型清单。</summary>
public sealed record ModelProviderGroup(string Id, string Name, IReadOnlyList<ModelCatalogEntry> Models);

/// <summary>目录中一个可被选中的模型。</summary>
public sealed record ModelCatalogEntry(string Id, string Name);

/// <summary>一个目录装载失败的提供方（如凭据不可用）；保留展示诊断用。</summary>
public sealed record ModelCatalogFailure(string Id, string Name, string Message);
