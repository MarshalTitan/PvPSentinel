# PvPSentinel

PvPSentinel is an experimental Dalamud plugin for Frontline strategy, safe navigation, match lifecycle management, and modular PvP combat providers. PvPSentinel can remain the strategic brain and navigation owner while RotationSolverReborn or another independent external combat system handles job actions, or run its first native Machinist module in a read-only observer mode.

RotationSolverReborn, MMOMinion, AnyoneCore, Anyone's Champion, ChampionMachinist, TensorCore, and TensorReactions are not required dependencies. The optional Reborn provider uses only installation metadata and read-only public IPC; the native implementation uses public Dalamud/game state and locally verified action data only.

## Current development milestone

Implemented in source:

- authoritative Frontline relationship classification from Dalamud's public native `Character.Battalion` value: local entity ID first, then same zero-based team (`0-2`) ally, different valid team enemy, otherwise unknown; `friendly`, `targetable`, `attackable`, and `Hostile` flags are diagnostic only
- a normalized, map-independent `BattlefieldState` covering self, match lifecycle, entity-keyed player history/staleness, relationship confidence, allied/enemy clusters, objectives, combat context, death/respawn, and independently failing sensor health
- all five current Frontline campaigns identified by language-neutral content and territory IDs:
  - The Borderland Ruins (Secure)
  - Seal Rock (Seize)
  - The Fields of Glory (Shatter)
  - Onsal Hakair (Danshig Naadam)
  - Worqor Chirteh (Triumph)
- deterministic 20y connected-component allied/enemy clustering with centroid, staleness, combat activity, local distance, nearby objective, and numerical balance
- a Shatter adapter with all 19 persistent logical objectives, verified large/small lifecycle IDs, activation ETA/strength parsing, and physical-object confirmation
- a Seal Rock adapter that aggregates paired map records by stable location and tracks verified B/A rank and neutral/Maelstrom/Adders/Flames state evidence without guessing unresolved S-rank IDs or PvP-team-to-GC mappings
- a discovery-first Borderland Ruins adapter for territory 1273 / duty 127 that aggregates effectively identical marker coordinates into stable `SEC-xx` locations, retains raw marker/object/text evidence and vetted physical `SEC-OBJ-xxx` observations, suppresses live-confirmed transient/Trader non-destinations and presentation-only 480/486/0/countdown/HP transition churn, and deliberately leaves name/type/state/ownership/tactical meaning `UNRESOLVED`
- shared discovery-first Onsal Hakair (territory 888 / duty 701) and Worqor Chirteh (territory 1313 / duty 1080) adapters that keep bounded raw marker/object research separate from strongly stable, clickable `ONS-xx`/`WOR-xx` destinations; Worqor named Triumph text now identifies activating, unclaimed, and claimed phases while claimed-team ownership remains unresolved
- normalized `OUTSIDE`/`PRE_MATCH`/`MATCH_ACTIVE`/terminal `RESULTS`, reliable death/respawn transitions, and retained match summaries before adapter reset
- normalized `NONE`/`PVP_ENEMY`/`OBJECTIVE_COMBAT`/`STALE_GAME_COMBAT`; confirmed enemy-player combat pauses the owned route while preserving its destination, while objective or stale generic combat cannot permanently block travel
- one manual M2 vnavmesh owner with arm/disarm, immediate STOP, reachable-point snapping, explicit generated path acquisition, actual waypoint inspection, arrival, cancellation, bounded stuck recovery, failed-corridor comparison, vnavmesh-generated local departure stages, alternate destination approaches, and clean ownership release
- no direct steering fallback: movement never starts until a vnavmesh route passes validation
- privacy-sanitized per-match `events.jsonl` plus coordinate-complete `summary.json`, with state-diff/rate-limited diagnostics and a target-counter correlation timeline rather than continuous UI dumps
- optional long-distance preferred-mount use, defaulting to Company Chocobo and populated from the character's unlocked Mount-sheet rows, with combat, casting, nearby-enemy mount-start, arrival, and transition gates and no random fallback
- combat provider modes:
  - `Off`
  - `External Combat / ACR`
  - `RotationSolverReborn (External)`
  - `Native PvPSentinel (experimental)`
- native development modes:
  - `Shadow / Observe` (default): evaluates and logs, but the combat subsystem cannot change target, use an action, or move
  - `Active`: permits the same independently implemented decision pipeline to issue verified native actions
- a job-neutral native combat provider and `IPvpJobCombatModule` boundary, with the initial Machinist module separated into state, Analysis, burst, execute, and pressure controllers
- contextual native target evaluation using validity, PvP classification, range, percentage and absolute HP, shields, Guard, observable allied focus, execute potential, current-target stickiness, local support, and overextension
- a configurable target-score safety floor that returns `WOULD TARGET: NONE` instead of choosing a least-bad unsafe/overextended enemy; safety overrides target stickiness and burst commitment
- configurable commitment time and meaningful score advantage before a valid native target can be replaced
- hard defensive preemption: an eligible Recuperate, urgent Purify, or threat-aware Guard prevents burst/pressure fall-through even when the external ACR has already consumed the defensive cooldown
- common PvP defense for repeatable sub-75% Recuperate; Stun, Heavy, Bind, Silence, Half-asleep, Sleep, and Deep Freeze Purify with Resilience protection; and a tunable Guard threshold that rises with targeters, enemy density, numerical disadvantage, and observed rapid HP loss; automatic Elixir remains intentionally disabled
- a shared `PvPThreatTracker` used by both Native defense and the UI, reporting currently observed enemy hard targets, their jobs/distances, nearby enemy/friendly density, and a derived threat level
- an optional movable targeting-me counter, enabled by default only in PvP and hidden at zero, with position locking, count/job-text sizes, a borderless floating presentation, and an optional job-abbreviation row
- dedicated high-resolution counter glyphs, built locally through Dalamud's managed font atlas so large numbers remain sharp without copying or depending on AnyoneCore image assets
- independent MCH policy for Analysis/tool preservation, Wildfire target memory, Full Metal continuation and focus-aware anti-overkill initiation, conservative contextual Marksman's Spite confidence, Dervish, proactive Bishop Autoturret, and normal pressure
- a Shadow action ledger that applies hypothetical animation locks, cooldown recovery, and limit-gauge spending without ever changing target, issuing an action, or moving the player
- structured native diagnostics including `WOULD TARGET`, `WOULD TARGET: NONE`, `WOULD USE`, competing/rejected scores, defensive preemption, dynamic Guard factors, Purify and LB rejection reasons, action-layer resolution, HP/MP, allied focus, tools, Wildfire, Overheated, and limit gauge
- generic external-combat coordination based only on local combat, casting, and queued-action state, with a configurable resume grace period
- optional RotationSolverReborn detection and read-only status/action-event diagnostics without invoking Reborn's PvP-blocked state-changing IPC
- a stable Reborn engagement latch that yields owned navigation through actual combat and observed hard-target pressure, then resumes after a short configurable quiet period; passive enemy proximity cannot starve a preserved route, death releases the latch for respawn/regroup, and Sentinel's own pending mount cast is not treated as combat
- fail-closed navigation when the Reborn provider is selected but the plugin, status IPC, or autorotation is unavailable
- an explicit MCH limit-gauge warning because Reborn's current MCH PvP rotation does not automate Marksman's Spite
- Daily Challenge: Frontline inspection through the game's Duty Finder, including localized game-data names
- per-map queue allow-list semantics: an unchecked active campaign means “do not queue today”
- the existing queue/lifecycle implementation retained but forcibly disabled during manual M2 validation
- configurable session match limit and a latched emergency stop
- detailed battlefield and M2 diagnostics for map/adapter, lifecycle/results, PvP team, relationship counts, clusters, objective state/rank/owner/source/confidence, current-map research evidence, combat context, death/respawn, sensor health, mesh readiness/build progress, movement ownership, snapped destination, waypoints, route progress, separate stuck/path-failure counts, and retained summary
- safe manual-M2 preferred mounting for long routes, with live game-data/unlock validation, latched mount-transition waits, stable mounted travel until confirmed combat/arrival, route-distance-aware dismounting, preserved generated-route stages, and explicit failure diagnostics
- collapsible Configuration groups plus a Development window organized around an always-visible Testing Controls foldout and separate Frontline/Team, Battlefield/Sensors, Threat, Objectives/Research, M2 Navigation, Combat, and Lifecycle foldouts
- pure logic tests for team classification, stale eviction, deterministic clustering, Shatter transitions, Seal Rock paired aggregation/ownership, Secure coordinate aggregation/stability/unresolved-state safety/evidence transitions, Onsal/Worqor discovery promotion and raw-only isolation, all-map adapter registration, terminal results, combat normalization, retained summaries, route comparison, bounded recovery/ownership, mocked vnavmesh planning, logging diff/rate limits, and privacy, in addition to the existing combat/provider suite

Generic all-map autonomous strategy, automatic objective capture, and native queue/requeue remain disabled. Objective buttons change with the current map: Shatter exposes A1–A4/B1–B15, Seal Rock populates discovered logical locations dynamically, Secure creates buttons only from bounded stationary objective-like evidence, and Onsal/Worqor promote only stable discovery evidence. The field-confirmed Secure geometric center uses the stable `SEC-CENTER` identity; other `SEC-xx`, `ONS-xx`, and `WOR-xx` values are session-scoped and their displayed coordinates are authoritative. Raw observations remain research-only. Worqor has a separate opt-in group destination mode for fresh unclaimed or soon-activating Triumphs supported by allies. Seal Rock and Shatter have separate opt-in supervised group pilots for their verified objective markers and visible allied field groups. Captured Seal Rock and claimed Triumph ownership remain `UNRESOLVED`; Onsal objective semantics remain `UNRESOLVED`.

## Safety model

Configuration version 15 preserves the earlier fail-closed combat migration, keeps Native in `Shadow / Observe`, forcibly turns off generic autonomous strategy, objective navigation, and queue/requeue, and defaults the preferred Mount-sheet selection to Company Chocobo. It also migrates the exact legacy five-second Reborn quiet default to two seconds while preserving any deliberately customized value. The Worqor, Seal Rock, and Shatter group pilots each require an explicit opt-in. Manual movement still requires explicit master enable, navigation enable, arming, and destination selection; a pilot arms its own route when selected. Map discovery does not change Combat Provider configuration. Selecting Native does not silently activate combat execution.

The targeting-me counter is deliberately limited to enemy players currently loaded and classified whose observable hard target is the local player. It cannot detect soft targeting, queued attacks, future intent, or an enemy that has not yet switched its observable target. The HUD shows every such observed hard target; Guard consumes the within-30y subset from that same shared observation so the live-validated defensive tuning is not silently widened.

Manual M2 movement requires:

1. master and navigation enabled;
2. a recognized Frontline duty and live local player;
3. an armed manual controller and a freshly selected discovered objective or allied-cluster destination;
4. no confirmed enemy-player combat or death state;
5. vnavmesh ready and a reachable snapped approach point; and
6. a generated path whose start, endpoint, coordinates, and total length pass validation.

PvPSentinel stops only the vnavmesh path it owns. It never falls back to running directly at a group or objective coordinate. Death cancels the destination and requires a fresh manual request. Confirmed enemy-player combat pauses the route, preserves the destination, and generates a fresh path from the post-fight position after the combat provider releases movement. Repeated failures are bounded; materially identical replacements and short detours that continuously rejoin a failed corridor are rejected. Sentinel then requests a generated path to a bounded side/back departure stage and retries the original destination from the new origin; it never steers directly to that stage.

In `External Combat / ACR` mode, PvPSentinel does not set targets or execute combat actions. It has no IPC contract with MMOMinion or Champion. Local combat, casting, or queued-action state stops PvPSentinel-owned navigation; strategic travel resumes after those signals clear and the configured grace period expires.

In `RotationSolverReborn (External)` mode, Reborn exclusively owns local combat targeting, actions, cooldowns, and ordinary PvP defensives. PvPSentinel reads installation/autorotation status and announced next-action events but never calls Reborn's PvP-blocked control endpoints. Once local combat starts, PvPSentinel stops its owned path but preserves the strategic/manual destination. After the combat flag clears, the shared threat tracker reports no enemy inside the configured radius observably hard-targeting the player, and the short quiet period expires, Sentinel discards the interrupted route, repaths from the current position, and resumes travel. Merely nearby enemies remain visible in diagnostics but do not reset the timer. A renewed real engagement immediately returns movement ownership to combat. Selecting another destination while paused replaces the preserved destination without taking movement back from combat. Death and explicit STOP still cancel the destination. Strategic navigation is fail-closed if Reborn cannot be confirmed active.

In Native `Shadow / Observe`, the provider runs target, defense, execute, burst, tool, utility, and pressure evaluation but never invokes the target/action executor. Native combat contains no movement code; strategic navigation remains a separate subsystem. Active mode is still gated by master enable, a recognized Frontline, authoritative player classification, a live supported job, a valid action context, local action-data verification, and client-reported action readiness.

Queue/accept/requeue code remains preserved, but v0.3.1.1 does not invoke it. The configuration UI reports the M2 lock instead of offering an automation toggle.

The emergency stop immediately stops PvPSentinel-owned movement, attempts to cancel a PvPSentinel-owned queue, latches lifecycle automation, disables every PvPSentinel action-capable switch, and sets the combat provider to `Off`. External plugins remain independent and must be stopped through their own controls. Clearing the Sentinel latch does not re-enable any switch.

## Architecture

```text
PvPSentinel
├── GameState       Dalamud/Lumina state, team classification, local action state
├── FrontlineCore   normalized players/clusters/lifecycle/combat/sensors/summaries
│   └── Maps        Secure, Shatter, Seal Rock, Onsal/Worqor discovery adapters
├── Intelligence    clustering, main-force hysteresis, target scoring
├── Strategy        shared tactics and per-map objective research policies
├── Behavior        high-level state and safety decisions
├── Navigation      single owner, manual M2 controls, vnavmesh path/route recovery
├── Combat          Off/generic external/Reborn/native provider coordination
│   ├── Threat      shared hard-target/density tracking for defense and HUD
│   ├── Reborn      read-only presence/status/action IPC and engagement latch
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

### Stage A — passive battlefield and objective sensing

- master: off
- navigation: off
- mounting: off
- objective navigation: off
- queue automation: off
- combat provider: `Off`

Inside Frontline, confirm local PvP team is one of the live-validated zero-based Battalion values `0-2`, SELF remains exactly one, teammates use the same team value, and opponents use the other values even when their raw friendly flag is true or targetability changes at distance. Do not infer Grand Company names from the numeric values.

On Shatter, confirm all 19 logical rows appear and compare inactive/preactivating/active, ETA, strength, coordinates, and physical confirmations against the map. On Seal Rock, confirm no duplicate logical row oscillates when paired marker records occupy the same coordinate; verify rank and observed GC ownership changes. Unresolved values must remain explicit.

For the Shatter supervised pilot, enable navigation, select RotationSolverReborn and confirm autorotation active, then turn on **Shatter group navigation (opt-in)**. Begin the match with the map closed and check whether the one-time marker bootstrap occurs if ice markers are missing. Watch whether the pilot chooses a fresh active or soon-activating objective with allied support, otherwise travels to a visible field group. Confirm that combat does not change a committed destination, death causes a new choice after respawn, manual buttons take priority, and STOP turns the pilot off. Record the complete match events and summary, plus video of a wrong destination, wall, stall, or failed respawn. Queue/requeue remains manual.

On Secure, leave manual navigation disarmed for the opening scan. Confirm the adapter reports territory 1273 / duty 127, repeated marker records at one coordinate collapse into one location, the geometric center appears as `SEC-CENTER`, and other session labels visibly include X/Z coordinates. Names/types/state/owners remain `UNRESOLVED`. Observe at least one capture/loss and one central or temporary object so marker changes, physical objects, and `_WideText` evidence can be correlated after the match.

On Onsal or Worqor, leave manual navigation disarmed for at least 10 seconds. Confirm the explicit discovery adapter and territory/duty pair, verify raw marker families remain research-only, and use only promoted `ONS-xx`/`WOR-xx` buttons whose labels include X/Z coordinates. On Worqor, named Triumph markers expose activating, unclaimed, and claimed states and rank; claimed owner remains unresolved. The separate Worqor group navigation option is off by default and should be enabled only for a supervised live match with RotationSolverReborn autorotation active. Confirm that destinations appear without manually opening the map, a supported fresh Triumph is chosen, and the commitment persists through combat. On death, a cancelled route is cleared before checking Reborn readiness; after respawn, a visible field group becomes a one-trip fallback when no fresh supported Triumph is available. STOP disables the mode.

On Seal Rock, the separate group navigation pilot is off by default. In a supervised match with active Reborn autorotation and ready vnavmesh, enable it and keep the map closed initially. Confirm that fresh neutral tomelith markers are observed without a manual map open, that a supported node is selected only when allies are nearby, and that captured/unknown nodes are ignored. If no safe neutral node is available, it should travel once to a visible field cluster. Observe commitment through combat, arrival hold, and death/respawn reselection. A manual destination must take priority; STOP must turn the pilot off, while bounded route failure must pause it. Save events, summary, and a video of any wall, stall, or incorrect choice.

### Stage B — manual M2 vnavmesh navigation

- master: on
- navigation: on
- manual navigation: armed in Diagnostics
- combat provider: `Off`
- autonomous strategy/objective navigation/queue: locked off

Select one objective at a time. Verify `Snapped Position`, actual `Waypoint Count`/`Next Waypoint`, `RequestingPath`, `Following`, and `ARRIVED`. Choose routes that cross ramps, walls, elevation changes, and the map center. Confirm a stall stops the path, emits `navigation_recovery_started`, follows only a generated local departure-stage path, emits `navigation_recovery_stage_arrived`, and then requests the original destination from the new origin. It must eventually arrive or fail/release ownership without direct steering. Test immediate STOP and death cancellation. For enemy-player combat, confirm `navigation_yielded_external_combat`, no Sentinel path movement during the fight, then `navigation_resumed_after_combat`, a newly generated route from the post-fight position, and eventual `ARRIVED` without selecting the destination again.

### Stage C — RotationSolverReborn external combat coordination

- master: on
- navigation: on
- combat provider: `RotationSolverReborn (External)`
- Reborn auto-enable at PvP start: on
- Reborn auto-disable when dead/after combat: off
- Reborn auto-disable at match end: on

Verify installed/loaded/version/autorotation diagnostics, mounted transit, first engagement, persistent combat ownership, death/respawn regrouping, and match-end deactivation. Reborn must own every local target and combat action. PvPSentinel must hold navigation throughout actual combat and while an observed enemy inside the targeting-threat radius hard-targets the player, then repath after the quiet period even if unrelated enemies remain nearby. Marksman's Spite remains manual in this first provider build.

### Stage D — lifecycle observation only

Observe pre-match, active, results, and map exit. Confirm RESULTS remains terminal even while the header/timer is visible and that the retained summary is written before the adapter resets. Do not test automatic queue/accept/requeue in this milestone.

After each manual test, upload the displayed M2 log directory's `events.jsonl` and `summary.json` together with the corresponding `dalamud.log`. The per-match directory is shown in Diagnostics; leaving the Frontline writes the retained summary.

### Stage E — native MCH shadow comparison

- master: on
- navigation: as required for the strategic test
- combat provider: `Native PvPSentinel (experimental)`
- native development mode: `Shadow / Observe`
- verbose logging: on

Run the external ACR normally and compare its observed choices with `WOULD TARGET` and `WOULD USE`. Confirm there are no PvPSentinel-originated target changes or actions. Record target scores/switches, defensive threshold decisions, primed-tool and Analysis state, Wildfire state, allied focus, limit gauge, and rejection details. Do not select Active until the shadow trace has been reviewed.

The four-match v0.2.0.1 Shadow pass validated the safety refinement: no selected target fell below the configured score floor, no offensive decision bypassed an eligible defensive preemption, Guard rose as high as its configured 65% cap under severe focus, and the Purify evaluator recognized the removable controls observed in that trace. Therefore v0.2.0.2 preserved those combat thresholds rather than tuning toward Champion action counts.

The subsequent full Seal Rock comparison also held the score floor (the lowest selected score was exactly the configured `10.0` boundary), preserved defensive preemption, recognized all three observed Purify controls, and made Guard eligible before each of the six recorded deaths. It also confirmed that the compact target counter matched the independent observed count. The trace exposed decision-model gaps rather than unsafe threshold regressions: Shadow did not remember its own hypothetical Wildfire commitment, unaffordable Purify could be named as the preempting defense, and both Bioblaster and Scattergun were unnecessarily restricted to clustered targets. v0.2.0.4 corrects those issues while retaining the existing safety thresholds and Shadow default.

The v0.2.0.4 full-match comparison validated remembered Wildfire target continuation, the target-score floor, hard defensive preemption, the shared threat inputs, and the Analysis/Bioblaster policy. It also exposed a wider observer-model issue: because Shadow intentionally does not consume the live action, it could recommend Bishop, Full Metal Field, Analysis, a primed tool, Guard, Purify, or the same full limit gauge again before the external ACR changed client readiness. v0.2.0.5 adds a job-independent hypothetical action ledger, keeps those holds across transient target/death resets, and adds a focus-aware Wildfire survival floor so a heavily focused near-death target is not chosen for a fresh four-hit commitment.

The next 15-minute validation pass confirmed that ledger: Analysis, Bishop, Full Metal Field, Guard, Purify, Wildfire, and Marksman's Spite all respected their simulated recovery windows while lower-priority ready actions continued to fall through. It also exposed two narrower safety gaps. Two severely overextended targets barely cleared the numeric score floor after positive execute/focus bonuses, and one Marksman's Spite recommendation targeted only 7.5k effective HP while six allies were already focusing the victim. v0.2.0.6 makes severe overextension a hard target rejection, adds a focus-adjusted normal-range LB anti-overkill floor, and raises the default target commitment/switch advantage to reduce short-lived target churn. The manually used LB at approximately 16:58 on the match timer is excluded from Champion behavior analysis.

For the next Shadow pass, validate that severe-overextension candidates remain `WOULD TARGET: NONE`, that the LB conservation rejection appears on collapsing normal-range targets without suppressing legitimate long-range finishes, and that the stronger target hysteresis reduces short-lived switches without missing clear execute opportunities. Also continue checking Guard timing against the shared targeter/density trace. Native Active remains intentionally unrecommended until that pass is reviewed.

Manual actions excluded from external-provider behavior analysis are: the earlier comparison's LB at match clock 11:52; Standard-issue Elixir in match 2 at approximately 16:33:10 in the four-match trace; and Marksman's Spite against the Bard in match 3 at approximately 16:43:02. Automatic Elixir remains intentionally unimplemented.

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

This work must not be distributed from an untagged development checkpoint. A manual M2 test release requires:

- clean current-API build and passing logic tests;
- Native MCH remaining `Shadow / Observe` by default and all combat-provider boundaries preserved;
- autonomous strategy, capture, and queue/requeue locked off;
- clear live-validation labeling and event/summary logs for follow-up;
- a version bump and matching `v<Version>` tag; and
- a subsequent `MarshalTitan/Sentinel` catalog update only after the release asset succeeds.

Manual ARRIVED evidence around major terrain/elevation changes is still required before any autonomous strategy work is authorized.
