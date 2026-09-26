using PvPSentinel.Combat.Native.Defense;
using PvPSentinel.Combat.Native.Jobs.Machinist;

namespace PvPSentinel.Combat.Native;

internal sealed record ShadowActionAvailability(
    bool IsAvailable,
    string Explanation);

/// <summary>
/// Tracks actions that Shadow/Observe has hypothetically spent. The live client
/// cannot consume those actions on the observer's behalf, so relying only on
/// ActionManager readiness causes impossible repeated recommendations whenever
/// the external ACR has not yet used the same cooldown or limit gauge.
/// </summary>
internal sealed class NativeShadowActionLedger
{
    private static readonly TimeSpan GlobalActionLock = TimeSpan.FromMilliseconds(600);

    private static readonly IReadOnlyDictionary<uint, ShadowActionSpec> Specs =
        new Dictionary<uint, ShadowActionSpec>
        {
            [PvPCommonActions.Recuperate] = new("Recuperate", TimeSpan.FromSeconds(1)),
            [PvPCommonActions.Purify] = new("Purify", TimeSpan.FromSeconds(4)),
            [PvPCommonActions.Guard] = new("Guard", TimeSpan.FromSeconds(30)),
            [MachinistActions.BlastCharge] = new("Blast Charge", TimeSpan.FromSeconds(2.4)),
            [MachinistActions.BlazingShot] = new("Blazing Shot", TimeSpan.FromSeconds(1.5)),
            [MachinistActions.Scattergun] = new("Scattergun", TimeSpan.FromSeconds(16)),
            [MachinistActions.Drill] = new("Drill", TimeSpan.FromSeconds(10)),
            [MachinistActions.Bioblaster] = new("Bioblaster", TimeSpan.FromSeconds(10)),
            [MachinistActions.AirAnchor] = new("Air Anchor", TimeSpan.FromSeconds(10)),
            [MachinistActions.ChainSaw] = new("Chain Saw", TimeSpan.FromSeconds(10)),
            [MachinistActions.Wildfire] = new("Wildfire", TimeSpan.FromSeconds(24)),
            [MachinistActions.BishopAutoturret] = new("Bishop Autoturret", TimeSpan.FromSeconds(30)),
            [MachinistActions.Analysis] = new("Analysis", TimeSpan.FromSeconds(20)),
            // The action recast is shorter, but a hypothetical use spends the
            // full limit gauge. Model the current 90-second gauge charge time.
            [MachinistActions.MarksmansSpite] = new("Marksman's Spite", TimeSpan.FromSeconds(90)),
            [MachinistActions.FullMetalField] = new("Full Metal Field", TimeSpan.FromSeconds(30)),
            [MachinistActions.Dervish] = new("Dervish", TimeSpan.FromSeconds(45)),
        };

    private readonly Dictionary<uint, DateTime> readyAtUtc = [];
    private DateTime globalReadyAtUtc = DateTime.MinValue;
    private string lastActionName = "none";

    public ShadowActionAvailability CheckGlobal(DateTime now)
    {
        var remaining = globalReadyAtUtc - now;
        return remaining > TimeSpan.Zero
            ? new ShadowActionAvailability(
                false,
                $"Shadow simulation global action lock after hypothetical {lastActionName} has {remaining.TotalSeconds:F1}s remaining")
            : new ShadowActionAvailability(true, "Shadow simulation global action lock is clear");
    }

    public ShadowActionAvailability Check(uint actionId, DateTime now)
    {
        if (!Specs.TryGetValue(actionId, out var spec) || !readyAtUtc.TryGetValue(actionId, out var readyAt))
            return new ShadowActionAvailability(true, "No hypothetical cooldown is being held");

        var remaining = readyAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            readyAtUtc.Remove(actionId);
            return new ShadowActionAvailability(true, "Hypothetical cooldown has recovered");
        }

        return new ShadowActionAvailability(
            false,
            $"Shadow simulation already spent {spec.Name}; hypothetical {spec.Recast.TotalSeconds:0.#}s recovery has {remaining.TotalSeconds:F1}s remaining");
    }

    public void Commit(uint actionId, DateTime now)
    {
        globalReadyAtUtc = now + GlobalActionLock;
        if (!Specs.TryGetValue(actionId, out var spec))
            return;

        lastActionName = spec.Name;
        readyAtUtc[actionId] = now + spec.Recast;
    }

    private sealed record ShadowActionSpec(string Name, TimeSpan Recast);
}
