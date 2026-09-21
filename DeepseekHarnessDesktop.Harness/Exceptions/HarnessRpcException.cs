namespace DeepseekHarnessDesktop.Harness.Exceptions;

/// <summary>后端 RPC 返回的业务错误；Code 形如 session/not-found。业务错误是终态，不应重试。</summary>
public sealed class HarnessRpcException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
