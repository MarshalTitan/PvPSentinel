using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Intelligence;

internal sealed class MainGroupTracker(DevelopmentLogger developmentLog)
{
    private HashSet<uint> committedMembers = [];
    private DateTime committedAtUtc = DateTime.MinValue;

    public FriendlyCluster? Select(IReadOnlyList<FriendlyCluster> clusters, DateTime now)
    {
        var reliableClusters = clusters.Where(cluster => cluster.PlayerCount >= 2).ToArray();
        if (reliableClusters.Length == 0)
        {
            committedMembers.Clear();
            committedAtUtc = DateTime.MinValue;
            developmentLog.Changed("main-group", "none", "No cluster with at least two friendly players is available.");
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
        string selectionReason;
        if (committedMembers.Count == 0 || incumbent.Overlap == 0)
        {
            selected = largest;
            selectionReason = committedMembers.Count == 0
                ? "No incumbent group; selected the largest reliable cluster."
                : "No overlap with the incumbent group; reacquired the largest reliable cluster.";
        }
        else
        {
            var meaningfulAdvantage = Math.Max(3, (int)Math.Ceiling(incumbent.Cluster.PlayerCount * 0.30f));
            var commitmentExpired = now - committedAtUtc >= TimeSpan.FromSeconds(8);
            var shouldSwitch = commitmentExpired && largest.PlayerCount >= incumbent.Cluster.PlayerCount + meaningfulAdvantage;
            selected = shouldSwitch ? largest : incumbent.Cluster;
            selectionReason = shouldSwitch
                ? $"Switched after commitment expired: largest cluster {largest.PlayerCount} vs incumbent {incumbent.Cluster.PlayerCount}, required advantage {meaningfulAdvantage}."
                : $"Retained incumbent: overlap {incumbent.Overlap}, largest {largest.PlayerCount}, incumbent {incumbent.Cluster.PlayerCount}, commitment expired={commitmentExpired}, required advantage {meaningfulAdvantage}.";
        }

        var selectedIds = selected.Members.Select(member => member.EntityId).ToHashSet();
        if (!selectedIds.SetEquals(committedMembers))
        {
            committedMembers = selectedIds;
            committedAtUtc = now;
        }

        var signature = string.Join(",", selectedIds.OrderBy(id => id));
        developmentLog.Changed(
            "main-group",
            signature,
            $"Selected cluster #{selected.Id} with {selected.PlayerCount} players, confidence {selected.Confidence:F2}. {selectionReason}");
        developmentLog.Throttled(
            "main-group-evaluation",
            $"Cluster #{selected.Id}, {selected.PlayerCount} players, confidence {selected.Confidence:F2}. {selectionReason}");

        return selected;
    }
}
