# Deferrals & Discovered Issues — email-communication-intelligence-r2

> Source of truth (pairs with GitHub Issues for visibility). Every entry names a concrete behavior/contract
> that fails without the work (§11). File the GitHub Issue via `/defer` (or at push — `push-to-github`
> Step 1.6 blocks push on unfiled entries).

---

## DEFER-030-01 — RAG grounding for non-representable regarding types — ✅ RESOLVED (2026-08-05)

| Field | Value |
|---|---|
| **Filed / Resolved** | 2026-08-05 (task 030) → resolved same day (operator direction) |
| **GitHub Issue** | Not filed — resolved before push (no residual product-need work). |
| **Original concern** | A `sprk_communication` whose **primary** regarding is a **service request** (or the non-core types work assignment / event / budget / report card / analysis / organization) indexed with `ParentEntity = null` → a service-request-scoped RAG query returned zero of that SR's correspondence. |
| **Resolution** | Investigated the downstream chain and found the grounding path is a **generic string passthrough**: the RAG query filter is `parentEntityType eq '…'` (`RagService`), `SearchIndexNameResolver` routes the index by the source-entity lookup / tenant default (NOT by `parentEntityType`), and the only type-switch (`AiAnalysisNodeExecutor.MapToRecordEntityType`, a separate records-index feature) already degrades gracefully to null for unknown types. So extending it is safe. **Added `servicerequest`** — the one *core* auto-file type (matter / project / service request) that was missing — to `ParentEntityContext.EntityTypes` + `RegardingParentEntityMapper.RepresentableTypeMap` + a seam test. Build clean; 9 mapper + 141 downstream (normalizer / semantic-search / chat-host) tests green; conflict-check on `ParentEntityContext.cs` clean. |
| **Residual (intentional, NOT deferred)** | The remaining non-core types (work assignment / event / budget / report card / analysis / organization) are **intentionally not RAG-grounding parents** — RAG scoping by those is not a product need. Documented in `RegardingParentEntityMapper`'s XML docs with the one-line recipe to add one later (type const + map row; the filter is generic, no downstream change). No tracking needed. |
| **Cross-worktree note** | Touched the AI-owned `Models/Ai/ParentEntityContext.cs` (spaarke-ai-architecture-redesign-r2 surface). Additive (one const + one `All` entry). **Re-run `/conflict-check` before the PR.** |

---

## COORD-054-01 — Shared non-editor quoted-text anchor (Compose ↔ Communication) — coordination, NOT blocking

| Field | Value |
|---|---|
| **Filed** | 2026-08-07 (task 054) |
| **GitHub Issue** | Raise at PR time with spaarkeai-compose / -fidelity owners (coordination, not a blocker). |
| **Concern (§11 concrete)** | Two shared libs now each own a citation domain: **Compose** = legal-section-number resolution over a numbered `.docx` (`composeCitationResolver.ts`); **Communication** = quoted-text-span anchoring over free-form email/attachment prose (`logic/citations/readerReferenceMap.ts`, task 054). They are genuinely different problems (legal-number lookup vs quoted-text search) and neither forks the other. Compose's quoted-text primitive (`highlightCitedSpan`/`findCommentAnchorRange`) is ProseMirror-editor-bound, so it could not be reused for the non-editor reconciliation reader (this is why 054 escalated → owner-approved §6.5 Path A). |
| **Cost of doing nothing** | None today — both anchors are correct in their domain. The only latent cost: if a THIRD non-editor surface later needs quoted-text anchoring, it would reach for `readerReferenceMap` (fine) OR someone re-implements it (the thing to avoid). |
| **Convergence option (Path B, future)** | Extract a shared, non-editor-bound `resolveQuotedSpan(quotedText, normalizedText)` primitive both Compose (re-basing `highlightCitedSpan`) and Communication reuse. Cross-team change across two active worktrees — do only if a real third consumer appears. |
| **Owner action** | Note it in the 054 PR description; loop in spaarkeai-compose. No code change required in this project. |

---

## COORD-058-01 — r5 code-page must wire the Pillar E reconcile surfaces (feeds task 058) — coordination, NOT blocking

| Field | Value |
|---|---|
| **Filed** | 2026-08-07 (tasks 052 / 055a / 055b / 055) |
| **GitHub Issue** | Raise at task-058 PR time with email-communication-solution-r5 owners (the r5 BINDING coordination contract, FR-E6). |
| **Concern (§11 concrete)** | R2 shipped Pillar E building blocks in the r5-owned `Spaarke.Communication.Components` + the BFF Communication surface. r5's code page / SpaarkeAi widget must CONSUME them, or the reconcile UX has no host. Enumerated so 058 records each in the contract. |
| **Additive component extensions (backward-compatible; existing r5 callers unaffected)** | (1) **052** — `EmailConnectionsReview` gained an optional `onCreateNewRecord` gated tile (`EmailWorkspace` omits the prop → no change). (2) **055 / net-new** — `ReconcileTabs/FieldUpdateReconcileTab` + `FieldUpdateReconcileModal` exported from `@spaarke/communication-components`. r5 mounts `FieldUpdateReconcileTab` in the task-053 browse-shell `renderTabs` slot (lifting `onCitationClick` → the shell's `activeCitation`) AND `FieldUpdateReconcileModal` on the email record form; the host supplies `regarding` from task-052's `onConfirmed` NFR-10 handshake and re-supplies it on override. |
| **New BFF endpoints r5 calls** | **055a** `POST /api/communications/proposals/{reviewLogId}/apply` now accepts an optional `{ overrideValue }` body (edited-value Accept). **055b** `POST /api/communications/proposals/{reviewLogId}/dismiss` (Reject; no body). **056b** `POST /api/communications/{communicationId}/create-task` (ad-hoc "+ New task"; body = subject + regarding + FR-E5 fields). All re-gate server-side (caller 403 / allow-list / citation / open-pending 409 / subject+regarding 422). |
| **Tasks tab exports (056)** | `ReconcileTabs/TaskReconcileTab` + `TaskReconcileModal` exported from `@spaarke/communication-components`. r5 mounts the tab in the browse-shell `renderTabs` slot (Tasks) + the modal on the email form. **Accept routing r5 must NOT re-implement**: an UNCHANGED proposal → `POST …/create-task/apply` (034); a proposal whose name/description was EDITED → `POST …/{communicationId}/create-task` (056b ad-hoc) + best-effort `POST …/dismiss` (055b) on the original (the 034 apply body carries no subject/description, so editing them there would be silently dropped — the tab routes around that). "+ New task" → 056b. |
| **Cost of doing nothing** | The Fields reconcile tab, the browse-shell citation flow, and edited-Accept / Reject have no host surface — FR-E2..E4 ship as libraries with no user entry point. |
| **Owner action** | Record all of the above in the task-058 r5 coordination contract; cite in the 052/055 PR descriptions. `/conflict-check` before any PR touching `Spaarke.Communication.Components` (r5 PRIMARY owner). No further code change required in R2. |
