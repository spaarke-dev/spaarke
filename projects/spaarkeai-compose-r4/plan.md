# Implementation Plan — Spaarke Compose R4 (Shadow Document Architecture)

> **Source**: [`spec.md`](spec.md) · [`design.md`](design.md)
> **Created**: 2026-07-22 by `/project-pipeline`
> **Cutover**: Hard-replace, gated by Phase 0 proof.

---

## Architecture Context

**The shape in one sentence**: OOXML stays the server-authoritative source of truth; the editor edits a lossy *view*; every edit maps deterministically to `(paraId, runIndex, run-local-offset)` in the server model and is applied surgically by one Patch Engine — no text-search anywhere in the write path.

### Invariants (binding — every task inherits, from design §3)

- **I-1** One authoritative model = the real OOXML package (retained; never wholesale-regenerated for a loaded doc).
- **I-2** Server-authoritative — the client never authors `.docx` bytes.
- **I-3** Stable addressing — every editable node carries `w14:paraId`; edits reference it, never text-search, never absolute position.
- **I-4** Edits are operations, applied surgically; untouched XML subtrees byte-identical after save.
- **I-5** One byte-author — a single Patch Engine writes the package.
- **I-6** Client is a view + controller — TipTap renders the projection and emits operations.
- **I-7** No text-search anchoring in the write path (fuzzy content-match survives ONLY as a below-threshold "surface-as-comment" last resort on reload/cross-Word-session re-anchor).

---

## Discovered Resources

### Applicable ADRs

| ADR | Path | Relevance to R4 |
|---|---|---|
| **ADR-013** | `.claude/adr/ADR-013-ai-architecture.md` | AI facade discipline — `Services/Compose/` MUST NOT inject `IOpenAiClient`/executor/routing types; AI returns operations via `PublicContracts`. Tier-1 NetArchTest enforces. **Path C (comply).** |
| **ADR-007** | `.claude/adr/ADR-007-spefilestore.md` | Graph isolation — no `Microsoft.Graph` type above `SpeFileStore`; Patch Engine is `byte[]`-in/`byte[]`-out. **Path C.** |
| **ADR-009** | `.claude/adr/ADR-009-redis-caching.md` | Redis-first — version/re-anchor summary state via `IDistributedCache`, not `IMemoryCache`. |
| **ADR-010** | `.claude/adr/ADR-010-di-minimalism.md` | DI minimalism — Patch Engine is a stateless concrete singleton. |
| **ADR-028** | `.claude/adr/ADR-028-spaarke-auth-architecture.md` | Auth v2 — client save/generate fetches use `@spaarke/auth` (`useAuth`/`authenticatedFetch`); no custom token props on the editor. |
| **ADR-038** | `docs/adr/ADR-038-testing-strategy.md` | Integration-heavy; seam DoD (`tests/integration/seam/**`); banned `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests. **Path C.** |
| **ADR-039 / ADR-040** | `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`, `ADR-040-session-ledger.md` | Grounded execution / session ledger — AI redline path stays envelope-only; **no new dispatch endpoint, engine frozen.** |

### KEEP + extend (from `notes/as-built-inventory.md`)

- `ComposeDocxProjectionBuilder.cs` — the custom `w:p`→HTML mapper (replaced mammoth in Phase 1). **Extend** to emit the offset-addressing table + opaque atoms.
- `ComposeDocxProjection.cs` — projection contract. Extend with the addressing table.
- `ParaIdPreParser.cs` / `AnnotationReanchorService.ExtractParaIds` — document-order paraId extraction (incl. table cells). Reused for O(1) node resolution.
- `ComposeBaselineParaIdStamper.cs` — mints + stamps paraIds. **Extend** to persist ids into the shadow package on ingest.
- `AnnotationReanchorService.cs` — fuzzy re-anchor bands + ambiguity guard. Becomes the **last-resort** cross-Word-session / stale-base fallback.
- Client `paraIdExtension.ts`, `docxBridge.ts` (`stampParaIds`/`captureParaIdSnapshot`) — id-carry kept; snapshot purpose changes (op log, not paragraph diff).
- SPE facade + `SpeDocumentViewer` — store + open-in-Office launch surface. Unchanged.
- `DocxAnnotationWriter.cs` EDGE-1…4 native-OOXML wisdom — **migrate** into the Patch Engine before retiring the class.

### RIP OUT (from `notes/as-built-inventory.md`)

- `DocxAnnotationWriter.LocateTarget` (whole-doc text-search — 422 root cause).
- `DocxAnnotationWriter` as the write path.
- `ComposeParagraphRedlineSynthesizer` (paragraph-diff).
- Client `collectEditedParagraphs` / `{paraId,text}` export (`docxBridge.ts`).
- Residual `mammoth` fallback mounts.
- `DocxAnnotation.TargetText` text-anchor contract.

### Prior art to borrow (study-only, from `notes/bridge-prior-art.md`)

| Sub-problem | Borrow from | License |
|---|---|---|
| Client op capture + rebasing (+ AI bookmark) | ProseMirror `transform` (Step/StepMap/Mapping), `prosemirror-changeset` | MIT |
| Operation-schema shape | Slate op discriminated-union + `Path.transform` | MIT |
| OOXML-as-truth projection reference | frozen fork `sorenlouv/docx-editor` (`@sqren/docx-editor@1.0.3`) — study/vendor-and-own only | Apache-2.0 |
| Anchor-survives-edits theory | Yjs `RelativePosition` / Peritext essay | MIT |
| Server-side .NET surgical patch | Open-XML-SDK + **Docxodus** (MIT, active) — A/B candidate | MIT |

> ⚠️ **EigenPal correction**: the official `eigenpal/docx-editor` repo is a **closed facade** (stubs that throw). No runtime dependency on anything EigenPal ships. Docxodus (server) is the one surviving real vendor option, decided by the Phase-0 A/B.

### Skills likely invoked during execution

`task-execute`, `code-review`, `adr-check`, `conflict-check` (before every BFF PR), `bff-deploy` / `code-page-deploy` (Phase 6), `context-handoff`, `test-diet` (wrap-up).

---

## Placement Justification (per CLAUDE.md §10 / §11)

**Hot-path**: BFF=Y (`Services/Compose/`), SpaarkeAi=Y (Compose widget surface). Publish ceiling ≤60 MB compressed applies per BFF task (baseline ~49.63 MB incl. PDBs). Zero new runtime package expected (`DocumentFormat.OpenXml` already present; Docxodus only if the Phase-0 A/B selects it — verify size delta then).

New components are all **consolidations or extensions**, not additive scope (spec §New Components three-question gate): `ComposeShadowPatchEngine` **replaces** both legacy writers; the operation schema **replaces** two text/paragraph-coarse contracts; the offset-addressing table **extends** `ComposeDocxProjection`; the opaque-atom node is genuinely new (no overlap). Each BFF task states its Placement Justification in the PR and cites `.claude/constraints/bff-extensions.md`.

---

## Phase Breakdown (WBS)

Front-load the two hard surfaces (operation schema + step→OOXML applier) and spike them on the CIPO doc **before** committing Phase 3. Hard-replace deletion happens only after the Phase 0 gate is green.

### Phase 0 — Gate (proof before cutover) · tasks 001–006
- **001** Shadow-Document ADR (R4 architecture ADR; amends the R3 paragraph-diff project decision — ADR Tension Path B). _(main-session, `.claude/adr/`)_
- **002** Fidelity corpus assembled as LFS fixtures — the 3 sample-docs + owner worst-offenders (FR-01/NFR-01 evidence base).
- **003** Operation schema — the shared, versioned contract both ends compile against (FR-11).
- **004** Round-trip byte-diff harness — load → no-op save → byte-diff untouched subtrees (NFR-01).
- **005** Applier spike on the CIPO doc + Patch-engine A/B (Docxodus vs build-on-OpenXML-SDK) (FR-04/FR-11 choice).
- **006** **Phase 0 GATE review** — schema + harness + spike green ⇒ authorize cutover. HARD go/no-go.

### Phase 1 — Backend ingest · tasks 010–013
- **010** Persist `w14:paraId` on ingest (extend `ComposeBaselineParaIdStamper` to write into the retained package) (FR-01).
- **011** Intra-paragraph offset-addressing table (extend `ComposeDocxProjectionBuilder` / `ComposeDocxProjection`) (FR-01).
- **012** Opaque atoms for SDT/fields/complex objects — projection side (FR-02).
- **013** Phase-1 ingest/projection seam slice (`tests/integration/seam/**`) (NFR-06).

### Phase 2 — Frontend capture · tasks 020–024
- **020** ProseMirror step→operation interceptor (client) (FR-03).
- **021** Opaque-atom node — client schema (FR-02 client half).
- **022** Rebased operation log per dirty session (ProseMirror `Mapping`) (FR-03).
- **023** Delete `collectEditedParagraphs` / paragraph-diff export (client) (FR-06 client half; gated on Phase 0).
- **024** Phase-2 client capture tests (Vitest).

### Phase 3 — Patch Engine · tasks 030–035
- **030** `ComposeShadowPatchEngine` core — resolve node by paraId (O(1)), split runs at offset, emit `w:ins`/`w:del`/`w:comment` (migrate EDGE-1…4) (FR-04).
- **031** Structural operations (split / merge / insert / delete paragraph — sequenced last) (FR-05).
- **032** Retire both legacy writers (`DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer`) (FR-06; gated on Phase 0 + 030/031).
- **033** Born-in-editor unification (initial content = insert-everything op set onto empty shadow package) (FR-09).
- **034** Patch-engine seam slices + corpus round-trip proof (NFR-01/NFR-02).
- **035** Deploy patch-engine core to dev (behind existing surface; hard-replace not yet public).

### Phase 4 — AI anchoring · tasks 040–042
- **040** Generate-window bookmark/Decoration at selection; paraId context; resolve-on-return (FR-07).
- **041** Validate returned anchors before apply; fuzzy-as-comment last resort (FR-07).
- **042** AI anchoring tests — concurrent-edit ProseMirror test (Success Criterion 3).

### Phase 5 — Concurrency + save · tasks 050–054
- **050** Version-stamp every save (SPE eTag + projection schema version) + re-anchor-on-stale via `AnnotationReanchorService` (FR-08).
- **051** eTag sequencing for create-on-save (no eTag-mismatch 500) (FR-08).
- **052** HTTP 423 (Office lock) → user-actionable ProblemDetails (FR-08).
- **053** Import round-trip — pre-existing `w:ins`/`w:del`/`w:comment` render as first-class tracked changes + comment threads in the editor mount (FR-10).
- **054** Concurrency + import seam slices (NFR-06/NFR-08).

### Phase 6 — Hardening + cutover · tasks 060–063
- **060** Hard-replace cutover completion — remove residual `mammoth`, grep-audit both writers gone (FR-12).
- **061** Corpus proof re-run (byte-diff before/after) + publish-size + CVE + NetArch (NFR-01/04/05).
- **062** Deploy full R4 + operator UAT on the CIPO doc (Success Criteria 3/4/6).
- **063** **Flagship gate G** — all 8 success criteria green (final judgment gate).

### Wrap-up · task 090
- **090** Project wrap-up — README → Complete, lessons-learned, `/test-diet`, archive.

---

## Critical Path

`003 (op schema) → 005 (applier spike) → 006 (Phase 0 gate) → 030 (Patch Engine) → 032 (retire writers) → 034 (corpus proof) → 060 (hard-replace) → 061 → 062 → 063 (flagship gate) → 090`

The op schema (003) is the spine both ends implement; the Phase 0 gate (006) is the hard prerequisite to every cutover/deletion task. Structural ops (031) sequence last within the Patch Engine phase.

## Risk Register

| Risk | Mitigation |
|---|---|
| Hard-replace with no A/B safety net | Phase 0 proof gate (006) is a HARD prerequisite to old-path deletion; corpus harness (004) is acceptance evidence run before + after cutover (NFR-08). |
| Offset→run mapping (formatted/split runs, existing tracked changes) | The hardest bridge piece — spiked in Phase 0 (005), owned by tasks 011 + 030; opus/xhigh. |
| Paragraph-mark deletion (merge) via `w:pPr/w:rPr/w:del` | Flagged by prior art as the hardest edge — explicit acceptance criterion on 031. |
| `Services/Compose/` file contention across 4 sibling projects | `/conflict-check` before every BFF PR; most Compose tasks `parallel-safe:false`. |
| Docxodus size delta (if adopted) | Verify publish size per BFF task; the A/B (005) weighs fidelity + fit + size. |
| Owner corpus not yet supplied | 002 seeds with the 3 sample-docs + CIPO; owner worst-offenders tracked as an Unresolved Question (blocks final NFR-01 bar, not Phase 0 start). |

## References

- Spec: [`spec.md`](spec.md) · Design: [`design.md`](design.md)
- Inventory: [`notes/as-built-inventory.md`](notes/as-built-inventory.md) · Prior art: [`notes/bridge-prior-art.md`](notes/bridge-prior-art.md)
- BFF governance: `.claude/constraints/bff-extensions.md` · Hot-path registry: `projects/INDEX.md`
- Testing: `docs/adr/ADR-038-testing-strategy.md` · `docs/standards/TEST-ARCHITECTURE.md`
