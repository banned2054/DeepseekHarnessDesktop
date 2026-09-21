namespace DshDesktop.Core.Models;

/// <summary>
///     后端对一个凭据引用（如 apiKeyEnv 声明的 GLM_API_KEY）的诊断视图。
///     仅描述解析状态，不包含密钥值；权威判定始终由后端完成。
/// </summary>
public sealed record CredentialStatus(bool Configured, string? Source, bool Writable);
