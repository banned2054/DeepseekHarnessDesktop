using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DshDesktop.Infrastructure.Services;
using DshDesktop.ViewModels;

namespace DshDesktop;

public partial class MainWindow : Window
{
    private const double AutoScrollBottomTolerance = 140;

    // 侧栏拖拽/折叠常量：宽度上限 = min(480, 可用宽 - 右栏最小 560)；
    // splitter 布局列宽 0，命中区悬跨分界线、不占布局宽度。
    private const double SidebarMinWidth     = 240;
    private const double SidebarMaxWidth     = 480;
    private const double SidebarDefaultWidth = 280;
    private const double ContentMinWidth     = 560;

    private double _messagesExtent;
    private bool   _anchoringPrepend;
    private bool   _isSidebarCollapsed;
    private double _lastSidebarWidth = SidebarDefaultWidth;

    // Avalonia 不为 ColumnDefinition 的 x:Name 生成字段，按位置取列。
    private ColumnDefinition SidebarColumn => RootGrid.ColumnDefinitions[0];

    public MainWindow() : this(new MainWindowViewModel(new SimulatedSessionService(),
                                                       new SimulatedBackendStatusService(),
                                                       new SimulatedWorkspaceService(),
                                                       new SimulatedToolApprovalService(),
                                                       isSimulatedMode : true))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        MessageInput.AddHandler(KeyDownEvent, OnMessageInputKeyDown, RoutingStrategies.Tunnel);
        MessageInput.GotFocus                         += OnMessageInputGotFocus;
        MessageInput.LostFocus                        += OnMessageInputLostFocus;
        viewModel.ConversationItems.CollectionChanged += KeepScrolledToBottom;
        viewModel.PropertyChanged                     += OnViewModelPropertyChanged;
        MessagesScroll.ScrollChanged                  += OnMessagesScrollChanged;
        RootGrid.SizeChanged                          += OnRootGridSizeChanged;
    }

    private void OnMessageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.SendMessageCommand.Execute(null);
        }
    }

    /// <summary>
    /// 输入框焦点变化时切换面板 focused 类：聚焦反馈由整块面板的克制描边承担，
    /// 输入框自身保持透明无边框（对应 WebUI composer 的卡片级聚焦高亮）。
    /// </summary>
    private void OnMessageInputGotFocus(object? sender, FocusChangedEventArgs e) => SetComposerFocused(true);

    private void OnMessageInputLostFocus(object? sender, RoutedEventArgs e) => SetComposerFocused(false);

    private void SetComposerFocused(bool focused) => ComposerSurface.Classes.Set("focused", focused);

    /// <summary>加载完成后解除前插锚定；延后到布局落地，覆盖插入后一次 extent 增高。</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsLoadingOlder) &&
            DataContext is MainWindowViewModel { IsLoadingOlder: false })
        {
            Dispatcher.UIThread.Post(() => _anchoringPrepend = false, DispatcherPriority.Background);
        }
    }

    /// <summary>新消息到达时，若用户本就停在底部附近则继续贴底；用户上翻时不打扰。</summary>
    private void KeepScrolledToBottom(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var scroll = MessagesScroll;
        if (scroll is not null && WasNearBottom(scroll, scroll.Extent.Height))
        {
            PostScrollToEnd(scroll);
        }
    }

    /// <summary>
    /// 内容增高（流式追加、新气泡完成布局）时维持贴底。CollectionChanged 只覆盖增删条目：
    /// 气泡内的文本增高不触发它，只体现在 extent 变化上；贴底判断用增高前的 extent，
    /// 避免长气泡一次布局后越界超出容差被误判为用户上翻。
    /// 「加载更早」前插历史时改走锚定补偿：保持视觉位置不动，且不触发贴底。
    /// </summary>
    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll)
        {
            return;
        }

        if (_anchoringPrepend && e.ExtentDelta.Y > 0)
        {
            // 顶部插入内容把既有内容向下推；同步抬高偏移，用户看到的位置保持不变。
            scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + e.ExtentDelta.Y);
            return;
        }

        var previousExtent = _messagesExtent;
        _messagesExtent = scroll.Extent.Height;
        if (previousExtent > 0
         && !e.ExtentDelta.Y.Equals(0)
         && WasNearBottom(scroll, previousExtent))
        {
            PostScrollToEnd(scroll);
        }
    }

    private static bool WasNearBottom(ScrollViewer scroll, double extent)
    {
        return extent - (scroll.Offset.Y + scroll.Viewport.Height) <= AutoScrollBottomTolerance;
    }

    private static void PostScrollToEnd(ScrollViewer scroll)
    {
        Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>窗口尺寸变化后重新 clamp 左栏宽度；折叠状态下保持整列隐藏，不介入。</summary>
    private void OnRootGridSizeChanged(object? sender, SizeChangedEventArgs e) => ClampSidebarWidth();

    private void OnSidebarToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_isSidebarCollapsed)
        {
            ExpandSidebar();
        }
        else
        {
            CollapseSidebar();
        }
    }

    /// <summary>折叠：左栏与 splitter 隐藏、左列连同 MinWidth 清零，右侧填满；
    /// 列的 MinWidth 不清零时 Width=0 会被钉在 240，露出窗口原始背景。</summary>
    private void CollapseSidebar()
    {
        _isSidebarCollapsed = true;
        if (SidebarColumn.Width.IsAbsolute)
        {
            _lastSidebarWidth = SidebarColumn.Width.Value;
        }

        SidebarColumn.MinWidth    = 0;
        SidebarColumn.Width       = new GridLength(0);
        Sidebar.IsVisible         = false;
        SidebarSplitter.IsVisible = false;
        UpdateSidebarToggleState();
    }

    /// <summary>展开：恢复 MinWidth 下限与上次宽度并按当前窗口 clamp；splitter 命中区随之恢复。</summary>
    private void ExpandSidebar()
    {
        _isSidebarCollapsed       = false;
        SidebarColumn.MinWidth    = SidebarMinWidth;
        SidebarColumn.Width       = new GridLength(_lastSidebarWidth, GridUnitType.Pixel);
        Sidebar.IsVisible         = true;
        SidebarSplitter.IsVisible = true;
        ClampSidebarWidth();
        UpdateSidebarToggleState();
    }

    /// <summary>
    /// 左栏宽度 clamp 到 [240, min(480, 可用宽-560)]；上限同步写入列与左栏内容的
    /// MaxWidth，让 GridSplitter 拖拽在右栏最小宽 560 之前停下。
    /// </summary>
    private void ClampSidebarWidth()
    {
        if (_isSidebarCollapsed)
        {
            return;
        }

        var available = RootGrid.Bounds.Width;
        var upper = Math.Max(SidebarMinWidth,
                             Math.Min(SidebarMaxWidth, available - ContentMinWidth));
        SidebarColumn.MaxWidth = upper;
        Sidebar.MaxWidth       = upper;
        var current = SidebarColumn.Width.IsAbsolute ? SidebarColumn.Width.Value : SidebarDefaultWidth;
        SidebarColumn.Width = new GridLength(Math.Clamp(current, SidebarMinWidth, upper), GridUnitType.Pixel);
        _lastSidebarWidth   = SidebarColumn.Width.Value;
    }

    /// <summary>开关按钮状态：图标、Tooltip 与可访问名称随折叠态互斥切换，不重复显示。</summary>
    private void UpdateSidebarToggleState()
    {
        var tip = _isSidebarCollapsed ? "展开侧栏" : "收起侧栏";
        ToolTip.SetTip(SidebarToggle, tip);
        AutomationProperties.SetName(SidebarToggle, tip);
        CollapseSidebarIcon.IsVisible = !_isSidebarCollapsed;
        ExpandSidebarIcon.IsVisible   = _isSidebarCollapsed;
    }
}
