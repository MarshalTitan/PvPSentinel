# Frontline automation reference audit

Reviewed `XeldarAlz/FFXIV-AutoPVPSeriesGrind` at commit `ada94edae9c6547321832a7cb47b4a7855dabc03` alongside PvPSentinel `v0.3.1.12`. Reference: https://github.com/XeldarAlz/FFXIV-AutoPVPSeriesGrind . The reference is AGPL-3.0-or-later and carries NOTICE terms for works containing its code. This change implements general ideas independently; no source, map anchor table, preset, asset, or implementation class was copied.

## Frontline choices

| Area | Decision in PvPSentinel |
| --- | --- |
| Field-group fallback | A shared tracker supplies a visible group of at least three, excludes the observed pre-match/respawn base, waits for a sustained stronger challenger, smooths the position, and uses a short velocity lead. Its output is sampled only when a pilot chooses a new bounded destination. Existing objective evidence and commitment remain map-specific. |
| Base evidence | Capture the player's pre-match or respawn position, not the position where the toggle was enabled mid-match. If no base is known, use the local-distance and group-size gates. Never fall back to an observed spawn group just because the field is empty. |
| vnavmesh | Require both ready IPC and idle build progress. Validate the full generated route; reconcile a delayed result only against a nearby unprotected prefix. Otherwise request a fresh path from the current player position. Invalidate outstanding path generations on STOP, death, results, replacement, and combat handoff. |
| Stuck/recovery | Existing stuck timers already run only while Sentinel owns a running path. Stage execution protects turns and elevation changes. Failed-corridor comparison, materially different replacements, Worqor departure evidence, and bounded attempts are retained. No random destination jitter or direct steering was adopted. |
| Mount/respawn | Existing preferred Company Chocobo resolution, enemy safety gates, dismount before close engagement, terminal route cancellation on death, and regroup reassessment remain in force. No mount or death logic was copied. |
| Combat/LB | Reborn stays the ordinary-combat provider. No built-in all-job rotation or Auto PVP LB dependency was added. The reference plugin negotiates a separate LB preset IPC version and pushes per-job presets when available; any future optional integration would need explicit provider ownership and validation. |

## Queue readiness, read-only

The current `QueueLifecycleController` can inspect Daily Frontline, reject an unknown or unchecked campaign, track an owned queue submission, refuse an unrelated duty-ready popup, and count completed duties. Its adapter has a guarded Commence control. The live `Plugin` deliberately returns a disabled queue decision instead of calling the controller's update loop; generic strategy and requeue remain off.

Before activating, implement and field-validate: persistent ownership tied to a specific queue attempt and duty identity (including reconnect/manual queue changes); a safe result-to-exit transition with timeout and duplicate-completion protection; penalty detection and a configurable post-match delay; immediate STOP versus finish-current-match semantics; match-limit persistence/reset rules; and tests across failed queue submission, externally initiated queue, declined ready popup, disconnect, results, and duty exit. Never cancel another queue to submit Frontline. The reference project has a complete results/leave/requeue loop and penalty checks, but its generic ready-popup and queue-cancellation behavior does not satisfy PvPSentinel's ownership rule.

Reviewed upstream concepts in `Core/Combat/{FrontlineBrain,CrowdPicker,CrowdTracker}.cs`, `Core/Tasks/{MovementExecutor,StuckDetector,AutoPvpSeries.Queue,AutoPvpSeries.Match}.cs`, and `Core/Ipc/{NavIpc,PvpAutoLbIpc}.cs`. This document intentionally contains no upstream source or constants tables.
