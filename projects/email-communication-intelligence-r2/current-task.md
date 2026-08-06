# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-06 (context-handoff — end of R-1/R-2/R-3 + 021-drift + 022 session)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **022 (FR-C2 context-merge on duplicate) ✅ code-complete (2026-08-06).** Owner-directed correct solution: two additive set-union memo columns. Prior this session: R-2 (83f2496d9), R-3 (ed62571d8), R-1 (9d69d2ca2), 021-drift (0e1ba86d3) — committed + pushed (fd899c668). |
| **Step** | 022 DONE (uncommitted — commit pending). `DeliveryContextMerge` (pure set-union + non-fatal MergeAsync) + inbound merge (3 sites, fail-open + contract-first) + office uploader merge. 14 tests; 1127 Communication+Office green; CVE clean. **GATED tail = task 028** (`sprk_deliveredmailboxes` + `sprk_savedbyusers` memo columns; operator). Note: `notes/022-context-merge-on-duplicate-complete.md`. |
| **Status** | **R-2 ✅ (code)**: editable Compose save that is byte-identical to a canonical is recorded as a hash-linked COPY (`sprk_canonicaldocument` set + notified), NOT suppressed (no data loss / session cross-wiring); GRADUATES to its own canonical on content divergence (link cleared via a new `DBNull` clear-sentinel on `IGenericEntityService.UpdateAsync`). `FindCanonicalByHashAsync` excludes linked copies. N+1 avoided (alt-key lookup widened). Build 0-err/0-warn; **854 Compose+ContentDedup green** (18 new); publish 48.30 MB (+0.01); CVE clean; ADR/code-review/conflict-check clean. Notes: `notes/R-2-compose-content-dedup-graduate-complete.md`. **GATED tail = task 027** (`sprk_canonicaldocument` self-lookup schema — operator go-ahead; code ships safely behind it, non-fatal until column exists). Prior session: **021 ✅** (84912c9cd), **016 ✅** (2bd7d905e), **024 ✅** (7bd7c2108). |
| **Next Action** | **PUSH first** — branch is **1 commit ahead of origin** (022 `6b1a194a6`; the R-1/R-2/R-3/021-drift set already pushed at `fd899c668`). Then next **non-gated code task**: **034 Job C apply endpoint** (opus·high) or **025 cross-path reconciliation** (deps 021 ✅). **Gated (operator go-ahead):** **task 027** (`sprk_canonicaldocument` — enables R-2) + **task 028** (`sprk_deliveredmailboxes`/`sprk_savedbyusers` memo — enables 022). **010 HMAC signer** also operator-gated (KV). Session done: R-1/R-2/R-3 + 021-drift + 022 all committed; suites green (Communication 962→1127 across the touched filters). Memory: `closed-r5-projects-editable.md` (compose-r5 + email-communication-solution-r5 CLOSED — edit directly). |

### Completed this session (all committed)
- **Task 003 ✅** — `notes/fixtures/r1-golden-emails.md`.
- **Task 030 ✅** — FR-D1 RAG grounding (`RegardingParentEntityMapper` + both index sites; N+1 fixed; seam 8/8; DEFER-030-01 filed). Commit `f1f5cf5dd`.
- **Task 031 ✅** — FR-D2 batched identifier query (`QueryRecordsByNumberFieldValuesAsync` In-filter; ≈175→≤7; rung tests migrated to batched seam 21/21). Commit `c700d1b0b`.
- **CVE fix ✅** — `System.Security.Cryptography.Xml` 8.0.3→8.0.4 (3 HIGH); solution-wide clean. Commit `0455d8658`.
- **Task 015 ✅** — FR-A3 self-association guarantee formalized + seam regression (`ThreadSelfAssociationRegressionTests`, 2/2; stripped-headers via In-Reply-To + References). No production code changed. → **task 032 must absorb this into the D3 golden suite.**

- **DEFER-030-01 ✅ CLOSED** (`e0650bcac`) — service-request RAG grounding added (core type); residual edge types intentional non-support. No open deferrals.
- **Task 032 ✅** — FR-D3 golden regression suite (`GoldenMisfileRegressionTests`, 3 golden scenarios drive the REAL spine → Ambiguous/Resolved/contact-not-filed verdicts reproduced; 149/149 Communication suite green). FR-A3 absorbed via co-located 015 file (no duplicate). Pillar D test coverage complete.
- **Task 011 ✅** — Tracking-footer config (`TrackingFooterOptions` + `TrackingFooterGate`, cloned from AutoFileOptions/AutoFileGate; unconditional DI in CommunicationModule; only KV secret name, no key material). 8/8 tests green. **Unblocks nothing new autonomously** — 012 (send-path inject) + 013 (TrackingTokenRung) both need 010 (Key Vault signer, gated). 014 (RecipientAliasRung+Bcc, deps 011) is now the next code-only candidate.

### Gates ahead (need operator go-ahead — NOT autonomous)
004 (Entra/security), 020/023 (Dataverse schema mutation), 033 (Dataverse seed), 010 (Key Vault), all deploys, all Pillar E (contended shared-lib, sequential).
### Standing reminders
- **/conflict-check MUST re-run before the PR** (030/031/015/DEFER-030-01 touched shared Communication + the AI-owned `ParentEntityContext.cs`; cleared only at execution time). Publish baseline 46.88 MB. **CVE fixed** (Xml 8.0.4).

### Files Modified This Session (2026-08-06 — remediation R-1/R-2/R-3 + 021-drift; all committed)
- **R-2** (`83f2496d9`): `Services/Compose/ComposeService.cs` (link-on-create + graduate; widened alt-key lookup), `Services/Documents/ContentDedupDetector.cs` (ResolveContentIdentityAsync + linked-copy exclusion + NotifyLinkedCopyAsync), `Spaarke.Dataverse/DataverseServiceClientImpl.cs` + `IGenericEntityService.cs` (DBNull clear-sentinel), tests `ContentDedupDetectorTests` + `ComposeContentDedupTests` (new), `tasks/027-canonicaldocument-selflookup-schema.poml` (new, GATED), TASK-INDEX row.
- **R-3** (`ed62571d8`): `Services/Office/OfficeDocumentPersistence.cs` (tuple return), `OfficeService.cs` (skip finalization + delete blob on dup), `OfficeStorageUploader.cs` (DeleteFromSpeAsync), `Infrastructure/Graph/SpeFileStore.cs` (DeleteFileAsync virtual), tests `OfficeDocumentPersistenceDedupTests` + `OfficeStorageUploaderDeleteTests` (new).
- **R-1** (`9d69d2ca2`): `Api/CommunicationEndpoints.cs` (POST /confirm-affinity), `Services/Communication/Engine/AffinityConfirmationRecorder.cs` + `Models/RecordAffinityConfirmation.cs` (new), `CommunicationModule.cs` (DI), client `ConnectionsWriteHandler.ts` (recordAffinity field) + `EmailConnectionsReview.tsx` (fire) + `EmailWorkspace.tsx` (BFF wire), tests `AffinityConfirmationRecorderTests` (new) + `EmailAssociationsAndTracking.test.tsx` (+2).
- **021-drift** (`0e1ba86d3`): `tests/…/Integration/CommunicationIntegrationTests.cs` (4 inbound stubs → `CreateCommunicationRaceProofAsync`).
- Memory saved: `closed-r5-projects-editable.md` (compose-r5 + email-communication-solution-r5 are CLOSED; edit directly).

### Critical Context
**Open questions resolved 2026-08-05 — project runs spike-free** (tasks 001/002 removed): gate-after-write dedup · Tier-2 deferred out of R2 · FR-E5 = Path B (create via `IActionSeam` + PATCH; add base/final-due-date fields, task 034) · backfill forward-only · browse shell = `BrowseModal` preset. See CLAUDE.md → **Decisions Made** + TASK-INDEX → **Resolved decisions**. Pillar-E UI is prototype-validated (`spaarke-prototype/projects/email-communication-intelligence-r2-uat`). Heavily-contended shared surfaces — `/conflict-check` before every shared PR; `parallel-safe:false` on shared writers. Execution intentionally **not started** — operator review gate.

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
*No decisions recorded yet*

---

## Next Action

**Next Step**: Review `tasks/TASK-INDEX.md`, then execute **003** (R1 close-out) or **020** (Pillar C alternate-key schema) — no spike gate.

**Pre-conditions**:
- Operator has reviewed the task breakdown
- No spike gate (spikes retired); 020/023 schema + 003/004 prereqs are the entry points

**Key Context**:
- Refer to `CLAUDE.md` for hot-path coordination + tiering rules
- ADR-045/024/013/018/028 apply broadly

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-08-05
- Focus: Project initialization via /project-pipeline (plan + tasks; execution deferred)

### Key Learnings
*See CLAUDE.md → Implementation Notes for discovery findings.*

### Handoff Notes
*Project scaffolded; awaiting operator go-ahead to execute task 001.*

---

## Quick Reference

### Project Context
- **Project**: email-communication-intelligence-r2
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
