using AnarlogTrigger.Actions;
using AnarlogTrigger.Services;
using AnarlogTrigger.State;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AnarlogTrigger.UI;

/// <summary>
/// Hidden WinUI host window that owns the system tray icon and Fluent menu.
/// </summary>
public sealed class TrayHostWindow : Window
{
    private readonly MeetingTriggerService _service;
    private readonly Config.SettingsStore _settingsStore;
    private readonly AnarlogHotkeySender _hotkeySender;
    private readonly ILogger _logger;
    private readonly string _logDirectory;
    private readonly TaskbarIcon _trayIcon;
    private readonly MenuFlyoutItem _statusItem;
    private readonly MenuFlyoutItem _toggleItem;
    private readonly ToggleMenuFlyoutItem _startupItem;
    private bool _disposed;

    public TrayHostWindow(
        MeetingTriggerService service,
        Config.SettingsStore settingsStore,
        AnarlogHotkeySender hotkeySender,
        ILogger logger,
        string logDirectory)
    {
        _service = service;
        _settingsStore = settingsStore;
        _hotkeySender = hotkeySender;
        _logger = logger;
        _logDirectory = logDirectory;

        Title = "AnarlogTrigger";
        Content = new Grid(); // required XamlRoot host for ContentDialogs

        ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        // Keep a 0-size window off-screen so dialogs still have a XamlRoot.
        AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1, 1));

        _statusItem = new MenuFlyoutItem { Text = "Status: starting…", IsEnabled = false };
        _toggleItem = new MenuFlyoutItem { Text = "Stop monitoring" };
        _toggleItem.Click += (_, _) => OnToggleMonitoring();

        _startupItem = new ToggleMenuFlyoutItem
        {
            Text = "Run at startup",
            IsChecked = Config.StartupRegistration.IsEnabled()
        };
        _startupItem.Click += async (_, _) => await OnToggleStartupAsync();

        var testHotkey = new MenuFlyoutItem { Text = "Test start hotkey (Ctrl+Shift+N)" };
        testHotkey.Click += async (_, _) => await OnTestHotkeyAsync();

        var addProcess = new MenuFlyoutItem { Text = "Add process…" };
        addProcess.Click += async (_, _) => await OnAddProcessAsync();

        var openConfig = new MenuFlyoutItem { Text = "Open config" };
        openConfig.Click += async (_, _) => await OnOpenConfigAsync();

        var reloadConfig = new MenuFlyoutItem { Text = "Reload config" };
        reloadConfig.Click += async (_, _) => await OnReloadConfigAsync();

        var openLog = new MenuFlyoutItem { Text = "Open log folder" };
        openLog.Click += async (_, _) => await OnOpenLogAsync();

        var exit = new MenuFlyoutItem { Text = "Exit" };
        exit.Click += (_, _) => OnExit();

        var menu = new MenuFlyout();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(testHotkey);
        menu.Items.Add(addProcess);
        menu.Items.Add(openConfig);
        menu.Items.Add(reloadConfig);
        menu.Items.Add(openLog);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "AnarlogTrigger",
            ContextFlyout = menu,
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.LeftOrRightClick,
            NoLeftClickDelay = true
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            _trayIcon.IconSource = new BitmapImage(new Uri(iconPath));
        }

        _trayIcon.ForceCreate();
        _trayIcon.DoubleClickCommand = new RelayCommand(() => _ = OnOpenConfigAsync());

        _service.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateStatusUi);
        _service.StartMonitoring();
        UpdateStatusUi();
        _logger.LogInformation("WinUI tray UI ready");

        Closed += (_, _) =>
        {
            if (!_disposed)
            {
                OnExit();
            }
        };
    }

    public void DisposeTray()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _trayIcon.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    private void UpdateStatusUi()
    {
        var monitoring = _service.IsMonitoring ? "monitoring" : "paused";
        var phase = _service.Phase switch
        {
            MeetingTriggerPhase.Idle => "idle",
            MeetingTriggerPhase.Debouncing => "debouncing start",
            MeetingTriggerPhase.RecordingStarted => "recording started",
            MeetingTriggerPhase.AwaitingStopDismiss => "awaiting stop dismiss",
            _ => _service.Phase.ToString()
        };

        _statusItem.Text = $"Status: {monitoring} · {phase}";
        _toggleItem.Text = _service.IsMonitoring ? "Stop monitoring" : "Start monitoring";
        _trayIcon.ToolTipText = $"AnarlogTrigger ({monitoring})";
    }

    private void OnToggleMonitoring()
    {
        if (_service.IsMonitoring)
        {
            _service.StopMonitoring();
        }
        else
        {
            _service.StartMonitoring();
        }

        UpdateStatusUi();
    }

    private async Task OnToggleStartupAsync()
    {
        var enable = _startupItem.IsChecked;
        try
        {
            Config.StartupRegistration.SetEnabled(enable);
            _logger.LogInformation("Run at startup {State}", enable ? "enabled" : "disabled");
            _trayIcon.ShowNotification(
                "AnarlogTrigger",
                enable ? "Will start with Windows" : "Removed from startup");
        }
        catch (Exception ex)
        {
            _startupItem.IsChecked = !enable;
            _logger.LogError(ex, "Failed to update startup registration");
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task OnTestHotkeyAsync()
    {
        try
        {
            _hotkeySender.SendStartListeningHotkey();
            _logger.LogInformation("Test Ctrl+Shift+N sent to anarlog.exe");
            _trayIcon.ShowNotification(
                "AnarlogTrigger",
                "Sent Ctrl+Shift+N to Anarlog — check whether it started listening.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test hotkey failed");
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task OnAddProcessAsync()
    {
        await RunWithHostWindowAsync(async () =>
        {
            var box = new TextBox
            {
                PlaceholderText = "e.g. firefox or MySoftphone",
                Width = 320
            };

            var dialog = new ContentDialog
            {
                Title = "Add process name",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Executable name (without or with .exe):" },
                        box
                    }
                },
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content!.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                _settingsStore.AddExtraProcess(box.Text);
                _service.ReloadSettings();
                _trayIcon.ShowNotification(
                    "AnarlogTrigger",
                    $"Watching process: {box.Text.Trim()}");
            }
            catch (Exception ex)
            {
                var error = new ContentDialog
                {
                    Title = "AnarlogTrigger",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = Content!.XamlRoot
                };
                await error.ShowAsync();
            }
        });
    }

    private async Task OnOpenConfigAsync()
    {
        try
        {
            ProcessStart(_settingsStore.ConfigPath);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task OnReloadConfigAsync()
    {
        try
        {
            _service.ReloadSettings();
            _trayIcon.ShowNotification("AnarlogTrigger", "Config reloaded");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task OnOpenLogAsync()
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            ProcessStart(_logDirectory);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private void OnExit()
    {
        DisposeTray();
        if (Application.Current is App app)
        {
            app.ShutdownApp();
        }
        else
        {
            Application.Current?.Exit();
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        await RunWithHostWindowAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "AnarlogTrigger",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = Content!.XamlRoot
            };
            await dialog.ShowAsync();
        });
    }

    private async Task RunWithHostWindowAsync(Func<Task> action)
    {
        H.NotifyIcon.WindowExtensions.Show(this, disableEfficiencyMode: false);
        try
        {
            await action();
        }
        finally
        {
            H.NotifyIcon.WindowExtensions.Hide(this, enableEfficiencyMode: false);
        }
    }

    private static void ProcessStart(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
