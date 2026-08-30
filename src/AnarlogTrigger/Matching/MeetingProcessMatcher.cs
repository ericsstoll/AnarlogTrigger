using System.Diagnostics;
using AnarlogTrigger.Config;
using Microsoft.Extensions.Logging;

namespace AnarlogTrigger.Matching;

public sealed class MeetingProcessMatcher
{
    private readonly ILogger<MeetingProcessMatcher> _logger;
    private HashSet<string> _watched;
    private HashSet<string> _excluded;

    public MeetingProcessMatcher(AppSettings settings, ILogger<MeetingProcessMatcher> logger)
    {
        _logger = logger;
        (_watched, _excluded) = BuildSets(settings);
    }

    public void Reload(AppSettings settings)
    {
        (_watched, _excluded) = BuildSets(settings);
        _logger.LogInformation(
            "Process filter reloaded: {WatchedCount} watched, {ExcludedCount} excluded",
            _watched.Count,
            _excluded.Count);
    }

    public bool TryMatch(uint processId, out string processName)
    {
        processName = string.Empty;
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var normalized = Normalize(processName);
        if (_excluded.Contains(normalized))
        {
            return false;
        }

        return _watched.Contains(normalized);
    }

    private static (HashSet<string> Watched, HashSet<string> Excluded) BuildSets(AppSettings settings)
    {
        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in settings.BuiltInMeetingProcesses.Concat(settings.ExtraProcessNames))
        {
            var normalized = Normalize(name);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                watched.Add(normalized);
            }
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in settings.ExcludedProcessNames)
        {
            var normalized = Normalize(name);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                excluded.Add(normalized);
            }
        }

        excluded.Add(Normalize(Process.GetCurrentProcess().ProcessName));
        return (watched, excluded);
    }

    private static string Normalize(string processName)
    {
        var trimmed = processName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed;
    }
}
