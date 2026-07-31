# CLAUDE.md — Spaarke Compose R5 (project context)

> Loads when working in this project. Extends root `CLAUDE.md`; does not override it.
> **Authored 2026-07-28** at project setup (design.md written, worktree created). Next step: `/design-to-spec spaarkeai-compose-r5`.

## What this project is

**Editing completeness on top of R4's Shadow Document Architecture.** R4 made the OOXML round-trip correct-by-construction and shipped **error-free with documented functional limits** (it *guarded* the constructs the closed op schema didn't cover). R4.5 made *reading* a legal doc high-fidelity + referenceable. **R5 implements the editing limits** — it removes each R4 guard by building the feature behind it, so the editor covers every construct a user can create, and authored documents get a clean cross-session lifecycle.

R5 is **additive**. It rips out nothing and **re-litigates none of R4's locked decisions (D1–D5) or invariants (I-1–I-7)**.

Source of truth (in order): [`spec.md`](spec.md) *(not yet written — run `/design-to-spec`)* → [`design.md`](design.md) *(done)* → [`README.md`](README.md) *(authoritative gap ledger)* → [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md).

## 🚨 MANDATORY: Task Execution Protocol

When working a task here, **invoke the `task-execute` skill** — do NOT read POML files and implement manually. See root `CLAUDE.md` §4. Trigger phrases: "continue"/"next task" → read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute`.

## Scope — 11 gaps (the R5 backlog)

**IN R5:** G1, G2, G3, G4, G5, G7, G8, G9, G10, G11, G12. Full sized ledger in [`README.md`](README.md).

- **G1** cross-session authored-vs-imported origin routing (durable marker) · **G2** clean (non-tracked) apply mode · **G3** `setBlockAttr` applier (heading/list/alignment as `w:pPrChange`) · **G4** table op (tracked; the L-sized long pole — schedule last) · **G5** hyperlinks (authored render + edit op) · **G7** Save-Version vs Save-New split-button · **G8** external-change refresh + remount banner · **G9** comment-pane scroll-sync · **G10** Document Profile re-run on Compose save · **G11** track-changes-off keeps imported redlines visible · **G12** accept/reject imported tracked changes (ET-2 reconciliation, revision-id-addressed ops).

**❌ NOT R5 — G6 is DONE by R4.5** (transient-mount projection unification / Compose mammoth removal). Do not re-scope it.

**REQ-3 split:** READ side (render headings/lists/numbering on load) = R4.5. R5 owns the EDIT side only.

## Binding rules for THIS project

### Inherited foundation — DO NOT re-litigate
- **Invariants I-1…I-7** (from R4): one authoritative OOXML model; server-authoritative bytes; `w14:paraId` addressing; edits are surgical ops (untouched subtrees byte-identical); **one byte-author per path**; client is view+controller; **no text-search in the write path (I-7)**.
- **Locked decisions D1…D5** (from R4): step-level operational deltas; `(paraId, runIndex, run-local-offset)` anchor; docx-only now; SPE = store + open-in-Office launch; unified `ComposeShadowPatchEngine` on the imported/tracked path.
- **Two-byte-author split is intentional (R4 036/037, C-revised).** New/blank docs → clean bytes via `ComposeDocumentRenderer`; imported docs → tracked via `ComposeShadowPatchEngine`. **R5 does NOT merge the two authors.** G1/G2 "authored doc stays clean" is served by the renderer path + a clean-apply engine branch, NOT by forcing origination through the op log.

### R5 decisions (owner to confirm at design-to-spec — see design.md §0)
- **R5-D1** origin is durable (persisted marker), not inferred from SPE-id presence.
- **R5-D2** clean-apply = an engine mode/branch OR re-author-from-content-model (Phase-0 spike decides) — NOT a second synthesizer.
- **R5-D3** new ops (G4 table, G12 accept/reject-revision) **extend** the closed `ComposeOperation` catalog — never fork (ADR-039).
- **R5-D4** G3 edit-side numbering **reuses R4.5's `NumberingComputationEngine`** — do NOT re-implement (R4.5 FR-14).
- **R5-D5** G10 profile re-run rides the Compose save hook (owner decision 2026-07-28; include unless complexity is high).

## Coordination (BINDING) — see design.md §10 + notes/COORDINATION-with-r4.5.md

### `spaarkeai-compose-fidelity-r4.5` — ✅ MERGED to master (2026-07-28), execution gate CLEARED
R4.5's WS-2/3/4 outputs are on master (`CitationResolver.cs`, numbering engine in `Services/Compose/`). R5 **rebases onto** them:
- **⚠️ docxBridge hazard:** `docxBridge.ts` exports BOTH `docxToTipTapHtml` (mammoth READ — R4.5 deleted) AND `buildContentModel`/`stampParaIds`/paraId helpers (WRITE — **G1/G2/G7 depend on these**). **NEVER delete `docxBridge.ts` or "remove the mammoth module"** — only the read fn is gone.
- **Reuse, don't fork:** G3 → `NumberingComputationEngine`; G10 → `CitationResolver` (`paraId→legal-number`); G7 → R4.5's transient-mount projection identity.
- **Two contended files** (rebase R5 work onto post-R4.5 versions): `ComposeService.cs` (G1 origin / G7 create-vs-replace / G10 profile trigger), `ComposeWorkspace.tsx` (G1 `isTransientCreate` region / G7 toolbar / G8 mount).
- **Deploy:** shared `sprk_spaarkeai` web resource + `spaarke-bff-dev` ("last deploy wins"). Build/deploy from master-with-R4.5.

### `ai-advanced-capabilities-analysis-hub-r1` — surface-sharing + behavioral downstream consumer (NFR-09)
Planned/not-started (blocked on owner schema); low code-conflict but **shares** `Spaarke.Compose.Components` (`ComposeEditor.tsx`, `ComposeAiToolbar.tsx`), `ConversationPane` compose-routing + e2e tests, and the SpaarkeAi three-pane. It is a **downstream consumer of Compose save/versioning/redline behavior**. **R5's G1/G2/G7/G10/G12 MUST NOT regress its reopen-restore or clean-retirement parity.** Add R5 to the mutual coordination clause; register R5 in `projects/INDEX.md` (pipeline does this) so `/conflict-check` surfaces the overlap.

## BFF Hygiene (root §10) — this project is BFF=Y

Every BFF-touching task MUST:
- State **Placement Justification** in the PR (cite `.claude/constraints/bff-extensions.md`). Engine/save orchestration stays in `Services/Compose/`.
- Keep `Services/Compose/` **pure** — no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest); no `Microsoft.Graph` above `SpeFileStore` (ADR-007). Engine is `byte[]`-in/`byte[]`-out.
- **Verify publish size** ≤60 MB compressed; report absolute + delta vs **~46.11 MB** post-R4 baseline. Zero new runtime package expected.
- Add/update **seam slices** in `tests/integration/seam/**` for every save/load/apply change (ADR-038; NO `Mock<HttpMessageHandler>`, DI-registration, or ctor-null tests). New corpus fixtures for G12 (a doc with pre-existing revisions) + G2 (born-in-editor).
- Run **`/conflict-check` before every BFF PR** — overlaps `spaarkeai-compose-r1/r2/r3`, `spaarke-ai-architecture-redesign-r2`, and the `Spaarke.Compose.Components` surface shared with `analysis-hub-r1`.

## Licensing (NFR-03) — HARD
MIT/permissive only. No commercial/per-seat/AGPL. **No TipTap Pro** (`@tiptap-pro/*` forbidden) — MIT base + `@tiptap/extension-*` only. No runtime dependency on EigenPal. R5 adds appliers/ops/UX — no new library.

## Entry points

| Surface | Start here |
|---|---|
| Compose services / engine | `src/server/api/Sprk.Bff.Api/Services/Compose/` (`ComposeShadowPatchEngine.cs`, `ComposeDocumentRenderer.cs`, `ComposeService.cs`; R4.5: `NumberingComputationEngine`, `CitationResolver.cs`) |
| Compose endpoints | `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` (G8: `check-changes` + `spe-doc-changed` webhook already exist, unwired) |
| Op schema (shared spine) | `ComposeOperation.cs` ↔ client `compose-operations.ts` (extend for G4/G12) |
| Compose client | `src/client/shared/Spaarke.Compose.Components/src/` (`ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `stepOperationInterceptor.ts`/`classifyStep`, `TrackChangesExtension.ts`, `importedRevisions.ts`, `docxBridge.ts` ⚠️, `ComposeCommentThread*`) |
| Seam tests | `tests/integration/seam/Compose/` · unit: `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` |

## Applicable ADRs
ADR-013 (AI facade — no AI internals in Compose) · ADR-007 (no Graph above SpeFileStore) · ADR-039/040 (closed op catalog, engine frozen, no new AI dispatch) · ADR-038 (seam DoD; banned mock/DI/ctor tests) · ADR-009 (version/re-anchor state via `IDistributedCache`) · ADR-028 (client fetches via `@spaarke/auth`).

## Setup status / next steps
- ✅ `design.md` → `spec.md` (`/design-to-spec`, 11 FRs + 9 NFRs) → `plan.md` + **22 task POMLs + `tasks/TASK-INDEX.md`** (`/project-pipeline` → `/task-create`). All XML-valid, canonical metadata, seam-DoD constraints.
- ✅ Portfolio **Project #695** registered under Epic #421 (`/devops-project-register`); `projects/INDEX.md` row added (BFF=Y/SpaarkeAi=Y).
- ✅ Branch synced with master-with-R4.5 (picked up **ADR-049** governing Shadow Document ADR). BFF build green.
- ⛔ **Execution HELD on two Phase-0 HUMAN GATES**: (1) task **002** Dataverse `sprk_composeorigin` schema approval; (2) task **003** G2 clean-apply spike (R5-D2). See `tasks/TASK-INDEX.md` + `current-task.md`.
- ⏭️ **NEXT**: clear gates 002/003, then run tasks via **`task-execute`** per the TASK-INDEX DAG (`task-execute 001` first). Optional: set a Target Date on Project #695.

## Task set (22) — see `tasks/TASK-INDEX.md`
Phase 0 (gate): 001 baseline · 002🔔 Dataverse field · 003🔔 G2 spike · 004 op-schema design · 005 numbering reuse · 006 reciprocal note. Phase 1 (edit-path ops): 010 G3 align · 011 G3 heading/list · 012 G12 single · 013 G12 batch · 014 G4 tables(L). Phase 2 (lifecycle): 020 G1 · 021 G2 · 022 G7. Phase 3 (UX): 030 G8 · 031 G9 · 032 G11 · 033 G5. Phase 4: 040 G10 · 041 hardening · 042 deploy+UAT · 090 wrap-up. **Serialization-heavy** (shared Compose files → `parallel-safe:false` dominates); `/goal`-ineligible (human gates + judgment-heavy).
