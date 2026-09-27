namespace PvPSentinel.FrontlineCore;

internal static class TeamClassifier
{
    public static (BattlefieldRelationship Relationship, RelationshipConfidence Confidence) Classify(
        uint entityId,
        uint localEntityId,
        byte pvpTeam,
        byte localPvpTeam)
    {
        if (entityId != 0 && entityId == localEntityId)
            return (BattlefieldRelationship.Self, RelationshipConfidence.LocalEntity);

        if (localPvpTeam > 0 && pvpTeam > 0)
        {
            return pvpTeam == localPvpTeam
                ? (BattlefieldRelationship.AllyConfirmed, RelationshipConfidence.PositiveTeam)
                : (BattlefieldRelationship.EnemyConfirmed, RelationshipConfidence.PositiveTeam);
        }

        return (BattlefieldRelationship.Unknown, RelationshipConfidence.Unresolved);
    }
}
