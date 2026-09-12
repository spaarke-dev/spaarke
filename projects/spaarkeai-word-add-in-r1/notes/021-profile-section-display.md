# Task 021 — Profile section display (FR-07): deviations + decisions

## §11 route decision — extended an existing route, added NO new BFF route

**Escalation trigger 1** ("if no existing route returns the profile fields and a NEW BFF route is
required, STOP and escalate") did NOT fire, because no new route was required.

Grep evidence (`grep -rn "sprk_filesummary\b|sprk_filetldr|sprk_filekeywords|sprk_filesummarystatus"
--include=*.cs src/server`) confirmed no pane-facing reader existed for the four profile fields.
Two candidate extension points were evaluated:

1. **`GET /api/v1/documents/{id}`** (`DataverseDocumentsEndpoints.cs`) — already exists, already
   keyed by document id, already carries `.AddDocumentAuthorizationFilter("read")` +
   `.RequireAuthorization()` (the exact authorization this task needs), and its response DTO
   (`DocumentEntity`) **already declared** `Summary` / `Tldr` / `Keywords` / `DocumentType`
   properties that no caller of `GetDocumentAsync` had ever selected or mapped — dead fields,
   not a gap needing new surface.
2. **`/api/documents/{documentId}/*`** (`FileAccessEndpoints.cs`) — the sibling family the Office
   add-in already calls for `resolve-identity`. Adding `GET /api/documents/{documentId}/profile`
   here would have been a clean fit stylistically, but it is a **new route**, which is exactly what
   the escalation trigger exists to gate.

Chose (1). The entire server-side change is: extend `GetDocumentAsync`'s `ColumnSet` by 5 column
names, add `DocumentEntity.SummaryStatus` (int?), and populate 5 fields in `MapToDocumentEntity`.
Zero new routes, zero new DI registrations, zero new packages.

**Consumer check**: grep of `src/client` for `api/v1/documents` found exactly one pre-existing
consumer (`SummarizeAnalysisStep.tsx` in the PCF-oriented shared UI library) — the Office add-in
had never called this route before. Adding 5 columns/fields to a DTO already this broad, for a
route with one unrelated consumer, is a low-risk additive change; the alternative (new route) would
have needed the escalation the task explicitly wanted surfaced.

## Free-text "Description" field — RETAINED, not removed, relabeled to "Notes"

Per the task's own framing ("Decide and state in the PR whether the free-text description input is
retained alongside the Profile section or removed; do not silently conflate the two"):

**Decision: retained.** The existing Textarea bound to `documentDescription` / `sprk_documentdescription`
is a distinct, legitimate save-time capability (nothing in spec requires removing it, and removing it
would silently regress an existing feature). It is NOT the same thing as the AI-generated Profile.

To satisfy the acceptance criterion "the string 'Description' no longer labels this section", the
Textarea's own field `Label` was renamed `"Description"` → `"Notes"` (its placeholder and
`aria-label` were updated to match). The `id="document-description"` and the state variable
(`documentDescription`) were left unchanged — only the user-facing label moved, not the wire
contract (still writes `sprk_documentdescription` via `buildSaveContext`).

A new, separate `<div className={styles.section}>` with its own `"Profile"` heading (via
`DocumentProfileSection`) was added immediately after the "Document Details" card. It never shared
a label with the free-text field, so there is no possible reading where "Description" labels the
AI-profile section.

## BFF Placement Justification (root CLAUDE.md §10)

- **Existing**: `GET /api/v1/documents/{id}` — see §11 decision above.
- **Extension, not new**: the change adds columns to an existing `RetrieveAsync` call and fields to
  an existing mapper method (`MapToDocumentEntity`), both already used by 8+ other call sites in
  `DataverseServiceClientImpl.cs`. No new endpoint, no new DI, no new package.
- **Publish size**: +0.02 MB against a fresh `origin/master` build (`e0a6f87c4`, task 030's
  same-day measurement reused since master was unchanged) — see `notes/021-publish-size.md`.
- **CVE**: `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api.csproj` — no
  vulnerable packages (no package changes were made).
- **Config boundary (§G)**: not applicable — no playbook/node/action config field was touched.

## Test file placement deviation (authorized by the orchestrator's brief)

Per the authorized deviation, the new contract test was placed at
`tests/integration/contract/Api/Documents/DocumentProfileContractTests.cs` (a new file, matching
the existing `Api/Documents/DocumentIdentityContractTests.cs` sibling from task 016) rather than
appended to `OfficeEndpointsContractTests.cs` — the route this task extends lives under
`/api/v1/documents`, not `/api/office`, so `Api/Documents/` is the more accurate home than the
`Api/Office/OfficeDocumentProfileContractTests.cs` name the brief offered as an alternative.

## Environment gap found and fixed (not a code change)

This worktree's `npm install` never built the `@spaarke/auth` workspace package (`src/client/shared/
Spaarke.Auth` had no `dist/`), which made `npm run typecheck` and `npm run build` fail on
`shared/services/AuthService.ts`'s `import ... from '@spaarke/auth'` — confirmed PRE-EXISTING via
`git stash` (the error reproduces on a clean `8b5d58a2b` checkout with no task-021 changes). Ran
`npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` (`tsc`) inside
`src/client/shared/Spaarke.Auth` to produce its `dist/` output — a build step, no source edited.
After that, both `npm run typecheck` (0 production errors, 284 test-file errors — matches the
project's documented baseline exactly) and `npm run build` (clean) ran representatively.

## `useDocumentProfile` design note

`useOfficeTheme` (named in the ADR-021 constraint) is not called directly inside
`DocumentProfileSection` — the pane's root `FluentProvider` (fed by `App.tsx`'s `useTheme`, which
runs the same Office.js theme-bridge detection) already resolves every `tokens.*` reference for the
whole tree, and `SaveFlow.tsx` (the sibling this section lives inside) follows the same convention.
Calling the hook again in a leaf component would start a second, redundant theme-detection
subscription without changing what renders.
