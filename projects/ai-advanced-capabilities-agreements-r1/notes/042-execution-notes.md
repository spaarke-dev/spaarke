# Task 042 — Execution Notes: Separated, location-labelled Assistant confirmations

> Rigor: FULL · Model tier: sonnet @ medium · Step mode: directional · Status: complete

## Step 0 — locate the renderer + what location metadata is available

Traced the `COMPOSE_EDIT_CONFIRMATION` seam per the POML's pointer: `dispatchComposeAction` in
`ConversationPane.tsx` (~L1086 pre-task) builds the per-note "What I changed" confirmation and injects
it via `makeComposeEditControlsMessage(confirmationText, { ledgerRef, bindingId })`. Confirmed this file
is exactly where 041 left it — 041's own execution notes explicitly record `ConversationPane.tsx`
UNTOUCHED (the batch loop lives in `ComposeEditor.tsx`), so the baseline commit `f0e1c8144` pointer held.

**The location-metadata investigation is the substantive finding of this task.** Traced every path that
could carry a clause-location string into this renderer:

1. **`ComposeActionRequest`** (`useSerialActionQueue.ts`) — fields are `id`, `bindingId`, `args?.slots`,
   `documentSessionId?`, `revisionScope?`. `args.slots` is whatever `ComposeEditor.tsx`'s
   `dispatchNoteToolRequest` builds: `selectionText`, `selectionAnchorStart`, `selectionAnchorEnd`,
   `documentSpeId`, `documentRecordId`, `sessionId`, optional `instruction`. **No location field.**
2. **`dispatched.result`** (the AI action's own output) — checked all 9
   `infra/dataverse/outputschemas/compose-*.schema.json` files. The two schemas the note-tool edit path
   actually returns (`compose-draft-alternative`, `compose-revise-document`) have NO location/sectionRef
   field (`target_text`/`new_text`/`match_mode`/`rationale`/`sources` only). One UNRELATED schema
   (`compose-summarize-word-changes`) does carry `changes[].location` — but that's a different action,
   not the edit-confirmation path this task targets.
3. **`deriveClauseLocationLabel`** (`clauseLocation.ts`, the task's own named source of truth) is called
   in exactly two places today: `ComposeCommentGutter.tsx` (the note card's own display) and
   `ComposeEditor.tsx:2421` (an unrelated highlight-thread object). **Neither call's result is ever
   threaded into `enqueueComposeAction`'s request.** Both call sites are in `@spaarke/compose-components`
   — read-only for this task per the HARD BOUNDARIES.

**Conclusion**: today, ZERO callers of `enqueueComposeAction` (the two shipped ones —
`ComposeAiToolbar.tsx`'s inline toolbar and `ComposeEditor.tsx`'s `dispatchNoteToolRequest`) populate a
location signal that reaches `ConversationPane.tsx`. Closing that gap requires editing
`ComposeEditor.tsx` (one field added to `dispatchNoteToolRequest`'s `slots`, using the
`editor.state.doc`/`span.from`/`thread.sectionRef` already in scope there) — outside this task's file
boundary (`ConversationPane.tsx` is the only writable file; `src/client/shared/**` is read-only).

## Step 0.5 — design decision under the boundary (directional mode, CLAUDE.md §8.5)

Given the confirmed wiring gap and the read-only boundary, three options were considered:

- **Fabricate a "location" from available data** (e.g. quote `target_text`, the result's verbatim
  clause substring) as a stand-in. Rejected: it isn't a location, would be semantically misleading
  under a header literally named for `deriveClauseLocationLabel`'s Pg/Sec/Para/Heading convention, and
  — decisively — **breaks the existing locked test** (see below).
- **Always render a generic filler header** ("Clause update") whenever an edit confirmation renders.
  Rejected for the same decisive reason (breaks the locked test) and because a repeated, identical
  label on every entry of a batch would not actually help a reviewer tell entries apart — it would just
  be noise.
- **Build the header mechanism to be forward-compatible: render when a location IS resolvable, omit
  cleanly when it is not.** Selected. This is discovered to be the ONLY option consistent with the
  existing test suite:

`ConversationPane.compose-edit-controls.test.tsx` (the DEF-12 forcing test, explicitly protected by
this task's ADR-041 constraint — "existing tests must pass untouched") asserts, with an EXACT
`.toBe(EXPECTED_CONFIRMATION)` match, that a no-location dispatch payload produces
`${COMPOSE_EDIT_CONFIRMATION}\n\n**What I changed:** ${RATIONALE}` — i.e. **no header at all**. Any
unconditional header text (generic or content-derived) would break this locked assertion, which this
task is explicitly forbidden from editing. This resolved the ambiguity in acceptance criterion #2
("renders a graceful generic header (no undefined)") in favor of: satisfy "no undefined" and
"graceful" by cleanly OMITTING the header rather than fabricating a non-distinguishing one — never by
printing literal `undefined` or any filler string.

This is a directional-mode adaptation (root CLAUDE.md §8.5), not a silent scope reduction: it is
disclosed here, in the code's own doc comments, and in the final report, with a concrete, actionable
fast-follow recommendation below.

## Step 1 — implementation (`ConversationPane.tsx`)

Two new pure helper functions, placed beside the `COMPOSE_EDIT_CONFIRMATION` /
`COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION` constants they extend:

- **`extractComposeEditLocationLabel(request, result)`** — defensively checks `locationLabel` (the
  established field name — mirrors `ComposeEditor.tsx`'s own `locationLabel: deriveClauseLocationLabel(...)`
  convention), then `sectionRef`/`location`, first on `request.args?.slots`, then on the dispatched
  `result` payload. Returns `null` today (universal case), and will activate the moment ANY future
  caller populates one of these fields — zero further change needed in this file.
- **`withComposeEditLocationHeader(confirmationText, locationLabel)`** — prepends `### {label}\n\n` when
  resolvable, else returns `confirmationText` unchanged.

Wired into `dispatchComposeAction`'s existing confirmation-building block: computed right after
`extractComposeEditExplanation(dispatched.result)` (the existing "What I changed" extraction), applied
before the `injection.enqueue(...)` call — no change to `store-before-render` sequencing, no change to
`ledgerRef`/`bindingId` targeting, no change to the accept/reject/try-another controls (ADR-041 scope
intact).

**Why `###` markdown, not a custom component (ADR-021 tokens)**: `ConversationPane.tsx` never renders
its own JSX for message content — `makeComposeEditControlsMessage` builds an `IChatMessage` whose
`content` is a markdown STRING that `SprkChatMessageRenderer` (`@spaarke/ui-components`, read-only)
renders via `renderMarkdownHtml` + the shared `SPRK_MARKDOWN_CSS` stylesheet
(`src/client/shared/Spaarke.UI.Components/src/services/renderMarkdown.ts`). That stylesheet already
styles `h3` with `font-weight: var(--fontWeightBold)`, `margin-top: var(--spacingVerticalL)`,
`margin-bottom: var(--spacingVerticalS)` — Fluent v9 SEMANTIC TOKENS, dark-mode-safe by construction,
identical to every other Assistant markdown message in the app. Emitting a `###` heading therefore gets
BOTH the "bold" and the "clear whitespace separation" acceptance criteria for free, with genuinely ZERO
new styling surface — the correct reading of "spacing via Fluent v9 tokens" for a file that owns no CSS
of its own.

## Step 2 — verification

- **New test file**: `ConversationPane.compose-edit-location-header.test.tsx` (6 cases, same harness
  pattern as the DEF-12 forcing test — real dispatch, wire-boundary fetch mock, one per-session
  in-memory ledger):
  1. resolved `locationLabel` on request slots → `### {label}\n\n` prefix, bold + separated.
  2. no location anywhere → graceful fallback, byte-identical to pre-042 (`.toBe` exact match), no
     `undefined` anywhere in the content.
  3. whitespace-only `locationLabel` → treated as unresolved (trimmed-empty guard), same graceful path.
  4. location resolved from the RESULT payload (`sectionRef`) when request slots carry none — proves
     the forward-compat fallback branch.
  5. a 041-style batch of 3 sequential dispatches (2 with distinct locations, 1 without) → 3 separate
     messages, each independently headed or gracefully bare, with 3 distinct `ledgerRef`s proving they
     are 3 independent confirmation entries (the "reads as distinct per-clause outcomes" acceptance
     criterion — inter-entry separation itself is inherited from SprkChat's existing per-message bubble
     layout, unaffected by and out of scope for this file).
  6. whole-document revision scope (`COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION`) gets the identical
     header treatment — proves the mechanism isn't selection-edit-only.
- **Existing locked test** (`ConversationPane.compose-edit-controls.test.tsx`) — NOT edited; re-run and
  confirmed passing unchanged (3/3), proving the no-location path is byte-identical to before.
- **Full SpaarkeAi suite**: `npx jest` → **91 suites / 838 tests, all green** (baseline 90/832 + this
  task's 6 new tests in 1 new suite; zero regressions).
- **Typecheck**: `npx tsc --noEmit -p .` — 0 errors attributable to `ConversationPane.tsx` or the new
  test file (73 pre-existing errors in unrelated shared-lib files, confirmed via the build's own
  `tsc-surface-gate: 73 pre-existing error(s) in shared libs (deferred to Phase B). Surface-owned: 0.`).
- **Build**: `npm run build` (SpaarkeAi) — green (vite build + ribbon build both succeed).
- **Lint**: `npm run lint` fails to invoke in this environment (`'eslint' is not recognized`) — the
  SAME pre-existing repo-wide ESLint v9 flat-config gap 041's own notes documented; not attempted to fix
  (out of this task's scope).

## Step 3 — quality gates (self-run, FULL rigor)

- **code-review** (self-run against the diff): 0 Critical, 0 AI code smells across all 5 categories
  (no new interfaces, no try/catch, the `unknown`-typing guards are legitimate runtime narrowing not
  defensive null-checks, comments explain rationale rather than restating code, both new functions are
  single-purpose and <15 lines). Security/performance: no new surface (no I/O, no user-input handling
  beyond what the existing `explanation`/`rationale` fields already do into the same DOMPurify-sanitized
  markdown pipeline). Component justification (CLAUDE.md §11 / Step 6.6): N/A — both new functions are
  private in-file helpers extending EXISTING logic, not a new service/endpoint/DI/package/column.
- **adr-check** (self-run): ADR-021 Compliant (see "why `###` markdown" above — zero new styling
  surface, tokens-only by construction). ADR-041 Compliant — store-before-render sequencing unchanged;
  one low-confidence note flagged for reviewer awareness: when the (currently dormant) request-slots
  branch of `extractComposeEditLocationLabel` eventually fires, that value originates from the outbound
  request rather than stored ledger evidence — read as compliant because a location label identifies
  WHICH clause is discussed (descriptive metadata), not a completion/outcome claim, so it does not fall
  under the "MUST NOT render a completion claim... without stored ledger evidence" rule. ADR-039/ADR-030
  unaffected (no new dispatch mechanism, no new PaneEventBus discriminant).

## Acceptance criteria — evidence

| Criterion | Status | Evidence |
|---|---|---|
| Each confirmation shows a bold clause-location header; entries clearly separated (spacing token) | ⚠️ Built + tested, **not yet visible in production** | Mechanism proven via 6 new tests (bold `###` header, token-based spacing via `SPRK_MARKDOWN_CSS`). Runtime activation is blocked on a wiring gap in `ComposeEditor.tsx` (read-only for this task) — see Deviation summary below. |
| A finding with no resolvable location renders a graceful generic header (no "undefined") | ✅ Pass | `ConversationPane.compose-edit-location-header.test.tsx` tests 2–3; never renders the literal string `undefined`; today's universal no-location case is exercised continuously by the (untouched) DEF-12 forcing test. |
| Negative: confirmation semantics/controls unchanged (existing tests pass untouched) | ✅ Pass | `ConversationPane.compose-edit-controls.test.tsx` — 0 edits, 3/3 passing; full suite 91/838 green. |

## Deviation / escalation summary

**This task cannot fully deliver FR-12's headline outcome (real clause-location text visible on
confirmations) within its own file boundary**, and this is disclosed explicitly rather than papered
over:

- **What was built**: the complete rendering mechanism (bold `###` header + token-based spacing +
  graceful fallback + forward-compat data extraction), fully tested, zero regressions, zero boundary
  violations.
- **What is missing for the feature to be VISIBLE in production**: a location signal has to reach
  `ConversationPane.tsx`. The natural, minimal fix is a ONE-LINE addition in `ComposeEditor.tsx`'s
  `dispatchNoteToolRequest` (`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx`,
  ~L2536): add `locationLabel: deriveClauseLocationLabel(editor.state.doc, span.from, thread?.sectionRef)`
  to the `slots` object being built (the function, the `doc`, and `span.from` are ALL already in scope
  at that exact call site — this is a genuinely small, low-risk follow-up, not a redesign). The moment
  that lands, this task's header activates with **zero further `ConversationPane.tsx` changes** — the
  extraction already checks the exact field name that addition would set.
- **Why this wasn't done here**: `src/client/shared/**` is explicitly read-only for task 042 (HARD
  BOUNDARIES — "ConversationPane.tsx is YOURS now... everything else read-only:
  src/client/shared/**, src/server/**, infra/**"), stated to avoid concurrent-edit conflict with the
  parallel wave (051 also touches `@spaarke/compose-components`). This is a genuine, disclosed
  data-availability gap, not a corner cut — see root CLAUDE.md §6 "Ambiguous or conflicting
  requirements" escalation trigger. No ADR is violated (§6.5 does not apply — this is a task-scope /
  file-boundary tension, not an ADR conflict), so the full §6.5 ceremony isn't the right instrument;
  this is reported as a direct finding instead, per the task's own Step 0 instruction to "check what
  metadata reaches the renderer" and report honestly.
- **Recommendation**: a small fast-follow task (owner-scoped, touches `ComposeEditor.tsx`) to thread
  `locationLabel` through BOTH `ComposeEditor.tsx`'s note-tool path and (optionally)
  `ComposeAiToolbar.tsx`'s inline-toolbar path, so every `COMPOSE_EDIT_CONFIRMATION` producer — not just
  note-tools — gets the header. Given task 060 (deploy) depends on 042, this should be flagged to the
  wave orchestrator now rather than discovered at UAT.

No other deviations. No ADR conflict (§6.5 N/A — the tension is a file-boundary/task-scope one, not an
ADR MUST/MUST NOT violation). No security-sensitive surface touched. No breaking API/schema change.

## Deferred (per task brief)

Live UI tests (batch readability in a running app, dark-mode toggle) — deferred to tasks 060/061 per
the task brief's own instruction; not attempted here (no `--chrome` session / deployed environment in
this task's scope). Note for 060/061: because the wiring gap above is unresolved, a live UAT batch run
TODAY will show confirmations with NO location headers (graceful, but not the intended-looking feature)
— the fast-follow recommendation above should land before or alongside 060/061's UI verification for the
headline UX outcome to be observable.
