namespace DeepseekHarnessDesktop.Core.Models;

/// <summary>
///     会话累计 token 计量（whole-log 投影值）。四桶互斥：计费输入 =
///     <see cref="UncachedInputTokens" /> + <see cref="CacheReadTokens" /> +
///     <see cref="CacheWriteTokens" />；缓存命中率 = CacheReadTokens / 计费输入。
/// </summary>
public sealed record SessionUsage(
    long UncachedInputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens);

/// <summary>
///     会话累计时间与步数统计（whole-log 投影值）。速度 = DecodeTokens / DecodeMs；
///     DecodeMs 只累计上报了 usage 的步（与 DecodeTokens 同口径）。
/// </summary>
public sealed record SessionStats(
    long   Turns,
    long   Steps,
    double LlmMs,
    double ToolMs,
    double TtftMs,
    long   TtftSteps,
    double DecodeMs,
    long   DecodeTokens);
