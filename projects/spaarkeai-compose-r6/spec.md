# Spaarke Compose R6 — Render-on-Save Canonical Model & Word-Parity Fidelity — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-05
> **Source**: `projects/spaarkeai-compose-r6/design.md` (hand-authored 2026-08-05 by Ralph Schroeder + Claude)
> **Governing ADR**: [ADR-049 Compose Shadow Document](../../.claude/adr/ADR-049-compose-shadow-document.md) — **amendment proposed (Path B)** — see ADR Tensions
> **Supersedes**: the reactive per-UAT anchor-patch line of `spaarkeai-compose-r3/r4/r5`; the interim `compose-anchor-robustness-r1` surgical-anchor-tolerance framing (abandoned)

---

## Executive Summary

Every Compose save failure across R3→R5 has been the **same bug class**: reconciling anchors between
the TipTap editor model and the server-authoritative OOXML, discovered reactively in UAT and patched one
divergence at a time. The latest (`AppligentNDA_Signed.docx`) hard-fails with a 422. R6 **re-architects
Compose around render-on-save**: *save = render a fresh docx from a canonical document model into a new
immutable SPE version*, never a surgical byte-patch of inherited XML. This **eliminates the anchor
bug class by construction** — there is nothing to anchor against on save. R6 also adds **PDF as a
first-class intake source**, **Word template part-merge** for house-style chrome, a **Documents
version-history open UX** (the safety net made real), and a **round-trip fidelity CI harness** that moves
divergence discovery from UAT to CI (seeded with the NDA). TipTap stays; the Word add-in ("Option B") and
Word-grade lossless round-trip are explicitly out of scope.

---

## Scope

### In Scope
- **Render-on-save core** — route **all** saves (born-in-editor *and* imported) through render-from-model;
  retire the surgical-patch save path and the paraId count-gate that produces the 422.
- **Canonical document model** — generalize the existing editor projection into the single hub between
  every source format and the editor; reuse the R4.5 `NumberingComputationEngine`.
- **Near-term fidelity tier** — paragraphs/headings, numbering/lists, bold/italic/underline, tables,
  headers/footers, page breaks, hyperlinks, comments, tracked-changes (redlines) round-trip through the
  model without hard-fail; **medium tier** (styles/theme, images, footnotes, tab stops, section
  properties) as reachable; **hard tier** (text boxes, drawings, fields, content controls) degrades
  gracefully (accept-flatten), never 422.
- **Template part-merge** — merge the editor's rendered docx body into a firm/matter `.dotx` supplying
  `styles.xml` / `numbering.xml` / theme / headers / footers / `sectPr` via direct OOXML **part-merge**.
- **PDF intake** — PDF (NDA/agreement) → canonical model via the existing Azure Document Intelligence
  service; edit in Compose; save as a docx version.
- **Documents version-history open UX** — a user can list and **open (read-only)** a specific prior
  version of a document and get the exact bytes.
- **Round-trip fidelity CI harness** — a representative-corpus release gate, seeded with
  `AppligentNDA_Signed.docx`.
- **ADR-049 amendment** (Path B) codifying the render-on-save save path.

### Out of Scope
- **Word add-in / Office.js** ("Option B") — a separate future project.
- **Replacing TipTap** (Collabora/LibreOffice-Online editor swap — rejected road-not-taken).
- **Word-grade lossless round-trip** of Word-refined versions back into Compose (old requirement #3 —
  dropped; re-import is lossy through the same adapters; version history is the safety net).
- **Tactical / surgical NDA anchor fix** — explicitly declined by the owner; the NDA 422 is resolved by
  the pivot itself and the NDA becomes a regression fixture. (Accepted consequence: the NDA 422 stays live
  in prod until R6 ships.)
- **PDF export / headless-LibreOffice sidecar** — deferred to a fast-follow (see Owner Clarifications +
  Unresolved Questions). Inherits R4.5's deferred pagination decision and its two open licensing sign-offs.
- **Version restore & branch-from** — deferred to a fast-follow; R6 delivers read-only open of prior
  versions only.
- **Page/line pagination** — R4.5-deferred (WS-5); no page/line claim ships in R6.

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Compose/**` — `ComposeService.SaveAsync` (render-on-save routing),
  `ComposeDocumentRenderer` (render-from-model, generalized), `ComposeDocxProjectionBuilder` (canonical
  model + `NumberingComputationEngine`), retirement of `ComposeBaselineParaIdStamper` count-gate and
  `ComposeShadowPatchEngine` from the save path.
- `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentIntelligenceService.cs` + `DocumentParserRouter.cs` —
  PDF intake → canonical model.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Delivery/WordTemplateService.cs` + `ITemplateEngine` + the
  Dataverse `template` entity — reused for template *storage/variable* rendering; part-merge built in
  `Services/Compose`.
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs`, `.../Api/ContainerItemEndpoints.cs` /
  `DocumentsEndpoints.cs` — new **OBO** version-history list + open-prior-version endpoint.
- `src/client/shared/Spaarke.Compose.Components/**` — editor projection/import of the canonical model
  (`docxBridge.ts` **NEVER** deleted).
- `src/solutions/AllDocuments/src/App.tsx` (or a new version-history entry point) — Documents version UX.
- `tests/fixtures/compose-corpus/**` — add `AppligentNDA_Signed.docx` as an LFS fixture; fidelity harness.
- `.claude/adr/ADR-049-compose-shadow-document.md` — Path-B amendment.

---

## Requirements

### Functional Requirements

1. **FR-01 — Render-on-save core.** `ComposeService.SaveAsync` routes **all** saves (both
   `ComposeOrigin.Authored` and `ComposeOrigin.Imported`) through render-from-model
   (`ComposeDocumentRenderer.SynthesizeDocument`); there is **no** surgical byte-patch on the save path.
   An empty edit set still produces a faithful render of the current model. Persistence
   (`ReplaceFileContentAsUserAsync` → new SPE version), the Redis eTag stamp, and the stale-base re-anchor
   path carry over unchanged.
   - **Acceptance**: saving `AppligentNDA_Signed.docx` after edits **succeeds (no 422)**, the edits land
     correctly, and a new immutable SPE version is produced — with no surgical-anchor code executed on the
     save path.

2. **FR-02 — Retire the count-gate / surgical save path.** The `ComposeBaselineParaIdStamper` count-gate
   (`ComposeBaselineParaIdStamper.cs:113`) and `ComposeShadowPatchEngine` are removed from the save path.
   The surgical engine is retained **only** for any transitional clean-apply path explicitly permitted by
   the ADR-049 amendment. `docxBridge.ts` is **NEVER** deleted (inherited hard constraint).
   - **Acceptance**: static verification that no code path reachable from `SaveAsync` invokes the surgical
     patcher or the count-gate for a normal save; a `w14:paraId`/count mismatch on an imported document no
     longer refuses the save.

3. **FR-03 — Canonical document model.** Generalize the editor projection (`ComposeContentModel` /
   `ComposeDocxProjectionBuilder`) into the single canonical hub: every source format becomes the model,
   the editor edits the model, and save renders the model out. Reuse the R4.5 `NumberingComputationEngine`
   (deterministic, `numId`-scoped per ECMA-376) for numbering.
   - **Acceptance**: docx and PDF both project into the same canonical model; numbering labels match Word
     for the corpus numbering exemplars (R4.5 golden labels).

4. **FR-04 — Near-term fidelity tier.** Paragraphs/headings, numbering/lists, bold/italic/underline,
   tables (reuse the R5 tracked-table work), headers/footers, page breaks, hyperlinks, comments, and
   tracked-changes round-trip through the model without hard-fail. Hard-tier constructs (text boxes,
   drawings, fields, content controls — exactly what broke the surgical patcher on the NDA) **degrade
   gracefully** (accept-flatten) and are surfaced as warnings, never a 422. The prior version remains
   retrievable via FR-07.
   - **Acceptance**: each near-term feature survives a load→edit→save→reopen round-trip; each hard-tier
     construct flattens without hard-failing, with a user-visible warning.

5. **FR-05 — Template part-merge (document assembly).** Merge the editor's rendered docx body into a
   firm/matter `.dotx` that supplies `styles.xml` / `numbering.xml` / theme / headers / footers /
   `sectPr`, via direct OOXML **part-merge** (NOT `altChunk`, which merely embeds foreign content).
   **Locked decision (owner, 2026-08-05)**: build the part-merge **inside `Services/Compose`** (the real
   OOXML part/packaging machinery already lives there on `DocumentFormat.OpenXml` 3.5.1) and reuse the
   Dataverse `template` entity + `ITemplateEngine` (Handlebars) for template *storage* and *variable*
   rendering. The design §3-Q3 investigation is **resolved** — native Dataverse Word templates
   (`documenttemplate`) are **not** used (zero existing code; mail-merge placeholder replacement cannot
   inject a full authored body). Each part-merge component still carries a written **Placement
   Justification** (§10) and **Component Justification** (§11).
   - **Acceptance**: a document merged through a firm template carries that template's headers/footers/
     styles.

6. **FR-06 — PDF intake.** Open a PDF (NDA/agreement) in Compose: PDF → canonical model via the existing
   `DocumentIntelligenceService` (Azure DI Layout/Read) + `DocumentParserRouter`; edit; save as a docx
   version. Set **honest lossiness expectations** (PDF is fixed-layout; intake is lossier than docx) and
   lean on version history (FR-07).
   - **Acceptance**: a PDF NDA opens in Compose, is edited, and saves as a docx version.

7. **FR-07 — Documents version-history open UX (read-only).** Add a new **OBO (user-context)** endpoint
   that **lists** a document's SPE versions and **opens a specific prior version read-only (exact bytes)**,
   surfaced from the Documents surface. (The only version-list endpoint today is admin-only + config-scoped
   at `ContainerItemEndpoints.cs:48`; the OBO `DownloadFileVersionAsUserAsync` primitive exists but is
   Compose-internal and unexposed.) **Restore and branch-from are out of scope** (fast-follow).
   - **Acceptance**: a user opens a prior version (e.g. v3 after v4 exists) from the Documents surface and
     gets the exact bytes.

8. **FR-08 — Round-trip fidelity harness (CI release gate).** A harness round-trips the representative
   corpus and **fails the build** on a hard-fail or fidelity regression, moving discovery from UAT to CI.
   Seeded with `AppligentNDA_Signed.docx` (moved from the project `notes/` into
   `tests/fixtures/compose-corpus/` as an LFS fixture).
   - **Acceptance**: the harness runs in CI, gates the release, and the NDA fixture passes by construction
     under render-on-save.

### Non-Functional Requirements

- **NFR-01 — BFF publish size ≤60 MB compressed.** Re-measure the baseline (~49.63 MB incl. PDBs; state
  the PDB convention when reporting) on the first BFF-touching task; report absolute + delta on **every**
  BFF-touching task; ≥+5 MB single-task delta requires explicit justification; ≥55 MB cumulative →
  architecture review; ≥60 MB → HARD STOP. Azure DI is a managed service (no binary weight). **Any future
  PDF-export/LibreOffice sidecar MUST NOT be linked into the BFF** (deferred anyway; separate-process only).
- **NFR-02 — Licensing (permissive-only for anything linked into the BFF).** Carries R4.5 NFR-03: no
  commercial (Aspose / GemBox / Syncfusion — including Syncfusion's "Community License", which is
  free-of-charge but **not** permissive) and no AGPL paginators linked into the BFF; LibreOffice (MPL-2.0/
  LGPL) permitted **only** as a separate process. Binds when PDF export lands in the fast-follow; recorded
  here so it is not lost.
- **NFR-03 — No new HIGH-severity CVE** from `dotnet list package --vulnerable --include-transitive` on
  every BFF-touching task.
- **NFR-04 — Deploy discipline.** BFF + `sprk_spaarkeai` deployed together; anti-clobber verify (live
  artifact is a strict superset before deploy); run `/conflict-check` before **every** BFF PR
  (`Services/Compose/` + `ComposeService.cs` / `ComposeWorkspace.tsx` overlap `spaarkeai-compose-r1..r5` +
  `spaarke-ai-architecture-redesign-r2` + `analysis-hub-r1` / `agreements-r1`). Commit with `--no-verify`;
  co-author trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **NFR-05 — Test shape (ADR-038).** Every read/projection/save change adds/updates `tests/integration/
  seam/**` vertical-slice and fidelity-harness slices; **no** `Mock<HttpMessageHandler>`, DI-registration,
  or ctor-null tests. Test PRs are FULL rigor (root §8).

---

## Technical Constraints

### Applicable ADRs
- **ADR-049** (Compose Shadow Document) — governing ADR; **Path-B amendment** for the save path (below).
- **ADR-007** (SpeFileStore facade) — `Services/Compose/` stays `byte[]`-in / projection-out; no
  `Microsoft.Graph` types above `SpeFileStore`.
- **ADR-013** (AI Architecture) — no AI-internal types (`IOpenAiClient` / executors / routing) inside
  `Services/Compose/`; PDF intake consumes the existing document-parse service, not AI-dispatch.
- **ADR-039** (Grounded Execution) — engine frozen; R6 adds no new AI dispatch endpoint.
- **ADR-040** (Session Ledger) — persist the canonical model / projection payload per the existing pattern.
- **ADR-029 / ADR-010** (BFF publish hygiene / DI minimalism) — publish-size ratchet; minimal DI.
- **ADR-038** (Testing Strategy) — seam + fidelity-harness DoD; banned mock/DI/ctor tests.

### MUST Rules
- ✅ MUST route every save through render-from-model; MUST NOT execute a surgical byte-patch on the save
  path (the pivot's core invariant).
- ✅ MUST keep `Services/Compose/` `byte[]`-in / projection-out and free of AI-internal / `Microsoft.Graph`
  types (ADR-007 / ADR-013).
- ✅ MUST reuse `DocumentIntelligenceService` for PDF intake and the `template` entity + `ITemplateEngine`
  for template storage/variable rendering — no parallel subsystems (§11).
- ✅ MUST build the OOXML part-merge inside `Services/Compose` (not by extending the text-only
  `WordTemplateService`).
- ❌ MUST NOT delete `docxBridge.ts`.
- ❌ MUST NOT link any PDF-export/pagination engine into the BFF binary (permissive-only; separate-process
  only — NFR-01 / NFR-02).
- ❌ MUST NOT ship a page/line "100% identical to Word" claim (R4.5 F-5).

### Existing Patterns to Follow
- Render-from-model precedent: `ComposeDocumentRenderer.SynthesizeDocument` (`Services/Compose/ComposeDocumentRenderer.cs:102`)
  and `AppendSection` (:142) — both author-not-patch.
- Save-path discriminant: `ComposeService.SaveAsync` (`ComposeService.cs:642`), Authored/Imported branch at `:714`.
- Numbering: `NumberingComputationEngine` inside `ComposeDocxProjectionBuilder.cs:1357`.
- PDF parse: `DocumentIntelligenceService` + `DocumentParserRouter` + `ITextExtractor`.
- OBO version primitive: `DriveItemOperations.DownloadFileVersionAsUserAsync:842` (to be exposed via a new endpoint).

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

BFF=Y → the ≤60 MB publish-size ceiling (NFR-01) applies per task, and a **Placement Justification** is
required for each major component:
- **PDF intake** → extends the existing `DocumentIntelligenceService` / `DocumentParserRouter` inside
  `Services/Ai`; no new subsystem; Azure DI is a managed service (no binary weight).
- **Template part-merge** → built inside the existing `Services/Compose` module on the existing
  `DocumentFormat.OpenXml` 3.5.1; template storage reuses the existing `template` entity + `ITemplateEngine`.
- **Version-history endpoint** → added to the existing SPE/Documents endpoint surface; reuses the existing
  OBO `DownloadFileVersionAsUserAsync` primitive; `byte[]`-in / projection-out (ADR-007).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| **OBO version-history endpoint** (list + open prior version) | Admin-only, app-only, config-scoped `GET /api/spe/containers/{id}/items/{itemId}/versions` (`ContainerItemEndpoints.cs:48`) | **No** — different auth path (OBO vs app-only) and scope (user document vs admin/config); reuses the OBO `DownloadFileVersionAsUserAsync:842` primitive underneath | Without it the render-on-save "safety net" is unreachable from the product — a user cannot open v3 after v4; Success Criterion 4 fails |
| **Template part-merge component** (`Services/Compose`) | `WordTemplateService` (text-node `{{placeholder}}` replacement only — no styles/numbering/theme/sectPr merge); native `documenttemplate` = **zero code** | **No** — `WordTemplateService` cannot inject a full authored body into a style-supplying `.dotx`; reuses `template` entity + `ITemplateEngine` for storage/variables | Without it Compose cannot apply house style (headers/footers/styles/numbering) to inbound or born-in-Spaarke documents; Success Criterion 3 fails |
| **Version-history client entry point** (`AllDocuments` or new) | `src/solutions/AllDocuments/src/App.tsx` (Dataverse-record list; no SPE/version affordances) | **Extend** where feasible (add an affordance to the Documents surface) | Without it FR-07's endpoint has no user-reachable surface; Success Criterion 4 fails |

Render-on-save routing, the canonical document model, PDF intake, and the fidelity harness are
**modify/extend existing** surfaces → no new-component justification required.

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-049** | I-4 "untouched XML subtrees are byte-identical after save"; line-40 MUST NOT "re-derive the `.docx` from the editor model on save" | Render-on-save **re-derives** the whole docx from the canonical model on every save — directly the opposite of I-4 + line-40, **for the save path** | **B (amendment)** | The invariants were guardrails against a failure mode (fidelity loss from naive re-render). The pivot removes that failure mode by *widening the canonical model + tiered adapters* and making **version history** the safety net — nothing to anchor means the entire 422 class disappears. I-7 (no write-path text-search) is satisfied *trivially* (rendering needs no search). |

**ADR-049 amendment (Path B) — codifies:**
1. Save renders a new immutable version from the canonical model — **no surgical anchoring** on the save path.
2. **Version history is the fidelity safety net** (append-only; prior versions retrievable via FR-07).
3. **Representative-corpus round-trip is a release gate** (FR-08).
4. The surgical engine (`ComposeShadowPatchEngine`) is retained **only** for any transitional clean-apply
   path.

The amendment **must merge with or before** the dependent code (not a silent deviation). House-style
precedent: the existing Path-B amendment block already in ADR-049 (which superseded the R3
paragraph-diff decision).

| BFF §10 (Placement) | "state placement explicitly; extract per criteria" | PDF intake (Azure DI) + template-merge (extend existing service) are additive BFF work | **A (documented exception)** | Each gets a Placement Justification (above); both extend existing services, no new subsystem. *(The PDF-export/LibreOffice-sidecar Path-A exception is deferred with the export phase.)* |

---

## Success Criteria

1. [ ] Saving `AppligentNDA_Signed.docx` after edits **succeeds (no 422)**, produces a new version, and
   edits land correctly — with **no** surgical-anchor code on the save path. — Verify by: harness +
   manual UAT on the NDA fixture.
2. [ ] A PDF NDA opens in Compose, is edited, and saves as a docx version. — Verify by: end-to-end run on
   a PDF NDA.
3. [ ] A document merged through a firm template carries that template's headers/footers/styles. — Verify
   by: part-merge integration test asserting `styles.xml`/`sectPr`/header-footer provenance.
4. [ ] A user opens a prior version (e.g. v3 after v4) from the Documents surface and gets the **exact
   bytes** (read-only). — Verify by: version-list + open-prior-version endpoint test + Documents UX check.
5. [ ] The round-trip fidelity harness runs in CI and **gates the release**. — Verify by: CI job present +
   red on an injected regression.
6. [ ] Publish size ≤60 MB; no new HIGH CVE; BFF placement justified for every new component. — Verify by:
   `dotnet publish` size measurement + `dotnet list package --vulnerable` + PR Placement Justifications.

---

## Dependencies

### Prerequisites
- **Phase-0 verify**: confirm SPE versioning is non-destructive/append-only against the live Documents
  surface; inventory the Documents version APIs; measure the current BFF publish-size baseline; draft the
  ADR-049 amendment.
- Coordination with sibling Compose worktrees (`spaarkeai-compose-r1..r5`) and
  `spaarke-ai-architecture-redesign-r2` (sole owner of `Services/Ai/`) — consume `Services/Ai/PublicContracts/`
  seams only; no fork.

### External Dependencies
- Azure Document Intelligence (already provisioned — `DocumentIntelligenceService` + `DocumentIntelligenceOptions`).
- SharePoint Embedded (versioning behavior) via `SpeFileStore` / `ISpeFileOperations`.
- Git-LFS for the corpus fixture (`AppligentNDA_Signed.docx`).

---

## Owner Clarifications

*Answers captured during the design-to-spec interview (2026-08-05):*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| R6 scope | How much of the 7 phases ships in R6? | **Full 7-phase scope** (with the two trims below) | All phases in scope; spec covers render-on-save, fidelity, template-merge, PDF, version-UX, CI harness |
| Version-history UX depth | Open-only vs restore vs branch-from? | **Open / view prior versions (read-only)** | FR-07 delivers OBO list + open-prior-version (exact bytes); restore & branch-from are explicitly out of scope (fast-follow) |
| PDF scope | Intake, intake+export, or defer? | **Intake only in R6** | FR-06 delivers PDF intake via existing Azure DI; PDF export (LibreOffice sidecar) deferred to fast-follow; canonical model still designed PDF-ready |
| Template-merge mechanism | Native Dataverse Word templates vs part-merge-in-Compose vs keep the investigation? | **Lock part-merge-in-Compose** | FR-05 commits to OOXML part-merge in `Services/Compose` reusing the `template` entity + `ITemplateEngine`; the §3-Q3 investigation task is dropped (native `documenttemplate` rejected — zero code, placeholder-merge insufficient) |
| Corpus worst-offenders | Supply now / proceed / hard-gate? | **Proceed; keep intake slots open** | Pipeline runs on NDA + synthetic exemplars; corpus placeholder rows stay open (no gate); owner-supplied docs auto-register later |
| PDF-export licensing sign-offs | Rule now or defer? | **Defer to the export fast-follow** | The 2 R4.5 sign-offs (AGPL-as-service; Syncfusion) are not forced in R6; carried in Unresolved Questions as blockers for the deferred export project |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify / to be confirmed during implementation):*

- **PDF export engine**: when the export fast-follow lands, it commits to the **headless-LibreOffice
  sidecar** path (per design §6 + R4.5's classification of LibreOffice as NFR-03-clean out-of-process).
  A Graph `format=pdf` render spike remains the R4.5-noted alternative. — affects the deferred export phase.
- **SPE append-only versioning**: SPE versioning is non-destructive/append-only; the Phase-0 verify task
  confirms this against the live Documents surface before FR-07 is built. — affects FR-07 + the pivot's
  safety-net claim.
- **Fidelity ceiling**: "Word parity, as much as reasonable" means the near-term tier round-trips safely
  and the hard tier accept-flattens; no promise of Word-grade lossless round-trip (dropped requirement).

---

## Unresolved Questions

*Flagged for `/project-pipeline` / owner — do not block spec publication:*

- [ ] **Owner worst-offender corpus** — rows 4–8 in `tests/fixtures/compose-corpus/corpus-manifest.md`
  are still placeholders (live track-changes redline; table-heavy w/ nested/merged cells; literal
  OOXML fields/content-controls; real multi-level numbered doc; multi-section distinct headers/footers).
  Blocks: full fidelity-harness coverage (FR-08) — the NDA + synthetic exemplars cover the core, but owner
  docs strengthen the gate.
- [ ] **Two R4.5 licensing sign-offs** (carry-forward, bind only when PDF export lands): (a) AGPL-3.0
  "as a separate service" ambiguity under NFR-02; (b) Syncfusion "Community License" free-but-not-
  permissive. Both are human-sign-off items (root §9). Blocks: the deferred PDF-export phase, not R6.

---

*AI-optimized specification. Original design: `projects/spaarkeai-compose-r6/design.md` (preserved verbatim).*
