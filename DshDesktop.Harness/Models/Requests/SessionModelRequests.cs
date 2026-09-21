namespace DshDesktop.Harness.Models.Requests;

/// <summary>session/selectModel 请求；ModelSelection + sessionId。</summary>
public sealed record SessionSelectModelRequest(
    string  SessionId,
    string  Provider,
    string  Model,
    string? ReasoningEffort = null);

/// <summary>session/modelCatalog 无参请求。</summary>
public sealed record SessionModelCatalogRequest;
