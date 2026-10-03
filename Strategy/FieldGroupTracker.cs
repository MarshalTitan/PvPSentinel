using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.Strategy;

internal readonly record struct FieldGroupChoice(FriendlyCluster? Cluster, Vector3 Destination);

/// <summary>
/// Observes allied field groups for the supervised pilots. It supplies a destination
/// only when a field group is visible; the map pilots still own objective choices
/// and keep a route committed until their existing arrival/death boundaries.
/// </summary>
internal sealed class FieldGroupTracker
{
    private FrontlineMap map = FrontlineMap.Unknown;
    private Vector3? basePosition;
    private Vector3? center;
    private Vector3 velocity;
    private Vector3? challengerCenter;
    private DateTime challengerSinceUtc = DateTime.MinValue;
    private DateTime sampleUtc = DateTime.MinValue;
    private bool wasDead;

    internal Vector3? SmoothedCenter => center;

    public FieldGroupChoice Update(FrontlineMap currentMap, bool activeMatch, bool preMatch,
        bool reliableTeam, Vector3? playerPosition, bool dead,
        IReadOnlyList<FriendlyCluster> clusters, DateTime now)
    {
        if (currentMap == FrontlineMap.Unknown || currentMap != map)
        {
            Reset();
            map = currentMap;
        }
        if (!activeMatch && !preMatch)
        {
            Reset();
            return default;
        }
        if (preMatch && playerPosition is { } atBase && !dead)
            basePosition ??= atBase;
        if (dead)
        {
            wasDead = true;
            ClearGroup();
            return default;
        }
        if (wasDead && playerPosition is { } atRespawn)
        {
            basePosition = atRespawn;
            wasDead = false;
            ClearGroup();
        }
        if (!activeMatch || !reliableTeam || playerPosition is null)
        {
            ClearGroup();
            return default;
        }

        var local = playerPosition.Value;
        var candidates = clusters.Where(group => group.PlayerCount >= 3 &&
            Distance(local, group.Center) is >= 35f and <= 400f &&
            (basePosition is null || Distance(basePosition.Value, group.Center) >= 50f)).ToArray();
        if (candidates.Length == 0)
        {
            ClearGroup();
            return default;
        }
        var best = candidates.OrderByDescending(group => Score(group, local)).First();
        var incumbent = center is { } previous
            ? candidates.OrderBy(group => Distance(group.Center, previous)).FirstOrDefault()
            : null;
        if (incumbent is not null && Distance(incumbent.Center, center!.Value) > 32f)
            incumbent = null;

        FriendlyCluster selected;
        if (incumbent is null)
        {
            selected = best;
            center = null;
            challengerCenter = null;
        }
        else if (ReferenceEquals(best, incumbent) || Score(best, local) < Score(incumbent, local) + 12f)
        {
            selected = incumbent;
            challengerCenter = null;
        }
        else
        {
            if (challengerCenter is null || Distance(challengerCenter.Value, best.Center) > 28f)
            {
                challengerCenter = best.Center;
                challengerSinceUtc = now;
            }
            else
                challengerCenter = best.Center;
            if (now - challengerSinceUtc >= TimeSpan.FromSeconds(3))
            {
                selected = best;
                center = null;
                challengerCenter = null;
            }
            else
                selected = incumbent;
        }

        if (center is null || sampleUtc == DateTime.MinValue ||
            now - sampleUtc > TimeSpan.FromSeconds(3) ||
            Distance(center.Value, selected.Center) > 32f)
        {
            center = selected.Center;
            velocity = Vector3.Zero;
        }
        else
        {
            var seconds = (float)(now - sampleUtc).TotalSeconds;
            if (seconds > 0f)
            {
                var blend = Math.Clamp(seconds / (1.2f + seconds), 0f, 1f);
                var next = Vector3.Lerp(center.Value, selected.Center, blend);
                var measured = (next - center.Value) / seconds;
                measured.Y = 0f;
                velocity = Vector3.Lerp(velocity, measured, blend);
                if (velocity.Length() > 5f)
                    velocity = Vector3.Normalize(velocity) * 5f;
                center = next;
            }
        }
        sampleUtc = now;
        var lead = velocity.Length() >= 0.8f ? velocity * 1.2f : Vector3.Zero;
        return new FieldGroupChoice(selected, center.Value + lead);
    }

    public void Reset()
    {
        map = FrontlineMap.Unknown;
        basePosition = null;
        wasDead = false;
        ClearGroup();
    }

    private void ClearGroup()
    {
        center = null;
        velocity = Vector3.Zero;
        sampleUtc = DateTime.MinValue;
        challengerCenter = null;
        challengerSinceUtc = DateTime.MinValue;
    }

    private static float Score(FriendlyCluster group, Vector3 local) =>
        group.PlayerCount * 6f - Distance(group.Center, local) * 0.08f;

    private static float Distance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
