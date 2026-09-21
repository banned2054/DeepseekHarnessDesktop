namespace DeepseekHarnessDesktop.Core.Models;

/// <summary>会话时间线中的一个可显示条目；Seq 与后端事件日志序号对齐，决定时间线顺序。</summary>
public abstract record ConversationEntry(long Seq, DateTimeOffset CreatedAt);

/// <summary>工具调用的当前状态。</summary>
public enum ToolActivityStatus
{
    /// <summary>已发起，尚未有结果事件。</summary>
    Running,

    /// <summary>结果为普通内容。</summary>
    Succeeded,

    /// <summary>结果带 isError 标记或错误身份。</summary>
    Failed
}

/// <summary>
///     一次工具调用（tool/call 与其 tool/result 的折叠视图）。
///     ArgumentsJson 是模型产出的原始参数 JSON 文本；ResultText 是结果内容块的文本拼接，
///     截断策略由显示层决定。Turn 是后端标注的轮次序号。
/// </summary>
public sealed record ToolActivity(
    long               Seq,
    string             CallId,
    string             Name,
    string?            ArgumentsJson,
    ToolActivityStatus Status,
    string?            ResultText,
    string?            ErrorReason,
    DateTimeOffset     CreatedAt,
    DateTimeOffset?    CompletedAt = null,
    long?              Turn        = null) : ConversationEntry(Seq, CreatedAt)
{
    public ToolActivity Settle(
        ToolActivityStatus status, string? resultText, string? errorReason, DateTimeOffset completedAt)
    {
        return this with
        {
            Status = status,
            ResultText = resultText,
            ErrorReason = errorReason,
            CompletedAt = completedAt
        };
    }
}

/// <summary>
///     turn/end 边界事件的占位条目（不可见）。一轮对话以 turn/end 收束：
///     界面据此把该轮的过程条目（中间助手消息、工具调用）折叠为过程组，
///     规则与参考 Web 客户端的 turn-process 投影一致。
///     <see cref="Reason" /> 是结束原因（completed/aborted/interrupted）；真实 Host 在
///     follow 快照尾部会为「开放中的轮」合成 interrupted 边界使窗口自洽，消费方需识别。
/// </summary>
public sealed record TurnBoundary(
    long           Seq,
    long           Turn,
    DateTimeOffset CreatedAt,
    string?        Reason = null) : ConversationEntry(Seq, CreatedAt);
