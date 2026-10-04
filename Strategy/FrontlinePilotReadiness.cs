using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;

namespace PvPSentinel.Strategy;

internal sealed record FrontlinePilotReadiness(
    bool Enabled,
    FrontlineMatchLifecycle Lifecycle,
    bool TeamReliable,
    bool MeshReady,
    CombatProvider Provider,
    bool ProviderInstalled,
    bool ProviderLoaded,
    bool ProviderActivityObservable,
    bool ProviderActive)
{
    // A live, queryable provider connection is the travel gate. RSR's mode
    // signal is displayed separately: it does not prove whether combat actions
    // were executed, and its Off state is not a navigation stop request.
    public bool CanTravel => Enabled && Lifecycle == FrontlineMatchLifecycle.MatchActive &&
        TeamReliable && MeshReady && ProviderLoaded && ProviderActivityObservable;

    public IReadOnlyList<string> Lines =>
    [
        $"Pilot enabled: {Yes(Enabled)}",
        $"Match lifecycle: {Lifecycle} [{Gate(Lifecycle == FrontlineMatchLifecycle.MatchActive)}]",
        $"Team classification: {(TeamReliable ? "Reliable" : "Unreliable")} [{Gate(TeamReliable)}]",
        $"vnavmesh: {(MeshReady ? "Ready" : "Unavailable/loading")} [{Gate(MeshReady)}]",
        $"Combat provider: {Provider}",
        $"Provider installed: {Yes(ProviderInstalled)}",
        $"Provider loaded: {Yes(ProviderLoaded)} [{Gate(ProviderLoaded)}]",
        $"Provider activity observable: {Yes(ProviderActivityObservable)} [{Gate(ProviderActivityObservable)}]",
        $"Provider reports autorotation active: {Yes(ProviderActive)} [diagnostic; action execution unverified]",
        ..(Provider == CombatProvider.RotationSolverReborn && ProviderLoaded && !ProviderActive
            ? new[]
            {
                "RSR reports Off; travel is permitted, but confirm combat actions in a supervised match.",
                "If RSR does not act, review its PvP auto-start, death, transition, match-end and after-combat settings.",
            }
            : Array.Empty<string>()),
    ];

    private static string Yes(bool value) => value ? "Yes" : "No";
    private static string Gate(bool value) => value ? "OK" : "FAIL";
}
