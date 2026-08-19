# Task 070 — Blank page mounts editable (R6 D8 / FR-08) — VERIFIED ALREADY SATISFIED (regression guard added; NO src change)

> Phase 7 · sonnet@high · FULL rigor · 2026-08-17 · client-only. No BFF bytes.

## Headline finding (transparent — the task premise is STALE)

**The D8 defect does NOT reproduce in the current codebase. Blank page ALREADY mounts editable.** I made
**no production change** — fabricating one would be dishonest (root §6.5). Instead I verified the behavior
rigorously by code-trace and locked it with a CI regression guard.

## Why the R6 D8 symptom no longer occurs (full trace)

D8 (R6 defer register): *"Compose tab → Blank page mounts NON-editable; Open template editable. Both use
the SAME `mountBornInEditor` path; only the seed differs (`'<p></p>'` vs heading+para). Empty-seed-specific;
suspects at ComposeEditor.tsx:2252/2155 (setContent no-op against identical creation content)."*

Current code (post the DEF-08 born-in-editor rework + compose-r4 task 027 content-model cutover + r6 task
052 atomic docxBytes/seedHtml):

1. **Both** `handleBlankRequested('<p></p>')` and `handleTemplateRequested(COMPOSE_BLANK_TEMPLATE_HTML)`
   call `mountBornInEditor(html, UNTITLED_DOC_NAME)` → dispatch `{ kind: 'mountDraftHtml', html, … }`
   (`ComposeWorkspace.tsx` ~3260/3277/3281).
2. The `mountDraftHtml` reducer (`ComposeWorkspace.types.ts` ~629) sets **`status: 'loaded'`**,
   **`docxBytes: null`**, **`seedHtml: action.html`**. Its own comment (lines ~656–658) states: *"docxBytes
   is also null for this mount kind (the editor seeds directly from initialHtml), so the editor's docx-mount
   branch (projection / error-unavailable) is never reached here."* The reference-only state is set ONLY
   inside that docx-mount branch → **unreachable for born-in-editor mounts.**
3. `showEditor = status === 'loaded' || 'saving'` (`ComposeWorkspace.tsx` ~3879) → true → `<ComposeEditor
   docxBytes={state.docxBytes /* null */} initialHtml={state.seedHtml /* '<p></p>' */} />` mounts.
4. ComposeEditor mount effect (`ComposeEditor.tsx` ~2318): `!docxBytes` → clears `setReferenceOnly(null)` →
   `if (initialHtml && initialHtml.length > 0)` — **`'<p></p>'.length === 7 > 0`**, identical treatment to
   the template — → `editor.commands.setContent(initialHtml)` and returns. `referenceOnly` stays null.
5. Editability = `!referenceOnly` (the only reference-only render branch is `if (referenceOnly) return …`).
   There is **no `setEditable(false)`** anywhere and no seedHtml-emptiness editability gate. → **editable.**

The R6 "setContent no-op against identical creation content" suspicion (the editor is created with
`content: '<p></p>'`, so blank's `setContent('<p></p>')` is a no-op) is real but **irrelevant to
editability**: the post-setContent code (`captureParaIdSnapshot`, `opLogRef.reset`, `dirtyRef=false`,
`onDirtyChange(true)`) runs regardless, and editability never depended on the transaction firing. Whatever
made blank non-editable at R6 (a since-removed reference-only-on-empty gate, or docxBytes not being nulled)
no longer exists.

Corroboration from EXISTING CI tests: `ComposeEditor.aiToolbarTriggers` / `.scroll` / `ToolbarPolish` all
mount `<ComposeEditor docxBytes={null} …>` and successfully find `role="textbox"` — i.e. a born-in-editor
mount with null docxBytes is already proven editable across the suite.

## What shipped — regression guard only (locks FR-08 so it can't silently regress)

- **NEW `ComposeEditor.blankPageEditable.test.tsx`** (CI-only — needs `@spaarke/*` resolution):
  1. blank seed `'<p></p>'` (docxBytes null) → editable `role="textbox"`, NO `compose-reference-only`;
  2. non-empty template seed → editable too (the D8 parity control);
  3. genuinely non-editable non-docx buffer (`%PDF-1.4`, `.pdf` fileName) → `compose-reference-only`, NO
     textbox (negative regression guard — the reference-only routing is preserved).
- **No production change** to `ComposeWorkspace.tsx` / `ComposeEditor.tsx` (§11: nothing added — the fix
  is already present; adding a parallel mount path was explicitly forbidden and unnecessary).

## Verification

- **Standalone jest: 642 pass / 0 fail** (unchanged — no src change; the new test is in the CI-only group,
  +1 load-failure by design, same boundary as every other ComposeEditor mount test).
- **No BFF bytes** → publish/CVE unchanged.

## Gates (Step 9.5 — test-modifying ⇒ mandatory per CLAUDE.md §8)

- **code-review: PASS** — regression guard mirrors the shipped `ComposeEditor.referenceOnly` harness
  (auth mock, docxBridge mock as a reintroduced-reader tripwire, byte-signature buffers); no smells.
- **adr-check: PASS** — ADR-038 KEEP-category behavior regression guard (asserts real user-facing
  editability, not a wiring/DI/`Mock<HttpMessageHandler>`/ctor-null banned test); NFR-06 `docxBridge.ts`
  mocked, not deleted; ADR-049 save path untouched.

## Owner note (flagged, not silent)

070 shipped as a **verification + regression guard, not a code fix**, because the current code already
satisfies FR-08. If the owner has a specific host context where Blank page still mounts non-editable (e.g.
a particular Xrm-dialog embed), please share the repro and I'll diagnose that path — the generic
three-pane / code-page mount is confirmed editable.

## Phase 7: 070 DONE (17→18/20). Next: 071 (restore-from-source no-blank, xhigh) → 072 (add-comment) → 074 (apply-template ETag/404) → 090.
