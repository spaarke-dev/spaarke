# task-create — `/goal` condition for an eligible wave (Step 3.85)

> **Parent skill**: [task-create](../SKILL.md) Step 3.85. Read this when a wave is goal-eligible.
> **Last Reviewed**: 2026-11-03

Eligibility is decided in SKILL.md Step 3.85. For an eligible wave, compile a **by-reference** condition and store it in `TASK-INDEX.md` next to the wave.

## Rules

- **Stay under the 4,000-character `/goal` cap.** Do NOT enumerate every acceptance criterion; refer to them.
- **Prove completion through what the worker surfaces.** The evaluator reads only the transcript (it runs no commands and reads no files), so the condition names the wave's verify command and the gate outputs that must appear in the transcript.
- **Keep the turn cap.** It is the runaway guard.
- **Escalation is a legitimate exit, never an error.** A fired `<escalation>` trigger writes `BLOCKED.md` and ends the loop.

## Template

```
All of the following hold in this session:
(1) Every task in Wave {N} ({id-list}) shows its acceptance criteria passing via transcript
    output — {the wave's verify command, e.g. "dotnet test exits 0" / "npm test passes"};
(2) Each task's Step 9.5 gates (code-review + adr-check) have been RUN and their full findings
    surfaced in the transcript;
(3) git status shows only the wave's expected file changes.
OR: a BLOCKED.md exists under projects/{name}/ documenting a root-CLAUDE.md §6 escalation, shown in transcript.
Stop after {N_tasks × 6} turns if neither state is reached.
```

The `{N_tasks × 6}` turn cap is a default the operator may raise in the condition for FULL-rigor waves. Hitting it means re-launching after reading the transcript for the cause (`project-pipeline` Step 5 retry rule).

## Recording format in TASK-INDEX.md

```
Wave 2 (parallel, 3 agents): 020, 021, 022 — prereq: Wave 1 — goal-eligible: YES
  goal-condition: "{compiled condition}"
Wave 3 (sequential): 030 — touches .claude/ — goal-eligible: NO (single-task, .claude/ boundary)
```
