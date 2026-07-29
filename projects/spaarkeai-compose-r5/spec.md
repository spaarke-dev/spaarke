# Spaarke Compose R5 — Editing Completeness — AI Implementation Specification

> **Status**: Ready for Implementation (pipeline-ready)
> **Created**: 2026-07-28
> **Source**: [`design.md`](design.md) (+ [`README.md`](README.md) gap ledger, [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md))
> **Codename**: Spaarke Compose R5 (R1 → R2 → R3 → R4 → R4.5 → **R5**)
> **Owner**: Ralph Schroeder
> **Positioning**: R4 made the OOXML round-trip correct-by-construction and shipped error-free with documented functional limits. R4.5 made *reading* a legal doc high-fidelity + referenceable. **R5 implements the editing limits** — it removes each R4 guard by building the feature behind it, so the editor covers every construct a user can create, and authored documents get a clean cross-session lifecycle. R5 is **additive**; it re-litigates none of R4's locked decisions (D1–D5) or invariants (I-1–I-7).

---

## Executive Summary

Compose is Spaarke's legal drafting workspace: a lawyer opens (or authors) a Word document, edits it, accepts/rejects AI redlines, and saves back to SharePoint Embedded (SPE) as real `.docx` with native tracked changes and threaded comments. R4 shipped the correct engine by **guarding** every construct its closed 10-operation schema didn't yet cover (disabled controls, informed paste, op-log preserved across rejected saves — **no user-triggerable errors, no silent data loss**). **R5 removes each guard by implementing the feature behind it**, across 11 code-grounded gaps (G1–G5, G7–G12), while holding R4's no-error / no-silent-loss invariant intact. This is editing completeness + document lifecycle — not translation correctness (R4 solved that) and not read fidelity (R4.5 solved that).

---

## Scope

### In Scope — the 11-gap R5 backlog

- **G1 — Cross-session authored-vs-imported origin routing.** A durable origin marker (Dataverse field on `sprk_document`, per owner) persisted at create-on-save and returned by `LoadAsync`; the client routes reopened authored docs onto the clean payload instead of the op-log. (REQ 1)
- **G2 — Clean (non-tracked) apply mode.** An engine clean-apply branch that emits plain runs (no `w:ins`/`w:del`), **OR** re-author authored docs from the content model each save — chosen by a Phase-0 spike (R5-D2). (REQ 1)
- **G3 — `setBlockAttr` applier.** `Style` (heading level), `ListOrdered`, `ListLevel`, `Alignment` applied as tracked `w:pPrChange`; client emits heading/list in `classifyStep` (today `defer-structural`). Edit-side numbering **reuses R4.5's `NumberingComputationEngine`** (R5-D4). (REQ 3 edit)
- **G4 — Table op (full tracked structure).** New op type in the closed `ComposeOperation` set + client capture + engine applier emitting **full tracked table structure** (`w:tblPrChange`, row/cell `w:ins`/`w:del` tracking). The L-sized long pole — scheduled last. (REQ 3 edit)
- **G5 — Hyperlink support.** `href` on `ComposeInlineRun` + `w:hyperlink` in `ComposeDocumentRenderer.BuildRun` (authored path); hyperlink op + `link` in `ComposeMarkType` + engine applier (edit path). (REQ 3 edit)
- **G7 — Save-Version vs Save-New-Document control.** Toolbar split-button + create-vs-replace routing; covers the transient/upload path (identity from R4.5 WS-1). (Versioning UX)
- **G8 — External-change refresh + remount banner.** Wire the existing `POST /api/compose/document/{id}/check-changes` + `spe-doc-changed` webhook to a remount + non-blocking banner. (Concurrency UX)
- **G9 — Comment pane scroll-sync.** Position-link the Comments pane to in-document anchor positions. (Comments UX)
- **G10 — Document Profile re-run on Compose save** (+ reload + manual "Refresh Profile" button), so downstream analysis/search reflects edits; reuses R4.5 `CitationResolver` (`paraId→legal-number`) for precise citations. (Dataverse profiling; R5-D5)
- **G11 — Track-changes toggle keeps pre-existing redlines visible.** Toggling the user's free-typed-edit overlay off must not hide imported/AI redlines (first-class marks). View-only, no persistence change. (UX clarity)
- **G12 — Accept/reject imported tracked changes (ET-2 reconciliation).** `acceptRevision`/`rejectRevision` ops addressed by revision **id** — **single-by-id AND accept-all/reject-all batch** (per owner). Engine resolves natively (accept-ins strips `w:ins`; accept-del removes run; reject = inverse); also fixes imported-deletion end-of-paragraph re-anchor. (Tracked-change reconciliation)

Each gap's **exit criterion is removing its R4 guard** (re-enabling the disabled control) with a `tests/integration/seam/**` slice proving the construct round-trips and the no-error / no-silent-loss invariant still holds.

### Out of Scope

- **G6 — transient-mount projection unification / mammoth removal.** ✅ Done by R4.5 WS-1. **R5 must NOT re-scope it.** (Traceability stub only.)
- **READ side of REQ 3** — rendering headings/lists/numbering faithfully on load/upload → owned by R4.5 (WS-2/WS-3). R5 owns the **EDIT** side only.
- **Merging the two byte-authors** — the renderer (clean) + engine (tracked) split is the shipped R4 decision (036/037, C-revised). Collapsing to one public byte-author is an optional cosmetic refactor, explicitly out of R5 scope.
- **pdf / xlsx / pptx** — docx-only (D3, inherited).
- **WOPI-embed editing** — SPE = store + open-in-Office launch (D4, inherited).
- **New AI dispatch endpoint / forking `Services/Ai/`** — engine frozen (ADR-039); consume `Services/Ai/PublicContracts/` seams only.
- **New runtime library** — R5 is appliers + ops + client interceptors + UX; zero new runtime package expected.

### Affected Areas

> **Discovery corrections (2026-07-28, code-grounded):** paths/anchors verified against the branch. (a) `ComposeOperation.cs` is under `Services/Compose/**Operations**/`; client mirror at `types/compose-operations.ts` — both in exact sync (10 ops, no table/revision ops yet). (b) **`NumberingComputationEngine` is NOT a standalone file** — it's an `internal sealed class` nested in `ComposeDocxProjectionBuilder.cs:~1357`; **G3 needs an extract-to-standalone-vs-reference-nested decision** (its `internal` visibility may block reuse from the engine — Phase-0/Phase-1 item). (c) **G8 endpoints ARE registered/wired** (`ComposeEndpoints.cs:209` webhook, `:233` check-changes, real handlers) — G8's true gap is the webhook **delivery/subscription** leg (E2E-pending) + the **client remount/banner** wiring, not "unwired." (d) **G10 save-hook profile trigger already exists** (`ComposeService.cs:~895–942`, fire-and-forget on a detached DI scope) — G10's gap is **reload/onload + manual button** only. (e) R4 **guard sites** to re-enable live in `ComposeFormatToolbar.tsx` (hyperlink `:635`, structural heading/list/alignment `:537/:546/:563/:572/:581`, table `:654`). (f) client files are under `widgets/`, `types/`, `utils/`, `widgets/marks/`. (g) **docxBridge hazard clean** — write helpers present, `docxToTipTapHtml` gone.

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs` — clean-apply branch (G2); `setBlockAttr`→`w:pPrChange` applier (G3, fills the `StructuralOpNotYetImplemented` throw at `:249`/enum `:1438`); full table applier (G4); hyperlink edit applier (G5); `acceptRevision`/`rejectRevision` handlers incl. batch (G12). Always-tracked appliers today: `ApplyInsertText:314`, `WrapRunAsDeleted:942`.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs` — `w:hyperlink` emission on `BuildRun` (G5 authored path).
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` — origin marker persist+return (G1); create-vs-replace routing (G7); profile re-trigger on save + reload (G10). **⚠️ R4.5-owned-FIRST contended file — rebase onto post-R4.5 `LoadAsync`/`SaveAsync`.**
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeOperation.cs` ↔ client `compose-operations.ts` — extend the closed catalog with `table` + `acceptRevision`/`rejectRevision` ops (G4/G12; R5-D3 — extend never fork).
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — G8 endpoints (`check-changes`, `spe-doc-changed` webhook) already exist, unwired; soft-contended.
- `src/client/shared/Spaarke.Compose.Components/src/ComposeWorkspace.tsx` — origin/transient/save routing (G1, `isTransientCreate` region); Save split-button (G7); external-change remount (G8). **⚠️ R4.5-owned-FIRST contended file.**
- `src/client/shared/Spaarke.Compose.Components/src/ComposeEditor.tsx` — external-change banner (G8); comment scroll-sync (G9); track-changes-off redline visibility (G11); toolbar. Soft-contended (also with `analysis-hub-r1`).
- `src/client/shared/Spaarke.Compose.Components/src/stepOperationInterceptor.ts` (`classifyStep`) — emit heading/list `setBlockAttr` (G3), table steps (G4), revision ops (G12) that are `defer-structural` today.
- `src/client/shared/Spaarke.Compose.Components/src/TrackChangesExtension.ts` (`:157–161`) — G11 view-only decoration flip.
- `src/client/shared/Spaarke.Compose.Components/src/importedRevisions.ts` — G12 revision resolution + imported-deletion end-of-paragraph re-anchor.
- `src/client/shared/Spaarke.Compose.Components/src/docxBridge.ts` — **⚠️ DO NOT DELETE.** G1/G2/G7 depend on `buildContentModel`/`stampParaIds`/paraId helpers; only R4.5's read fn `docxToTipTapHtml` was removed.
- `src/client/shared/Spaarke.Compose.Components/src/ComposeCommentThread*` — G9 scroll-sync.
- **Dataverse schema** — one new `sprk_document` field for the G1 origin marker (see Dependencies — human schema gate).
- **Tests** — `tests/integration/seam/Compose/` (seam slices per applier + save/load change); `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`. New corpus fixtures: G12 (CIPO patent w/ pre-existing revisions), G2 (born-in-editor doc), G4 (table doc).
- **Reuse from R4.5 (do NOT rebuild):** `NumberingComputationEngine` (G3), `CitationResolver.cs` `paraId→legal-number` (G10), transient-mount projection identity (G7).

---

## Requirements

### Functional Requirements

Each FR maps to a design gap; acceptance = the R4 guard removed + a through-the-wire seam slice + no-error/no-silent-loss preserved.

1. **FR-01 (G1) — Cross-session authored-vs-imported origin routing.** Persist a durable origin marker (Dataverse field on `sprk_document`) at create-on-save; `LoadAsync` returns it; the client routes reopened authored docs onto the clean payload. *Acceptance:* a reopened authored doc loads with origin=authored and its subsequent edits take the clean path (not the op-log/tracked path); imported docs load origin=imported and stay tracked. Seam slice on both origins.

2. **FR-02 (G2) — Clean (non-tracked) apply mode.** The authored-doc save path applies edits WITHOUT emitting `w:ins`/`w:del` — via an engine clean-apply branch OR re-author-from-content-model (R5-D2 Phase-0 spike selects; spec records both, spike picks one and documents the reason). *Acceptance:* a born-in-editor doc's cross-session edits produce clean OOXML (zero tracked-change markup) that opens in Word with no redlines; corpus byte-diff shows untouched subtrees byte-identical. Seam slice on the born-in-editor corpus doc.

3. **FR-03 (G3) — `setBlockAttr` applier (heading / list / alignment).** Fill the `StructuralOpNotYetImplemented` seam: apply `Style`(heading level), `ListOrdered`, `ListLevel`, `Alignment` as tracked `w:pPrChange` (imported) or clean paragraph properties (authored). Client `classifyStep` emits heading/list (today `defer-structural`; alignment already emitted). **Edit-side numbering reuses R4.5 `NumberingComputationEngine` — MUST NOT fork the numbering algorithm** (R5-D4 / R4.5 FR-14). **Reuse mechanism decision (Phase-0/1):** the engine is currently `internal sealed` nested in `ComposeDocxProjectionBuilder.cs:~1357` — G3 either extracts it to a standalone reusable type or references it in place; the extraction (if chosen) is a pure refactor that MUST keep the projection-builder path byte-identical. *Acceptance:* ET-1 (alignment) and SDL-1/2 (heading/list) guards removed — controls re-enabled on loaded docs; edit applies as tracked `w:pPrChange`; renumber matches R4.5's read-time model. Per-construct seam slices.

4. **FR-04 (G4) — Table op (full tracked structure).** Add one `table` op to the closed catalog (R5-D3 — extend, not fork); client captures table steps (today `defer-structural`); engine applier emits **full tracked table structure**: `w:tblPrChange`, row/cell `w:ins`/`w:del` tracking for add/remove row/column and cell-content edits. *Acceptance:* SDL-3 guard removed — table control re-enabled on loaded docs; a table structural edit round-trips as Word-valid tracked table changes; untouched subtrees byte-identical. Seam slice on a table corpus doc. **Scheduled last (L-item).**

5. **FR-05 (G5) — Hyperlink support (both paths).** Authored: `href` on `ComposeInlineRun` + `w:hyperlink` in `ComposeDocumentRenderer.BuildRun`. Edit: hyperlink op + `link` in `ComposeMarkType` + engine applier. *Acceptance:* SDL-4/5 guards removed — hyperlink control re-enabled in both modes; a hyperlink survives authored save (clean `w:hyperlink`) and imported edit (tracked); no `unrepresentable`/silent-loss path. Seam slices on both paths.

6. **FR-06 (G7) — Save-Version vs Save-New-Document control.** Toolbar split-button: **Save Version** (update existing `sprk_document` + SPE item — default) vs **Save New Document** (deliberate fork); `ComposeService` create-vs-replace routing covers the transient/upload path using R4.5 WS-1 transient-mount identity. *Acceptance:* the 8-duplicate UAT scenario no longer mints duplicate records on transient/upload saves; Save-Version replaces in place, Save-New forks a new record. Seam slice + UAT. **Depends on R4.5 WS-1.**

7. **FR-07 (G8) — External-change refresh + remount banner.** The `check-changes` endpoint (`ComposeEndpoints.cs:233`) + `spe-doc-changed` webhook (`:209`) are already registered with handlers; **G8 completes the two incomplete legs**: (a) the webhook **delivery/subscription** leg (E2E-pending), and (b) the **client** mount-lifecycle remount + non-blocking banner ("Document updated from document management system version"). After a 423 lock releases (web/desktop closed), a refresh remounts the projection. *Acceptance:* an external edit (Open-in-Web/Desktop) triggers remount + banner without data loss; the existing 423 lock message still works. UAT + mount-effect seam/UI coverage.

8. **FR-08 (G9) — Comment pane scroll-sync.** The Comments pane scroll-tracks in-document anchor positions (position-linked, not a flat list). *Acceptance:* scrolling the document moves the pane to the corresponding comment anchor and vice versa. UAT.

9. **FR-09 (G10) — Document Profile re-run on Compose save.** Fire the Dataverse Document-Profile re-trigger on the Compose→`sprk_document`/SPE save path (R5-D5 — save-hook already exists fire-and-forget at `ComposeService.cs:843`; the real gap is **reload/onload + a manual "Refresh Profile" button**), reusing R4.5 `CitationResolver` for precise `paraId→legal-number` citations. *Acceptance:* a Compose save re-runs the profile so downstream analysis reflects edits; reload + manual button both re-trigger. **Escape hatch (R5-D5):** if complexity proves high, document the deferral per §6.5 rather than shipping a half-wired trigger. Save→profile-updated assertion or documented deferral.

10. **FR-10 (G11) — Track-changes-off keeps imported redlines visible.** Toggling the user's free-typed-edit overlay off (`TrackChangesExtension.ts:157–161` + toolbar) MUST keep imported/AI redlines (first-class marks) rendered. View-only; **no persistence change**. *Acceptance:* with the toggle off, imported/AI redlines remain visible and the user's own overlay hides; toggling on restores the overlay; save output unchanged. Client test/UAT.

11. **FR-11 (G12) — Accept/reject imported tracked changes (single + batch).** Add `acceptRevision`/`rejectRevision` ops addressed by revision **id** to the closed catalog (R5-D3), **plus accept-all / reject-all batch ops** (per owner). Engine resolves natively (accept-ins strips `w:ins` keep run; accept-del removes run; reject = inverse) with deterministic batch ordering; fix the imported-deletion end-of-paragraph re-anchor in `importedRevisions.ts`. **Zero text-search — resolve by revision id, not content match** (I-7 / NFR-02). *Acceptance:* accepting or rejecting a pre-existing imported tracked change (single AND batch) then saving succeeds — no `TrackedChangeReconciliationUnsupported` 422; output is Word-valid revision state. ET-2 seam slice on the CIPO corpus doc with pre-existing revisions.

### Requirement-level acceptance (owner REQs)

- **REQ 1** (G1+G2): a reopened authored doc's edits are CLEAN; imported docs still track. Verify: seam slice + UAT.
- **REQ 2**: imported-doc edits are TRACKED. ✅ Already met in R4 (baseline; guarded against regression by NFR-08).
- **REQ 3 (edit)** (G3/G4/G5): headings, lists, tables, hyperlinks apply as clean (authored) or tracked (imported); each R4 guard removed. Verify: per-construct seam slice + control re-enabled.

### Non-Functional Requirements

- **NFR-01 — Byte preservation** (inherited I-4): every construct edit leaves untouched OOXML subtrees byte-identical; R4's corpus byte-diff harness (24/24) MUST NOT regress.
- **NFR-02 — Placement determinism / no text-search** (inherited I-7): every new applier resolves by `(paraId, runIndex, offset)` or revision **id** — zero text-search in the write path, including G12 revision resolution.
- **NFR-03 — Licensing** (inherited): MIT/permissive only; `DocumentFormat.OpenXml` (MIT, already a dep) + MIT TipTap base + `@tiptap/extension-*` only. **No `@tiptap-pro/*`, no Syncfusion/SuperDoc/EigenPal runtime dep.** No new runtime package for R5.
- **NFR-04 — Publish size** (inherited, root §10): BFF ≤60 MB compressed. Report absolute + delta vs **~46.11 MB** post-R4 baseline on every BFF-touching task; ≥+5 MB single-task delta → justify; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP.
- **NFR-05 — Facade purity** (inherited): `Services/Compose/` stays `byte[]`-in/`byte[]`-out — no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest); no `Microsoft.Graph` above `SpeFileStore` (ADR-007).
- **NFR-06 — Seam DoD** (inherited, ADR-038): every save/load/apply change carries a through-the-wire `WebApplicationFactory` seam slice in `tests/integration/seam/**`. **No `Mock<HttpMessageHandler>`, no DI-registration tests, no ctor-null tests.**
- **NFR-07 — Word-native output** (inherited): results open in Word / Word-for-web with real accept/reject redlines + threaded comments; G12 reconciliation produces Word-valid revision state.
- **NFR-08 — No-regression of guards** (R5-specific): each removed guard MUST NOT reintroduce a user-triggerable error or silent data-loss path; R4's op-log-preservation safety net stays in place until the replacing feature is proven by a seam slice.
- **NFR-09 — Downstream-consumer safety** (R5-specific): the save/versioning/redline contract changes (G1/G2/G7/G10/G12) MUST NOT regress `ai-advanced-capabilities-analysis-hub-r1`'s reopen-restore (its FR-09/FR-11) or clean-retirement parity assumptions. G12 in particular hardens the redline-reconciliation behavior that project's retirement parity depends on — a positive coupling that MUST NOT break existing accept/reject.

---

## Technical Constraints

### Applicable ADRs

- **ADR-049 (canonical — Compose Shadow Document Architecture)** — codifies D1–D5, I-1–I-7, the two-byte-author split, and the R4.5 read-side F-1…F-5 invariants. Explicitly states "live renumber-on-edit is **R5 G3**" (F-3) and "consumer wiring of `CitationResolver` continues downstream" (G10). **The governing ADR for every R5 task.**
- **ADR-039 / ADR-040** — closed op catalog, engine frozen, no new AI dispatch endpoint. G4/G12 **extend** the `ComposeOperation` catalog under version control; AI redline path stays envelope-only.
- **ADR-013** — AI facade; no AI internals in `Services/Compose/`. R5 appliers are pure OOXML (Tier-1 NetArchTest).
- **ADR-010** — Patch Engine = stateless concrete singleton. New appliers (G2/G3/G4/G5/G12) stay `byte[]`-in/`byte[]`-out with no per-request state.
- **ADR-007** — no `Microsoft.Graph` above `SpeFileStore`. G7/G10 keep the SPE hop in the endpoint/facade layer; the profile re-trigger is a domain event, not a Graph call in the engine.
- **ADR-038** — integration-heavy testing; seam DoD; banned `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests.
- **ADR-009** — version/re-anchor state via `IDistributedCache`.
- **ADR-028** — client fetches via `@spaarke/auth`.

### MUST / MUST NOT Rules

- ✅ MUST resolve every applier by `(paraId, runIndex, offset)` or revision id — **MUST NOT** text-search in the write path (I-7).
- ✅ MUST leave untouched OOXML subtrees byte-identical (surgical ops, I-4).
- ✅ MUST extend the closed `ComposeOperation` catalog for G4/G12 — **MUST NOT** fork it or add a side channel.
- ✅ MUST reuse R4.5 `NumberingComputationEngine` for G3 renumber — **MUST NOT** re-implement numbering.
- ✅ MUST keep `Services/Compose/` `byte[]`-in/`byte[]`-out (ADR-013 Tier-1) — **MUST NOT** inject AI-internal types.
- ✅ MUST add a `tests/integration/seam/**` slice for every save/load/apply change — **MUST NOT** use banned test shapes.
- ✅ MUST preserve the two-byte-author split (renderer clean / engine tracked) — **MUST NOT** merge the byte-authors or force authored origination through the op log.
- ❌ MUST NOT delete `docxBridge.ts` or "remove the mammoth module" — only R4.5's read fn `docxToTipTapHtml` is gone; `buildContentModel`/`stampParaIds`/paraId helpers are load-bearing for G1/G2/G7.
- ❌ MUST NOT re-scope G6 (done by R4.5 WS-1).
- ❌ MUST NOT add a new runtime package (NFR-03/NFR-04).

### Existing Patterns to Follow

- Engine appliers — `ComposeShadowPatchEngine.ApplyInsertText` / `WrapRunAsDeleted` (tracked path; add clean + structural branches alongside).
- Authored renderer — `ComposeDocumentRenderer.BuildRun` (clean-authoring, zero text-search, the cited I-5 exception).
- Op-schema mirror — `ComposeOperation.cs` ↔ `compose-operations.ts` (one schema both ends implement).
- Reuse — R4.5 `NumberingComputationEngine` + `CitationResolver.cs` in `Services/Compose/`.
- Seam DoD reference — existing `tests/integration/seam/Compose/` slices.

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>            <!-- Services/Compose/ engine appliers, op schema, save-path profile hook -->
  <spaarkeai>Y</spaarkeai> <!-- Compose widget hosted in the SpaarkeAi three-pane; toolbar/mount/comment UX -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**BFF=Y → Placement Justification required per component** (cite `.claude/constraints/bff-extensions.md`); ≤60 MB publish ceiling per task; **run `/conflict-check` before every BFF PR** (overlaps `spaarkeai-compose-fidelity-r4.5`, `spaarkeai-compose-r1/r2/r3`, `spaarke-ai-architecture-redesign-r2`, and the `Spaarke.Compose.Components` surface shared with `analysis-hub-r1`). All new Compose surface lands in `Services/Compose/` (engine/save orchestration) — no new service, no new library, no new BFF endpoint family (G8 wires endpoints that already exist).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Engine **clean-apply branch** (G2/FR-02) | `ComposeShadowPatchEngine.ApplyInsertText`/`WrapRunAsDeleted` (tracked-only) | **Extend** with a mode flag/branch — not a new class | A reopened authored doc's own edits render as tracked changes (REQ-1 wart persists) |
| `setBlockAttr` applier (G3/FR-03) | Engine `StructuralOpNotYetImplemented` seam | **Extend** — fill the seam; reuse R4.5 `NumberingComputationEngine` | Heading/list/alignment edits on loaded docs stay `defer-structural` / 422-guarded |
| **`table` op + full applier** (G4/FR-04) | Closed `ComposeOperation` catalog | **Extend** the catalog with one op + applier | Table edits silently dropped; table control stays disabled on loaded docs |
| Hyperlink op + `w:hyperlink` render (G5/FR-05) | `ComposeMarkType`, `ComposeDocumentRenderer.BuildRun` | **Extend** the mark set + renderer | Hyperlinks silently lost (`unrepresentable`); control disabled in both modes |
| `acceptRevision`/`rejectRevision` (+ batch) ops (G12/FR-11) | Closed op catalog; `importedRevisions.ts` | **Extend** with id-addressed revision ops | Accepting an imported tracked change + saving fails with 422 |
| **Origin marker field** (G1/FR-01) | `sprk_document` fields | **Extend** the record with one durable field — no new table | Reopened authored docs flip to the tracked path (only discriminator today is "has an SPE id") |

Everything else (G7 toolbar, G8 banner, G9 scroll-sync, G10 hook, G11 toggle) is **modify/extend** of existing client/service surface — no new component.

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR / prior decision | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-049 (Compose Shadow Document — governing)** | I-1…I-7 + D1–D5 (surgical ops, no text-search, one byte-author per path, engine `byte[]`-in/out) | None — R5 is additive appliers/ops on the same spine | **C (comply)** | Every R5 applier honors ADR-049's MUSTs; the one standing exception (two byte-authors per path) is ADR-049's own accepted state, restated in the row below. ADR-049 F-3 names G3 as the sanctioned live-renumber follow-on. |
| **ADR-039 (closed catalogs / frozen engine)** | Grounded execution over a closed op catalog | G4/G12 add op types | **C (comply)** | New ops **extend** the closed `ComposeOperation` catalog under version control; no side channel, no new AI dispatch endpoint. The AI-redline path stays envelope-only. |
| **R4 D5 (one byte-author) / I-5** | "One byte-author writes the package" | R5 keeps the renderer (clean) + engine (tracked) split for G1/G2 | **A (project-scoped exception — already granted in R4 as C-revised)** | Two byte-authors per *path* is the shipped R4 decision (036/037). R5 honors it; it does not force authored origination through the op log. Cited, not re-opened. |
| **ADR-013 (AI facade)** | No AI internals in `Services/Compose/` | None — R5 appliers are pure OOXML | **C (comply)** | Engine stays `byte[]`-in/`byte[]`-out; Tier-1 NetArchTest enforces. |
| **ADR-007 (Graph isolation)** | No `Microsoft.Graph` above `SpeFileStore` | G7/G10 touch the SPE save path | **C (comply)** | SPE hop stays in the endpoint/facade layer; the profile re-trigger is a domain event, not a Graph call in the engine. |
| **ADR-038 (testing)** | Seam DoD; banned mock/DI/ctor tests | New appliers + save-path changes | **C (comply)** | Seam slices in `tests/integration/seam/**` for every applier + save/load change; corpus byte-diff no-regression. |

> Additional tensions may surface at Phase 0 (notably the G2 spike and the G1 Dataverse-schema gate); handle via §6.5 (surface as A/B/C).

---

## Cross-Project Coordination (BINDING — mirrored from design §10; enforced by `/conflict-check`)

### `spaarkeai-compose-fidelity-r4.5` — ✅ MERGED to master (2026-07-28); execution gate CLEARED

Canonical source: [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md) — read before opening any Compose PR.

- **Aligned in direction, no design conflict.** R4.5 does not touch `ComposeShadowPatchEngine`/byte-authoring (its T-3 = comply with the two-author split) — the engine is cleanly R5's. R5 **rebases onto** R4.5 outputs now on master: **G7**←WS-1 transient-mount identity (HARD), **G1**←WS-1 `isTransientCreate` region (HARD), **G3**←WS-3 `NumberingComputationEngine` (reuse, FR-14), **G2**←WS-2/3 projection round-trip fidelity, **G8/G9**←WS-1 mount effects, **G10**←WS-4 `paraId→legal-number`.
- **Two HARD merge-conflict files (R4.5-owned-FIRST; R5 rebases):** `ComposeService.cs`, `ComposeWorkspace.tsx`. Soft-contended: `ComposeEditor.tsx`, `ComposeEndpoints.cs`, `docxBridge.ts`.
- **⚠️ docxBridge hazard:** never delete `docxBridge.ts` / "remove the mammoth module" — G1/G2/G7 depend on its write helpers.
- **Deploy contention:** shared `sprk_spaarkeai` web resource + `spaarke-bff-dev` ("last deploy wins"). Build/deploy from master-with-R4.5.
- **Reciprocal-note gap:** the mirror note in `projects/spaarkeai-compose-fidelity-r4.5/notes/` was never created — low-risk (R4.5 code-complete); optional Phase-0 courtesy task to author it.

### `ai-advanced-capabilities-analysis-hub-r1` — surface-sharing + behavioral downstream consumer (NFR-09)

- PLANNED/not-started (blocked on a human Dataverse-schema gate); **low direct code-conflict** but a **real behavioral coupling**. Shares `Spaarke.Compose.Components` (`ComposeEditor.tsx`, `ComposeAiToolbar.tsx`), `ConversationPane` compose-routing (`composeReviseRouting.ts`/`composeDraftRouting.ts`), the SpaarkeAi three-pane shell + `launch-resolver.ts`, and the `ConversationPane.compose-*` e2e tests. **R5's G8/G9/G11 client work + G7 toolbar touch this exact surface** — coordinate merge order (`/conflict-check` catches file-level, not region-level, overlap).
- **Disjoint BFF:** analysis-hub's BFF work is in `Services/Ai/` + a new `/api/ai/analysis/fork`; no `/api/compose/**` reference. R5's `Services/Compose/` BFF work does not collide.
- **Behavioral (the real risk — NFR-09):** analysis-hub is a downstream consumer of Compose save/versioning/redline. Its `sprk_analysisworkspace` retirement assumes Compose auto-save-to-SPE + accept/reject redlines + export; its FR-09/FR-11 reopen-restore assumes a stable Compose save/version contract over shared `sprk_document`/SPE driveItem. **R5's G1/G2/G7/G10/G12 land underneath that assumption and MUST NOT regress it.**
- **`sprk_chathistory`:** analysis-hub Task 062 plans to retire the read; `ChatEndpoints`/Insights also write it. R5 does not touch it — flagged so a shared-column deletion isn't assumed safe.
- **Reciprocal action:** register R5 in `projects/INDEX.md` (pipeline does this) so `/conflict-check` surfaces the `Spaarke.Compose.Components` + `ConversationPane` overlap; add R5 to analysis-hub's Compose coordination clause when that project pipes to execution (its `PLAN.md:41` lists `compose-r1..r4` but not R5).

---

## Phasing (WBS sketch — full decomposition is `/project-pipeline`'s job)

> **Hard prerequisite ✅ satisfied:** R4.5 merged to master 2026-07-28 → Phase-0's merge check is confirm-only. R5 may pipe to execution.

- **Phase 0 — Gate:** confirm R5-D1…D5 (✅ done — all confirmed); **G1 Dataverse-schema gate** (author the `sprk_document` origin field — human approval); **G2 clean-apply spike** (re-author-from-model vs engine clean branch) on a born-in-editor corpus doc; op-schema extension design for G4/G12; confirm R4.5 merged; (optional) author the reciprocal R4.5 coordination note.
- **Phase 1 — Edit-path structural ops (op-schema wave):** G3 alignment (S) → G3 heading/list (M, reuse R4.5 numbering) → G12 accept/reject-revision incl. batch (M–L) → G4 tables full tracked structure (L). Grouped — shared op-schema + engine-applier surface.
- **Phase 2 — Authored-doc lifecycle:** G1 origin routing + G2 clean-apply (REQ 1 — highest user-visible value) → G7 versioning UX (split-button + upload-path coverage on R4.5 identity).
- **Phase 3 — Concurrency + UX:** G8 external-change refresh banner → G9 comment scroll-sync → G11 track-changes-off redline visibility → G5 hyperlinks (both paths).
- **Phase 4 — Profiling + hardening + cutover:** G10 profile re-run on save (triage complexity first — R5-D5 escape hatch) → corpus byte-diff no-regression → publish-size → deploy + operator UAT.

---

## Success Criteria (graduation)

1. [ ] **REQ 1** — a reopened authored doc's edits are CLEAN (no tracked changes); imported docs still track. Verify: seam slice + UAT.
2. [ ] **REQ 3 (edit)** — headings, lists, tables, hyperlinks apply as clean (authored) or tracked (imported); each R4 guard removed + control re-enabled. Verify: per-construct seam slice.
3. [ ] **G12** — accepting/rejecting an imported tracked change (single AND batch) saves successfully (no 422). Verify: ET-2 seam slice on the CIPO corpus doc with pre-existing revisions.
4. [ ] **Versioning (G7)** — Save-Version updates in place; Save-New forks; transient/upload docs no longer mint duplicates. Verify: UAT (the 8-duplicate scenario) + seam slice.
5. [ ] **Concurrency/UX (G8/G9/G11)** — external-change remount+banner; comment scroll-sync; track-changes-off keeps imported redlines visible. Verify: UAT.
6. [ ] **Profiling (G10)** — Compose save re-runs the Document Profile (+ reload + manual button), OR documented complexity deferral per R5-D5. Verify: save→profile-updated assertion or deferral note.
7. [ ] **No regression** — corpus byte-diff still 24/24; publish ≤60 MB; ADR + seam tests green; no new user-triggerable error path; NFR-09 downstream parity intact. Verify: harness + `dotnet publish` + CI.

---

## Dependencies

### Prerequisites

- **✅ CLEARED — `spaarkeai-compose-fidelity-r4.5` merged to master** (transient-mount projection + `NumberingComputationEngine` + `CitationResolver.cs`, 2026-07-28). Former execution-blocking dependency satisfied.
- **🔔 HUMAN GATE — G1 Dataverse schema change.** The origin marker is a new `sprk_document` field (owner decision). Authoring the field requires the human Dataverse-schema approval gate (same class of gate that blocks `analysis-hub-r1`). Recommended: a two-value choice/option-set `sprk_composeorigin` (`Authored` / `Imported`), default set at create-on-save. **Field name + type to be confirmed at Phase 0** (see Unresolved Questions). G1 code (FR-01) is blocked until the field exists.
- **New worktree** `spaarkeai-compose-r5` (created off updated master). Register in `projects/INDEX.md` (hot-path BFF=Y, SpaarkeAi=Y) with the §Coordination watch-list — `/project-pipeline` does this.
- **Corpus:** reuse R4's fidelity corpus (CIPO patent w/ pre-existing revisions = the G12 ET-2 doc) + a born-in-editor doc for the G2 spike + a table doc for G4.

### External Dependencies

- **Portfolio:** register a Project Issue under Epic #421 (SPAARKE AI), mirroring R4's #679 (R5 has no Project Issue yet) — `/devops-project-register` or defer.
- **Consume `Services/Ai/PublicContracts/` seams only — NO fork of `Services/Ai/`** (`spaarke-ai-architecture-redesign-r2` is sole owner); engine frozen (ADR-039).
- Zero new runtime package (`DocumentFormat.OpenXml` already present; baseline ~46.11 MB).

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| R5 decisions | Confirm R5-D1…D5 as the binding basis? | **Confirm all as written** — R5-D2 stays a Phase-0 spike; R5-D5 keeps G10 in R5 with a complexity-defer escape hatch | All five become binding spec constraints (see §Technical Constraints, FR-02, FR-03, FR-09) |
| Origin marker (G1/Q3) | Dataverse field vs SPE item metadata? | **Dataverse field on `sprk_document`** | Adds a schema change → human Dataverse gate (see Prerequisites); marker is server-queryable; FR-01 LoadAsync contract returns it |
| G4 table scope (Q4) | Full tracked structure vs reduced cell-content-only first slice? | **Full tracked table structure** (`w:tblPrChange` + row/cell tracking) | FR-04 commits to the full L-item; scheduled last in Phase 1; highest value/highest risk |
| G12 granularity (Q5) | Single-by-id only vs also accept-all/reject-all batch? | **Single-by-id AND accept-all/reject-all batch** | FR-11 adds batch ops with deterministic reconciliation ordering + batch seam cases |
| Deploy order (Q6) | Confirm R4.5→master→R5 build/deploy order? | Settled (R4.5 merged); build/deploy from master-with-R4.5 | Recorded as assumption; deploy contention on shared `sprk_spaarkeai` + `spaarke-bff-dev` — coordinate timing |

---

## Assumptions

*(Proceeding with these; owner did not further specify.)*

- **G1 field shape** — assuming a two-value option-set/choice `sprk_composeorigin` (`Authored`/`Imported`) on `sprk_document`, set at create-on-save, defaulting to `Imported` for docs that arrive with SPE bytes. Exact name/type confirmed at Phase 0.
- **G2 spike outcome** — spec keeps BOTH R5-D2 options open; the Phase-0 spike selects one on the born-in-editor corpus doc and documents the reason. FR-02 acceptance is invariant to which is chosen.
- **G10 depth (R5-D5)** — assuming reload/onload + manual "Refresh Profile" button is the deliverable (the save-hook trigger already exists fire-and-forget); if end-to-end profile complexity proves high, deferral is documented per §6.5 rather than shipping a half-wired trigger.
- **Deploy order** — R4.5→master→R5; R5 builds/deploys from master-with-R4.5 ("last deploy wins" on the shared surfaces).
- **Reciprocal R4.5 coordination note** — recommended as an optional Phase-0 courtesy task; low-risk since R4.5 is code-complete.

---

## Unresolved Questions

*(Resolve at Phase 0 — none block spec/pipeline, but the first blocks G1 code.)*

- [ ] **G1 origin field — exact name, type, and default.** Recommended `sprk_composeorigin` choice (`Authored`/`Imported`). **Blocks:** FR-01 (G1) code until the human Dataverse-schema gate approves the field.
- [ ] **G2 clean-apply approach (R5-D2 spike).** Engine clean-apply branch vs re-author-from-content-model. **Blocks:** FR-02 implementation path (not the spec); decided by the Phase-0 spike.
- [ ] **G4 tracked-table OOXML ordering.** The specific `w:tblPrChange` / row-cell `w:ins`/`w:del` structure for Word-valid tracked table edits. **Blocks:** FR-04 applier detail (design-time discovery in Phase 1); does not block the op-schema addition.
- [ ] **G12 batch reconciliation ordering.** Deterministic order for accept-all/reject-all so results are stable + Word-valid. **Blocks:** FR-11 batch acceptance (single-by-id can land first).
- [ ] **Portfolio registration** — create the Project Issue under Epic #421 now or defer. **Blocks:** nothing (portfolio hygiene only).

---

*AI-optimized specification. Original design: [`design.md`](design.md). Backlog source: [`README.md`](README.md). Coordination source: [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md). R5 = editing completeness on the R4 Shadow Document Architecture; re-litigates none of R4's D1–D5 / I-1–I-7; depends on R4.5 (landed).*
