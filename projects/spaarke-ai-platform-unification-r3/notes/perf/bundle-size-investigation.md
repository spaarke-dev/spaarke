# SpaarkeAi Bundle Size Investigation (Phase F task 061 pulled forward)

> **Date**: 2026-05-20
> **Trigger**: SpaarkeAi bundle gzip jumped from 509 KB (post-Wave 2b) → 798 KB (post-Wave 3)
> **NFR-12 target**: <250 KB gzip delta vs R2 baseline
> **Status**: Root cause identified; remediation requires user trade-off decision

---

## Bundle measurements

| Stage | gzip | Δ from prior | Cumulative Δ from baseline |
|---|---|---|---|
| Pre-R3 baseline (master) | ~258 KB (estimated from task 024 perf doc) | — | — |
| Post-Wave 1 (Phase A foundations) | 508 KB | +250 KB | +250 KB |
| Post-Wave 2a+2c | 506 KB | -2 KB | +248 KB |
| Post-Wave 2b | 509 KB | +3 KB | +251 KB |
| **Post-Wave 3** | **798 KB** | **+289 KB** | **+540 KB** ⚠️ |
| NFR-12 budget | 508 KB (= baseline + 250) | n/a | n/a |
| **Over budget by** | **290 KB** | | |

Wave 1 alone added 250 KB just from `@fluentui/react-icons` + `@fluentui/react-components` pulling in via the shared PaneHeader / WorkspaceShell components. That puts us right at the NFR-12 ceiling before any feature code lands.

Wave 3 then added another 290 KB — the smoking gun.

---

## Root cause

**`vite-plugin-singlefile` inlines async chunks into the main HTML.**

The intent of `vite-plugin-singlefile` is to produce ONE deployable HTML file for Dataverse web resources (per ADR-026). It does this by inlining every JS/CSS chunk — including async chunks produced by dynamic `import()`.

Task 024's `useChatFileAttachment` hook lazy-imports `pdfjs-dist` + `mammoth` via `await import('pdfjs-dist')` / `await import('mammoth')` inside the `addFiles` function body. This is the recommended Vite pattern for code-splitting.

**Under a normal Vite build**, those become separate JS chunks loaded on demand → the libraries are NOT in the initial bundle.

**Under `vite-plugin-singlefile`**, the async chunks are inlined into the main HTML as inline scripts or data-URI imports → the libraries ARE in the initial bundle (just behind an `await import()` gate that resolves instantly).

**Evidence in `dist/spaarkeai.html`**:
```
$ grep -oE "(pdfjsLib|GlobalWorkerOptions|getDocument|workerPort)" spaarkeai.html | sort -u
GlobalWorkerOptions
getDocument
pdfjsLib
workerPort
```

These are PDF.js core APIs. Their presence in the inlined HTML confirms `pdfjs-dist` is fully bundled. `mammoth` is similarly bundled (though smaller).

`pdfjs-dist` minified+gzipped is ≈ 250 KB. `mammoth` is ≈ 50 KB. Together they account for the ~290 KB Wave 3 spike.

---

## Why widget tree-shaking is NOT the issue

Initial hypothesis was that task 042's eager import of `GetStartedCardsWidget` from the `@spaarke/ai-widgets` barrel pulled in all sibling widget eager exports. Verification by string-probe of `spaarkeai.html`:

| Widget | Occurrences in bundle |
|---|---|
| RedlineViewer | 0 (not bundled) |
| ProgressTracker | 0 (not bundled) |
| PlaybookGallery | 0 (not bundled) |
| CreateMatterWizard | 2 (registration strings only) |
| DocumentUploadWizard | 1 (registration string only) |
| SearchSelectWizard | 1 (registration string only) |
| Findings | 1 (registration string only) |
| EmailCompose | 1 (registration string only) |
| MeetingSchedule | 1 (registration string only) |
| CreateProjectWizard | 1 (registration string only) |
| FindSimilarWizard | 1 (registration string only) |
| **GetStartedCards** | **1 (registered + actually used)** |

Conclusion: Rollup tree-shaking IS working for the widget barrel. Only `GetStartedCardsWidget` body is bundled because it's actually rendered. Other widgets are correctly absent.

**The 290 KB jump is entirely from `pdfjs-dist` + `mammoth` being inlined.**

---

## Options (TRADE-OFF — requires user input)

### Option 1: Accept the budget overrun (no code change)

- **Effort**: Zero
- **Impact**: NFR-12 target relaxed from "<250 KB delta" to "~540 KB delta". App still works correctly.
- **Risk**: User-perceived load time on cold load increases. SpaarkeAi welcome state takes longer to render.
- **Recommendation**: Acceptable if Phase G smoke tasks (071-074) measure NFR-01 / NFR-03 timings within targets despite the larger bundle.

### Option 2: Deploy PDF.js + mammoth as separate Dataverse web resources

- **Effort**: Medium — requires (a) building pdfjs-dist + mammoth as separate web resource files, (b) deploying them to Dataverse (additional script for `Deploy-SpaarkeAi.ps1`), (c) refactoring `useChatFileAttachment` to load them via dynamic `<script>` tag injection from a known Dataverse web resource URL rather than `await import()`.
- **Impact**: Main SpaarkeAi bundle drops by ~290 KB. PDF.js / mammoth load on demand from `/api/data/v9.x/WebResourceSet(...)` URLs.
- **Risk**: Cross-origin / auth nuances with loading scripts from Dataverse web resources. CSP may block. Needs investigation spike.
- **Recommendation**: Best long-term solution but requires its own implementation task. Could be deferred to a follow-on project.

### Option 3: Server-side extraction (changes FR-07 contract)

- **Effort**: Medium — extend Phase E backend (task 050) to accept raw file bytes, do extraction server-side, return text.
- **Impact**: pdfjs-dist + mammoth disappear entirely. Bundle drops by ~290 KB.
- **Risk**: SPEC DEVIATION. FR-07 + OC-02 both explicitly specify "client-side text extraction (PDF.js for PDFs, mammoth for DOCX, raw text for `.txt`/`.md`)". Server-side extraction is a different architecture (and a different security posture — file bytes hit the server, not just extracted text).
- **Recommendation**: Reasonable if the user agrees to revise the spec. Simplifies the frontend significantly.

### Option 4: Disable `vite-plugin-singlefile` and use a multi-file deployment

- **Effort**: High — requires changes to deployment scripts and Dataverse web resource registration (multiple files instead of one).
- **Impact**: Bundle size restored to expected ~258 KB main bundle + lazy chunks loaded on demand.
- **Risk**: ADR-026 violation. Existing deployment pipeline (`Deploy-SpaarkeAi.ps1`) assumes single-file output. May not be feasible for Dataverse custom pages.
- **Recommendation**: Not recommended — architectural shift.

### Option 5: Make PDF.js / mammoth opt-in via runtime feature flag

- **Effort**: Low-medium — guard the lazy imports behind a feature flag (`window.SPAARKE_ENABLE_FILE_ATTACHMENTS = true`). Only when set does the hook attempt to load the libs.
- **Impact**: Bundle still contains the libs (because singlefile inlines them) — DOES NOT FIX bundle size. Only defers their execution.
- **Recommendation**: Not useful here. The bundle-size problem is at compile/inline time, not runtime.

---

## Recommended path

**Short term (Phase F task 061)**: Adopt **Option 1** — accept the overrun for R3. Document the deviation explicitly. Verify Phase G smoke tasks (074 NFR verification) that NFR-01 (pane render <500 ms) and NFR-03 (History overlay <200 ms + populate <300 ms) targets are still met despite the larger bundle. If yes, ship and revisit in a follow-on.

**Long term (next project)**: Adopt **Option 2** (separate web resources for PDF.js + mammoth) as a discrete project. The pattern is reusable for other heavy libraries that need to ship to Dataverse code pages.

---

## Implications for Wave 4 + remainder of R3

- **Task 026 (Wave 4)**: No change required. Frontend payload wiring is bundle-neutral (just sends already-extracted text to BFF).
- **Tasks 050 + 051 (Phase E)**: No bundle impact (backend code).
- **Task 061 (Phase F)**: Update its acceptance criteria to reflect the chosen option. If Option 1, the criterion becomes "document the overrun + verify Phase G NFR targets still pass" rather than "<250 KB delta".
- **Task 070 (Phase G deploy)**: Proceeds normally regardless of option chosen.

---

*Document this decision and add a `<follow-up>` task in the project plan if Option 2 or 3 is chosen.*
