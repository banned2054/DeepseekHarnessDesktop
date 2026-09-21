using System.Collections.ObjectModel;

namespace DeepseekHarnessDesktop.ViewModels;

/// <summary>
///     一轮对话的过程组（对应参考 Web 客户端的 turn-process 投影）：轮内最终回复之前的
///     中间助手消息与工具调用折叠为一行摘要（「N 次工具调用 · M 条消息 · K 个 subagent」，
///     全为零时显示「已思考」）。默认收起，可展开查看内部条目；子代理委派调用单独计数。
/// </summary>
public sealed class TurnProcessGroupViewModel : ConversationItemViewModel
{
    private bool _isExpanded;

    public TurnProcessGroupViewModel(long seq) : base(seq)
    {
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    /// <summary>过程条目（中间助手消息与工具卡片），按时间线顺序混排。</summary>
    public ObservableCollection<ConversationItemViewModel> Process { get; } = [];

    public RelayCommand ToggleCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>轮内工具调用数（不含子代理委派）。</summary>
    public int ToolCallCount => Process.OfType<ToolActivityItemViewModel>()
                                       .Count(tool => !IsSubagentDelegationTool(tool.Name));

    /// <summary>轮内子代理委派调用数。</summary>
    public int SubagentCount => Process.OfType<ToolActivityItemViewModel>()
                                       .Count(tool => IsSubagentDelegationTool(tool.Name));

    /// <summary>轮内折叠的中间消息数（有正文的提交；思考-only 条目不算消息，与参考实现口径一致）。</summary>
    public int MessageCount => Process.OfType<MessageItemViewModel>()
                                      .Count(message => !string.IsNullOrWhiteSpace(message.Content));

    public bool HasFailed => Process.OfType<ToolActivityItemViewModel>().Any(tool => tool.IsFailed);

    public string FailedText => $"{Process.OfType<ToolActivityItemViewModel>().Count(tool => tool.IsFailed)} 失败";

    public string SummaryText
    {
        get
        {
            var labels = new List<string>();
            if (ToolCallCount > 0) labels.Add($"{ToolCallCount} 次工具调用");

            if (MessageCount > 0) labels.Add($"{MessageCount} 条消息");

            if (SubagentCount > 0) labels.Add($"{SubagentCount} 个 subagent");

            return labels.Count == 0 ? "已思考" : string.Join(" · ", labels);
        }
    }

    /// <summary>子代理委派工具名（与参考实现 isSubagentDelegationTool 一致）。</summary>
    private static bool IsSubagentDelegationTool(string name)
    {
        return name == "subagent" || name.StartsWith("subagent_", StringComparison.Ordinal);
    }

    /// <summary>并入一个过程条目；计数经 Count 属性通知。</summary>
    public void Add(ConversationItemViewModel item)
    {
        Process.Add(item);
        RefreshSummary();
    }

    /// <summary>刷新派生的摘要展示。</summary>
    public void RefreshSummary()
    {
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasFailed));
        OnPropertyChanged(nameof(FailedText));
    }

    /// <summary>按 CallId 查找组内工具卡片（落定结果就地合并）。</summary>
    public ToolActivityItemViewModel? FindTool(string callId)
    {
        return Process.OfType<ToolActivityItemViewModel>()
                      .FirstOrDefault(tool => tool.CallId == callId);
    }
}
