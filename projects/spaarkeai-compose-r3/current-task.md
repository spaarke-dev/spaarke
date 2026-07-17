# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-16
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 001 ✅ + 002 ✅ complete — **PAUSED for owner input on 2 systemic decisions** |
| **Step** | — |
| **Status** | awaiting-owner |
| **Next Action** | Owner to confirm: (1) Docxodus 6.4.0/net8 vs net10-migration; (2) BFF-IO test doctrine (defer-to-seam per ADR-038 vs unit tests). Then: task 010 (E2, OpenXml-only, Docxodus-independent) serial + **W4 toolset (040/042/043/044) parallel-agent fan-out**. Task 003 blocked on owner-supplied real firm `.docx` templates. |

### Critical Context
All six pre-spec spikes (S1/S1b/S2/S3/S4/S5) passed — no design pivots. The fidelity core sequences E2 (paraId substrate) → E1 (delta save); toolset + E3 parallelize; import depends on E1/E2. The NFR-09 real-template hardening gate (Phase 6) gates the E1 delta-save cutover.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-07-16: Seed README moved to `notes/seed-README.md`; canonical README generated (operator chose "regenerate canonical"). — Reason: preserve lineage while giving a standard project overview.
- 2026-07-16: Pipeline stopped at "ready to execute" (operator chose "generate + stop"); task 001 NOT auto-started. — Reason: FULL-rigor BFF blast radius + hot-path overlap with compose-r2 warrants owner coordination first.
- 2026-07-16: Owner confirmed `spaarkeai-compose-r2` completed/closed + all work on master. — E1-cutover coordination gate (task 022 pre-condition) CLEARED. Residual gate before any BFF PR: run `/conflict-check` for `Services/Compose/` hot-path.
- 2026-07-16: **Task 001 COMPLETE.** §6.5 Path-C: adopted Docxodus **6.4.0** (net8.0 line) instead of spec-named 7.1.0 — 7.x is net10.0-only (NU1202), 6.4.0 is same MIT fork + engine + pulls OpenXml 3.5.1. SkiaSharp×2 (managed + Linux native pkg) excluded runtime;native → 0 SkiaSharp in publish, no runtimes/. Publish 47.26 MB incl PDBs (+0.60 MB vs fresh 46.66 MB baseline). No new HIGH CVE (only pre-existing Kiota, accepted per ADR-029). **DOC-RECONCILE**: design §12.3 + tasks 010/020/021/022 say "7.1.0" → 6.4.0. **OPEN**: confirm 6.4.0/net8 acceptable (or plan net10 migration → 7.1.0).
- 2026-07-16: **Task 002 COMPLETE.** Added `DownloadFileVersionAsUserAsync` to SPE facade (ISpeFileOperations + SpeFileStore + DriveItemOperations) via Graph v5 `/versions/{id}/content` OBO; 404→null; ADR-007 clean; build green. **§6.5 Path-C: unit test DEFERRED to 022/024 seam** — Graph v5 unmockable at DriveItemOperations level (all 5 existing SpeFileStoreTests are `[Fact(Skip)]`); ADR-038 bans facade-mock scaffolding + mandates seam tests; POML says seam rides 022/024. **SYSTEMIC OPEN**: confirm defer-to-seam doctrine for all R3 BFF-IO tasks (010/020/021/022/023), or require unit tests. **TRACKING**: task 024 seam MUST assert baseline-by-versionId retrieval.

---

## Next Action

**Next Step**: Execute task 001 (Phase 0 — Docxodus packaging + publish-size/CVE baseline).

**Pre-conditions**:
- ✅ Owner confirmed `spaarkeai-compose-r2` completed/closed + on master (2026-07-16) — `Services/Compose/` collision risk on the E1 cutover cleared.
- `/conflict-check` run for BFF hot-path (still recommended before opening any BFF PR).

**Key Context**:
- Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before any BFF task; report publish-size delta vs ~49.63 MB baseline.
- Docxodus MUST exclude SkiaSharp assets; never call `HtmlToWml`/`FormattingAssembler`.

**Expected Output**: Docxodus + OpenXml 3.5.1 referenced (SkiaSharp excluded), publish-size + CVE baseline recorded.

---

## Blockers

**Status**: None. Prior soft gate (compose-r2 coordination) CLEARED 2026-07-16 — R2 completed/closed + on master.

---

## Session Notes

### Current Session
- Started: 2026-07-16
- Focus: Pipeline initialization (artifacts + task decomposition) — complete; awaiting execution kickoff.

### Key Learnings
- Engine frozen (ADR-039): E3 is server-derived, NOT a new Action output — no catalog rows change.

### Handoff Notes
*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-039 (frozen engine / closed catalogs), ADR-040 (ledger), ADR-013 (AI facade), ADR-007 (Graph isolation), ADR-005/009/015 (SPE/Redis/Tier-3), ADR-021/028 (Fluent v9 / auth), ADR-038 (testing), ADR-029 (publish hygiene), ADR-032 (Null-Object, if gated).

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above.
2. **If more context needed**: Read Active Task and Progress sections.
3. **Load task file**: `tasks/{task-id}-*.poml`.
4. **Load knowledge files**: From task's `<knowledge>` section.
5. **Resume**: From the "Next Action" section.

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
