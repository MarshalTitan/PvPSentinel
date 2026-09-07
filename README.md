# PvPSentinel

PvPSentinel is a standalone Dalamud plugin under development for explainable, stable Frontline movement and Machinist PvP combat decisions.

This repository is independent from the central `MarshalTitan/Sentinel` custom-plugin catalog. The catalog is intentionally **not** changed during local development.

## Milestone 1 status

Implemented:

- Dalamud API 15 / .NET 10 plugin project and valid development ZIP packaging
- `/pvpsentinel` diagnostics and `/pvpsentinel config` settings
- saved configuration with a master enable switch
- direct `IClientState` PvP detection
- language-independent Frontline detection from local `TerritoryType` / `ContentFinderCondition` data
- loaded friendly/enemy player snapshots with job, position, HP, shield, targetability, and statuses
- Frontline-specific three-way player classification using live party/alliance membership, with Unknown as the fail-safe fallback
- per-player classification diagnostics including IDs, raw status flags, membership flags, targetability, and distance
- friendly clustering, main-force selection, density, confidence, and movement trend
- main-group commitment and meaningful-size hysteresis
- configurable target scoring and safe low-HP finish opportunities
- behavior states for idle, regroup, follow, engage, finish, retreat, death, and respawn regroup
- vnavmesh adapter using the current supported IPC surface
- stable ranged follow destinations with commitment time and switch-distance hysteresis
- modular MCH PvP combat decision/execution layer
- mandatory diagnostics and state-transition logging
- live diagnostic-window visibility control with the saved preference restored after reload
- opt-in throttled development logging for classification, clustering, targeting, behavior, navigation, repathing, and combat decisions
- fail-closed safety gates outside recognized Frontline duties

Not implemented in this milestone:

- duty queueing or automatic duty acceptance
- repeated unattended matches
- Crystalline Conflict or Rival Wings
- non-MCH job controllers
- objective/score memory structures
- Wrath-controlled PvP rotation (Wrath's documented IPC does not support PvP combos/options)
- beta/release publishing or changes to `MarshalTitan/Sentinel/repo.json`

## Safety defaults

The master switch and combat execution both default to **off**. Navigation can only run when all of these are true:

1. the master switch is enabled;
2. navigation is enabled;
3. Dalamud reports PvP excluding the Wolves' Den;
4. local game data identifies the territory as a Daily Frontline Challenge map;
5. the local player is alive;
6. a reliable friendly cluster exists; and
7. vnavmesh reports that its navigation mesh is ready.

Player classification is an additional hard gate. The plugin requires an available Frontline alliance roster and no unresolved player classifications before navigation, targeting, or combat may proceed.

Any missing requirement stops owned movement. Combat has additional gates for MCH, an `Engage` or `FinishKill` behavior, a valid selected target, verified local action data, and the separate combat toggle.

## Architecture

```text
PvPSentinel
├── GameState       stable Dalamud/Lumina state capture and Frontline detection
├── Intelligence    clustering, main-force hysteresis, target scoring
├── Behavior        high-level state and safety decisions
├── Navigation      stable destination logic and vnavmesh IPC adapter
├── Combat          generic interface, native executor, MCH PvP controller
├── Integrations    isolated optional Wrath boundary
├── Models          immutable diagnostic snapshots
└── UI              configuration and development diagnostics
```

The behavior engine decides whether combat is appropriate. The MCH controller chooses an action. Navigation follows a friendly-force destination and never chooses combat actions.

## Build

Prerequisites:

- Windows with XIVLauncher/Dalamud installed
- current Dalamud API 15 development assemblies in `%AppData%\XIVLauncher\addon\Hooks\dev`
- .NET 10 SDK

From the repository root:

```powershell
dotnet restore PvPSentinel.csproj
dotnet build PvPSentinel.csproj -c Release --no-restore
```

Outputs:

- development DLL: `bin\Release\PvPSentinel.dll`
- installable development ZIP: `bin\Release\PvPSentinel\latest.zip`

## Local Dalamud loading

1. Build the project in Release mode.
2. In game, open `/xlsettings` and go to **Experimental**.
3. Add the full path to `bin\Release\PvPSentinel.dll` under **Dev Plugin Locations**.
4. Open `/xlplugins`, locate the installed development plugin, and enable it.
5. Run `/pvpsentinel`.

Do not enable navigation or combat on the first load. Confirm the diagnostics window works and configuration survives a reload first.

## First Frontline test sequence

Run the stages separately and use the master switch as the emergency stop.

### Stage A — passive diagnostics

- Master enabled: **off**
- Navigation: **off**
- Combat: **off**
- Target selection: **on**

Enter Frontline as MCH and verify map/mode/job detection, friendly and enemy counts, cluster membership, main-group confidence, target scores, finish flags, death, and respawn transitions.

For the classification re-test, expand **Nearby PCs within 40y** during both spawn and an active fight. Confirm that members of your own Frontline alliance are Friendly, visible opposing players are Enemy even when their Hostile flag is false, and no observed player is silently folded into Friendly. If **Classification reliable** is No or any nearby PC is Unknown, stop after Stage A and capture the expanded row plus the alliance roster counts.

The **Show diagnostic window** checkbox now changes the live window immediately and persists that visibility preference. **Verbose logging** emits throttled development decisions to `Dalamud.log`; switching it on clears prior suppression state so the current classification, cluster, behavior, targeting, navigation, and combat details appear promptly without being repeated every update.

### Stage B — movement only

- Master enabled: **on**
- Navigation: **on**
- Combat: **off**

Verify that movement follows the main force from a ranged offset, does not bounce between small splinter groups, and stops immediately when the master switch is disabled.

### Stage C — combat opt-in

Only after Stage A and Stage B diagnostics are credible:

- Master enabled: **on**
- Navigation: as appropriate for the test
- Combat: **on**

Observe the desired action, last accepted action, Guard handling, target changes, and finish-opportunity reasoning. Disable the master switch at once if targeting or action choice is wrong.

## Feedback needed from the first in-game test

Capture the diagnostic values before and after any incorrect decision:

- territory/map and detected mode
- behavior and commitment time
- every visible cluster's player count, confidence, and center
- nearby 20y/40y friendly and enemy counts
- chosen target, HP, distance, statuses, score, and finish flag
- destination and vnavmesh path state
- desired/last action and explanation
- relevant `Dalamud.log` lines containing `PvPSentinel`

## Third-party boundaries

PvPSentinel uses vnavmesh only through documented IPC endpoints. Wrath Combo is optional and inactive in this milestone because its documented IPC explicitly does not support PvP combo/options control. The MCH controller is an independent implementation; current action identifiers were cross-checked against local game data and the BSD-3-Clause Wrath Combo source.
