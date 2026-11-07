namespace V77Navigator.Core;

public sealed record NavigationPosition(int ProcessId, long Window, string ModuleKey, int Offset);

// Future adapters must validate PID, HWND and document identity before restoring.
public sealed class NavigationHistory(int capacity = 100)
{
    private readonly LinkedList<NavigationPosition> entries = new();
    private readonly int limit = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public int Count => entries.Count;
    public void Push(NavigationPosition position)
    {
        if (entries.Last?.Value == position) return;
        entries.AddLast(position);
        if (entries.Count > limit) entries.RemoveFirst();
    }
    public NavigationPosition? Pop()
    {
        if (entries.Last is null) return null;
        var p = entries.Last.Value;
        entries.RemoveLast();
        return p;
    }
}
