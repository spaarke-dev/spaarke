# Task 052 — Execution Notes: Word-comment export mirror + configurable author (FR-15)

> Rigor: FULL · Model tier: sonnet @ high · Step mode: directional · Status: complete

## Step 0 — re-located seam points (post-merge/post-rename) + seam-choice audit

Line refs in the POML/root-cause map are pre-merge (`compose-r5` + task 010's rename landed since). Re-located:

| Symptom | Pre-merge ref | ACTUAL location (this branch, 2026-07-31) |
|---|---|---|
| (a) hardcoded author | `ComposeEditor.tsx:2146` | `ComposeEditor.tsx:2196` — `useComposeCommentThreads(editor, 'AI Advisory Review')` |
| (b)/(c) relabel display-only | `ComposeCommentGutter.tsx:343-378` | `ComposeCommentGutter.tsx:327-382` (`AdvisoryNoteSegment`, `ADVISORY_NOTE_LABELS`, `ADVISORY_NOTE_DISPLAY_LABEL`, `parseAdvisoryNote`) |
| (d) standardRef never-export scope | `ComposeCommentThread.types.ts:89` | `ComposeCommentThread.types.ts:87-101` (the `ComposeCommentThreadModel` metadata doc block) |
| export mapping | `ComposeCommentThread.types.ts:256-262` | `ComposeCommentThread.types.ts:192-246` — `composeSessionCommentThreadsToAnchoredComments` (the LIVE save path; `composeCommentThreadsToDocxAnnotations` at `:118-142` is the retired/legacy sibling — still exported + tested, updated for consistency) |

**Task 002 confirmed COMPLETE** (status ✅, notes/002-execution-notes.md): the `agreement-review` Action's output
schema now emits `flaggedSections[{sectionRef, quotedText, riskLevel, flaggedClause, assessment, standardRef}]` —
`explanation` is gone server-side, replaced by discrete `flaggedClause` (grounded fact) + `assessment` (judgment).
**Task 010 confirmed COMPLETE**: `ndaClauseLocation.ts` → `clauseLocation.ts`, `NdaReviewSummaryPanel` →
`AgreementReviewSummaryPanel` (deprecated alias kept). Built on both renamed/split states throughout.

### IMPORTANT finding (not this task's file scope — documented, not fixed)

The client bridge that projects the review Action's ledgered `flaggedSections[]` into
`ComposeAdvisoryCommentItem`/`AdvisoryCommentInput` —
`src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts`
(`projectFlaggedSectionsToAdvisoryComments`) — **still reads the pre-002 `section.explanation` field**, which no
longer exists in the Action's output post-002 (it's `flaggedClause`/`assessment` now). Since
`projectFlaggedSectionsToAdvisoryComments` requires a truthy `explanation` to include an item
(`if (!targetText || !explanation) continue;`), **every flagged section from a live post-002 review is currently
silently skipped** — zero advisory comments reach the gutter or the export today, end-to-end, until that bridge is
updated to read `flaggedClause`/`assessment`. This file is in `src/solutions/SpaarkeAi/**` and adjacent to the
`ConversationPane.tsx` hot-file (owned by tasks 021→031→041→042 per project CLAUDE.md) — **out of this task's
declared surface** (`Spaarke.Compose.Components` only). No task in `TASK-INDEX.md` currently names this fix
explicitly. **Flagging for the project owner**: whichever task next touches
`useNdaReviewAdvisoryCommentsBridge.ts` (or a new task) must update `projectFlaggedSectionsToAdvisoryComments` to
read `flaggedClause`/`assessment` (and pass them through the `compose_advisory_comments` event's
`ComposeAdvisoryCommentItem` items, which also need the two new optional fields added in
`Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`) — otherwise this task's export-mirror fix, while structurally
complete and tested, has no live advisory comments to apply it to until that bridge is repaired. This does not
block task 052's own acceptance criteria (which are about the export MAPPING, verified via fixtures below) but is
material to the FR-15 end-to-end outcome and belongs in the project's Phase-2/3 or hub-r1 coordination backlog.

### Seam choice (confirmed the POML's default — export-mapping seam)

Composed the structured text **at the export mapping** (`composeSessionCommentThreadsToAnchoredComments` /
`composeCommentThreadsToDocxAnnotations`), NOT at `ComposeEditor.placeAdvisoryComments`-time. Reasons, confirmed by
inspecting the actual gutter render code (`ComposeCommentGutter.tsx:750-953`):
- The gutter renders `flaggedClause`/`assessment`/`standardRef` as **separate styled Fluent elements** (risk
  `Badge`, semibold label `Text`, a clickable `StandardRefChip` popover that async-fetches the full standard text
  on demand). Baking a single flattened string onto `thread.text` at placement time would destroy the gutter's
  ability to render these as distinct, interactive elements.
- **Durable-recall payload-driven proof**: since the export mapping reads nothing but the thread's own fields, a
  thread re-materialized by a future `placeAdvisoryComments` call (tasks 030-032, durable recall) composes
  IDENTICAL export text to a live one, with zero special-casing — verified by a dedicated fixture test (see below).
- No override needed; the step-0 audit confirms the default recommendation.

## Step 1 — shared helper

Hoisted into a NEW file, `src/client/shared/Spaarke.Compose.Components/src/widgets/advisoryNoteFormatting.ts`:
`AdvisoryNoteSegment`, `ADVISORY_NOTE_LABELS`, `ADVISORY_NOTE_DISPLAY_LABEL`, `parseAdvisoryNote` (moved verbatim
from `ComposeCommentGutter.tsx`), plus three NEW functions:
- `isAdvisoryCommentThread(thread)` — the plain-vs-advisory discriminant (any of
  riskLevel/sectionRef/standardRef/flaggedClause/assessment present), reusing the exact rule the gutter's own
  comment already documented ("Only advisory notes carry a sectionRef; a plain session comment has none").
- `getAdvisoryNoteSegments(thread)` — discrete `flaggedClause`/`assessment` fields win when present (no parsing);
  else degrades to `parseAdvisoryNote(thread.text)`.
- `composeAdvisoryCommentExportText(thread)` — the export string: plain threads pass `text` through unchanged;
  advisory threads join labelled segments with blank-line separators, then append a `Standard: {ref}` line (+ the
  full clause text after an em-dash when the thread carries `standardText`).

`ComposeCommentGutter.tsx` now imports `getAdvisoryNoteSegments`/`parseAdvisoryNote` from this module and
re-exports `parseAdvisoryNote`/`AdvisoryNoteSegment` (existing import sites, incl. its own test suite, unaffected).
Its render loop now computes `allSegments` via `getAdvisoryNoteSegments(thread)` instead of
`parseAdvisoryNote(fullText)`; the truncation-length `fullText` is derived from the resolved segments' bodies
(so a legacy marker-laden note's truncation threshold reflects its VISIBLE length, not the raw marker-laden
string — verified no regression against the existing truncation tests, see Step 4).

**Grep-proof (single relabel source)**: `ADVISORY_NOTE_DISPLAY_LABEL`/`ADVISORY_NOTE_LABELS`/`parseAdvisoryNote`
definitions exist in exactly ONE file repo-wide (`advisoryNoteFormatting.ts`); `ComposeCommentGutter.tsx` only
imports + re-exports.

## Step 2 — export mapping + scope lift

`ComposeCommentThread.types.ts`: added `flaggedClause?`, `assessment?`, `standardText?` to
`ComposeCommentThreadModel`; rewrote the field-group doc comment to document the **scope lift** — `riskLevel`/
`sectionRef` stay UI-only (badge + location-label derivation, never part of the note's visible labelled body);
`standardRef` (+ new `flaggedClause`/`assessment`/`standardText`) are now exported because the gutter always
renders them as part of the note's visible content, so silently dropping them made the saved comment materially
incomplete relative to what the reviewer saw (the root cause). Both `composeCommentThreadsToDocxAnnotations` (the
retired/legacy DocxAnnotationInput path — updated for consistency, still covered by its own tests) and
`composeSessionCommentThreadsToAnchoredComments` (the LIVE save path) now compose the ROOT comment's `commentText`
via `composeAdvisoryCommentExportText(thread)`. Replies are unchanged (raw `reply.text` — a reply is follow-up
discussion, never carries advisory metadata on `ComposeCommentReply`).

`standardText` is documented as payload-driven-only: nothing populates it today (the gutter's own
`StandardRefChip` resolves the full standard text via an async BFF call, which the export mapping — a pure,
synchronous function per ADR-049/ADR-007 — cannot make); a future prefetch/durable-recall wiring that sets it
will export it automatically with no mapping-function change.

## Step 3 — author config

Added `advisoryCommentAuthor?: string` to `ComposeEditorProps` (default `'AI Advisory Review'` — the exact
pre-existing literal, per ADR-012 lib-level-prop-with-current-default). Threaded through:
`useComposeCommentThreads(editor, advisoryCommentAuthor)` (was the hardcoded literal). Also extended
`AdvisoryCommentInput` (`flaggedClause?`/`assessment?`/`standardText?`, additive — `explanation` stays required,
unchanged) and `ComposeCommentThreadMetadata` (`useComposeCommentThreads.ts`) with the same three fields, and
`placeAdvisoryComments`'s `createThread(...)` call now forwards them. No current caller populates the three new
fields (see the IMPORTANT finding above) — purely additive plumbing, zero behavior change until upstream wiring
lands.

## Step 4 — tests

New files (all in `Spaarke.Compose.Components`, no sibling-owned surface touched):
- `advisoryNoteFormatting.test.ts` — unit coverage: discrete-field composition (no parsing), legacy marker-parse
  degrade, legacy no-marker degrade (no fabricated structure), plain-thread passthrough, standardRef/standardText
  inclusion + omission, and a durable-recall parity fixture (same discrete fields, different id/timestamp ⇒
  byte-identical export text).
- `ComposeCommentThread.exportMirror.test.ts` — mapping-level (`composeSessionCommentThreadsToAnchoredComments`)
  proof using the same hand-built ProseMirror-schema harness as the sibling
  `ComposeCommentThread.anchoredComments.test.ts` (kept that file scoped to its own cross-paragraph-clamp
  concern): structured export from discrete fields, legacy marker degrade, legacy no-marker degrade, plain-comment
  passthrough, a save→reopen round-trip assertion (same thread shape re-run through the mapping twice ⇒ identical
  commentText), and the recalled-thread parity fixture at the full mapping level (not just the formatting helper).
- `ComposeEditor.advisoryCommentAuthor.test.tsx` — end-to-end against the REAL `ComposeEditor` (mirrors
  `ComposeEditor.advisoryComments.test.tsx`'s mount convention, in a SEPARATE file so this task doesn't touch that
  DEF-01-affected suite): default author unchanged when the prop is omitted; a configured author name is
  attributed to newly-placed advisory threads.

## Step 5 — build + tests

- **Build** (`npm run build` = `tsc`): **0 errors.**
- **Touched/new suites** (7 files: `advisoryNoteFormatting.test.ts`, `ComposeCommentGutter.test.tsx`,
  `ComposeCommentThread.exportMirror.test.ts`, `ComposeCommentThread.anchoredComments.test.ts`,
  `ComposeCommentThread.test.tsx`, `ComposeEditor.advisoryCommentAuthor.test.tsx`,
  `ComposeEditor.advisoryComments.test.tsx`): **80 passed / 1 failed**, 7 suites (6 passed, 1 failed).
- **Full package regression** (`npx jest`, all 67 suites / 757 tests): **741 passed / 16 failed**, 61 suites
  passed / 6 failed. **Verified via `git stash` isolation** (stashed all 4 task-052 source edits, reran the full
  suite against the ORIGINAL pre-052 code, confirmed byte-identical failure on the one shared suite, then
  `git stash pop` to restore) that the failure count/identity is **unchanged from baseline**:
  - `ComposeEditor.advisoryComments.test.tsx` (1 test: `placed` 2 vs expected 1) — the documented **DEF-01** bug,
    task 012's scope, reproduced identically pre/post this task's changes.
  - `ComposeWorkspace.bornInEditorSave.test.tsx`, `ComposeWorkspace.search.test.tsx`,
    `ComposeWorkspace.saveOpLogPreservation.test.tsx`, `ComposeWorkspace.imports.test.tsx` (4 suites) +
    `stepOperationInterceptor.test.ts` (1 suite) — pre-existing, unrelated to any file this task touched (none of
    `ComposeWorkspace.tsx`/`stepOperationInterceptor.ts` were modified). This is the same "6 failures across
    5-6 suites" baseline task 010's notes documented.
  - **Zero suites newly failing.** Every one of task 052's own new/touched suites passes in both the isolated and
    full runs.
- **Lint**: `npm run lint` / a direct `eslint` invocation fails repo-wide with "couldn't find an eslint.config.js"
  — no ESLint config resolves for this package (or the repo root) under the installed ESLint 9.39.4; this is a
  pre-existing environment gap, unrelated to this task's diff (confirmed no `.eslintrc*`/`eslint.config*` exists
  in the package or up the tree). Not attempted to fix (out of scope) — `tsc` (strict-mode build) is the static
  check actually available and it is clean.

## Step 6 — quality gates (self-run, FULL rigor)

**code-review essentials**: Diff reviewed file-by-file (351 diff lines across the 4 modified files + 4 new
files). No security-sensitive code, no new dependencies, no new DI/endpoints, no ADR-boundary crossings (pure
client-side TS/TSX in an existing shared package). Naming/JSDoc consistent with surrounding file conventions.
No dead code introduced (the legacy `composeCommentThreadsToDocxAnnotations` function was ALSO updated, for
consistency — avoids a "one path fixed, one silently diverges" latent bug). No `console.log`/debug leftovers.

**adr-check essentials**:
- **ADR-049** (Compose shadow document, I-7 no-text-search-in-write-path): no text-search added — the export
  mapping composes from already-anchored, already-resolved thread fields; `parseAdvisoryNote`'s regex operates on
  the thread's OWN text for LABEL splitting (display formatting), never for document-location search. `ApplyComment`
  (server) untouched — confirmed via `git diff --stat -- src/server/` (see below).
- **ADR-012** (shared component library): author configurability implemented as a lib-level prop with the current
  literal as the default, per the constraint. No Fluent-v9/dark-mode surface added (no new visual component — pure
  data/type/logic changes plus one leaf module with no JSX).
- **ADR-007/ADR-013** (Graph/AI-facade isolation): `advisoryNoteFormatting.ts` takes no Graph/AI dependency; it's a
  pure, dependency-free string/array transform.
- **CLAUDE.md §11** (component justification): the one NEW file is a **de-duplication hoist**, not a new
  overlapping abstraction — Existing = the gutter's own local label-map + parser (duplicated-logic risk the very
  bug this task fixes); Extension = moved the logic into a shared leaf module both consumers import, rather than
  writing a second copy at the export site; Cost-of-doing-nothing = the export mapping would need its OWN
  parse/relabel copy, guaranteeing future drift between gutter and export (exactly the historical bug).
- No violations found requiring the §6.5 escalation protocol.

## Manual Word verification (DEFERRED)

Per the task's explicit instruction, the ui-test ("save reviewed doc → open in Word → verify all four symptoms")
is **deferred to the deploy wave (tasks 060/061 UAT)** — this session cannot open Word. The in-repo proof is the
mapping-level test suite above (`ComposeCommentThread.exportMirror.test.ts`), which asserts the EXACT
`commentText` string that will reach `ComposeShadowPatchEngine.ApplyComment` (server, unchanged) and become the
`<w:comment>` body. One nuance for the 060/061 UAT to be aware of: `ApplyComment` writes the composed string as a
single OOXML `<w:t>` run (`Space = SpaceProcessingModeValues.Preserve`) — this task's `\n\n` segment separators are
literal LF characters, not `<w:br/>` elements (adding those would require a server change, which is explicitly
out of scope / a negative acceptance criterion). Whether Word renders embedded LFs as visible line breaks in a
comment balloon vs. collapsing them is a rendering nuance to confirm at UAT; the labelled text content itself
(author, "Flagged clause", "Assessment says: …", "Standard: …") is present and correctly composed regardless.

## Acceptance criteria

| Criterion | Result |
|---|---|
| Saved-then-reopened-in-Word review shows configured author · "Flagged clause" · "Assessment says: …" · "Standard: …" | **PASS at mapping level** (`ComposeCommentThread.exportMirror.test.ts` asserts the exact composed string); **DEFERRED to 060/061 UAT** for the visual Word-open confirmation (cannot open Word in this session) |
| standardRef exports (citation; full clause text when available); scope-lift documented at the change site | **PASS** — `composeAdvisoryCommentExportText` appends `Standard: {ref}` (+ ` — {standardText}` when carried); scope-lift documented in `ComposeCommentThreadModel`'s field-group doc comment |
| Gutter + export consume ONE shared relabel source (grep-proof, no duplicated label maps) | **PASS** — `ADVISORY_NOTE_DISPLAY_LABEL`/`parseAdvisoryNote` exist in exactly one file (`advisoryNoteFormatting.ts`); gutter imports + re-exports only |
| Legacy threads (raw explanation only) export their text without crash or fabricated structure | **PASS** — `composeAdvisoryCommentExportText`/`getAdvisoryNoteSegments` degrade to `parseAdvisoryNote`, which returns a single unlabelled segment (no fabricated label) when no marker is recognized; covered by 3 dedicated tests at both the helper and mapping level |
| Negative: server `ApplyComment` untouched (git diff proof); durable-recalled threads export identically to live ones | **PASS** — `git diff --stat -- src/server/` shows only an unrelated concurrent task's DI file (task 020, running in parallel in this shared worktree — not part of this task's diff); this task's own changes touch zero files under `src/server/`. Durable-recall parity proven by dedicated fixture tests at both the helper (`advisoryNoteFormatting.test.ts`) and mapping (`ComposeCommentThread.exportMirror.test.ts`) levels |

## Deviations / escalations

No ADR conflict; no §6.5 escalation needed. One **material finding surfaced, not fixed** (out of this task's file
scope): the `useNdaReviewAdvisoryCommentsBridge.ts` client bridge still reads the pre-002 `explanation` field and
currently drops every flagged section from a live post-002 review before it ever reaches `placeAdvisoryComments`
— see the "IMPORTANT finding" callout in Step 0. This does not block task 052's acceptance criteria (verified at
the mapping/fixture level, per the task's own design) but should be tracked as a follow-up (likely alongside
whichever task next touches `ConversationPane.tsx`/the bridge, or as a new small task) before FR-15 can be
observed end-to-end in a live review.

## Files touched

- `src/client/shared/Spaarke.Compose.Components/src/widgets/advisoryNoteFormatting.ts` (NEW — shared helper)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/advisoryNoteFormatting.test.ts` (NEW — unit tests)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentThread.exportMirror.test.ts` (NEW — mapping-level tests)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.advisoryCommentAuthor.test.tsx` (NEW — author-config tests)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentThread.types.ts` (model fields + scope-lift doc + export composition, both functions)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (`advisoryCommentAuthor` prop + `AdvisoryCommentInput` new fields + `placeAdvisoryComments` passthrough)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeCommentGutter.tsx` (consumes shared helper; local label-map/parser removed)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/useComposeCommentThreads.ts` (`ComposeCommentThreadMetadata` new fields + `createThread` passthrough)

Not touched (out of file-scope, documented above): `src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts`, `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`, `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx`, `src/server/**` (all unchanged by this task).
