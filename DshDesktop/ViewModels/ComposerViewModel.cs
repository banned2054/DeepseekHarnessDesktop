using DshDesktop.Core.Services;

namespace DshDesktop.ViewModels;

/// <summary>
///     底部输入区子视图模型：草稿编辑与发送/取消。目标会话与后端连接状态是外部推送的
///     快照（由 MainWindowViewModel 在选中会话、运行状态与连接状态变化时同步），
///     本类不持有会话条目、不订阅后端事件；发送/取消失败经回调上报给窗口级错误显示。
/// </summary>
public sealed class ComposerViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;

    // 错误仍由 MainWindow 级共享 ErrorText 呈现：null 表示清除当前错误。
    private readonly Action<string?> _reportError;

    private string  _draftMessage = string.Empty;
    private bool    _isBackendConnected;
    private bool    _isCancelling;
    private bool    _isSending;
    private bool    _isSessionRunning;
    private string? _sessionId;

    public ComposerViewModel(ISessionService sessionService, Action<string?> reportError)
    {
        _sessionService    = sessionService;
        _reportError       = reportError;
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
        CancelCommand      = new AsyncRelayCommand(CancelGenerationAsync, CanCancelGeneration);
    }

    public AsyncRelayCommand SendMessageCommand { get; }

    public AsyncRelayCommand CancelCommand { get; }

    public string DraftMessage
    {
        get => _draftMessage;
        set
        {
            if (SetProperty(ref _draftMessage, value)) SendMessageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (SetProperty(ref _isSending, value)) SendMessageCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>当前发送/取消的目标会话；未选中会话时为 null。</summary>
    public string? SessionId
    {
        get => _sessionId;
        private set
        {
            if (SetProperty(ref _sessionId, value)) SendMessageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsSessionRunning
    {
        get => _isSessionRunning;
        private set
        {
            if (SetProperty(ref _isSessionRunning, value)) CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBackendConnected
    {
        get => _isBackendConnected;
        private set
        {
            if (SetProperty(ref _isBackendConnected, value)) SendMessageCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>选中会话变化时整体替换上下文；isRunning 取新会话当前的运行状态。</summary>
    public void SetSession(string? sessionId, bool isRunning)
    {
        SessionId        = sessionId;
        IsSessionRunning = isRunning;
    }

    public void SetSessionRunning(bool isRunning)
    {
        IsSessionRunning = isRunning;
    }

    public void SetBackendConnected(bool connected)
    {
        IsBackendConnected = connected;
    }

    private async Task SendMessageAsync()
    {
        if (SessionId is null || string.IsNullOrWhiteSpace(DraftMessage)) return;

        var draftAtSend = DraftMessage;
        var content     = draftAtSend.Trim();
        var requestId   = Guid.NewGuid().ToString();
        IsSending = true;
        _reportError(null);
        try
        {
            await _sessionService.SendPromptAsync(SessionId, requestId, content);
            // 请求完成时草稿若已被改动（发送期间继续输入），不清除新输入的内容。
            if (string.Equals(DraftMessage, draftAtSend, StringComparison.Ordinal)) DraftMessage = string.Empty;
        }
        catch (Exception exception)
        {
            _reportError(exception.Message);
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task CancelGenerationAsync()
    {
        if (SessionId is null) return;

        _isCancelling = true;
        CancelCommand.RaiseCanExecuteChanged();
        try
        {
            await _sessionService.CancelAsync(SessionId);
        }
        catch (Exception exception)
        {
            _reportError(exception.Message);
        }
        finally
        {
            _isCancelling = false;
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanSendMessage()
    {
        return SessionId is not null
            && !IsSending
            && !string.IsNullOrWhiteSpace(DraftMessage)
            && IsBackendConnected;
    }

    private bool CanCancelGeneration()
    {
        return IsSessionRunning && !_isCancelling;
    }
}
