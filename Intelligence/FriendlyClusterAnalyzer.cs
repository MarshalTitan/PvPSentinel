using System.Numerics;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Intelligence;

internal sealed class FriendlyClusterAnalyzer(DevelopmentLogger developmentLog)
{
    private readonly Dictionary<uint, PositionSample> previous = new();

    public IReadOnlyList<FriendlyCluster> Analyze(
        IReadOnlyList<PlayerSnapshot> friendlies,
        float linkRadius,
        DateTime capturedAtUtc)
    {
        if (friendlies.Count == 0)
            return [];

        var groups = BuildConnectedComponents(friendlies, Math.Max(5f, linkRadius));
        var clusters = new List<FriendlyCluster>(groups.Count);
        var ordered = groups.OrderByDescending(group => group.Count).ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var members = ordered[index];
            var center = Average(members.Select(member => member.Position));
            var movement = Average(members.Select(member => Velocity(member, capturedAtUtc)));
            var meanDistance = members.Average(member => Vector3.Distance(member.Position, center));
            var density = 1f - Math.Clamp(meanDistance / Math.Max(linkRadius, 1f), 0f, 1f);
            var share = members.Count / (float)friendlies.Count;
            var sizeConfidence = Math.Clamp(members.Count / 12f, 0f, 1f);
            var confidence = Math.Clamp((share * 0.65f) + (sizeConfidence * 0.25f) + (density * 0.10f), 0f, 1f);

            clusters.Add(new FriendlyCluster(index + 1, members, center, movement, density, confidence));
        }

        previous.Clear();
        foreach (var player in friendlies)
            previous[player.EntityId] = new PositionSample(player.Position, capturedAtUtc);

        developmentLog.Throttled(
            "cluster-analysis",
            $"{friendlies.Count} friendlies produced {clusters.Count} clusters: {string.Join(", ", clusters.Select(cluster => $"#{cluster.Id} n={cluster.PlayerCount} confidence={cluster.Confidence:F2} density={cluster.Density:F2}"))}.");

        return clusters;
    }

    private Vector3 Velocity(PlayerSnapshot player, DateTime now)
    {
        if (!previous.TryGetValue(player.EntityId, out var sample))
            return Vector3.Zero;

        var seconds = (float)(now - sample.AtUtc).TotalSeconds;
        if (seconds is < 0.05f or > 3f)
            return Vector3.Zero;

        var velocity = (player.Position - sample.Position) / seconds;
        return velocity.LengthSquared() > 100f ? Vector3.Zero : velocity;
    }

    private static List<List<PlayerSnapshot>> BuildConnectedComponents(
        IReadOnlyList<PlayerSnapshot> players,
        float linkRadius)
    {
        var visited = new bool[players.Count];
        var result = new List<List<PlayerSnapshot>>();

        for (var start = 0; start < players.Count; start++)
        {
            if (visited[start])
                continue;

            var group = new List<PlayerSnapshot>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(players[current]);

                for (var candidate = 0; candidate < players.Count; candidate++)
                {
                    if (visited[candidate])
                        continue;

                    if (HorizontalDistance(players[current].Position, players[candidate].Position) <= linkRadius)
                    {
                        visited[candidate] = true;
                        queue.Enqueue(candidate);
                    }
                }
            }

            result.Add(group);
        }

        return result;
    }

    private static Vector3 Average(IEnumerable<Vector3> vectors)
    {
        var total = Vector3.Zero;
        var count = 0;
        foreach (var vector in vectors)
        {
            total += vector;
            count++;
        }

        return count == 0 ? Vector3.Zero : total / count;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private sealed record PositionSample(Vector3 Position, DateTime AtUtc);
}
