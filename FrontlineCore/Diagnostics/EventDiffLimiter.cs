namespace PvPSentinel.FrontlineCore.Diagnostics;

internal sealed class EventDiffLimiter(TimeSpan minimumRepeatInterval)
{
    private readonly Dictionary<string, (string Value, DateTime AtUtc)> last = [];

    public bool ShouldEmit(string key, string value, DateTime now)
    {
        if (!last.TryGetValue(key, out var prior) || prior.Value != value || now - prior.AtUtc >= minimumRepeatInterval)
        {
            last[key] = (value, now);
            return true;
        }
        return false;
    }

    public void Reset() => last.Clear();
}

