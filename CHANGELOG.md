# Changelog

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
