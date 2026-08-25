using AnarlogTrigger.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace AnarlogTrigger.Notifications;

/// <summary>
/// Shows a Windows toast reminding the user to stop Anarlog, with an action to focus Anarlog.
/// </summary>
public sealed class StickyStopReminder : IDisposable
{
    private readonly ILogger<StickyStopReminder> _logger;
    private readonly object _gate = new();
    private bool _shownThisCycle;
    private bool _subscribed;

    public StickyStopReminder(ILogger<StickyStopReminder> logger)
    {
        _logger = logger;
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        _subscribed = true;
    }

    public void ResetCycle()
    {
        lock (_gate)
        {
            _shownThisCycle = false;
        }
    }

    public void Show(string? processName)
    {
        lock (_gate)
        {
            if (_shownThisCycle)
            {
                return;
            }

            _shownThisCycle = true;
        }

        var appLabel = string.IsNullOrWhiteSpace(processName) ? "the meeting app" : processName;
        try
        {
            new ToastContentBuilder()
                .AddArgument("action", "reminder")
                .AddText("Meeting may have ended")
                .AddText($"{appLabel} released the microphone. Stop Anarlog recording when ready.")
                .SetToastScenario(ToastScenario.Reminder)
                .AddButton(new ToastButton()
                    .SetContent("Open Anarlog")
                    .AddArgument("action", "focus-anarlog"))
                .AddButton(new ToastButton()
                    .SetContent("Dismiss")
                    .AddArgument("action", "dismiss"))
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTimeOffset.Now.AddDays(1);
                });

            _logger.LogInformation("Stop reminder toast shown for {Process}", appLabel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast notification failed");
            lock (_gate)
            {
                _shownThisCycle = false;
            }
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (!args.TryGetValue("action", out var action))
            {
                return;
            }

            if (action is "focus-anarlog" or "reminder")
            {
                try
                {
                    AnarlogWindowActivator.FocusAnarlog();
                    _logger.LogInformation("Focused Anarlog from toast action '{Action}'", action);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to focus Anarlog from toast");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast activation handling failed");
        }
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
            _subscribed = false;
        }

        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch
        {
            // ignored
        }

        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch
        {
            // ignored
        }
    }
}
