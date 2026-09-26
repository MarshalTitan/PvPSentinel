using PvPSentinel.Models;
using PvPSentinel.Combat.Threat;

namespace PvPSentinel.Combat.Native.Defense;

internal sealed record DefensiveEvaluation(
    IReadOnlyList<NativeActionCandidate> Candidates,
    IReadOnlyList<string> Rejections,
    int IncomingThreats,
    bool SuppressOffense,
    string PreemptionReason,
    string GuardState,
    string PurifyState);

internal sealed class PvPDefensiveController
{
    private const uint GuardStatusId = 3054;
    private const uint ResilienceStatusId = 3248;
    private const uint StunStatusId = 1343;
    private const uint HeavyStatusId = 1344;
    private const uint BindStatusId = 1345;
    private const uint SilenceStatusId = 1347;
    private const uint MiracleOfNatureStatusId = 3085;
    private const uint DeepFreezeStatusId = 3219;
    private readonly Queue<(DateTime CapturedAtUtc, float HpPercent)> hpHistory = new();

    private static readonly HashSet<uint> RemovableStatusIds =
    [
        StunStatusId,
        HeavyStatusId,
        BindStatusId,
        SilenceStatusId,
        DeepFreezeStatusId,
        MiracleOfNatureStatusId,
    ];

    private static readonly HashSet<string> RemovableStatusNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stun",
        "Heavy",
        "Bind",
        "Silence",
        "Deep Freeze",
        "Miracle of Nature",
    };

    public DefensiveEvaluation Evaluate(
        GameStateSnapshot game,
        PvPThreatSnapshot threat,
        Configuration config)
    {
        var local = game.LocalPlayer;
        if (local is null)
            return new DefensiveEvaluation(
                [], ["Defense: local player unavailable."], 0, false, "None",
                "Guard unavailable: local player missing.",
                "Purify unavailable: local player missing.");

        var candidates = new List<NativeActionCandidate>();
        var rejections = new List<string>();
        var incomingTargeters = threat.IsReliable ? threat.CombatRelevantTargeterCount : 0;
        var nearbyThreats = threat.IsReliable ? threat.NearbyEnemyCount : 0;
        var nearbyFriendlies = threat.IsReliable ? threat.NearbyFriendlyCount : 0;
        var incomingThreats = Math.Max(incomingTargeters, nearbyThreats);
        var selfTarget = (local.GameObjectId, local.EntityId);
        var hpLossPerSecond = UpdateHpLossRate(game.CapturedAtUtc, local.HpPercent);

        var removable = local.Statuses.FirstOrDefault(status =>
            RemovableStatusIds.Contains(status.Id) || RemovableStatusNames.Contains(status.Name));
        var resilient = local.HasStatus(ResilienceStatusId) || local.HasStatusExact("Resilience");
        var purifyEligible = NativeCombatPolicy.PurifyEligible(removable is not null, resilient, local.CurrentMp);
        string purifyState;
        if (removable is not null && !resilient)
        {
            if (local.CurrentMp >= 2000)
            {
                purifyState = $"Purify eligible: {removable.Name} ({removable.RemainingSeconds:F1}s), Resilience absent, MP {local.CurrentMp:N0}.";
                candidates.Add(Self(
                    PvPCommonActions.Purify,
                    "Purify",
                    selfTarget,
                    $"Remove supported PvP control effect {removable.Name} ({removable.RemainingSeconds:F1}s); Resilience is absent."));
            }
            else
            {
                purifyState = $"Purify rejected despite CC: {removable.Name} is present, but MP {local.CurrentMp:N0} is below 2,000.";
                rejections.Add(purifyState);
            }
        }
        else if (removable is not null)
        {
            purifyState = $"Purify rejected despite CC: {removable.Name} is present, but Resilience immunity is active.";
            rejections.Add(purifyState);
        }
        else
        {
            purifyState = "Purify idle: no supported removable PvP control effect observed.";
        }

        NativeActionCandidate? recuperate = null;
        if (local.HpPercent < config.NativeRecuperateHpPercent)
        {
            if (NativeCombatPolicy.RecuperateEligible(local.HpPercent, config.NativeRecuperateHpPercent, local.CurrentMp))
            {
                recuperate = Self(
                    PvPCommonActions.Recuperate,
                    "Recuperate",
                    selfTarget,
                    $"Player HP {local.HpPercent:F1}% is below the configured {config.NativeRecuperateHpPercent:F1}% threshold; repeated healing remains eligible while HP/MP permit.");
            }
            else
            {
                rejections.Add($"Recuperate rejected: HP {local.HpPercent:F1}% is low, but MP {local.CurrentMp} is below 2000.");
            }
        }

        var guarding = local.HasStatus(GuardStatusId) || local.HasStatusExact("Guard");
        var guard = NativeCombatPolicy.CalculateGuardThreshold(
            config.NativeGuardHpPercent,
            config.NativeGuardMaximumHpPercent,
            incomingTargeters,
            nearbyThreats,
            nearbyFriendlies,
            config.NativeGuardTargeterHpBonus,
            config.NativeGuardNearbyThreatHpBonus,
            config.NativeGuardNearbyThreatBaseline,
            config.NativeGuardDisadvantageHpBonus,
            hpLossPerSecond,
            config.NativeGuardRapidLossPercentPerSecond,
            config.NativeGuardRapidLossHpBonus);
        var dangerousHp = local.HpPercent <= guard.Threshold;
        var meaningfulPressure = incomingTargeters >= config.NativeGuardMinimumThreats ||
                                 nearbyThreats >= config.NativeGuardMinimumThreats;
        var guardNeeded = dangerousHp && meaningfulPressure && !guarding;
        var guardFactors =
            $"base {config.NativeGuardHpPercent:F1}% + targeters {guard.TargeterContribution:F1} + density {guard.NearbyContribution:F1} + disadvantage {guard.DisadvantageContribution:F1} + collapse {guard.RapidLossContribution:F1}; " +
            $"targeters={incomingTargeters}, nearby enemies/allies={nearbyThreats}/{nearbyFriendlies}, numerical disadvantage={guard.NumericalDisadvantage}, HP loss={guard.HpLossPercentPerSecond:F1}%/s, cap={config.NativeGuardMaximumHpPercent:F1}%";
        string guardState;
        if (dangerousHp && meaningfulPressure && !guarding)
        {
            guardState = $"Guard eligible: HP {local.HpPercent:F1}% <= dynamic threshold {guard.Threshold:F1}% ({guardFactors}).";
            candidates.Add(Self(
                PvPCommonActions.Guard,
                "Guard",
                selfTarget,
                guardState));
        }
        else
        {
            var reason = guarding
                ? "Guard rejected: Guard is already active."
                : !dangerousHp
                    ? $"Guard rejected: HP {local.HpPercent:F1}% is above dynamic threshold {guard.Threshold:F1}% ({guardFactors})."
                    : $"Guard rejected: HP is inside the dynamic threshold, but {incomingTargeters} targeter(s)/{nearbyThreats} nearby threat(s) are below configured pressure {config.NativeGuardMinimumThreats}. ({guardFactors}).";
            guardState = reason;
            rejections.Add(guardState);
        }

        if (recuperate is not null)
            candidates.Add(recuperate);

        rejections.Add("Standard-issue Elixir rejected: first milestone intentionally leaves Elixir for a future verified safe-disengagement evaluator.");
        var recuperateNeeded = NativeCombatPolicy.RecuperateEligible(
            local.HpPercent, config.NativeRecuperateHpPercent, local.CurrentMp);
        var preemptionReason = NativeCombatPolicy.DefensePreemptionReason(
            purifyEligible, guardNeeded, recuperateNeeded);
        var suppressOffense = preemptionReason != "None";

        return new DefensiveEvaluation(
            candidates,
            rejections,
            incomingThreats,
            suppressOffense,
            preemptionReason,
            guardState,
            purifyState);
    }

    public void Reset() => hpHistory.Clear();

    private float UpdateHpLossRate(DateTime capturedAtUtc, float hpPercent)
    {
        if (hpHistory.Count > 0 && capturedAtUtc <= hpHistory.Last().CapturedAtUtc)
            hpHistory.Clear();

        hpHistory.Enqueue((capturedAtUtc, hpPercent));
        while (hpHistory.Count > 0 && (capturedAtUtc - hpHistory.Peek().CapturedAtUtc).TotalSeconds > 1.5)
            hpHistory.Dequeue();

        var oldest = hpHistory.Peek();
        var seconds = (capturedAtUtc - oldest.CapturedAtUtc).TotalSeconds;
        if (seconds < 0.5)
            return 0f;

        return Math.Max(0f, (oldest.HpPercent - hpPercent) / (float)seconds);
    }

    private static NativeActionCandidate Self(
        uint id,
        string name,
        (ulong ObjectId, uint EntityId) target,
        string reason) =>
        new(NativeActionLayer.EmergencyDefense, id, name, NativeActionTarget.Self, target.ObjectId, target.EntityId, reason);
}
