using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using PvPSentinel.Diagnostics;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace PvPSentinel.Integrations;

internal sealed record RotationSolverRebornStatus(
    bool Installed,
    bool Loaded,
    bool IpcAvailable,
    bool AutorotationActive,
    string Version,
    uint NextActionId,
    string NextAction,
    uint NextGcdActionId,
    string NextGcdAction,
    DateTime LastActionSignalUtc,
    string Explanation)
{
    public static RotationSolverRebornStatus Unavailable(string explanation) => new(
        false, false, false, false, "Unknown", 0, "None", 0, "None", DateTime.MinValue, explanation);
}

/// <summary>
/// Read-only integration with RotationSolverReborn. The current Reborn IPC
/// deliberately blocks state-changing calls in PvP, so this adapter only
/// detects installation/runtime state and observes announced actions.
/// </summary>
internal sealed class RotationSolverRebornAdapter : IDisposable
{
    private const string InternalPluginName = "RotationSolver";
    private readonly IDalamudPluginInterface pi;
    private readonly IDataManager data;
    private readonly DevelopmentLogger developmentLog;
    private readonly ICallGateSubscriber<bool> autorotationActive;
    private readonly ICallGateSubscriber<uint, object> nextActionChanged;
    private readonly ICallGateSubscriber<uint, object> nextGcdActionChanged;

    private DateTime nextPluginRefreshUtc = DateTime.MinValue;
    private DateTime nextStatusQueryUtc = DateTime.MinValue;
    private RotationSolverRebornStatus? cachedStatus;
    private bool installed;
    private bool loaded;
    private string version = "Unknown";
    private bool eventsSubscribed;
    private uint nextActionId;
    private uint nextGcdActionId;
    private DateTime lastActionSignalUtc = DateTime.MinValue;

    public RotationSolverRebornAdapter(
        IDalamudPluginInterface pluginInterface,
        IDataManager dataManager,
        DevelopmentLogger developmentLog)
    {
        pi = pluginInterface;
        data = dataManager;
        this.developmentLog = developmentLog;
        autorotationActive = pi.GetIpcSubscriber<bool>("RotationSolverReborn.AutorotationActive");
        nextActionChanged = pi.GetIpcSubscriber<uint, object>("RotationSolverReborn.ActionUpdater.NextActionChanged");
        nextGcdActionChanged = pi.GetIpcSubscriber<uint, object>("RotationSolverReborn.ActionUpdater.NextGCDActionChanged");
        TrySubscribeEvents();
    }

    public RotationSolverRebornStatus GetStatus(DateTime now)
    {
        if (cachedStatus is not null && now < nextStatusQueryUtc)
            return cachedStatus;
        nextStatusQueryUtc = now.AddMilliseconds(250);

        RefreshPluginState(now);
        if (!installed)
            return Cache(RotationSolverRebornStatus.Unavailable("RotationSolverReborn is not installed. The provider remains inactive and PvPSentinel will not assume combat is being handled."));

        if (!loaded)
        {
            return Cache(new RotationSolverRebornStatus(
                true, false, false, false, version, nextActionId, ResolveAction(nextActionId),
                nextGcdActionId, ResolveAction(nextGcdActionId), lastActionSignalUtc,
                "RotationSolverReborn is installed but not loaded."));
        }

        TrySubscribeEvents();
        try
        {
            var active = autorotationActive.InvokeFunc();
            return Cache(new RotationSolverRebornStatus(
                true, true, true, active, version, nextActionId, ResolveAction(nextActionId),
                nextGcdActionId, ResolveAction(nextGcdActionId), lastActionSignalUtc,
                active
                    ? "RotationSolverReborn is loaded and reports autorotation active. PvP control IPC is intentionally not used."
                    : "RotationSolverReborn is loaded and connected, but reports autorotation Off. Travel may continue; verify combat actions during supervised testing."));
        }
        catch (Exception ex)
        {
            developmentLog.Throttled(
                "reborn-status-ipc-failure",
                $"RotationSolverReborn AutorotationActive IPC threw {ex.GetType().Name}: {ex.Message}");
            return Cache(new RotationSolverRebornStatus(
                true, true, false, false, version, nextActionId, ResolveAction(nextActionId),
                nextGcdActionId, ResolveAction(nextGcdActionId), lastActionSignalUtc,
                $"RotationSolverReborn is loaded, but its read-only status IPC is unavailable ({ex.GetType().Name})."));
        }
    }

    public void Dispose()
    {
        if (!eventsSubscribed)
            return;

        try
        {
            nextActionChanged.Unsubscribe(OnNextActionChanged);
            nextGcdActionChanged.Unsubscribe(OnNextGcdActionChanged);
        }
        catch (Exception ex)
        {
            developmentLog.Throttled(
                "reborn-event-unsubscribe-failure",
                $"RotationSolverReborn event unsubscription threw {ex.GetType().Name}: {ex.Message}");
        }
        eventsSubscribed = false;
    }

    private void RefreshPluginState(DateTime now)
    {
        if (now < nextPluginRefreshUtc)
            return;
        nextPluginRefreshUtc = now.AddSeconds(1);

        try
        {
            var plugin = pi.InstalledPlugins.FirstOrDefault(candidate =>
                candidate.InternalName.Equals(InternalPluginName, StringComparison.OrdinalIgnoreCase) ||
                candidate.InternalName.Equals("RotationSolverReborn", StringComparison.OrdinalIgnoreCase) ||
                candidate.Name.Equals("Rotation Solver Reborn", StringComparison.OrdinalIgnoreCase));

            installed = plugin is not null;
            loaded = plugin?.IsLoaded == true;
            version = plugin?.Version.ToString() ?? "Unknown";
            developmentLog.Changed(
                "reborn-plugin-state",
                $"{installed}|{loaded}|{version}",
                $"RotationSolverReborn detection: installed={installed}, loaded={loaded}, version={version}.");
        }
        catch (Exception ex)
        {
            installed = false;
            loaded = false;
            version = "Unknown";
            developmentLog.Throttled(
                "reborn-plugin-detection-failure",
                $"RotationSolverReborn plugin detection threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TrySubscribeEvents()
    {
        if (eventsSubscribed)
            return;

        try
        {
            nextActionChanged.Subscribe(OnNextActionChanged);
            nextGcdActionChanged.Subscribe(OnNextGcdActionChanged);
            eventsSubscribed = true;
        }
        catch (Exception ex)
        {
            developmentLog.Throttled(
                "reborn-event-subscribe-failure",
                $"RotationSolverReborn action-event subscription threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnNextActionChanged(uint actionId)
    {
        nextActionId = actionId;
        lastActionSignalUtc = DateTime.UtcNow;
        developmentLog.Changed(
            "reborn-next-action",
            actionId.ToString(),
            $"RotationSolverReborn announced next action {ResolveAction(actionId)} ({actionId}). This is a decision signal, not execution confirmation.");
    }

    private RotationSolverRebornStatus Cache(RotationSolverRebornStatus status)
    {
        cachedStatus = status;
        return status;
    }

    private void OnNextGcdActionChanged(uint actionId)
    {
        nextGcdActionId = actionId;
        lastActionSignalUtc = DateTime.UtcNow;
        developmentLog.Changed(
            "reborn-next-gcd",
            actionId.ToString(),
            $"RotationSolverReborn announced next GCD {ResolveAction(actionId)} ({actionId}). This is a decision signal, not execution confirmation.");
    }

    private string ResolveAction(uint actionId)
    {
        if (actionId == 0)
            return "None";

        try
        {
            var row = data.GetExcelSheet<LuminaAction>().GetRow(actionId);
            var name = row.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"Action {actionId}" : name;
        }
        catch
        {
            return $"Action {actionId}";
        }
    }
}
