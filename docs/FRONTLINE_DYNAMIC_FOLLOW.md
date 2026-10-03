# Frontline dynamic navigation (v0.3.1.17)

Enable **PvPSentinel** and **navigation**. Choose **RotationSolverReborn** as the combat provider. A running Frontline, reliable team classification, ready current-territory vnavmesh, and observed active combat provider are required before automatic travel. The Development window lists each blocker. External ACR currently lacks a verifiable active-state signal and remains blocked for automatic travel. Queue/requeue remains disabled.

On Worqor, Seal Rock and Shatter, a fresh supported objective takes priority and uses the existing static route, approach geometry, and bounded recovery. A stale or unsupported Worqor/Seal Rock objective is retired after a three-second confirmation; Shatter keeps its depletion/staleness confirmation. Onsal and Secure objective state is unresolved, so their automated policy follows only an active allied field group. Manual destination selection takes priority everywhere.

The common field tracker excludes spawn groups, requires at least three allies, smooths the centroid, predicts brief motion, and confirms a stronger group before switching. The follower chooses a stable per-life ranged position seven to twelve yalms behind and five to twelve yalms to one side of the predicted group position. It requests a route refresh after the destination moves roughly ten yalms with a two-second debounce, or four yalms after three seconds. Pending path generation is allowed to complete under the existing bounded policy. Arrival has no six/eight-second group hold; if the group moved, the next leg starts immediately. A nearby formation does not issue a route. Company Chocobo and the existing mount safety gates remain in effect.

The shared follower never controls combat. Reborn owns ordinary combat actions. PvPSentinel observes provider readiness and owns strategic travel, including ordinary combat travel under the existing cast/movement safety policy. No Reborn mode-setting PvP IPC or chat command is used. STOP disables navigation until it is explicitly enabled again. Emergency STOP also disables the master and combat-provider selection. Death, Results, team/mesh/provider loss, or bounded route failure cancel owned movement.

## Recommended RotationSolverReborn settings

| RSR setting | Value |
| --- | --- |
| Auto turn on when PvP match starts | ON |
| Auto turn off when dead in PvP | OFF |
| Disable automatically during area transitions | OFF |
| Auto turn off when PvP match ends | ON |
| Auto turn off after combat | OFF |
| Set RSR to PvP-specific state when enabled in PvP zone | ON |

If Reborn is loaded but inactive, automatic travel remains blocked. Enable autorotation through its own supported controls and verify the lifecycle settings above. PvPSentinel never forces its operating mode.

## Supervised match evidence

Upload `events.jsonl`, `summary.json`, `dalamud.log`, and `frontline-entry-stages.log` after the match. A short video is useful when movement visibly lags the group, replans too often, collides with geometry, or stalls after respawn. The Development window shows the map policy, dynamic follower state, formation slot, destination, route state and provider blockers.

This implementation uses PvPSentinel's own map sensors and shared field tracker. APSG match decisions were behavioral reference only; no substantial AGPL source was copied.
