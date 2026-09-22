using DshDesktop.Core.Models;
using DshDesktop.Core.Services;
using System.Collections.ObjectModel;

namespace DshDesktop.ViewModels;

/// <summary>
///     底部输入区子视图模型：草稿编辑、发送/取消与模型选择。目标会话与后端连接状态是
///     外部推送的快照（由 MainWindowViewModel 在选中会话、运行状态与连接状态变化时同步），
///     本类不持有会话条目、不订阅后端事件；生效选型由 root 转发的 follow 快照与回声更新，
///     发送/取消/选型失败经回调上报给窗口级错误显示。
/// </summary>
public sealed class ComposerViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;

    // 错误仍由 MainWindow 级共享 ErrorText 呈现：null 表示清除当前错误。
    private readonly Action<string?> _reportError;

    private ModelSelection?       _currentModel;
    private ModelCatalog?         _modelCatalog;
    private ModelOptionViewModel? _selectedModelOption;

    private string  _draftMessage = string.Empty;
    private bool    _isBackendConnected;
    private bool    _isCancelling;
    private bool    _isSelectingModel;
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

    /// <summary>模型下拉可选项：目录扁平投影；当前选型不在目录中时补一项占位。</summary>
    public ObservableCollection<ModelOptionViewModel> ModelOptions { get; } = [];

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
            if (SetProperty(ref _sessionId, value))
            {
                SendMessageCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsModelPickerEnabled));
            }
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
            if (SetProperty(ref _isBackendConnected, value))
            {
                SendMessageCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsModelPickerEnabled));
            }
        }
    }

    /// <summary>当前会话生效的模型选型；由 root 转发的 follow 快照与 model/selection 回声更新。</summary>
    public ModelSelection? CurrentModel
    {
        get => _currentModel;
        private set
        {
            if (SetProperty(ref _currentModel, value)) SyncSelectedModelOption();
        }
    }

    /// <summary>下拉选中项；用户改动即发起选型。生效值仍以后端回声为准，失败时回退显示。</summary>
    public ModelOptionViewModel? SelectedModelOption
    {
        get => _selectedModelOption;
        set
        {
            if (SetProperty(ref _selectedModelOption, value) && value is not null) _ = SelectModelAsync(value);
        }
    }

    /// <summary>下拉是否可用：目录已加载、有选中会话且后端已连接。</summary>
    public bool IsModelPickerEnabled => ModelOptions.Count > 0 && SessionId is not null && IsBackendConnected;

    /// <summary>展示用生效选型：会话未选过型时回退目录默认。</summary>
    private ModelSelection? EffectiveModel => _currentModel ?? _modelCatalog?.Default;

    /// <summary>
    ///     选中会话变化时整体替换上下文；isRunning 取新会话当前的运行状态。会话身份变化时
    ///     重置会话级选型并让下拉回退目录默认——必须在新会话 follow 启动前调用，否则清空
    ///     动作会把随后（可能同步）到达的新会话快照选型抹掉。
    /// </summary>
    public void SetSession(string? sessionId, bool isRunning)
    {
        var sessionChanged = sessionId != SessionId;
        SessionId        = sessionId;
        IsSessionRunning = isRunning;
        if (!sessionChanged) return;

        CurrentModel = null;
        SyncSelectedModelOption();
    }

    public void SetSessionRunning(bool isRunning)
    {
        IsSessionRunning = isRunning;
    }

    public void SetBackendConnected(bool connected)
    {
        IsBackendConnected = connected;
    }

    /// <summary>接收 root 转发的生效选型：follow 快照投影或 model/selection 回声（后端权威）。</summary>
    public void ApplyCurrentModel(ModelSelection? selection)
    {
        CurrentModel = selection;
    }

    private async Task SelectModelAsync(ModelOptionViewModel option)
    {
        // 与当前生效选型相同、无会话或已有选型在途：回退显示，不重复请求。
        // 失败回退读取当前生效选型而非请求时的值：在途请求跨会话完成时不会污染新会话显示。
        if (SessionId is null || _isSelectingModel
                              || (EffectiveModel is { } effective && option.Matches(effective)))
        {
            SyncSelectedModelOption();
            return;
        }

        _isSelectingModel = true;
        try
        {
            // 生效值以 follow 流的 model/selection 回声为准（模拟实现同路径）。
            await _sessionService.SelectModelAsync(SessionId, option.Provider, option.Model);
        }
        catch (Exception exception)
        {
            _reportError(exception.Message);
            SyncSelectedModelOption();
        }
        finally
        {
            _isSelectingModel = false;
        }
    }

    /// <summary>把下拉选中项对齐到生效选型；目录不含该选型时先补占位项。</summary>
    private void SyncSelectedModelOption()
    {
        var effective = EffectiveModel;
        if (effective is null)
        {
            _selectedModelOption = null;
            OnPropertyChanged(nameof(SelectedModelOption));
            return;
        }

        var match = ModelOptions.FirstOrDefault(option => option.Matches(effective));
        if (match is null)
        {
            match = new ModelOptionViewModel(effective.Provider, effective.Provider,
                                             effective.Model, effective.Model);
            ModelOptions.Insert(0, match);
        }

        _selectedModelOption = match;
        OnPropertyChanged(nameof(SelectedModelOption));
    }

    /// <summary>目录变化时重建下拉选项（当前生效选型保持可选）。</summary>
    private void RebuildModelOptions()
    {
        ModelOptions.Clear();
        if (_modelCatalog is { } catalog)
            foreach (var group in catalog.Groups)
            foreach (var model in group.Models)
                ModelOptions.Add(new ModelOptionViewModel(group.Id, group.Name, model.Id, model.Name));

        OnPropertyChanged(nameof(IsModelPickerEnabled));
        SyncSelectedModelOption();
    }

    /// <summary>拉取模型目录并重建下拉选项；异常抛给调用方决定上报与重试语义。</summary>
    public async Task RefreshModelCatalogAsync(CancellationToken cancellationToken = default)
    {
        _modelCatalog = await _sessionService.GetModelCatalogAsync(cancellationToken);
        RebuildModelOptions();
    }

    /// <summary>重连等 fire-and-forget 场景的目录刷新：失败经错误回调上报。</summary>
    public async Task RefreshModelCatalogSafeAsync()
    {
        try
        {
            await RefreshModelCatalogAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _reportError($"模型目录加载失败：{exception.Message}");
        }
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
