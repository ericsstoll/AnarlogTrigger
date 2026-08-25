namespace AnarlogTrigger.Matching;

public static class BuiltInMeetingApps
{
    public static IReadOnlyList<string> DefaultProcessNames { get; } =
    [
        "ms-teams",
        "Teams",
        "Zoom",
        "slack",
        "Discord",
        "webex",
        "CiscoCollabHost",
        "ciscowebexstart",
        "g2m",
        "GoTo Meeting",
        "BlueJeans",
        "Skype",
        "SkypeApp",
        "Chime"
    ];
}
