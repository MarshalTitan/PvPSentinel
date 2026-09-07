using Dalamud.Plugin.Services;

namespace PvPSentinel.Diagnostics;

internal sealed class DevelopmentLogger(IPluginLog log, Func<bool> isEnabled)
{
    private readonly Dictionary<string, LogState> states = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public void Changed(string key, string signature, string message)
    {
        if (!isEnabled())
            return;

        lock (sync)
        {
            if (states.TryGetValue(key, out var previous) && previous.Signature == signature)
                return;

            states[key] = new LogState(signature, DateTime.UtcNow);
        }

        log.Debug("PvPSentinel verbose [{Category}]: {Detail}", key, message);
    }

    public void Throttled(string key, string message, TimeSpan? minimumInterval = null)
    {
        if (!isEnabled())
            return;

        var now = DateTime.UtcNow;
        var interval = minimumInterval ?? TimeSpan.FromSeconds(5);
        lock (sync)
        {
            if (states.TryGetValue(key, out var previous) && now - previous.LoggedAtUtc < interval)
                return;

            states[key] = new LogState(message, now);
        }

        log.Debug("PvPSentinel verbose [{Category}]: {Detail}", key, message);
    }

    public void Reset()
    {
        lock (sync)
            states.Clear();
    }

    private sealed record LogState(string Signature, DateTime LoggedAtUtc);
}
