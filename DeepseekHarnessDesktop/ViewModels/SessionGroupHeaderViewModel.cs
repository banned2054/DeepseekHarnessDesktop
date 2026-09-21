using System.Windows.Input;

namespace DeepseekHarnessDesktop.ViewModels;

/// <summary>会话列表的分组标题行；成员行是否显示由列表投影按展开态决定。</summary>
public sealed class SessionGroupHeaderViewModel(
    string   key,
    string   title,
    int      sessionCount,
    bool     isExpanded,
    ICommand toggleCommand)
{
    /// <summary>分组标识；工作区 id 或未分组的固定哨兵值。</summary>
    public string Key { get; } = key;

    public string TitleText { get; } = title;

    public int SessionCount { get; } = sessionCount;

    public bool IsExpanded { get; } = isExpanded;

    public string CountText => SessionCount > 0 ? $"{SessionCount} 个会话" : "暂无会话";

    public ICommand ToggleCommand { get; } = toggleCommand;
}
