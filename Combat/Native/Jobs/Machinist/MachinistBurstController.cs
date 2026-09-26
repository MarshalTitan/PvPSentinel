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
    private ulong wildfireTargetId;
    private DateTime wildfireCommittedAtUtc = DateTime.MinValue;
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
            var safe = context.Local.HpPercent >= 45f && !target.IsOverextended;
            var survivalFloor = NativeCombatPolicy.WildfireSurvivalFloor(
                context.Configuration.NativeWildfireMinimumEffectiveHp,
                target.AlliedFocus,
                context.Configuration.NativeWildfireFocusHpPerPlayer,
                context.Configuration.NativeWildfireUncreditedFocus,
                context.Configuration.NativeWildfireMaximumMinimumHp);
            var worthwhile = target.EffectiveHp >= survivalFloor &&
                             !target.IsGuarding &&
                             target.Distance <= 25f;
            if (safe && worthwhile)
            {
                initiation.Add(Enemy(context, MachinistActions.Wildfire, "Wildfire",
                    $"Initiate a deliberate burst transition at {target.Distance:F1}y; target effective HP {target.EffectiveHp:N0} meets survival floor {survivalFloor:N0}, allied focus {target.AlliedFocus}, local HP {context.Local.HpPercent:F1}%."));
            }
            else
            {
                var overkillRisk = target.EffectiveHp < survivalFloor;
                rejections.Add($"Wildfire rejected: safe={safe}, worthwhile={worthwhile}, overkill risk={overkillRisk} (effective HP {target.EffectiveHp:N0} vs survival floor {survivalFloor:N0} from allied focus {target.AlliedFocus}), target Guard={target.IsGuarding}, distance={target.Distance:F1}y.");
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
