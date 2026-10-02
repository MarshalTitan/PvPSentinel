# Changelog

## 0.3.1.11 — Remove the Targeting-Me display

- Remove the Target Counter window, its window-manager registration and per-frame draw, dedicated 128 px digit font handle/atlas, font disposal, positioning/locking and job-text rendering. Remove the Configuration section and all seven display-only fields and their obsolete version-5 migration. Existing version-15 and earlier configurations ignore the old JSON keys and are re-saved as version 16 without them.
- Preserve `PvPThreatTracker` evaluation every update, Reborn targeting-us engagement and navigation arbitration, battlefield combat context, Native Guard/defense, diagnostic threat rows, and threat-change logging. No SentinelHUD dependency is introduced. Navigation, map adapters, pilots, mounting, and combat providers retain their behavior.
- Add compatibility tests for a prior config containing counter fields and for continued threat/engagement behavior. The standalone user-facing counter now belongs to SentinelHUD.

## 0.3.1.10 — Shatter ice approaches and expired target recovery

- Correlated the first supervised Shatter match: map bootstrap populated 36 markers, but A4 routes repeatedly stalled roughly 4–5 yalms from the crystal center and entered bounded failure pause. The post-reconnect log has no stack trace for the earlier game crash, so its cause remains unconfirmed.
- Route Shatter ice selections and manual ice buttons to several offset approaches outside the large or small crystal. Exclude the physical center and generic inner fallback candidates, including after combat repathing; reject mesh snaps that violate the crystal clearance. Other map destinations keep their prior candidate policy.
- Retire a committed ice route after a confirmed inactive/depleted marker, or a sustained stale marker, then choose another supported destination after a short hold. A brief marker flicker does not cancel the route. Route failures still pause the supervised pilot.
- State the active Reborn autorotation requirement in the waiting status and settings. The provided screenshot showed Reborn installed but inactive after respawn; Sentinel does not activate its PvP controls. Add regression tests for both crystal sizes, center exclusion, marker transition, and selection after retirement.

## 0.3.1.9 — Shatter supervised group-navigation pilot

- Add a separate default-off Shatter match pilot. Choose only a fresh, verified active ice marker or a near-term activating marker (25 seconds or less) with nearby allied support and no large local enemy advantage. Reject depleted, stale, or unconfirmed ice. Prefer a supported large tomelith when choices are otherwise similar. If no supported ice exists, make a bounded leg to a visible allied field group.
- Keep each route committed until arrival or death, including through active Reborn combat. Manual destinations take priority, STOP disables the mode, and bounded route failure pauses it. Require ready vnavmesh, reliable team classification, active Reborn autorotation, and an active Shatter match. Add policy tests for state, stale/unsupported/depleted markers, combat commitment, death/respawn, manual priority, failure, and results.
- Extend the bounded one-time AgentMap marker bootstrap to Shatter when no named ice marker appears with the map closed. Record the action in match events and close only the map opened by Sentinel. This requires live validation; generic all-map strategy and queue/requeue remain disabled.

## 0.3.1.8 — Seal Rock supervised group-navigation pilot

- Correlated the complete Seal Rock match: 15 manual destination requests, four arrivals, three detected stalls with completed recovery stages, no path or sensor failures, eight explicit STOPs and three death cancellations. There were no new requests after the 01:11 UTC arrival despite two later deaths; that match did not exercise automatic Seal Rock selection.
- Add an opt-in Seal Rock match pilot using fresh active neutral tomelith markers near a visible allied group. Captured nodes with unresolved team ownership are never automatically selected. Without a supported neutral node, make a bounded leg to a visible allied field group, including after respawn. Each leg stays committed through Reborn combat until arrival or death; manual clicks take priority, STOP disables the pilot, and a bounded failure pauses it for the match.
- Require active RotationSolverReborn autorotation, ready vnavmesh, reliable team classification, and active Seal Rock match before choosing a route. The new option defaults off, including on existing installations. Add pilot decision tests. Generic all-map strategy and queue/requeue stay disabled pending field validation.

## 0.3.1.7 — Worqor reconnect and respawn regroup

- Correlated two abrupt session endings and the complete sixth Worqor match. The full run had 24 navigation requests, 16 arrivals, six deaths, zero path failures, zero stuck events, and zero sensor errors. The two earlier files end without a managed exception or crash dump, so the disconnect cause is not established. The direct event-framework marker probe returned zero named markers in all reconnect sessions; remove that unproductive native call and initialize the map agent once in a safe window after five seconds in Worqor, including pre-match.
- Fix the pilot's death ordering. When Reborn autorotation became inactive during a death, the former early gate skipped clearing its committed destination; respawn could leave the pilot committed to a route the navigation controller had already cancelled. Death now clears the commitment before the Reborn gate. Waiting status and bounded log events specify whether match, team, or Reborn readiness is blocking selection.
- After death, prefer a fresh Triumph supported by allies. If none is viable and Reborn is active, make one bounded, committed trip to a visible field cluster of at least three allies; ignore groups still at spawn and hold on arrival before considering another Triumph. Manual routing, STOP, and path-failure limits retain priority.
- Parse the trailing control payload on live claimed Triumph tooltips. All claimed markers had previously stayed visually unclaimed because the exact-text parser rejected them; ownership still remains unresolved. Exclude the observed `60599/0/210` team-two spawn marker from clickable WOR destinations even beside an EventObj.

## 0.3.1.6 — Worqor marker readiness and opt-in group destination

- The fifth Worqor match promoted all 12 named Triumph locations after the map became available. Five manual requests yielded four arrivals and one explicit STOP, with zero path failures, stuck events, or sensor errors. The 0.3.1.5 snowman recovery did not trigger in this match.
- Probe the event framework for Triumph markers before falling back to opening and closing the map once, out of combat, after six active-match seconds without named locations. Log the marker source and bootstrap action for live validation.
- Parse observed English Triumph marker text into activating, unclaimed, or claimed states, rank, and countdown. Preserve the claimed marker faction 4/5/6 as raw evidence; our-team ownership remains unresolved.
- Add an opt-in Worqor group pilot. It chooses a fresh unclaimed or soon-activating Triumph supported by an allied cluster, prefers the starting side for the first leg, and keeps its destination until arrival or death. A manual selection takes priority; STOP disables the mode, and a bounded route failure pauses it. RotationSolverReborn remains responsible for combat actions while existing manual-route travel handles movement through combat.
- The map-independent autonomous strategy and queue/requeue remain disabled. Marker bootstrap and pilot selection require live validation.

## 0.3.1.5 — Worqor central snowman recovery

- Correlated the fourth Worqor match and its 41-second video. All 12 promoted destinations were named Triumph markers. Twenty-two manual requests produced 13 arrivals, one bounded failure, three stuck events, no path-generation failures, and no sensor errors. Eight combat-continuation intervals showed four arrivals, two death cancellations, and two explicit STOPs.
- The video confirms the central snowman physically blocks a northwest vnavmesh route from the lower center at about `(-3,-17,7)`. The obstacle was absent from the nearby object list; a generic short side step and a second generated departure both failed. A different generated route from `(6,-17,5)` then reached Triumph 2 at `(52,-17,34)` by traveling north and east.
- When a generated northwest corridor fails within that filmed lower-center footprint, prefer the observed east exit as the first recovery destination. Vnavmesh still generates and drives the full stage before repathing the original manual destination. The strategy is restricted by map, floor, position, and failed-corridor direction; other routes keep generic recovery. STOP, death, results, and three-attempt failure remain terminal. This staged recovery requires another live validation before being considered reliable.
- Autonomous group following remains disabled pending group-target selection and observed obstacle-route validation.

## 0.3.1.4 — Worqor destination cleanup after combat travel validation

- Correlated the third Worqor match: 17 manual requests, 13 arrivals, nine combat-continuation intervals with seven subsequent arrivals and two death cancellations, zero navigation/path failures, zero stuck events, and no sensor errors. The new combat travel policy made measurable progress through active Reborn combat; results cancelled the remaining route.
- Kept the live-observed player and Levemete marker identities, the `60597 + 0/181` base pair, and `60573 + 4278190080` base evidence in raw research rather than clickable `WOR-xx` destinations. The paired `60574 + 0/4278190080` base marker remains raw even when physical objects overlap it; named Triumph markers still qualify.
- Automatic group following remains off while group-target selection and moving-player separation lack live validation. A central snowman obstruction did not recur in this match; no fixed detour is inferred from its absence.

## 0.3.1.3 — Worqor combat travel and team-marker cleanup

- Correlated the second Worqor match. Fourteen manual requests yielded two arrivals, eight combat handoffs, five death cancellations, and one bounded route failure at the central obstacle. No classification or sensor errors occurred.
- Keep an active manual vnavmesh route moving on foot during combat when RotationSolverReborn is confirmed active. Reborn still owns combat actions; STOP, death, results, and bounded path failures still stop movement. A visible option can restore the prior full-engagement yield behavior.
- Extend raw-only Worqor discovery filtering across observed team variants: moving `60359`–`60361 + 4278190080` and landing/base `60598/60599 + 26/181/4278190080`, with the adjacent third-team base icon and previously observed 62 value treated conservatively. Named Triumph evidence remains eligible for stable manual destinations.
- Add nearby non-player object IDs and coordinates to stuck diagnostics. The central snowman/collision route still needs a visual confirmation and dedicated obstacle route validation before automatic group following is enabled.

## 0.3.1.2 — Worqor discovery filtering and result-screen navigation

- Correlated the first complete Worqor Chirteh match. Seventeen manual requests produced ten arrivals, no path failures, seven combat yields, two automatic resumes, clean `1 SELF / 23 ALLY / 37 ENEMY / 0 UNKNOWN` peak classification, nine death/respawn pairs, and terminal results with no sensor errors. The two recorded stuck recoveries occurred after results appeared, when the client stopped moving; no in-match stuck event was recorded.
- Kept live-confirmed Worqor `60359/60360 + 4278190080` moving markers and `60599 + 26/4278190080` landing/base markers in raw research even when they overlap physical objects. They no longer create misleading `WOR-xx` buttons or fill the 32-location limit.
- Stop owned navigation and cancel a pending manual route when results become visible. Further requests cannot start movement until a new match begins.
- Preserve the stable, named Triumph marker locations and their raw evidence without inferring ownership or autonomous strategy.

## 0.3.1.1 — Onsal evidence cleanup and prompt combat resume

- Correlated the first complete Onsal discovery session with all four recordings. Twelve manual requests produced four `ARRIVED` routes, zero navigation/path failures, two bounded stuck recoveries, four combat yields, two automatic route resumes, clean `1 SELF / 23 ALLY / 46 ENEMY / 0 UNKNOWN` classification, three detected deaths/respawns, terminal results, and no sensor error or plugin exception.
- Confirmed the reported post-fight stall was combat arbitration rather than vnavmesh failure. The former policy required five quiet seconds and reset that timer for every living targetable enemy inside 30 yalms, so unrelated enemy traffic could starve a preserved route indefinitely.
- Changed RotationSolverReborn handoff to keep yielding for actual local combat/casting/action evidence and for shared-threat enemies observably hard-targeting the player, while passive enemy proximity remains diagnostic and cannot reset the resume timer.
- Reduced the configurable Reborn post-combat default from five seconds to two seconds and migrates only the exact legacy default; deliberately customized values remain intact. A renewed real engagement still reacquires combat ownership and stops navigation immediately.
- Added explicit external-provider diagnostics for both nearby enemies and enemies currently targeting the player, and made engagement-state/threat changes visible in event-driven Dalamud logging.
- Demoted live-confirmed Onsal moving marker families `60359/60360 + 4278190080` and landing/base family `60599 + 62/4278190080` to raw research evidence even when they overlap an EventObj. They can no longer consume the 32-location ceiling or create false `ONS-xx` buttons.
- Normalized the observed Onsal `ObjectiveId` 446/448 presentation flicker for stability/transition comparison while retaining the original IDs in raw evidence, eliminating high-frequency false transitions without assigning speculative objective semantics.
- Added regression coverage for passive-enemy resume, observed hard-target blocking, Onsal moving-marker physical-overlap rejection, and 446/448 stability. Autonomous strategy, automatic objective navigation, queue/requeue, and provider-independent PvP Limit Break execution remain disabled; Native MCH remains `Shadow / Observe` by default.

## 0.3.1.0 — All-map discovery foundation and Onsal test build

- Correlated the final v0.3.0.9 Secure session, including its fresh start after the game crash. The complete trace retained clean `1 SELF / 23 ALLY / 37 ENEMY / 0 UNKNOWN` classification, Company Chocobo travel, explicit STOP, death cancellation, combat yield/resume, and five successful `ARRIVED` routes without a plugin exception.
- Normalized the newly observed Secure `ObjectiveId` 480/0 presentation flicker in addition to the earlier 486/0 case, and bounded repeated per-signal/global objective transitions so a noisy discovery source cannot dominate `summary.json`.
- Restricted physical objective corroboration to event objects or large targetable battle NPCs. Player pets such as Demi-Bahamut and Bunshin remain available as raw research evidence but can no longer falsely confirm a Secure or discovery location.
- Corrected mounted generated-stage completion for the live-observed actor/ground-mesh height offset. The allowance is bounded, applies only after a generated route has been followed, and still rejects a genuinely different floor.
- Added discovery-first Onsal Hakair (territory 888 / duty 701) and Worqor Chirteh (territory 1313 / duty 1080) adapters. Both keep raw marker families separate from a maximum of 32 strongly stable, clickable `ONS-xx`/`WOR-xx` locations and leave objective semantics, rank, ownership, and strategy explicitly `UNRESOLVED`.
- Made Testing Controls and Objectives / Map Research map-aware. Only the current map's destinations and research are shown; Onsal and Worqor session labels include authoritative X/Z coordinates, while large raw collections remain collapsed research evidence.
- Registered explicit adapters for all five current Frontline maps while preserving the field-validated Secure, Shatter, and Seal Rock implementations and the shared navigation, mounting, combat-handoff, classification, threat, lifecycle, and retained-summary systems.
- Expanded `_WideText` research retention for cross-map discovery and added regression coverage for all-map adapter registration, discovery aggregation/promotion, raw-only isolation, Secure 480 flicker, physical-object filtering, transition bounds, and mounted stage arrival.
- Fixed retained-match team recording so live-valid zero-based Battalion team `0` is preserved instead of being treated as absent.
- Autonomous strategy, automatic objective navigation, queue/requeue, provider-independent PvP Limit Break execution, and speculative Onsal/Worqor tactics remain disabled. Native MCH remains `Shadow / Observe` by default.

## 0.3.0.9 — Secure landing cleanup and bounded summaries

- Correlated the complete v0.3.0.8 Secure match with both recordings. The stable `SEC-CENTER` identity and coordinate-bearing session labels worked, all three previously identified pre-match families remained raw-only, and classification stayed clean at `1 SELF / 23 ALLY / 41 ENEMY / 0 UNKNOWN`.
- Confirmed `60597/0/162` at `(-257.4, -7.1, 149.3)` is the Storm Landing/base marker rather than a tactical objective. Together with the previously observed `60598` landing family, these markers now remain raw research evidence and cannot create manual navigation buttons.
- Preserved the safe result of the failed landing test: Sentinel never direct-steered through the structure, bounded all recovery attempts, reported failure, and released movement ownership.
- Confirmed `SEC-CENTER` reached the central platform from spawn. vnavmesh initially returned the same sparse false shortcut on both attempts; staged recovery moved to a generated departure route and then followed the full 29-waypoint multi-level ramp route. No unverified hard-coded ramp coordinates were added.
- Changed retained research notes from an every-frame history of changing counters to the final bounded sensor snapshot. Objective transitions remain separately retained, while `summary.json` no longer grows by more than a megabyte from diagnostic-count strings alone.
- Preferred Company Chocobo mounting, combat yield/death cancellation, lifecycle/results detection, threat tracking, and the borderless Targeting-Me counter remain intact. Native MCH remains Shadow / Observe by default; autonomous strategy, queue/requeue, and provider-independent Limit Break execution remain disabled.

## 0.3.0.8 — Stable Secure center identity

- Corrected the discovery-label assumption exposed by the latest complete Secure match: `SEC-xx` values were assigned by per-session promotion order and therefore did not identify the same physical location across matches.
- Added the field-confirmed geometric map-center coordinate as the stable `SEC-CENTER` manual destination. This is a geometric identity only; it does not infer objective type, lifecycle, ownership, or strategy.
- Made every remaining session-scoped Secure destination button include rounded X/Z coordinates and added an explicit UI/research warning that coordinates, not `SEC-xx`, are authoritative across matches.
- Demoted the three newly proven pre-match/non-objective marker families (`63922/0/721462`, `71121/0/721223`, and the `71041/0/393222` Levemete marker) to raw research evidence so they cannot consume destination numbers or create misleading navigation buttons.
- The supplied trace confirms all three v0.3.0.7 staged departure recoveries reached their staging point and repathed successfully, with no path-generation failures or rejected final routes. It also confirms the borderless Targeting-Me counter tracked observed hard-target counts during combat.
- Preserved manual-only Secure navigation, single movement ownership, Company Chocobo preference, combat pause/resume, terminal death/STOP behavior, Native MCH Shadow default, and disabled autonomous strategy, queue/requeue, and provider-independent Limit Break execution.

## 0.3.0.7 — Staged stair recovery and complete research coordinates

- Correlated the v0.3.0.6 Secure trace with both recordings. Preferred-mount travel is live-confirmed: every automatic request summoned Company Chocobo, including the preserved SEC-08 destination after RotationSolverReborn combat ownership cleared. Team classification remained clean at `1 SELF / 23 ALLY / 41 ENEMY / 0 UNKNOWN`.
- Confirmed the strengthened route comparator safely rejected every path that rejoined the failed Secure center stair corridor. The remaining defect was recovery topology: changing only destination-side approach anchors could not alter vnavmesh's blocked opening corridor.
- Added bounded local departure-stage recovery. After a stall, Sentinel derives six side/back staging candidates from the failed corridor, snaps each through vnavmesh, rejects stages that remain near the failed origin or repeat its geometry, follows a validated generated path to the stage, then repaths the original destination from that materially different origin.
- Preserved single movement ownership, explicit route inspection, mounted travel through the recovery stage, bounded failure, and the prohibition on direct-steering or terrain-running fallbacks.
- Added `navigation_recovery_started`, `navigation_recovery_stage_arrived`, and `navigation_recovery_stage_advanced` evidence so the next live trace can distinguish local escape movement from the final destination route.
- Fixed retained `summary.json` serialization so objective and research-object positions contain actual `x`, `y`, and `z` values instead of empty objects.
- Added privacy-safe, rate-limited `threat_changed` events with targeting-me count, nearby balance, threat level, and targeter jobs/distances. This will correlate the shared tracker with the borderless counter in future recordings without logging player names.
- Capped highly volatile team-probe and cluster events to a three-second cadence while preserving state changes, reducing oversized match logs.
- The supplied clips did not overlap the match's one confirmed hard-target interval, so the existing counter visibility rules remain unchanged: it is reliable in Frontline and hidden at zero when that setting is enabled.
- Native MCH remains Shadow / Observe by default. Autonomous strategy, objective capture, queue/requeue, and provider-independent Limit Break execution remain disabled.

## 0.3.0.6 — Preferred mount and field-refined route recovery

- Correlated the first v0.3.0.5 Secure trace with three recordings. Seven manual requests produced three `ARRIVED` results; the trace positively confirmed two preserved destinations resumed with a fresh path after RotationSolverReborn combat ownership cleared. Death and explicit STOP remained terminal.
- Added a persistent **Preferred Mount** selector populated from the character's currently unlocked mounts through Dalamud's supported unlock service and the current `Mount` game-data sheet. The default is Company Chocobo.
- Replaced on-foot Mount Roulette summoning with the configured mount's `ActionType.Mount`/Mount-row request. An unresolved, locked, or currently unavailable mount fails visibly with its reason and never falls back to a random mount; the mounted-state general action remains only for normal dismounting.
- Preserved combat, casting, nearby-enemy, transition-grace, retry, arrival, and combat-dismount safety gates.
- Demoted live-confirmed moving Secure marker family `60359/0/1115742468` to raw research evidence. It can no longer create false `SEC-xx` buttons even when it overlaps physical-object evidence; verified stationary central-objective evidence remains intact.
- Strengthened failed-corridor comparison using 3D path geometry and continuous aligned-overlap detection. A replacement such as the observed SEC-12 route, which adds a small opening detour and then rejoins the same failed stair corridor, is rejected before it consumes another movement attempt.
- Added regression coverage for the newly confirmed transient marker family, the exact SEC-12 rejoin shape, and vertically separated routes.
- Kept Native MCH in Shadow / Observe by default and autonomous strategy, objective capture, queue/requeue, and provider-independent Limit Break execution disabled.

## 0.3.0.5 — Live-enemy clearance and Secure field-data cleanup

- Correlated the second Secure v0.3.0.4 field trace with six recordings: automatic mounting and the targeting-me counter operated correctly, two generated routes reached `ARRIVED`, and the central-platform route rejected a bad direct path before recovering through a materially different stair route.
- Fixed the Reborn engagement-clearance count to include only living, targetable enemy players with HP remaining. Enemy corpses can no longer keep a preserved strategic destination paused or prevent a post-fight mount indefinitely.
- Shared Sentinel's mount-request latch with Reborn arbitration so the Mount Roulette cast frame before Dalamud exposes `IsMounting` cannot be mistaken for a combat cast and immediately trigger a dismount/yield cycle.
- Made a destination selected during combat pause explicitly replace the preserved destination. The new destination remains paused safely, then repaths from the post-fight position after clearance; diagnostics emit `navigation_destination_replaced`.
- Demoted the live-confirmed pre-match Trader marker (`60935/0/721735`) to bounded raw research evidence so it no longer becomes an impossible `SEC-xx` navigation button.
- Normalized the observed Secure `ObjectiveId` 486/0 presentation flicker and changing countdown/HP numbers for transition comparison. Current raw evidence is still retained, while thousands of non-semantic per-frame `secure_observable_transition` records are suppressed.
- Preserved the six central temporary-objective locations and their spawn/HP lifecycle evidence; these were correlated by stable coordinates and were not discarded as player-marker noise.
- Kept death and explicit STOP terminal, Native MCH in Shadow / Observe by default, and autonomous strategy, objective capture, queue/requeue, and provider-independent Limit Break execution disabled.

## 0.3.0.4 — Resumable combat yield and stable mounted travel

- Changed manual M2 combat handoff from terminal cancellation to a resumable pause: Sentinel stops its owned vnavmesh path, preserves the selected destination, lets RotationSolverReborn own the fight, then requests a fresh generated route from the post-fight position after combat, nearby enemies, and the configured quiet period clear.
- Kept death, explicit STOP, disarming, path-failure exhaustion, and safety-gate failures as terminal route cancellations.
- Added `navigation_resumed_after_combat` evidence and enriched `navigation_yielded_external_combat` with destination preservation, normalized battlefield block, and provider-yield reasons.
- Fixed premature dismounts caused by crossing below the mount-start threshold. Once mounted, travel now remains mounted until the configured arrival distance or confirmed combat requires a dismount.
- Stopped merely visible nearby enemies from forcing a mid-route dismount; nearby enemies still prevent a new mount attempt, while actual combat immediately receives movement ownership and requests dismount.
- Based manual dismount decisions on remaining generated-route length where available, avoiding early dismounts on ramps and switchbacks whose endpoint is horizontally close but still distant along the traversable route.
- Latched an accepted Mount Roulette request for up to four seconds so the vnavmesh path does not restart during the cast before Dalamud exposes the mount-transition condition.
- Prevented an unflagged mount cast with no nearby enemies from being misclassified as a Reborn combat engagement.
- Reduced mount diagnostics to state transitions rather than emitting another event for every nearby-enemy count change.
- Added pure regression coverage for combat pause/resume arbitration, mount-threshold stability, arrival/combat dismounting, and mount-cast engagement filtering.
- Autonomous strategy, queue/requeue, and provider-independent Limit Break execution remain disabled; Native MCH remains Shadow / Observe by default.

## 0.3.0.3 — Frontline team resolution and manual M2 mounting

- Corrected Dalamud Frontline relationship classification from the second Secure field trace: `Character.Battalion` is a zero-based team index in this source, with the complete local roster observed on `0` and the opposing teams on `1` and `2`.
- Kept classification bounded to recognized Frontline duties and live-confirmed Battalion values `0-2`; SELF remains entity-ID-first, and no Grand Company name mapping is inferred.
- Restored authoritative SELF/ALLY/ENEMY counts, clusters, combat context, target evaluation, and shared threat tracking when the local Battalion is `0`.
- Fixed the Targeting-Me counter by feeding it the same resolved enemy set used by combat defense instead of the ineffective Secure `StatusFlags.Hostile` fallback (the live match observed zero hostile flags despite up to 49 targetable players).
- Integrated the existing safe mount controller into manual M2: long routes request Mount Roulette, wait for real mount/dismount transitions, dismount near the destination or observed enemies, and then resume the preserved generated route.
- Kept manual M2 traversable on foot when mounting is merely blocked by stale/objective combat, preserving the normalized combat-context safety contract.
- Added `navigation_mount_state_changed` evidence with destination, state, mounted/mounting flags, remaining distance, nearby enemy count, and decision reason.
- Added regression coverage for zero-based Battalion classification and manual mount arbitration.
- Preserved the validated protected-route behavior: the field trace completed 14/19 manually requested routes with zero path-generation failures; its single stair/platform stall repathed through materially different geometry and arrived 3.7 seconds later.
- Native MCH remains Shadow / Observe by default; autonomous strategy, objective capture, queue/requeue, and provider-independent LB automation remain disabled.

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
