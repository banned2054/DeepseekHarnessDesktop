namespace DeepseekHarnessDesktop.Infrastructure.Exceptions;

/// <summary>后端 Host 进程启动失败或意外退出。</summary>
public sealed class BackendProcessException(string message, string? stderrTail, Exception? innerException)
    : Exception(Describe(message, stderrTail), innerException)
{
    public BackendProcessException(string message, string? stderrTail = null)
        : this(message, stderrTail, null)
    {
    }

    /// <summary>Host stderr 的尾部片段，用于诊断；可能为空。</summary>
    public string? StderrTail { get; } = stderrTail;

    private static string Describe(string message, string? stderrTail)
    {
        var text = message.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase)
            ? $"{message}（监听端口被占用）"
            : message;
        return string.IsNullOrWhiteSpace(stderrTail) ? text : $"{text}\n{stderrTail}";
    }
}
