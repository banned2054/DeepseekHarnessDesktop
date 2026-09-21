using DshDesktop.Core.Models;
using DshDesktop.Harness.Exceptions;
using DshDesktop.Harness.Models.Events;
using DshDesktop.Harness.Services.Connection;
using DshDesktop.Harness.Services.Sessions;
using DshDesktop.Harness.Services.Workspaces;
using DshDesktop.Infrastructure.Services.Backend;
using Xunit;
using Xunit.Abstractions;

namespace DshDesktop.Tests;

/// <summary>
///     对真实 Harness Host 的端到端验证（隔离 DSH_HOME，无模型凭据）：
///     独立 Node 启动、令牌认证、会话列表、创建会话、发送消息、订阅快照与增量、取消调用与优雅关闭。
///     仅在设置 DSH_E2E_RUNTIME_DIR（prepare-dev-runtime.mjs 生成的目录）时运行，缺失时明确跳过。
///     隔离 home 没有模型凭据，不存在真实生成：这里的取消只验证不破坏连接，
///     生成中取消的真实中断由 RealModelConversationTests（显式启用 + 凭据校验）覆盖。
/// </summary>
public sealed class RealBackendE2ETests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task HostBootsListCreatePromptFollowAndCancelAllWork()
    {
        var runtimeDir     = Environment.GetEnvironmentVariable(RealBackendTestSupport.RuntimeDirVariable);
        var node           = RealBackendTestSupport.FindNodeExecutable();
        var launcherScript = RealBackendTestSupport.FindLauncherScript();
        Skip.If(string.IsNullOrWhiteSpace(runtimeDir),
                $"未设置 {RealBackendTestSupport.RuntimeDirVariable}，跳过真实后端 E2E 验证。");
        Skip.If(node is null, "PATH 中找不到 Node 可执行文件，跳过。");
        Skip.If(launcherScript is null, "找不到 launcher 脚本，跳过。");

        var root = Path.Combine(Path.GetTempPath(), $"dsh-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var options = RealBackendTestSupport.BuildOptions(node!, launcherScript!, runtimeDir!,
                                                          Path.Combine(root, "dsh-home"), root);

        var       hostService        = new NodeBackendHostService(options);
        var       connection         = new HarnessConnection(hostService.StartAsync);
        var       sessions           = new HarnessSessionService(connection);
        using var followCancellation = new CancellationTokenSource();
        var       collector          = new RealBackendTestSupport.UpdateCollector(output);
        Task?     followTask         = null;
        try
        {
            var info = await hostService.StartAsync().WaitAsync(TimeSpan.FromSeconds(150));
            Assert.StartsWith("http://127.0.0.1:", info.AuthenticatedUri.ToString());

            var list = await sessions.GetSessionsAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(list);

            var created = await sessions.CreateSessionAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.StartsWith("session-", created.Id);

            followTask = collector.StartAsync(sessions, created.Id, followCancellation);

            await RealBackendTestSupport.WaitForAsync(() =>
                                                          collector
                                                             .FirstOrDefault<SessionUpdate.Snapshot>() is not null,
                                                      TimeSpan.FromSeconds(30), "follow 快照未到达");

            await sessions.SendPromptAsync(created.Id, Guid.NewGuid().ToString(), "端到端验证消息")
                          .WaitAsync(TimeSpan.FromSeconds(30));

            await RealBackendTestSupport.WaitForAsync(() => collector.Snapshot()
                                                                     .Any(update =>
                                                                              update is SessionUpdate.MessageAppended
                                                                              {
                                                                                  Message.Role: MessageRole.User
                                                                              }), TimeSpan.FromSeconds(30),
                                                      "用户消息未出现在会话流中");

            var stored = await sessions.GetMessagesAsync(created.Id).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Contains(stored, message => message.Content.Contains("端到端验证消息"));

            // session/page 真实往返：新会话没有更早历史，验证请求形状与响应解析（空页 + hasMore=false）。
            var openingSnapshot = collector.FirstOrDefault<SessionUpdate.Snapshot>();
            Assert.NotNull(openingSnapshot);
            var olderPage = await sessions
                                 .LoadOlderAsync(created.Id, openingSnapshot.Cursor, openingSnapshot.WindowStartSeq)
                                 .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(olderPage.HasMore);
            Assert.True(olderPage.WindowStartSeq >= 0);

            // 快照统计投影：tokenUsage/sessionStats 随快照可解析（新会话全零也算到达）。
            await RealBackendTestSupport.WaitForAsync(() => collector.Snapshot()
                                                                     .Any(update => update is SessionUpdate
                                                                             .UsageUpdated), TimeSpan.FromSeconds(30),
                                                      "快照 usage 投影未到达");
            var openingUsage = collector.Snapshot().OfType<SessionUpdate.UsageUpdated>().First();
            output.WriteLine($"快照 usage 投影：未命中 {openingUsage.Usage.UncachedInputTokens}"
                           + $" · 缓存读 {openingUsage.Usage.CacheReadTokens}"
                           + $" · 输出 {openingUsage.Usage.OutputTokens}");

            // session/control 真实往返：Host 级状态流 Baseline 到达且可解析（jobs 帧被忽略）。
            using (var controlCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(followCancellation.Token))
            {
                var controlBaselineTask = Task.Run(async () =>
                {
                    await foreach (var frame in connection.FollowSessionControlAsync(controlCancellation.Token))
                        if (frame is SessionControlFrame.Baseline baseline)
                            return baseline;

                    return null;
                });
                var controlBaseline = await controlBaselineTask.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.NotNull(controlBaseline);
                output.WriteLine($"control 基线投影会话数：{controlBaseline!.Projections.Count}");
                controlCancellation.Cancel();
            }

            // workspace/follow 真实往返：隔离 home 无已登记工作区，基线允许为空，
            // 但必须到达且可解析（Baseline 无条件触发一次变更通知）。
            var workspaces      = new HarnessWorkspaceService(connection);
            var baselineArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            workspaces.WorkspacesChanged += (_, _) => baselineArrived.TrySetResult();
            // 首次调用启动订阅并立即返回（可能为空）；基线到达后事件通知。
            await workspaces.GetWorkspacesAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await baselineArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var baselineWorkspaces = await workspaces.GetWorkspacesAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(baselineWorkspaces);
            output.WriteLine($"workspace 基线条目数：{baselineWorkspaces.Count}");
            await workspaces.DisposeAsync();

            // 隔离 home 无凭据，不存在真实生成：取消只要求幂等且不破坏连接；
            // 会话不在运行态时后端可能拒绝取消，属协议允许的答复。
            try
            {
                await sessions.CancelAsync(created.Id).WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (HarnessRpcException)
            {
                // 不视为失败；真实中断验证见 RealModelConversationTests。
            }

            // 取消后连接与只读调用仍可用。
            var listAfterCancel = await sessions.GetSessionsAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Contains(listAfterCancel, summary => summary.Id == created.Id);

            Assert.False(hostService.LastError is { Length: > 0 }, $"后端意外出错：{hostService.LastError}");
            collector.AssertNoSubscriptionFault();
            Assert.False(followTask.IsFaulted, "订阅任务异常退出。");
        }
        finally
        {
            followCancellation.Cancel();
            if (followTask is not null)
                try
                {
                    await followTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    // 订阅未在宽限期内退出；连接释放会强制结束它。
                }

            await connection.DisposeAsync();
            await hostService.DisposeAsync();
            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Host 建立的链接目录可能短暂占用；留给系统临时目录清理。
            }
        }
    }
}
