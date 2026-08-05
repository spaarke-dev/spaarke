# Implementation Plan — Email Communication Intelligence R2

> **Source spec**: [spec.md](./spec.md) · **Charter**: [design.md](./design.md) · **Generated**: 2026-08-05 via `/project-pipeline`

## 1. Executive Summary

**Purpose**: Harden R1's trusted-capture layer — dedup once, file through one intelligent path, tamper-evident tracking, and a reconciliation UI that surfaces the intelligence R1 left dark. All additive (ADR-045 extend-never-fork).

**Scope**: 5 pillars, 24 FRs + 11 NFRs. Backend enablers (A/C/D) precede the surfaces that consume them (B/E). Pillar E's UI is prototype-validated.

**Estimated effort**: 7 phases, **40 tasks** (spikes 001/002 retired 2026-08-05). Dominant tier **Sonnet 5 @ high**; **Opus/xhigh** on 5 correctness/security-critical tasks (token signing, race-proof dedup, SPE detector, Job C apply, citation glue).

## 2. Architecture Context

- **Association Engine** (ADR-045): single rung ladder over `NormalizedMessage`; new rungs implement `IAssociationRung` and write regarding via `RegardingFieldMap` (ADR-024). Status/eligibility in `AssociationStatusMapper`.
- **AI facade** (ADR-013/NFR-05): AI reached only via `Services/Ai/PublicContracts/` (`IActionSeam`).
- **Config** (ADR-018): operator-managed app settings via `IOptionsMonitor` (`AutoFileOptions`/`AutoFileGate` pattern). HMAC key in Key Vault (ADR-028, Path A).
- **BFF hygiene** (§10): publish ≤60 MB; unconditional DI + Null-Object (ADR-010/032); tests updated.
- **UI** (ADR-050/012/021/022): enhance shared `DataGrid`; reconcile modals = `SprkModal` `FormModal`; reuse `EmailConnectionsReview`, `EmailReadingPaneShell`, Compose `CitationResolver`; React-version-agnostic (dual-use).
- **Schema/solution** (ADR-027): new column / alternate key / affinity store / seed → managed solution.

### Discovered Resources (cited in task POMLs)
- **ADRs**: 045, 024, 018, 013, 028, 007, 010, 032, 015, 027, 038, 004, 048, 040; 050, 012, 021, 022.
- **Skills**: `task-execute`, `conflict-check`, `dataverse-create-schema`, `dataverse-deploy`, `bff-deploy`, `office-addins-deploy`, `code-page-deploy`, `pcf-deploy`, `ribbon-edit`, `fluent-v9-component`, `spe-integration`, `test-diet`, `adr-check`, `code-review`, `azure-deploy`.
- **Knowledge**: `communication-intelligence-architecture.md`, `communication-service-architecture.md`, `office-outlook-teams-integration-architecture.md`, `COMPOSE-READ-REFERENCE-FIDELITY.md`, `SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`, `MODAL-DESIGN-SYSTEM.md`, `COMMUNICATION-ADMIN/DEPLOYMENT-GUIDE.md`, `SECRET-ROTATION-PROCEDURES.md`, `.claude/constraints/bff-extensions.md`, `.claude/agent-memory/researcher/spe-dedup-content-identity-2026-07.md`.
- **Prior projects**: r1 (engine + golden emails), r5 (shell + components + coordination contract), `sdap-file-duplication-detector-r1` (absorbed dedup + spikes), `dataset-grid-framework-r2` (DataGrid conflict-check).

## 3. Implementation Approach

Dependency-ordered phases. Critical path: **P0 spikes → C1/C3 dedup → B3 unify → D5 Job C apply → E5 task tab**. Pillar A largely parallel after A1a/A1b. Pillar E mostly sequential (shared-lib `parallel-safe:false`).

## 4. Work Breakdown Structure

### Phase 0 — Prerequisites *(spikes retired 2026-08-05)*
- **003** R1 close-out — reconcile R1 task 013; pin R1 UAT golden emails (feeds FR-D3).
- **004** Entra NAA app-registration verification (FR-B0 runtime prereq; runbook).
> **Spikes retired (owner 2026-08-05):** SPE dedup adopts **gate-after-write** (read `quickXorHash` from the driveItem *after* upload; reconcile + notify; accept a transient blob) — no spike-1 gate. **Tier-2 (near-dup) deferred out of R2** — exact-hash Tier-1 only — no spike-2.

### Phase 1 — Pillar A: Trusted threading & external matching
- **A1a** HMAC footer/token signing helper (Key Vault, `DefaultAzureCredential`). **Opus/xhigh.**
- **A1b** Footer config (operator app setting; clone `AutoFileOptions`/`AutoFileGate`).
- **A1c** Footer injection on send path (`CommunicationService.SendAsync` pre-`ResolveSender`) + add-in compose.
- **A1d** `TrackingTokenRung` (reuse `RungKind.ExplicitReference`; signed-valid=1.0, bare=0.65, forged=ignored, verify-before-trust).
- **A2** `RecipientAliasRung` + Bcc (add `bccRecipients` to Graph `$select` + `GraphMessageNormalizer`→`NormalizedMessage`; new `RungKind` + mapper eligibility).
- **A3** External-reply self-association — **formalize + regression test only** (already in `ThreadContinuityRung`; folds into D3).
- **A4** `AffinityRung` + new `sprk_affinity` store (per-tenant frequency table; suggest-only; ADR-040 Path A).

### Phase 2 — Pillar C: De-duplication *(C3 gated on P0-a)*
- **C1** internet-message-id dedup + Dataverse alternate key (extend `ExistsByGraphMessageIdAsync`; race-proof; SB idempotency on message-id). **Opus/xhigh.** *Schema: alternate key.*
- **C2** Context-merge on duplicate (delivery/recipient/uploader context on canonical row).
- **C3** SPE content dedup Tier-1 — **gate-after-write** (read `quickXorHash` from driveItem post-upload; detector on `SpeFileStore`; all upload paths incl. email-attachment; notify-never-silent). **Opus.** *Schema: `sprk_canonicalhash` column, **forward-only** (no backfill).* Tier-2 deferred out of R2.
- **C4** Cross-path reconciliation (`sprk_communication` ↔ `sprk_document` via message-id).

### Phase 3 — Pillar D: R1 carry-overs *(backend enablers for B/E)*
- **D1** Fix FR-06 RAG grounding (ParentEntity tagging at `IncomingCommunicationProcessor` L1031 + `CommunicationEnrichmentService` L388; **forward-only**, no historical re-index).
- **D2** Batched identifier query (`IdentifierReverseLookupRung` → `In`-filter; ≈175→≤7).
- **D3** Golden regression suite (R1 UAT misfile emails; ADR-038 KEEP path; absorbs A3 test).
- **D4** Job B allow-list seed (`sprk_emailupdatefield` starter rows).
- **D5** Job C apply endpoint + `create-task` queue-feed discriminator (sibling of `CommunicationProposalApplyService`). **Path B**: create via `IActionSeam.CreateTaskAsync`, then PATCH status/completed-date/base-date/final-due-date via impersonated `UpdateRecordAsync` under the same audit; **add `base-date` + `final-due-date` task-entity fields** (schema step). **Opus.** Backs E5.

### Phase 4 — Pillar B: Unified filing surfaces *(B3 depends on C1)*
- **B0** Add-in realignment (Entra NAA sign-in; Word manifest; `authenticatedFetch`; cleanup).
- **B1** Real Spaarke intake folder (shared mailbox+folder AND add-in drag target → full pipeline; deduped).
- **B2** Drag-to-matter + engine suggestions (reuse `derivePrimaryReview`); finish ribbon quick-save.
- **B3** Unify user-upload with capture (same engine + dedup via C1).

### Phase 5 — Pillar E: Reconciliation UI *(depends on D5 + DataGrid; conflict-check r5 + dataset-grid-framework-r2)*
- **E2** Reconciliation grid (enhance `DataGrid` via `overrides.columnRenderers` + `onRecordAction`; `sprk_gridconfiguration` over `sprk_communication` type=Email). `parallel-safe:false`.
- **E1** Triage as grid columns + detail (`columnRenderers`).
- **E3** Related-to card-picker (reuse `EmailConnectionsReview`; no second write path).
- **E-shell+reader** Browse shell (**`BrowseModal` preset** — `@spaarke/ui-components`, `SprkModal/presets`, ADR-050) + one normalized reader (`EmailReadingPaneShell(hideList)` + extended `EmailBodyView` folding attachment text) + citation navigation via ParaIdMap + `resolveCitation`. **Opus/xhigh** (net-new glue; NFR-11).
- **E4** Field-update reconcile tab (`FormModal`; value editable before Accept; `POST /proposals/{id}/apply`).
- **E5** Task/deadline reconcile tab (`FormModal`; name/desc/base·due·final/assigned-to/status/completed; create-and-complete + ad-hoc). Depends on D5.
- **E7** Reconciliation routing (category→team ADR-018 config + per-team `sprk_gridconfiguration` views + `membershipFilter`; no new entity).
- **E6** r5 coordination (update `projects/email-communication-solution-r5/notes/email-intelligence-r1-coordination.md`).
- **NFR-10** wiring: association confirmed before Fields/Tasks; re-scope on override.

### Phase 6 — Wrap-up
- `/test-diet` (ADR-038); lessons-learned; doc-drift audit; update coordination contract + `projects/INDEX.md`; final publish-size report.

## 5. Dependencies

- **External**: SPE spike outcomes; Entra NAA registration; Exchange mail-flow rule (per client, optional).
- **Internal**: r5 (shared components), dataset-grid-framework-r2 (DataGrid), compose (`CitationResolver`), ai-architecture-redesign-r2 (`Services/Ai` owner), `sdap-file-duplication-detector-r1` (absorbed).

## 6. Testing Strategy

- **Seam tests** (ADR-038) under `tests/integration/seam/Communication/**`: dedup (C1/C3), tagged-send→reply→capture (A1), drag-vs-capture parity (B), RAG grounding (D1).
- **Golden regression** (D3) from R1 UAT misfile emails — KEEP path, CI-guarded.
- **Unit**: rung eligibility/mapper decisions; citation resolution parity with Compose server twin.
- **UI**: Pillar E end-to-end reconcile flow on code-page + SpaarkeAi widget.
- **Coverage = observation, never gate** (ADR-038).

## 7. Acceptance Criteria

See [README.md → Graduation Criteria](./README.md#graduation-criteria) (10 measurable criteria mapped to the spec's Success Criteria).

## 8. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| quickXorHash instability post-upload | High | Spike-gated (P0-a) before Tier-1 build |
| Shared-lib contention (r5 / dataset-grid-r2 / compose / messaging) | High | `parallel-safe:false` + `/conflict-check` every shared PR + coordination contract |
| Footer DLP/anti-spam | Med | Transparent footer + per-firm opt-out; deployment-checklist |
| Citation drift vs Compose | Med | Reuse `CitationResolver`; parity seam tests |
| BFF publish-size creep | Med | Per-task publish-size report; ≤60 MB ceiling |

## 9. Next Steps

1. Operator reviews `tasks/TASK-INDEX.md` (execution was intentionally deferred).
2. Run `task-execute` on Phase 0 (P0 spikes first — they gate Pillar C).
3. `/conflict-check` before any shared-lib/shared-service PR.
