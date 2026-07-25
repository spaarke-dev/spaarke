# Current Task — `ai-advanced-capabilities-nda-r1`

**Active task**: none (task decomposition complete; ready to execute task 001)
**Status**: not-started
**Next action**: execute **task 001** (ADR-039 advisory-tier amendment) via `task-execute`. It is the MERGE GATE (FR-00) and a governance change — run **interactively with human review**, not autonomously (CLAUDE.md §6/§6.5).

## Pipeline progress
- [x] Pre-flight (branch current, tree clean, baseline builds)
- [x] Step 1 — spec validated (43 FRs, all sections)
- [x] Step 1.7 — ADR tensions processed (ADR-039 Path B, ADR-016 Path C)
- [x] Step 2 — resource discovery + artifacts (README, PLAN, CLAUDE.md)
- [x] Step 3 — task decomposition (`/task-create`) → 22 POMLs + TASK-INDEX.md (validator PASS)
- [ ] Step 4 — feature branch / push / draft PR ← NEXT (outward-facing; confirm before pushing)
- [ ] Step 5 — task execution (start with 001 = ADR-039 amendment, human-review gate)

## Task set
- 22 tasks / 7 phases. Critical path `001 → 010 → 020 → 023 → 031 → 040`.
- Validator: `pwsh scripts/Validate-TaskPoml.ps1 projects/ai-advanced-capabilities-nda-r1/tasks` → PASS (0 errors, 2 informational ui-test warnings on 040/060, covered by 061 e2e).
- Opus tasks: 001 (ADR amendment), 020 (advisory-quality Action), 023 (spine orchestration). Rest Sonnet-5.

## Notes
- **Task 001 is a merge gate + high-blast-radius** — advisory-tier tasks (020/023/031/040/041) MUST NOT merge before it.
- **§10 BFF-touching**: 010, 023, 040, 041 → Placement Justification + publish-size ≤60 MB in notes/PR.
- **NFR-06 tenant pin**: 012 verifies empirically, 052 guards with an integration test — highest-risk silent failure.
- Docs + tasks committed on `work/ai-advanced-capabilities-nda-r1` (rebased onto latest master); not yet pushed.
- Steps/files/decisions reset here when task 001 begins.
