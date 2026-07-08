# `/goal` feature evaluation — for spaarke-ai-architecture-redesign-r1

> 2026-07-05 · researched via claude-code-guide agent against official docs
> (code.claude.com/docs/en/goal.md, commands.md, changelog — all fetched
> 2026-07-05). Operator-requested evaluation before /design-to-spec.

## What it is (facts)

- **GA** since Claude Code v2.1.154 (2026-05-28). Built-in command (confirmed
  NOT a local skill in this repo). Surfaces: CLI, desktop, remote control,
  non-interactive `-p`.
- `/goal <condition>` (≤4,000 chars) sets a **completion condition**; Claude
  keeps working turn after turn until a **separate fast evaluator model**
  (Haiku) judges the condition met from the conversation transcript. If not
  met, the evaluator's reason seeds the next turn. `/goal` = status;
  `/goal clear` = stop. Indicator: `◎ /goal active`.
- **The evaluator does not run commands or read files** — it judges only what
  Claude has surfaced in-conversation. Conditions must therefore be
  *observable end states with a stated check* ("`dotnet test` exits 0 and the
  output is shown", "grep for X returns zero hits, shown"), never subjective
  ("architecture is clean").
- Session-scoped: one goal at a time; survives `--resume`/`--continue`
  (counters reset); killed by `/clear`. Requires hooks enabled + workspace
  trust (both true in this repo). Eval cost negligible (Haiku per turn).
- vs `/loop`: /loop is time-driven, /goal is completion-driven. vs Stop hooks:
  /goal is per-session and declarative.

## Verdict for THIS project: **adopt, at wave level — never at phase-gate level**

The project has three altitudes of "done", and /goal fits exactly one:

| Altitude | Done-ness decided by | /goal fit |
|---|---|---|
| **Task** (POML) | `task-execute` protocol + Step 9.5 gates (code-review, adr-check) | ❌ too fine — the protocol already governs turns within a task |
| **Wave / work batch** (e.g. "P0 tasks 001-006", "Track-B sweep batch 2") | objective, machine-checkable evidence | ✅ **the sweet spot** |
| **Phase gate** (G-P1..G-P4) | **human UAT scripts** (design.md §2) — browser UAT, operator judgment | ❌ by design — the evaluator cannot perform UAT, and the gates exist precisely so a human decides |

### Why the wave level fits so well here

The migration map was (accidentally) written in /goal's native language:
**grep-verified, observable acceptance**. Examples of conditions this project
can use nearly verbatim:

- **Track-B sweep batch**: `/goal "Every file in Track-B batch N is deleted; grep for each deleted symbol returns zero hits (output shown); dotnet build and npm run build:prod succeed (output shown); TASK-INDEX.md rows marked ✅. Do not modify files outside the batch list. Stop after 25 turns."`
- **P0 ledger dark-ship**: `/goal "SessionOutput model + persistence lands; ledger round-trip test (write→Redis→Cosmos→restore) passes with output shown; zero readers reference Outputs yet; publish-size delta reported; code-review + adr-check clean."`
- **P2 deletion proof**: `/goal "grep for PlaybookDispatcher, IntentRerankerService, PlaybookCandidateSelector, CompoundIntentDetector returns zero hits outside git history (shown); eval suite green (shown)."`

### Composition rules (bake into the project CLAUDE.md via the pipeline)

1. Goal conditions MUST include: the evidence to display (test/grep/build
   output), a scope bind ("no files outside …"), a turn cap, and "quality
   gates (Step 9.5) passed" so /goal cannot steamroll the rigor protocol.
2. `/goal` NEVER wraps a G-gate: phases end with `/goal clear` + human UAT +
   operator approval, then the next wave's goal is set.
3. Each wave's suggested goal condition is authored INTO the wave definition
   at /task-create time (conditions are reviewable artifacts, not ad-hoc).
4. Checkpoint discipline unchanged: context-handoff cadence applies inside
   goal-driven runs (goals survive resume; counters reset — acceptable).

## Cautions

- Autonomy amplifier: /goal + auto mode removes both per-turn and per-tool
  prompts — use scope binds + turn caps religiously on BFF hot-path waves.
- The evaluator sees only the transcript: a condition met "silently" (tests
  passed but output not shown) reads as unmet — conditions must demand shown
  evidence (this is a feature: it forces the faithful-reporting discipline).
- Compaction behavior is not explicitly documented; assume evaluator context
  resets and keep conditions self-contained (no "as discussed above").

## Sources
code.claude.com/docs/en/goal.md · commands.md · scheduled-tasks.md ·
changelog (v2.1.154) — fetched 2026-07-05. Local check: no /goal skill in
.claude/skills/ or ~/.claude (built-in).
