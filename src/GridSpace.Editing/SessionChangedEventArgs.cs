namespace GridSpace.Editing;

public sealed class SessionChangedEventArgs(string reason, bool documentChanged) : EventArgs
{
    public string Reason { get; } = reason;
    public bool DocumentChanged { get; } = documentChanged;
}
