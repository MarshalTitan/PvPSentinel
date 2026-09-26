using System.Numerics;

namespace PvPSentinel.Combat.Native.Jobs.Machinist;

internal sealed record BurstEvaluation(
    IReadOnlyList<NativeActionCandidate> Continuation,
    IReadOnlyList<NativeActionCandidate> Initiation,
    IReadOnlyList<string> Rejections,
    string State);

internal sealed class MachinistBurstController
{
    private static readonly TimeSpan WildfireSequenceTimeout = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan WildfireCooldown = TimeSpan.FromSeconds(24);
    private ulong wildfireTargetId;
    private DateTime wildfireCommittedAtUtc = DateTime.MinValue;
    private DateTime hypotheticalWildfireReadyAtUtc = DateTime.MinValue;
    private int attacksCommitted;
    private string lastExitReason = "not committed";

    public bool IsActive => wildfireTargetId != 0;
    public ulong TargetId => wildfireTargetId;

    public BurstEvaluation Evaluate(NativeCombatContext context)
    {
        var target = context.Target;
        var continuation = new List<NativeActionCandidate>();
        var initiation = new List<NativeActionCandidate>();
        var rejections = new List<string>();

        // Shadow mode can observe a real Wildfire sequence without any IPC: the
        // local and target statuses are ordinary public game state.
        if (!IsActive && context.Local.HasStatus(1946))
        {
            var observedTarget = context.Game.Enemies.FirstOrDefault(enemy => enemy.HasStatus(1323));
            if (observedTarget is not null)
            {
                wildfireTargetId = observedTarget.GameObjectId;
                wildfireCommittedAtUtc = context.Game.CapturedAtUtc;
                attacksCommitted = 0;
                lastExitReason = "observed from public Wildfire statuses";
            }
        }

        if (IsActive)
        {
            var burstTarget = context.Game.Enemies.FirstOrDefault(enemy => enemy.GameObjectId == wildfireTargetId);
            var age = context.Game.CapturedAtUtc - wildfireCommittedAtUtc;
            var burstDistance = burstTarget is null
                ? float.MaxValue
                : HorizontalDistance(context.Local.Position, burstTarget.Position);
            var burstSupport = burstTarget is null
                ? 0
                : context.Game.Friendlies.Count(ally => !ally.IsDead && HorizontalDistance(ally.Position, burstTarget.Position) <= 25f);
            var burstThreats = burstTarget is null
                ? 0
                : context.Game.Enemies.Count(enemy => !enemy.IsDead && HorizontalDistance(enemy.Position, burstTarget.Position) <= 20f);
            var unsafeToContinue = context.Local.HpPercent < 25f ||
                                   burstDistance > context.Configuration.NativeTargetOverextensionRange &&
                                   (burstSupport < 2 || burstThreats > burstSupport + 1);
            if (burstTarget is null || burstTarget.IsDead || !burstTarget.IsTargetable)
                Abandon("Wildfire target became invalid");
            else if (age > WildfireSequenceTimeout)
                Abandon("Wildfire sequence expired");
            else if (unsafeToContinue)
                Abandon($"continuation unsafe at {context.Local.HpPercent:F1}% HP/overextension");
            else if (target?.Player.GameObjectId == wildfireTargetId)
            {
                continuation.Add(Enemy(context, MachinistActions.FullMetalField, "Full Metal Field",
                    $"Continue committed Wildfire on the remembered target; prioritize Full Metal Field ({attacksCommitted}/4 follow-up attacks observed)."));
            }
            else
            {
                rejections.Add("Wildfire continuation held: native target selection no longer favors the remembered Wildfire target.");
            }
        }

        if (!IsActive && target is not null)
        {
            var hypotheticalCooldownRemaining = hypotheticalWildfireReadyAtUtc - context.Game.CapturedAtUtc;
            var safe = context.Local.HpPercent >= 45f && !target.IsOverextended;
            var worthwhile = target.Player.CurrentHp >= 18000 && !target.IsGuarding && target.Distance <= 25f;
            if (hypotheticalCooldownRemaining > TimeSpan.Zero)
            {
                rejections.Add($"Wildfire rejected: the Shadow simulation already committed it and preserves the {WildfireCooldown.TotalSeconds:F0}s recast ({hypotheticalCooldownRemaining.TotalSeconds:F1}s remaining).");
            }
            else if (safe && worthwhile)
            {
                initiation.Add(Enemy(context, MachinistActions.Wildfire, "Wildfire",
                    $"Initiate a deliberate burst transition at {target.Distance:F1}y; target HP {target.Player.CurrentHp:N0}, allied focus {target.AlliedFocus}, local HP {context.Local.HpPercent:F1}%."));
            }
            else
            {
                rejections.Add($"Wildfire rejected: safe={safe}, worthwhile={worthwhile}, target Guard={target.IsGuarding}, distance={target.Distance:F1}y, target HP={target.Player.CurrentHp:N0}.");
            }
        }

        var state = IsActive
            ? $"active on 0x{wildfireTargetId:X16}; attacks {attacksCommitted}/4; age {(context.Game.CapturedAtUtc - wildfireCommittedAtUtc).TotalSeconds:F1}s"
            : $"inactive ({lastExitReason})";
        return new BurstEvaluation(continuation, initiation, rejections, state);
    }

    public void NotifyActionAccepted(NativeActionCandidate action, DateTime now, bool hypothetical)
    {
        if (action.ActionId == MachinistActions.Wildfire)
        {
            wildfireTargetId = action.TargetObjectId;
            wildfireCommittedAtUtc = now;
            if (hypothetical)
                hypotheticalWildfireReadyAtUtc = now + WildfireCooldown;
            attacksCommitted = 0;
            lastExitReason = hypothetical ? "hypothetically committed in Shadow" : "committed";
            return;
        }

        if (!IsActive || action.TargetType != NativeActionTarget.Enemy || action.TargetObjectId != wildfireTargetId)
            return;

        attacksCommitted++;
        if (attacksCommitted >= 4)
            Abandon("four Wildfire follow-up attacks completed");
    }

    public void Reset()
    {
        hypotheticalWildfireReadyAtUtc = DateTime.MinValue;
        Abandon("reset");
    }

    private void Abandon(string reason)
    {
        wildfireTargetId = 0;
        wildfireCommittedAtUtc = DateTime.MinValue;
        attacksCommitted = 0;
        lastExitReason = reason;
    }

    private static NativeActionCandidate Enemy(NativeCombatContext context, uint id, string name, string reason) =>
        new(NativeActionLayer.BurstContinuation, id, name, NativeActionTarget.Enemy,
            context.Target!.Player.GameObjectId, context.Target.Player.EntityId, reason);

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
