using Dalamud.Game;
using Dalamud.Plugin.Services;
using PvPSentinel.Combat.Native.Defense;
using PvPSentinel.Combat.Native.Jobs.Machinist;
using PvPSentinel.Combat.Native.Targeting;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Combat.Native;

internal sealed class NativeCombatProvider : ICombatController
{
    private readonly NativeActionExecutor executor;
    private readonly PvPTargetEvaluator targetEvaluator = new();
    private readonly PvPDefensiveController defense = new();
    private readonly IReadOnlyDictionary<uint, IPvpJobCombatModule> modules;
    private readonly Dictionary<uint, bool> verifiedActions;
    private readonly DevelopmentLogger developmentLog;
    private readonly IPluginLog log;
    private string lastTargetLogSignature = string.Empty;
    private string lastActionLogSignature = string.Empty;

    public NativeCombatProvider(
        IDataManager data,
        NativeActionExecutor executor,
        IEnumerable<IPvpJobCombatModule> modules,
        DevelopmentLogger developmentLog,
        IPluginLog log)
    {
        this.executor = executor;
        this.developmentLog = developmentLog;
        this.log = log;
        this.modules = modules.ToDictionary(module => module.JobId);
        verifiedActions = PvPCommonActions.ExpectedNames
            .Concat(MachinistActions.ExpectedNames)
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => VerifyAction(data, group.Key, group.First().Value));
    }

    public string LastAction { get; private set; } = "None";

    public CombatDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        TargetDecision? strategicTarget,
        Configuration config)
    {
        if (!game.IsFrontline || !game.IsClassificationReliable || game.LocalPlayer is null || game.LocalPlayer.IsDead)
        {
            ResetState();
            return Decision(false, 0, "Native idle", "Native combat safety gate is closed.", null);
        }

        var local = game.LocalPlayer;
        if (!modules.TryGetValue(local.JobId, out var module))
        {
            ResetState();
            return Decision(false, 0, "Unsupported job", $"No native PvP job module is registered for job {local.JobAbbreviation} ({local.JobId}).", null);
        }

        var targetEvaluation = targetEvaluator.Evaluate(game, config, module.CommittedTargetId);
        LogTargetDecision(targetEvaluation, local, config.NativeCombatMode);

        var target = targetEvaluation.Selected;
        var marksmanReady = target is not null && executor.Check(new NativeActionCandidate(
            NativeActionLayer.Execute,
            MachinistActions.MarksmansSpite,
            "Marksman's Spite",
            NativeActionTarget.Enemy,
            target.Player.GameObjectId,
            target.Player.EntityId,
            "Limit gauge readiness probe.")).IsReady;
        var actionPermission = !game.IsBetweenAreas && !game.IsMounted && !game.IsMounting;
        var offensePermitted = actionPermission && behavior is (BehaviorState.Engage or BehaviorState.FinishKill);
        var context = new NativeCombatContext(game, local, behavior, target, config, offensePermitted, marksmanReady);
        var defensive = defense.Evaluate(game, config);
        var job = module.Evaluate(context);
        var rejected = new List<string>(defensive.Rejections);
        rejected.AddRange(job.Rejections);
        if (!actionPermission)
            rejected.Insert(0, $"Combat permission rejected: between areas={game.IsBetweenAreas}, mounted={game.IsMounted}, mounting={game.IsMounting}.");

        var ordered = defensive.Candidates.Concat(job.Candidates);
        NativeActionCandidate? chosen = null;
        foreach (var candidate in actionPermission ? ordered : [])
        {
            if (!verifiedActions.GetValueOrDefault(candidate.ActionId))
            {
                rejected.Add($"{candidate.ActionName} rejected: action ID {candidate.ActionId} failed local data verification.");
                continue;
            }

            if (candidate.TargetType == NativeActionTarget.Enemy &&
                target?.IsGuarding == true &&
                candidate.ActionId != MachinistActions.Drill)
            {
                rejected.Add($"{candidate.ActionName} rejected: target is Guarding; only verified Guard-bypassing Drill remains eligible.");
                continue;
            }

            var availability = executor.Check(candidate);
            if (!availability.IsReady)
            {
                rejected.Add($"{candidate.ActionName} unavailable: {availability.Explanation}.");
                continue;
            }

            chosen = candidate;
            break;
        }

        var limitState = game.LimitBreakBarUnits == 0
            ? $"{game.LimitBreakCurrentUnits} units; maximum unavailable; action ready={marksmanReady}"
            : $"{game.LimitBreakCurrentUnits}/{game.LimitBreakBarUnits} units ({game.LimitBreakPercent:F1}%); bars={game.LimitBreakBarCount}; action ready={marksmanReady}";
        var diagnostics = BuildDiagnostics(
            config.NativeCombatMode,
            job,
            targetEvaluation,
            local,
            limitState,
            chosen,
            rejected);

        if (chosen is null)
        {
            lastActionLogSignature = string.Empty;
            var holdReason = offensePermitted
                ? "No verified and client-ready action passed the ordered native decision layers."
                : "Only emergency defense is permitted in the current strategic behavior; no defensive action is ready.";
            return Decision(true, 0, "Hold", holdReason, diagnostics);
        }

        if (config.NativeCombatMode == NativeCombatMode.ShadowObserve)
        {
            LogActionDecision(chosen, shadow: true);
            diagnostics = diagnostics with { ActionResolution = $"WOULD USE {chosen.ActionName}: {chosen.Reason}" };
            return Decision(true, chosen.ActionId, $"WOULD USE {chosen.ActionName}", chosen.Reason, diagnostics);
        }

        var accepted = executor.TryExecute(chosen);
        if (accepted)
        {
            LastAction = chosen.ActionName;
            module.NotifyActionAccepted(chosen, game.CapturedAtUtc, hypothetical: false);
            LogActionDecision(chosen, shadow: false);
            diagnostics = diagnostics with { ActionResolution = $"USED {chosen.ActionName}: client accepted the action." };
            return Decision(true, chosen.ActionId, chosen.ActionName, chosen.Reason + " Client accepted the action.", diagnostics);
        }

        diagnostics = diagnostics with { ActionResolution = $"ATTEMPT FAILED {chosen.ActionName}: client did not accept the action." };
        return Decision(true, chosen.ActionId, chosen.ActionName, chosen.Reason + " Client did not accept the action attempt.", diagnostics);
    }

    private NativeCombatDiagnostics BuildDiagnostics(
        NativeCombatMode mode,
        JobCombatEvaluation job,
        TargetEvaluation targetEvaluation,
        PlayerSnapshot local,
        string limitState,
        NativeActionCandidate? chosen,
        IReadOnlyList<string> rejections)
    {
        var target = targetEvaluation.Selected;
        var runnerUp = targetEvaluation.RunnerUp;
        return new NativeCombatDiagnostics(
            mode,
            job.CombatState,
            target?.Player.GameObjectId ?? 0,
            target is null
                ? "None"
                : $"{(mode == NativeCombatMode.ShadowObserve ? "WOULD TARGET " : string.Empty)}{target.Player.JobAbbreviation} - {target.Player.Name}",
            target?.Score.Total ?? 0f,
            runnerUp is null ? "None" : $"{runnerUp.Player.JobAbbreviation} - {runnerUp.Player.Name}: {runnerUp.Score.Total:F1}",
            target is null ? targetEvaluation.SelectionReason : targetEvaluation.SelectionReason + " " + target.Score.Summary,
            $"HP {local.CurrentHp:N0}/{local.MaxHp:N0} ({local.HpPercent:F1}%); MP {local.CurrentMp:N0}/{local.MaxMp:N0}",
            target is null ? "None" : $"{target.Player.CurrentHp:N0}/{target.Player.MaxHp:N0} ({target.Player.HpPercent:F1}%); shield {target.Player.ShieldPercent}%",
            target?.IsGuarding ?? false,
            target?.AlliedFocus ?? 0,
            job.ToolState,
            job.WildfireState,
            job.Overheated,
            limitState,
            chosen is null ? "HOLD" : $"SELECTED {chosen.Layer}: {chosen.ActionName}",
            rejections.Take(12).ToArray());
    }

    private void LogTargetDecision(TargetEvaluation evaluation, PlayerSnapshot local, NativeCombatMode mode)
    {
        if (evaluation.Selected is null)
            return;

        var target = evaluation.Selected;
        var shadowPrefix = mode == NativeCombatMode.ShadowObserve ? "WOULD TARGET" : "SELECTED TARGET";
        var differsFromGameTarget = local.TargetObjectId != target.Player.GameObjectId;
        var signature = $"{mode}|{target.Player.GameObjectId}|{differsFromGameTarget}";
        developmentLog.Changed("native-target", signature,
            $"{shadowPrefix} {target.Player.Name} ({target.Player.JobAbbreviation}) score {target.Score.Total:F1}; current game target differs={differsFromGameTarget}. {evaluation.SelectionReason}");
        if (signature != lastTargetLogSignature)
        {
            log.Information("PvPSentinel native {Decision}: {Target} ({Job}), score {Score:F1}. {Reason}",
                shadowPrefix, target.Player.Name, target.Player.JobAbbreviation, target.Score.Total, evaluation.SelectionReason);
            lastTargetLogSignature = signature;
        }
    }

    private void LogActionDecision(NativeActionCandidate action, bool shadow)
    {
        var label = shadow ? "WOULD USE" : "USED";
        var signature = $"{shadow}|{action.ActionId}|{action.TargetObjectId}";
        developmentLog.Changed("native-action", signature, $"{label} {action.ActionName}. {action.Reason}");
        if (signature == lastActionLogSignature)
            return;

        log.Information("PvPSentinel native {Decision} {Action} ({ActionId}). {Reason}",
            label, action.ActionName, action.ActionId, action.Reason);
        lastActionLogSignature = signature;
    }

    private void ResetState()
    {
        targetEvaluator.Reset();
        foreach (var module in modules.Values)
            module.Reset();
        lastTargetLogSignature = string.Empty;
        lastActionLogSignature = string.Empty;
    }

    private bool VerifyAction(IDataManager data, uint id, string expectedName)
    {
        var row = data.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English).FirstOrDefault(action => action.RowId == id);
        var actualName = row.RowId == 0 ? string.Empty : row.Name.ToString();
        var valid = actualName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        if (!valid)
            log.Warning("Native PvP action {ActionId} failed verification. Expected '{Expected}', local data says '{Actual}'.", id, expectedName, actualName);
        return valid;
    }

    private static CombatDecision Decision(
        bool active,
        uint actionId,
        string actionName,
        string explanation,
        NativeCombatDiagnostics? diagnostics) =>
        new(CombatProvider.NativePvPSentinel, active, false, actionId, actionName, explanation, diagnostics);
}
