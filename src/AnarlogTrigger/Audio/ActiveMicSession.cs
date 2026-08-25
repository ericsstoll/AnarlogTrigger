namespace AnarlogTrigger.Audio;

public sealed class ActiveMicSession
{
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; init; }
}

public sealed class MatchedMicPresenceChangedEventArgs : EventArgs
{
    public required bool IsPresent { get; init; }
    public ActiveMicSession? Session { get; init; }
}
