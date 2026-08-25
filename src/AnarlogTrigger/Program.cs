using AnarlogTrigger.Actions;
using AnarlogTrigger.Audio;
using AnarlogTrigger.Config;
using AnarlogTrigger.Matching;
using AnarlogTrigger.Notifications;
using AnarlogTrigger.Services;
using AnarlogTrigger.UI;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AnarlogTrigger;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

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

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        try
        {
            var settingsStore = new SettingsStore(configPath, loggerFactory.CreateLogger<SettingsStore>());
            var matcher = new MeetingProcessMatcher(
                settingsStore.Settings,
                loggerFactory.CreateLogger<MeetingProcessMatcher>());
            var monitor = new MicSessionMonitor(
                matcher,
                loggerFactory.CreateLogger<MicSessionMonitor>());
            var hotkey = new AnarlogHotkeySender();
            var reminder = new StickyStopReminder(loggerFactory.CreateLogger<StickyStopReminder>());
            var service = new MeetingTriggerService(
                settingsStore,
                matcher,
                monitor,
                hotkey,
                reminder,
                loggerFactory.CreateLogger<MeetingTriggerService>());

            Log.Information("AnarlogTrigger starting. Config: {ConfigPath}", configPath);
            Application.Run(new TrayApplicationContext(
                service,
                settingsStore,
                hotkey,
                loggerFactory.CreateLogger("Tray"),
                logDirectory));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AnarlogTrigger failed to start");
            MessageBox.Show(
                ex.Message,
                "AnarlogTrigger",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
