using AnarlogTrigger.Matching;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AnarlogTrigger.Audio;

/// <summary>
/// Polls WASAPI capture sessions and reports when a watched meeting process holds the mic.
/// </summary>
public sealed class MicSessionMonitor : IDisposable
{
    private readonly MeetingProcessMatcher _matcher;
    private readonly ILogger<MicSessionMonitor> _logger;
    private readonly object _gate = new();
    private MMDeviceEnumerator? _enumerator;
    private System.Threading.Timer? _timer;
    private bool _lastPresent;
    private bool _disposed;

    public event EventHandler<MatchedMicPresenceChangedEventArgs>? MatchedMicPresenceChanged;

    public MicSessionMonitor(MeetingProcessMatcher matcher, ILogger<MicSessionMonitor> logger)
    {
        _matcher = matcher;
        _logger = logger;
    }

    public void Start(int pollIntervalMs)
    {
        lock (_gate)
        {
            if (_timer is not null)
            {
                return;
            }

            _enumerator = new MMDeviceEnumerator();
            var interval = Math.Max(250, pollIntervalMs);
            _timer = new System.Threading.Timer(_ => PollSafe(), null, 0, interval);
            _logger.LogInformation("Mic session monitor started (poll {Interval}ms)", interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _enumerator?.Dispose();
            _enumerator = null;
            if (_lastPresent)
            {
                _lastPresent = false;
                RaisePresence(false, null);
            }

            _logger.LogInformation("Mic session monitor stopped");
        }
    }

    private void PollSafe()
    {
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mic session poll failed");
        }
    }

    private void Poll()
    {
        MMDeviceEnumerator enumerator;
        lock (_gate)
        {
            if (_disposed || _enumerator is null)
            {
                return;
            }

            enumerator = _enumerator;
        }

        ActiveMicSession? matched = null;
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        for (var i = 0; i < devices.Count; i++)
        {
            try
            {
                var device = devices[i];
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                if (sessions is null)
                {
                    continue;
                }

                for (var s = 0; s < sessions.Count; s++)
                {
                    var session = sessions[s];
                    if (session.State != AudioSessionState.AudioSessionStateActive)
                    {
                        continue;
                    }

                    var pid = session.GetProcessID;
                    if (!_matcher.TryMatch(pid, out var processName))
                    {
                        continue;
                    }

                    matched = new ActiveMicSession
                    {
                        ProcessId = pid,
                        ProcessName = processName
                    };
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed reading capture device sessions");
            }

            if (matched is not null)
            {
                break;
            }
        }

        var present = matched is not null;
        bool changed;
        lock (_gate)
        {
            changed = present != _lastPresent;
            _lastPresent = present;
        }

        if (changed)
        {
            if (present)
            {
                _logger.LogInformation(
                    "Matched meeting mic active: {ProcessName} (PID {Pid})",
                    matched!.ProcessName,
                    matched.ProcessId);
            }
            else
            {
                _logger.LogInformation("Matched meeting mic released");
            }

            RaisePresence(present, matched);
        }
    }

    private void RaisePresence(bool present, ActiveMicSession? session)
    {
        MatchedMicPresenceChanged?.Invoke(this, new MatchedMicPresenceChangedEventArgs
        {
            IsPresent = present,
            Session = session
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }
}
