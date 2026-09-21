using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DeepseekHarnessDesktop.Core.Services;
using DeepseekHarnessDesktop.Harness.Services.Approvals;
using DeepseekHarnessDesktop.Harness.Services.Connection;
using DeepseekHarnessDesktop.Harness.Services.Sessions;
using DeepseekHarnessDesktop.Harness.Services.Workspaces;
using DeepseekHarnessDesktop.Infrastructure.Services;
using DeepseekHarnessDesktop.Infrastructure.Services.Backend;
using DeepseekHarnessDesktop.Services.Backend;
using DeepseekHarnessDesktop.ViewModels;

namespace DeepseekHarnessDesktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var                  configuration = DesktopBackendConfiguration.FromEnvironment();
            ISessionService      sessionService;
            IWorkspaceService    workspaceService;
            IBackendHostService  backendService;
            IToolApprovalService toolApprovalService;
            HarnessConnection?   connection      = null;
            var                  isSimulatedMode = true;

            if (configuration is { UseRealBackend: true, Options: { } options })
            {
                var hostService = new NodeBackendHostService(options);
                connection          = new HarnessConnection(hostService.StartAsync);
                sessionService      = new HarnessSessionService(connection, configuration.PreferredModel);
                workspaceService    = new HarnessWorkspaceService(connection);
                backendService      = hostService;
                toolApprovalService = new HarnessToolApprovalService(connection);
                isSimulatedMode     = false;
            }
            else
            {
                sessionService      = new SimulatedSessionService();
                workspaceService    = new SimulatedWorkspaceService();
                backendService      = new SimulatedBackendStatusService();
                toolApprovalService = new SimulatedToolApprovalService();
            }

            var viewModel = new MainWindowViewModel(sessionService, backendService, workspaceService,
                                                    toolApprovalService,
                                                    isSimulatedMode,
                                                    action => Dispatcher.UIThread.Post(action));
            var mainWindow = new MainWindow(viewModel);
            if (configuration.ConfigurationError is { Length: > 0 } error)
            {
                viewModel.ShowStartupNotice(error);
            }

            desktop.MainWindow = mainWindow;
            var capturedConnection = connection;
            var capturedBackend    = backendService;
            var capturedWorkspaces = workspaceService;
            mainWindow.Closed += async (_, _) =>
            {
                await viewModel.DisposeAsync();
                if (capturedWorkspaces is IAsyncDisposable disposableWorkspaces)
                {
                    await disposableWorkspaces.DisposeAsync();
                }

                if (capturedConnection is not null)
                {
                    await capturedConnection.DisposeAsync();
                }

                await capturedBackend.DisposeAsync();
            };
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
