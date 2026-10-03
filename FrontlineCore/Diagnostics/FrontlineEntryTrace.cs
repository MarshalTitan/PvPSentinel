using Dalamud.Plugin.Services;

namespace PvPSentinel.FrontlineCore.Diagnostics;

// Separate from buffered match events: a native process exit cannot flush them.
// Sample entry stages at 0, 5, 10 and 15 seconds only.
internal sealed class FrontlineEntryTrace(string configDirectory, IPluginLog log)
{
    private readonly string path = Path.Combine(configDirectory, "frontline-entry-stages.log");
    private uint territory;
    private DateTime enteredUtc;
    private int nextSample;
    private bool sampling;

    public void Begin(uint currentTerritory, DateTime now)
    {
        if (territory != currentTerritory)
        {
            territory = currentTerritory;
            enteredUtc = now;
            nextSample = 0;
            Write($"{now:O} territory={territory} ENTRY (new process/transition)");
        }
        sampling = nextSample < 4 && now - enteredUtc >= TimeSpan.FromSeconds(nextSample * 5);
        if (sampling)
        {
            Write($"{now:O} territory={territory} sample={nextSample} START");
            nextSample++;
        }
    }

    public void Stage(string stage, string state, DateTime now)
    {
        if (sampling)
            Write($"{now:O} territory={territory} {stage} {state}");
    }

    public void EndFrame(DateTime now)
    {
        Stage("frontline-scan", "OK", now);
        sampling = false;
    }

    public void Reset()
    {
        territory = 0;
        sampling = false;
    }

    private void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(true);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "PvPSentinel entry-stage trace could not be written.");
        }
    }
}
