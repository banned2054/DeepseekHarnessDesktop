namespace DeepseekHarnessDesktop.Harness.Models;

public sealed record HarnessConnectionOptions(string ExecutablePath, string Profile, TimeSpan StartupTimeout);
