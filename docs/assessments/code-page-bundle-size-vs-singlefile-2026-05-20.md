# Code Page Bundle Size vs. `vite-plugin-singlefile` Assessment

> **Date**: 2026-05-20
> **Author**: AI assessment produced during `spaarke-ai-platform-unification-r3` Wave 3
> **Method**: Direct measurement of `dist/spaarkeai.html` bundle composition via string-probing; analysis of Vite build config and shared-library barrel structure; comparison against NFR-12 budget in `projects/spaarke-ai-platform-unification-r3/spec.md`.
> **Status**: Findings + recommendations for cross-project decision-making. R3 is proceeding with Option 1 (accept overrun). The BFF remediation project + any future Code Page project should review before adopting heavy client-side libraries.
> **Related**:
> - [ADR-026](../adr/ADR-026-full-page-custom-page-standard.md) — Full-page Code Page standard (Vite + React 19 + `vite-plugin-singlefile`)
> - [`projects/spaarke-ai-platform-unification-r3/spec.md`](../../projects/spaarke-ai-platform-unification-r3/spec.md) — NFR-12 bundle budget definition
> - [`projects/spaarke-ai-platform-unification-r3/notes/perf/bundle-size-investigation.md`](../../projects/spaarke-ai-platform-unification-r3/notes/perf/bundle-size-investigation.md) — R3-local working notes (subset of this assessment)
> - [`projects/sdap-bff-api-remediation-fix/`](../../projects/sdap-bff-api-remediation-fix/) — BFF remediation (queued; potential consumer of Option 3 server-side-extraction pattern)
> - [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](./bff-ai-extraction-assessment-2026-05-20.md) — sibling assessment (BFF AI extraction)

---

## Executive summary

There is an **architectural tension** between two long-standing decisions in this repo:

1. **ADR-026** — Full-page Code Pages deploy as a SINGLE inlined HTML file via `vite-plugin-singlefile`. This is non-negotiable for current Dataverse web-resource registration (one solution component = one file).
2. **NFR-12-style bundle budgets** — Modern web apps lazy-load heavy libraries (PDF.js, mammoth, monaco, etc.) via `await import(...)` and `React.lazy(...)`. Vite, by default, produces async chunks that are loaded on demand. This is the standard mitigation for keeping initial bundle small.

**`vite-plugin-singlefile` silently nullifies the second strategy** by inlining every async chunk into the single HTML. From the bundler's perspective the lazy import works; from the deployable artifact's perspective the heavy library is unconditionally shipped. **Developers who follow modern Vite patterns will believe their bundle is small when it is not.**

Spaarke AI Platform Unification R3 surfaced this in concrete numbers:

- Pre-R3 SpaarkeAi bundle: **~258 KB** gzip (baseline)
- Post-R3 SpaarkeAi bundle: **798 KB** gzip
- NFR-12 budget allowance: **+250 KB** (so target = ~508 KB)
- **Overage: ~290 KB**, entirely attributable to `pdfjs-dist` + `mammoth` being inlined despite a correct lazy-import implementation

R3 ships Option 1 (accept overrun, verify NFR-01/NFR-03 timings still pass at deploy time). This assessment exists so the **BFF remediation project**, any **future Code Page project** considering heavy client-side libraries, and any **architecture review** of ADR-026 / NFR budgeting practices have a shared evidence base.

**The cross-project recommendation is Option 2** (heavy libraries deployed as separate Dataverse web resources, loaded via dynamic `<script>` injection) — but it requires a small, scoped engineering investment that no project currently owns. **The BFF remediation project should consider whether to scope it in**, since (a) the pattern is reusable across all future Code Pages, (b) it unblocks several known follow-ons (PDF.js, mammoth, Monaco editor — all candidates for future Code Pages), and (c) the BFF project's "streamline" charter makes it a natural home for the matching backend changes if Option 3 is chosen instead.

---

## 1. Problem statement

### 1.1 The conflict

For any Code Page that needs a heavy client-side library:

```
┌─────────────────────────────────────────────────────────────────┐
│  Standard Vite pattern (recommended online, in Vite docs):       │
│                                                                  │
│    async function doExpensiveThing() {                           │
│      const lib = await import('expensive-lib');     ←─ lazy      │
│      return lib.extract(input);                                  │
│    }                                                             │
│                                                                  │
│  Vite output: lib goes into a separate JS chunk loaded on demand│
│  Initial bundle: small ✅                                         │
└─────────────────────────────────────────────────────────────────┘
                          ▼
                          ▼ when wrapped with vite-plugin-singlefile
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│  vite-plugin-singlefile output:                                  │
│                                                                  │
│    Single HTML file containing ALL chunks inlined.               │
│    Async chunks resolve INSTANTLY (no network) but the bytes     │
│    were shipped on the initial page load.                        │
│                                                                  │
│  Initial deployable: bloated ❌                                   │
└─────────────────────────────────────────────────────────────────┘
```

There is no "tree-shake away the async chunk's heavy library" path — by definition, if the library can be loaded at runtime, the code must be shipped.

### 1.2 Why this matters across projects

Every Code Page-style surface in the repo (SpaarkeAi, LegalWorkspace, WorkspaceLayoutWizard, CreateProjectWizard, FindSimilar, etc.) uses the same Vite + singlefile setup. Each new Code Page adopting a heavy library will hit this issue. Today's heavy libraries in the repo (or planned):

| Library | Approx. gzip | Used in |
|---|---:|---|
| `pdfjs-dist` (PDF.js core) | ~250 KB | R3 `useChatFileAttachment` (FR-07) |
| `mammoth` (DOCX → text) | ~50 KB | R3 `useChatFileAttachment` (FR-07) |
| `monaco-editor` | ~700 KB | Candidate for future "JSON Prompt Schema editor" Code Page |
| `pdf-lib` (PDF authoring) | ~150 KB | Candidate for redline preview / annotation |
| `jszip` | ~50 KB | Candidate for solution-zip operations |
| `xlsx` (SheetJS) | ~400 KB | Candidate for Excel preview / extraction |

Without a shared solution, every project will rediscover this conflict.

---

## 2. Evidence

### 2.1 Bundle composition (measured)

Run from `src/solutions/SpaarkeAi/dist/`:

```bash
$ grep -oE "(pdfjsLib|GlobalWorkerOptions|getDocument|workerPort)" spaarkeai.html | sort -u
GlobalWorkerOptions
getDocument
pdfjsLib
workerPort
```

These are PDF.js core APIs. Their presence in the inlined HTML confirms `pdfjs-dist` is fully bundled.

Probe for individual widget bodies (to rule out a different hypothesis):

| Widget body in bundle | Result |
|---|---|
| `RedlineViewer` | 0 occurrences (not bundled) ✅ tree-shaking works |
| `ProgressTracker` | 0 (not bundled) ✅ |
| `PlaybookGallery` | 0 (not bundled) ✅ |
| `CreateMatterWizard` | 2 (registration strings only) ✅ |
| `DocumentUploadWizard` | 1 (registration string only) ✅ |
| `SearchSelectWizard` | 1 ✅ |
| `Findings` | 1 ✅ |
| `EmailCompose` | 1 ✅ |
| `MeetingSchedule` | 1 ✅ |
| `CreateProjectWizard` | 1 ✅ |
| `FindSimilarWizard` | 1 ✅ |
| `GetStartedCardsWidget` | 1 (registered + actually rendered) ✅ expected |

Conclusion: Rollup's widget-barrel tree-shaking is **working correctly** (this was an initial hypothesis ruled out). The bloat is entirely from `pdfjs-dist` + `mammoth`.

### 2.2 Build configuration

`src/solutions/SpaarkeAi/vite.config.ts`:

```typescript
import { viteSingleFile } from "vite-plugin-singlefile";

export default defineConfig({
  plugins: [
    // ...
    viteSingleFile(),  // inlines all chunks (including async) into one HTML
  ],
  build: {
    assetsInlineLimit: 100000000,  // inline ALL assets, no separate files
    rollupOptions: {
      output: {
        manualChunks: undefined,  // single bundle, no code-splitting
      },
    },
  },
  base: "./",  // relative paths for Dataverse web-resource hosting
});
```

The same shape is used in `LegalWorkspace/vite.config.ts`, `WorkspaceLayoutWizard/vite.config.ts`, and every other Code Page solution.

### 2.3 Lazy-import code (proven correct)

`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts`:

```typescript
async function extractPdf(file: File): Promise<string> {
  const pdfjs = await import('pdfjs-dist');  // lazy, by the book
  const doc = await pdfjs.getDocument({ data: await file.arrayBuffer() }).promise;
  // ...
}

async function extractDocx(file: File): Promise<string> {
  const mammoth = await import('mammoth');  // lazy, by the book
  const result = await mammoth.extractRawText({ arrayBuffer: await file.arrayBuffer() });
  return result.value;
}
```

Vite correctly emits these as separate JS chunks during the Rollup phase. `vite-plugin-singlefile` then inlines those chunks into `spaarkeai.html`. The lazy semantics are preserved at runtime (the import resolves to an already-evaluated module on first call), but the **bytes are shipped on initial page load**.

### 2.4 NFR-12 budget calculation

From `projects/spaarke-ai-platform-unification-r3/spec.md` (NFR-12 row):

> Daily Briefing widget + History overlay + file-extraction libs add **<250 KB gzip** to the SpaarkeAi bundle (file-extraction libs lazy-loaded on first `+` click)

R3 actuals (gzip):

| Wave | Bundle size | Notes |
|---|---|---|
| Pre-R3 (master) | ~258 KB (estimated baseline) | Plain three-pane shell |
| Post-Wave 1 (foundations) | 508 KB | +250 KB from `@fluentui/react-icons` + `@fluentui/react-components` pulled via shared `PaneHeader` + `WorkspaceShell` — **already hits the NFR-12 ceiling** before any feature code |
| Post-Wave 2a+2c | 506 KB | -2 KB (WelcomePanel chrome trim removed some text) |
| Post-Wave 2b | 509 KB | +3 KB (Daily Briefing components in LegalWorkspace, minimal SpaarkeAi delta) |
| **Post-Wave 3** | **798 KB** | **+289 KB** — pdfjs-dist + mammoth inlined |
| Target | ~508 KB | (baseline + 250 KB) |
| **Overage** | **~290 KB** | |

The Wave 1 → Wave 2 trajectory was already at the budget ceiling because the Fluent v9 component library is heavier than R2 was. The Wave 3 spike is the file-extraction libs specifically — those libs were intended to be lazy-loaded, but singlefile inlined them.

---

## 3. Options

Five options were evaluated. Each has different scope, risk, and architectural impact.

### Option 1 — Accept overrun, document deviation (no code change)

| Aspect | Detail |
|---|---|
| Effort | 0 hours (documentation only) |
| Risk | Low — app functions correctly; only initial page-load latency is affected |
| Spec compliance | NFR-12 violated; requires explicit waiver from spec owner |
| Architectural impact | None |
| Cross-project benefit | None |

**When this is acceptable**: deployment is to a fast network (corp LAN, intranet); first-paint timing (NFR-01) and overlay timings (NFR-03) still pass in real-host smoke testing; the overage doesn't compound across upcoming Code Pages.

**R3 decision**: Adopting this for R3. NFR-01 + NFR-03 verification happens in Phase G task 074 (Lighthouse + manual timing). If they pass, R3 ships. If they fail, escalate.

### Option 2 — Separate Dataverse web resources for heavy libs (CROSS-PROJECT, RECOMMENDED)

| Aspect | Detail |
|---|---|
| Effort | Medium (1–2 weeks scoped) |
| Risk | Medium — needs CSP / auth / cache investigation |
| Spec compliance | Restores NFR-12 |
| Architectural impact | Establishes a reusable pattern; ADR-26 may need an addendum |
| Cross-project benefit | **HIGH** — every future Code Page with a heavy lib benefits |

**Sketch**:

1. Build `pdfjs-dist` + `mammoth` (and others) as standalone IIFE/UMD scripts (already shipped by both libs).
2. Deploy each library as a Dataverse web resource (e.g., `sprk_libs_pdfjs.js`, `sprk_libs_mammoth.js`).
3. Add a thin helper (in `@spaarke/ui-components`) `loadDataverseScript(webResourceName: string): Promise<void>` that:
   - Checks if the global symbol already exists (idempotent).
   - Resolves the web-resource URL via `Xrm.Page.context.getClientUrl() + '/WebResources/' + name` (in host) OR a configured base URL (in Vite dev).
   - Injects `<script src="...">` and waits for `load`.
4. Refactor `useChatFileAttachment` (and any future heavy-lib consumer) to call `await loadDataverseScript('sprk_libs_pdfjs')` then use `window.pdfjsLib` / `window.mammoth`.

**Why this is the right cross-project solution**:

- One implementation, many consumers (R3 file attachments, future Monaco-based JSON editor, future xlsx Excel preview, future redline annotation).
- Browser HTTP cache handles repeat loads — first user pays once, subsequent users get a fast cached response.
- Library updates ship independently of the Code Page (just redeploy the lib web resource).
- Does not require any backend changes.

**Known risks to investigate before adopting**:

- **CSP**: Dataverse hosts may have a Content Security Policy that blocks dynamically injected scripts. Verification required.
- **Auth**: Dataverse web-resource URLs require an authenticated session. The Code Page already has that session, but a fetch from a different origin (some embedding scenarios) may not.
- **Workers**: PDF.js historically used a Web Worker for parsing. Worker scripts also need to be loaded from a known URL — additional web resource (`sprk_libs_pdfjs_worker`) likely required.
- **CORS / SameOrigin**: When SpaarkeAi is loaded inside an iframe in Power Apps, the web-resource URL is the same origin — should be fine. When loaded in Microsoft Teams or as a deep-linked Code Page, the origin model may differ.

**Recommended ownership**: A new project, or scope this into an existing project. The BFF remediation project is a candidate if it has appetite for a frontend-platform sub-deliverable. Otherwise, a dedicated "Code Page heavy-lib loader" project would be small (1–2 weeks) and unblocks several future projects.

### Option 3 — Move extraction server-side (BFF EXTENSION, FR-07 SPEC DEVIATION)

| Aspect | Detail |
|---|---|
| Effort | Medium (~1 week, includes BFF endpoint + tests) |
| Risk | Medium — changes auth/security posture (file bytes hit server) |
| Spec compliance | Violates R3 FR-07 + OC-02 ("client-side extraction") — requires re-confirmation with owner |
| Architectural impact | Reuses ADR-013 (extend BFF in-process for AI); same module |
| Cross-project benefit | Medium — establishes pattern for "thin frontend, smart backend" file processing |

**Sketch**:

1. Extend Phase E task 050 (or add a separate endpoint): `POST /api/ai/files/extract` accepts `multipart/form-data` with up to 5 files, returns `attachments: Array<{filename, contentType, textContent}>` (same shape as today's frontend output).
2. Server uses `iText7` or `PdfPig` for PDF; `OpenXml` for DOCX. Both are .NET-native, no Vue/Node-side libs.
3. Frontend `useChatFileAttachment` calls the new endpoint instead of loading PDF.js / mammoth.
4. Existing FR-07 acceptance: still passes ("attach 5 .txt files, ask 'summarize', AI replies referencing all 5" — the message payload shape is unchanged).

**Trade-offs**:

- **Pros**: Bundle drops by ~290 KB. Bundle stays well under NFR-12. Server libraries are easier to keep updated and security-patched.
- **Cons**: File bytes leave the browser (auth + audit considerations). Adds round-trips (one per attachment batch). Increases server resource usage. Existing OC-02 wording specifies client-side.
- **Connection to BFF remediation project**: If the BFF remediation team is scoping extensions, this is a natural fit ("AI document extraction endpoint" alongside the chat-attachments work in R3 Phase E). The endpoint inherits ADR-008 endpoint filters and ADR-013 in-process placement.

### Option 4 — Disable `vite-plugin-singlefile` (ARCHITECTURAL SHIFT)

| Aspect | Detail |
|---|---|
| Effort | High (1–2 weeks across all Code Pages, plus deployment pipeline changes) |
| Risk | High — breaks ADR-026 |
| Spec compliance | Restores NFR-12 by removing the inlining behavior |
| Architectural impact | **Violates ADR-026**. Requires ADR revision/replacement. |
| Cross-project benefit | Removes the structural conflict but at high migration cost |

**Sketch**: Move from singlefile inline-everything to multi-file output where lazy chunks are separate `.js` files. Each Code Page solution becomes 1 HTML + N JS chunks. Deployment pipeline must register each chunk as a Dataverse web resource and the HTML must reference them via relative URLs.

**Why this is not recommended**: ADR-026 explicitly chose singlefile to avoid this complexity. Dataverse web-resource management of multi-file bundles is operationally heavier (more solution components, more import time, more redeploy surface area on every bump). Option 2 achieves the same goal (heavy libs out of main bundle) without abandoning singlefile for the app code.

### Option 5 — Runtime feature flag gating

| Aspect | Detail |
|---|---|
| Effort | Low |
| Risk | n/a |
| Spec compliance | DOES NOT FIX bundle size |
| Architectural impact | None |
| Cross-project benefit | None |

**Sketch**: Guard the lazy imports behind `if (window.SPAARKE_ENABLE_FILE_ATTACHMENTS) { ... }`. Only when the flag is set does the hook attempt to load the libs.

**Why this doesn't work**: The bundle-size problem is at compile/inline time, not runtime. Whether the flag is true or false at runtime, the bytes are still in the inlined HTML. This option was evaluated and discarded.

---

## 4. Recommendation

### For R3 (this project)

**Option 1**. Accept the overrun. Document it in `plan.md` § Risk Register and in the Phase H wrap-up lessons-learned. Phase G task 074 (NFR verification) will measure NFR-01 (pane render <500 ms) and NFR-03 (History overlay <200 ms + populate <300 ms) against the real bundle. If those targets pass on a representative network, the overage is empirically acceptable. If they fail, escalate to Option 2 or 3 as a follow-on.

### For the BFF remediation project (queued)

The BFF remediation team is best positioned to evaluate **Option 3** because:
- It's a BFF endpoint addition (their domain).
- Their charter is "streamline BFF" — adding a new clean endpoint that obviates 290 KB of frontend bundle aligns with that.
- ADR-013 (extend BFF for AI) explicitly supports this kind of in-process extension.
- It removes a frontend-side complexity that the BFF team owns the consequences of (load times affect users of SpaarkeAi which uses BFF).

If they decline (legitimate: spec says client-side, security review on file-byte upload), they should at least be informed that **Option 2** is the recommended cross-project path so they can choose to include or exclude it from their plan.

### For the architecture / standards group

ADR-026 should be amended (not replaced) to add a "Heavy library handling" subsection that:

- Explicitly notes the singlefile/lazy-import incompatibility.
- Recommends Option 2 as the standard pattern for heavy libs.
- Lists known-heavy libraries and their recommended Option 2 web-resource names (a registry).

This prevents future Code Page projects from re-discovering the issue.

### For new Code Page projects (going forward)

Until Option 2 is implemented as a shared pattern, **bundle-budget every Code Page project against the realistic singlefile inlining behavior, not the theoretical lazy-import behavior**. Specifically:

- Treat any `await import()` of a >50 KB library as "fully bundled" for budget purposes.
- Require an explicit waiver in the project spec if the budget is exceeded.
- Reference this assessment when scoping.

---

## 5. Open questions

These were not resolved during the assessment and may warrant follow-up:

- **OQ-1**: What is the actual NFR-01 / NFR-03 timing impact of a 798 KB gzip bundle on the production Dataverse network? If timings still pass, the overage is functionally a non-issue and Option 1 is the right permanent answer.
- **OQ-2**: Are there Dataverse-side limits on a single web-resource file size (compressed or uncompressed)? `vite-plugin-singlefile` makes the deployable a single file; if Dataverse caps it (e.g., 30 MB per resource), every Code Page that grows toward that cap is at risk.
- **OQ-3**: Has the Dataverse browser cache behavior for Code Page web resources been profiled? If hot reloads cache effectively, the singlefile approach + acceptance-of-overrun is much more defensible than for cold loads.
- **OQ-4**: For Option 2, what's the Dataverse-side caching policy on web-resource files? PDF.js etc. should rarely change; long-cache headers would amortize the load cost.
- **OQ-5**: For Option 3, does the BFF remediation team have appetite for adding a `/api/ai/files/extract` endpoint? If yes, R3 Phase E (tasks 050 + 051) could be re-scoped to include it.

---

## 6. Action items

| # | Action | Owner | Status |
|---|---|---|---|
| 1 | Phase G task 074 measures NFR-01 + NFR-03 against 798 KB bundle | R3 | Pending (Wave G) |
| 2 | Document NFR-12 overrun in R3 plan.md Risk Register + lessons-learned | R3 | Pending (Wave H wrap-up) |
| 3 | Share this assessment with BFF remediation project team | R3 owner | **NOW** |
| 4 | BFF remediation team evaluates Option 3 inclusion in their scope | BFF team | **NEW** |
| 5 | Architecture review of ADR-026 — add "Heavy library handling" subsection | ARB / docs owner | **NEW** |
| 6 | Scope a "Code Page heavy-lib loader" project (Option 2) — assign owner | TBD | **NEW** |

---

*This assessment is read-only; no code or config was modified. R3 is proceeding with Option 1 in parallel. Decisions on Options 2 / 3 / 5 require their owner project's scoping review.*
