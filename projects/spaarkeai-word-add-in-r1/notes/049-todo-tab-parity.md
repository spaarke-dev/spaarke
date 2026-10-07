# Task 049 — Create To Do tab parity (Word gets the shared capability)

> FR-14 / FR-19 / NFR-10. Fixes the stale gate task 040's audit found and could not fix (its own hard
> rule against weakening a test blocked changing `TAB_CONFIGS` and `TaskPaneNavigation.test.tsx`
> together). Written 2026-09-15.

## Step 1 — Confirmed in code: the Word path produces a usable To Do (escalation trigger does NOT fire)

The POML's escalation trigger says STOP if "the record regarding is never populated for a Word document
in practice." Read the actual code paths before touching the gate:

1. **`documentIdentityService.ts` `applyDocumentIdentityOutcome`** (lines 271-294): when the FR-01
   identity resolver (task 013) returns `kind: 'resolved'` for the open Word document AND
   `outcome.relatedRecord` is non-null, it sets `documentId`, `documentName`, `fileName`,
   `relatedRecord`, **and** `regardingEntity` (via `toFriendlyRegardingType`) + `regardingRecordId` +
   `regardingName` — all in the same merge, onto `App.savedContext`. This runs once per pane session
   after authentication (`App.tsx` lines 330-341), for any host with `canGetDocumentUrl: true` (Word
   only — Outlook's capability is always `false`, so this path is a no-op there by construction, no
   host check needed).

2. **`App.tsx` `handleSaved`** (lines 177-197), wired as `SaveView`'s `onSaved` prop (line 698) — fires
   on **any** successful save, either host, when the user picked a "Related to" record. Sets
   `regardingEntity`, `regardingRecordId`, `regardingName`, `relatedRecord`. Entirely host-agnostic:
   `SaveView` is the same component instance for both hosts (no `hostType` branch wraps the `onSaved`
   wiring).

3. **`App.tsx` `SaveView`'s `onComplete`** (lines 687-696) — fires on every completed save, either host,
   and sets `savedContext.documentId = cleanGuid(docId)`. The comment there (pre-existing, task 036) is
   explicit: *"Word usually already has one from task 013's URL-based resolution... for Word it is a
   same-value overwrite in the common case."*

**Conclusion**: filing a Word document to a record — whether the document was already tracked and
resolved (path 1) or the user just picked "Related to" and pressed Save (paths 2+3 firing together on
the same save) — leaves `savedContext` with `documentId`, `regardingEntity`, and `regardingRecordId` all
populated. That is exactly what `handleCreateTodo`'s guard (`App.tsx` line 350) requires, and exactly
what flows into the `POST /api/office/todo` body (lines 378-383) as the record regarding + the document
carrier. **The escalation trigger does not fire.**

Server-side, `OfficeTodoRegardingContractTests.cs`
(`Post_OfficeCreateTodo_WordDocumentAndMatterRegarding_WritesBothLookups_AndResolverFieldsDescribeTheMatter`,
task 035) proves the other half: given a request shaped exactly like the one `handleCreateTodo` builds
(`DocumentId` + `RegardingEntityType`/`RegardingRecordId`), the server writes **both**
`sprk_regardingmatter` and `sprk_regardingdocument`, with the resolver fields describing the Matter —
not the document. End to end, the Word path is real, not a dead end.

## Step 2 — Tab table + test

`TaskPaneNavigation.tsx`: `TAB_CONFIGS.createTodo.availableFor` flipped from `['outlook']` to
`['outlook', 'word']`. Rewrote the stale comment at the entry (was task 040's audit-finding comment
explaining why it was NOT changed; now explains why it IS, citing 035's document-carrier block and
FR-14's acceptance). Also corrected the two summary comments higher in the file (the file-header
"Renderer decision" doc comment and the `TAB_CONFIGS` block comment) that both still said "Create To Do
is Outlook-only."

`TaskPaneNavigation.test.tsx`: renamed `'renders only the Save tab for Word'` →
`'renders Save, Find and Create To Do tabs for Word'`; assertions now check all three tabs present
(`getByRole` instead of `queryByRole().not.toBeInTheDocument()` for Create To Do) plus Find (not
previously asserted in this test at all) and confirms Share/Recent stay absent on Word too. No other
test in the file was touched — the Outlook tests (tab presence, selection, click, disabled, compact,
default-tab) are byte-identical to before.

## Step 3 — Host-neutral copy

Two strings, per the POML's exact scope:

| File | Before | After |
|---|---|---|
| `App.tsx` `handleCreateTodo` guard | `'File this email to Spaarke first (Save tab).'` | `'Save this to Spaarke first (Save tab).'` |
| `CreateTodoView.tsx` empty state | `MessageBarTitle`: "File this email first" / body: "A To Do is related to the record you file the email to. Save it on the **Save** tab, then come back here." | `MessageBarTitle`: "Save this to Spaarke first" / body: "A To Do is related to the record this is saved to. Save it on the **Save** tab, then come back here." |

No host-name conditional was added to produce either string — both are now unconditional literals, same
for both hosts, per NFR-10. Grep confirms (see final report) neither rendered string contains "email" or
a host name; remaining "email"/"Word"/"Outlook" occurrences in both files are in code comments/JSDoc,
not rendered copy.

Checked for test blast radius before editing: no test in the repo asserts either old string
literal (`File this email` has zero hits under `__tests__/`), and no `CreateTodoView.test.tsx` /
`App.test.tsx` exists at all (confirmed — this is a pre-existing, deliberate gap noted by task 040's
audit, not something this task's narrow scope reopens).

## Acceptance criterion 3 — "proven by test (new, or an existing 035 test cited)"

**Citing existing 035 coverage rather than adding a new client-side test.** Reasoning:

- The client-side code that builds the carrier (`App.tsx handleCreateTodo`, lines ~363-385) is
  unconditional on host — `documentId` is included in the POST body whenever `savedContext.documentId`
  is truthy, full stop. There is no Word/Outlook branch to test differently; the same code path already
  serves both hosts today (has since task 035 shipped), and this task did not touch that function's body
  (only its guard's error string).
- `App.tsx` has **zero** test coverage today (no `App.test.tsx` exists anywhere in the package — verified
  by glob). Building a render harness for it (mocking `IHostAdapter`, `authService`, `fetch`, etc.) to
  cover one already-host-agnostic line would be a disproportionate new-test-infrastructure lift for a
  task whose actual change surface is one data-table flip and two literal strings — and it was explicitly
  out of scope for task 040's own audit for the same reason.
- The server-side contract test named above already proves, end to end from the HTTP boundary, that a
  request shaped exactly like the one this client code builds for a Word To Do (`DocumentId` +
  `RegardingEntityType`/`RegardingRecordId`) results in both the document carrier and the record
  regarding being written correctly.

Net: acceptance criterion 3 is satisfied by (a) this task's own direct code-read of the unconditional
client construction (Step 1 above) plus (b) citing
`OfficeTodoRegardingContractTests.Post_OfficeCreateTodo_WordDocumentAndMatterRegarding_WritesBothLookups_AndResolverFieldsDescribeTheMatter`
(`tests/integration/contract/Api/Office/OfficeTodoRegardingContractTests.cs`) as the existing 035
coverage, per the constraint's explicit "or state why the existing 035 coverage is sufficient" clause.
This file is under `src/server/**` / `tests/**` and was **not modified** by this task (client-only scope;
task 048 owns server changes in parallel).

## Deviations from the literal POML steps (directional mode)

- Also corrected two summary/doc comments in `TaskPaneNavigation.tsx` beyond the single `TAB_CONFIGS`
  entry comment the POML's background section quoted, because they stated the same now-false claim
  ("Create To Do is Outlook-only") and would otherwise mislead the next reader. Not a behavior change —
  comments only.
- Did not touch `src/client/office-addins/CLAUDE.md` line 28 ("Word: Save + Find. Outlook: Save +
  Create To Do + Find.") even though it is now stale — it is outside the POML's `<relevant-files>` list
  and outside this note's file, and editing module-pointer docs beyond the named scope risked
  second-guessing the POML's boundary. Flagged here for the main session / a follow-up doc-drift pass.

## Step 4 — Gate results

**Environment note**: a fresh worktree needed two one-time build steps before `tsc` could resolve
`@spaarke/auth` (the `file:` workspace dependency at `src/client/shared/Spaarke.Auth` had no `dist/`
output in this worktree — `npm install` + `npm run build` there, neither of which touched any source).
Without that, `tsc --noEmit` showed 112 errors (111 + one `AuthService.ts` module-resolution error,
unrelated to this task's files). After building `@spaarke/auth`, the count matched the documented
baseline exactly.

| Gate | Result |
|---|---|
| `npx tsc --noEmit` | **111 total, 0 production** — exact match to the documented baseline. Zero new test-file errors either (111 = baseline, not 112). |
| `npm run build` (dummy `ADDIN_CLIENT_ID`/`TENANT_ID`/`BFF_API_CLIENT_ID`/`BFF_API_BASE_URL`) | Exit 0, no webpack errors. |
| Full `npx jest --ci` (run 1) | `Test Suites: 10 failed, 33 passed, 43 total` / `Tests: 84 failed, 554 passed, 638 total` — exact match to the documented ten-suite/84-test baseline. |
| Full `npx jest --ci` (run 2, re-run per POML instruction) | Byte-identical: `10 failed, 33 passed, 43 total` / `84 failed, 554 passed, 638 total`. Stable across both runs — no blip. |
| `TaskPaneNavigation.test.tsx` before/after | **Before** (baseline, per `ci-gated-suites.txt`'s own annotation): 6/7 passing, 1 failing ("disables tabs when disabled prop is true", pre-existing Fluent `TabList`/`aria-disabled` issue, unrelated to host gating). **After** (both full-suite runs): still 6/7 passing, same single failure, same error text/location. The renamed test ("renders Save, Find and Create To Do tabs for Word") passes. |
| 32 committed `ci-gated-suites.txt` paths via `--runTestsByPath` | `Test Suites: 32 passed, 32 total` / `Tests: 412 passed, 412 total`. **Count discrepancy from the POML's stated "33 suites": this worktree's committed `ci-gated-suites.txt` (rebased onto `work/spaarkeai-word-add-in-r1`) has exactly 32 paths, not 33 — it does not yet contain task 040's `capabilities.test.ts` entry/section.** That section exists only as an uncommitted local edit in the shared/main checkout (`c:\code_files\spaarke-wt-spaarkeai-word-add-in-r1\`), matching the `M src/client/office-addins/ci-gated-suites.txt` already flagged in this session's initial git-status snapshot — not yet merged into the branch this worktree rebased onto. Not a defect in this task; flagged for the main session to reconcile when it commits that pending edit. |
| `capabilities.test.ts` (bonus check — not yet in this branch's gate file, but directly adjacent to this task's domain) | `Test Suites: 1 passed, 1 total` / `Tests: 5 passed, 5 total`. Unaffected by this task's change (confirms `TaskPaneNavigation.tsx`'s data-table edit didn't touch adapter capability logic). |
| ESLint on the 4 touched files | 0 errors. 5 pre-existing `@typescript-eslint/no-empty-function` warnings in `TaskPaneNavigation.test.tsx` (the `onTabChange={() => {}}` no-op stub pattern, present since before this task, on lines this task did not touch). |
| Step 9.5 code-review (self-run) | 0 critical, 0 new warnings, 0 AI code smells. Full report in the final agent message. |
| Step 9.5 adr-check (self-run) | 0 violations. ADR-021 (Fluent v9), ADR-012 (Path A import boundary), ADR-038 (test strengthened, not weakened — office-addins `__tests__/**` KEEP-equivalent Path A exception honored), NFR-10 (host-neutral views, grep-verified) all compliant. Full report in the final agent message. |

### New suites for the main session to gate

None from this task. `capabilities.test.ts` was already identified by task 040 (§8 of `parity-checklist.md`)
and is pending the main session's own uncommitted edit to `ci-gated-suites.txt` — this task did not add
a new test file, so there is nothing additional to list.
