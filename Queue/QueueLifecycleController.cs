using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Queue;

internal sealed class QueueLifecycleController : IDisposable
{
    private readonly IFrontlineQueueAdapter adapter;
    private readonly IDutyState dutyState;
    private readonly DevelopmentLogger developmentLog;
    private DateTime nextActionUtc = DateTime.MinValue;
    private bool lastSnapshotWasFrontline;
    private bool ownsQueueSession;
    private bool matchCompletedAwaitingExit;

    public QueueLifecycleController(
        IFrontlineQueueAdapter adapter,
        IDutyState dutyState,
        DevelopmentLogger developmentLog)
    {
        this.adapter = adapter;
        this.dutyState = dutyState;
        this.developmentLog = developmentLog;
        dutyState.DutyCompleted += OnDutyCompleted;
    }

    public int CompletedMatches { get; private set; }
    public bool EmergencyStopLatched { get; private set; }

    public QueueDecision Update(GameStateSnapshot game, Configuration config)
    {
        lastSnapshotWasFrontline = game.IsFrontline;
        var limit = Math.Clamp(config.MatchLimit, 1, 100);
        if (EmergencyStopLatched)
            return Decision(QueueLifecycleState.EmergencyStopped, FrontlineMap.Unknown, limit,
                "Emergency stop is latched. Queueing, owned navigation, and combat remain disabled until explicitly cleared.");

        if (!config.Enabled)
            return Decision(QueueLifecycleState.Disabled, FrontlineMap.Unknown, limit,
                "Master enable is off; queue lifecycle actions are disabled.");

        if (game.IsFrontline)
        {
            ownsQueueSession = false;
            if (matchCompletedAwaitingExit)
                return Decision(QueueLifecycleState.LeavingMatch, game.FrontlineMap, limit,
                    $"Frontline completion recorded on {game.FrontlineMap.DisplayName()}; waiting for the game's natural duty exit.");
            return Decision(QueueLifecycleState.InMatch, game.FrontlineMap, limit,
                $"Frontline match active on {game.FrontlineMap.DisplayName()}.");
        }

        if (matchCompletedAwaitingExit)
        {
            matchCompletedAwaitingExit = false;
            return Decision(QueueLifecycleState.Loading, FrontlineMap.Unknown, limit,
                "Frontline duty exit observed; waiting for the post-match requeue cooldown.");
        }

        if (!config.QueueAutomationEnabled)
            return Decision(QueueLifecycleState.Disabled, FrontlineMap.Unknown, limit,
                "Automatic Frontline lifecycle is disabled.");

        if (CompletedMatches >= limit)
            return Decision(QueueLifecycleState.MatchLimitReached, FrontlineMap.Unknown, limit,
                $"Configured match limit reached ({CompletedMatches}/{limit}); no requeue will occur.");

        if (!game.IsLoggedIn || game.IsBetweenAreas || game.IsBoundByDuty)
            return Decision(QueueLifecycleState.Loading, FrontlineMap.Unknown, limit,
                "Waiting for a stable logged-in, out-of-duty state before queue activity.");

        var client = adapter.Capture();
        if (client.State == QueueClientState.Ready)
        {
            if (!ownsQueueSession)
                return Decision(QueueLifecycleState.DutyReady, client.DetectedDailyCampaign, limit,
                    "A duty is ready, but PvPSentinel did not create this queue session; it will not accept an unrelated or pre-existing queue.");

            if (game.CapturedAtUtc >= nextActionUtc && adapter.AcceptReadyDuty())
            {
                nextActionUtc = game.CapturedAtUtc.AddSeconds(3);
                return Decision(QueueLifecycleState.AcceptingDuty, client.DetectedDailyCampaign, limit,
                    "Duty Ready was detected and the Commence action was accepted.");
            }

            return Decision(QueueLifecycleState.DutyReady, client.DetectedDailyCampaign, limit,
                "Duty Ready is visible; waiting for the Commence control to become safely available.");
        }

        if (client.State is QueueClientState.Pending or QueueClientState.Queued)
            return Decision(QueueLifecycleState.Queued, client.DetectedDailyCampaign, limit,
                $"Daily Frontline queue is active ({client.State}).");

        if (client.DetectedDailyCampaign == FrontlineMap.Unknown)
        {
            if (game.CapturedAtUtc >= nextActionUtc)
            {
                adapter.OpenDailyFrontline();
                nextActionUtc = game.CapturedAtUtc.AddSeconds(2);
            }

            return Decision(QueueLifecycleState.InspectingDailyCampaign, FrontlineMap.Unknown, limit,
                $"Inspecting the client-exposed Daily Frontline campaign. {client.DetectionEvidence} No queue action is allowed until exactly one map is identified.");
        }

        if (!config.IsMapAllowed(client.DetectedDailyCampaign))
            return Decision(QueueLifecycleState.DailyCampaignBlocked, client.DetectedDailyCampaign, limit,
                $"Today's campaign is {client.DetectedDailyCampaign.DisplayName()}, which is unchecked. PvPSentinel will not queue today.");

        if (!client.DailyFrontlineSelected)
        {
            if (game.CapturedAtUtc >= nextActionUtc)
            {
                adapter.SelectDailyFrontline();
                nextActionUtc = game.CapturedAtUtc.AddSeconds(1);
            }

            return Decision(QueueLifecycleState.ReadyToQueue, client.DetectedDailyCampaign, limit,
                $"Today's allowed campaign is {client.DetectedDailyCampaign.DisplayName()}; selecting Daily Challenge: Frontline.");
        }

        if (game.CapturedAtUtc >= nextActionUtc && adapter.JoinSelectedDailyFrontline())
        {
            ownsQueueSession = true;
            nextActionUtc = game.CapturedAtUtc.AddSeconds(3);
            return Decision(QueueLifecycleState.JoiningQueue, client.DetectedDailyCampaign, limit,
                $"Submitted the Daily Challenge: Frontline queue for {client.DetectedDailyCampaign.DisplayName()}.");
        }

        return Decision(QueueLifecycleState.ReadyToQueue, client.DetectedDailyCampaign, limit,
            "Daily Frontline is selected; waiting for the Join control to become safely available.");
    }

    public void EmergencyStop()
    {
        EmergencyStopLatched = true;
        if (ownsQueueSession)
        {
            var cancelled = adapter.CancelQueue();
            developmentLog.Changed("queue-emergency-cancel", cancelled.ToString(),
                cancelled
                    ? "Emergency stop cancelled the PvPSentinel-owned queue session."
                    : "Emergency stop could not cancel the owned queue; automatic acceptance remains disabled.");
        }
        ownsQueueSession = false;
        nextActionUtc = DateTime.MaxValue;
    }

    public void ClearEmergencyStop()
    {
        EmergencyStopLatched = false;
        nextActionUtc = DateTime.MinValue;
    }

    public void ResetMatchCounter() => CompletedMatches = 0;

    public void Dispose() => dutyState.DutyCompleted -= OnDutyCompleted;

    private void OnDutyCompleted(IDutyStateEventArgs _)
    {
        if (!lastSnapshotWasFrontline)
            return;
        CompletedMatches++;
        matchCompletedAwaitingExit = true;
        nextActionUtc = DateTime.UtcNow.AddSeconds(8);
        developmentLog.Changed("queue-match-count", CompletedMatches.ToString(),
            $"Frontline completion recorded. Session count is now {CompletedMatches}.");
    }

    private QueueDecision Decision(
        QueueLifecycleState state,
        FrontlineMap campaign,
        int limit,
        string explanation)
    {
        developmentLog.Changed("queue-state", $"{state}|{campaign}|{CompletedMatches}|{limit}|{EmergencyStopLatched}", explanation);
        return new QueueDecision(state, campaign, CompletedMatches, limit, EmergencyStopLatched, explanation);
    }
}
