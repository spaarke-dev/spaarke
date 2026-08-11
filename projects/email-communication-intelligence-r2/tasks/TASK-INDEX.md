# Task Index — Email Communication Intelligence R2

> **Generated**: 2026-08-05 via `/project-pipeline` · **40 tasks** across 7 phases (0–6)
> **Revised 2026-08-05**: spikes 001/002 retired (gate-after-write + Tier-2-deferred decisions); see Resolved decisions below.
> **Status legend**: 🔲 not-started · 🔄 in-progress · ✅ completed · ⛔ blocked · ⏸️ deferred
> **Execution**: via `task-execute` per task (Sonnet 5 @ high default; `<model-tier>`/`<effort>` per POML).
> **Execution status (2026-08-06)**: Pillars C + D largely complete; Pillar B code done (041/042/043 ✅; 040 blocked on gated 004). Pillar-C dedup schema in place (027/028 operator-created + verified live; 029 resolved by reuse of `sprk_relatedcommunication`). Remaining: operator-gated backend (004/010 + downstream), Pillar E (contended shared-lib), deploys (paused).

## Registry

| # | Task | Phase | Status | deps | model·effort | parallel-safe |
|---|---|---|---|---|---|---|
| 003 | R1 close-out — reconcile 013 + pin golden misfile emails | 0 | ✅ | — | sonnet·high | ✅ true |
| 004 | Entra NAA app-registration verify/provision | 0 | ✅ | — | sonnet·med | ✅ true |
| 010 | HMAC footer/token signing helper (Key Vault) | 1·A | ✅ | — | **opus·xhigh** | ❌ false |
| 011 | Footer config (operator app setting, per-tenant) | 1·A | ✅ | — | sonnet·high | ❌ false |
| 012 | Inject signed footer on outbound send path | 1·A | ✅ | 010,011 | sonnet·high | ❌ false |
| 013 | `TrackingTokenRung` (reuse RungKind.ExplicitReference) | 1·A | ✅ | 010,011 | **opus·high** | ✅ true (A-rungs) |
| 014 | `RecipientAliasRung` + Bcc plumbing | 1·A | ✅ | 011 | sonnet·high | ✅ true (A-rungs) |
| 015 | Formalize external-reply self-association + test | 1·A | ✅ | — | sonnet·high | ❌ false |
| 016 | `AffinityRung` + `sprk_affinity` store | 1·A | ✅ | 011 | **opus·high** | ✅ true (A-rungs) |
| 017 | Pillar A BFF deploy (size/CVE) | 1·A | 🔲 | 010–016 | sonnet·med | ❌ false |
| 020 | Alternate key on `sprk_communication.sprk_internetmessageid` | 2·C | ✅ | — | sonnet·high | ✅ true (schema-c) |
| 021 | Canonical message-id dedup — race-proof create + SB idempotency | 2·C | ✅ | 020 | **opus·xhigh** | ❌ false |
| 022 | Context-merge on duplicate | 2·C | ✅ | 021 | sonnet·high | ❌ false |
| 023 | Indexed `sprk_document.sprk_canonicalhash` column (forward-only) | 2·C | ✅ | — | sonnet·high | ✅ true (schema-c) |
| 024 | SPE content dedup Tier-1 — **gate-after-write** (quickXorHash detector) | 2·C | ✅ | 023 | **opus·high** | ❌ false |
| 025 | Cross-path reconciliation (comm ↔ document via message-id) | 2·C | ✅ | 021 | sonnet·high | ❌ false |
| 027 | `sprk_document.sprk_canonicaldocument` self-lookup (FR-C3 graduate-on-divergence) — schema (operator-created, verified live) | 2·C | ✅ | — | sonnet·high | ✅ true (schema-c) |
| 028 | `sprk_communication.sprk_deliveredmailboxes` + `sprk_savedbyusers` memo (FR-C2 context-merge) — schema (operator-created, verified live) | 2·C | ✅ | — | sonnet·high | ✅ true (schema-c) |
| 029 | FR-C4 cross-path reconciliation — **RESOLVED BY REUSE** of existing `sprk_document.sprk_relatedcommunication` (no new column; code rewired) | 2·C | ✅ | — | sonnet·high | ✅ true (schema-c) |
| 026 | Pillar C BFF deploy (size/CVE) | 2·C | 🔲 | 021,022,023,024,025,027,028,029 | sonnet·med | ❌ false |
| 030 | Fix FR-06 RAG grounding — ParentEntity tagging (both sites) | 3·D | ✅ | — | sonnet·high | ✅ true (D-indep) |
| 031 | Batched identifier query (≈175→≤7) | 3·D | ✅ | — | sonnet·high | ✅ true (D-indep) |
| 032 | Golden regression suite (+ absorbs A3 test) | 3·D | ✅ | 015 | sonnet·high | ✅ true (D-indep) |
| 033 | Job B allow-list seed (`sprk_emailupdatefield`) | 3·D | 🔲 | — | sonnet·med | ✅ true (D-indep) |
| 034 | Job C apply endpoint + create-task queue-feed discriminator | 3·D | ✅ | — | **opus·high** | ❌ false |
| 035 | Pillar D BFF deploy (size/CVE) | 3·D | 🔲 | 030,031,034 | sonnet·med | ❌ false |
| 040 | Add-in realignment (FR-B0 a–d) — **code ✅** (401-retry routing, Word-manifest parity, cleanup; new `authenticatedJsonFetch` §11-justified); runtime NAA sign-in + dark-mode live-render **operator-gated** (needs live Office host; deploys paused) | 4·B | ✅-code | 004 | sonnet·high | ✅ true (PB-a) |
| 041 | Real Spaarke intake folder — re-scoped (mech-1 = config, documented; mech-2 → 043) | 4·B | ✅ | — | sonnet·high | ✅ true (PB-a) |
| 042 | Drag-to-matter + engine pre-select + ribbon quick-save | 4·B | ✅ | 041 | **opus·high (Option B)** | ✅ true (PB-b) |
| 043 | Unify user-upload with capture (engine + dedup) — absorbs FR-B1 add-in drag-target (from 041) | 4·B | ✅ | 021,024 | sonnet·high | ❌ false |
| 044 | Deploy Pillar B add-in (Azure SWA) | 4·B | 🔲 | 040,042 | sonnet·med | ❌ false |
| 045 | Pillar B BFF deploy (size/CVE) | 4·B | 🔲 | 041,043 | sonnet·med | ❌ false |
| 050 | Reconciliation grid — **`ReconciliationGrid` + Needs-review config (NO DataGrid-framework edit — all seams already shipped; zero dataset-grid-r2 contention)** | 5·E | ✅ | — | sonnet·high | ❌ false (shared lib) |
| 051 | Triage as grid columns — **`triageColumnRenderers.tsx` (category/priority/summary/RI-conf/review-outcome) merged into `ReconciliationGrid` via columnRenderers seam; config default-sort = `sprk_triagepriority` asc; null→placeholder** | 5·E | ✅ | 050 | sonnet·high | ❌ false |
| 052 | Related-to card-picker — **`RelatedToCell` (requires-review/chip states) opens reused `EmailConnectionsReview` in `SprkModal`; single write path (`applyRegardingSelection`, hostRecordId===commId); `ReconciliationGrid.relatedTo` binding; NFR-10 `onConfirmed`. §6.5 reuse-by-extension: wired r5's already-declared `onCreateNewRecord` prop into a gated create-new tile (backward-compat) — 058 coordination note, notes/052** | 5·E | ✅ | 050 | sonnet·high | ❌ false |
| 053 | Browse shell + one normalized reader (attachment folding) — **`ReconciliationBrowseShell` (SprkModal+nav two-pane; NOT BrowseModal preset — owner-approved ADR-050 Path C, see notes/053) + `EmailBodyView` attachment-text folding; reuses EmailReadingPaneShell(hideList)/AttachmentList** | 5·E | ✅ | 050 | **opus·xhigh** | ❌ false |
| 054 | Citation navigation — **quoted-text anchor `logic/citations/readerReferenceMap.ts` + `HighlightedText` + `EmailBodyView.activeCitation` (jump/highlight; source-not-locatable). §6.5 Path A: composeCitationResolver is legal-number-only + its quoted-text twin is editor-bound → built the email-domain quoted-text analog, NOT a fork; see notes/054** | 5·E | ✅ | 053 | **opus·xhigh** | ❌ false |
| 055a | **Job B apply-OVERRIDE endpoint (FR-E4 backend; split from 055 — Option A)** — `ApplyProposalRequest{OverrideValue?}` + `ApplyAsync` overload; override applies the human value through the SAME allow-list/citation/coercion/impersonation guards, records `Overriden` audit. §10 verified (~47 MB, no CVE). notes/055a | 2·C/5·E | ✅ | 031 | **opus·high** (BFF write) | ❌ false |
| 055b | **Job B DISMISS endpoint (FR-E4 "Reject" backend; split from 055 — Option A, owner-approved 2026-08-07)** — `POST /proposals/{reviewLogId}/dismiss` + `ICommunicationProposalApplyService.DismissAsync`; caller-resolution (403) + open-pending guard (409) + ONE `Dismissed` (100000004) audit row, NO record write, NO allow-list/citation re-gate (rejection safe regardless of drift). §10 verified (~47.06 MB, no CVE, no ArchTest delta). notes/055b | 5·E | ✅ | 031 | **opus·high** (BFF write) | ❌ false |
| 055 | Field-update reconcile tab (Job B, editable, apply-under-audit) — **frontend; consumes 055a override + 055b dismiss endpoints**. `ReconcileTabs/FieldUpdateReconcileTab` (inline cards, browse-pane mount) + `FieldUpdateReconcileModal` (FormModal, email-form mount); NFR-10 gate + re-scope; Accept→apply{overrideValue}, Reject→dismiss, Hold→no-write; citation→054. 11 UI tests (incl. 053+054+055 browse-mount seam); shared-lib build green. notes/055 | 5·E | ✅ | 052,053,055a,055b | sonnet·high | ❌ false |
| 056b | **Job C AD-HOC create-task endpoint (FR-E5 "+ New task" backend; split from 056 — Option A, owner-approved 2026-08-07)** — `POST /communications/{communicationId}/create-task` + `CreateAdHocAsync`; reviewer-authored task (no proposal/citation) via the SAME `CreateTaskAsync` + impersonated FR-E5 PATCH + ONE `Applied` audit row; create-and-complete inline; caller-403, subject/regarding-422, ADR-015 loud-patch-fail-422. §10 verified (47.06 MB, no CVE, no ArchTest delta). notes/056b | 5·E | ✅ | 034 | **opus·high** (BFF write) | ❌ false |
| 056 | Task/deadline reconcile tab (Job C, create-and-complete + ad-hoc) — **frontend; consumes 034 apply + 056b ad-hoc + 055b dismiss**. `ReconcileTabs/TaskReconcileTab` (inline editable 8-field task form; browse-pane) + `TaskReconcileModal` (FormModal); Accept routing (unchanged→034 apply, edited-identity→056b ad-hoc+055b dismiss, +New task→056b); create-and-complete inline; ADR-015 no-auto-finalize; NFR-10 gate+re-scope; citation→054. 13 UI tests; shared-lib build green. notes/056 | 5·E | ✅ | 034,052,053,056b | sonnet·high | ❌ false |
| 057 | Reconciliation routing (category→team + per-team views) — **backend**: `CategoryRoutingOptions` (ADR-018 IOptionsMonitor, per-tenant) + `CategoryRoutingGate` + triage-time `ownerid`→team assignment on the existing additive triage `UpdateAsync` (ADR-024, NFR-04 non-fatal); **frontend**: `per-team.gridconfiguration.json` (`behavior.membershipFilter` roles=owner/identityTypes=team; grid already forwards `membershipResolver`). No new entity (ADR-045). Tests: 2 triage seam (mapped→ownerid, unmapped→unassigned) + 6 gate unit (disabled/mapped/unmapped/blank/per-tenant) + 3 frontend routing; §10 47.07 MB/no CVE/no ArchTest delta. Communication seam 150/150; package 222/222. notes/057 | 5·E | ✅ | 050 | sonnet·high | ❌ false |
| 058 | r5 coordination contract (record R2 ownership D/E/F + Exceptions Queue) — §10 of `email-communication-solution-r5/notes/email-intelligence-r1-coordination.md`; ownership statement (2026-08-05) + **as-built consumption contract** (2026-08-10): 055a/055b/056b endpoints, `FieldUpdate*`/`Task*ReconcileTab`+`Modal` exports r5 mounts, the 056 edited-proposal→ad-hoc+dismiss Accept-routing r5 must NOT re-implement, 052 `onCreateNewRecord` tile, 057 `CategoryRouting` config. /conflict-check-before-every-shared-lib-PR (incl. dataset-grid-framework-r2). notes/058 | 5·E | ✅ | — | sonnet·med | ❌ false |
| 060 | Prototype refinement (A4 larger modal · A5 thin scrollbar · A6 50/50 drag-resize) + long-thread/4-attachment scroll demo — **spaarke-prototype** `email-communication-intelligence-r2-uat/src/App.tsx`; owner UX review of the reconciliation layout. notes: pillar-e-reconciliation-ux-requirements.md §A | 5·E-mount | ✅ | — | sonnet·med | ✅ true (prototype repo) |
| 061 | **ReconciliationWorkspace composition** — `ReconciliationGrid` → `ReconciliationBrowseShell` (renderTabs = Related-to/Fields/Tasks) + UX **A4** (SprkModal lg→xl) / **A5** (thin scroll) / **A6** (PanelSplitter 50/50 drag-resize via local `useSplitRatio`); NFR-10 gate + NFR-11 citation. Compose+enhance — no fork. Grid `onRecordsLoaded` pass-through added for the queue. tsc 0-err; jest 229/229; Step 9.5 + conflict-check clean. notes/061 | 5·E-mount | ✅ | 050–057 | sonnet·high | ❌ false (shared lib) |
| 062 | **Dual host** — new Vite code page `src/solutions/CommunicationReconciliation` → `sprk_communicationreconciliation` (EmailPage-pattern initAuth + XrmDataverseClient + authenticatedFetch + `resolveReview`/`resolveRegarding` via shipped `derivePrimaryReview`) **and** additive SpaarkeAi widget `communications-reconciliation` (mirrors `EmailWorkspaceWidget`, chassis untouched); both mount the SAME `ReconciliationWorkspace`. `NEEDS_REVIEW_CONFIG_ID` = placeholder for 059. Both build green (code-page bundle grep-verified; ai-widgets new files 0 tsc-err). /conflict-check soft-pass; Step 9.5 clean. Known refinement: in-shell confirm remounts (059/UAT). notes/062 | 5·E-mount | ✅ | 061 | sonnet·high | ❌ false (SpaarkeAi hot-path) |
| 063 | **UAT round-2 label reconciliation** — E1a Related-to tile "Create new record"→**"New record"** (keeps DocumentAdd20Regular add icon; "+ New task"-consistent); E2a Fields proposal **"Accept"** (verified already shipped, unchanged); E3a Tasks proposal "Accept"→**"Create"** (owner's explicit ask; Fields stays "Accept"; testid/handler `acceptProposal` kept stable by design). No test-file change needed (testid-based). tsc 0-err (after ui-components dist rebuild — build-order), jest 24/24; Step 9.5 clean. notes/063 | 5·E-mount | ✅ | 052,055,056 | sonnet·high | ❌ false (shared lib) |
| 064 | **E1b/E1c Quick Start "+ New record" + .eml pre-load** — E1b: injected `onLaunchCreateRecord` → host mounts `QuickStartModal` (additive `onRecordCreated` awaiting `launchSurface` outcome) → created ref re-enters `applyRegardingSelection` as confirmed regarding; modal-on-modal. E1c (**BFF resolver, decided**): new BFF endpoint materializes archived `.eml` (sprk_document) as a **chat-session document → `{sessionId,fileId}`** (wizard fetch is session-scoped) → host passes as `QuickStartModal.getFileContext` → wizard AI-pre-populates. §10 governance + **/conflict-check BFF+SpaarkeAi**. Explicit plan **§9**. Build LAST. | 5·E-mount | 🔲 | 052,062 | **opus·high** | ❌ false (SpaarkeAi+BFF+shared hot-path) |
| 065 | **E2b/E2c Fields typed controls + Update-other-fields** — E2b: self-resolve `getXrmForPicker().Utility.getEntityMetadata` → DateTime→DatePicker / Picklist→Dropdown(options) / Lookup→lookupObjects(Targets); E2c: full-width "+ Update other fields" → `navigateToEntityRecordSurfaceAsync` (confirmed record form), modal-on-modal. OOB self-resolve, **NO BFF, no new host prop**. Explicit plan **§9**. Build SECOND. | 5·E-mount | 🔲 | 055,063 | **opus·high** | ❌ false (shared lib) |
| 066 | **E3b New-task Assigned-to OOB advanced-lookup** — `TaskReconcileTab` Assigned-to text Input → `getXrmForPicker().Utility.lookupObjects({entityTypes:['systemuser','team']})` (exact reuse of EmailConnectionsReview:157-176; guarded no-op non-MDA keeps text fallback). Shared lib only, no BFF. Explicit plan **§9**. Build FIRST. | 5·E-mount | 🔲 | 056,063 | sonnet·high | ❌ false (shared lib) |
| 059 | Deploy Pillar E — code page + SpaarkeAi widget (seed needs-review + per-team `sprk_gridconfiguration`, set `NEEDS_REVIEW_CONFIG_ID`, code-page-deploy + rebuild SpaarkeAi + Deploy-AllDataGridConsumers) | 5·E | 🔲 | 061,062 | sonnet·med | ❌ false |
| 090 | Project wrap-up (test-diet, lessons, doc-drift, size) | 6 | 🔲 | all | sonnet·high | ❌ false |

## Parallel Execution Groups (waves)

| Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0 — prereqs** | 003, 004 | — | Independent; parallel. *(Spikes 001/002 retired 2026-08-05 — gate-after-write + Tier-2-deferred.)* |
| **W1a — A foundation** | 010 → 011 → 012 | — | `parallel-safe:false` (shared `CommunicationModule`/`Configuration`/send path) — **sequential**. 015 (test-only) any time. |
| **W1b — A-rungs** | 013, 014, 016 | 010,011 | Parallel *within this project* (distinct rung files) — but `/conflict-check` on shared `CommunicationModule.cs`/`RungKind.cs`/`AssociationStatusMapper.cs`. |
| **W2-schema — C schema** | 020, 023 | — | Parallel (disjoint schema surfaces). |
| **W2-code — C dedup** | 021 → 022, 025 · 024 | 021←020; 024←023 | `parallel-safe:false` (contended `Services/Communication` / `SpeFileStore`) — **sequential**. |
| **W3 — D independent** | 030, 031, 033 (+032←015) | — | Parallel. **Goal-eligible candidate** (machine-verifiable, low-ambiguity, non-security) — operator may run under `/goal`; Step 9.5 authority unchanged. |
| **W3-code — D5** | 034 | — | Sequential (contract). Backs 056. |
| **W4 — B** | 040, 041 (PB-a) → 042 (PB-b) · 043 | 040←004; 042←041; 043←021,024 | 040/041 parallel; 043 gated on C1/C3. |
| **W5 — E (sequential)** | 050 → {051,052,053,057} → {054,055,056} | 050 first; 056←034 | **All `parallel-safe:false`** (shared `Spaarke.Communication.Components` + `DataGrid`) — **strictly sequential, main-session**. |
| **W5-mount — E mount build** | 060 ✅ → 061 ✅ → 062 ✅ → **063** → {064,065,066} | 061←050-057; 062←061; 063←052/055/056; 064←062; 065/066←063 | **Sequential, main-session** (shared lib + SpaarkeAi hot-path). 060 done (prototype). 061/062 done. **063 = UAT round-2 labels, shippable now (no gate).** **064/065/066 = modal-on-modal behaviors (Quick Start+.eml / typed-controls+Update-other-fields / Assigned-to lookup) — GATED on prototype sign-off; reuse-only per plan §7.5.** goal-eligible: NO (shared/hot-path). |
| **Deploys** | 017, 026, 035, 044, 045, 059 | per-phase | After their phase's code lands; each reports publish-size ≤60 MB. **059←061,062** (mount build). |
| **W6 — wrap-up** | 090 | all | `/test-diet`, lessons, doc-drift, coordination + INDEX, size report. |

## Critical Path

`020 → 021 → 043 → 045` and `023 → 024 → 043` (dedup foundation → unify upload → deploy) **and** the Pillar E spine `050 → 053 → 054 / 055 / 056 → 059` (with `056` gated on `034`). `090` is terminal (deps all). Longest chains are Pillar C→B backend and the Pillar E reader/citation glue. *(No spike gate — 023/024 start on their own deps.)*

## 🚨 Hot-path coordination (BINDING — run `/conflict-check` before every shared PR)

`parallel-safe:false` on all shared-Communication / DataGrid / Compose writers. Contending active worktrees:
- **email-communication-solution-r5** — owns `Spaarke.Communication.Components` (Pillar E); update its coordination contract (task 058).
- **spaarke-dataset-grid-framework-r2** — shared `DataGrid` (task 050 enhancement).
- **messaging-communication-app r1/r2/r3**, **spaarke-notification-spine-r1**, **email-communication-solution-r4** — shared `Services/Communication` persist/emit path (Pillars A/C/B/D).
- **spaarkeai-compose-r5 / -fidelity-r4.5** — `CitationResolver` reused by task 054 (do NOT fork — NFR-11).
- **spaarke-ai-architecture-redesign-r2** — `Services/Ai` owner; reach AI only via `PublicContracts/` (task 034 — NFR-05/ADR-013).

## ✅ Resolved decisions (owner, 2026-08-05)

1. **SPE content dedup → gate-after-write** (tasks 023/024): read `quickXorHash` from the driveItem metadata **after** upload, reconcile + notify (never silently suppress a document); accept a brief transient blob. **Spikes 001/002 retired.**
2. **SPE Tier-2 (near-dup) → deferred out of R2** — exact-hash Tier-1 only (task 024); near-dup is a follow-up.
3. **FR-E5 task fields vs `IActionSeam.CreateTaskAsync` → Path B "add fields"** (task 034): create via the seam, then PATCH status/completed-date/**base-date/final-due-date** via impersonated `UpdateRecordAsync` under the same audit row; **add `base-date` + `final-due-date` as new task-entity fields** (schema step in 034). Facade unchanged (ADR-013); full FR-E5 field set structured in R2. 056 consumes it.
4. **Backfill → forward-only** (tasks 023/030): no historical reprocessing.
5. **Browse shell → `BrowseModal` preset** (`@spaarke/ui-components`, `SprkModal/presets`; ADR-050 / MODAL-DESIGN-SYSTEM / MODAL-DECISION-CRITERIA) — task 053.

## High-risk items
- 021 (race-proof structural dedup), 024 (SPE detector), 010 (HMAC signing), 034 (Job C audit + PATCH), 053/054 (citation-map glue) — the `opus`/`xhigh` tasks; highest blast radius.
- Gate-after-write (024): the detector notifies + links on a hit, **never silently suppresses** a document (data-loss guard).

---

*Execute via `task-execute`. Update this table's status column (🔲→✅) as the last step of each task.*
