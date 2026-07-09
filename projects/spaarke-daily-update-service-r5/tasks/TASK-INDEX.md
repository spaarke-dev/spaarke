# TASK-INDEX — spaarke-daily-update-service-r5

> **Generated**: 2026-07-08 (project-pipeline Step 3 / task-create)
> **Total**: 26 tasks · **Status**: all 🔲 not-started · **Awaiting**: operator go-ahead for execution
> **Baseline**: synced to origin/master (incl. r2-core #580/#582); BFF build green

## Rigor / tier distribution

- **FULL**: 010, 011, 012, 013, 014, 016*, 017, 020, 021, 023, 030, 033, 034, 090 (*016 = STANDARD rigor but opus tier)
- **STANDARD**: 002, 015, 016, 024, 031, 032, 035, 036, 037, 038, 040
- **MINIMAL**: 001, 022
- **opus tier** (judgment-heavy): 016 (eval-corpus design), 021 (visual/UX design). All others **sonnet**.
- **xhigh effort** (brownfield/root-cause): 010, 013, 016, 030, 033. All others **high**.
- **TEST-MODIFYING rigor**: 016, 033, 035 (touch `tests/**` → code-review + adr-check unconditional).

## Task registry

| ID | Title | Phase | Status | Deps | Blocks | Rigor | Tier/Effort | Parallel-safe |
|----|-------|-------|--------|------|--------|-------|-------------|---------------|
| 001 | OData naming convention doc | 0 | ✅ | none | 002 | MINIMAL | sonnet/high | ✅ |
| 002 | @odata.bind grep audit + fix in-use | 0 | 🔲 | 001 | — | STANDARD | sonnet/high | ✅ |
| 010 | Remove per-channel LLM narrate leg | A | ✅ | none | 011,012,013 | FULL | sonnet/xhigh | ❌ narrator/composite |
| 011 | Deterministic item-row rendering | A | ✅ | 010 | 016 | FULL | sonnet/high | ❌ client lib |
| 012 | Retire BRIEF-NARRATE-CHANNEL Action | A | 🔲 | 010 | — | FULL | sonnet/high | ❌ narrator.cs |
| 013 | Deterministic TL;DR factual scaffolding | A | ✅ | 010 | 014,016 | FULL | sonnet/xhigh | ❌ composite/collector |
| 014 | Binary anchor resolution (no threshold) | A | ✅ | 013 | 016 | FULL | sonnet/high | ❌ TldrSection/composite |
| 015 | Groundedness guardrail (eval-only) | A | 🔲 | none | — | STANDARD | sonnet/high | ✅ Group G |
| 016 | Eval family — mixed-item corpus + gate | A | 🔲 | 011,013,014 | 017 | STANDARD(TEST-MOD) | **opus**/xhigh | ❌ |
| 017 | Phase A deploy + G-R5-A UAT + metering | A | 🔲 | 016 | 090 | FULL | sonnet/high | ❌ deploy |
| 020 | Scaffold `/prototype` harness (cross-repo) | D | ✅ | none | 021 | FULL | sonnet/high | ❌ cross-repo |
| 021 | Design iterations (UX params) | D | 🔲 | 020 | 022 | FULL | **opus**/high | ❌ |
| 022 | Operator harness sign-off (GATE G-R5-D) | D | 🔲 | 021 | 023 | MINIMAL | sonnet/high | ❌ operator gate |
| 023 | Production port to shared lib | D | 🔲 | 022,011 | 024 | FULL | sonnet/high | ❌ client lib |
| 024 | Phase D deploy + G-R5-D UAT | D | 🔲 | 023 | 090 | STANDARD | sonnet/high | ❌ deploy |
| 030 | CoerceFieldValue String→Choice fix | B | ✅ | none | 032 | FULL | sonnet/xhigh | ❌ frozen-engine |
| 031 | jps-validate Step 7.7 Choice check | B | 🔲 | none | — | STANDARD | sonnet/high | ❌ `.claude/` main-session |
| 032 | fieldMapping sweep + restore documenttype | B | 🔲 | 030 | — | STANDARD | sonnet/high | ❌ |
| 033 | Collaborator-scope fix + re-flip test | B | ✅ | none | 034 | FULL(TEST-MOD) | sonnet/xhigh | ❌ collector |
| 034 | Collector de-duplication | B | ✅ | 033 | 036 | FULL | sonnet/high | ❌ collector |
| 035 | Client-helper jest tests | B | ✅ | none | — | STANDARD(TEST-MOD) | sonnet/high | ✅ Group H |
| 036 | Collapse 7 QueryHighPriority* helpers | B | 🔲 | 034 | 037 | STANDARD | sonnet/high | ❌ collector |
| 037 | Promise-cache primary-contact + comment | B | 🔲 | 036 | 038 | STANDARD | sonnet/high | ❌ collector |
| 038 | Phase B deploy + G-R5-C UAT | B | 🔲 | 037,032,035 | 090 | STANDARD | sonnet/high | ❌ deploy |
| 040 | Deploy convention (master-sync-first) | E | ✅ | none | — | STANDARD | sonnet/high | ✅ Group F |
| 090 | Project wrap-up | Wrap | 🔲 | 017,024,038,040 | — | FULL | sonnet/high | ❌ main-session |

## Shared-file serialization (why so much is parallel-safe=false)

Most briefing tasks touch a small set of shared files; concurrent edits would collide. The binding chains:

- **`DailyBriefingNarrator.cs` / `DailyBriefingCompositeService.cs`**: 010 → (012, 013) → 014
- **`DailyBriefingCollector.cs`**: 013 (scaffolding) and the hardening chain **033 → 034 → 036 → 037** all touch this file. **Do not run a collector task concurrently with another collector task.** Sequence 013 relative to the 033-chain (either before 033 or after 037; recommend after 013's Phase-A work merges).
- **client `Spaarke.DailyBriefing.Components`**: 011 → 023 (023 is the redesign port; also deps 022)
- **`.claude/`** (031): main-session-only (sub-agents cannot write `.claude/`).

## Critical path

`010 → 013 → 014 → 016 → 017 → 090` (accuracy headline) is the longest chain. Phase D (`020→021→022→023→024`) runs parallel to A but **023 also deps 011** (must render against the stable deterministic view-model). Phase B is largely independent of A except the shared collector file (see above).

## Parallel Execution Plan

> Max 6 agents/wave. Build-verify between waves (dotnet build for `.cs`, npm build for `.ts/.tsx`). Run `/conflict-check` before each wave (r2-core `Services/Ai/` overlap).

**Wave 1 (parallel, distinct files — up to 6 agents)** — prereq: none
`001` (docs) · `030` (UpdateRecordNodeExecutor.cs) · `033` (collector — starts B collector chain) · `035` (client tests) · `040` (deploy docs) · `020` (harness, cross-repo)
— `015` and `031` are also root-ready; run `015` in this wave if a slot is free; run `031` **main-session** (not as a sub-agent) any time.
— goal-eligible: **NO** (mixed correctness-critical + cross-repo + `.claude/` boundary; per-task review required)

**Wave 2** — prereq: Wave 1 roots
`002` (after 001) · `010` (start A chain — narrator/composite; NOT collector, so parallel-safe vs 033-chain) · `032` (after 030) · `021` (after 020) · `034` (after 033, collector)
— goal-eligible: **NO**

**Wave 3** — prereq: Wave 2
`011`,`012`,`013` (after 010; 013 touches collector → must not overlap 034/036 — sequence after the 033-chain reaches a safe point) · `022` (after 021 — operator gate) · `036` (after 034, collector)
— goal-eligible: **NO** (022 is an operator sign-off)

**Wave 4** — prereq: Wave 3
`014` (after 013) · `023` (after 022 + 011) · `037` (after 036, collector)

**Wave 5** — prereq: Wave 4
`016` (after 011,013,014) · `024` (after 023) · `038` (after 037,032,035)

**Wave 6** — prereq: Wave 5
`017` (after 016)

**Wave 7 (final)** — prereq: 017,024,038,040
`090` wrap-up (main-session)

*Wave assignment is a scheduling suggestion; the binding contract is each task's `<dependencies>` + the shared-file serialization above. The collector chain (033→034→036→037) and the 013 collector touch must never run concurrently.*

## Gates

- **G-R5-A** (accuracy) → verified at 017 (browser UAT on spaarkedev1 + eval green + token metering)
- **G-R5-D** (appearance) → operator sign-off at 022, browser UAT at 024
- **G-R5-C** (hardening) → verified at 038 (collaborator smoke + Choice write)

## Deferred (tracked at wrap-up 090 via `/defer`)

- Monitored-For schema (D-3) — future round
- EventDetailSidePane `@odata.bind` one-liner — side-pane not in use
