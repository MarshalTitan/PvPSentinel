using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

/// <summary>
/// Keeps shared player/lifecycle/combat sensing active on Frontline maps whose
/// objective adapter has not yet been implemented. It never invents objectives.
/// </summary>
internal sealed class PassiveFrontlineAdapter(FrontlineMap map) : FrontlineMapAdapterBase
{
    public override FrontlineMap Map { get; } = map;
    public override string Name => $"Shared sensors ({Map.DisplayName()}; objectives UNRESOLVED)";
    public override void Reset(DateTime now) => Records.Clear();
    public override IReadOnlyList<ObjectiveChange> Update(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        IReadOnlyList<TrackedPlayer> players) => [];
}
