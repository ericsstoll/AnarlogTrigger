using AnarlogTrigger.Services;
using AnarlogTrigger.State;
using Microsoft.Extensions.Logging;

namespace AnarlogTrigger.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly MeetingTriggerService _service;
    private readonly Config.SettingsStore _settingsStore;
    private readonly Actions.AnarlogHotkeySender _hotkeySender;
    private readonly ILogger _logger;
    private readonly string _logDirectory;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayApplicationContext(
        MeetingTriggerService service,
        Config.SettingsStore settingsStore,
        Actions.AnarlogHotkeySender hotkeySender,
        ILogger logger,
        string logDirectory)
    {
        _service = service;
        _settingsStore = settingsStore;
        _hotkeySender = hotkeySender;
        _logger = logger;
        _logDirectory = logDirectory;

        _statusItem = new ToolStripMenuItem("Status: starting…") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Stop monitoring", null, OnToggleMonitoring);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add("Test start hotkey (Ctrl+Shift+N)", null, OnTestHotkey);
        menu.Items.Add("Add process…", null, OnAddProcess);
        menu.Items.Add("Open config", null, OnOpenConfig);
        menu.Items.Add("Reload config", null, OnReloadConfig);
        menu.Items.Add("Open log folder", null, OnOpenLog);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "AnarlogTrigger",
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => OnOpenConfig(null, EventArgs.Empty);

        _service.StateChanged += (_, _) => UpdateStatusUi();
        _service.StartMonitoring();
        UpdateStatusUi();
        _logger.LogInformation("Tray UI ready");
    }

    private void UpdateStatusUi()
    {
        void Apply()
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
            _trayIcon.Text = $"AnarlogTrigger ({monitoring})";
        }

        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.BeginInvoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void OnToggleMonitoring(object? sender, EventArgs e)
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

    private void OnTestHotkey(object? sender, EventArgs e)
    {
        try
        {
            _hotkeySender.SendStartListeningHotkey();
            _logger.LogInformation("Test Ctrl+Shift+N sent to anarlog.exe");
            _trayIcon.ShowBalloonTip(
                2500,
                "AnarlogTrigger",
                "Sent Ctrl+Shift+N to Anarlog — check whether it started listening.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test hotkey failed");
            MessageBox.Show(ex.Message, "AnarlogTrigger", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnAddProcess(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Add process name",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(360, 110),
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };

        var label = new Label
        {
            Text = "Executable name (e.g. firefox or MySoftphone):",
            AutoSize = true,
            Location = new Point(12, 12)
        };
        var box = new TextBox
        {
            Location = new Point(12, 36),
            Width = 330
        };
        var ok = new Button
        {
            Text = "Add",
            DialogResult = DialogResult.OK,
            Location = new Point(186, 70),
            Width = 75
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(267, 70),
            Width = 75
        };

        dialog.Controls.AddRange([label, box, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            _settingsStore.AddExtraProcess(box.Text);
            _service.ReloadSettings();
            _trayIcon.ShowBalloonTip(
                3000,
                "AnarlogTrigger",
                $"Watching process: {box.Text.Trim()}",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AnarlogTrigger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnOpenConfig(object? sender, EventArgs e)
    {
        try
        {
            ProcessStart(_settingsStore.ConfigPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AnarlogTrigger", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnReloadConfig(object? sender, EventArgs e)
    {
        try
        {
            _service.ReloadSettings();
            _trayIcon.ShowBalloonTip(2000, "AnarlogTrigger", "Config reloaded", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AnarlogTrigger", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnOpenLog(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            ProcessStart(_logDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AnarlogTrigger", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _service.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _service.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void ProcessStart(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static Icon LoadAppIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icoPath))
        {
            return new Icon(icoPath);
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
