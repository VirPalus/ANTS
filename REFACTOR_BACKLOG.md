# REFACTOR_BACKLOG

Notes captured during the staged refactor (see `REFACTOR_PLAN.md`). Append-only.
Each entry is dated and self-contained.

## Lessons Learned

### NUL-byte corruption on large-file Edit-shrinks (FUSE-specific)

The Claude.ai Write/Edit tools, when shrinking large C# files (>60 KB,
e.g. `Engine/Engine.cs`) through the FUSE mount, can leave trailing NUL
bytes equal to the number of shrunk bytes. The logical content is correct;
the physical file is NUL-padded back to the original size.

Mitigation pipeline (applied before every commit):

1. Edit the file.
2. Verify: `wc -c`, `md5sum`, `tail -c 50 | xxd`, `tr -cd '\0' | wc -c`.
3. If NUL > 0: strip trailing NULs via Python
   (`while data.endswith(b'\x00'): data = data[:-1]`).
4. Re-verify: NUL count must be 0.

Manual removal (step 3) is still required; the verification step (2) is
the backstop that catches the issue before it reaches git.

Observed incidents:

- 2026-04-17/18 FASE 0.A — `Tests/Characterization/CharacterizationHarness.cs`
  shrink from 8325 B to 2150 B left 6175 trailing NULs.
- 2026-04-18 FASE 1.1 — `Engine/Engine.cs` shrink from 65491 B to 65445 B
  left 46 trailing NULs.

Python-direct writes (bypass Edit tool, use `open(f, 'wb').write(...)`)
do not trigger this — confirmed cleanly in FASE 1.2 across 11 files.

### FUSE truncation variant (2026-04-18)

Tijdens FASE 8.1 docs-update ontdekt: Edit-tool op
`CODEBASE_AUDIT_REPORT.md` en `REFACTOR_PLAN.md` truncateerde files tot
originele byte-grootte, nieuwe content toegevoegd maar staartregels
kwijtgeraakt (302→297 regels, 283→279 regels). Nieuwe variant van
FUSE-bug: geen NUL-padding, maar trailing truncation.

Detection signal: post-Edit `wc -c` matches BEFORE-edit size exactly
(niet de verwachte new size). Verifieer altijd `wc -c` matches
expected delta.

Mitigation: zelfde Python-direct write recipe
(`open(f, 'wb').write(data)`).

Observed incidents:

- 2026-04-18 FASE 8.1 docs: `CODEBASE_AUDIT_REPORT.md` +
  `REFACTOR_PLAN.md` truncated during Edit-tool call.

## Deferred Decisions

### Patrol enum value removal (2026-04-18)

`GoalType.Patrol` appears dead at first glance but serves as UI state-tag
for Attacker/Defender roles. The original audit (CODEBASE_AUDIT_REPORT.md
§3) claimed "only the enum declaration" — this was stale; repo-wide grep
finds 4 live consumers.

Removing it requires either:

(a) UI change accepting `"Explore"` label for non-foragers in the
    info-panel (Engine.cs:1903 renders `ant.Goal.Type.ToString()`), or
(b) refactor to separate logical-state (Goal) from UI-display-state
    (role-label), so attacker/defender ants can keep a distinct
    on-screen string without the enum value.

Deferred until an explicit UI-modernization task. FASE 8.1 is marked
SKIPPED in REFACTOR_PLAN.md.

References:

- `Simulation/Roles/AttackerRole.cs:48,50` — check-and-set.
- `Simulation/Roles/DefenderRole.cs:53,55` — check-and-set.
- `Engine/Engine.cs:1903` — `ant.Goal.Type.ToString()` info-panel display.

### ClearEnemyTrailForTarget removal (2026-04-18)

`PheromoneGrid.ClearEnemyTrailForTarget(int)` appears unused at first
glance but is called from `World.cs:372` inside
`ForgetEnemyTrailAboutDeadColony(Colony deadColony)`, which itself runs
from `DespawnDeadColonies` on every tick with dead colonies.

The method removes the dead colony's keys from 4 per-enemy-colony
dictionaries:

- `_enemyTrails`
- `_enemyDistances`
- `_enemyActiveCells`
- `_enemyActiveSet`

Deleting the method would cause:

(a) compile break at `World.cs:372` (CS1061), and if the caller is also
    removed,
(b) memory leak in the 4 dictionaries (grow-only over colony deaths),
    and
(c) harness-digest drift, because lingering enemy-trail data affects
    `pheromoneCellCount-per-channel` in the per-second digest and
    influences `SensorSystem` indirectly.

Not deferred for later — this is live code. FASE 8.2 is marked SKIPPED
in REFACTOR_PLAN.md and CODEBASE_AUDIT_REPORT.md §3 has been corrected.

References:

- `Simulation/Pheromones/PheromoneGrid.cs:233-239` — definition.
- `Simulation/World.cs:372` — only call site, inside
  `ForgetEnemyTrailAboutDeadColony`.
- `Simulation/World.cs:335-360` — `DespawnDeadColonies` call chain.

## Tech-debt identified during refactor (for FASE 4 Engine split)

### Lunge rendering leaks CombatTuning into Engine.cs

During FASE 7.3 CombatTuning extraction, it was noted that `Engine.cs`
directly reads `CombatTuning.LungeDuration` and `CombatTuning.LungeDistance`
for lunge animation rendering (lines 1136, 1138).

This violates "Engine = only engine concerns" principle — combat tuning
values should not be read by rendering code directly.

Resolution: FASE 4 Engine split will move lunge-animation rendering
from `Engine.cs` to a dedicated renderer (`WorldRenderer.cs` or
`OverlayRenderer.cs`). That renderer takes the `CombatTuning` reference
instead of `Engine.cs`.

Impact: removes last `CombatTuning` dependency from `Engine.cs`.
Risk: L (part of FASE 4 extraction work anyway).
Discovered: 2026-04-18 during FASE 7.3 scope-report.

## Post-refactor feature requests

### Combat: rotate-to-face before attacking

Current behavior: ants attack regardless of facing direction
(can hit enemies at side/back).
Desired behavior: ant must rotate to face enemy (enemy in vision cone)
before committing to attack animation.
Impact: harness digest changes (this is a gameplay tweak, not refactor).
Effort: small feature, requires vision-check gate before CombatSystem
attack trigger.
Discovered: 2026-04-18 during FASE 7.3 visual testing.

### Pheromone overlay performance (tracked as FASE 6.6)

Current behavior: rendering with pheromone overlay enabled drops fps
from 2000+ to ~70 (96% drop).
Desired behavior: same visual result, significantly better fps.
Impact: harness digest unchanged (simulation logic identical);
rendering-only optimization.
Tracked as: `REFACTOR_PLAN.md` FASE 6.6 (added 2026-04-18).
Effort: profiling-driven; candidate approaches include SKPicture
caching, dirty-flag partial updates, tile-based rendering,
lower-resolution overlay texture.
Discovered: 2026-04-18 during FASE 7.1 visual testing.
