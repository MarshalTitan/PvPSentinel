using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Dalamud.Plugin.Services;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Diagnostics;

internal sealed class FrontlineEventRecorder(string pluginConfigDirectory, IPluginLog log)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private string? sessionDirectory;
    private readonly StringBuilder pending = new();
    private int pendingLines;
    private DateTime lastFlushUtc = DateTime.MinValue;
    public string CurrentLogDirectory => sessionDirectory ?? "NONE";

    public void Start(FrontlineMap map, DateTime now)
    {
        try
        {
            Flush(force: true, now);
            sessionDirectory = Path.Combine(pluginConfigDirectory, "M2Logs",
                $"{now:yyyyMMdd-HHmmss}-{map.ToString().ToLowerInvariant()}");
            Directory.CreateDirectory(sessionDirectory);
            lastFlushUtc = now;
        }
        catch (Exception ex)
        {
            sessionDirectory = null;
            log.Warning(ex, "PvPSentinel could not create the M2 event-log directory.");
        }
    }

    public void Record(string eventName, DateTime now, object? data = null)
    {
        if (sessionDirectory is null)
            return;
        try
        {
            var entry = JsonSerializer.Serialize(new
            {
                timestamp_utc = now,
                event_name = eventName,
                data,
            }, JsonOptions);
            pending.AppendLine(entry);
            pendingLines++;
            Flush(force: pendingLines >= 32 || now - lastFlushUtc >= TimeSpan.FromSeconds(1), now);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "PvPSentinel could not append an M2 event.");
        }
    }

    public void Complete(RetainedMatchSummary summary)
    {
        if (sessionDirectory is null)
            return;
        try
        {
            Flush(force: true, DateTime.UtcNow);
            File.WriteAllText(Path.Combine(sessionDirectory, "summary.json"),
                JsonSerializer.Serialize(summary, JsonOptions));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "PvPSentinel could not write the retained M2 match summary.");
        }
    }

    private void Flush(bool force, DateTime now)
    {
        if (!force || pendingLines == 0 || sessionDirectory is null)
            return;

        try
        {
            File.AppendAllText(Path.Combine(sessionDirectory, "events.jsonl"), pending.ToString());
            pending.Clear();
            pendingLines = 0;
            lastFlushUtc = now;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "PvPSentinel could not flush buffered M2 events.");
        }
    }
}
