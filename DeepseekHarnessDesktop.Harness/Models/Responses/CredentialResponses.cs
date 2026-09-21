namespace DeepseekHarnessDesktop.Harness.Models.Responses;

/// <summary>credentials/describe 返回的单个引用状态；协议保证不携带密钥值。</summary>
public sealed record CredentialInfoWire(bool Configured, string? Source, bool Writable);
