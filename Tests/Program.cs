using System.Numerics;
using PvPSentinel.Combat;
using PvPSentinel.Combat.Native;
using PvPSentinel.Combat.Native.Jobs.Machinist;
using PvPSentinel.Combat.Threat;
using PvPSentinel.GameState;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

var failures = new List<string>();

Check("existing Native provider configuration value is preserved", 2, (int)CombatProvider.NativePvPSentinel);
Check("Reborn provider uses a new configuration value", 3, (int)CombatProvider.RotationSolverReborn);

Check("local is friendly", PlayerClassification.Friendly,
    FrontlinePlayerResolver.Classify(true, true, true, false, false, false, true));
Check("roster membership wins contradictory hostile flag", PlayerClassification.Friendly,
    FrontlinePlayerResolver.Classify(false, true, true, true, false, false, true));
Check("alliance flag wins contradictory hostile flag", PlayerClassification.Friendly,
    FrontlinePlayerResolver.Classify(false, true, true, false, false, true, true));
Check("hostile is enemy without roster inference", PlayerClassification.Enemy,
    FrontlinePlayerResolver.Classify(false, true, false, false, false, false, true));
Check("reliable Frontline non-member is enemy", PlayerClassification.Enemy,
    FrontlinePlayerResolver.Classify(false, true, true, false, false, false, false));
Check("unresolved non-member stays unknown", PlayerClassification.Unknown,
    FrontlinePlayerResolver.Classify(false, true, false, false, false, false, false));
Check("non-Frontline non-member stays unknown", PlayerClassification.Unknown,
    FrontlinePlayerResolver.Classify(false, false, true, false, false, false, false));

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
    Check($"display name round trip {item.Map}", item.Map, FrontlineMapCatalog.IdentifyText(item.Map.DisplayName()));
}

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
    rebornEngagement.Update(rebornStart, false, false, false, false, true, false, false, 3, false, 5f).State);
Check("active Reborn begins in transit", ExternalEngagementState.Transit,
    rebornEngagement.Update(rebornStart, true, false, false, false, false, false, false, 0, false, 5f).State);
Check("mount cast does not latch a combat engagement", false,
    rebornEngagement.Update(rebornStart.AddSeconds(1), true, false, false, true, false, true, true, 0, false, 5f).ShouldYield);
Check("combat latches the Reborn engagement", true,
    rebornEngagement.Update(rebornStart.AddSeconds(2), true, false, false, false, true, false, false, 4, false, 5f).ShouldYield);
Check("nearby enemies hold engagement after combat flag clears", true,
    rebornEngagement.Update(rebornStart.AddSeconds(3), true, false, false, false, false, false, false, 2, false, 5f).ShouldYield);
Check("clear battlefield starts quiet period", true,
    rebornEngagement.Update(rebornStart.AddSeconds(4), true, false, false, false, false, false, false, 0, false, 5f).ShouldYield);
Check("quiet period continues before configured boundary", true,
    rebornEngagement.Update(rebornStart.AddSeconds(8.9), true, false, false, false, false, false, false, 0, false, 5f).ShouldYield);
var rebornCleared = rebornEngagement.Update(
    rebornStart.AddSeconds(9), true, false, false, false, false, false, false, 0, false, 5f);
Check("strategic travel resumes after clear quiet period", false, rebornCleared.ShouldYield);
Check("cleared engagement returns to transit", ExternalEngagementState.Transit, rebornCleared.State);
Check("death releases navigation yield", false,
    rebornEngagement.Update(rebornStart.AddSeconds(10), true, true, false, false, true, false, false, 5, false, 5f).ShouldYield);
Check("death is reported distinctly", ExternalEngagementState.Dead,
    rebornEngagement.Update(rebornStart.AddSeconds(10.1), true, true, false, false, false, false, false, 0, false, 5f).State);
Check("respawn enters regroup state without combat yield", ExternalEngagementState.Regrouping,
    rebornEngagement.Update(rebornStart.AddSeconds(11), true, false, false, false, false, false, false, 0, true, 5f).State);

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

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} logic test(s) failed:");
    foreach (var failure in failures)
        Console.Error.WriteLine("- " + failure);
    return 1;
}

Console.WriteLine("PvPSentinel logic tests passed (provider compatibility, classification, maps, paths, external/Reborn engagement, native combat, and shared threat policies).");
return 0;

void Check<T>(string name, T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{name}: expected {expected}, got {actual}");
}

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
