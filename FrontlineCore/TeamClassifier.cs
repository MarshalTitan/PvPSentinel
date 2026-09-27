namespace PvPSentinel.FrontlineCore;

internal static class TeamClassifier
{
    // Live Dalamud observations on Borderland Ruins show Character.Battalion as
    // a zero-based Frontline team index: the complete local roster was 0 while
    // the two opposing teams were 1 and 2. The surrounding Frontline gate is
    // essential because 0 is also the default value outside this content.
    public static bool IsValidFrontlineBattalion(byte battalion) => battalion <= 2;

    public static (BattlefieldRelationship Relationship, RelationshipConfidence Confidence) Classify(
        uint entityId,
        uint localEntityId,
        byte pvpTeam,
        byte localPvpTeam,
        bool battalionValuesAuthoritative)
    {
        if (entityId != 0 && entityId == localEntityId)
            return (BattlefieldRelationship.Self, RelationshipConfidence.LocalEntity);

        if (battalionValuesAuthoritative &&
            IsValidFrontlineBattalion(localPvpTeam) &&
            IsValidFrontlineBattalion(pvpTeam))
        {
            return pvpTeam == localPvpTeam
                ? (BattlefieldRelationship.AllyConfirmed, RelationshipConfidence.BattalionTeam)
                : (BattlefieldRelationship.EnemyConfirmed, RelationshipConfidence.BattalionTeam);
        }

        return (BattlefieldRelationship.Unknown, RelationshipConfidence.Unresolved);
    }
}
