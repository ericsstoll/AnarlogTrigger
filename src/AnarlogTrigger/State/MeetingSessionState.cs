namespace AnarlogTrigger.State;

public enum MeetingTriggerPhase
{
    Idle,
    Debouncing,
    RecordingStarted,
    AwaitingStopDismiss
}

public sealed class MeetingSessionState
{
    public MeetingTriggerPhase Phase { get; set; } = MeetingTriggerPhase.Idle;
    public string? LastProcessName { get; set; }
    public uint? LastProcessId { get; set; }
    public DateTimeOffset? MicSeenSince { get; set; }
    public DateTimeOffset? LastStartUtc { get; set; }
}
