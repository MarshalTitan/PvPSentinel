using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using PvPSentinel.Models;

namespace PvPSentinel.Combat;

internal sealed class MachinistPvpCombatController : ICombatController
{
    private readonly NativeActionExecutor executor;
    private readonly Dictionary<uint, bool> verifiedActions;

    public MachinistPvpCombatController(IDataManager data, NativeActionExecutor executor, IPluginLog log)
    {
        this.executor = executor;
        verifiedActions = MachinistPvpActions.ExpectedNames.ToDictionary(
            pair => pair.Key,
            pair => VerifyAction(data, pair.Key, pair.Value, log));
    }

    public string LastAction { get; private set; } = "None";

    public CombatDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        TargetDecision? target,
        Configuration config)
    {
        if (!game.IsMachinist)
            return new CombatDecision(false, 0, "None", "Unsupported PvP job; combat is disabled.");

        if (!game.IsFrontline || !game.IsClassificationReliable || game.LocalPlayer is null || game.LocalPlayer.IsDead)
            return new CombatDecision(false, 0, "None", "Combat safety gate is closed.");

        if (behavior is not BehaviorState.Engage and not BehaviorState.FinishKill || target is null)
            return new CombatDecision(true, 0, "Hold", "MCH controller is active, but the behavior engine did not authorize combat.");

        var (actionId, explanation) = ChooseAction(game.LocalPlayer, target, behavior);
        var actionName = ActionName(actionId);
        if (actionId == 0 || !verifiedActions.GetValueOrDefault(actionId))
            return new CombatDecision(true, 0, actionName, explanation + " Action ID is unavailable or failed local data verification.");

        if (!config.CombatEnabled)
            return new CombatDecision(true, actionId, actionName, explanation + " Execution is disabled in configuration.");

        if (!executor.IsReady(actionId, target.Target.GameObjectId))
            return new CombatDecision(true, actionId, actionName, explanation + " Waiting for range/cooldown/action availability.");

        if (executor.TryExecute(actionId, target.Target.EntityId))
        {
            LastAction = actionName;
            return new CombatDecision(true, actionId, actionName, explanation + " Action accepted by the client.");
        }

        return new CombatDecision(true, actionId, actionName, explanation + " The client did not accept the action attempt.");
    }

    private static (uint Id, string Explanation) ChooseAction(
        PlayerSnapshot local,
        TargetDecision target,
        BehaviorState behavior)
    {
        if (target.Target.HasStatusExact("Guard"))
            return (0, "Target is Guarding; hold damage until the physical-ranged role action is integrated and verified.");

        if (behavior == BehaviorState.FinishKill && target.IsFinishOpportunity)
            return (MachinistPvpActions.MarksmanSpite, "FinishKill requests the MCH limit break when available.");

        var hasAnalysis = local.HasStatus("Analysis");
        if (!hasAnalysis && (local.HasStatus("Drill Primed") || local.HasStatus("Chain Saw Primed") || local.HasStatus("Air Anchor Primed")))
            return (MachinistPvpActions.Analysis, "A high-value tool is primed; request Analysis first.");

        if (local.HasStatus("Drill Primed"))
            return (MachinistPvpActions.Drill, "Drill is primed.");
        if (local.HasStatus("Air Anchor Primed"))
            return (MachinistPvpActions.AirAnchor, "Air Anchor is primed.");
        if (local.HasStatus("Chain Saw Primed"))
            return (MachinistPvpActions.ChainSaw, "Chain Saw is primed.");
        if (local.HasStatus("Bioblaster Primed"))
            return (MachinistPvpActions.BioBlaster, "Bioblaster is primed.");
        if (local.HasStatus("Overheated"))
            return (MachinistPvpActions.BlazingShot, "Overheated; use the instant follow-up shot.");

        return (MachinistPvpActions.BlastCharge, "Use the baseline ranged GCD while no tool proc is visible.");
    }

    private static bool VerifyAction(IDataManager data, uint id, string expectedName, IPluginLog log)
    {
        var row = data.GetExcelSheet<Lumina.Excel.Sheets.Action>().FirstOrDefault(action => action.RowId == id);
        var actualName = row.RowId == 0 ? string.Empty : row.Name.ToString();
        var valid = actualName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        if (!valid)
            log.Warning("MCH PvP action {ActionId} failed verification. Expected '{Expected}', local data says '{Actual}'.", id, expectedName, actualName);
        return valid;
    }

    private static string ActionName(uint id) =>
        MachinistPvpActions.ExpectedNames.GetValueOrDefault(id, id == 0 ? "Hold" : $"Action {id}");
}
