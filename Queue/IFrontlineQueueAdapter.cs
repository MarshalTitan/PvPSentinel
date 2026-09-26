using PvPSentinel.Models;

namespace PvPSentinel.Queue;

internal enum QueueClientState
{
    None,
    Pending,
    Queued,
    Ready,
    Other,
}

internal sealed record QueueClientSnapshot(
    QueueClientState State,
    bool DailyFrontlineSelected,
    FrontlineMap DetectedDailyCampaign,
    string DetectionEvidence);

internal interface IFrontlineQueueAdapter
{
    QueueClientSnapshot Capture();
    bool OpenDailyFrontline();
    bool SelectDailyFrontline();
    bool JoinSelectedDailyFrontline();
    bool AcceptReadyDuty();
    bool CancelQueue();
}
