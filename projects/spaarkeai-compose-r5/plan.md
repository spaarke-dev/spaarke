# Spaarke Compose R5 — Implementation Plan (PLAN.md)

> **Status**: Ready for `/task-create` decomposition
> **Created**: 2026-07-28 by `/project-pipeline`
> **Source**: [`spec.md`](spec.md) → [`design.md`](design.md) → [`README.md`](README.md) gap ledger → [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md)
> **Governing ADR**: [ADR-049 — Compose Shadow Document Architecture](../../.claude/adr/ADR-049-compose-shadow-document.md)
> **Portfolio**: [Project #695](https://github.com/spaarke-dev/spaarke/issues/695) · Epic #421

---

## Executive Summary

R5 delivers **editing completeness** on R4's Shadow Document Architecture: it removes each R4 *guard* (a disabled control / deferred step) by building the feature behind it, across 11 code-grounded gaps (G1–G5, G7–G12), while holding R4's no-error / no-silent-loss invariant intact. Work is **additive appliers + op-catalog extensions + client interceptors + UX wiring** on the existing engine — no new service, library, or BFF endpoint family. Two Phase-0 human gates (G1 Dataverse-schema approval, G2 clean-apply spike) precede the implementation waves.

---

## Architecture Context — Discovered Resources (project-pipeline Step 2)

### Governing + applicable ADRs
- **ADR-049** (canonical Compose Shadow Document) — D1–D5, I-1–I-7, two-author split, R4.5 F-1…F-5; names G3 as the sanctioned live-renumber follow-on.
- **ADR-039/040** (frozen engine / closed op catalog / no new AI dispatch) · **ADR-013** (AI facade purity, Tier-1 NetArchTest) · **ADR-010** (Patch Engine = stateless singleton) · **ADR-007** (no Graph above `SpeFileStore`) · **ADR-038** (seam DoD; banned mock/DI/ctor tests) · **ADR-009** (`IDistributedCache` re-anchor state) · **ADR-028** (`@spaarke/auth` client fetch).

### Code touchpoints (verified 2026-07-28)
| Concern | Location |
|---|---|
| Tracked appliers (extend for clean/structural) | `Services/Compose/ComposeShadowPatchEngine.cs` — `ApplyInsertText:314`, `WrapRunAsDeleted:942`, `setBlockAttr` throw `:249` / enum `:1438` |
| Clean byte-author (hyperlink authored path) | `Services/Compose/ComposeDocumentRenderer.cs` — `BuildRun:366`, `AssembleParagraph:346` |
| Save/load orchestration | `Services/Compose/ComposeService.cs` — `LoadAsync:227`, `SaveAsync:570`, create-on-save `~:789`, profile fire-and-forget `~:895–942` |
| Closed op catalog (extend: table, acceptRevision, rejectRevision) | `Services/Compose/Operations/ComposeOperation.cs` — `[JsonDerivedType]` `:148–157` (10 ops) |
| Numbering (G3 reuse — **nested `internal`**) | `Services/Compose/ComposeDocxProjectionBuilder.cs:~1357` — `NumberingComputationEngine.Compute` (extract-vs-reference decision) |
| Citation (G10 reuse) | `Services/Compose/CitationResolver.cs` — `Resolve`/`ResolveCitation` (standalone, static) |
| G8 endpoints (registered; complete delivery + client legs) | `Api/ComposeEndpoints.cs` — webhook `:209`, check-changes `:233` |
| Client op mirror | `types/compose-operations.ts` — `COMPOSE_OPERATION_TYPES:54–64` (in sync) |
| Client step classifier | `widgets/stepOperationInterceptor.ts` — `classifyStep:442`, `defer-structural`/`unrepresentable` |
| Client shell / transient save | `widgets/ComposeWorkspace.tsx` — `isTransientCreate:975`, `triggerSave:968` |
| Editor / toggle / comments | `widgets/ComposeEditor.tsx` — track-changes toggle `~:2375`, comment threads `:1798–1812`, deferred-notice `:1650–1665` |
| Track-changes decoration flip (G11) | `widgets/marks/TrackChangesExtension.ts:157–161` |
| Imported revisions (G12) | `widgets/importedRevisions.ts` — `applyImportedRevisions:272` |
| **docxBridge write helpers (DO NOT DELETE)** | `utils/docxBridge.ts` — `buildContentModel:224`, `stampParaIds:92`, paraId helpers `:189/:210` (`docxToTipTapHtml` gone ✓) |
| R4 guard sites (re-enable) | `widgets/ComposeFormatToolbar.tsx` — hyperlink `:635`, structural `:537/:546/:563/:572/:581`, table `:654` |
| Seam-test pattern (canonical) | `tests/integration/seam/Compose/` — e.g. `ComposeShadowPatchEngineByteDiffSeamTests.cs`, `ComposePatchEngineSaveSeamTests.cs`, `ComposeCorpusFixtureLocator.cs` |
| Unit tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` — e.g. `ComposeOperationSchemaTests.cs`, `ComposeServiceCreateOnSaveTests.cs` |

### Reuse contracts (do NOT rebuild — R4.5, on master)
- `NumberingComputationEngine.Compute` (G3 renumber) · `CitationResolver` (G10 citations) · transient-mount projection identity (G7).

### Skills in play
`task-execute` (every task) · `code-review` + `adr-check` (Step 9.5 gates — unconditional for BFF/test-modifying tasks) · `conflict-check` (before every BFF PR) · `bff-deploy` + `code-page-deploy` (Phase 4 deploy) · `ui-test` (client tasks) · `test-diet` (wrap-up).

---

## Phase Breakdown (WBS — task-create decomposes this)

Sizes: S ≈ ≤1d, M ≈ 2–4d, L ≈ 1–2wk. Model tiers: `sonnet@high` default; `opus`/`xhigh` flagged for engine-heavy / high-blast-radius work.

### Phase 0 — Gate (blocking; some items are HUMAN gates)
Deliverables:
- **P0-1 — R4.5 merge confirmation** (S, `sonnet@high`): confirm `NumberingComputationEngine` + `CitationResolver` present on branch; corpus + seam harness green baseline. *(Confirm-only — gate cleared.)*
- **P0-2 — 🔔 G1 Dataverse origin field (HUMAN GATE)** (S design + human approval): author `sprk_document.sprk_composeorigin` choice (`Authored`/`Imported`); default at create-on-save. Blocks G1 code. Uses `dataverse-create-schema`.
- **P0-3 — G2 clean-apply spike** (M, `opus@xhigh`): decide R5-D2 (engine clean-apply branch vs re-author-from-content-model) on a born-in-editor corpus doc; write decision note; blocks G2 code. Byte-diff evidence required.
- **P0-4 — Op-schema extension design** (M, `opus@high`): design the `table` + `acceptRevision`/`rejectRevision` op shapes for the closed catalog (server `ComposeOperation.cs` ↔ client `compose-operations.ts`); dependency for G4/G12.
- **P0-5 — NumberingComputationEngine reuse decision** (S, `sonnet@high`): extract-to-standalone vs reference-nested; if extract, pure refactor keeping projection byte-identical. Dependency for G3 heading/list.
- **P0-6 — (optional courtesy) reciprocal R4.5 coordination note** (S): drop the mirror note into `projects/spaarkeai-compose-fidelity-r4.5/notes/`.
- **P0-7 — Register in `projects/INDEX.md`** (S): add R5 row (BFF=Y, SpaarkeAi=Y) with coordination watch-list.

### Phase 1 — Edit-path structural ops (op-schema + engine-applier wave)
Deliverables (share the op-schema/engine surface — sequence within phase):
- **P1-1 — G3 alignment applier** (S, `sonnet@high`): `Alignment`→`w:pPrChange` (tracked) / clean; client already emits alignment; re-enable ET-1 guard. Seam slice.
- **P1-2 — G3 heading/list applier** (M, `opus@high`): `Style`/`ListOrdered`/`ListLevel`→`w:pPrChange`; client `classifyStep` emits heading/list; reuse NumberingComputationEngine (per P0-5); re-enable SDL-1/2 guards. Seam slices.
- **P1-3 — G12 accept/reject-revision (single-by-id)** (M, `opus@xhigh`): add ops to catalog (per P0-4); engine resolves by revision id (accept-ins strips wrapper, accept-del removes run, reject inverse); fix imported-deletion end-of-para re-anchor. ET-2 seam slice on CIPO corpus. **No text-search (NFR-02).**
- **P1-4 — G12 accept-all/reject-all batch** (M, `opus@high`): batch ops with deterministic reconciliation ordering; batch seam cases. Depends on P1-3.
- **P1-5 — G4 tables (full tracked structure)** (L, `opus@xhigh`): `table` op (per P0-4) + client capture + engine applier emitting `w:tblPrChange` + row/cell `w:ins`/`w:del`; re-enable SDL-3 guard. **Scheduled last.** Seam slice on table corpus.

### Phase 2 — Authored-doc lifecycle (REQ 1 — highest user-visible value)
- **P2-1 — G1 origin routing** (M, `sonnet@high`): `ComposeService.LoadAsync`/`SaveAsync` persist+return origin marker (per P0-2); client routes reopened authored docs onto clean payload (`isTransientCreate` region). Rebase onto post-R4.5 `ComposeService.cs`/`ComposeWorkspace.tsx`. Seam slices (both origins).
- **P2-2 — G2 clean-apply implementation** (M–L, `opus@xhigh`): implement the P0-3 decision; born-in-editor cross-session edits produce clean OOXML. Byte-diff + seam slice.
- **P2-3 — G7 versioning split-button** (M, `sonnet@high`): toolbar Save-Version vs Save-New; `ComposeService` create-vs-replace covering transient/upload (R4.5 WS-1 identity); the 8-duplicate scenario fixed. Seam slice + UAT.

### Phase 3 — Concurrency + UX
- **P3-1 — G8 external-change refresh + banner** (M, `sonnet@high`): complete webhook delivery leg + client remount/banner on the existing endpoints. UAT + mount coverage.
- **P3-2 — G9 comment scroll-sync** (S–M, `sonnet@high`): position-link Comments pane to anchors. UAT.
- **P3-3 — G11 track-changes-off redline visibility** (S, `sonnet@high`): keep imported/AI redlines rendered when overlay off (`TrackChangesExtension.ts:157–161`); view-only. Client test/UAT.
- **P3-4 — G5 hyperlinks (both paths)** (M–L, `opus@high`): authored `w:hyperlink` in `BuildRun` + `href` on `ComposeInlineRun`; hyperlink op + `link` mark + engine applier; re-enable SDL-4/5 guard. Seam slices both paths.

### Phase 4 — Profiling + hardening + cutover
- **P4-1 — G10 profile re-run (reload + manual button)** (M, `sonnet@high`): save-hook trigger already exists — add reload/onload + "Refresh Profile" button; reuse `CitationResolver`. R5-D5 complexity-defer escape hatch documented if needed.
- **P4-2 — No-regression + publish-size hardening** (S–M, `sonnet@high`): corpus byte-diff 24/24; `dotnet publish` ≤60 MB (report vs ~46.11 MB); ADR + seam green.
- **P4-3 — Deploy + operator UAT** (M, `sonnet@high`): build/deploy from master-with-R4.5 to shared `sprk_spaarkeai` + `spaarke-bff-dev` (coordinate deploy timing); operator UAT against §9 graduation criteria.
- **P4-4 — 090 wrap-up** (S, `sonnet@high`): `/test-diet` reconciliation; README→Complete; lessons-learned; portfolio sync/archive.

---

## Dependencies & Critical Path

```
P0-2 (HUMAN schema gate) ─────────────► P2-1 (G1) ──► P2-3 (G7)
P0-3 (G2 spike) ──────────────────────► P2-2 (G2)
P0-4 (op-schema design) ──► P1-3/P1-4 (G12) , P1-5 (G4)
P0-5 (numbering reuse) ───► P1-2 (G3 heading/list)
P1-1 (G3 align) ─► P1-2 ─► P1-3 ─► P1-4 ─► P1-5 (Phase-1 op-schema wave, in order)
Phase 1 + Phase 2 ─► Phase 3 ─► Phase 4 (P4-2 no-regression gates deploy)
```

- **Critical path (longest):** P0-4 → P1-3 → P1-4 → P1-5 (G4 tables, the L long pole) → P4-2 → P4-3.
- **Human-gated:** P0-2 blocks all G1 work; P0-3 blocks G2. These can proceed in parallel with the Phase-1 op-schema wave (which depends only on P0-4/P0-5).
- **Parallel-safe candidates:** P3-1/P3-2/P3-3 touch mostly different client files (coordinate `ComposeEditor.tsx` regions). P1-1 must precede P1-2 (same applier surface).

## Cross-Project Coordination (BINDING — see spec §Coordination)
- **R4.5** (merged): rebase G1/G7/G10 onto post-R4.5 `ComposeService.cs`; rebase G1/G7/G8 onto post-R4.5 `ComposeWorkspace.tsx`. **Never delete `docxBridge.ts`.** Deploy from master-with-R4.5.
- **analysis-hub-r1** (NFR-09): downstream consumer of Compose save/versioning/redline; shares `Spaarke.Compose.Components` + `ConversationPane` routing/e2e. R5's G1/G2/G7/G10/G12 MUST NOT regress its reopen-restore / retirement parity. `/conflict-check` before every BFF + shared-client PR.

## Non-Functional Gates (every applicable task)
NFR-01 byte-preservation (24/24) · NFR-02 no text-search (I-7) · NFR-03 MIT-only, no new package · NFR-04 publish ≤60 MB (vs ~46.11) · NFR-05 facade purity (Tier-1 NetArchTest) · NFR-06 seam DoD · NFR-07 Word-native output · NFR-08 guard no-regression · NFR-09 downstream parity.

## Estimated Effort
~18 implementation tasks + gates/deploy/wrap-up ≈ **22–26 tasks**. Rough range **4–7 weeks** single-threaded; G4 tables (L) is the long pole. Phases 1 & 2 parallelizable after Phase-0 gates clear.

## References
[`spec.md`](spec.md) · [`design.md`](design.md) · [`README.md`](README.md) · [`notes/COORDINATION-with-r4.5.md`](notes/COORDINATION-with-r4.5.md) · [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) · [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) · [`projects/INDEX.md`](../INDEX.md)
