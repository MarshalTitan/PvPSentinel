using System.Numerics;
using System.Text.Json;
using PvPSentinel.Combat;
using PvPSentinel.Combat.Native;
using PvPSentinel.Combat.Native.Jobs.Machinist;
using PvPSentinel.Combat.Threat;
using PvPSentinel.FrontlineCore;
using PvPSentinel.FrontlineCore.Diagnostics;
using PvPSentinel.FrontlineCore.Maps;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

var failures = new List<string>();

Check("existing Native provider configuration value is preserved", 2, (int)CombatProvider.NativePvPSentinel);
Check("Reborn provider uses a new configuration value", 3, (int)CombatProvider.RotationSolverReborn);

var mapCases = new[]
{
    (Content: 127u, Territory: 1273u, Map: FrontlineMap.BorderlandRuins),
    (Content: 130u, Territory: 431u, Map: FrontlineMap.SealRock),
    (Content: 180u, Territory: 554u, Map: FrontlineMap.FieldsOfGlory),
    (Content: 701u, Territory: 888u, Map: FrontlineMap.OnsalHakair),
    (Content: 1080u, Territory: 1313u, Map: FrontlineMap.WorqorChirteh),
};

foreach (var item in mapCases)
{
    Check($"content ID {item.Content}", item.Map, FrontlineMapCatalog.Identify(item.Content, 0));
    Check($"territory ID {item.Territory}", item.Map, FrontlineMapCatalog.Identify(0, item.Territory));
    Check($"catalog content ID {item.Map}", item.Content, item.Map.ContentFinderConditionId());
    Check($"catalog territory ID {item.Map}", item.Territory, item.Map.TerritoryId());
    Check($"exact runtime identity {item.Map}", true, item.Map.HasExactIdentity(item.Content, item.Territory));
    Check($"display name round trip {item.Map}", item.Map, FrontlineMapCatalog.IdentifyText(item.Map.DisplayName()));
}
Check("Secure mismatched territory is not exact identity", false,
    FrontlineMap.BorderlandRuins.HasExactIdentity(127, 554));

var origin = Vector3.Zero;
var destination = new Vector3(100f, 0f, 0f);
CheckPath("normal generated path is accepted", true,
    [origin, new Vector3(50f, 0f, 10f), destination], origin, destination);
CheckPath("one-point path is rejected", false, [origin], origin, destination);
CheckPath("non-finite path is rejected", false,
    [origin, new Vector3(float.NaN, 0f, 0f), destination], origin, destination);
CheckPath("path beginning far from player is rejected", false,
    [new Vector3(20f, 0f, 0f), destination], origin, destination);
CheckPath("path ending far from destination is rejected", false,
    [origin, new Vector3(80f, 0f, 0f)], origin, destination);
CheckPath("path ending on the wrong vertical level is rejected", false,
    [origin, new Vector3(100f, 25f, 0f)], origin, destination);

var looping = new List<Vector3> { origin };
for (var index = 0; index < 14; index++)
    looping.Add(index % 2 == 0 ? new Vector3(250f, 0f, 0f) : origin);
looping.Add(destination);
CheckPath("implausibly long path is rejected", false, looping, origin, destination);

var yieldTracker = new ExternalCombatYieldTracker();
var yieldStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
Check("combat starts external yield", true,
    yieldTracker.Update(yieldStart, true, false, false, 2.5f).ShouldYield);
Check("yield grace remains active", true,
    yieldTracker.Update(yieldStart.AddSeconds(2), false, false, false, 2.5f).ShouldYield);
Check("strategic movement resumes after grace", false,
    yieldTracker.Update(yieldStart.AddSeconds(3), false, false, false, 2.5f).ShouldYield);
Check("queued action starts external yield", true,
    yieldTracker.Update(yieldStart.AddSeconds(4), false, false, true, 2.5f).ShouldYield);
yieldTracker.Reset();
Check("reset clears external yield", false,
    yieldTracker.Update(yieldStart.AddSeconds(4.1), false, false, false, 2.5f).ShouldYield);

var rebornEngagement = new RotationSolverEngagementTracker();
var rebornStart = new DateTime(2026, 9, 27, 1, 0, 0, DateTimeKind.Utc);
Check("inactive Reborn does not claim combat ownership", ExternalEngagementState.Unavailable,
    rebornEngagement.Update(rebornStart, false, false, false, false, false, true, false, false, 3, 0, false, 5f).State);
Check("active Reborn begins in transit", ExternalEngagementState.Transit,
    rebornEngagement.Update(rebornStart, true, false, false, false, false, false, false, false, 0, 0, false, 5f).State);
Check("mount cast does not latch a combat engagement", false,
    rebornEngagement.Update(rebornStart.AddSeconds(1), true, false, false, true, false, false, true, true, 0, 0, false, 5f).ShouldYield);
Check("unflagged mount cast without nearby enemies does not latch combat", false,
    rebornEngagement.Update(rebornStart.AddSeconds(1.5), true, false, false, false, false, false, true, true, 0, 0, false, 5f).ShouldYield);
Check("Sentinel mount request latch suppresses the pre-condition cast frame", false,
    rebornEngagement.Update(rebornStart.AddSeconds(1.75), true, false, false, false, true, false, true, true, 2, 0, false, 5f).ShouldYield);
Check("combat latches the Reborn engagement", true,
    rebornEngagement.Update(rebornStart.AddSeconds(2), true, false, false, false, false, true, false, false, 4, 1, false, 5f).ShouldYield);
Check("observed enemy hard target holds engagement after combat flag clears", true,
    rebornEngagement.Update(rebornStart.AddSeconds(3), true, false, false, false, false, false, false, false, 2, 1, false, 5f).ShouldYield);
Check("passive nearby enemies start quiet period instead of starving resume", true,
    rebornEngagement.Update(rebornStart.AddSeconds(4), true, false, false, false, false, false, false, false, 2, 0, false, 5f).ShouldYield);
Check("quiet period continues before configured boundary", true,
    rebornEngagement.Update(rebornStart.AddSeconds(8.9), true, false, false, false, false, false, false, false, 3, 0, false, 5f).ShouldYield);
var rebornCleared = rebornEngagement.Update(
    rebornStart.AddSeconds(9), true, false, false, false, false, false, false, false, 3, 0, false, 5f);
Check("strategic travel resumes after quiet period despite passive enemies", false, rebornCleared.ShouldYield);
Check("cleared engagement returns to transit", ExternalEngagementState.Transit, rebornCleared.State);
Check("death releases navigation yield", false,
    rebornEngagement.Update(rebornStart.AddSeconds(10), true, true, false, false, false, true, false, false, 5, 2, false, 5f).ShouldYield);
Check("death is reported distinctly", ExternalEngagementState.Dead,
    rebornEngagement.Update(rebornStart.AddSeconds(10.1), true, true, false, false, false, false, false, false, 0, 0, false, 5f).State);
Check("respawn enters regroup state without combat yield", ExternalEngagementState.Regrouping,
    rebornEngagement.Update(rebornStart.AddSeconds(11), true, false, false, false, false, false, false, false, 0, 0, true, 5f).State);

Check("Recuperate eligible below 75%", true,
    NativeCombatPolicy.RecuperateEligible(74.9f, 75f, 2000));
Check("Recuperate not eligible at 75%", false,
    NativeCombatPolicy.RecuperateEligible(75f, 75f, 10000));
Check("Recuperate requires MP", false,
    NativeCombatPolicy.RecuperateEligible(20f, 75f, 1999));
Check("Purify is actionable for removable CC with MP", true,
    NativeCombatPolicy.PurifyEligible(true, false, 2000));
Check("Purify does not preempt when MP is insufficient", false,
    NativeCombatPolicy.PurifyEligible(true, false, 1999));
Check("Purify does not preempt through Resilience", false,
    NativeCombatPolicy.PurifyEligible(true, true, 10000));
Check("Purify recognizes Half-asleep by public status name", true,
    NativeCombatPolicy.IsPurifyRemovableStatus(0, "Half-asleep"));
Check("Purify recognizes Sleep by public status name", true,
    NativeCombatPolicy.IsPurifyRemovableStatus(0, "Sleep"));
Check("Purify does not claim unsupported Miracle of Nature", false,
    NativeCombatPolicy.IsPurifyRemovableStatus(3085, "Miracle of Nature"));
Check("Recuperate hard-preempts offense", "Recuperate priority",
    NativeCombatPolicy.DefensePreemptionReason(false, false, true));
Check("Guard preempts simultaneous Recuperate", "Guard priority",
    NativeCombatPolicy.DefensePreemptionReason(false, true, true));
Check("Purify has highest defensive preemption", "Purify priority",
    NativeCombatPolicy.DefensePreemptionReason(true, true, true));
Check("no defensive condition leaves offense open", "None",
    NativeCombatPolicy.DefensePreemptionReason(false, false, false));
Check("target commitment blocks early switch", false,
    NativeCombatPolicy.ShouldSwitchTarget(1.9, 2f, 40f, 18f));
Check("target score threshold blocks small switch", false,
    NativeCombatPolicy.ShouldSwitchTarget(3, 2f, 17.9f, 18f));
Check("meaningful target improvement switches", true,
    NativeCombatPolicy.ShouldSwitchTarget(3, 2f, 18f, 18f));
Check("target score floor rejects negative least-bad target", false,
    NativeCombatPolicy.IsTargetScoreAcceptable(-1f, 10f));
Check("target score floor accepts exact boundary", true,
    NativeCombatPolicy.IsTargetScoreAcceptable(10f, 10f));
Check("severe overextension is rejected even above score floor", false,
    NativeCombatPolicy.IsTargetAcceptable(75f, 10f, true));
Check("safe candidate above score floor remains acceptable", true,
    NativeCombatPolicy.IsTargetAcceptable(10f, 10f, false));

var lowPressureGuard = NativeCombatPolicy.CalculateGuardThreshold(
    35f, 65f, 0, 2, 3, 4f, 1.5f, 2, 2f, 0f, 12f, 8f);
var focusFireGuard = NativeCombatPolicy.CalculateGuardThreshold(
    35f, 65f, 4, 8, 2, 4f, 1.5f, 2, 2f, 16f, 12f, 8f);
Check("low-pressure Guard retains base threshold", 35f, lowPressureGuard.Threshold);
Check("focus fire raises Guard threshold", true, focusFireGuard.Threshold > lowPressureGuard.Threshold);
Check("Guard threat threshold respects cap", 65f, focusFireGuard.Threshold);
Check("rapid collapse contributes to Guard threshold", 8f, focusFireGuard.RapidLossContribution);

Check("Marksman low focus receives no speculative allowance", 36000u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 1, 3000, 56000));
Check("Marksman only credits focus beyond grace", 39000u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 2, 3000, 56000));
Check("Marksman conservative allowance respects cap", 56000u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 9, 3000, 56000));
Check("Marksman applies confidence margin to Chain Saw modifier", 43200u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 0, 3000, 56000, 1.2f));
Check("Marksman rejects above-solo HP without strong focus", false,
    NativeCombatPolicy.HasMarksmanHighHpConfidence(41000, 40000, 1f, 2, 3));
Check("Marksman accepts above-solo HP with strong focus", true,
    NativeCombatPolicy.HasMarksmanHighHpConfidence(41000, 40000, 1f, 3, 3));
Check("Marksman anti-overkill floor rises with allied focus", 20000u,
    NativeCombatPolicy.MarksmanOverkillFloor(10000, 6, 2000, 1, 20000));
Check("Marksman conserves gauge on collapsing normal-range target", true,
    NativeCombatPolicy.IsMarksmanOverkillRisk(7538, 24.8f, 25f, 20000));
Check("Marksman retains long-range finishing role below overkill floor", false,
    NativeCombatPolicy.IsMarksmanOverkillRisk(7538, 25.1f, 25f, 20000));

Check("Wildfire survival floor retains low-focus baseline", 28000u,
    NativeCombatPolicy.WildfireSurvivalFloor(28000, 1, 2500, 1, 45000));
Check("Wildfire survival floor rises with credited allied focus", 38000u,
    NativeCombatPolicy.WildfireSurvivalFloor(28000, 5, 2500, 1, 45000));
Check("Wildfire survival floor respects cap", 45000u,
    NativeCombatPolicy.WildfireSurvivalFloor(28000, 12, 2500, 1, 45000));

var shadowLedger = new NativeShadowActionLedger();
var shadowStart = new DateTime(2026, 9, 26, 18, 37, 10, DateTimeKind.Utc);
Check("fresh Shadow action is available", true,
    shadowLedger.Check(MachinistActions.BishopAutoturret, shadowStart).IsAvailable);
shadowLedger.Commit(MachinistActions.BishopAutoturret, shadowStart);
Check("Shadow global lock follows a hypothetical action", false,
    shadowLedger.CheckGlobal(shadowStart.AddMilliseconds(300)).IsAvailable);
Check("Shadow global lock clears after animation lock", true,
    shadowLedger.CheckGlobal(shadowStart.AddMilliseconds(600)).IsAvailable);
Check("Shadow prevents duplicate Bishop during hypothetical recast", false,
    shadowLedger.Check(MachinistActions.BishopAutoturret, shadowStart.AddSeconds(29.9)).IsAvailable);
Check("Shadow releases Bishop at hypothetical recast", true,
    shadowLedger.Check(MachinistActions.BishopAutoturret, shadowStart.AddSeconds(30)).IsAvailable);
shadowLedger.Commit(MachinistActions.MarksmansSpite, shadowStart);
Check("Shadow spends hypothetical limit gauge", false,
    shadowLedger.Check(MachinistActions.MarksmansSpite, shadowStart.AddSeconds(89)).IsAvailable);
Check("Shadow limit gauge model recovers at charge time", true,
    shadowLedger.Check(MachinistActions.MarksmansSpite, shadowStart.AddSeconds(90)).IsAvailable);

Check("no observed pressure has no threat level", PvPThreatLevel.None,
    PvPThreatPolicy.EvaluateLevel(0, 0));
Check("one nearby enemy is low observed threat", PvPThreatLevel.Low,
    PvPThreatPolicy.EvaluateLevel(0, 1));
Check("two hard targeters are moderate threat", PvPThreatLevel.Moderate,
    PvPThreatPolicy.EvaluateLevel(2, 2));
Check("combined targeters and density are high threat", PvPThreatLevel.High,
    PvPThreatPolicy.EvaluateLevel(2, 6));
Check("six hard targeters are extreme threat", PvPThreatLevel.Extreme,
    PvPThreatPolicy.EvaluateLevel(6, 3));
Check("native hostile fallback accepts an unrostered hostile", true,
    PvPThreatPolicy.IsFallbackHostile(false, true, false, false, false));
Check("native hostile fallback excludes alliance members", false,
    PvPThreatPolicy.IsFallbackHostile(false, true, false, true, true));
Check("native hostile fallback excludes local player", false,
    PvPThreatPolicy.IsFallbackHostile(true, true, false, false, false));
Check("manual M2 waits for a mount request", true,
    ManualMountPolicy.ShouldWaitBeforeMovement(true, false, false, false, true));
Check("manual M2 waits for a dismount request", true,
    ManualMountPolicy.ShouldWaitBeforeMovement(false, true, true, false, true));
Check("manual M2 waits during an observed mount transition", true,
    ManualMountPolicy.ShouldWaitBeforeMovement(false, false, false, true, true));
Check("blocked mount while on foot does not suppress a valid manual route", false,
    ManualMountPolicy.ShouldWaitBeforeMovement(false, false, false, false, true));
Check("manual M2 may move once mounted", false,
    ManualMountPolicy.ShouldWaitBeforeMovement(false, false, true, false, false));
Check("confirmed PvP combat pauses and preserves a manual destination", true,
    ManualCombatYieldPolicy.ShouldPause(true, false));
Check("Reborn engagement pauses and preserves a manual destination", true,
    ManualCombatYieldPolicy.ShouldPause(false, true));
Check("manual destination does not resume while Reborn still owns combat", false,
    ManualCombatYieldPolicy.ShouldResume(true, false, true));
Check("manual destination resumes after battlefield and provider clear", true,
    ManualCombatYieldPolicy.ShouldResume(true, false, false));
Check("mount threshold crossing alone does not dismount", false,
    MountTravelPolicy.ShouldDismount(true, 54f, 28f, false));
Check("a visible enemy alone does not change mounted travel policy", false,
    MountTravelPolicy.ShouldDismount(true, 100f, 28f, false));
Check("route arrival threshold requests dismount", true,
    MountTravelPolicy.ShouldDismount(true, 27.9f, 28f, false));
Check("combat ownership requests an immediate dismount", true,
    MountTravelPolicy.ShouldDismount(true, 100f, 28f, true));

var normalizedSelf = TeamClassifier.Classify(0x10, 0x10, 0, 0, true);
Check("normalized classifier resolves SELF before team", BattlefieldRelationship.Self, normalizedSelf.Relationship);
Check("SELF has local-entity confidence", RelationshipConfidence.LocalEntity, normalizedSelf.Confidence);
Check("same zero-based Battalion team is ally", BattlefieldRelationship.AllyConfirmed,
    TeamClassifier.Classify(0x11, 0x10, 0, 0, true).Relationship);
Check("different zero-based Battalion team is enemy", BattlefieldRelationship.EnemyConfirmed,
    TeamClassifier.Classify(0x12, 0x10, 2, 0, true).Relationship);
Check("Battalion value outside 0-2 stays unknown", BattlefieldRelationship.Unknown,
    TeamClassifier.Classify(0x12, 0x10, 3, 0, true).Relationship);
Check("unavailable Battalion source leaves non-self unknown", BattlefieldRelationship.Unknown,
    TeamClassifier.Classify(0x12, 0x10, 1, 0, false).Relationship);
Check("Battalion 0 is live-valid in Frontline context", true,
    TeamClassifier.IsValidFrontlineBattalion(0));
Check("Battalion 3 is not a live-confirmed Dalamud Frontline team", false,
    TeamClassifier.IsValidFrontlineBattalion(3));
Check("missing native Battalion sentinel is invalid", false,
    TeamClassifier.IsValidFrontlineBattalion(byte.MaxValue));

var trackingNow = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
Check("fresh player remains trackable at freshness boundary", true,
    PlayerTrackingPolicy.IsFresh(trackingNow, trackingNow.AddSeconds(1.5)));
Check("stale player is evicted beyond ten seconds", true,
    PlayerTrackingPolicy.IsExpired(trackingNow, trackingNow.AddSeconds(10.01)));
Check("player is not evicted at exact ten-second boundary", false,
    PlayerTrackingPolicy.IsExpired(trackingNow, trackingNow.AddSeconds(10)));

var clusterPlayers = new[]
{
    Track(1, BattlefieldRelationship.AllyConfirmed, new Vector3(0, 0, 0)),
    Track(2, BattlefieldRelationship.AllyConfirmed, new Vector3(10, 0, 0)),
    Track(3, BattlefieldRelationship.AllyConfirmed, new Vector3(50, 0, 0)),
    Track(4, BattlefieldRelationship.EnemyConfirmed, new Vector3(8, 0, 0)),
};
var clustered = BattlefieldClusterer.Build(clusterPlayers, BattlefieldRelationship.AllyConfirmed,
    Vector3.Zero, 20f, []);
Check("connected-component clustering forms two allied groups", 2, clustered.Count);
Check("largest deterministic cluster has two members", 2, clustered[0].MemberCount);
Check("cluster numerical context counts nearby enemy", 1, clustered[0].NearbyEnemies);

Check("Shatter large preactivation ID is normalized", ObjectiveLifecycle.Preactivating,
    ShatterObjectivePolicy.Resolve(60989)!.Value.State);
Check("Shatter small active ID is normalized", ObjectiveLifecycle.Active,
    ShatterObjectivePolicy.Resolve(60904)!.Value.State);
Check("Shatter logical objective parses from tooltip", "B15",
    ShatterObjectivePolicy.ParseLogicalId("Icebound Tomelith B15 Activation in: 0:30")!);
Check("Shatter activation ETA parses", 30,
    ShatterObjectivePolicy.ParseActivationEtaSeconds("Activation in: 0:30")!.Value);
Check("Shatter strength parses", 67,
    ShatterObjectivePolicy.ParseStrengthPercent("Ice Strength: 67%")!.Value);

var pairedSealMarkers = new[]
{
    Marker(60589, new Vector3(100, 5, -30)),
    Marker(60484, new Vector3(100.1f, 5, -30.1f)),
};
var sealPaired = SealRockObjectiveAggregator.Aggregate(pairedSealMarkers);
Check("Seal Rock paired records aggregate to one logical location", 1, sealPaired.Count);
Check("Seal Rock paired record retains evidence count", 2, sealPaired[0].EvidenceCount);
Check("Seal Rock rank A is normalized", "A", sealPaired[0].Rank);
Check("Seal Rock ownership overlay is retained", "MAELSTROM", sealPaired[0].GrandCompany);
var sealChanged = SealRockObjectiveAggregator.Aggregate([Marker(60591, new Vector3(100, 5, -30))]);
Check("Seal Rock ownership change is observable", "ADDERS", sealChanged[0].GrandCompany);

var securePosition = new Vector3(100, 5, -30);
var secureMarkers = new[]
{
    SecureMarker(70001, 71001, 72001, securePosition, "Unresolved marker A", 1),
    SecureMarker(70002, 71002, 72002, securePosition + new Vector3(0.1f, 0, -0.1f), "Unresolved marker B", 2),
};
var secureAggregate = SecureObjectiveAggregator.Aggregate(secureMarkers);
Check("Secure duplicate coordinates aggregate to one candidate", 1, secureAggregate.Count);
Check("Secure aggregate retains both marker records", 2, secureAggregate[0].EvidenceCount);
Check("Secure discovery does not infer state from marker IDs", true,
    secureAggregate[0].EvidenceSummary.Contains("70001/71001/72001", StringComparison.Ordinal));
Check("Secure marker with non-sentinel objective evidence is promotable", SecureMarkerPromotionClass.ObjectiveSignal,
    SecureObjectiveAggregator.ClassifyPromotionEvidence(secureAggregate[0], false));
Check("Secure candidate is not stable before minimum age", false,
    SecureObjectiveAggregator.IsStable(trackingNow, trackingNow.AddSeconds(1.9), 8,
        SecureMarkerPromotionClass.ObjectiveSignal));
Check("Secure candidate is stable after bounded scans and age", true,
    SecureObjectiveAggregator.IsStable(trackingNow, trackingNow.AddSeconds(2), 8,
        SecureMarkerPromotionClass.ObjectiveSignal));

var transientSecure = SecureMarker(60360, 0, 1115742468, securePosition, "", 0);
Check("live-verified moving Secure marker family is raw-only", SecureMarkerPromotionClass.RawObservationOnly,
    SecureObjectiveAggregator.ClassifyPromotionEvidence(
        SecureObjectiveAggregator.Aggregate([transientSecure])[0], false));
var newlyConfirmedTransientSecure = SecureMarker(60359, 0, 1115742468, securePosition, "", 0);
Check("second-match moving Secure marker family is raw-only", SecureMarkerPromotionClass.RawObservationOnly,
    SecureObjectiveAggregator.ClassifyPromotionEvidence(
        SecureObjectiveAggregator.Aggregate([newlyConfirmedTransientSecure])[0], true));
var traderSecure = SecureMarker(60935, 0, 721735, securePosition, "Trader", 0);
Check("live-verified Secure Trader marker remains raw-only", SecureMarkerPromotionClass.RawObservationOnly,
    SecureObjectiveAggregator.ClassifyPromotionEvidence(
        SecureObjectiveAggregator.Aggregate([traderSecure])[0], false));
foreach (var nonObjective in new[]
         {
             SecureMarker(63922, 0, 721462, securePosition, "", 0),
             SecureMarker(71121, 0, 721223, securePosition, "Player marker", 0),
             SecureMarker(71041, 0, 393222, securePosition, "Levemete", 0),
         })
{
    Check("newly field-verified pre-match Secure family remains raw-only",
        SecureMarkerPromotionClass.RawObservationOnly,
        SecureObjectiveAggregator.ClassifyPromotionEvidence(
            SecureObjectiveAggregator.Aggregate([nonObjective])[0], false));
}
foreach (var landingMarker in new[]
         {
             SecureMarker(60597, 0, 162, new Vector3(-257.4f, -7.1f, 149.3f), "", 0),
             SecureMarker(60597, 0, 1115742468, new Vector3(-257.4f, -7.1f, 149.3f), "", 0),
             SecureMarker(60598, 0, 162, new Vector3(0.2f, -7f, -297.4f), "", 0),
         })
{
    Check("field-verified Secure landing marker remains raw-only",
        SecureMarkerPromotionClass.RawObservationOnly,
        SecureObjectiveAggregator.ClassifyPromotionEvidence(
            SecureObjectiveAggregator.Aggregate([landingMarker])[0], false));
}
var objectivePresent = SecureObjectiveAggregator.Aggregate(
    [SecureMarker(60575, 0, 486, securePosition, "South", 0)])[0];
var objectiveFlicker = SecureObjectiveAggregator.Aggregate(
    [SecureMarker(60575, 0, 0, securePosition, "South", 0)])[0];
Check("Secure 486/zero objective-field flicker is not a semantic transition",
    objectivePresent.TransitionFingerprint, objectiveFlicker.TransitionFingerprint);
var objective480 = SecureObjectiveAggregator.Aggregate(
    [SecureMarker(60575, 0, 480, securePosition, "South", 0)])[0];
Check("Secure 480/zero objective-field flicker is not a semantic transition",
    objective480.TransitionFingerprint, objectiveFlicker.TransitionFingerprint);
var countdownA = SecureObjectiveAggregator.Aggregate(
    [SecureMarker(63979, 0, 486, securePosition, "Spawns in: 2:36", 0)])[0];
var countdownB = SecureObjectiveAggregator.Aggregate(
    [SecureMarker(63979, 0, 486, securePosition, "Spawns in: 2:35", 0)])[0];
Check("Secure countdown ticks are retained as evidence without transition spam",
    countdownA.TransitionFingerprint, countdownB.TransitionFingerprint);
var hpEvidence = SecureObjectiveAggregator.Aggregate(
    [SecureMarker(60999, 0, 486, securePosition, "HP: 20%", 0)])[0];
Check("Secure lifecycle shape change remains a semantic transition", false,
    countdownA.TransitionFingerprint == hpEvidence.TransitionFingerprint);

var secureAdapter = new SecureAdapter();
secureAdapter.Reset(trackingNow);
for (var scan = 0; scan < 7; scan++)
    secureAdapter.Update(trackingNow.AddMilliseconds(scan * 300), secureMarkers, [], []);
Check("Secure adapter does not expose an unstable navigation destination", 0, secureAdapter.Objectives.Count);
secureAdapter.Update(trackingNow.AddMilliseconds(2100), secureMarkers, [], []);
Check("Secure adapter promotes one stable SEC location", 1, secureAdapter.Objectives.Count);
Check("first Secure logical location has deterministic session ID", "SEC-01", secureAdapter.Objectives[0].LogicalId);
Check("Secure objective lifecycle remains unresolved", ObjectiveLifecycle.Unknown, secureAdapter.Objectives[0].State);
Check("Secure objective ownership remains unresolved", ObjectiveOwner.Unresolved, secureAdapter.Objectives[0].Owner);
var secureChangedMarker = new[]
{
    SecureMarker(70003, 71003, 72003, securePosition, "Changed unresolved evidence", 3),
};
var secureChanges = secureAdapter.Update(trackingNow.AddSeconds(2.4), secureChangedMarker, [], []);
Check("Secure raw evidence change produces transition event", true,
    secureChanges.Any(change => change.EventName == "secure_observable_transition"));
var securePhysical = new ObjectiveObservation(
    0xABCDEF, 0x12345678, 8123, "Unresolved Secure Object", "EventObj",
    securePosition + new Vector3(1, 0, 1), true, false, 1000, 1000);
var securePhysicalChanges = secureAdapter.Update(
    trackingNow.AddSeconds(2.7), secureChangedMarker, [securePhysical], []);
Check("Secure physical research object is retained", 1, secureAdapter.ResearchObjects.Count);
Check("Secure physical appearance is evented", true,
    securePhysicalChanges.Any(change => change.EventName == "secure_research_object_appeared"));
var playerPet = new ObjectiveObservation(
    0xABCD00, 0x12340000, 7803, "Demi-Bahamut", "BattleNpc",
    securePosition, false, false, 57000, 57000);
Check("non-targetable player pet is not objective-like physical evidence", false,
    SecureAdapter.IsPotentialObjectivePhysical(playerPet));
var targetableObjectiveNpc = playerPet with { IsTargetable = true, MaxHp = 300_000 };
Check("large targetable BattleNpc may corroborate an objective", true,
    SecureAdapter.IsPotentialObjectivePhysical(targetableObjectiveNpc));

var centerAdapter = new SecureAdapter();
centerAdapter.Reset(trackingNow);
var centerMarker = new[]
{
    SecureMarker(63980, 0, 486, new Vector3(0.015f, 28.977f, -0.015f), "System", 0),
};
for (var scan = 0; scan < 8; scan++)
    centerAdapter.Update(trackingNow.AddMilliseconds(scan * 300), centerMarker, [], []);
Check("Secure geometric center has a stable cross-match identity", "SEC-CENTER",
    centerAdapter.Objectives.Single().LogicalId);

var transientAdapter = new SecureAdapter();
transientAdapter.Reset(trackingNow);
for (var scan = 0; scan < 40; scan++)
{
    var moving = transientSecure with { Position = securePosition + new Vector3(scan * 2f, 0f, 0f) };
    transientAdapter.Update(trackingNow.AddMilliseconds(scan * 250), [moving], [], []);
}
Check("moving Secure marker family never becomes a navigation destination", 0, transientAdapter.Objectives.Count);

var ceilingAdapter = new SecureAdapter();
ceilingAdapter.Reset(trackingNow);
var excessiveStableMarkers = Enumerable.Range(0, 40)
    .Select(index => SecureMarker(70000u + (uint)index, 71000u + (uint)index, 486,
        new Vector3(index * 4f, 0f, index * 4f), $"stable {index}", 1))
    .ToArray();
for (var scan = 0; scan < 8; scan++)
    ceilingAdapter.Update(trackingNow.AddMilliseconds(scan * 300), excessiveStableMarkers, [], []);
Check("Secure destination promotion obeys sanity ceiling", SecureObjectiveAggregator.MaximumPromotedLocations,
    ceilingAdapter.Objectives.Count);

var discoveryMarkers = new[]
{
    SecureMarker(80001, 81001, 82001, new Vector3(25, 4, -75), "UNRESOLVED objective evidence", 1),
    SecureMarker(80002, 81002, 82002, new Vector3(25.1f, 4, -75.1f), "UNRESOLVED paired evidence", 1),
};
var discoveryAggregate = DiscoveryMarkerAggregator.Aggregate(discoveryMarkers);
Check("generic discovery aggregates duplicate coordinates", 1, discoveryAggregate.Count);
Check("generic discovery retains paired evidence", 2, discoveryAggregate[0].EvidenceCount);
Check("generic discovery identifies objective-like stable evidence", DiscoveryMarkerPromotionClass.ObjectiveSignal,
    DiscoveryMarkerAggregator.ClassifyPromotionEvidence(discoveryAggregate[0], false));
var discovery480 = DiscoveryMarkerAggregator.Aggregate(
    [SecureMarker(80001, 0, 480, securePosition, "Evidence 42%", -1)])[0];
var discoveryZero = DiscoveryMarkerAggregator.Aggregate(
    [SecureMarker(80001, 0, 0, securePosition, "Evidence 41%", -1)])[0];
Check("generic discovery normalizes 480/zero and volatile counters",
    discovery480.TransitionFingerprint, discoveryZero.TransitionFingerprint);
var rawDiscoveryMarker = SecureMarker(
    80003, 0, DiscoveryMarkerAggregator.ObservedTransientObjectiveSentinel,
    securePosition, "", -1);
Check("generic identity-only marker remains research-only", DiscoveryMarkerPromotionClass.RawObservationOnly,
    DiscoveryMarkerAggregator.ClassifyPromotionEvidence(
        DiscoveryMarkerAggregator.Aggregate([rawDiscoveryMarker])[0], false));

var onsalAdapter = new OnsalHakairAdapter();
onsalAdapter.Reset(trackingNow);
for (var scan = 0; scan < DiscoveryMarkerAggregator.ObjectiveSignalStableScans; scan++)
    onsalAdapter.Update(trackingNow.AddMilliseconds(scan * 300), discoveryMarkers, [], []);
Check("Onsal promotes only stable ONS destination evidence", 1, onsalAdapter.Objectives.Count);
Check("Onsal destination uses map-specific prefix", "ONS-01", onsalAdapter.Objectives.Single().LogicalId);
Check("Onsal objective semantics stay unresolved", ObjectiveLifecycle.Unknown,
    onsalAdapter.Objectives.Single().State);
Check("Onsal ownership stays unresolved", ObjectiveOwner.Unresolved,
    onsalAdapter.Objectives.Single().Owner);
Check("Onsal adapter identity is explicit", FrontlineMap.OnsalHakair, onsalAdapter.Map);
onsalAdapter.Reset(trackingNow.AddMinutes(1));
Check("Onsal reset clears promoted session locations", 0, onsalAdapter.Objectives.Count);

var rawOnlyOnsal = new OnsalHakairAdapter();
rawOnlyOnsal.Reset(trackingNow);
for (var scan = 0; scan < 60; scan++)
    rawOnlyOnsal.Update(trackingNow.AddMilliseconds(scan * 250),
        [rawDiscoveryMarker with { Position = securePosition + new Vector3(scan, 0, 0) }], [], []);
Check("Onsal raw observations never become buttons", 0, rawOnlyOnsal.Objectives.Count);

var liveTransientOnsal = new OnsalHakairAdapter();
liveTransientOnsal.Reset(trackingNow);
var liveMovingMarker = SecureMarker(
    60360, 0, 0xFF000000, securePosition, "", -1);
var nearbyEventObject = new ObjectiveObservation(
    0x1111, 0x2222, 9999, "Unresolved Event Object", "EventObj",
    securePosition, false, false, 0, 0);
for (var scan = 0; scan < DiscoveryMarkerAggregator.PhysicalEvidenceStableScans + 4; scan++)
    liveTransientOnsal.Update(trackingNow.AddMilliseconds(scan * 300),
        [liveMovingMarker], [nearbyEventObject], []);
Check("live-confirmed moving Onsal family stays raw beside physical objects", 0,
    liveTransientOnsal.Objectives.Count);

var flickeringOnsal = new OnsalHakairAdapter();
flickeringOnsal.Reset(trackingNow);
for (var scan = 0; scan < DiscoveryMarkerAggregator.ObjectiveSignalStableScans + 1; scan++)
{
    var marker = SecureMarker(60590, 0, scan % 2 == 0 ? 446u : 448u,
        securePosition, "Ovoo 10 Rank A Claimed", -1);
    flickeringOnsal.Update(trackingNow.AddMilliseconds(scan * 300), [marker], [], []);
}
Check("Onsal 446/448 presentation flicker does not reset location stability", 1,
    flickeringOnsal.Objectives.Count);

var worqorAdapter = new WorqorChirtehAdapter();
worqorAdapter.Reset(trackingNow);
for (var scan = 0; scan < DiscoveryMarkerAggregator.ObjectiveSignalStableScans; scan++)
    worqorAdapter.Update(trackingNow.AddMilliseconds(scan * 300), discoveryMarkers, [], []);
Check("Worqor scaffold promotes stable manual-test locations", "WOR-01",
    worqorAdapter.Objectives.Single().LogicalId);
Check("Worqor adapter identity is explicit", FrontlineMap.WorqorChirteh, worqorAdapter.Map);

var mapAdapters = new IFrontlineMapAdapter[]
{
    new SecureAdapter(),
    new SealRockAdapter(),
    new ShatterAdapter(),
    new OnsalHakairAdapter(),
    new WorqorChirtehAdapter(),
};
Check("all five current Frontline maps have explicit adapters", 5,
    mapAdapters.Select(adapter => adapter.Map).Distinct().Count());

var lifecycleTracker = new MatchLifecycleTracker();
var uiActive = new FrontlineUiObservation(true, false, TimeSpan.FromMinutes(19), 1400, [], "", "header");
var uiResults = new FrontlineUiObservation(true, true, TimeSpan.FromMinutes(1), 1400, [], "", "results");
Check("Frontline lifecycle enters active match", FrontlineMatchLifecycle.MatchActive,
    lifecycleTracker.Update(true, true, uiActive).Lifecycle);
Check("Frontline lifecycle detects results", FrontlineMatchLifecycle.Results,
    lifecycleTracker.Update(true, true, uiResults).Lifecycle);
Check("RESULTS is terminal while header remains visible", FrontlineMatchLifecycle.Results,
    lifecycleTracker.Update(true, true, uiActive).Lifecycle);
Check("map exit resets terminal results", FrontlineMatchLifecycle.Outside,
    lifecycleTracker.Update(false, false, uiActive).Lifecycle);

Check("enemy-player combat blocks navigation", true,
    CombatContextPolicy.BlocksMovement(CombatContextPolicy.Resolve(true, true, false)));
Check("objective combat does not block a fresh manual request", false,
    CombatContextPolicy.BlocksMovement(CombatContextPolicy.Resolve(true, false, true)));
Check("stale generic combat does not permanently block navigation", false,
    CombatContextPolicy.BlocksMovement(CombatContextPolicy.Resolve(true, false, false)));

var summaryStart = new DateTime(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);
var summaryBuilder = new RetainedMatchSummaryBuilder(FrontlineMap.FieldsOfGlory, 554, 180, summaryStart);
summaryBuilder.Observe(BattlefieldForSummary(summaryStart.AddMinutes(1), FrontlineMatchLifecycle.MatchActive, 1, 23, 48, 0, 2, 1));
summaryBuilder.Observe(BattlefieldForSummary(summaryStart.AddMinutes(2), FrontlineMatchLifecycle.MatchActive, 1, 23, 48, 0, 2, 1) with
{
    ResearchNotes = ["Observed 1 candidate", "Durable unresolved caveat"],
});
summaryBuilder.Observe(BattlefieldForSummary(summaryStart.AddMinutes(3), FrontlineMatchLifecycle.MatchActive, 1, 23, 48, 0, 2, 1) with
{
    ResearchNotes = ["Observed 2 candidates", "Durable unresolved caveat"],
});
summaryBuilder.RecordNavigation("navigation_request");
summaryBuilder.RecordNavigation("navigation_arrived");
summaryBuilder.RecordNavigation("navigation_path_failed");
summaryBuilder.RecordNavigation("navigation_stuck");
summaryBuilder.RecordObjectiveChange(
    new ObjectiveChange("secure_observable_transition", "SEC-01", "icon=1 -> icon=2"),
    summaryStart.AddMinutes(2));
summaryBuilder.Observe(BattlefieldForSummary(summaryStart.AddMinutes(20), FrontlineMatchLifecycle.Results, 1, 20, 40, 0, 2, 1) with
{
    ResearchNotes = ["Observed 2 candidates", "Durable unresolved caveat"],
});
var retained = summaryBuilder.Build(summaryStart.AddMinutes(21));
Check("retained summary preserves peak allies", 23, retained.PeakAllies);
Check("retained summary preserves peak enemies", 48, retained.PeakEnemies);
Check("retained summary preserves results", true, retained.ResultsDetected);
Check("retained summary counts navigation arrivals", 1, retained.NavigationArrivals);
Check("retained summary counts path failures", 1, retained.NavigationPathFailures);
Check("retained summary counts stuck events", 1, retained.NavigationStuckEvents);
Check("retained summary preserves objective transition evidence", 1, retained.ObjectiveTransitions.Count);
Check("retained summary preserves territory", 554u, retained.TerritoryId);
Check("retained summary preserves duty", 180u, retained.ContentFinderConditionId);
var zeroTeamSummaryBuilder = new RetainedMatchSummaryBuilder(
    FrontlineMap.OnsalHakair, 888, 701, summaryStart);
zeroTeamSummaryBuilder.Observe(BattlefieldForSummary(
    summaryStart, FrontlineMatchLifecycle.PreMatch, 0, 0, 0, 0, 0, 0) with
{
    LocalPvPTeam = byte.MaxValue,
});
zeroTeamSummaryBuilder.Observe(BattlefieldForSummary(
    summaryStart.AddSeconds(1), FrontlineMatchLifecycle.PreMatch, 1, 23, 0, 0, 0, 0) with
{
    LocalPvPTeam = 0,
});
Check("retained summary accepts zero-based Battalion team 0", (byte)0,
    zeroTeamSummaryBuilder.Build(summaryStart.AddMinutes(1)).LocalPvPTeam);
Check("retained summary keeps only the latest dynamic research snapshot", false,
    retained.UnresolvedObservations.Contains("Observed 1 candidate"));
Check("retained summary keeps current research counters", true,
    retained.UnresolvedObservations.Contains("Observed 2 candidates"));
var transitionBoundBuilder = new RetainedMatchSummaryBuilder(
    FrontlineMap.OnsalHakair, 888, 701, summaryStart);
for (var index = 0; index < 50; index++)
    transitionBoundBuilder.RecordObjectiveChange(
        new ObjectiveChange("onsal_observable_transition", "ONS-01", $"state={index}"),
        summaryStart.AddSeconds(index));
var perSignalBound = transitionBoundBuilder.Build(summaryStart.AddMinutes(1));
Check("retained summary bounds repeated transitions per signal",
    RetainedMatchSummaryBuilder.MaximumRetainedTransitionsPerSignal,
    perSignalBound.ObjectiveTransitions.Count);
for (var index = 0; index < 700; index++)
    transitionBoundBuilder.RecordObjectiveChange(
        new ObjectiveChange("onsal_research_object_appeared", $"ONS-OBJ-{index:000}", $"object={index}"),
        summaryStart.AddMinutes(2).AddSeconds(index));
var globallyBound = transitionBoundBuilder.Build(summaryStart.AddMinutes(20));
Check("retained summary has a global transition ceiling",
    RetainedMatchSummaryBuilder.MaximumRetainedObjectiveTransitions,
    globallyBound.ObjectiveTransitions.Count);
var retainedJson = JsonSerializer.Serialize(retained with
{
    ObjectiveResearch =
    [
        new ObjectiveResearchSummary("SEC-01", new Vector3(1.25f, 2.5f, -3.75f),
            "Unresolved", "Unresolved", "research", "test", summaryStart, summaryStart),
    ],
}, FrontlineJson.CreateOptions());
Check("retained summary serializes objective X coordinate", true,
    retainedJson.Contains("\"x\":1.25", StringComparison.Ordinal));
Check("retained summary serializes objective Y coordinate", true,
    retainedJson.Contains("\"y\":2.5", StringComparison.Ordinal));
Check("retained summary serializes objective Z coordinate", true,
    retainedJson.Contains("\"z\":-3.75", StringComparison.Ordinal));

var routeA = new[] { Vector3.Zero, new Vector3(10, 0, 0), new Vector3(20, 0, 0), new Vector3(40, 0, 0) };
var routeB = new[] { Vector3.Zero, new Vector3(10.5f, 0, 0.2f), new Vector3(20.5f, 0, 0), new Vector3(40, 0, 10) };
var routeC = new[] { Vector3.Zero, new Vector3(0, 0, 10), new Vector3(0, 0, 20), new Vector3(40, 0, 0) };
Check("opening route comparison rejects materially identical recovery", true,
    RouteComparison.MateriallyIdentical(routeA, routeB));
Check("opening route comparison accepts alternate geometry", false,
    RouteComparison.MateriallyIdentical(routeA, routeC));
var stairRoute = new[]
{
    Vector3.Zero,
    new Vector3(8, 0, 0),
    new Vector3(8, 1.2f, 2),
    new Vector3(8, 3f, 5),
    new Vector3(14, 3f, 5),
};
var stairPlan = RouteExecutionPlan.Build(stairRoute);
Check("stair entry waypoint is protected", true, stairPlan.Waypoints[2].Protected);
Check("protected route stages stop before the elevation change is skipped", 1,
    stairPlan.FindStageEnd(0));
var stairStage = stairPlan.BuildStage(new Vector3(0.2f, 0, 0), 0, stairPlan.FindStageEnd(0));
Check("staged route starts at the live player position", new Vector3(0.2f, 0, 0), stairStage[0]);
var failedCorridor = RouteComparison.FailureCorridor(stairRoute, new Vector3(7.9f, 0, 0));
Check("same stair corridor is rejected during recovery", true,
    RouteComparison.RepeatsFailedCorridor(failedCorridor,
        [new Vector3(7.9f, 0, 0), new Vector3(8, 1.2f, 2), new Vector3(8, 3f, 5), new Vector3(14, 3f, 5)]));
var secureTwelveFailure = new[]
{
    new Vector3(76.8f, -21.1f, 48.4f),
    new Vector3(77.8f, -19.8f, 46.8f),
    new Vector3(72.2f, -19.8f, 42.0f),
    new Vector3(62.8f, -19.8f, 34.5f),
    new Vector3(60.8f, -19.8f, 32.2f),
};
var secureTwelveReplacement = new[]
{
    new Vector3(76.6f, -21.1f, 48.6f),
    new Vector3(78.8f, -20.2f, 50.0f),
    new Vector3(79.5f, -19.8f, 49.8f),
    new Vector3(79.5f, -19.8f, 48.8f),
    new Vector3(77.8f, -19.8f, 46.8f),
    new Vector3(72.2f, -19.8f, 42.0f),
    new Vector3(62.8f, -19.8f, 34.5f),
    new Vector3(60.8f, -19.8f, 32.2f),
};
Check("SEC-12 detour that rejoins failed stair corridor is rejected", true,
    RouteComparison.RepeatsFailedCorridor(secureTwelveFailure, secureTwelveReplacement));
var departureAnchors = RouteComparison.DepartureAnchors(
    secureTwelveFailure[0], secureTwelveFailure, nearRadius: 12f, farRadius: 20f);
Check("stuck recovery generates bounded departure stages", 6, departureAnchors.Count);
Check("departure stages move materially away from failed origin", true,
    departureAnchors.All(point => Vector2.Distance(
        new Vector2(point.X, point.Z),
        new Vector2(secureTwelveFailure[0].X, secureTwelveFailure[0].Z)) >= 11.9f));
var failedDirection = Vector2.Normalize(new Vector2(
    secureTwelveFailure[1].X - secureTwelveFailure[0].X,
    secureTwelveFailure[1].Z - secureTwelveFailure[0].Z));
var firstDeparture = Vector2.Normalize(new Vector2(
    departureAnchors[0].X - secureTwelveFailure[0].X,
    departureAnchors[0].Z - secureTwelveFailure[0].Z));
Check("first recovery stage is perpendicular to failed corridor", true,
    Math.Abs(Vector2.Dot(failedDirection, firstDeparture)) < 0.01f);
Check("same horizontal route on another floor is not treated as identical", false,
    RouteComparison.MateriallyIdentical(routeA, routeA.Select(point => point + new Vector3(0, 8, 0)).ToArray()));
Check("bounded recovery allows attempt below limit", true, ManualNavigationPolicy.CanRetry(2, 3));
Check("bounded recovery stops at limit", false, ManualNavigationPolicy.CanRetry(3, 3));
Check("mounted actor-height offset accepts a completed generated stage", true,
    ManualNavigationPolicy.HasReachedGeneratedPoint(
        new Vector3(90.4f, -18.9f, 49f), new Vector3(90.6f, -23.5f, 48.3f),
        0.9f, 1.25f, true));
Check("same vertical mismatch is rejected while on foot", false,
    ManualNavigationPolicy.HasReachedGeneratedPoint(
        new Vector3(90.4f, -18.9f, 49f), new Vector3(90.6f, -23.5f, 48.3f),
        0.9f, 1.25f, false));
Check("mounted allowance still rejects a genuinely different floor", false,
    ManualNavigationPolicy.HasReachedGeneratedPoint(
        new Vector3(90.4f, -14f, 49f), new Vector3(90.6f, -23.5f, 48.3f),
        0.9f, 1.25f, true));
Check("death owns and cancels navigation", MovementOwner.DeathRecovery,
    ManualNavigationPolicy.ResolveOwner(true, false, true));
Check("enemy combat owns and pauses navigation", MovementOwner.ExternalCombat,
    ManualNavigationPolicy.ResolveOwner(false, true, true));
Check("released route has no movement owner", MovementOwner.None,
    ManualNavigationPolicy.ResolveOwner(false, false, false));

var mockVnav = new MockVNavmeshAdapter
{
    Reachable = new Vector3(30, 0, 0),
    Route = [Vector3.Zero, new Vector3(15, 0, 2), new Vector3(30, 0, 0)],
};
var planned = await ManualRoutePlanner.BuildAsync(mockVnav, Vector3.Zero, new Vector3(31, 0, 0), 2.5f);
Check("mock vnavmesh route plan is valid", true, planned.IsValid);
Check("manual planner always snaps before pathfinding", 1, mockVnav.NearestCalls);
Check("manual planner requests generated path once", 1, mockVnav.PathfindCalls);
var blockedVnav = new MockVNavmeshAdapter { Reachable = null };
var blockedPlan = await ManualRoutePlanner.BuildAsync(blockedVnav, Vector3.Zero, new Vector3(31, 0, 0), 2.5f);
Check("no reachable mesh point produces clean failure", false, blockedPlan.IsValid);
Check("no direct-steering fallback is started", 0, blockedVnav.StartCalls);

var limiter = new EventDiffLimiter(TimeSpan.FromSeconds(10));
Check("first event diff is emitted", true, limiter.ShouldEmit("cluster", "A", trackingNow));
Check("unchanged event is rate-limited", false, limiter.ShouldEmit("cluster", "A", trackingNow.AddSeconds(2)));
Check("changed event bypasses rate limit", true, limiter.ShouldEmit("cluster", "B", trackingNow.AddSeconds(3)));
Check("unchanged event is periodically refreshed", true, limiter.ShouldEmit("cluster", "B", trackingNow.AddSeconds(14)));
Check("privacy sanitizer removes player-like names", false,
    PrivacySanitizer.Sanitize("target Alice Example at objective").Contains("Alice Example", StringComparison.Ordinal));
Check("privacy sanitizer strips control characters", false,
    PrivacySanitizer.Sanitize("safe\ntext").Contains('\n'));

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} logic test(s) failed:");
    foreach (var failure in failures)
        Console.Error.WriteLine("- " + failure);
    return 1;
}

Console.WriteLine("PvPSentinel logic tests passed (providers, classification, Secure/Shatter/Seal Rock, M2 paths, lifecycle, native combat, and shared threat policies).");
return 0;

void Check<T>(string name, T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{name}: expected {expected}, got {actual}");
}

TrackedPlayer Track(uint id, BattlefieldRelationship relationship, Vector3 position) => new(
    id, id, 31, "MCH", relationship == BattlefieldRelationship.EnemyConfirmed ? (byte)2 : (byte)1,
    relationship, RelationshipConfidence.BattalionTeam, position, 50000, 50000, false, false, 0,
    trackingNow, trackingNow, 0f);

FrontlineMapMarkerObservation Marker(uint iconId, Vector3 position) => new(
    iconId, 0, 0, position, "Allagan Tomelith", 0, 0, "test");

FrontlineMapMarkerObservation SecureMarker(
    uint iconId,
    uint dataId,
    uint objectiveId,
    Vector3 position,
    string tooltip,
    sbyte eventState) => new(
        iconId, dataId, objectiveId, position, tooltip, 123456, eventState, "test");

BattlefieldState BattlefieldForSummary(
    DateTime at,
    FrontlineMatchLifecycle lifecycle,
    int self,
    int allies,
    int enemies,
    int unknown,
    int deaths,
    int respawns) => new(
        at, FrontlineMap.FieldsOfGlory, 554, "The Fields of Glory", "Shatter", null, 1, [],
        new RelationshipCounts(self, allies, enemies, unknown), [], [], [], [], [],
        new FrontlineMatchState(lifecycle, null, 1400, [], lifecycle == FrontlineMatchLifecycle.Results,
            "UNAVAILABLE / OPTIONAL", "test"),
        new CombatContextSnapshot(FrontlineCombatContext.None, false, 0, 0, "test"),
        new DeathRespawnSnapshot(DeathRespawnState.Alive, deaths, respawns, at), [], null, "test");

void CheckPath(
    string name,
    bool expected,
    IReadOnlyList<Vector3> path,
    Vector3 pathOrigin,
    Vector3 pathDestination)
{
    var actual = PathValidator.Validate(path, pathOrigin, pathDestination, 2.5f);
    if (actual.IsValid != expected)
        failures.Add($"{name}: expected valid={expected}, got valid={actual.IsValid} ({actual.Explanation})");
}

sealed class MockVNavmeshAdapter : IVNavmeshAdapter
{
    public bool IsReady => true;
    public float BuildProgress => -1f;
    public bool IsPathRunning { get; private set; }
    public bool IsPathfindInProgress => false;
    public int WaypointCount => Route.Count;
    public IReadOnlyList<Vector3> Waypoints => Route;
    public Vector3? Reachable { get; init; }
    public IReadOnlyList<Vector3> Route { get; init; } = [];
    public int NearestCalls { get; private set; }
    public int PathfindCalls { get; private set; }
    public int StartCalls { get; private set; }

    public Vector3? FindNearestReachable(Vector3 point, float horizontalRadius, float verticalRadius)
    {
        NearestCalls++;
        return Reachable;
    }

    public Task<IReadOnlyList<Vector3>> FindPathAsync(Vector3 origin, Vector3 destination, float tolerance)
    {
        PathfindCalls++;
        return Task.FromResult(Route);
    }

    public bool StartPath(IReadOnlyList<Vector3> waypoints, float tolerance)
    {
        StartCalls++;
        IsPathRunning = waypoints.Count >= 2;
        return IsPathRunning;
    }

    public void Stop() => IsPathRunning = false;
}
