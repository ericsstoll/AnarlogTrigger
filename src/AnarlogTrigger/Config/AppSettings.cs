using AnarlogTrigger.Matching;

namespace AnarlogTrigger.Config;

public sealed class AppSettings
{
    public int DebounceSeconds { get; set; } = 5;
    public int ReleaseDebounceSeconds { get; set; } = 5;
    public int StartCooldownSeconds { get; set; } = 60;
    public int PollIntervalMs { get; set; } = 1000;
    public List<string> BuiltInMeetingProcesses { get; set; } = BuiltInMeetingApps.DefaultProcessNames.ToList();
    public List<string> ExtraProcessNames { get; set; } = [];
    public List<string> ExcludedProcessNames { get; set; } = ["anarlog", "AnarlogTrigger"];
}
