using DshDesktop.Core.Models;
using DshDesktop.Core.Services;
using DshDesktop.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DshDesktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>整页都被过滤条目（注入上下文）时的连续翻页上限，避免一次触发连环拉取。</summary>
    private const int EmptyPageFollowUpLimit = 4;

    // 会话列表展示：单列表或按工作区分组（对齐参考客户端的视图选项）。
    private const int SessionListModeFlat        = 0;
    private const int SessionListModeByWorkspace = 1;

    private const    string              UngroupedKey = "$ungrouped";
    private readonly IBackendHostService _backendHostService;
    private readonly HashSet<string>     _collapsedGroups = [];
    private readonly Action<Action>      _postToUi;

    private readonly ISessionService      _sessionService;
    private readonly IToolApprovalService _toolApprovalService;
    private readonly IWorkspaceService    _workspaceService;

    // 时间线组装状态：快照、增量与翻页共用同一套分组规则。
    private TimelineAssembly _assembly;
    private ModelSelection?  _currentModel;
    private string           _errorText = string.Empty;

    private CancellationTokenSource? _followCancellation;

    // follow 代际门闩：随 BeginFollow 递增；旧订阅循环在锁内校验代际后才应用更新，
    // 防止被抢占的旧循环把上一会话的迟到更新写进新会话的状态。
    private readonly Lock _followGate = new();

    private int  _followEpoch;
    private bool _hasMoreHistory;

    // 历史窗口状态：快照游标（throughSeq）、窗口首条事件 seq（beforeSeq）与是否还有更早历史。
    private long _historyThroughSeq;
    private bool _isInitialized;
    private bool _isLoadingOlder;
    private bool _isSelectingModel;
    private int  _listRefreshPending;

    // 模型选择：目录为全局只读快照，当前选型随会话 follow 流回声更新（后端权威）。
    private ModelCatalog?         _modelCatalog;
    private ModelOptionViewModel? _selectedModelOption;
    private SessionItemViewModel? _selectedSession;

    // 默认按工作区分组，对齐参考 Web 客户端的默认视图选项。
    private int                   _sessionListModeIndex = SessionListModeByWorkspace;
    private SessionStats?         _stats;
    private long                  _statsSeq;
    private string?               _streamingAttemptId;
    private MessageItemViewModel? _streamingMessage;

    // 已加载窗口的全量条目（按 seq 升序）；翻页折叠开关变化时据此整体重建时间线。
    private List<ConversationEntry> _timelineEntries = [];

    // 会话统计：快照投影基线 + control 流整值更新，均带投影 seq 做乱序 gating。
    private SessionUsage?                   _usage;
    private long                            _usageSeq;
    private long                            _windowStartSeq = 1;
    private IReadOnlyList<WorkspaceSummary> _workspaces     = [];

    /// <summary>
    ///     保持旧测试与宿主构造调用的兼容性。未提供审批服务时，界面没有审批来源，
    ///     但纯会话测试不应因此必须组装基础设施实现。
    /// </summary>
    public MainWindowViewModel(
        ISessionService     sessionService,
        IBackendHostService backendHostService,
        IWorkspaceService   workspaceService,
        bool                isSimulatedMode = true,
        Action<Action>?     postToUi        = null)
        : this(sessionService, backendHostService, workspaceService, EmptyToolApprovalService.Instance,
               isSimulatedMode, postToUi)
    {
    }

    public MainWindowViewModel(
        ISessionService      sessionService,
        IBackendHostService  backendHostService,
        IWorkspaceService    workspaceService,
        IToolApprovalService toolApprovalService,
        bool                 isSimulatedMode = true,
        Action<Action>?      postToUi        = null)
    {
        _sessionService      = sessionService;
        _backendHostService  = backendHostService;
        _workspaceService    = workspaceService;
        _toolApprovalService = toolApprovalService;
        IsSimulationMode     = isSimulatedMode;
        _postToUi            = postToUi ?? (action => action());
        // 草稿/发送/取消已迁入 Composer；失败仍走窗口级 ErrorText（null 表示清除）。
        Composer             = new ComposerViewModel(sessionService, text => ErrorText = text ?? string.Empty);
        NewSessionCommand    = new AsyncRelayCommand(CreateNewSessionAsync);
        LoadOlderCommand     = new AsyncRelayCommand(LoadOlderAsync, CanLoadOlder);
        SelectSessionCommand = new RelayCommand<SessionItemViewModel>(session => SelectedSession = session);
        ToggleGroupCommand   = new RelayCommand<SessionGroupHeaderViewModel>(ToggleGroup);
        ApproveApprovalCommand =
            new RelayCommand<PendingApprovalViewModel>(approval => _ = RespondApprovalAsync(approval, true));
        RejectApprovalCommand =
            new RelayCommand<PendingApprovalViewModel>(approval => _ = RespondApprovalAsync(approval, false));
        _sessionService.SessionsChanged       += OnSessionsChanged;
        _workspaceService.WorkspacesChanged   += OnWorkspacesChanged;
        _backendHostService.StatusChanged     += OnBackendStatusChanged;
        _toolApprovalService.ApprovalsChanged += OnApprovalsChanged;
        _assembly                             =  CreateAssembly();
        Composer.SetBackendConnected(IsBackendConnected);
    }

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    /// <summary>会话列表的呈现行：会话行与分组标题行混排，按当前视图模式投影。</summary>
    public ObservableCollection<object> SessionRows { get; } = [];

    public ObservableCollection<ConversationItemViewModel> ConversationItems { get; } = [];

    /// <summary>模型下拉可选项：目录扁平投影；当前选型不在目录中时补一项占位。</summary>
    public ObservableCollection<ModelOptionViewModel> ModelOptions { get; } = [];

    /// <summary>当前选中会话的待决审批（审批横幅）；随审批增删与会话切换重建。</summary>
    public ObservableCollection<PendingApprovalViewModel> SessionPendingApprovals { get; } = [];

    public AsyncRelayCommand NewSessionCommand { get; }

    /// <summary>底部输入区子视图模型：草稿与发送/取消；会话/后端上下文由本类在状态变化时推送。</summary>
    public ComposerViewModel Composer { get; }

    public AsyncRelayCommand LoadOlderCommand { get; }

    public RelayCommand<SessionItemViewModel> SelectSessionCommand { get; }

    public RelayCommand<SessionGroupHeaderViewModel> ToggleGroupCommand { get; }

    public RelayCommand<PendingApprovalViewModel> ApproveApprovalCommand { get; }

    public RelayCommand<PendingApprovalViewModel> RejectApprovalCommand { get; }

    /// <summary>选中会话是否有待决审批（控制悬浮面板审批横幅区域）。</summary>
    public bool HasSessionPendingApprovals => SessionPendingApprovals.Count > 0;

    /// <summary>会话列表视图模式：0 单列表，1 按工作区。偏好持久化随阶段 4 桌面设置接入。</summary>
    public int SessionListModeIndex
    {
        get => _sessionListModeIndex;
        set
        {
            if (SetProperty(ref _sessionListModeIndex, value)) RebuildSessionRows();
        }
    }

    public SessionItemViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            var previous = _selectedSession;
            if (SetProperty(ref _selectedSession, value))
            {
                if (previous is not null)
                {
                    previous.PropertyChanged -= OnSelectedSessionPropertyChanged;
                    previous.IsCurrent       =  false;
                }

                if (value is not null)
                {
                    value.PropertyChanged += OnSelectedSessionPropertyChanged;
                    value.IsCurrent       =  true;
                }

                // 先清空上一会话的选型再订阅：新会话的当前选型由其快照携带
                // （模拟实现的快照可能同步到达，先启动订阅再清空会把快照值抹掉）。
                CurrentModel = null;
                Usage        = null;
                Stats        = null;
                _usageSeq    = 0;
                _statsSeq    = 0;
                _            = FollowSelectedSessionAsync(value);
                RebuildSessionPendingApprovals();
                // 重建行投影：工作区头的 IsCurrent（是否包含当前会话）随选中变化。
                RebuildSessionRows();
                Composer.SetSession(value?.Id, value?.Running ?? false);
                OnPropertyChanged(nameof(IsSessionRunning));
                OnPropertyChanged(nameof(IsModelPickerEnabled));
                LoadOlderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>当前会话生效的模型选型；由 follow 流的快照投影与 model/selection 回声更新。</summary>
    public ModelSelection? CurrentModel
    {
        get => _currentModel;
        private set
        {
            if (SetProperty(ref _currentModel, value)) SyncSelectedModelOption();
        }
    }

    /// <summary>
    ///     下拉选中项；用户改动即发起选型。生效值仍以后端回声为准，失败时回退显示。
    /// </summary>
    public ModelOptionViewModel? SelectedModelOption
    {
        get => _selectedModelOption;
        set
        {
            if (SetProperty(ref _selectedModelOption, value) && value is not null) _ = SelectModelAsync(value);
        }
    }

    /// <summary>下拉是否可用：目录已加载、有选中会话且后端已连接。</summary>
    public bool IsModelPickerEnabled
        => ModelOptions.Count > 0 && SelectedSession is not null && IsBackendConnected;

    /// <summary>展示用生效选型：会话未选过型时回退目录默认。</summary>
    private ModelSelection? EffectiveModel => _currentModel ?? _modelCatalog?.Default;

    /// <summary>当前会话累计 token 计量（whole-log 投影；无数据时为 null）。</summary>
    public SessionUsage? Usage
    {
        get => _usage;
        private set
        {
            if (SetProperty(ref _usage, value))
            {
                OnPropertyChanged(nameof(UsageValueText));
                OnPropertyChanged(nameof(CacheHitValueText));
                OnPropertyChanged(nameof(UsageDetailText));
                OnPropertyChanged(nameof(HasStatsData));
            }
        }
    }

    /// <summary>当前会话累计时间/步数统计（whole-log 投影；无数据时为 null）。</summary>
    public SessionStats? Stats
    {
        get => _stats;
        private set
        {
            if (SetProperty(ref _stats, value))
            {
                OnPropertyChanged(nameof(SpeedValueText));
                OnPropertyChanged(nameof(StatsDetailText));
                OnPropertyChanged(nameof(HasStatsData));
            }
        }
    }

    /// <summary>统计栏 Token 用量文案：总量 = 计费输入（未命中 + 缓存读 + 缓存写）+ 输出。</summary>
    public string UsageValueText
    {
        get
        {
            if (Usage is not { } usage) return "Token 用量 —";

            var total = usage.UncachedInputTokens + usage.CacheReadTokens + usage.CacheWriteTokens
                      + usage.OutputTokens;
            return total > 0 ? $"Token 用量 {TokenFormat.Compact(total)}" : "Token 用量 —";
        }
    }

    /// <summary>统计栏缓存命中率文案：缓存读 / 计费输入；无计费输入时显示 —。</summary>
    public string CacheHitValueText
    {
        get
        {
            if (Usage is not { } usage) return "缓存命中 —";

            var billed = usage.UncachedInputTokens + usage.CacheReadTokens + usage.CacheWriteTokens;
            return TokenFormat.CacheHitPercent(usage.CacheReadTokens, billed) is { } percent
                ? $"缓存命中 {percent}%"
                : "缓存命中 —";
        }
    }

    /// <summary>统计栏生成速度文案：解码 token / 解码时长；无解码数据时显示 —。</summary>
    public string SpeedValueText =>
        Stats is { DecodeMs: > 0 } stats
            ? $"生成速度 {TokenFormat.TokensPerSecond(stats.DecodeTokens / (stats.DecodeMs / 1000))}"
            : "生成速度 —";

    /// <summary>usage 明细悬停：四个桶的精确计数。</summary>
    public string? UsageDetailText =>
        Usage is { } usage
            ? $"未命中输入 {usage.UncachedInputTokens} · 缓存读 {usage.CacheReadTokens}"
            + $" · 缓存写 {usage.CacheWriteTokens} · 输出 {usage.OutputTokens}"
            : null;

    /// <summary>统计明细悬停：轮次、步数与累计耗时。</summary>
    public string? StatsDetailText =>
        Stats is { } stats
            ? $"{stats.Turns} 轮 · {stats.Steps} 步 · 模型耗时 {stats.LlmMs / 1000:0.#}s"
            + $" · 工具耗时 {stats.ToolMs                                 / 1000:0.#}s"
            : null;

    /// <summary>
    ///     统计条是否显示：对齐 WebUI StatsPills 的空会话口径——出现过至少一步生成
    ///     或有任何计费 token 才显示。不能只判 Usage/Stats 非 null：冷会话的 follow
    ///     快照会携带全 0 的投影 wire 视图，占位「—」不该在空对话露出。
    /// </summary>
    public bool HasStatsData => Stats is { Steps: > 0 } || (Usage is { } usage &&
                                                            usage.UncachedInputTokens + usage.CacheReadTokens +
                                                            usage.CacheWriteTokens    + usage.OutputTokens > 0);

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsSimulationMode { get; }

    public string SimulationNotice => "模拟模式 · 未连接真实 Harness";

    public bool IsSessionRunning => SelectedSession?.Running ?? false;

    public bool HasMoreHistory
    {
        get => _hasMoreHistory;
        private set
        {
            if (SetProperty(ref _hasMoreHistory, value)) LoadOlderCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsLoadingOlder
    {
        get => _isLoadingOlder;
        private set
        {
            if (SetProperty(ref _isLoadingOlder, value))
            {
                LoadOlderCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(LoadOlderText));
            }
        }
    }

    /// <summary>「加载更早」按钮文案；加载中切换为进行时提示（对齐参考客户端的按钮分页）。</summary>
    public string LoadOlderText => IsLoadingOlder ? "加载中…" : "加载更早";

    public string BackendStatusText
    {
        get
        {
            return _backendHostService.Status switch
            {
                BackendStatus.Starting  => "后端启动中…",
                BackendStatus.Connected => IsSimulationMode ? "模拟后端已连接" : "Harness 后端已连接",
                BackendStatus.Error     => "后端错误",
                _                       => "后端未连接"
            };
        }
    }

    public string? BackendErrorDetail => _backendHostService.LastError;

    public bool HasBackendError => !string.IsNullOrWhiteSpace(BackendErrorDetail);

    public bool IsBackendConnected => _backendHostService.Status == BackendStatus.Connected;

    public bool IsBackendDisconnected => !IsBackendConnected;

    public async ValueTask DisposeAsync()
    {
        var cancellation = Interlocked.Exchange(ref _followCancellation, null);
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        if (SelectedSession is not null) SelectedSession.PropertyChanged -= OnSelectedSessionPropertyChanged;

        _sessionService.SessionsChanged       -= OnSessionsChanged;
        _workspaceService.WorkspacesChanged   -= OnWorkspacesChanged;
        _backendHostService.StatusChanged     -= OnBackendStatusChanged;
        _toolApprovalService.ApprovalsChanged -= OnApprovalsChanged;
    }

    private async Task SelectModelAsync(ModelOptionViewModel option)
    {
        // 与当前生效选型相同、无会话或已有选型在途：回退显示，不重复请求。
        if (SelectedSession is null || _isSelectingModel
                                    || (EffectiveModel is { } effective && option.Matches(effective)))
        {
            SyncSelectedModelOption();
            return;
        }

        _isSelectingModel = true;
        try
        {
            // 生效值以 follow 流的 model/selection 回声为准（模拟实现同路径）。
            await _sessionService.SelectModelAsync(SelectedSession.Id, option.Provider, option.Model);
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
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

    private async Task RefreshModelCatalogAsync(CancellationToken cancellationToken)
    {
        _modelCatalog = await _sessionService.GetModelCatalogAsync(cancellationToken);
        RebuildModelOptions();
    }

    private async Task RefreshModelCatalogSafeAsync()
    {
        try
        {
            await RefreshModelCatalogAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorText = $"模型目录加载失败：{exception.Message}";
        }
    }

    /// <summary>呈现组合阶段发现的问题（例如真实后端配置缺失回退模拟）。</summary>
    public void ShowStartupNotice(string text)
    {
        ErrorText = text;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        _isInitialized = true;
        try
        {
            await _backendHostService.StartAsync(cancellationToken);
            // 工作区订阅先于会话列表启动：基线未到达时先按空投影分组，
            // WorkspacesChanged 事件到达后再重组（对齐参考客户端的 pending 表现）。
            await RefreshWorkspacesAsync(cancellationToken);
            await RefreshSessionsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _isInitialized = false;
        }
        catch (Exception exception)
        {
            _isInitialized = false;
            ErrorText      = exception.Message;
        }

        // 模型目录独立加载：失败不阻塞会话列表，下拉保持禁用并提示原因。
        if (IsBackendConnected)
            try
            {
                await RefreshModelCatalogAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _isInitialized = false;
            }
            catch (Exception exception)
            {
                ErrorText = $"模型目录加载失败：{exception.Message}";
            }
    }

    private async Task RefreshSessionsAsync(CancellationToken cancellationToken)
    {
        var selectedSessionId = SelectedSession?.Id;
        var summaries = (await _sessionService.GetSessionsAsync(cancellationToken))
                       .Where(summary => !summary.Blank || summary.Id == selectedSessionId).ToArray();

        // 就地更新既有条目：重建 ObservableCollection 会替换选中实例，
        // 触发重新订阅并让新快照清掉流式气泡，生成中的内容会闪动。
        var existingById = Sessions.ToDictionary(session => session.Id);
        for (var index = Sessions.Count - 1; index >= 0; index--)
            if (summaries.All(summary => summary.Id != Sessions[index].Id))
                Sessions.RemoveAt(index);

        var insertIndex = 0;
        foreach (var summary in summaries)
        {
            if (existingById.TryGetValue(summary.Id, out var item))
            {
                item.UpdateSummary(summary);
                var currentIndex = Sessions.IndexOf(item);
                if (currentIndex != insertIndex) Sessions.Move(currentIndex, insertIndex);
            }
            else
            {
                Sessions.Insert(insertIndex, new SessionItemViewModel(summary));
            }

            insertIndex++;
        }

        if (SelectedSession is not null && existingById.ContainsKey(SelectedSession.Id))
        {
            // 选中会话仍存在：实例未变，不触发重订阅；新增/移除/排序变化仍需重建行投影。
            OnPropertyChanged(nameof(IsSessionRunning));
            Composer.SetSessionRunning(SelectedSession.Running);
        }
        else
        {
            var selected = Sessions.FirstOrDefault(session => session.Id == SelectedSession?.Id)
                        ?? Sessions.FirstOrDefault();
            if (!ReferenceEquals(SelectedSession, selected))
            {
                SelectedSession = selected;
            }
            else
            {
                OnPropertyChanged(nameof(IsSessionRunning));
                Composer.SetSessionRunning(SelectedSession?.Running ?? false);
            }
        }

        RebuildSessionRows();
    }

    /// <summary>按当前视图模式把 Sessions 投影为呈现行；分组模式对齐参考客户端投影语义。</summary>
    private void RebuildSessionRows()
    {
        SessionRows.Clear();
        if (_sessionListModeIndex == SessionListModeFlat)
        {
            foreach (var session in Sessions) SessionRows.Add(session);

            return;
        }

        // 按工作区分组：组序为后端顺序，成员按更新时间降序（参考客户端 orderBy=updated）；
        // 不被任何工作区记账的会话（含新建空白会话）落入「未分组」，仅在有成员时显示。
        var accounted = new HashSet<string>();
        foreach (var workspace in _workspaces)
            AppendGroup(workspace.Id, workspace.Title,
                        Sessions.Where(session => workspace.SessionIds.Contains(session.Id)),
                        accounted);

        AppendGroup(UngroupedKey, "未分组",
                    Sessions.Where(session => !accounted.Contains(session.Id)),
                    accounted);
    }

    private void AppendGroup(
        string key, string title, IEnumerable<SessionItemViewModel> members, HashSet<string> accounted)
    {
        var memberList = members.ToList();
        if (key == UngroupedKey && memberList.Count == 0) return;

        foreach (var member in memberList) accounted.Add(member.Id);

        var expanded = !_collapsedGroups.Contains(key);
        SessionRows.Add(new SessionGroupHeaderViewModel(key, title, memberList.Count, expanded, ToggleGroupCommand,
                                                        memberList.Any(member => member.IsCurrent)));
        if (expanded)
            foreach (var member in memberList)
                SessionRows.Add(member);
    }

    private void ToggleGroup(SessionGroupHeaderViewModel? header)
    {
        if (header is null) return;

        if (!_collapsedGroups.Remove(header.Key)) _collapsedGroups.Add(header.Key);

        RebuildSessionRows();
    }

    /// <summary>审批列表变化可能在任一线程到达：回到界面线程重建选中会话的待决投影。</summary>
    private void OnApprovalsChanged(object? sender, EventArgs e)
    {
        _postToUi(RebuildSessionPendingApprovals);
    }

    /// <summary>把待决审批投影为当前选中会话的横幅条目。</summary>
    private void RebuildSessionPendingApprovals()
    {
        SessionPendingApprovals.Clear();
        if (SelectedSession is not null)
            foreach (var approval in _toolApprovalService.Pending)
                if (approval.SessionId == SelectedSession.Id)
                    SessionPendingApprovals.Add(new PendingApprovalViewModel(approval,
                                                                             ApproveApprovalCommand,
                                                                             RejectApprovalCommand));

        OnPropertyChanged(nameof(HasSessionPendingApprovals));
    }

    /// <summary>回复审批；裁决结果由服务的 ApprovalsChanged 回流（移除条目）。</summary>
    private async Task RespondApprovalAsync(PendingApprovalViewModel? approval, bool allowed)
    {
        if (approval is null) return;

        try
        {
            await _toolApprovalService.RespondAsync(approval.EventId, allowed);
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private async Task RefreshWorkspacesAsync(CancellationToken cancellationToken)
    {
        _workspaces = await _workspaceService.GetWorkspacesAsync(cancellationToken);
        RebuildSessionRows();
    }

    private void OnWorkspacesChanged(object? sender, EventArgs e)
    {
        // 后台线程事件：回到界面线程重读投影（工作区变更频率低，不做合并）。
        _postToUi(() => _ = RefreshWorkspacesSafeAsync());
    }

    private async Task RefreshWorkspacesSafeAsync()
    {
        try
        {
            await RefreshWorkspacesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private async Task FollowSelectedSessionAsync(SessionItemViewModel? session)
    {
        var cancellation = BeginFollow();
        int epoch;
        lock (_followGate) epoch = _followEpoch;

        if (session is null)
        {
            ConversationItems.Clear();
            ResetStreamingMessage();
            _timelineEntries = [];
            _assembly        = CreateAssembly();
            ResetHistoryWindow();
            return;
        }

        try
        {
            await foreach (var update in _sessionService.FollowSessionAsync(session.Id, cancellation.Token))
            {
                lock (_followGate)
                {
                    if (cancellation.IsCancellationRequested || epoch != _followEpoch
                                                             || !ReferenceEquals(SelectedSession, session))
                        return;

                    ApplySessionUpdate(update);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally
        {
            CompleteFollow(cancellation);
        }
    }

    private void ApplySessionUpdate(SessionUpdate update)
    {
        switch (update)
        {
            case SessionUpdate.Snapshot snapshot :
                ResetStreamingMessage();
                ConversationItems.Clear();
                _timelineEntries = [.. snapshot.Entries];
                // 真实 Host 会在快照尾部为开放中的轮合成 interrupted 边界（seq 即 cursor，
                // 持久日志中不存在）：丢弃它，让该轮保持开放，由后续真实 turn/end 收束；
                // 否则中途 attach/重连会把生成中的轮提前折旧，且真实边界到达后会二次折叠。
                if (_timelineEntries.Count > 0
                 && _timelineEntries[^1] is TurnBoundary { Reason: "interrupted" })
                    _timelineEntries.RemoveAt(_timelineEntries.Count - 1);

                _assembly = CreateAssembly();
                foreach (var entry in _timelineEntries) _assembly.Add(entry);

                _historyThroughSeq = snapshot.Cursor;
                _windowStartSeq    = snapshot.WindowStartSeq;
                HasMoreHistory     = snapshot.HasMore;
                SelectedSession?.AdoptTitle(snapshot.Title);
                CurrentModel = snapshot.CurrentModel;
                break;

            case SessionUpdate.MessageAppended appended :
                if (appended.Message.Role == MessageRole.Assistant) RemoveStreamingMessage();

                _timelineEntries.Add(appended.Message);
                _assembly.Add(appended.Message);
                break;

            case SessionUpdate.TurnEnded ended :
                var boundary = new TurnBoundary(ended.Seq, ended.Turn, DateTimeOffset.Now, ended.Reason);
                _timelineEntries.Add(boundary);
                _assembly.Add(boundary);
                break;

            case SessionUpdate.ToolCallStarted started :
                // 流式气泡之后、提交消息之前的工具调用：直接按事件顺序追加。
                _timelineEntries.Add(started.Activity);
                _assembly.Add(started.Activity);
                break;

            case SessionUpdate.ToolCallSettled settled :
                ApplyToolSettled(settled.Activity);
                break;

            case SessionUpdate.TitleChanged title :
                SelectedSession?.AdoptTitle(title.Title);
                break;

            case SessionUpdate.ModelSelected selected :
                CurrentModel = selected.Selection;
                break;

            // 统计整值更新带投影 seq：乱序到达的旧值（重连竞态）直接忽略。
            case SessionUpdate.UsageUpdated usage when usage.Seq >= _usageSeq :
                _usageSeq = usage.Seq;
                Usage     = usage.Usage;
                break;

            case SessionUpdate.StatsUpdated statsUpdate when statsUpdate.Seq >= _statsSeq :
                _statsSeq = statsUpdate.Seq;
                Stats     = statsUpdate.Stats;
                break;

            case SessionUpdate.StreamStarted started :
                _streamingAttemptId = started.AttemptId;
                if (_streamingMessage is null)
                {
                    _streamingMessage = MessageItemViewModel.CreateStreaming();
                    ConversationItems.Add(_streamingMessage);
                }

                break;

            case SessionUpdate.StreamTextDelta delta when delta.AttemptId == _streamingAttemptId :
                _streamingMessage?.AppendText(delta.Text);
                break;

            case SessionUpdate.StreamEnded ended :
                // 用户取消通常以 committed 结算：interrupted 助手消息事件会到达并替换气泡。
                // abandoned 是无法落盘的错误路径，不会有正式消息，需就地标注避免气泡悬挂。
                if (ended.Outcome == StreamOutcomeKind.Abandoned)
                    _streamingMessage?.MarkInterrupted();
                else
                    _streamingMessage?.StopStreaming();

                break;
        }
    }

    private void ApplyToolSettled(ToolActivity settled)
    {
        if (_assembly.SettleTool(settled)) return;

        // 窗口起点落在调用中间（或恢复期repair合成）：没有发起事件也展示结果卡片。
        _timelineEntries.Add(settled);
        _assembly.Add(settled);
    }

    /// <summary>
    ///     翻页：页内条目前插进全量条目后整体重建时间线。参考客户端在历史未读全时
    ///     不折叠过程组（historyIncomplete），因此折叠状态随 HasMoreHistory 变化，
    ///     逐页拼接无法维护，统一以全量条目重建。
    /// </summary>
    private async Task LoadOlderAsync()
    {
        if (SelectedSession is null || IsLoadingOlder || !HasMoreHistory) return;

        IsLoadingOlder = true;
        ErrorText      = string.Empty;
        try
        {
            var followUps = 0;
            while (HasMoreHistory && followUps <= EmptyPageFollowUpLimit)
            {
                var page = await _sessionService.LoadOlderAsync(SelectedSession.Id, _historyThroughSeq,
                                                                _windowStartSeq);
                _windowStartSeq = page.WindowStartSeq;
                _timelineEntries.InsertRange(0, page.Entries);
                HasMoreHistory = page.HasMore;
                RebuildTimeline();

                if (page.Entries.Count > 0) break;

                // 整页都是被过滤的注入上下文：继续翻下一页直到出现可见条目。
                followUps++;
            }
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsLoadingOlder = false;
        }
    }

    /// <summary>从全量条目重建时间线；折叠资格逐轮判定（窗口内完整覆盖的轮次折叠）。</summary>
    private void RebuildTimeline()
    {
        ConversationItems.Clear();
        _assembly = CreateAssembly();
        foreach (var entry in _timelineEntries) _assembly.Add(entry);

        // 重建会丢掉流式气泡；生成中重新挂回尾部，等待正式消息事件替换。
        if (_streamingMessage is not null) ConversationItems.Add(_streamingMessage);
    }

    private TimelineAssembly CreateAssembly()
    {
        return new TimelineAssembly(ConversationItems);
    }

    private bool CanLoadOlder()
    {
        return SelectedSession is not null && HasMoreHistory && !IsLoadingOlder && IsBackendConnected;
    }

    private void ResetHistoryWindow()
    {
        _historyThroughSeq = 0;
        _windowStartSeq    = 1;
        HasMoreHistory     = false;
    }

    private async Task CreateNewSessionAsync()
    {
        try
        {
            var summary = await _sessionService.CreateSessionAsync();
            var session = new SessionItemViewModel(summary);
            Sessions.Insert(0, session);
            RebuildSessionRows();
            SelectedSession = session;
            ErrorText       = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private CancellationTokenSource BeginFollow()
    {
        var cancellation = new CancellationTokenSource();
        var previous     = Interlocked.Exchange(ref _followCancellation, cancellation);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        // 代际门闩：新订阅开代。旧循环的「校验 + 应用」在同一把锁内原子进行，
        // 代际不符即退出——否则旧会话的迟到更新（如 stats 整值）会在新会话基线
        // 之后落盘，把新会话的统计串台成旧值且不再被修正。
        lock (_followGate) _followEpoch++;

        return cancellation;
    }

    private void CompleteFollow(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref _followCancellation, null, cancellation),
                            cancellation))
            cancellation.Dispose();
    }

    private void RemoveStreamingMessage()
    {
        if (_streamingMessage is not null) ConversationItems.Remove(_streamingMessage);

        ResetStreamingMessage();
    }

    private void ResetStreamingMessage()
    {
        _streamingMessage   = null;
        _streamingAttemptId = null;
    }

    private void OnSelectedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionItemViewModel.Running))
            _postToUi(() =>
            {
                OnPropertyChanged(nameof(IsSessionRunning));
                Composer.SetSessionRunning(SelectedSession?.Running ?? false);
            });
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        // 后台线程事件：合并 400ms 内的重复通知，再回到界面线程刷新列表。
        if (Interlocked.Exchange(ref _listRefreshPending, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400);
                Interlocked.Exchange(ref _listRefreshPending, 0);
                _postToUi(() => _ = RefreshSessionsSafeAsync());
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref _listRefreshPending, 0);
            }
        });
    }

    private async Task RefreshSessionsSafeAsync()
    {
        try
        {
            await RefreshSessionsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private void OnBackendStatusChanged(object? sender, EventArgs e)
    {
        _postToUi(() =>
        {
            OnPropertyChanged(nameof(BackendStatusText));
            OnPropertyChanged(nameof(BackendErrorDetail));
            OnPropertyChanged(nameof(HasBackendError));
            OnPropertyChanged(nameof(IsBackendConnected));
            OnPropertyChanged(nameof(IsBackendDisconnected));
            OnPropertyChanged(nameof(IsModelPickerEnabled));
            Composer.SetBackendConnected(IsBackendConnected);
            LoadOlderCommand.RaiseCanExecuteChanged();
            if (IsBackendConnected)
                // 重连后代目录可能变化，重新拉取（只读，可安全重试）。
                _ = RefreshModelCatalogSafeAsync();
        });
    }

    /// <summary>
    ///     把会话条目组装为时间线项目，折叠规则对齐参考 Web 客户端的 turn-process 投影：
    ///     轮内条目（中间助手消息、工具调用）先逐项显示；turn/end 到达后按「最后一轮步的
    ///     有正文且不含工具调用的助手消息为最终回复」结算，最终回复存在时其之前的过程条目
    ///     折叠为一个 <see cref="TurnProcessGroupViewModel" />。快照、增量与翻页共用同一套规则。
    ///     逐轮判定折叠资格：只有本轮起点（用户消息或上一轮边界）落在已加载窗口内时才折叠，
    ///     被窗口截断的首轮保持逐项展示——否则中途 attach 长会话时，全程要手动翻到顶才能
    ///     看到折叠形态（WebUI 实时会话的已加载窗口天然是全量，不存在此落差）。
    /// </summary>
    private sealed class TimelineAssembly(ObservableCollection<ConversationItemViewModel> target)
    {
        private readonly ObservableCollection<ConversationItemViewModel> _target = target;

        private readonly Dictionary<string, (ToolActivityItemViewModel Card, TurnProcessGroupViewModel? Group)>
            _toolsByCallId = new();

        /// <summary>当前轮的条目顺序；用于结算最终回复（最后一个无工具调用的有正文消息）。</summary>
        private readonly List<ConversationEntry> _turnEntries = [];

        private readonly List<ConversationItemViewModel> _turnItems = [];

        private long? _turn;

        /// <summary>上一条目是否是轮次起点（用户消息或 turn/end 边界）；窗口首条目按截断处理（假）。</summary>
        private bool _turnOpeningSeen;

        /// <summary>本轮起点是否在已加载窗口内；在本轮首个条目到达时快照 <see cref="_turnOpeningSeen" />。</summary>
        private bool _turnStartObserved;

        public void Add(ConversationEntry entry)
        {
            switch (entry)
            {
                case TurnBoundary boundary :
                    if (_turn is { } openTurn && openTurn == boundary.Turn)
                        CloseTurn(_turnStartObserved);
                    else if (_turn is not null)
                        // 轮次号不衔接（窗口裁剪等）：当前轮保守收尾，不折叠。
                        CloseTurn(false);

                    // 边界收束上一轮，其后是新一轮的起点。
                    _turnOpeningSeen = true;
                    return;

                case ConversationMessage { Role: MessageRole.User } message :
                    // 用户消息开新一轮：上一轮未见 turn/end 时保守收尾，不折叠。
                    // 用户气泡不属于任何轮的过程条目，不进入轮内跟踪。
                    CloseTurn(false);
                    _turnOpeningSeen = true;
                    _target.Add(new MessageItemViewModel(message));
                    return;

                case ConversationMessage message when string.IsNullOrWhiteSpace(message.Content) &&
                                                      !string.IsNullOrWhiteSpace(message.Reasoning) :
                    // 只有思考（reasoning）没有正文的消息：不产生气泡，像工具调用一样
                    // 作为轮内过程条目跟踪（折叠时收入过程组，展开时显示思考行）。
                    OpenTurn(message.Turn);
                    var reasoningItem = new MessageItemViewModel(message);
                    _turnEntries.Add(message);
                    Append(reasoningItem);
                    _turnOpeningSeen = false;
                    return;

                case ConversationMessage message when string.IsNullOrWhiteSpace(message.Content) :
                    // 无正文也无思考的助手提交（内容只有工具调用块）：无可见内容，不产生条目；
                    // 其工具调用由 tool/call 事件单独承载。
                    return;

                case ConversationMessage message :
                    OpenTurn(message.Turn);
                    var messageItem = new MessageItemViewModel(message);
                    _turnEntries.Add(message);
                    Append(messageItem);
                    _turnOpeningSeen = false;
                    return;

                case ToolActivity tool :
                    OpenTurn(tool.Turn);
                    var card = new ToolActivityItemViewModel(tool);
                    _toolsByCallId[tool.CallId] = (card, null);
                    _turnEntries.Add(tool);
                    Append(card);
                    _turnOpeningSeen = false;
                    return;

                default :
                    throw new NotSupportedException($"未支持的会话条目类型：{entry.GetType().Name}");
            }
        }

        /// <summary>
        ///     落定一个工具调用（按 CallId 匹配，无论其已折叠进组还是仍逐项显示）。
        /// </summary>
        public bool SettleTool(ToolActivity settled)
        {
            if (!_toolsByCallId.TryGetValue(settled.CallId, out var found)) return false;

            found.Card.Settle(settled);
            found.Group?.RefreshSummary();
            return true;
        }

        private void OpenTurn(long? turn)
        {
            if (_turnItems.Count == 0)
            {
                _turn              = turn;
                _turnStartObserved = _turnOpeningSeen;
            }
        }

        /// <summary>
        ///     结算当前轮：最终回复是轮内最后一条助手消息且它有正文（思考不算正文）、
        ///     不含工具调用块；存在且轮起点在窗口内时，其余过程条目收入过程组，最终回复保持
        ///     独立气泡。被窗口截断的轮次（起点未观察到）与被打断无正文的轮次保持逐项展示。
        /// </summary>
        private void CloseTurn(bool foldAllowed)
        {
            try
            {
                if (_turnItems.Count == 0) return;

                var answer = FindAnswer();
                var members = answer is null
                    ? _turnItems.ToList()
                    : _turnItems.Where(item => !ReferenceEquals(item, answer)).ToList();
                if (!foldAllowed || answer is null || members.Count == 0)
                    // 不折叠：条目保持逐项显示。
                    return;

                var group = new TurnProcessGroupViewModel(members[0].Seq);
                foreach (var item in members)
                {
                    if (item is ToolActivityItemViewModel card) _toolsByCallId[card.CallId] = (card, group);

                    group.Add(item);
                }

                // 先移除整轮条目再按「过程组、最终回复」的顺序放回。
                foreach (var item in _turnItems) _target.Remove(item);

                _target.Add(group);
                if (answer is not null) _target.Add(answer);
            }
            finally
            {
                _turnItems.Clear();
                _turnEntries.Clear();
                _turn = null;
            }
        }

        /// <summary>最终回复候选：轮内最后一条助手消息，须有正文且不含工具调用块。</summary>
        private MessageItemViewModel? FindAnswer()
        {
            for (var index = _turnEntries.Count - 1; index >= 0; index--)
            {
                if (_turnEntries[index] is not ConversationMessage message) continue;

                // 只看最后一条助手消息：它无正文（含只有思考）或含工具调用块时，
                // 该轮没有最终回复（对齐参考实现 latestAnswer 只取最后一个 step）。
                return !string.IsNullOrWhiteSpace(message.Content)
                    && !message.HasToolCalls
                    && _turnItems[index] is MessageItemViewModel item
                    ? item
                    : null;
            }

            return null;
        }

        private void Append(ConversationItemViewModel item)
        {
            _target.Add(item);
            _turnItems.Add(item);
        }
    }

    private sealed class EmptyToolApprovalService : IToolApprovalService
    {
        public static EmptyToolApprovalService Instance { get; } = new();

        public event EventHandler? ApprovalsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<PendingApproval> Pending => [];

        public Task RespondAsync(string eventId, bool allowed, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
