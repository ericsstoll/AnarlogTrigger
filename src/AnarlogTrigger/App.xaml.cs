using AnarlogTrigger.Actions;
using AnarlogTrigger.Audio;
using AnarlogTrigger.Config;
using AnarlogTrigger.Matching;
using AnarlogTrigger.Notifications;
using AnarlogTrigger.Services;
using AnarlogTrigger.UI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;

namespace AnarlogTrigger;

public partial class App : Application
{
    private TrayHostWindow? _trayWindow;
    private ILoggerFactory? _loggerFactory;
    private MeetingTriggerService? _service;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var baseDir = AppContext.BaseDirectory;
        var configPath = Path.Combine(baseDir, "appsettings.json");
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AnarlogTrigger",
            "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "anarlog-trigger-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        try
        {
            var settingsStore = new SettingsStore(configPath, _loggerFactory.CreateLogger<SettingsStore>());
            var matcher = new MeetingProcessMatcher(
                settingsStore.Settings,
                _loggerFactory.CreateLogger<MeetingProcessMatcher>());
            var monitor = new MicSessionMonitor(
                matcher,
                _loggerFactory.CreateLogger<MicSessionMonitor>());
            var hotkey = new AnarlogHotkeySender();
            var reminder = new StickyStopReminder(_loggerFactory.CreateLogger<StickyStopReminder>());
            _service = new MeetingTriggerService(
                settingsStore,
                matcher,
                monitor,
                hotkey,
                reminder,
                _loggerFactory.CreateLogger<MeetingTriggerService>());

            Log.Information("AnarlogTrigger starting (WinUI). Config: {ConfigPath}", configPath);

            _trayWindow = new TrayHostWindow(
                _service,
                settingsStore,
                hotkey,
                _loggerFactory.CreateLogger("Tray"),
                logDirectory);
            _trayWindow.Activate();
            H.NotifyIcon.WindowExtensions.Hide(_trayWindow);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AnarlogTrigger failed to start");
            NativeMessageBox.Show(ex.Message, "AnarlogTrigger");
            Exit();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        e.Handled = true;
    }

    internal void ShutdownApp()
    {
        try
        {
            _service?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _trayWindow?.DisposeTray();
        }
        catch
        {
            // ignored
        }

        _loggerFactory?.Dispose();
        Log.CloseAndFlush();
        Exit();
    }
}
