# Email Communication Solution R4 — Implementation Plan (WBS)

> **Source**: [`spec.md`](spec.md) (27 FRs, 8 NFRs) · [`design.md`](design.md) (rev 2)
> **Created**: 2026-07-14
> **Status**: Ready for task execution
> **Branch**: `work/email-communication-solution-r4` (worktree-isolated)
> **Portfolio**: [Project #642](https://github.com/spaarke-dev/spaarke/issues/642) · Epic [#431](https://github.com/spaarke-dev/spaarke/issues/431)

## Overview

R4 delivers a channel-extensible **Communication Intelligence layer** and absorbs R3's send-side client consolidation into one unified, server/client-parallelized project. The server work (C#: engine, enrichment, intelligence) and client work (TS: composer, wrappers, Code Page) are disjoint by file and language and execute in parallel.

## Architecture Context — Discovered Resources

### Applicable ADRs (full list in spec §Technical Constraints)
- **ADR-045** (NEW, authored in W0): Communication ADR — client canonical send + server Association Engine + enrichment + channel seams. *(Number confirmed by owner; the design's "ADR-033" reference was a stale R3 carryover — ADR-033 is already occupied.)*
- **ADR-024** (regarding family), **ADR-028** (Auth v2), **ADR-018** (kill-switch), **ADR-032** (Null-Object), **ADR-016/014** (AI budget/cache), **ADR-015** (privilege flag-only), **ADR-013** (AI facade), **ADR-037** (DeliverComposite), **ADR-003/008** (auth/filters), **ADR-006/026/021/022/012** (Code Page + Fluent v9), **ADR-029** (publish hygiene), **ADR-038** (testing).

### Applicable Skills (bound to task tags)
| Skill | Waves |
|---|---|
| `dataverse-create-schema` / `dataverse-deploy` | W0, W4 |
| `bff-deploy` | W0, W1, W3, W5, W7 |
| `fluent-v9-component` | W2, W4 |
| `code-page-deploy` | W4 |
| `ribbon-edit` | W4, W6 |
| `jps-action-create` / `jps-validate` | W3 (rung-5 action), W5 (Triage action) |
| `office-addins-deploy` | W7 |
| `ui-test` | W4 |
| `adr-check`, `code-review` | all FULL-rigor tasks (Step 9.5) |
| `doc-drift-audit` | W8 |

### Key existing code (reuse — Component Justification §11)
- `Services/Communication/`: `IncomingCommunicationProcessor`, `IncomingAssociationResolver`, `CommunicationService`, `RegardingLookupMap`.
- `Services/Ai/`: `AppOnlyAnalysisService`, `OutputRouter`/`DispositionRoutability.cs`, `EventRulesService`, `CreateNotification`/`CreateTask`/`DeliverComposite` executors.
- Client: `PolymorphicResolverService`, `RegardingResolver` PCF, `TODO_REGARDING_CATALOG`, `FieldMappingService`.
- Send-side reference: [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) + [`reference/r3-send-side-plan.md`](reference/r3-send-side-plan.md) (the absorbed R3 send-side detail; the R3 project body was deleted in W8 — see [`../x-email-communication-solution-r3/SUPERSEDED.md`](../x-email-communication-solution-r3/SUPERSEDED.md)).

### ⚠️ Hot-path coordination (BINDING)
Per [`projects/INDEX.md`](../INDEX.md), **`spaarke-ai-architecture-redesign-r2` is the sole owner of `Services/Ai/` internals**, publishing seams under `Services/Ai/PublicContracts/`. **W5 (Responsive Intelligence) edits `Services/Ai/` internals and is GATED**: task 050 is a coordination gate that MUST clear (via `/conflict-check` + confirmed OutputRouter disposition ownership + consuming PublicContracts seams, no internal fork) before tasks 051–054 begin. W1/W3 touch `Services/Communication/**` + new files (low overlap). All BFF-touching tasks run `/conflict-check` before opening a PR.

## Empirical Findings (carried from R3 pre-flight, verified 2026-06-05)
These adjust the spec baseline for the send-side waves; do not re-investigate:
1. `EmailComposer/` component + Code Page do NOT exist — build from scratch (W2/W4).
2. `communicationApi.ts` has `sendCommunication()` but is **missing** `SendCommunicationError` + `attachmentDriveItemIds` (W0/W2 additive).
3. `SendCommunicationRequest.cs` has only `AttachmentDocumentIds` (W0 non-breaking alias).
4. `CommunicationService.cs` does not capture `Internet-Message-Id` (W0).
5. Only `CreateMatter/SendEmailStep.tsx` is a true LegalWorkspace fork (Project/Event/Todo/WorkAssignment are not) → W6 smaller than a naive read.
6. `sprk_communication_send.js` is ~1,150 LOC × 2 copies (~2.3K total) → W6 retirement larger than spec stated.
7. `WorkAssignmentWizardDialog.tsx:31` cross-package import to resolve (W6).
8. Code Page exemplar: `src/client/code-pages/DocumentRelationshipViewer/` (W4).

## Phase Breakdown (Waves)

### W0 — Shared foundation (schema · ADR · server send-changes · retire OOB-email) — tasks 001–009
[X/S; main-session for `.claude/`] ≈3 d. **Blocks all downstream waves.**
- **001** [X] `sprk_communication` schema pass — R3 reply-thread + R4 association columns (FR-01)
- **002** [X] `Suggested`/`Ambiguous` option-set values — verify integers via Dataverse MCP (FR-02)
- **003** [X/S] Author `sprk_servicerequest` schema doc + wire as association target (FR-03)
- **004** [S/X] Add `sprk_event` to catalog/priority; correct org target → `sprk_organization` (FR-04)
- **005** [D, main-session] Author **ADR-045** Communication ADR (concise + full) (FR-05) — `parallel-safe: false`
- **006** [S] BFF send-path — `AttachmentDriveItemIds` non-breaking rename + `Internet-Message-Id` capture (FR-06)
- **007** [S] **Retire OOB-`email` subsystem** + publish-size delta report (FR-07, NFR-02)

### W1 — Server: enrichment + deterministic engine — tasks 010–017
[S] ≈4 d. *Parallel with W2.* Depends on W0.
- **010** `ICommunicationEnrichmentService` (both directions; outbound RAG) (FR-08)
- **011** Refactor `IncomingAssociationResolver` → Association Engine over normalized envelope; preserve rungs 0–2 under test (FR-09)
- **012** Rungs 0–1 (explicit-ref + thread continuity) across 8 targets (FR-10)
- **013** Rung 2 (participant correlation; org-by-domain) (FR-10)
- **014** Rung 3 structural detectors (`Detectors/`) (FR-10)
- **015** Confidence→status + **auto-file ≥0.85 deterministic** (ADR-018 kill-switch) (FR-11)
- **016** Channel seams (`ICommunicationChannelSender`/`ICommunicationArchiver`, email impl) (NFR-04)
- **017** Central auth + direction-symmetry + per-rung tests (NFR-03/06/08)

### W2 — Client: composer engine + wrappers — tasks 020–023
[C] ≈4 d. *Parallel with W1.* Depends on W0. (Absorbed R3 — reuse R3 task POMLs.)
- **020** `<EmailComposer />` engine + sub-components (FR-12)
- **021** `SendEmailStep`/`SendEmailDialog`/`SendEmailPage` wrappers (FR-12)
- **022** `sendCommunication()` refinements (`SendCommunicationError`, `attachmentDriveItemIds`) (FR-13)
- **023** Composer + wrapper unit tests (NFR-08)

### W3 — Server: semantic + AI rungs (4–5) — tasks 030–032
[S] ≈3 d. Depends on W1.
- **030** Rung 4 — `RecordSearchService` semantic match (FR-14)
- **031** Rung 5 — new JPS extract+classify Action → `AppOnlyAnalysisService` (FR-15)
- **032** Per-rung telemetry (DEC-8) + rung 4/5 tests (NFR-05/08)

### W4 — Channel-aware Communication Code Page — tasks 040–043
[C/X] ≈3 d. Depends on W2 (composer) + W1 (suggestions).
- **040** Channel-aware view/record Code Page generalized by `sprk_communicationtype`; `@spaarke/auth` (FR-16)
- **041** Mount `<EmailComposer />` for email; Form Component Control swap (admin fallback) (FR-16)
- **042** Embed `RegardingResolver` PCF + suggestion/confidence review; "Communications Awaiting Association" view; Field Mapping on accept (FR-17)
- **043** Code Page deploy + UI tests (NFR-07)

### W5 — Responsive Intelligence — tasks 050–054
[S] ≈3 d. Depends on W3. **GATED on r2-core coordination (task 050).**
- **050** ⚠️ **[GATE]** Coordinate with `spaarke-ai-architecture-redesign-r2` — confirm OutputRouter `record`/`notification` ownership + consume `PublicContracts` seams (`/conflict-check`). **Blocks 051–054.** `parallel-safe: false`
- **051** Complete OutputRouter `record` + `notification` dispositions (FR-18)
- **052** Wire enrichment → `EventRulesService("communication_assessed")` → CreateEvent/Task/Notification (FR-19)
- **053** "Communication Triage" JPS Action → `DeliverComposite` (FR-20)
- **054** Rule config via Binding + `sprk_matchconditions`; privilege-flag only; tests (FR-19)

### W6 — Client caller migration + retirements — tasks 060–062
[C] ≈2.5 d. Depends on W2 + W4. (Absorbed R3.)
- **060** Migrate SummarizeFilesDialog, FilePreviewDialog, DocumentEmailWizard (FR-21)
- **061** Migrate 5 create-record wizards + CreateMatter fork; resolve cross-package import (FR-21)
- **062** Retire `sprk_communication_send.js` after ribbon audit (FR-22)

### W7 — Microsoft hardening + auth + index — tasks 070–076
[S/C/X] ≈3–4 d. *Parallel track, deadline-driven.* Depends on W0.
- **070** Graph compliance audit (`Mail-Advanced.*` by 2026-12-31; EWS by 2026-10-01) (FR-23, NFR-01)
- **071** Subscription lifecycle-notification + `delta` reconciliation backstop (FR-24)
- **072** Outlook add-in NAA/`@spaarke/auth` migration + unified JSON manifest + org-URL fix (FR-25)
- **073** Apply stubbed BFF Office auth filters (`OfficeEndpoints`) (FR-25)
- **074** Add-in save pane consumes Association Engine suggestions (FR-25)
- **075** Index-config tokenization + read/write consolidation via `SearchIndexNameResolver` (FR-26)
- **076** Refresh `knowledge/work-iq` snapshot (DEC-7)

### W8 — Documentation — tasks 080–082
[D] ≈1.5 d. Depends on W1–W7 substantially complete.
- **080** Author `communication-intelligence-architecture.md` (FR-27)
- **081** Update `sprk_communication.md`, `email-processing-architecture.md`, `communication-service-architecture.md`; mark OOB-`email`/fragmented-send docs RETIRED (FR-27)
- **082** Update `EMAIL-TRIAGE-MODULE-DESIGN.md` per DEC-10 (FR-27)

### Wrap-up — task 090
- **090** Project wrap-up: README → Complete, lessons-learned, **`/test-diet` gate**, archive. `parallel-safe: false`

## Critical Path
`W0 (001→007) → W1 (010→015) → W3 (030→031) → W5 (050 gate → 051→054) → W8 → 090`

The W1‖W2 and W7‖(W1–W6) parallelism compresses wall-clock. W0→W1 lands the majority of receive-side value (deterministic, direction-symmetric association) before any AI cost; W2 lands send-side consolidation concurrently.

## Estimated Effort
~26–30 focused days serial; materially less wall-clock with server‖client + hardening parallelism.

## Timeline
Target Date: *to be set (Step 4.5 prompt / GitHub UI).* Two hard external deadlines constrain W7: **EWS 2026-10-01**, **`Mail-Advanced.*` 2026-12-31**.
