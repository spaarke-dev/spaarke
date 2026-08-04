# Task 092 Completion Notes — Convert `sprk_DocumentOperations.js` DOM Overlay (FR-18)

> RIGOR declared: **FULL** (override of POML's STANDARD — dispatch instruction: code-modifying, overlay retirement is spec Success Criterion §5). Author: task-092 execution, 2026-08-02.

---

## 1. What changed

`Spaarke.Document.showChoiceDialog` (a generic hand-rolled `window.top.document` DOM-overlay builder — `createElement`, `position:fixed`, manual ESC/cleanup handlers) is **removed**. Its sole caller, `Spaarke.Document.showDocumentLockedDialog(checkoutInfo, documentName)`, is **reimplemented** to chain two `Xrm.Navigation.openConfirmDialog` calls (a SUPPORTED client API) instead. The function's external signature and return contract (`Promise<'view'|'download'|null>`) are unchanged, so its two callers (`openInWeb`, `openInDesktop`) required **zero changes**.

Applied identically to both copies:
- `src/client/webresources/js/sprk_DocumentOperations.js`
- `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js`

Version bumped `1.27.0` → `1.28.0` in both (Config object + JSDoc header), with a new changelog entry, matching this file's own established convention (every substantive change bumps `Config.version` + adds a header changelog entry — 5 prior precedents: 1.27.0, 1.26.1, 1.26.0, 1.25.1, 1.25.0). The historical v1.22.0 changelog line that originally announced `showChoiceDialog()` was annotated (not rewritten) with `(removed in 1.28.0, see above)` so future readers aren't confused by the historical record.

## 2. Choice-UX mapping (old overlay → supported path)

**Actual choice options** (unchanged from the original `showDocumentLockedDialog`): the ribbon calls this only when a document is checked out by someone else, from `openInWeb` and `openInDesktop`. Three outcomes, exactly as before:

| Outcome | Old DOM overlay | New supported path |
|---|---|---|
| **View Only** | Click "👁️ View Only" option row (visible simultaneously with Download) | Confirm 1st `openConfirmDialog` ("View Only" button) |
| **Download Copy** | Click "📥 Download Copy" option row | Decline 1st dialog ("More Options"), then confirm 2nd `openConfirmDialog` ("Download Copy" button) |
| **Cancel** | Click "Cancel" button, the × close button, or press ESC | Decline 2nd dialog ("Cancel" button, × close, or ESC — `openConfirmDialog` returns `{confirmed:false}` for all of these) |

Caller code (`openInWeb`/`openInDesktop`, unchanged) branches on exactly these three string values:
```js
var choice = await Spaarke.Document.showDocumentLockedDialog(checkoutInfo, docInfo.name);
if (choice === 'download') { await Spaarke.Document.downloadDocument(primaryControl); return; }
else if (choice !== 'view') { return; } // cancelled
// choice === 'view' - continue to open
```
This contract is fully preserved — the new implementation returns the identical three values from the identical branch points.

**Design choice rationale** (per dispatch prompt's "candidate supported paths, in rough preference order"): option (a) — `Xrm.Navigation.openConfirmDialog` / a chain of them — was selected because the choice is effectively **ternary confirm-style** (view / download / cancel), matching the dispatch's own classification criterion for when to prefer this option over (b) `navigateTo` to an existing surface. Option (b) was ruled out: grepped the repo for an existing custom page that could host a 3-way "document locked" choice (`DocumentLocked`/`documentlocked`) — **none found** — and the task explicitly forbids authoring a new page in this task. `sprk_CheckinCommentDialog` (the one existing `openDialog`-launched custom page referenced elsewhere in this same file, line ~975) is a single-purpose comment-entry page, not a choice picker — not reusable here. Option (c) (restructuring into distinct flows) wasn't needed — the two chained confirms cleanly separate the two decision points without restructuring `openInWeb`/`openInDesktop`.

**Honest UX deltas (documented, not hidden)**:
1. **Sequential vs. simultaneous.** The old overlay showed both options on one screen. The new flow asks "View Only?" first, then (only if declined) "Download instead?" — same three final outcomes, one extra click in the download path.
2. **Icons dropped.** `openConfirmDialog` button labels are plain text; the 👁️/📥 icons and the two-line title+description per-option layout (ADR-023 visual richness) don't carry over. This is an inherent trade-off of moving off custom-rendered chrome onto a host-native dialog — anticipated and accepted by the dispatch prompt's own note: *"ADR-021 applies only if you render Spaarke-owned chrome (ideally you render none — the OOB dialogs are host-chromed)."*
3. **Mailto contact link degrades to plain text.** The old overlay rendered a clickable `mailto:` link (with pre-filled subject/body) next to the checked-out-by name. `openConfirmDialog`'s `text` is plain text, not HTML — a `<a href="mailto:...">` would render as literal characters, not a clickable link. The new dialog instead states the contact's email in a plain sentence ("Contact <name> at <email> to request access."); the user can still read/select/copy it, just not one-click. This is the one meaningful, disclosed UX regression. I judged this **not** escalation-worthy per the task's own bar (*"if a supported path cannot reproduce the specific choice UX... STOP"*) — the **choice UX** (which is what routes to different code paths) is fully reproduced; only a **convenience nicety** (one-click mailto) is lost, and no supported Xrm dialog API renders HTML/links, so there is no available path that would preserve it without new-page authoring (out of scope).

## 3. Byte-consistency + grep proof

**Discovery, important**: the two copies were **not** actually fully byte-identical before this task, contrary to the dispatch prompt's framing. A precise `diff` (captured before AND after my edit, both saved) shows **3 pre-existing, unrelated drift regions**, all outside the choice-dialog code:

| Region | src/client has... | infrastructure lacks... |
|---|---|---|
| `_getEnvironmentVariable` (env var query) | `defaultvalue` fallback + explanatory comment | falls back to `null` only if no override value is set |
| BFF URL resolution | strips trailing `/api` suffix (`.replace(/\/api$/i, "")`) + comment | only strips trailing slash |
| `sendToIndex` | lowercases GUIDs (`.toLowerCase()`) for Azure AI Search case-sensitivity + explanatory comment; error field `errorMessage` + per-item error detail loop | no GUID lowercasing; error field `error`, no per-item loop |

This matches task 090's own inventory (`oob-navigateto-inventory.md` §10), which verified byte-consistency **only for the 8 raw `navigateTo` calls** in this file ("byte-identical duplicate... same 8 calls, same line offsets") — it never claimed whole-file parity. I did **not** touch these 3 regions: they are unrelated to FR-18 (the DOM overlay), changing them would be undisclosed scope creep into behavior I cannot test end-to-end (ribbon is manual-only), and CLAUDE.md §11 requires a concrete failure-mode justification I don't have grounds to assert without the task asking for it. Flagging as a **discovered, out-of-scope item** (mirroring the inventory's own §11 "Discovered but out-of-scope" convention) — a candidate for `/project-defer-issue-tracking` at the owner's discretion.

**What I verified is byte-consistent**: the region I actually converted. Proof — I diffed the two files both **before** and **after** my edit and diffed *those two diffs against each other*:
- Before-edit `diff`: 5 hunks (env-var, BFF-URL, 3× sendToIndex) — **102 lines** of diff output.
- After-edit `diff`: same 5 hunks, same content, only line numbers shifted (header changelog insert shifted early hunks +9; the large function-body reduction shifted later hunks -135) — **102 lines** of diff output.
- **Zero new hunks appeared** in or around the choice-dialog section after my edit → my conversion is verified byte-identical between both copies.

```
node --check src/client/webresources/js/sprk_DocumentOperations.js
→ SRC_CLIENT_SYNTAX_OK
node --check infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js
→ INFRA_SYNTAX_OK
```
(No `package.json`/eslint config exists under `src/client/webresources/**` — confirmed via glob — consistent with task 090's §5 architectural note that these are unbundled, no-build-step web resources. `node --check` is the correct fallback syntax gate per the task's own instructions.)

**Overlay-pattern grep** (both files, AC-1):
```
grep -n "createElement|position:fixed|window\.top\.document|showChoiceDialog\s*=|showChoiceDialog\(" <file>
```
Remaining matches in both files:
- `window.top.document` / `createElement`/`position:fixed` — appear **only inside comments**: my new changelog entry + JSDoc (describing the removal) and the pre-existing v1.26.1 historical changelog line (describing a past fix). Zero live code.
- `showChoiceDialog` — appears **only inside comments** (my new changelog entry, the pre-existing v1.26.1 line, and the annotated v1.22.0 historical line). The symbol `Spaarke.Document.showChoiceDialog` no longer exists as a function.
- Two **unrelated, pre-existing, out-of-scope** `createElement` calls remain in each file: `document.createElement('script')` in `_loadMsalLibrary()` (dynamic MSAL CDN script-tag injection) and `document.createElement('a')` in `downloadDocument()` (synthetic-anchor download trigger). Neither is `position:fixed`, neither builds a modal/overlay, both pre-date and are unrelated to FR-18 — flagging explicitly so this isn't mistaken for an incomplete conversion.
- `position:fixed` as actual CSS: **zero** occurrences in either file (only appears inside the descriptive comment text above).

AC-1 ("a grep for `createElement`/`position:fixed` overlay patterns in both files returns none [excluding comments]") is satisfied.

## 4. Ribbon-XML binding evidence

Only one ribbon-XML file in the whole `DocumentRibbons` solution folder references `sprk_DocumentOperations` at all:
`infrastructure/dataverse/ribbon/DocumentRibbons/Entities/sprk_Document/RibbonDiff.xml` (confirmed via repo-wide grep — no other XML file in the folder matches).

Command/rule bindings found there (all `Library="$webresource:sprk_DocumentOperations.js"`):
```
JavaScriptFunction FunctionName="Spaarke.Document.checkoutDocument"
JavaScriptFunction FunctionName="Spaarke.Document.checkinDocument"
JavaScriptFunction FunctionName="Spaarke.Document.discardCheckout"
JavaScriptFunction FunctionName="Spaarke.Document.deleteDocument"
JavaScriptFunction FunctionName="Spaarke.Document.refreshDocument"
JavaScriptFunction FunctionName="Spaarke.Document.openInWeb"
JavaScriptFunction FunctionName="Spaarke.Document.openInDesktop"
JavaScriptFunction FunctionName="Spaarke.Document.downloadDocument"
JavaScriptFunction FunctionName="Spaarke.Document.sendToIndex"          (×3 — form/grid/subgrid)
CustomRule FunctionName="Spaarke.Document.canCheckout"
CustomRule FunctionName="Spaarke.Document.canCheckin"
CustomRule FunctionName="Spaarke.Document.canDiscard"
CustomRule FunctionName="Spaarke.Document.canDelete"
CustomRule FunctionName="Spaarke.Document.canRefresh"
CustomRule FunctionName="Spaarke.Document.canOpenInWeb"
CustomRule FunctionName="Spaarke.Document.canOpenInDesktop"
CustomRule FunctionName="Spaarke.Document.canDownload"
CustomRule FunctionName="Spaarke.Document.canSendToIndex"
```
**None of these bind `showChoiceDialog`/`showDocumentLockedDialog` directly** — those are internal helpers only ever called from within `openInWeb`/`openInDesktop`. Since this task did not rename or change the signature of any ribbon-bound function above (all 9 command functions + 9 rule functions are byte-unchanged), **every ribbon-XML binding remains valid with zero XML changes required**. This is the strongest form of evidence available short of a live Dataverse deploy.

## 5. Manual test script (AC-4 — cannot be run in this environment; provided for the owner)

Precondition: a `sprk_document` record whose file is checked out **by a different user** than the tester (or simulate by having a second test user check it out).

1. As the non-checkout-owner user, open the `sprk_Document` form for the locked record.
2. Click ribbon button **"Open in Web"** (bound to `Spaarke.Document.openInWeb`).
3. Expect: after token pre-fetch, a native Dataverse confirm dialog titled **"Document Locked"** appears with text naming who has it checked out (+ their email, if known, as a plain sentence) and asking *"Open it for viewing only? Any changes you make will not be saved."*, buttons **"View Only"** / **"More Options"**.
   - **3a. Click "View Only"** → expect the document opens in Office Online (view path), no download, no further dialog.
   - **3b. Click "More Options"** → expect a **second** confirm dialog titled "Document Locked", text *"Download a local copy of "<name>" to edit offline instead? Changes won't sync back automatically."*, buttons **"Download Copy"** / **"Cancel"**.
     - **3b-i. Click "Download Copy"** → expect `downloadDocument` fires (file download triggered), function returns without opening Office Online.
     - **3b-ii. Click "Cancel"** (or ESC / ×) → expect the function returns silently; no download, no open, no error.
4. Repeat steps 2–3 for ribbon button **"Open in Desktop"** (bound to `Spaarke.Document.openInDesktop`) — same three outcomes, desktop-protocol URL instead of Office Online tab.
5. Regression check (functions untouched by this task, confirm still wired): Check Out, Check In, Discard Checkout, Delete, Refresh, Download (direct button), Send to Index (form/grid/subgrid) all still fire from their ribbon buttons — this task changed zero lines in any of these functions.

Expected overall: no `window.top.document` DOM overlay ever appears (no dark-backdrop custom panel); all dialogs are native Dataverse-chromed confirm dialogs.

## 6. FULL-rigor Step 9.5 gates (self-run)

### Self code-review
- **Overlay fully removed**: confirmed — `showChoiceDialog` function deleted entirely; grep proof in §3.
- **Ribbon bindings intact**: confirmed — no ribbon-bound function name/signature touched; XML evidence in §4.
- **Byte-consistency**: confirmed for the converted region — diff-of-diffs proof in §3; pre-existing unrelated drift disclosed, not silently "fixed" as scope creep.
- **Style match**: new code uses the file's existing `async function` + `await Xrm.Navigation.openConfirmDialog({...})` idiom (identical call shape already used unchanged in `checkinDocument`/`discardCheckout`/`deleteDocument`/`openInWeb`/`openInDesktop` in this same file) — no TypeScript, no imports, no new patterns introduced.
- **No dead code left behind**: verified no other caller of `showChoiceDialog` exists anywhere in the repo (grepped before deleting) besides the definition + the one call site I removed.
- **Finding for the main session** (cannot self-fix — `.claude/` boundary): `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` names `sprk_DocumentOperations.js`'s `showChoiceDialog()` as its **reference implementation** for the `window.top.document` overlay pattern, and frames DOM-overlay-building as an acceptable technique "when a web resource needs a rich multi-option dialog... that `openConfirmDialog` cannot provide." Both the cited reference function and (arguably) the guidance itself are now stale relative to this project's binding constraint (MUST NOT hand-roll `position:fixed`/`createElement` overlays). I did not edit this file (outside my two-file + notes boundary; `.claude/**` is explicitly off-limits for this task). Recommend the main session either update the pattern's reference example or retire/reframe the pattern, and/or route through `/project-defer-issue-tracking`.

### adr-check
- **ADR-006** (web-resource exception, stays JS not PCF): satisfied — no PCF rewrite; this remains a plain, unbundled `.js` web resource with the same `ADR-006 Exception: Approved for ribbon button invocation` header line unchanged. The fix moved the choice UX onto a SUPPORTED client API (`Xrm.Navigation.openConfirmDialog`) rather than restructuring the surface type.
- **No unsupported DOM**: satisfied — `window.top.document`/`createElement`/`position:fixed` overlay code fully removed (§3).
- **NFR-05 (client-only, zero BFF impact)**: satisfied trivially — this task touched exactly two client-side `.js` web resource files; zero changes to `src/server/api/Sprk.Bff.Api/**` or any BFF-adjacent service.
- **ADR-021 (semantic tokens, no hex/inline color)**: not triggered — per the task's own framing, this conversion renders **zero** Spaarke-owned chrome; both dialogs are host-chromed native `Xrm.Navigation` dialogs with no custom styling authored.
- **ADR-023 (Choice Dialog pattern — 2-4 options, icon+title+description)**: visual richness (icons, stacked descriptions) is **not** reproduced — inherent to moving off custom DOM onto a native host dialog. Documented as an accepted, disclosed trade-off in §2, not a silent violation; the *decision* (view/download/cancel) is preserved even though the *chrome* is not. ADR-023's richer pattern remains fully intact for its actual home — the React-based `ChoiceModal` rebase elsewhere in this project (per project CLAUDE.md: "ChoiceModal is NOT in the prototype — build fresh (re-base ChoiceDialog, ADR-023)") — this ribbon-JS surface was never a candidate for that component (no module system, no React).

No ADR conflict requiring the §6.5 escalation protocol was found — path (C) "pivot to comply" was available and taken (chained supported dialogs reproduce the decision faithfully), so no exception/amendment needed.

## 7. Acceptance-criteria checklist

| # | Criterion | Status |
|---|---|---|
| AC-1 | Neither copy builds DOM in `window.top.document`; grep for `createElement`/`position:fixed` overlay patterns returns none | **PASS** — see §3 |
| AC-2 | `showChoiceDialog`'s behavior is served by a supported dialog / sanctioned launcher | **PASS** — `showDocumentLockedDialog` now uses chained `Xrm.Navigation.openConfirmDialog` (within `Xrm.Navigation`, a supported client API); see §2 for why this option was chosen over a `navigateTo`-to-existing-page path |
| AC-3 | Both copies converted identically, byte-consistent (diff clean) | **PASS for the conversion** — diff-of-diffs proof in §3 shows zero new asymmetry introduced. **Caveat, disclosed**: the two files were not fully byte-identical *before* this task (3 unrelated pre-existing drift regions, §3) — that drift is untouched, out of scope, and separately flagged |
| AC-4 | Ribbon command works end-to-end (dialog opens, selection routes correctly) | **Manual script provided, honestly not executed** — no live Dataverse ribbon environment available in this session; §5 gives the owner a precise step-by-step; static evidence (ribbon-XML binding intact, §4; syntax-valid, §3; unchanged caller contract, §2) supports correctness but does not substitute for the live check |

## 8. Deviations / escalations

No escalation triggered (the supported-path conversion succeeded without needing new-page authoring). Deviations, all disclosed above:
1. Discovered the two files had **pre-existing, unrelated drift** (3 regions) contradicting the dispatch prompt's "byte-consistent today" framing — documented in §3, left untouched, flagged as a candidate defer-issue.
2. **Mailto contact link** degrades from a clickable link to plain text — inherent to `openConfirmDialog` not supporting HTML — documented in §2 as an accepted UX trade-off, not an escalation-worthy gap.
3. `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` now has a **stale reference example** (cites the just-removed `showChoiceDialog()`) — cannot self-fix (`.claude/` boundary); flagged for the main session in §6.
4. Version bump (`1.27.0`→`1.28.0`) + changelog entry added in both files — not explicitly required by any AC, but matches this specific file's own established self-documentation convention (5 prior precedents) and the dispatch's "match the file's existing JS style" instruction.

## 9. Files touched by this task

- `src/client/webresources/js/sprk_DocumentOperations.js` (modified)
- `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js` (modified)
- `projects/spaarke-modal-system/notes/task-092-completion.md` (this file, created)

No other files touched. `TASK-INDEX.md`, `current-task.md`, `.claude/**`, `src/solutions/**`, `src/client/shared/**`, and `oob-navigateto-inventory.md` were not modified, per hard boundaries. No `git add`/commit performed.
