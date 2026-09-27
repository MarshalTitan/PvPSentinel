using System.Text.Json;
using System.Text.Json.Serialization;
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
    public string CurrentLogDirectory => sessionDirectory ?? "NONE";

    public void Start(FrontlineMap map, DateTime now)
    {
        try
        {
            sessionDirectory = Path.Combine(pluginConfigDirectory, "M2Logs",
                $"{now:yyyyMMdd-HHmmss}-{map.ToString().ToLowerInvariant()}");
            Directory.CreateDirectory(sessionDirectory);
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
            File.AppendAllText(Path.Combine(sessionDirectory, "events.jsonl"), entry + Environment.NewLine);
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
            File.WriteAllText(Path.Combine(sessionDirectory, "summary.json"),
                JsonSerializer.Serialize(summary, JsonOptions));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "PvPSentinel could not write the retained M2 match summary.");
        }
    }
}

