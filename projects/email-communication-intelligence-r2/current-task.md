# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-06 (CRITICAL task→sprk_event write-core fix done + pushed; task 034 apply-endpoint UNBLOCKED, not started)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **CLEAN BREAK POINT (2026-08-06). No task in progress — safe to start a fresh session.** Last completed: **012 ✅** (send-path footer injection), committed+pushed `78ba44753`. All work this session is committed+pushed; build green; no half-done edits. |
| **Session done (this session, all committed+pushed)** | **043 ✅** (unify user-upload w/ capture — `EmailUploadCaptureService`; commit `bbf989c03`). **004 ✅** (Entra NAA registration verified live — already provisioned+consented; runbook `notes/runbooks/004-entra-naa-registration.md`; commit `75e1fb237`). **027/028 ✅** (dedup schema columns — operator-created, verified live) **+ 029 ✅ RESOLVED BY REUSE** of existing `sprk_document.sprk_relatedcommunication` (no new column; `CrossPathLink` rewired; commit `eac5f9c10`; note `027-028-029-schema-closeout.md`). **010 ✅** (HMAC `ITrackingTokenSigner` — KV key via central credential, FixedTimeEquals, 18 tests; commit `20b758746`; note `010-tracking-token-signer-complete.md`). **012 ✅** (send-path footer injection — `ApplyTrackingFooterAsync`, both branches, 7 tests; commit `78ba44753`; note `012-footer-injection-send-path-complete.md`). |
| **Next task** | **013 — `TrackingTokenRung` (Pillar A, opus·high, deps 010✅/011✅ satisfied, `parallel-safe:true` A-rungs).** Verify the inbound footer token via `ITrackingTokenSigner.VerifyAsync`; on valid → deterministic regarding (reuse `RungKind.ExplicitReference` per project CLAUDE.md Implementation Notes → zero `AssociationStatusMapper` change). Self-contained new rung file under `Services/Communication/Engine/Rungs/`. POML: `tasks/013-trackingtokenrung.poml` (verify exact filename). Read the token from the inbound `NormalizedMessage` body (the footer survives quoted reply). Fresh session recommended (user asked). |
| **Status** | Pillar A: 010✅ 011✅ 012✅ 014✅ 015✅ 016✅ · **013🔲 (next, unblocked)** · 017 deploy (gated/paused). Pillar C: 020–025✅ + **027/028/029✅ (schema in place)** · 026 deploy (gated). Pillar D: 030–032✅ 034✅ · 033🔲(seed, gated) · 035 deploy. Pillar B: 041/042/043✅ · 040🔲(dep 004 now ✅ — UNBLOCKED for dev) · 044/045 deploy. Pillar E: 050–057🔲 (contended shared-lib, operator-gated). |
| **Next Action** | New session → `continue` runs **013**. **Feature-activation note**: the tracking footer (010/012) is code-complete but INERT until the operator provisions the KV signing key + sets `Communication:TrackingFooter:{SigningKeySecretName,Enabled}` + `KeyVaultUri` (runbook in `notes/010-…md`). **Still operator-gated:** 033 seed, all deploys (paused per operator), Pillar E. **040 now unblocked** (004 ✅) for dev add-in realignment. |

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
