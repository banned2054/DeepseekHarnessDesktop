using System.Text.Json;

namespace DshDesktop.Harness.Models.Rpc;

/// <summary>一元 RPC 请求信封（HTTP POST /api/&lt;method&gt; 的请求体）。</summary>
public sealed record RpcRequestEnvelope(string Type, string RpcId, string Method, JsonElement Payload);

/// <summary>流复用 open 帧。</summary>
public sealed record MuxOpenMessage(string Type, string StreamId, string Endpoint, JsonElement Payload);

/// <summary>流复用 cancel 帧。</summary>
public sealed record MuxCancelMessage(string Type, string StreamId);

/// <summary>流 payload：args 的键与宿主方法形参名一致（session 命名空间统一为 request）。</summary>
public sealed record StreamPayloadWire(Dictionary<string, JsonElement> Args);

/// <summary>$events/result 回执：outcome 三形态（gateway 的 parseRemoteEventResult 严格校验键集）。</summary>
public sealed record EventsResultRequest(string ClientId, string EventId, EventsOutcomeWire Outcome);

/// <summary>
///     outcome：kind=next（委托下家，键集仅 kind）、kind=result（裁决值，approval 为
///     allowed-once/rejected）、kind=rejected（监听器错误，携带 error）。
/// </summary>
public sealed record EventsOutcomeWire(string Kind, string? Value = null, EventsOutcomeErrorWire? Error = null);

public sealed record EventsOutcomeErrorWire(string Name, string Message);
