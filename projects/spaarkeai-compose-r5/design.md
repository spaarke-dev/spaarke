# Spaarke Compose R5 — Design (Editing Completeness)

> **Status**: DRAFT for `/design-to-spec`. Authored 2026-07-28 from the code-grounded R5 backlog ([`README.md`](README.md)) + the R4.5 coordination note ([`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md)).
> **Codename**: Spaarke Compose R5 (continuing R1 → R2 → R3 → R4 → R4.5 → **R5**)
> **Positioning**: R4 made the OOXML round-trip *correct by construction* and shipped **error-free with documented functional limits**. R4.5 made *reading* a legal doc *high-fidelity + referenceable*. **R5 implements the editing limits** — it removes each R4 guard by building the feature behind it, so the editor covers every construct a user can create, and authored documents get a clean cross-session lifecycle.
> **Owner**: Ralph Schroeder
> **Supersedes / extends**: nothing is ripped out. R5 is **additive completion** on top of R4's Shadow Document Architecture. It re-litigates none of R4's locked decisions (D1–D5) or invariants (I-1–I-7).
> **Depends on**: **`spaarkeai-compose-fidelity-r4.5` must land to master first** (hard sequencing — see §10). Reuses R4.5's numbering engine (G3) and transient-mount projection identity (G7).

> ### Constraints carried forward (BINDING — inherited from R4, unchanged)
> - **NO commercial / per-seat / AGPL component.** MIT/permissive only: `DocumentFormat.OpenXml` (MIT, already a BFF dep), MIT TipTap base + `@tiptap/extension-*` only (never `@tiptap-pro/*`). No Syncfusion, no SuperDoc code, no TipTap Pro, no runtime dependency on EigenPal.
> - **We are NOT building Word.** Goal = preservation fidelity + placement determinism, now extended to *editing* completeness for the constructs users actually create.
> - **BFF publish-size ceiling ≤60 MB compressed** (root CLAUDE.md §10). Baseline post-R4 ≈ **46.11 MB** (R4 task 061). Zero new runtime package expected — R5 is engine appliers + client interceptors + UX, not new libraries.

---

## 0. Inherited foundation (DO NOT re-litigate — R5 builds on these)

R5 inherits R4's architecture wholesale. These are **fixed inputs**, restated so every R5 task honors them:

**The 7 invariants (I-1…I-7):** one authoritative OOXML model (never wholesale-regenerated for a loaded doc); server-authoritative bytes; `w14:paraId` stable addressing; edits are surgical operations (untouched subtrees byte-identical); one byte-author per path; client is a view+controller; **no text-search anchoring in the write path**.

**The locked decisions (D1…D5):** step-level operational deltas; `(paraId, runIndex, run-local-offset)` anchoring; docx end-to-end (pdf/xlsx/pptx are later phases); SPE = store + open-in-Office launch (versioning/lock/423 in scope, WOPI-embed out); **one unified `ComposeShadowPatchEngine`** on the imported/tracked path.

**The two-byte-author split is intentional and preserved (R4 owner decision 036/037, C-revised).** New/blank documents author *clean* bytes via `ComposeDocumentRenderer` (zero text-search — the cited I-5 exception); imported documents get *tracked* edits via `ComposeShadowPatchEngine`. **R5 does NOT merge the two authors** — G1/G2's "authored doc stays clean" is served by the renderer path + a clean-apply engine branch, not by forcing origination through the op log (README.md "Architecture note"). Collapsing to one public byte-author remains an optional cosmetic refactor, explicitly out of R5 scope.

### R5-specific decisions (owner-stated; flag any that are wrong before spec)

| # | Decision | Rationale | Rejected alternative |
|---|---|---|---|
| **R5-D1 — Origin is durable, not inferred** | An authored-vs-imported **origin marker** is persisted (Dataverse field or SPE metadata) at create-on-save and returned by `LoadAsync`; the client routes on it. | Today the only discriminator is "has an SPE id yet," so a reopened authored doc wrongly flips to the tracked path (REQ-1 wart). | Keep inferring from SPE-id presence — the exact defect G1 fixes. |
| **R5-D2 — Clean apply is an engine branch, not a second synthesizer** | The engine gains a **clean-apply mode** that emits plain runs (no `w:ins`/`w:del`); OR authored docs re-author from the content model each save. Choice is a Phase-0 spike (see G2). | Keeps one byte-author per path; avoids resurrecting a paragraph-diff-style second writer (the R4 defect class). | A separate clean-save code path — reintroduces two-writer drift. |
| **R5-D3 — New ops extend the closed catalog, they don't fork it** | G4 (table), G12 (accept/reject-revision) add op types to the **existing** `ComposeOperation` closed set; G3/G5 add appliers for already-defined ops. No parallel op vocabulary. | ADR-039 closed-catalog discipline; one schema both ends implement. | A side channel for structural/table edits — breaks the single-spine contract. |
| **R5-D4 — Edit-side numbering reuses R4.5's engine** | G3's heading/list renumber-on-edit consumes R4.5's `NumberingComputationEngine` (read-time numbering model); it does **not** re-implement numbering. | Pre-reconciled in R4.5 spec **FR-14** (R4.5 = read-time number; R5 G3 = edit-time renumber, **shared model**). | Fork the numbering algorithm in the engine — divergence + double-maintenance. |
| **R5-D5 — Profile re-run rides the Compose save hook** | G10 fires the Dataverse Document-Profile re-trigger on the Compose→`sprk_document`/SPE save path (plus reload + manual button). | The Compose save is exactly when the profile goes stale; the re-trigger belongs with the save. Owner decision 2026-07-28. Include unless it proves to add significant complexity. | Leave profiling to a separate pipeline project — leaves Compose edits un-profiled (the UAT gap). |

---

## 1. Product statement — why R5 exists

Compose is the legal drafting workspace inside Spaarke: a lawyer opens (or authors) a Word document, edits it, accepts/rejects AI redlines, and saves back to SPE as real `.docx` with native tracked changes and threaded comments.

**R4 delivered the correct engine and shipped by *guarding* the constructs the closed 10-operation schema didn't yet cover** — unsupported edit-path controls disabled on loaded docs, hyperlinks disabled in both modes, formatted-paste informed via a banner, op-log preserved across a rejected save (no batch ever lost). **Result: no user-triggerable errors, no silent data loss — just features visibly not-yet-available.**

**R5's one-sentence problem:** *the guards are the product's visible gaps — authored documents reopen showing tracked changes (their own text looks like redlines), and headings/lists/tables/hyperlinks/accept-reject-on-imported-changes are disabled rather than applied. R5 removes each guard by implementing the feature behind it, keeping R4's no-error / no-silent-loss invariant intact.*

This is **not a translation-layer correctness problem** (R4 solved that) and **not a read-fidelity problem** (R4.5 solves that). R5 is **editing completeness + document lifecycle**: making every construct a user can create in the editor round-trip as either clean authored content (new docs) or tracked edits (imported docs).

## 2. Requirements (owner-stated 2026-07-23; REQ-3 split 2026-07-28)

- **REQ 1 — Authored-doc lifecycle stays CLEAN.** Start from a blank editor → first save creates the SPE file clean (already works) → reopen later and keep editing → edits remain CLEAN (not tracked), because it is the user's own original document. *(Gap: G1 origin routing + G2 clean-apply.)*
- **REQ 2 — Imported-doc edits are TRACKED.** Launched from an existing/uploaded `.docx` → track-changes ON. **✅ Already met in R4 (baseline).**
- **REQ 3 — Both modes support headings, lists, tables, hyperlinks.**
  - **READ side** (rendering headings/lists/**numbering** faithfully on load/upload) → **owned by R4.5 (WS-2/WS-3).** Out of R5 scope.
  - **EDIT side** (applying heading/list/table/hyperlink changes — clean for authored, tracked for imported) → **R5 (G3/G4/G5).** R5's edit-side numbering (G3) reuses R4.5's numbering engine.

## 3. Feature set — the R5 gap ledger (code-grounded)

The authoritative, sized ledger lives in [`README.md`](README.md) (§"Gap ledger"). R5 scope = **G1, G2, G3, G4, G5, G7, G8, G9, G10, G11, G12** (11 gaps). **G6 is NOT R5** — it moved to R4.5 WS-1 (transient-mount projection unification / mammoth removal), already code-complete on the R4.5 branch. The row is retained in the README as a traceability stub only.

| # | Gap | REQ / driver | Primary path | Size | Key code touchpoints |
|---|---|---|---|---|---|
| **G1** | Cross-session authored-vs-imported **origin routing** (durable marker on create-on-save + returned by `LoadAsync`; client sends clean payload for reopened authored docs) | REQ 1 | Client + `ComposeService` | S–M | `ComposeService.LoadAsync`/`SaveAsync`; `ComposeWorkspace.tsx` `isTransientCreate` (`~:940–1205`) |
| **G2** | **Clean (non-tracked) apply mode** in the engine (emit plain runs, not `w:ins`/`w:del`) OR re-author authored docs from the content model each save | REQ 1 | `ComposeShadowPatchEngine` | M–L | `ApplyInsertText`/`WrapRunAsDeleted` (always tracked today) — add clean branch |
| **G3** | **`setBlockAttr` applier** for `Style`(heading), `ListOrdered`, `ListLevel`, `Alignment` as tracked `w:pPrChange`; client emits heading/list in `classifyStep` (today `defer-structural`) | REQ 3 (edit) | Engine + client interceptor | M | `ComposeShadowPatchEngine.cs` (throws for `setBlockAttr` today); `stepOperationInterceptor.ts`/`classifyStep`; **reuses R4.5 `NumberingComputationEngine`** |
| **G4** | **Table op** — new op type in the closed set + client capture + engine applier emitting tracked table structure (`w:tblPrChange`, row/cell `w:ins`) | REQ 3 (edit) | Op schema + client + engine | L | `ComposeOperation.cs`/`compose-operations.ts`; `ComposeShadowPatchEngine.cs`. **Hardest piece — schedule last.** |
| **G5** | **Hyperlink support** — `href` on `ComposeInlineRun` + `w:hyperlink` in `ComposeDocumentRenderer.BuildRun` (authored, S–M); hyperlink op + `link` in `ComposeMarkType` + engine applier (edit, M) | REQ 3 (edit) | Content model + renderer + op schema + engine | M–L | `ComposeDocumentRenderer.BuildRun` (authored path); engine applier (edit path) |
| **G7** | **Save-Version vs Save-New-Document** control (toolbar split-button) + create-vs-replace routing; covers the transient/upload path | Versioning UX | Client toolbar + `ComposeService` | S–M | `ComposeWorkspace.tsx` toolbar; `ComposeService` create-vs-replace. **Depends on R4.5 WS-1** for transient/upload doc identity |
| **G8** | **External-change refresh + remount banner** — wire the existing `POST /api/compose/document/{id}/check-changes` + `spe-doc-changed` webhook to a remount + non-blocking banner | Concurrency UX | Client mount lifecycle + existing endpoints | M | `ComposeEditor.tsx`/`ComposeWorkspace.tsx` mount effects; endpoints already exist, just unwired |
| **G9** | **Comment pane scroll-sync** — position-link the Comments pane to in-document anchor positions | Comments UX | Client | S–M | `ComposeCommentThread*` + editor scroll coordination |
| **G10** | **Document Profile re-run on Compose save** (+ reload + manual "Refresh Profile" button) so downstream analysis/search reflects edits | Dataverse profiling | `ComposeService` save path + Dataverse form script/process | M | `ComposeService` save (`~:843`); reuses **R4.5 WS-4** `paraId→legal-number` for precise citations |
| **G11** | **Track-changes toggle keeps pre-existing redlines visible** — toggling the user's own free-typed-edit overlay off must not hide imported/AI redlines (first-class marks). View-only, no persistence change | UX clarity | Client | S | `TrackChangesExtension.ts` (`:157–161`) + toolbar (`ComposeEditor.tsx:1859–1865`). From UAT BUG-B (confirmed **not** data loss) |
| **G12** | **Accept/reject imported tracked changes (ET-2 reconciliation)** — `acceptRevision`/`rejectRevision` ops addressed by revision **id**; engine resolves natively (accept-ins strips `w:ins` wrapper; accept-del removes run; reject = inverse). Also fixes imported-deletion end-of-paragraph re-anchor | Editing (tracked-change reconciliation) | Op schema + client interceptor + engine + `importedRevisions.ts` | M–L | `ComposeOperation.cs`/`compose-operations.ts`; `stepOperationInterceptor.ts`; `ComposeShadowPatchEngine.cs`; `importedRevisions.ts`. **UAT 2026-07-28 hit via accept-then-save (422)** — no longer optional |

Each gap's **exit criterion is removing its R4 guard** (re-enabling the disabled control) with a seam slice proving the construct round-trips and the no-error / no-silent-loss invariant still holds.

## 4. Architecture — how each gap maps to the engine (no new subsystems)

R5 adds **appliers, op types, client interceptors, and UX wiring** to the R4 architecture. It introduces **no new service, no new library, no new BFF endpoint family** (G8 wires endpoints that already exist).

- **Engine work (server, `ComposeShadowPatchEngine.cs`)**: clean-apply branch (G2); `setBlockAttr`→`w:pPrChange` applier (G3); table applier `w:tblPrChange` (G4); hyperlink applier (G5, edit path); `acceptRevision`/`rejectRevision` handlers (G12). All are **new appliers for the existing op spine** — the engine stays `byte[]`-in/`byte[]`-out, pure, no AI internals (ADR-013), no `Microsoft.Graph` above `SpeFileStore` (ADR-007).
- **Op schema (shared spine)**: add `table` op + `acceptRevision`/`rejectRevision` ops to the closed `ComposeOperation` catalog (`ComposeOperation.cs` ↔ `compose-operations.ts`) — extends, never forks (ADR-039).
- **Client interceptor (`stepOperationInterceptor.ts`/`classifyStep`)**: emit heading/list `setBlockAttr` (G3), table steps (G4), and revision ops (G12) that are `defer-structural` today.
- **Renderer (authored/clean path, `ComposeDocumentRenderer.BuildRun`)**: `w:hyperlink` emission (G5 authored side) — the one clean-authoring REQ-3 gap.
- **`ComposeService` (save/load orchestration)**: origin marker persist+return (G1); create-vs-replace routing for versioning (G7); profile re-trigger on save (G10).
- **Client shell (`ComposeWorkspace.tsx`/`ComposeEditor.tsx`)**: origin-based routing (G1); Save split-button (G7); external-change remount + banner (G8); comment scroll-sync (G9); track-changes-off redline visibility (G11).
- **Reuse from R4.5 (do NOT rebuild)**: `NumberingComputationEngine` (G3 renumber-on-edit); `paraId→legal-number` citation reference (G10 precise citations); the transient-mount server projection + stable doc identity (G7 upload-path coverage).

## 5. Non-functional requirements (inherited + R5 deltas)

- **NFR-01 Byte preservation** (inherited): every construct edit still leaves untouched OOXML subtrees byte-identical (corpus byte-diff harness — R4's 28/28 must not regress).
- **NFR-02 Placement determinism / no text-search** (inherited, I-7): every new applier resolves by `(paraId, runIndex, offset)` or revision id — **zero text-search in the write path**, including G12's revision resolution (by id, not content match).
- **NFR-03 Licensing** (inherited): MIT/permissive only; no TipTap Pro; no new runtime package for R5.
- **NFR-04 Publish size** (inherited): BFF ≤60 MB compressed; report absolute + delta vs ~46.11 MB post-R4 baseline per BFF task.
- **NFR-05 Facade purity** (inherited): `Services/Compose/` stays pure — no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest); no `Microsoft.Graph` above `SpeFileStore` (ADR-007).
- **NFR-06 Seam DoD** (inherited, ADR-038): every save/load/apply change carries a through-the-wire `WebApplicationFactory` seam slice in `tests/integration/seam/**`; no `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests.
- **NFR-07 Word-native output** (inherited): results open in Word/Word-for-web with real accept/reject redlines + threaded comments; G12's reconciliation produces Word-valid revision state.
- **NFR-08 No-regression of guards** (R5-specific): each removed guard must not reintroduce a user-triggerable error or silent data-loss path; the R4 op-log-preservation safety net stays in place until the feature that replaces the guard is proven by a seam slice.
- **NFR-09 Downstream-consumer safety** (R5-specific): the save/versioning/redline contract changes (G1/G2/G7/G10/G12) MUST NOT regress `analysis-hub-r1`'s reopen-restore or clean-retirement parity assumptions (see §10.2).

## 6. Phasing (WBS sketch — full decomposition is `/project-pipeline`'s job)

> **Hard prerequisite: R4.5 lands to master first.** G3 (numbering engine) and G7 (transient-mount identity) build on R4.5 outputs. Sequence R5 pipeline after R4.5 merge.

Mirrors the README "Suggested R5 sequencing":

- **Phase 0 — Gate**: spec; confirm R5-D1…D5; **G2 clean-apply spike** (re-author-from-model vs engine clean branch) on a born-in-editor corpus doc; the op-schema extension for G4/G12; ensure R4.5 has merged (dependency check).
- **Phase 1 — Edit-path structural ops (op-schema wave)**: G3 alignment (S) → G3 heading/list (M, reuse R4.5 numbering) → G12 accept/reject-revision (M–L) → G4 tables (L). Grouped because they share the op-schema + engine-applier surface.
- **Phase 2 — Authored-doc lifecycle**: G1 origin routing + G2 clean-apply (REQ 1 — highest user-visible value) → G7 versioning UX (split-button + upload-path coverage on R4.5 identity).
- **Phase 3 — Concurrency + UX**: G8 external-change refresh banner → G9 comment scroll-sync → G11 track-changes-off redline visibility → G5 hyperlinks (both paths).
- **Phase 4 — Profiling + hardening + cutover**: G10 profile re-run on save (triage complexity first) → corpus byte-diff no-regression → publish-size → deploy + operator UAT.

## 7. Placement & New Components (per CLAUDE.md §10 / §11)

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

BFF=Y → Placement Justification required per component; ≤60 MB publish ceiling per task; **run `/conflict-check` before every BFF PR** (overlaps `spaarkeai-compose-fidelity-r4.5`, `spaarkeai-compose-r1/r2/r3`, `spaarke-ai-architecture-redesign-r2`, and the `Spaarke.Compose.Components` surface shared with `analysis-hub-r1`).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Engine **clean-apply branch** (G2) | `ComposeShadowPatchEngine.ApplyInsertText`/`WrapRunAsDeleted` (tracked-only today) | **Extend** the engine with a mode flag — not a new class. | Without it, a reopened authored doc's own edits render as tracked changes (REQ-1 wart persists). |
| `setBlockAttr` applier (G3) | Engine `StructuralOpNotYetImplemented` seam | **Extend** — fill the existing seam; reuse R4.5 `NumberingComputationEngine`. | Heading/list/alignment edits on loaded docs stay `defer-structural` / 422-guarded (disabled controls). |
| **Table op + applier** (G4) | Closed `ComposeOperation` catalog | **Extend** the catalog with one op type + applier. | Table edits silently dropped; the table control stays disabled on loaded docs. |
| Hyperlink op + `w:hyperlink` render (G5) | `ComposeMarkType`, `ComposeDocumentRenderer.BuildRun` | **Extend** the mark set + renderer. | Hyperlinks silently lost (`unrepresentable`); control disabled in both modes. |
| `acceptRevision`/`rejectRevision` ops (G12) | Closed op catalog; `importedRevisions.ts` | **Extend** with id-addressed revision ops. | Accepting an imported tracked change + saving fails with 422 (`TrackedChangeReconciliationUnsupported`). |
| **Origin marker** (G1) | `sprk_document` fields / SPE metadata | **Extend** the record/metadata with one durable flag — no new table. | Reopened authored docs flip to the tracked path (only discriminator today is "has an SPE id"). |

Everything else (G7 toolbar, G8 banner, G9 scroll-sync, G10 hook, G11 toggle) is **modify/extend** of existing client/service surface — no new component.

## 8. ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR / prior decision | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-039 (closed catalogs / frozen engine)** | Grounded execution over a closed op catalog | G4/G12 add op types | **C (comply)** | New ops **extend** the closed `ComposeOperation` catalog under version control; no side channel, no new AI dispatch endpoint. The AI-redline path stays envelope-only. |
| **R4 D5 (one byte-author) / I-5** | "One byte-author writes the package" | R5 keeps the renderer (clean) + engine (tracked) split for G1/G2 | **A (project-scoped exception, already granted in R4 as C-revised)** | Two byte-authors per *path* is the shipped R4 decision (036/037). R5 honors it; it does not force authored origination through the op log. Cited, not re-opened. |
| **ADR-013 (AI facade)** | No AI internals in `Services/Compose/` | None — R5 appliers are pure OOXML | **C (comply)** | Engine stays `byte[]`-in/`byte[]`-out; Tier-1 NetArchTest enforces. |
| **ADR-007 (Graph isolation)** | No `Microsoft.Graph` above `SpeFileStore` | G7/G10 touch the SPE save path | **C (comply)** | SPE hop stays in the endpoint/facade layer; the profile re-trigger is a domain event, not a Graph call in the engine. |
| **ADR-038 (testing)** | Seam DoD; banned mock/DI/ctor tests | New appliers + save-path changes | **C (comply)** | Seam slices in `tests/integration/seam/**` for every applier + save/load change; corpus byte-diff no-regression. |

> Additional tensions may surface at design-to-spec / Phase 0; handle via §6.5 (surface as A/B/C).

## 9. Success criteria (graduation)

1. [ ] **REQ 1** — a reopened authored doc's edits are CLEAN (no tracked changes); imported docs still track. Verify: seam slice + UAT.
2. [ ] **REQ 3 (edit)** — headings, lists, tables, hyperlinks apply as clean (authored) or tracked (imported) edits; each R4 guard removed. Verify: per-construct seam slice + control re-enabled.
3. [ ] **G12** — accepting/rejecting an imported tracked change saves successfully (no 422). Verify: ET-2 seam slice on a corpus doc with pre-existing revisions.
4. [ ] **Versioning (G7)** — Save-Version updates in place; Save-New forks; transient/upload docs no longer mint duplicates. Verify: UAT (the 8-duplicate scenario) + seam slice.
5. [ ] **Concurrency/UX (G8/G9/G11)** — external-change remount+banner; comment scroll-sync; track-changes-off keeps imported redlines visible. Verify: UAT.
6. [ ] **Profiling (G10)** — Compose save re-runs the Document Profile. Verify: save → profile-updated assertion (or documented complexity deferral per R5-D5).
7. [ ] **No regression** — corpus byte-diff still 28/28; publish ≤60 MB; ADR + seam tests green; no new user-triggerable error path introduced. Verify: harness + `dotnet publish` + CI.

## 10. Cross-project coordination (BINDING — owner-directed)

R5 shares the Compose surface with two active siblings. This section is the coordination contract; it is mirrored into `spec.md` at `/design-to-spec` and enforced by `/conflict-check` at PR time.

### 10.1 `spaarkeai-compose-fidelity-r4.5` — HARD sequencing + two contended files

**Canonical source:** [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md) (code-grounded collision analysis, 2026-07-28). Read it before opening any Compose PR.

- **Direction: aligned, no fundamental design conflict.** R4.5 = *reading* a legal doc with perfect fidelity + making it *referenceable*; R5 = *editing* it with full formatting fidelity. R4.5 does **not** touch `ComposeShadowPatchEngine`/byte-authoring (its ADR-tension T-3 = comply with the R4 two-author split) — the engine is cleanly R5's.
- **Sequencing (binding): R4.5 must land to master BEFORE R5 starts coding — ✅ SATISFIED as of 2026-07-28.** R4.5 has **merged to master** (its WS-2/WS-3/WS-4 commits + wrap-up/test-diet gates are on `master`; `CitationResolver.cs` and the numbering engine are in `Services/Compose/`). The R5 execution gate is therefore **cleared** — R5 may pipe to execution, not just design/spec. R5 gaps still **rebase onto** R4.5 outputs (now present on master): **G7** (needs WS-1 transient-mount projection identity — HARD), **G1** (its `isTransientCreate` discriminator is the exact block WS-1 rewrote — HARD, README under-stated this), **G3** (reuse WS-3 `NumberingComputationEngine`, do NOT fork — reconciled in R4.5 FR-14), **G2** (fidelity-bounded by the projection round-trip WS-2/3 hardened), **G8/G9** (hook the mount effects WS-1 restructured), **G10** (reuse WS-4 `paraId→legal-number`).
- **G6 is done and out of R5.** R4.5 WS-1 **already deleted the Compose `mammoth` fallback and unified all transient mounts through the server projection.** R5 must NOT re-scope G6.
- **⚠️ The docxBridge hazard (put in every relevant R5 task):** `docxBridge.ts` exports BOTH `docxToTipTapHtml` (mammoth READ — R4.5 deleted) AND `buildContentModel` + `stampParaIds` + paraId helpers (WRITE/save — **G1/G2/G7 depend on these**). **R5 must NOT "remove the mammoth module" or delete `docxBridge.ts`** — that breaks R5's own clean-authoring/versioning path. R4.5 correctly deleted only the read function.
- **Two HARD merge-conflict files (R4.5-owned-FIRST; R5 rebases):**
  | File | R4.5 change | R5 need |
  |---|---|---|
  | `ComposeService.cs` | WS-1 upload projection + WS-4 persist `paraId→number` in Load/Save | G1 origin marker (`LoadAsync`), G7 create-vs-replace, G10 profile trigger |
  | `ComposeWorkspace.tsx` | WS-1 upload/browse hydrate `projection` (`mountTransient` sites) | G1 transient/save routing (same `isTransientCreate` region), G7 toolbar, G8 mount |
  Soft-contended (mostly different regions): `ComposeEditor.tsx`, `ComposeEndpoints.cs`, `docxBridge.ts`.
- **Deploy contention:** both surfaces deploy to the shared `sprk_spaarkeai` web resource + `spaarke-bff-dev` ("last deploy wins" — R4 already hit this). **Preferred order: land R4.5 to master, then R5 builds/deploys from master-with-R4.5.**
- **⚠️ Reciprocal-note gap:** the COORDINATION note asks for a mirror note in `projects/spaarkeai-compose-fidelity-r4.5/notes/`; it was never created (R4.5 self-navigated the docxBridge hazard correctly anyway). Since R4.5 is code-complete this is low-risk, but note it so nobody assumes bidirectional wiring exists.

### 10.2 `ai-advanced-capabilities-analysis-hub-r1` — surface-sharing + behavioral downstream consumer

**Status:** PLANNED, not started, not merged (28 POMLs generated; blocked on a human Dataverse-schema gate). **Low direct code-conflict risk** — R5 can land first — but a **real behavioral coupling.** Shares branch ancestry with the R5 planning docs (`git log` shows the analysis-hub branch sits on top of the compose-r5 doc commits).

- **It shares the Compose surface, it doesn't just call it.** The Analysis working surface **is** the SpaarkeAi three-pane that hosts Compose ("There is ONE Analysis experience — the SpaarkeAi three-pane … Assistant + Compose three-pane"). It **modifies shared Compose components**: Task 041 threads `activeWorkType` through `Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` and consumes `ComposeAiToolbar.tsx` `getToolsForSurface`. Its Phase-0 gate (Task 001) **rewrites the `ConversationPane.compose-*` + `three-pane-compose-coordination` e2e tests.**
- **Contended client files (Medium–High):** `ComposeEditor.tsx`, `ComposeAiToolbar.tsx`, `ConversationPane` compose-routing (`composeReviseRouting.ts`/`composeDraftRouting.ts`), the SpaarkeAi three-pane shell + `launch-resolver.ts`, and the `ConversationPane.compose-*` e2e tests. **R5's G8/G9/G11 client work and G7 toolbar work touch this exact surface** — coordinate merge order; `/conflict-check` catches file-level overlap once both open PRs, but NOT the region-level contention.
- **Disjoint BFF endpoints:** analysis-hub's BFF work is in `Services/Ai/`/`ChatEndpoints`/`AnalysisEndpoints` + a new `/api/ai/analysis/fork` — **no `/api/compose/**` reference found.** R5's `Services/Compose/` BFF work does not collide with analysis-hub's BFF work.
- **Behavioral coupling (the real risk — NFR-09):** analysis-hub is a **downstream consumer of Compose save/versioning/redline behavior**, which R5 is actively changing. Its clean retirement of `sprk_analysisworkspace` **assumes Compose already provides auto-save-to-SPE, accept/reject redlines, and export** (design-discussion §13.6). Its FR-09/FR-11 reopen-restore assumes a stable Compose save/version contract over the shared `sprk_document`/SPE driveItem. **R5's G1/G2/G7/G10/G12 changes land underneath that assumption — R5 MUST NOT regress analysis-hub's reopen-restore or retirement parity** (NFR-09). G12 (accept/reject-revision) in particular hardens the very redline-reconciliation behavior analysis-hub's retirement parity depends on — a positive coupling, but it must not break the existing accept/reject the parity claim relies on.
- **`sprk_chathistory` shared-writer note:** analysis-hub Task 062 plans to retire the `sprk_chathistory` read; `ChatEndpoints`/Insights also write it. R5 does not touch it, but flag it so a shared-column deletion isn't assumed safe.
- **Action for `/design-to-spec`:** add `analysis-hub-r1` to R5's Compose coordination clause (analysis-hub's own `PLAN.md:41` lists `compose-r1..r4` but **not R5** — close that gap reciprocally when analysis-hub pipes to execution), and register R5 in `projects/INDEX.md` so `/conflict-check` surfaces the `Spaarke.Compose.Components` + `ConversationPane` overlap.

## 11. Dependencies / Prerequisites

- **✅ CLEARED — `spaarkeai-compose-fidelity-r4.5` is merged to master** (transient-mount projection + numbering engine + `CitationResolver`, as of 2026-07-28). The former execution-blocking dependency is satisfied; Phase 0's merge check is a confirm-only step.
- **New worktree**: `spaarkeai-compose-r5` (created by `/project-pipeline`). Register in `projects/INDEX.md` (hot-path BFF=Y, SpaarkeAi=Y) with the coordination watch-list from §10.
- **Portfolio**: register a Project Issue under Epic #421 (SPAARKE AI), mirroring R4's #679 pattern (R5 has no Project Issue yet).
- **Consume `Services/Ai/PublicContracts/` seams only — NO fork of `Services/Ai/`** (`spaarke-ai-architecture-redesign-r2` is sole owner); engine frozen, no new AI dispatch endpoint (ADR-039).
- **Corpus**: reuse R4's fidelity corpus (CIPO patent w/ pre-existing revisions is the G12 ET-2 test doc) + a born-in-editor doc for the G2 clean-apply spike.
- Zero new runtime package expected (`DocumentFormat.OpenXml` already present; publish baseline ~46.11 MB).

## 12. Open questions for `/design-to-spec` (seeded so the interview is fast)

- **Q1 (BLOCKING)**: Confirm R5-D1…D5, especially **R5-D2** (clean-apply = engine branch vs re-author-from-model — the G2 spike decides) and **R5-D5** (G10 profile re-run stays in R5 vs defers to a profiling project if complexity is high).
- **Q2 (RESOLVED)**: The **R4.5-lands-first** gate is satisfied (R4.5 merged to master 2026-07-28) — R5 may proceed through spec *and* execution. No longer blocking.
- **Q3 (IMPORTANT)**: **Origin marker (G1)** home — a durable `sprk_document` Dataverse field vs SPE item metadata? (Affects schema + `LoadAsync` contract. Recommendation: Dataverse field for queryability.)
- **Q4 (IMPORTANT)**: **G4 tables** scope for R5 — full tracked table structure (`w:tblPrChange` + row/cell tracking) vs a reduced first slice (cell-content edits only, structural table ops deferred)? It is the L-sized long pole.
- **Q5 (IMPORTANT)**: **G12** revision-op granularity — accept/reject single revision by id only, vs also accept-all/reject-all batch ops in R5?
- **Q6 (COORDINATION)**: Confirm deploy order (R4.5 → master → R5 builds from master-with-R4.5) and whether R5 should author the missing reciprocal coordination note into R4.5's notes folder as a Phase-0 courtesy task.

---
*Design document for `/design-to-spec`. Backlog source: [`README.md`](README.md). Coordination source: [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md) + the 2026-07-28 analysis-hub-r1 surface audit. R5 = editing completeness on the R4 Shadow Document Architecture; it re-litigates none of R4's D1–D5 / I-1–I-7 and depends on R4.5 landing first.*
