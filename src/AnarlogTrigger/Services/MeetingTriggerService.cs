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
    private System.Threading.Timer? _releaseDebounceTimer;
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
            CancelStartDebounce();
            CancelReleaseDebounce();
            _monitor.Stop();
            _state.Phase = MeetingTriggerPhase.Idle;
            _state.MicSeenSince = null;
            _state.MicMissingSince = null;
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
        _state.MicMissingSince = null;

        if (_state.Phase == MeetingTriggerPhase.DebouncingRelease)
        {
            CancelReleaseDebounce();
            _state.Phase = MeetingTriggerPhase.RecordingStarted;
            _logger.LogInformation(
                "Mic returned during release debounce for {Process}; stop reminder cancelled",
                session.ProcessName);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

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

        CancelStartDebounce();
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
        CancelStartDebounce();
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
            var settings = _settingsStore.Settings;
            var seconds = Math.Max(1, settings.ReleaseDebounceSeconds);
            _state.MicMissingSince = DateTimeOffset.UtcNow;
            _state.Phase = MeetingTriggerPhase.DebouncingRelease;
            StateChanged?.Invoke(this, EventArgs.Empty);

            CancelReleaseDebounce();
            _releaseDebounceTimer = new System.Threading.Timer(
                _ => CompleteReleaseDebounce(),
                null,
                TimeSpan.FromSeconds(seconds),
                Timeout.InfiniteTimeSpan);

            _logger.LogInformation(
                "Mic missing after start; release debounce ({Seconds}s) before stop reminder",
                seconds);
            return;
        }

        if (_state.Phase == MeetingTriggerPhase.DebouncingRelease)
        {
            // Already waiting to confirm release.
            return;
        }

        if (_state.Phase != MeetingTriggerPhase.Idle)
        {
            _state.Phase = MeetingTriggerPhase.Idle;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CompleteReleaseDebounce()
    {
        string? processName;
        lock (_gate)
        {
            if (!_monitoring || _state.Phase != MeetingTriggerPhase.DebouncingRelease)
            {
                return;
            }

            processName = _state.LastProcessName;
            _state.Phase = MeetingTriggerPhase.AwaitingStopDismiss;
            _state.MicMissingSince = null;
            _logger.LogInformation("Release confirmed; showing stop reminder");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            _reminder.Show(processName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show stop reminder");
        }

        lock (_gate)
        {
            // After reminder is shown, return to Idle so a later meeting can start again.
            // Sticky UI remains until user dismisses it.
            if (_state.Phase == MeetingTriggerPhase.AwaitingStopDismiss)
            {
                _state.Phase = MeetingTriggerPhase.Idle;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void CancelStartDebounce()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    private void CancelReleaseDebounce()
    {
        _releaseDebounceTimer?.Dispose();
        _releaseDebounceTimer = null;
    }

    public void Dispose()
    {
        StopMonitoring();
        _monitor.MatchedMicPresenceChanged -= OnMicPresenceChanged;
        CancelStartDebounce();
        CancelReleaseDebounce();
        _monitor.Dispose();
        _reminder.Dispose();
    }
}
