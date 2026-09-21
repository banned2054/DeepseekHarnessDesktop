using System.Text.Json;

namespace DeepseekHarnessDesktop.Harness.Models.Responses;

/// <summary>session/list 返回值。</summary>
public sealed record SessionListValue(IReadOnlyList<SessionSummaryWire> Items);

/// <summary>session/create 返回值。</summary>
public sealed record SessionCreateValue(string SessionId, string? AgentPreset = null);

/// <summary>session/prompt 与 session/cancel 的接受回执。</summary>
public sealed record SessionAcceptedValue(bool Accepted);

/// <summary>会话概要（线上形态）。updatedAt 为 Unix 毫秒。</summary>
public sealed record SessionSummaryWire(
    string                      SessionId,
    long                        UpdatedAt,
    bool                        Running,
    bool                        Blank,
    string?                     ParentSessionId = null,
    string?                     Origin          = null,
    string?                     Cwd             = null,
    SessionProjectionHintsWire? Projections     = null);

/// <summary>投影基线；values 按投影键散列，title 投影键为 "title"。</summary>
public sealed record SessionProjectionHintsWire(long AsOfSeq, Dictionary<string, JsonElement>? Values);

/// <summary>
///     session/page 返回值。Records 是 {type:'event', event:{...}} 条目数组（整体以 JsonElement 承载），
///     event 形态与 follow 快照的 records 完全一致，经 FollowFrameJson 统一解析。
/// </summary>
public sealed record SessionPageValue(JsonElement Records, bool HasMore);
