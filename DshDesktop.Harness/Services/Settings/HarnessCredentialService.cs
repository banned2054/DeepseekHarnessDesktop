using DshDesktop.Core.Models;
using DshDesktop.Harness.Json;
using DshDesktop.Harness.Services.Connection;

namespace DshDesktop.Harness.Services.Settings;

/// <summary>
///     通过后端 credentials/describe 查询凭据引用的解析状态。
///     客户端不解析凭据文件，也不接触密钥值；判定始终以当前 Host 的回答为准。
/// </summary>
public sealed class HarnessCredentialService(HarnessConnection connection)
{
    public async Task<CredentialStatus?> DescribeAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        // describe 的形参 refs 本身就是数组（不是请求对象），args 形如 { "refs": ["GLM_API_KEY"] }。
        var entries = await connection.InvokeAsync("credentials/describe", [reference],
                                                   HarnessJsonContext.Default.StringArray,
                                                   HarnessJsonContext.Default.DictionaryStringCredentialInfoWire,
                                                   cancellationToken, "refs")
                                      .ConfigureAwait(false);
        return entries.TryGetValue(reference, out var info)
            ? new CredentialStatus(info.Configured, info.Source, info.Writable)
            : null;
    }
}
