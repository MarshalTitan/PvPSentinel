# Changelog

## 0.3.0.2 — Secure discovery, route execution, and threat safety

- Separated raw Secure marker research from promoted manual navigation destinations. Live-verified moving marker families `60360/60361` remain bounded raw evidence and can never become `SEC-xx` buttons.
- Added evidence-tiered promotion requiring stable marker family, low drift, longer scan/age windows, or physical corroboration, plus hard bounds of 32 promoted locations, 128 promotion candidates, and 256 physical research objects.
- Replaced per-coordinate transient event spam with rate-limited raw-family summaries and buffered JSONL writes to reduce framework hitches.
- Preserved complete generated vnavmesh geometry and execute manual M2 routes in bounded stages at turns, elevation changes, and long straight intervals so stair/corner waypoints cannot be silently skipped.
- Added route/attempt IDs, raw and execution waypoint geometry, previous/current/next waypoint positions, stage targets, failed-corridor signatures, and materially-different replacement diagnostics.
- Decoupled the Targeting-Me counter from full PvP-team classification. It prefers positive PvP-team evidence and otherwise uses Dalamud-native Hostile flags with party/alliance/roster exclusions for hard-target observations only.
- Added privacy-safe `pvp_team_probe` events with raw local Battalion, StatusFlags, roster/team histograms, and hostile/targetable counts; strategic classification remains fail-closed when local Battalion is zero.
- Added regression coverage for transient Secure markers, promotion bounds, native-hostile fallback, protected stair stages, and repeated failed-corridor rejection.
- Preserved Shatter, Seal Rock, combat providers, Native Shadow default, disabled autonomous strategy, and disabled queue/requeue.

## 0.3.0.1 — Borderland Ruins discovery and live-test UI

- Added a dedicated discovery-first Borderland Ruins (Secure) adapter for runtime-verified territory 1273 / duty 127.
- Added coordinate aggregation and short stability qualification for raw map-marker evidence; stable locations receive session-persistent `SEC-xx` IDs without inferring objective name, type, lifecycle, owner, or tactical meaning.
- Added event-driven Secure marker appearance, evidence-transition, reappearance, and disappearance logging with raw IconId/DataId/ObjectiveId/EventState/end-time/text evidence.
- Added `SEC-OBJ-xxx` research tracking for nearby EventObj/BattleNpc evidence, including object/entity/base IDs, sanitized name, kind, position, targetability, HP, movement/state changes, and appearance/disappearance.
- Expanded `_WideText` lifecycle announcement capture for Secure-related evidence and mirrored discovery events into verbose Dalamud diagnostics.
- Added manual M2 buttons for stable `SEC-xx` locations while preserving explicit arming, generated-path validation, STOP, stuck recovery, route rejection, ARRIVED validation, and disabled autonomous destination selection.
- Expanded retained summaries with territory/duty, objective evidence, physical research objects, raw transition history, unresolved notes, and detailed navigation failure counters.
- Split manual navigation stuck count from path-failure count and added per-candidate `navigation_path_failed` events.
- Made major Configuration groups collapsible without changing any setting values, defaults, persistence, or ranges.
- Reorganized the Development window into collapsible Testing Controls, Frontline/Team, Battlefield/Sensors, Threat, Objectives/Research, M2 Navigation, Combat, and Lifecycle sections. Testing Controls and M2 Navigation open by default.
- Preserved every combat provider, Native Shadow default, threat/target counter, Shatter/Seal Rock adapter, lifecycle sensor, and manual-M2 safety gate.

## 0.3.0.0 — Manual M2 battlefield and navigation foundation

- Replaced Frontline roster/friendly-flag inference with self-first positive PvP-team classification from the public native character structure.
- Added normalized map-independent battlefield state, player staleness tracking, allied/enemy clusters, terminal results lifecycle, combat-context normalization, death/respawn state, sensor health, and retained match summaries.
- Added Shatter's 19 logical objective records with verified lifecycle IDs and physical confirmation.
- Added coordinate-aggregated Seal Rock objectives with verified B/A rank and observed ownership evidence; S rank and numeric team-to-GC mapping remain unresolved.
- Added passive shared-sensor adapters for other Frontline maps without inventing objective state.
- Added a single-owner manual M2 vnavmesh controller with arming, immediate stop, reachable-point snapping, generated route validation, waypoint inspection, arrival, bounded stuck recovery, failed-route comparison, and alternate approach anchors. There is no direct-movement fallback.
- Death and confirmed enemy-player combat cancel the current route and require a fresh request; objective/stale generic combat does not permanently block manual navigation.
- Added privacy-sanitized event JSONL and retained summary JSON output.
- Expanded Diagnostics with normalized battlefield, objective, sensor, retained-summary, and manual navigation panels.
- Added pure tests for normalized classification, staleness, clustering, objectives, lifecycle, combat context, summaries, recovery policy, mocked vnavmesh planning, logging rate limits, and privacy.
- Preserved External ACR, RotationSolverReborn, and Native PvPSentinel combat providers; Native MCH remains Shadow / Observe by default.
- Locked autonomous strategy, objective capture/navigation, and automatic queue/accept/requeue off for this milestone.
