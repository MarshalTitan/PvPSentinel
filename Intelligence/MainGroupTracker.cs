using PvPSentinel.Models;

namespace PvPSentinel.Intelligence;

internal sealed class MainGroupTracker
{
    private HashSet<uint> committedMembers = [];
    private DateTime committedAtUtc = DateTime.MinValue;

    public FriendlyCluster? Select(IReadOnlyList<FriendlyCluster> clusters, DateTime now)
    {
        var reliableClusters = clusters.Where(cluster => cluster.PlayerCount >= 2).ToArray();
        if (reliableClusters.Length == 0)
        {
            committedMembers.Clear();
            return null;
        }

        var largest = reliableClusters.OrderByDescending(cluster => cluster.PlayerCount).First();
        var incumbent = reliableClusters
            .Select(cluster => new
            {
                Cluster = cluster,
                Overlap = cluster.Members.Count(member => committedMembers.Contains(member.EntityId)),
            })
            .OrderByDescending(candidate => candidate.Overlap)
            .First();

        FriendlyCluster selected;
        if (committedMembers.Count == 0 || incumbent.Overlap == 0)
        {
            selected = largest;
        }
        else
        {
            var meaningfulAdvantage = Math.Max(3, (int)Math.Ceiling(incumbent.Cluster.PlayerCount * 0.30f));
            var commitmentExpired = now - committedAtUtc >= TimeSpan.FromSeconds(8);
            selected = commitmentExpired && largest.PlayerCount >= incumbent.Cluster.PlayerCount + meaningfulAdvantage
                ? largest
                : incumbent.Cluster;
        }

        var selectedIds = selected.Members.Select(member => member.EntityId).ToHashSet();
        if (!selectedIds.SetEquals(committedMembers))
        {
            committedMembers = selectedIds;
            committedAtUtc = now;
        }

        return selected;
    }
}
