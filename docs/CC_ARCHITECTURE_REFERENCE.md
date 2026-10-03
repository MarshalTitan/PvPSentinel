# Future Crystalline Conflict architecture reference

Read-only concept audit of `XeldarAlz/FFXIV-AutoPVPSeriesGrind` at `ada94edae9c6547321832a7cb47b4a7855dabc03`: https://github.com/XeldarAlz/FFXIV-AutoPVPSeriesGrind . PvPSentinel implements no CC movement, combat, map anchors, or queueing in this release. The reference is AGPL-3.0-or-later; no code or coordinate table was copied.

## Useful concepts to validate independently

- **Map identity and spawn side:** Recognize each CC territory variant, observe which spawn belongs to us, then validate safe anchors with live geometry and vnavmesh. Do not infer allegiance from an unverified hard-coded coordinate.
- **Escort state:** Track the tactical crystal and its motion separately from player clusters. A stable escort slot near the crystal could avoid crowding and repeated replans, but it needs local hazard/mesh checks and progress telemetry.
- **Tactical phases:** Engage, Stage, Reposition, Regroup, and Retreat provide a useful vocabulary. Add state dwell and emergency escalation so a transient target or player count does not flip movement every frame.
- **Anti-trickle and force evaluation:** Compare allies and enemies both at the crystal and near the player, with deaths and respawn timing, before committing to a contested push. Avoid walking alone into a numerical disadvantage.
- **Role and line of sight:** Backline and frontline roles need different safe positions; a target and waypoint should be checked for line of sight and mesh reachability independently.
- **Escape and hazards:** Evaluate multiple retreat directions toward allies, away from opponents and their spawn, while avoiding telegraphs. Path validation must still preserve corners/elevation and a bounded recovery budget.
- **Casual Match lifecycle:** Queue identity, duty-ready ownership, intro gate, result detection, duty exit, penalty, breaks, match limit, and STOP need their own CC tests. The current Frontline queue controller is disabled and should not be repurposed until these transitions are verified.

Suggested implementation order: read-only CC map/team/crystal sensing and annotated match logs; manual navigation to validated anchors; supervised escort/regroup pilot; provider coordination and defensive decision gates; finally an separately gated Casual Match queue lifecycle. Do not import the reference project's built-in rotation or LB preset IPC by default.

Reviewed upstream `Core/Game/CrystallineConflictMaps.cs`, `Core/Combat/{CrystalEscort,PvpBrain}.cs`, `Core/Tasks/{AutoPvpSeries.Match,AutoPvpSeries.Queue}.cs`, and related spawn, line-of-sight, telegraph, and duty-operation components. These notes describe ideas, not an implementation specification or copied source.
