using Dalamud.Game.ClientState.Objects.Enums;
using PvPSentinel.Models;

namespace PvPSentinel.GameState;

internal static class FrontlinePlayerResolver
{
    public static PlayerClassification Classify(
        bool isLocalPlayer,
        bool isFrontline,
        bool canClassifyNonMembers,
        bool isRosterMember,
        StatusFlags flags)
    {
        var partyMember = flags.HasFlag(StatusFlags.PartyMember);
        var allianceMember = flags.HasFlag(StatusFlags.AllianceMember);
        var hostile = flags.HasFlag(StatusFlags.Hostile);

        // Positive membership is authoritative. It wins over contradictory raw
        // hostility flags so a team member can never become an attack candidate.
        if (isLocalPlayer || isRosterMember || partyMember || allianceMember)
            return PlayerClassification.Friendly;

        if (hostile)
            return PlayerClassification.Enemy;

        // In a recognized Frontline duty, a loaded player character absent from
        // an available alliance roster is an opposing-team candidate. This rule
        // is deliberately unavailable while the alliance roster is incomplete.
        if (isFrontline && canClassifyNonMembers)
            return PlayerClassification.Enemy;

        return PlayerClassification.Unknown;
    }
}
