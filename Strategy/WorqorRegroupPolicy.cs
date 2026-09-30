using System.Numerics;

namespace PvPSentinel.Strategy;

internal sealed record WorqorRegroupCandidate(Vector3 Position, int PlayerCount);

internal static class WorqorRegroupPolicy
{
    // Used once after a death when no fresh Triumph has nearby allied support.
    // A local spawn cluster does not count as regrouping with the field force.
    public static WorqorRegroupCandidate? Choose(
        Vector3 localPosition, IEnumerable<WorqorRegroupCandidate> groups) =>
        groups
            .Select(group => new
            {
                Group = group,
                Distance = Vector2.Distance(new Vector2(localPosition.X, localPosition.Z),
                    new Vector2(group.Position.X, group.Position.Z)),
            })
            .Where(item => item.Group.PlayerCount >= 3 && item.Distance is >= 35f and <= 400f)
            .OrderByDescending(item => item.Group.PlayerCount * 8f - item.Distance * 0.15f)
            .ThenBy(item => item.Distance)
            .Select(item => item.Group)
            .FirstOrDefault();
}
