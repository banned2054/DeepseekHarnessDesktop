using Avalonia;
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

    // 悬浮输入面板占位常量：侧/顶留白与面板底边距须与 MainWindow.axaml 保持一致。
    private const double ComposerBottomMargin    = 18;
    private const double MessagesSidePadding     = 28;
    private const double MessagesTopPadding      = 22;
    private const double ComposerReserveExtraGap = 10;

    private double _messagesExtent;
    private bool   _anchoringPrepend;

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
        viewModel.ConversationItems.CollectionChanged += KeepScrolledToBottom;
        viewModel.PropertyChanged                     += OnViewModelPropertyChanged;
        MessagesScroll.ScrollChanged                  += OnMessagesScrollChanged;
        ComposerPanel.SizeChanged                     += OnComposerPanelSizeChanged;
    }

    /// <summary>
    /// 悬浮面板随错误提示与多行输入增高时，同步滚动内容的底部留白：
    /// 面板叠放在滚动区之上，只有把留白计入被滚动内容，滚到底时最后一条消息才不会被面板遮住。
    /// </summary>
    private void OnComposerPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var reservedBottom = e.NewSize.Height + ComposerBottomMargin + ComposerReserveExtraGap;
        MessagesContent.Padding = new Thickness(MessagesSidePadding, MessagesTopPadding,
                                                MessagesSidePadding, reservedBottom);
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
}
