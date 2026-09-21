using DeepseekHarnessDesktop.Core.Models;

namespace DeepseekHarnessDesktop.ViewModels;

/// <summary>模型下拉的一个可选项：某提供方内一个模型的投影。</summary>
public sealed class ModelOptionViewModel(string provider, string providerName, string model, string modelName)
{
    public string Provider { get; } = provider;

    public string ProviderName { get; } = providerName;

    public string Model { get; } = model;

    public string ModelName { get; } = modelName;

    /// <summary>下拉单行展示：模型名 · 提供方名。</summary>
    public string DisplayText => $"{ModelName} · {ProviderName}";

    /// <summary>选型是否与本项一致（provider/model 相同即视为同一项）。</summary>
    public bool Matches(ModelSelection selection)
    {
        return selection.Provider == Provider && selection.Model == Model;
    }
}
