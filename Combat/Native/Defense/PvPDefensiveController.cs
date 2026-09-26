using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.Combat.Native.Defense;

internal sealed record DefensiveEvaluation(
    IReadOnlyList<NativeActionCandidate> Candidates,
    IReadOnlyList<string> Rejections,
    int IncomingThreats);

internal sealed class PvPDefensiveController
{
    private const uint GuardStatusId = 3054;
    private const uint ResilienceStatusId = 3248;
    private const uint StunStatusId = 1343;
    private const uint HeavyStatusId = 1344;
    private const uint BindStatusId = 1345;
    private const uint SilenceStatusId = 1347;
    private const uint MiracleOfNatureStatusId = 3085;

    private static readonly HashSet<uint> RemovableStatusIds =
    [
        StunStatusId,
        HeavyStatusId,
        BindStatusId,
        SilenceStatusId,
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

    public DefensiveEvaluation Evaluate(GameStateSnapshot game, Configuration config)
    {
        var local = game.LocalPlayer;
        if (local is null)
            return new DefensiveEvaluation([], ["Defense: local player unavailable."], 0);

        var candidates = new List<NativeActionCandidate>();
        var rejections = new List<string>();
        var incomingTargeters = game.Enemies.Count(enemy =>
            !enemy.IsDead && enemy.TargetObjectId == local.GameObjectId &&
            HorizontalDistance(enemy.Position, local.Position) <= 30f);
        var nearbyThreats = game.Enemies.Count(enemy =>
            !enemy.IsDead && enemy.IsTargetable && HorizontalDistance(enemy.Position, local.Position) <= 18f);
        var incomingThreats = Math.Max(incomingTargeters, nearbyThreats);
        var selfTarget = (local.GameObjectId, local.EntityId);

        var removable = local.Statuses.FirstOrDefault(status =>
            RemovableStatusIds.Contains(status.Id) || RemovableStatusNames.Contains(status.Name));
        var resilient = local.HasStatus(ResilienceStatusId) || local.HasStatusExact("Resilience");
        if (removable is not null && !resilient)
        {
            if (local.CurrentMp >= 2000)
            {
                candidates.Add(Self(
                    PvPCommonActions.Purify,
                    "Purify",
                    selfTarget,
                    $"Remove supported PvP control effect {removable.Name} ({removable.RemainingSeconds:F1}s); Resilience is absent."));
            }
            else
            {
                rejections.Add($"Purify rejected: {removable.Name} is present but MP {local.CurrentMp} is below 2000.");
            }
        }
        else if (removable is not null)
        {
            rejections.Add($"Purify rejected: {removable.Name} is present but Resilience immunity is active.");
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
        var dangerousHp = local.HpPercent <= config.NativeGuardHpPercent;
        var meaningfulPressure = incomingTargeters >= config.NativeGuardMinimumThreats ||
                                 nearbyThreats >= config.NativeGuardMinimumThreats;
        if (dangerousHp && meaningfulPressure && !guarding)
        {
            candidates.Add(Self(
                PvPCommonActions.Guard,
                "Guard",
                selfTarget,
                $"Conservative Guard: HP {local.HpPercent:F1}% <= {config.NativeGuardHpPercent:F1}% with {incomingTargeters} enemy targeter(s) and {nearbyThreats} enemy threat(s) inside 18y."));
        }
        else
        {
            var reason = guarding
                ? "Guard rejected: Guard is already active."
                : !dangerousHp
                    ? $"Guard rejected: HP {local.HpPercent:F1}% is above the {config.NativeGuardHpPercent:F1}% danger threshold."
                    : $"Guard rejected: only {incomingTargeters} targeter(s)/{nearbyThreats} nearby threat(s), below configured pressure.";
            rejections.Add(reason);
        }

        if (recuperate is not null)
            candidates.Add(recuperate);

        rejections.Add("Standard-issue Elixir rejected: first milestone intentionally leaves Elixir for a future verified safe-disengagement evaluator.");
        return new DefensiveEvaluation(candidates, rejections, incomingThreats);
    }

    private static NativeActionCandidate Self(
        uint id,
        string name,
        (ulong ObjectId, uint EntityId) target,
        string reason) =>
        new(NativeActionLayer.EmergencyDefense, id, name, NativeActionTarget.Self, target.ObjectId, target.EntityId, reason);

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
