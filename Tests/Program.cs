using System.Numerics;
using PvPSentinel.Combat;
using PvPSentinel.Combat.Native;
using PvPSentinel.GameState;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

var failures = new List<string>();

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

Check("Recuperate eligible below 75%", true,
    NativeCombatPolicy.RecuperateEligible(74.9f, 75f, 2000));
Check("Recuperate not eligible at 75%", false,
    NativeCombatPolicy.RecuperateEligible(75f, 75f, 10000));
Check("Recuperate requires MP", false,
    NativeCombatPolicy.RecuperateEligible(20f, 75f, 1999));
Check("target commitment blocks early switch", false,
    NativeCombatPolicy.ShouldSwitchTarget(1.9, 2f, 40f, 18f));
Check("target score threshold blocks small switch", false,
    NativeCombatPolicy.ShouldSwitchTarget(3, 2f, 17.9f, 18f));
Check("meaningful target improvement switches", true,
    NativeCombatPolicy.ShouldSwitchTarget(3, 2f, 18f, 18f));
Check("Marksman focus allowance is contextual", 56000u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 2, 8000, 72000));
Check("Marksman allowance respects cap", 72000u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 9, 8000, 72000));
Check("Marksman includes reliable Chain Saw modifier", 48000u,
    NativeCombatPolicy.MarksmanEffectiveHpAllowance(40000, 0, 8000, 72000, 1.2f));

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} logic test(s) failed:");
    foreach (var failure in failures)
        Console.Error.WriteLine("- " + failure);
    return 1;
}

Console.WriteLine("PvPSentinel logic tests passed (classification, maps, paths, external yield, native combat policies). ");
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
