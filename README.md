# PvPSentinel

PvPSentinel is an experimental Dalamud plugin for Frontline strategy, safe navigation, match lifecycle management, and modular native PvP combat. PvPSentinel can remain the strategic brain and navigation owner while an independent external combat system handles job actions, or run its first native Machinist module in a read-only observer mode.

MMOMinion, AnyoneCore, Anyone's Champion, ChampionMachinist, TensorCore, and TensorReactions are not dependencies. The native implementation uses public Dalamud/game state and locally verified action data only.

## Current development milestone

Implemented in source:

- fail-closed Frontline player classification with `Friendly`, `Enemy`, and `Unknown` outcomes
- positive party/alliance/roster membership taking precedence over contradictory hostile flags
- all five current Frontline campaigns identified by language-neutral content and territory IDs:
  - The Borderland Ruins (Secure)
  - Seal Rock (Seize)
  - The Fields of Glory (Shatter)
  - Onsal Hakair (Danshig Naadam)
  - Worqor Chirteh (Triumph)
- shared friendly clustering, main-force tracking, threat evaluation, target scoring, and ranged-position strategy
- map-specific objective research policies and diagnostics for Secure, Seize, Shatter, Danshig Naadam, and Triumph
- generated-path-only vnavmesh navigation: no movement starts until a returned path passes validation
- path request timeout, path-start verification, progress/stuck detection, bounded repath backoff, and a consecutive-failure pause
- optional long-distance Mount Roulette use with combat, casting, nearby-enemy, arrival, and transition gates
- combat provider modes:
  - `Off`
  - `External Combat / ACR`
  - `Native PvPSentinel (experimental)`
- native development modes:
  - `Shadow / Observe` (default): evaluates and logs, but the combat subsystem cannot change target, use an action, or move
  - `Active`: permits the same independently implemented decision pipeline to issue verified native actions
- a job-neutral native combat provider and `IPvpJobCombatModule` boundary, with the initial Machinist module separated into state, Analysis, burst, execute, and pressure controllers
- contextual native target evaluation using validity, PvP classification, range, percentage and absolute HP, shields, Guard, observable allied focus, execute potential, current-target stickiness, local support, and overextension
- a configurable target-score safety floor that returns `WOULD TARGET: NONE` instead of choosing a least-bad unsafe/overextended enemy; safety overrides target stickiness and burst commitment
- configurable commitment time and meaningful score advantage before a valid native target can be replaced
- hard defensive preemption: an eligible Recuperate, urgent Purify, or threat-aware Guard prevents burst/pressure fall-through even when the external ACR has already consumed the defensive cooldown
- common PvP defense for repeatable sub-75% Recuperate; Stun, Heavy, Bind, Silence, Deep Freeze, and Miracle of Nature Purify with Resilience protection; and a tunable Guard threshold that rises with targeters, enemy density, numerical disadvantage, and observed rapid HP loss; automatic Elixir remains intentionally disabled
- independent MCH policy for Analysis/tool preservation, Wildfire target memory and Full Metal continuation, conservative contextual Marksman's Spite confidence, Dervish, proactive Bishop Autoturret, and normal pressure
- structured native diagnostics including `WOULD TARGET`, `WOULD TARGET: NONE`, `WOULD USE`, competing/rejected scores, defensive preemption, dynamic Guard factors, Purify and LB rejection reasons, action-layer resolution, HP/MP, allied focus, tools, Wildfire, Overheated, and limit gauge
- external-combat coordination based only on local combat, casting, and queued-action state, with a configurable resume grace period
- Daily Challenge: Frontline inspection through the game's Duty Finder, including localized game-data names
- per-map queue allow-list semantics: an unchecked active campaign means “do not queue today”
- automatic PvPSentinel-owned queue submission, owned duty acceptance, match observation, completion counting, and requeue
- configurable session match limit and a latched emergency stop
- detailed diagnostics for classification, map, objectives, group choice, path state, mounting, combat yield, and queue lifecycle
- pure logic tests for classification priority, all five map identifiers, generated-path rejection rules, external-combat yield timing, Recuperate boundary/MP rules, defensive preemption, target-score acceptance, target-switch hysteresis, dynamic Guard escalation/capping, and conservative contextual Marksman's Spite confidence

Objective navigation and queue automation are deliberately separate opt-ins. Objective observations remain visible while objective navigation is off, allowing live object IDs and states to be validated before they steer movement. Secure and Triumph passive capture objects currently remain research-only unless the client exposes positive active-state evidence.

## Safety model

Configuration version 4 preserves the earlier fail-closed automation migration, keeps new Native installations in `Shadow / Observe`, and migrates v0.2.0.0 observers to the safer target, Guard, and Marksman's Spite defaults. Master enable, navigation, mounting, objective navigation, queue automation, and the combat provider all require explicit selection. Selecting Native does not silently activate combat execution.

Strategic movement requires:

1. master and navigation enabled;
2. a recognized Frontline duty and live local player;
3. authoritative classification with no observed `Unknown` player;
4. a reliable friendly cluster or field-validated objective destination;
5. vnavmesh ready; and
6. a generated path whose start, endpoint, coordinates, and total length pass validation.

PvPSentinel stops only the vnavmesh path it owns. It never falls back to running directly at a group or objective coordinate. Repeated path failures pause navigation until the destination materially changes or automation is reset.

In `External Combat / ACR` mode, PvPSentinel does not set targets or execute combat actions. It has no IPC contract with MMOMinion or Champion. Local combat, casting, or queued-action state stops PvPSentinel-owned navigation; strategic travel resumes after those signals clear and the configured grace period expires.

In Native `Shadow / Observe`, the provider runs target, defense, execute, burst, tool, utility, and pressure evaluation but never invokes the target/action executor. Native combat contains no movement code; strategic navigation remains a separate subsystem. Active mode is still gated by master enable, a recognized Frontline, authoritative player classification, a live supported job, a valid action context, local action-data verification, and client-reported action readiness.

Queue automation accepts only a queue session submitted by the current PvPSentinel process. A pre-existing or unrelated Duty Ready prompt is reported but not accepted. If the daily campaign cannot be identified as exactly one allowed map, no queue action is taken.

The emergency stop immediately stops owned movement, attempts to cancel a PvPSentinel-owned queue, latches lifecycle automation, disables every action-capable switch, and sets the combat provider to `Off`. Clearing the latch does not re-enable any switch.

## Architecture

```text
PvPSentinel
├── GameState       Dalamud/Lumina state, team classification, local action state
├── Intelligence    clustering, main-force hysteresis, target scoring
├── Strategy        shared tactics and per-map objective research policies
├── Behavior        high-level state and safety decisions
├── Navigation      mount control, generated path validation, stuck/repath logic
├── Combat          Off/external/native provider coordination
│   └── Native      target evaluation, common defense, action executor, job modules
│       └── Jobs
│           └── Machinist  state, Analysis, burst, execute, utility, pressure
├── Queue           daily-map detection and owned Frontline lifecycle
├── Models          immutable diagnostic snapshots
└── UI              fail-closed configuration and development diagnostics
```

## Build and tests

Prerequisites:

- Windows with XIVLauncher/Dalamud installed
- current Dalamud API 15 development assemblies in `%AppData%\XIVLauncher\addon\Hooks\dev`
- .NET 10 SDK

From the repository root:

```powershell
dotnet restore PvPSentinel.csproj
dotnet build PvPSentinel.csproj -c Release --no-restore
dotnet run --project Tests/PvPSentinel.LogicTests.csproj -c Release
```

The development DLL is written to `bin\Release\PvPSentinel.dll`; the Dalamud SDK also produces `bin\Release\PvPSentinel\latest.zip`.

## Staged live validation

Do not begin with movement, combat, objectives, or queue automation enabled.

### Stage A — passive classification and map diagnostics

- master: off
- navigation: off
- mounting: off
- objective navigation: off
- queue automation: off
- combat provider: `Off`

Inside Frontline, expand nearby-player diagnostics at spawn and during a fight. Confirm every visible teammate is `Friendly`, opponents are `Enemy` even when their raw hostile flag is false, and no observed player is `Unknown`. Record declared/resolved alliance counts if classification is not reliable.

Also capture the objective-research rows for the active map: name, base ID, object kind, targetability, HP, position, and observed live state. This evidence is required before widening map-specific objective authorization.

### Stage B — validated group navigation

- master: on
- navigation: on
- combat provider: `Off`
- objective navigation: off

Verify every move passes through `RequestingPath` and `PathValidated`/`FollowingPath`. Confirm blocked terrain produces repath/backoff or a failure pause, never direct travel toward the coordinate. Test death, respawn regrouping, long-distance mounting, close-range dismounting, and the emergency stop.

### Stage C — external combat coordination

- master: on
- navigation: on
- combat provider: `External Combat / ACR`

Verify PvPSentinel stops its owned path while the local player is in combat, casting, or has a queued action, then generates/resumes strategic travel after the grace period. External targeting and actions should remain entirely independent.

### Stage D — daily campaign and lifecycle

Keep the match limit at `1`. First verify the detected campaign and allowed-map decision without submitting. Then opt into queue automation and observe selection, join, Duty Ready acceptance, match count, natural duty exit, limit handling, and requeue behavior.

### Stage E — native MCH shadow comparison

- master: on
- navigation: as required for the strategic test
- combat provider: `Native PvPSentinel (experimental)`
- native development mode: `Shadow / Observe`
- verbose logging: on

Run the external ACR normally and compare its observed choices with `WOULD TARGET` and `WOULD USE`. Confirm there are no PvPSentinel-originated target changes or actions. Record target scores/switches, defensive threshold decisions, primed-tool and Analysis state, Wildfire state, allied focus, limit gauge, and rejection details. Do not select Active until the shadow trace has been reviewed.

For v0.2.0.1, specifically verify that unsafe candidates produce `WOULD TARGET: NONE`, eligible defense produces `OFFENSE SUPPRESSED`, Guard thresholds rise before focus-fire deaths, and Purify/LB rejections state their confidence reason. The manually triggered limit break at match clock 11:52 in the first comparison trace is excluded from external-provider behavior analysis.

The first live pass must specifically validate:

- whether player `TargetObjectId` observations reliably represent allied focus and incoming enemy pressure in a 72-player Frontline;
- current PvP status visibility/ownership for Guard, Resilience, removable controls, Analysis/tool priming, Overheated, Wildfire, and Chain Saw vulnerability;
- PvP limit-gauge unit scaling and Marksman's Spite readiness across death/respawn;
- self/enemy target semantics and client action-readiness results for Recuperate, Purify, Guard, Dervish, Bishop, Wildfire, and Full Metal Field;
- whether the equipped Frontline role action makes Dervish locally available while Bravery/Eagle Eye remain cleanly unavailable;
- target hysteresis and overextension behavior during dense fights; and
- Wildfire target retention, four-hit progression, expiry, invalid-target abandonment, and unsafe-continuation abandonment.

Active Native MCH remains blocked by release discipline, not by architecture: it is an explicit local development selection and requires the shadow findings above before live control testing.

## Release discipline

This work must not be distributed from an untagged development checkpoint. A release requires:

- clean current-API build and passing logic tests;
- reviewed Native MCH Shadow / Observe traces with no target/action mutations attributable to PvPSentinel;
- Stage A classification evidence;
- staged live path, mount, external-yield, campaign-detection, and queue-lifecycle validation;
- any required fixes from those tests;
- a version bump and matching `v<Version>` tag; and
- a subsequent `MarshalTitan/Sentinel` catalog update only after the release asset succeeds.

Until those gates pass, do not create a release tag or update the public catalog.
