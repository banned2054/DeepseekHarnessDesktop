namespace DshDesktop.Harness.Exceptions;

/// <summary>连接层故障（认证失败、传输断开、协议载波错误）；恢复方式是重建连接并重新订阅。</summary>
public sealed class HarnessConnectionException : Exception
{
    public HarnessConnectionException(string message) : base(message)
    {
    }

    public HarnessConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
