using System.Security.Cryptography;

namespace DeepseekHarnessDesktop.Harness.Utils;

/// <summary>线上标识符铸造；对齐官方客户端的 UUID v4 形态。</summary>
public static class WireIds
{
    /// <summary>RPC 请求 id（幂等回显匹配用，任意字符串亦可）。</summary>
    public static string NewRpcId()
    {
        return NewUuidV4();
    }

    /// <summary>session/prompt 的幂等键：同一 id 的重复提交会被后端去重。</summary>
    public static string NewRequestId()
    {
        return NewUuidV4();
    }

    /// <summary>流复用通道的 streamId。</summary>
    public static string NewStreamId()
    {
        return NewUuidV4();
    }

    private static string NewUuidV4()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }
}
