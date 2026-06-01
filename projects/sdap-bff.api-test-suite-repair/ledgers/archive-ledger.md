# Archive Ledger — `sdap-bff.api-test-suite-repair`

> **Purpose**: Track every test file archived per [`design.md`](../design.md) §6.5 + NFR-06 ("Archive via rename to `*.cs.archived-YYYY-MM-DD`; never delete"). Each entry documents what was archived, why, and when.
>
> **Schema**: Each row identifies the archived file, the archive date, the archiving task ID, and the §6.5 justification (`archived-duplicate` / `archived-dead-target` / `archived-rewrite-deferred`). Per NFR-04, if any single phase exceeds 10 archives, owner sign-off is required and recorded in this ledger.
>
> **Authoritative reconciliation point**: Phase 4 task 085 (publish all ledgers) reconciles this ledger against actual `*.cs.archived-*` files in the working tree. The post-Phase-2+3 canonical baseline ([`baseline/post-phase23-authoritative-2026-05-31.md`](../baseline/post-phase23-authoritative-2026-05-31.md)) confirms the cumulative count at the close of Phase 2+3.

---

## Cumulative archive count by phase

| Phase | Task IDs | Archives | NFR-04 ceiling (10/phase) | Owner sign-off |
|---|---|---:|---|---|
| Phase 0 (baseline + decisions) | 001–008 | 0 | ✅ trivially satisfied | n/a |
| Phase 1 (P1.A/B/C/D/E) | 010–025 | 0 | ✅ trivially satisfied | n/a |
| Phase 2+3 Wave 2.1 (P23.A + P23.B + P23.H part 1) | 030, 031, 033, 034, 040, 041 | 0 | ✅ | n/a |
| Phase 2+3 Wave 2.2 (P23.H part 2 + P23.M part 1) | 042–046, 050 | 0 | ✅ | n/a |
| Phase 2+3 Wave 2.3 (P23.M part 2) | 051–056 | 0 | ✅ | n/a |
| Phase 2+3 Wave 2.4 (P23.I) | 060–063, 027, 032 | 0 | ✅ | n/a |
| **Phase 2+3 Wave 2.5 (P23.L LOW-tier)** | **070, 071, 072, 073, 074** | **0** | ✅ **trivially satisfied** | **n/a (no escalation filed)** |
| **CUMULATIVE TOTAL through Phase 2+3 close** | (all above) | **0** | ✅ | n/a |

**Status**: Zero archives have been created across the entire Sprk.Bff.Api.Tests + Spe.Integration.Tests repair effort. The §6.2 final end-state `archived` was not used; repair tracks (`repaired` / `real-bug-pending-fix` / `flaky-quarantined`) absorbed all dispositioned tests.

---

## Pre-existing archives (out of scope for this project)

The following archive predates this project and is documented here for completeness only:

| File | Archive date | Archived by | Justification |
|---|---|---|---|
| [`tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14`](../../../tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14) | 2025-10-14 | Pre-project (sdap-bff-api-remediation-fix predecessor) | Pattern precedent — cited in this project's [`CLAUDE.md`](../CLAUDE.md) §"Implementation Notes" as the canonical archive-naming exemplar |

This file is **not counted** in this project's cumulative archive count (it was already in the working tree at Phase 0 baseline).

---

## Reconciliation (verified by task 074 — 2026-05-31)

`projects/sdap-bff.api-test-suite-repair/baseline/post-phase23-authoritative-2026-05-31.md` confirms:

- `git ls-files | Select-String 'archived-2026'` returns **0 matches** — no project-era archived files exist
- `git ls-files tests/unit/Sprk.Bff.Api.Tests/**/*.archived-* tests/integration/Spe.Integration.Tests/**/*.archived-*` returns only `JobProcessorTests.cs.archived-2025-10-14` (the pre-existing predecessor archive)
- `escalations/` contains zero `archive-approval-T-*` files — none were required because no batch approached the 10-archive ceiling
- Wave 2.5 tasks 070, 071, 072, 073 each completed with explicit "0 archives" outcome (per their respective POML notes + current-task.md decision logs)

**Conclusion**: The archive ledger is fully reconciled. Cumulative archive count through Phase 2+3 close = **0** (excluding the pre-project precedent file). NFR-04 (≤10 archives per phase) is satisfied trivially across all phases of this project. No owner escalation has been filed because none has been required.

---

## Schema reference (for future Phase 4 entries — none expected)

If Phase 4 introduces archives (unlikely at this point — repair tracks have absorbed all known dispositions), each entry MUST follow this format:

```markdown
## ARC-T0XX-NN — {brief description}

| Field | Value |
|---|---|
| **Archive ID** | ARC-T0XX-NN |
| **Date** | 2026-MM-DD |
| **Archiving task** | Task 0XX (Phase X — {name}) |
| **Original file** | `tests/.../FileName.cs` |
| **Archived file** | `tests/.../FileName.cs.archived-2026-MM-DD` |
| **Justification** | `archived-duplicate` / `archived-dead-target` / `archived-rewrite-deferred` |
| **Replaced by** | (if duplicate, the test file that retains coverage; else "n/a — coverage abandoned per §6.5") |
| **Owner sign-off (if NFR-04 escalation)** | (signed in `escalations/archive-approval-T-XXX.md` if cumulative phase count > 10) |

### Detail
{Brief explanation of why archived instead of repaired. Must cite §6.5 justification.}
```

---

*This ledger is required at Phase 2+3 exit gate (per [`design.md`](../design.md) §6.5) and re-verified at Phase 4 task 085 final publishing.*

---

## Finalization (2026-05-31 by task 085)

- **Cumulative project archive count**: 0 (excluding pre-existing `JobProcessorTests.cs.archived-2025-10-14`)
- **NFR-04 ceiling (≤10/phase)**: ✅ trivially satisfied across Phase 0, Phase 1, Phase 2+3 Waves 2.1–2.5, and Phase 4
- **NFR-06 (rename-not-delete)**: ✅ trivially satisfied (no archives created)
- **Owner sign-off escalations filed**: 0 (none required per ceiling)
- **Reconciliation with `git ls-files`**: ✅ task 074 verified 0 project-era `.archived-2026-*` files
- **Contribution to exit ledger touched-test sum**: 0
