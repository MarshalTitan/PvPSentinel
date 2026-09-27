using System.Numerics;

namespace PvPSentinel.FrontlineCore;

internal static class BattlefieldClusterer
{
    public static IReadOnlyList<BattlefieldCluster> Build(
        IReadOnlyList<TrackedPlayer> players,
        BattlefieldRelationship relationship,
        Vector3 localPosition,
        float linkRadius,
        IReadOnlyList<MapObjectiveState> objectives)
    {
        var candidates = players
            .Where(player => player.Relationship == relationship && player.IsFresh && !player.IsDead)
            .OrderBy(player => player.EntityId)
            .ToArray();
        if (candidates.Length == 0)
            return [];

        var visited = new bool[candidates.Length];
        var groups = new List<List<TrackedPlayer>>();
        for (var start = 0; start < candidates.Length; start++)
        {
            if (visited[start])
                continue;

            var group = new List<TrackedPlayer>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(candidates[current]);
                for (var next = 0; next < candidates.Length; next++)
                {
                    if (visited[next] || HorizontalDistance(candidates[current].Position, candidates[next].Position) > linkRadius)
                        continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            groups.Add(group);
        }

        return groups
            .Select(group =>
            {
                var centroid = Average(group.Select(player => player.Position));
                var nearestObjective = objectives
                    .Where(objective => objective.ReferencePosition is not null)
                    .Select(objective => (Objective: objective, Distance: HorizontalDistance(centroid, objective.ReferencePosition!.Value)))
                    .OrderBy(item => item.Distance)
                    .FirstOrDefault();
                var allies = players.Count(player => player.Relationship == BattlefieldRelationship.AllyConfirmed && player.IsFresh &&
                    HorizontalDistance(player.Position, centroid) <= 25f);
                var enemies = players.Count(player => player.Relationship == BattlefieldRelationship.EnemyConfirmed && player.IsFresh &&
                    HorizontalDistance(player.Position, centroid) <= 25f);
                return new BattlefieldCluster(
                    0,
                    relationship,
                    centroid,
                    group.Count,
                    group.Average(player => player.StalenessSeconds),
                    group.Max(player => player.StalenessSeconds),
                    group.Count(player => player.IsInCombat),
                    HorizontalDistance(localPosition, centroid),
                    allies,
                    enemies,
                    nearestObjective.Objective is not null && nearestObjective.Distance <= 35f
                        ? nearestObjective.Objective.LogicalId
                        : "NONE",
                    group.Select(player => player.EntityId).Order().ToArray());
            })
            .OrderByDescending(cluster => cluster.MemberCount)
            .ThenBy(cluster => cluster.Centroid.X)
            .ThenBy(cluster => cluster.Centroid.Z)
            .Select((cluster, index) => cluster with { Id = index + 1 })
            .ToArray();
    }

    private static Vector3 Average(IEnumerable<Vector3> positions)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var position in positions)
        {
            sum += position;
            count++;
        }
        return count == 0 ? Vector3.Zero : sum / count;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
