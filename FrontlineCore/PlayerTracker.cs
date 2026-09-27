using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore;

internal sealed class PlayerTracker
{
    private readonly Dictionary<uint, Track> tracks = [];

    public IReadOnlyList<TrackedPlayer> Update(
        DateTime now,
        uint localEntityId,
        byte localPvpTeam,
        bool battalionValuesAuthoritative,
        IEnumerable<PlayerSnapshot> observations)
    {
        foreach (var player in observations)
        {
            if (player.EntityId == 0)
                continue;

            var classified = TeamClassifier.Classify(
                player.EntityId,
                localEntityId,
                player.PvPTeam,
                localPvpTeam,
                battalionValuesAuthoritative);
            if (!tracks.TryGetValue(player.EntityId, out var track))
            {
                track = new Track(now);
                tracks[player.EntityId] = track;
            }

            track.LastSeenUtc = now;
            track.Snapshot = player;
            track.Relationship = classified.Relationship;
            track.Confidence = classified.Confidence;
        }

        foreach (var entityId in tracks
                     .Where(pair => PlayerTrackingPolicy.IsExpired(pair.Value.LastSeenUtc, now))
                     .Select(pair => pair.Key)
                     .ToArray())
            tracks.Remove(entityId);

        return tracks
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToSnapshot(pair.Key, now))
            .ToArray();
    }

    public void Reset() => tracks.Clear();

    private sealed class Track(DateTime firstSeenUtc)
    {
        public DateTime FirstSeenUtc { get; } = firstSeenUtc;
        public DateTime LastSeenUtc { get; set; } = firstSeenUtc;
        public PlayerSnapshot Snapshot { get; set; } = null!;
        public BattlefieldRelationship Relationship { get; set; }
        public RelationshipConfidence Confidence { get; set; }

        public TrackedPlayer ToSnapshot(uint entityId, DateTime now)
        {
            var age = Math.Max(0f, (float)(now - LastSeenUtc).TotalSeconds);
            return new TrackedPlayer(
                entityId,
                Snapshot.GameObjectId,
                Snapshot.JobId,
                Snapshot.JobAbbreviation,
                Snapshot.PvPTeam,
                Relationship,
                Confidence,
                Snapshot.Position,
                Snapshot.CurrentHp,
                Snapshot.MaxHp,
                Snapshot.IsDead,
                Snapshot.IsInCombat,
                Snapshot.TargetObjectId,
                FirstSeenUtc,
                LastSeenUtc,
                age);
        }
    }
}
