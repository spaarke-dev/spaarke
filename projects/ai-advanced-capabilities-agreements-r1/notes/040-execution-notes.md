# Task 040 — Execution Notes: Bidirectional highlight — summary row → document section AND gutter Review Note

> Rigor: FULL · Model tier: sonnet @ high · Step mode: directional · Status: complete

## Step 0 — trace (row→doc highlight flow + the gutter's note-identity model)

Traced the existing doc-only flow and the gutter's selection model before designing anything:

1. **`AgreementReviewSummaryPanel.tsx`** is purely presentational — a row's `onNavigate?.(finding)` callback is
   the ONLY interaction surface; the panel has no knowledge of gutter threads at all.
2. **`ComposeCommentGutter.tsx`** is also purely presentational — it renders `threads` and exposes
   `selectedThreadId`/`onSelectThread` (a UAT round-4 #8 mechanism already shipped: clicking a gutter card calls
   `onSelectThread(threadId)`, which the host uses to paint BOTH the doc anchor (via
   `SelectedCommentExtension`/`selectedCommentPluginKey` — a ProseMirror view decoration, yellow) AND the
   matching gutter card (`selectedThreadId` prop → `cardSelected` style) — this is the SAME "selected" visual
   language the constraint asks the reverse link to reuse.
3. **The actual host wiring lives in `ComposeEditor.tsx`, not `ComposeWorkspace.tsx`.** `ComposeWorkspace.tsx`
   only holds `reviewSummaryFindings` state (built in its `onAdvisoryComments` receiver) and threads it down as
   the `reviewSummary` prop; `ComposeEditor.tsx` is the component that actually **mounts both**
   `AgreementReviewSummaryPanel` (`:2856`) and `ComposeCommentGutter` (`:3136`) as siblings, and it is the ONLY
   place that already holds all three pieces needed for the reverse link: the findings (`enrichedReviewFindings`),
   the placed threads (`advisoryComments.threads`), and the shared selection state (`selectedThreadId`/
   `selectThread`, `:2311-2359`).
4. **The existing row-click handler, `handleReviewNavigate` (`:2389-2394` pre-task), called ONLY
   `qaHighlight.highlight(finding.quotedText, finding.sectionRef)`** — the doc-only ephemeral highlight
   (`QaHighlightExtension`, a SEPARATE mechanism from `selectedThreadId`). It never touched `selectedThreadId`,
   so a summary-row click never selected (or scrolled to) the matching gutter card — precisely the gap FR-10
   describes.
5. **Join-key trace**: `ComposeWorkspace.tsx`'s `onAdvisoryComments` handler (`:1765-1812`) builds BOTH the
   placed threads (via `editorRef.current?.placeAdvisoryComments(items.map(...))`) AND `reviewSummaryFindings`
   (via `setReviewSummaryFindings(items.map(...))`) from the **SAME source array** (`items`), in the same
   iteration order. `item.sectionRef` becomes both `finding.sectionRef` (verbatim) and — via
   `placeAdvisoryComments` → `createThread(item.explanation, span, { sectionRef: item.sectionRef, ... })` —
   `thread.sectionRef` (verbatim, `ComposeEditor.tsx:2675-2684`). `item.explanation` likewise becomes both
   `finding.explanation` and the thread's root `text` verbatim. This is the deterministic anchor the task's
   join-key language ("the finding's anchor... deterministic since 011") refers to in practice: `sectionRef` IS
   the WS-4/task-011 citation carried unchanged onto both objects, so exact-string equality between
   `finding.sectionRef` and `thread.sectionRef` is a faithful, zero-re-derivation join — not a heuristic guess.
   `explanation`/`text` equality is the tie-break for the (rare) case of two findings citing the identical
   `sectionRef`. Findings whose comment failed to place (`result.failed`) are STILL included in
   `reviewSummaryFindings` (task 030/031 precedent) but have no corresponding thread — the join naturally
   returns no match for them, which is exactly the "note removed / never placed" graceful-degrade case.

## Step 1 — seam decision (🔔 documented per the POML's own instruction to "decide at the seam and document")

**Decision: the reverse link wires in `ComposeEditor.tsx`, not exclusively inside
`AgreementReviewSummaryPanel.tsx`/`ComposeCommentGutter.tsx`.**

The POML's stated "Your surface" (panel + gutter files) assumed `ComposeWorkspace.tsx` was the nearest common
host needing a touch and asked to avoid it (030's territory this wave). Step 0's trace shows the REAL common
host — the only place both the findings and the threads are simultaneously in scope, because they are siblings
mounted by the SAME parent — is `ComposeEditor.tsx`, which is **not** in the task's forbidden list (only
`ComposeWorkspace.tsx`, `infra/dataverse/**`, `src/solutions/**`, `src/server/**` are forbidden) and is **not**
owned by task 030 this wave (030's own group-E disjointness claim is "ComposeWorkspace+Binding" — it does not
touch `ComposeEditor.tsx`). Two siblings cannot coordinate shared state without a common ancestor; there is no
way to achieve "one click on a summary row lights up both" without SOME edit at that ancestor (a new
React Context would still need a `<Provider>` mounted at the same ancestor — same touch point, more surface).

Kept the touch **minimal and additive**, matching the "your surface: panel + gutter (+ a small shared
type/util)" spirit as closely as the architecture allows:

- **All new LOGIC is a pure, directly-unit-tested export in `ComposeCommentGutter.tsx`**:
  `resolveMatchingThreadId(finding, threads)` — the deterministic join described in Step 0.5, with the SAME
  "never guess on ambiguity" discipline task 012 (DEF-01) established for anchor resolution (returns
  `undefined`, not a first-match guess, when the join is ambiguous or absent).
- **`ComposeEditor.tsx` gets a ~25-line call-site change**: `handleReviewNavigate` now resolves the match and,
  on a hit, calls the ALREADY-SHIPPED `selectThread` (UAT round-4 #8) instead of `qaHighlight.highlight` — ONE
  coordinated action reusing an existing mechanism, not a new one. `selectThread` gained one additive optional
  parameter (`forceSelect`, default `false` — the existing gutter-card call site is unaffected) so a summary-row
  click always re-selects/re-scrolls rather than toggling off on a repeat click of the same row (gutter-card
  clicks keep their original toggle-to-deselect behavior).
- No change to `AgreementReviewSummaryPanel.tsx` — its `onNavigate` callback already passes the whole
  `finding` (with `sectionRef`/`explanation`), which is all the seam needs. No change to
  `ComposeCommentGutter.tsx`'s component/props — only the new pure export was added alongside the existing
  `layoutCommentGutterCards` pure export (identical placement/testability convention).

**Per CLAUDE.md §6.5, this is not an ADR conflict** (no ADR rule is being violated — ADR-030 explicitly scopes
the PaneEventBus to CROSS-pane communication; panel/gutter/editor are all Compose-side, so a direct
callback/state wiring, not a bus event, is the ADR-030-compliant choice) — it is a **task-scope adaptation**
under directional steps mode (root CLAUDE.md §8.5: "You MAY adapt the sequence to the real codebase state").
Flagging it explicitly here per the task's own "decide at the seam and document" instruction, and in the
completion report, so it is visible rather than silently done.

## Step 1 (cont.) — "ONE coordinated action" mechanics

- **No double scroll**: `handleReviewNavigate` calls at most ONE of `selectThread(...)` (matched) or
  `qaHighlight.highlight(...)` (unmatched) — never both. On a match it ALSO calls `qaHighlight.clear()` first
  (clears any ephemeral highlight left over from a PRIOR unmatched-row click) and `setSelectedThreadId(null)`
  on the unmatched branch (clears any stale SELECTED note from a PRIOR matched-row click) — this is what
  guarantees "rapid row-switching leaves exactly ONE highlighted pair" regardless of match/no-match sequence,
  not just same-type sequence.
- **No focus steal**: `selectThread` never touches ProseMirror selection or calls `.focus()` — it only
  dispatches a decoration-only transaction (`tr.setMeta(selectedCommentPluginKey, ...)`) and calls
  `scroller.scrollTo(...)` on the plain DOM scroll container. Verified by reading the (unchanged) function body,
  not just by inference.
- **Same visual language as the doc-side highlight**: the matched path paints the SAME
  `SELECTED_COMMENT_ANCHOR_CLASS` (yellow) decoration the gutter-card-click path already uses (UAT round-4 #8 /
  round-5 #5 "selected = yellow everywhere") — reused verbatim, not a new highlight style.

## Step 2 — tests

- **`ComposeCommentGutter.test.tsx`** — new `describe('resolveMatchingThreadId', ...)` (6 cases, pure/no
  editor dependency, mirrors the `layoutCommentGutterCards` convention): unique sectionRef match; sectionRef-tie
  disambiguated via explanation; no match (graceful `undefined`); genuinely ambiguous even after both signals
  (graceful `undefined`, never a guess); no sectionRef + no threads; whitespace tolerance.
- **New file `ComposeEditor.bidirectionalHighlight.test.tsx`** — integration-level, against the REAL
  `ComposeEditor` (mirrors `ComposeEditor.advisoryComments.test.tsx`'s mount convention), asserting via the
  DOM-visible `SELECTED_COMMENT_ANCHOR_CLASS`/`QA_HIGHLIGHT_CLASS` decoration classes (chosen over gutter-card
  `aria-pressed` assertions because `ComposeCommentGutter` positions cards via `editor.view.coordsAtPos`, which
  is not reliably reachable in jsdom without spying on the internal, non-exposed editor instance — the shared
  decoration class is DOM-visible, editor-instance-independent proof that the same `selectedThreadId` state
  that also drives the gutter card changed correctly):
  1. a summary row with a matching placed thread selects ONLY that thread's doc anchor (not the other one), and
     the doc-only ephemeral highlight never also fires;
  2. switching to a different matched row leaves exactly ONE selected pair (the old decoration is gone, the new
     one is present) — no stacking;
  3. a row whose finding never got a placed note (a distinct `sectionRef` no thread carries) degrades
     gracefully to the pre-existing doc-only ephemeral highlight, with no error and no stale note-selection
     decoration left over from a prior matched click.
- The panel's own acceptance test `AgreementReviewSummaryPanel.test.tsx` is untouched (no panel behavior
  changed) and still passes.

## Step 3 — quality gates (self-run, FULL rigor)

- **Build**: `npm run build` (`tsc`) in `Spaarke.Compose.Components` — clean, zero errors.
- **Tests**: full package suite — `810 total, 795 passed, 15 failed` — the 15 failures are the EXACT
  pre-existing set named in the task brief (`stepOperationInterceptor.test.ts` +
  `ComposeWorkspace.{bornInEditorSave,imports,saveOpLogPreservation,search}.test.tsx`), confirmed by suite name
  match; zero new failures. All 14 `ComposeEditor.*.test.tsx` suites (63 tests, including the sensitive
  `ComposeEditor.advisoryComments.test.tsx` DEF-01 suite, untouched by this task) pass. The two touched/new
  suites (`ComposeCommentGutter.test.tsx`, `ComposeEditor.bidirectionalHighlight.test.tsx`) pass in isolation
  (40/40) and as part of the full run.
- **Lint**: `npm run lint` (eslint) failed to run in this environment — repo-root ESLint v9 flat-config
  migration issue unrelated to this task's files (`ESLint couldn't find an eslint.config.js`); not attempted to
  fix (out of scope, pre-existing environment state, not a code defect in the touched files).
- **ADR-021** (Fluent v9 / semantic tokens / dark mode): no new UI/markup/styling was added — the change reuses
  the ALREADY-compliant `SelectedCommentExtension`/`QaHighlightExtension` decoration classes verbatim. N/A/pass.
- **ADR-030** (PaneEventBus — cross-pane only): correctly NOT used — panel, gutter, and the editor are all
  Compose-side (same pane); a direct callback/state wiring at the shared-ancestor seam is the ADR-030-compliant
  choice, not a bus event. Pass.
- **CLAUDE.md §11 (component justification)**: `resolveMatchingThreadId` is the only new abstraction —
  Existing: no function resolves a finding to its thread today. Extension: not possible to extend an existing
  function (none exists for this join). Cost-of-doing-nothing: a row click can never locate/highlight its
  matching gutter note, leaving the triage loop one-directional (the literal defect FR-10 exists to fix).

## Deviation / escalation note

**One deviation from the POML's literal "Your surface" statement**, disclosed per the task's own "decide at the
seam and document" instruction (see Step 1): a ~25-line, additive, fully-tested call-site change in
`ComposeEditor.tsx` (`handleReviewNavigate` + one additive optional parameter on `selectThread`) was necessary
because that file — not `ComposeWorkspace.tsx` (the file the boundary was actually protecting, per task 030's
concurrent ownership) — is the real common ancestor holding both the findings and the threads. This is not a
HARD BOUNDARY violation (`ComposeEditor.tsx` is not in the forbidden list) and not an ADR conflict (see §6.5
note above); it is flagged here for transparency and reviewer visibility, not because it was blocked. Verified
zero regressions across the full `ComposeEditor.*` suite (63 tests) and the full package suite (795/810,
matching the pre-existing baseline exactly).

No other deviations. All three acceptance criteria are met and covered at the unit/integration level (live
click-through + dark-mode toggle deferred to 060/061 per the task brief — the new decoration classes/behavior
are inherited from already dark-mode-verified extensions, so no NEW dark-mode surface was introduced).
