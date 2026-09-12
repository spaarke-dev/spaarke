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

## Post-review bugfix: `sprk_documenttype` is a Choice column, not text

A coordinator review (2026-09-12, after the initial task-021 commit `45653864d`) caught a real
runtime defect: `sprk_documenttype` is a Dataverse **Choice (Picklist)** column, not free text —
verified live via the metadata API, and corroborated by the pre-existing writer at
`DataverseServiceClientImpl.cs:850` (`document["sprk_documenttype"] = new
OptionSetValue(request.DocumentType.Value)`). My original mapper line,
`entity.GetAttributeValue<string>("sprk_documenttype")`, casts the stored `OptionSetValue` directly
to `string`; the SDK's `Entity.GetAttributeValue<T>` performs an unconditional `(T)obj` cast, so this
threw `InvalidCastException` for any entity where the attribute was present.

**Blast radius, confirmed by grep**: because task 021 added `sprk_documenttype` to
`GetDocumentAsync`'s `ColumnSet`, the crash was reachable from two places — the new
`GET /api/v1/documents/{id}` read AND `VisualizationService.SearchForVisualizationAsync`, whose Step
1 (`sourceDataverseDoc = await _documentService.GetDocumentAsync(documentId.ToString(), ...)`, its own
comment: "always required") calls the SAME method. This means the bug, unfixed, would have broken the
**Find Similar visualization feature** for any document with a classified document type — not only
the new Profile section. No OTHER `IDocumentDataverseService` method (`GetDocumentsByMatterAsync`,
`GetDocumentsByParentAsync`, etc.) selects `sprk_documenttype` in its own `ColumnSet`, so those paths
were never at risk. Grep of `src/server` for `.DocumentType` confirmed **zero** production consumers
of `DocumentEntity.DocumentType` existed before task 021 (every other `.DocumentType` hit belongs to
an unrelated type — `BulkRagIndexingPayload`, `VisualizationDocument`, etc.) — so no consumer could
have been relying on the old always-null value or on a code string; the read-model's `string?` shape
for `DocumentType` was free to keep.

**Fix**: `MapToDocumentEntity` now reads `sprk_documenttype` by preferring
`entity.FormattedValues["sprk_documenttype"]` (the SDK populates this with the Choice's display label
on every `Retrieve`) and falling back to the raw numeric value, stringified, only when no formatted
value is present (defensive — some hand-built entities in other tests/paths may lack
`FormattedValues`). This needs zero client-side changes: `DocumentProfileSection.tsx` already renders
`documentType` as a plain label string. Considered also carrying the raw `int` option value alongside
the label (per the reviewer's "consider") — decided against it: zero consumers need the int today,
and `DocumentEntity` is already a wide DTO; adding an unused field would be scope creep the existing
component-justification discipline (CLAUDE.md §11) argues against. `sprk_filesummarystatus` (also a
Choice column) already read correctly via `GetAttributeValue<OptionSetValue>(...)?.Value` — not part
of the same bug class, confirmed by tests below. The three Memo columns
(`sprk_filesummary`/`sprk_filetldr`/`sprk_filekeywords`) were also independently verified live as
Memo (string-backed, no OptionSetValue involved) — no fix needed there.

**Seam for the regression test (ADR-038 §7 B8)**: `MapToDocumentEntity` was `private` and uses no
instance state, so it was changed to `public static` — the SAME precedent this file already uses for
`StageAnalysisRegardingFields` ("Exposed public static for direct testability... avoids the
tests/CLAUDE.md B8 ban on internal/reflection tests"). No `InternalsVisibleTo`, no reflection.

**Fail-then-pass, demonstrated directly** (not merely asserted): with the public-static conversion
applied but the buggy `DocumentType` line unchanged, `dotnet test --filter
FullyQualifiedName~DocumentEntityMappingTests` gave **2 failed / 8 passed / 10 total**, both failures
throwing `System.InvalidCastException : Unable to cast object of type 'Microsoft.Xrm.Sdk.OptionSetValue'
to type 'System.String'.` at `DataverseServiceClientImpl.cs:1542` — the exact line and exact exception
the reviewer predicted. After applying the fix, the same filter gave **10/10 passed**. Both runs are
captured verbatim in the task's final report.

New test file: `tests/unit/domain/Dataverse/DocumentEntityMappingTests.cs` (KEEP path
`tests/unit/domain/**`, ADR-038 §2 #6 — pure domain mapping logic) — 10 cases: DocumentType with a
formatted label, DocumentType with no formatted label (raw-value fallback), DocumentType column not
selected (null, no throw), SummaryStatus across 3 option values (asserts the RAW int is returned even
when a formatted label IS present — pins the deliberate DIFFERENCE from DocumentType's label-preferring
behavior so a future refactor doesn't accidentally unify the two and break the client's status switch),
SummaryStatus column not selected (null), and the 3 Memo columns read cleanly as plain strings.

## `useDocumentProfile` design note

`useOfficeTheme` (named in the ADR-021 constraint) is not called directly inside
`DocumentProfileSection` — the pane's root `FluentProvider` (fed by `App.tsx`'s `useTheme`, which
runs the same Office.js theme-bridge detection) already resolves every `tokens.*` reference for the
whole tree, and `SaveFlow.tsx` (the sibling this section lives inside) follows the same convention.
Calling the hook again in a leaf component would start a second, redundant theme-detection
subscription without changing what renders.
