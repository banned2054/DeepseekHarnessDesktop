namespace DeepseekHarnessDesktop.Harness.Models.Responses;

/// <summary>session/selectModel 返回值。</summary>
public sealed record SessionSelectModelValue(SessionModelSelectionWire Selected);

/// <summary>一次模型选择。</summary>
public sealed record SessionModelSelectionWire(
    string  Provider,
    string  Model,
    string? ReasoningEffort = null);

/// <summary>session/modelCatalog 返回值：默认选型、可路由提供方、成组模型与装载失败项。</summary>
public sealed record SessionModelCatalogValue(
    SessionModelSelectionWire?              Default,
    IReadOnlyList<string>?                  RoutableProviders,
    IReadOnlyList<ModelProviderGroupWire>?  Groups   = null,
    IReadOnlyList<ModelCatalogFailureWire>? Failures = null);

/// <summary>目录中一个成功装载的提供方组。</summary>
public sealed record ModelProviderGroupWire(
    string                                Id,
    string                                Name,
    IReadOnlyList<ModelCatalogModelWire>? Models = null);

/// <summary>组内一个模型；reasoning 档位暂无界面消费方，不解释。</summary>
public sealed record ModelCatalogModelWire(
    string  Id,
    string  Name,
    string? Description = null);

/// <summary>目录装载失败的一个提供方。</summary>
public sealed record ModelCatalogFailureWire(
    string  Id,
    string? Name    = null,
    string? Message = null);
