using DshDesktop.Core.Models;
using DshDesktop.Core.Services;
using DshDesktop.Infrastructure.Services;
using DshDesktop.ViewModels;
using Xunit;

namespace DshDesktop.Tests;

/// <summary>Composer 纯行为测试：上下文由 SetSession/SetBackendConnected 显式推送，不依赖窗口组装。</summary>
public sealed class ComposerViewModelTests
{
    [Fact]
    public void SendRequiresSessionDraftAndConnectedBackend()
    {
        var composer = CreateComposer();

        // 三个前提逐项到位：会话、草稿、后端连接。
        composer.SetBackendConnected(true);
        composer.SetSession("session-1", isRunning : false);
        Assert.False(composer.SendMessageCommand.CanExecute(null));

        composer.DraftMessage = "你好";
        Assert.True(composer.SendMessageCommand.CanExecute(null));

        // 草稿清空后回到不可发送。
        composer.DraftMessage = "   ";
        Assert.False(composer.SendMessageCommand.CanExecute(null));
    }

    [Fact]
    public void SendIsUnavailableWithoutSession()
    {
        var composer = CreateComposer();

        composer.SetBackendConnected(true);
        composer.DraftMessage = "未选中会话时的草稿";

        Assert.False(composer.SendMessageCommand.CanExecute(null));
    }

    [Fact]
    public void SendIsUnavailableWhenBackendDisconnected()
    {
        var composer = CreateComposer();

        composer.SetSession("session-1", isRunning : false);
        composer.SetBackendConnected(false);
        composer.DraftMessage = "后端未连接时的草稿";

        Assert.False(composer.SendMessageCommand.CanExecute(null));

        // 恢复连接后即可发送。
        composer.SetBackendConnected(true);
        Assert.True(composer.SendMessageCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelIsAvailableWhileSessionRunningAndCallsService()
    {
        var sessionService = new ControllableSessionService();
        var composer       = new ComposerViewModel(sessionService, _ => { });

        Assert.False(composer.CancelCommand.CanExecute(null));

        composer.SetSession("session-1", isRunning : true);
        Assert.True(composer.CancelCommand.CanExecute(null));

        composer.CancelCommand.Execute(null);
        await WaitUntilAsync(() => sessionService.CancelledSessions.Count == 1);
        Assert.Equal("session-1", Assert.Single(sessionService.CancelledSessions));

        // 运行结束回到不可取消。
        composer.SetSessionRunning(false);
        Assert.False(composer.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendCompletionDoesNotClearDraftEnteredWhileRequestIsInFlight()
    {
        var sessionService = new ControllableSessionService();
        var composer       = new ComposerViewModel(sessionService, _ => { });
        composer.SetSession("session-1", isRunning : false);
        composer.SetBackendConnected(true);

        composer.DraftMessage = "第一条消息";
        composer.SendMessageCommand.Execute(null);
        await sessionService.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // 请求未完成期间用户继续输入：旧请求完成时不得清掉新草稿。
        composer.DraftMessage = "发送期间的新草稿";
        sessionService.ReleaseSend.TrySetResult();
        await WaitUntilAsync(() => !composer.IsSending);

        Assert.Equal("发送期间的新草稿", composer.DraftMessage);
    }

    [Fact]
    public async Task FailedSendReportsErrorThroughCallback()
    {
        var     sessionService = new ControllableSessionService { FailSend = true };
        string? reported       = null;
        var     composer       = new ComposerViewModel(sessionService, text => reported = text);
        composer.SetSession("session-1", isRunning : false);
        composer.SetBackendConnected(true);

        composer.DraftMessage = "会失败的发送";
        composer.SendMessageCommand.Execute(null);

        await WaitUntilAsync(() => reported is not null);
        Assert.Contains("发送失败", reported, StringComparison.Ordinal);
        Assert.False(composer.IsSending);
    }

    private static ComposerViewModel CreateComposer()
    {
        return new ComposerViewModel(new ControllableSessionService(), _ => { });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var timeout = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < timeout)
        {
            if (condition()) return;

            await Task.Delay(10);
        }

        Assert.True(condition(), "预期的异步 Composer 状态未在超时前出现。");
    }

    /// <summary>可控发送/取消桩：其余行为走模拟实现，发送可阻塞到手动放行，取消记录目标会话。</summary>
    private sealed class ControllableSessionService : ISessionService
    {
        private readonly SimulatedSessionService _inner = new();

        public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> CancelledSessions { get; } = [];

        public bool FailSend { get; set; }

        public event EventHandler? SessionsChanged
        {
            add => _inner.SessionsChanged += value;
            remove => _inner.SessionsChanged -= value;
        }

        public Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(CancellationToken cancellationToken = default)
        {
            return _inner.GetSessionsAsync(cancellationToken);
        }

        public Task<SessionSummary> CreateSessionAsync(CancellationToken cancellationToken = default)
        {
            return _inner.CreateSessionAsync(cancellationToken);
        }

        public Task<ModelCatalog> GetModelCatalogAsync(CancellationToken cancellationToken = default)
        {
            return _inner.GetModelCatalogAsync(cancellationToken);
        }

        public Task<ModelSelection> SelectModelAsync(
            string sessionId, string provider, string model, CancellationToken cancellationToken = default)
        {
            return _inner.SelectModelAsync(sessionId, provider, model, cancellationToken);
        }

        public Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
            string sessionId, CancellationToken cancellationToken = default)
        {
            return _inner.GetMessagesAsync(sessionId, cancellationToken);
        }

        public Task<SessionHistoryPage> LoadOlderAsync(
            string sessionId, long throughSeq, long beforeSeq, CancellationToken cancellationToken = default)
        {
            return _inner.LoadOlderAsync(sessionId, throughSeq, beforeSeq, cancellationToken);
        }

        public async Task SendPromptAsync(
            string sessionId, string requestId, string content, CancellationToken cancellationToken = default)
        {
            SendStarted.TrySetResult();
            if (FailSend) throw new InvalidOperationException("发送失败（模拟）");

            await ReleaseSend.Task.WaitAsync(cancellationToken);
            await _inner.SendPromptAsync(sessionId, requestId, content, cancellationToken);
        }

        public Task CancelAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            CancelledSessions.Add(sessionId);
            return _inner.CancelAsync(sessionId, cancellationToken);
        }

        public IAsyncEnumerable<SessionUpdate> FollowSessionAsync(
            string sessionId, CancellationToken cancellationToken = default)
        {
            return _inner.FollowSessionAsync(sessionId, cancellationToken);
        }
    }
}
