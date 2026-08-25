using AnarlogTrigger.Actions;
using AnarlogTrigger.Audio;
using AnarlogTrigger.Config;
using AnarlogTrigger.Matching;
using AnarlogTrigger.Notifications;
using AnarlogTrigger.State;
using Microsoft.Extensions.Logging;

namespace AnarlogTrigger.Services;

public sealed class MeetingTriggerService : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly MeetingProcessMatcher _matcher;
    private readonly MicSessionMonitor _monitor;
    private readonly AnarlogHotkeySender _hotkeySender;
    private readonly StickyStopReminder _reminder;
    private readonly MeetingSessionState _state = new();
    private readonly ILogger<MeetingTriggerService> _logger;
    private readonly object _gate = new();
    private System.Threading.Timer? _debounceTimer;
    private bool _monitoring;

    public bool IsMonitoring => _monitoring;
    public MeetingTriggerPhase Phase
    {
        get
        {
            lock (_gate)
            {
                return _state.Phase;
            }
        }
    }

    public event EventHandler? StateChanged;

    public MeetingTriggerService(
        SettingsStore settingsStore,
        MeetingProcessMatcher matcher,
        MicSessionMonitor monitor,
        AnarlogHotkeySender hotkeySender,
        StickyStopReminder reminder,
        ILogger<MeetingTriggerService> logger)
    {
        _settingsStore = settingsStore;
        _matcher = matcher;
        _monitor = monitor;
        _hotkeySender = hotkeySender;
        _reminder = reminder;
        _logger = logger;
        _monitor.MatchedMicPresenceChanged += OnMicPresenceChanged;
    }

    public void StartMonitoring()
    {
        lock (_gate)
        {
            if (_monitoring)
            {
                return;
            }

            _monitoring = true;
            _monitor.Start(_settingsStore.Settings.PollIntervalMs);
            _logger.LogInformation("Meeting trigger monitoring enabled");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopMonitoring()
    {
        lock (_gate)
        {
            if (!_monitoring)
            {
                return;
            }

            _monitoring = false;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _monitor.Stop();
            _state.Phase = MeetingTriggerPhase.Idle;
            _state.MicSeenSince = null;
            _logger.LogInformation("Meeting trigger monitoring disabled");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReloadSettings()
    {
        var settings = _settingsStore.Reload();
        _matcher.Reload(settings);
        if (_monitoring)
        {
            _monitor.Stop();
            _monitor.Start(settings.PollIntervalMs);
        }

        _logger.LogInformation("Settings reloaded from {Path}", _settingsStore.ConfigPath);
    }

    private void OnMicPresenceChanged(object? sender, MatchedMicPresenceChangedEventArgs e)
    {
        lock (_gate)
        {
            if (!_monitoring)
            {
                return;
            }

            if (e.IsPresent && e.Session is not null)
            {
                OnMicAcquired(e.Session);
            }
            else
            {
                OnMicReleased();
            }
        }
    }

    private void OnMicAcquired(ActiveMicSession session)
    {
        _state.LastProcessName = session.ProcessName;
        _state.LastProcessId = session.ProcessId;

        if (_state.Phase is MeetingTriggerPhase.RecordingStarted or MeetingTriggerPhase.AwaitingStopDismiss)
        {
            // Already started for this cycle; ignore until release + cooldown path resets.
            return;
        }

        var settings = _settingsStore.Settings;
        if (_state.LastStartUtc is { } lastStart &&
            DateTimeOffset.UtcNow - lastStart < TimeSpan.FromSeconds(settings.StartCooldownSeconds))
        {
            _logger.LogDebug("Ignoring mic acquire during cooldown");
            return;
        }

        _state.MicSeenSince = DateTimeOffset.UtcNow;
        _state.Phase = MeetingTriggerPhase.Debouncing;
        StateChanged?.Invoke(this, EventArgs.Empty);

        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(
            _ => CompleteDebounce(),
            null,
            TimeSpan.FromSeconds(Math.Max(1, settings.DebounceSeconds)),
            Timeout.InfiniteTimeSpan);

        _logger.LogInformation(
            "Debouncing start for {Process} ({Seconds}s)",
            session.ProcessName,
            settings.DebounceSeconds);
    }

    private void CompleteDebounce()
    {
        lock (_gate)
        {
            if (!_monitoring || _state.Phase != MeetingTriggerPhase.Debouncing)
            {
                return;
            }

            try
            {
                _reminder.ResetCycle();
                _hotkeySender.SendStartListeningHotkey();
                _state.LastStartUtc = DateTimeOffset.UtcNow;
                _state.Phase = MeetingTriggerPhase.RecordingStarted;
                _logger.LogInformation(
                    "Sent Ctrl+Shift+N to anarlog.exe for {Process} (PID {Pid})",
                    _state.LastProcessName,
                    _state.LastProcessId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Anarlog start hotkey");
                _state.Phase = MeetingTriggerPhase.Idle;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMicReleased()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _state.MicSeenSince = null;

        if (_state.Phase == MeetingTriggerPhase.Debouncing)
        {
            _state.Phase = MeetingTriggerPhase.Idle;
            _logger.LogInformation("Mic released during debounce; start cancelled");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_state.Phase == MeetingTriggerPhase.RecordingStarted)
        {
            _state.Phase = MeetingTriggerPhase.AwaitingStopDismiss;
            var processName = _state.LastProcessName;
            _logger.LogInformation("Mic released after start; showing stop reminder");
            StateChanged?.Invoke(this, EventArgs.Empty);

            // Show outside lock work already done; reminder is sticky until user dismisses.
            try
            {
                _reminder.Show(processName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show stop reminder");
            }

            // After reminder is shown, return to Idle so a later meeting can start again.
            // Sticky UI remains until user dismisses it.
            _state.Phase = MeetingTriggerPhase.Idle;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_state.Phase != MeetingTriggerPhase.Idle)
        {
            _state.Phase = MeetingTriggerPhase.Idle;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        StopMonitoring();
        _monitor.MatchedMicPresenceChanged -= OnMicPresenceChanged;
        _debounceTimer?.Dispose();
        _monitor.Dispose();
        _reminder.Dispose();
    }
}
