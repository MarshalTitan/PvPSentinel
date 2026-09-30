using System.Numerics;

namespace PvPSentinel.Models;

// The policy test deliberately supplies only the frame inputs it reads.
internal sealed record TestPilotPlayer(Vector3 Position, bool IsDead);

internal sealed record GameStateSnapshot(
    DateTime CapturedAtUtc,
    FrontlineMap FrontlineMap,
    TestPilotPlayer? LocalPlayer,
    bool IsClassificationReliable,
    string ClassificationReliabilityExplanation = "Team unavailable");

internal sealed record FriendlyCluster(Vector3 Center, Vector3 MovementTrend, int PlayerCount);
