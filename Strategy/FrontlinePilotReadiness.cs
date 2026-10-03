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
    // Providers without a trustworthy active-state signal cannot own a pilot.
    public bool CanTravel => Enabled && Lifecycle == FrontlineMatchLifecycle.MatchActive &&
        TeamReliable && MeshReady && ProviderLoaded && ProviderActivityObservable && ProviderActive;

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
        $"Provider active: {Yes(ProviderActive)} [{Gate(ProviderActive)}]",
        ..(Provider == CombatProvider.RotationSolverReborn && ProviderLoaded && !ProviderActive
            ? new[]
            {
                "RSR inactive; check its lifecycle settings:",
                "Auto turn on when PvP match starts: ON",
                "Auto turn off when dead in PvP: OFF",
                "Disable automatically during area transitions: OFF",
                "Auto turn off when PvP match ends: ON",
                "Auto turn off after combat: OFF",
                "Set RSR to PvP-specific state when enabled in PvP zone: ON",
            }
            : Array.Empty<string>()),
    ];

    private static string Yes(bool value) => value ? "Yes" : "No";
    private static string Gate(bool value) => value ? "OK" : "FAIL";
}
