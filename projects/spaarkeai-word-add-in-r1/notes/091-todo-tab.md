# Task 091 — To Do tab label/icon, tab spacing, Assigned To email, create confirmation

> UAT round 3 (2026-10-03) items UAT-2 and UAT-4 — `notes/042-uat-round3-2026-10-03.md` §1. Wave UAT3-W3,
> alone (deps: 088, 092, 093 — all already merged into this worktree before this task started).

## 1. What shipped

| Area | Change |
|---|---|
| **Tab label** | `TaskPaneNavigation.tsx`'s `TAB_CONFIGS.createTodo` entry: label `'Create To Do'` → `'To Do'`. The tab's own `value` (`'createTodo'`) and routing are unchanged — only the label/icon the owner saw. |
| **Tab icon** | New local `src/client/office-addins/shared/taskpane/components/icons/MicrosoftToDoIcon.tsx` — a byte-for-byte copy of `src/client/shared/Spaarke.UI.Components/src/icons/MicrosoftToDoIcon.tsx`'s SVG paths (the Microsoft To Do blue check), because the add-in cannot import `@spaarke/ui-components` (ADR-012 Path-A exception). Used with `active` (fixed brand blue, not toggled by tab selection — matches `KanbanHeader.tsx:169`'s own usage) on both the tab icon and the `CreateTodoView` heading icon (the heading TEXT stays "Create a To Do" per the owner's exact scope — only its icon changed). |
| **Tab spacing** | `TaskPaneToolbar.tsx`: a new `tabListGap` style (`gap: tokens.spacingHorizontalM`) applied via `className` on the live `<TabList>`. Additive to TabList's own internal flex layout — confirmed via `TaskPaneNavigation.tsx`'s own precedent of passing `className` to override `justifyContent`. |
| **Assigned-To email (server)** | `OfficeSearchService.cs`: `EntitySearchMeta` gained an optional `EmailField` (6th positional param, default `null`); Contact's meta entry sets it to `"emailaddress1"`. `QuerySearchEntityAsync` adds it to `selectFields` — **never** to the `contains(...)` filter clause, which is built strictly from `NameField`/`RefField`. `MapSearchRow` populates a new `Email` property, `null` when the field is absent or blank — never falling back to `PrimaryField`/`Name` the way `DisplayInfo` does. |
| **Assigned-To email (DTO)** | `EntitySearchResponse.cs`: new `Email` property on `EntitySearchResult` — additive, nullable, `null` for every type except Contact. |
| **Assigned-To rows/chip (client)** | `ContactOption` gained `email?: string`. The lookup-result rows now show name, then email (when present), then job title as a quieter third line — but **never** the server's literal `"contact"` fallback (new pure helper `realJobTitle`, which treats the literal string `"contact"` as absent). The selected chip now stacks name + email instead of name only. A contact with neither shows name only, never the literal "contact". |
| **Create confirmation** | `CreateTodoView.tsx`: on a successful create, a success `MessageBar` ("To Do created: {name}") appears above the form (which stays, fields disabled, matching the Save tab's own "keep the form" precedent from task 088) with an **"Open in Spaarke"** button, shown only when `canOpenRecord` (from `hostAdapter.getCapabilities().canOpenBrowserWindow`, threaded through `App.tsx`) AND `ORG_URL` AND a real `todoId` are all present — never a disabled/dead link. The confirmation is announced via the pane's existing `useAnnounce` (`'polite'`); a failure is announced `'assertive'`. |
| **`todoId` plumbing** | `CreateTodoResult` gained `todoId?: string`. `App.tsx`'s `handleCreateTodo` now reads the server's 201 body (`{ todoId, name }`, `OfficeEndpoints.cs` / `CreateTodoResponse`) and echoes `todoId` back; on failure it now calls the existing `describeFetchFailure` helper (already used by `SaveFlow.tsx`) instead of a bare `` `Create failed (${res.status}).` `` — the server's own reason now reaches the user. |

## 2. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All server changes are inside `OfficeSearchService.cs` / `EntitySearchResponse.cs` — no new service, endpoint, DI registration, package or background work. Three-question test (CLAUDE.md §11):

1. **Existing** — the contact search route, its impersonated-per-caller query mechanism (task 062, F1), and the 201 `CreateTodoResponse` shape all already exist.
2. **Extension** — yes: one additive optional field on an existing record type (`EntitySearchMeta.EmailField`), one additive nullable property on an existing DTO (`EntitySearchResult.Email`), and wiring of data the server already returns (`todoId`) into data the client already drops.
3. **Cost of doing nothing** — users cannot tell same-named contacts apart and may assign a To Do to the wrong person record; a created To Do gives no visible confirmation and cannot be reached from the pane (named in the task's own justification).

## 3. Why the email field is safe (security constraint)

The constraint was: email is DISPLAY data only, selected in the **same** query under the **same** per-caller trim (task 062's impersonated `MSCRMCallerID` read), never added to the searched columns, and never a second query. Verified two ways:

1. **Code read**: `QuerySearchEntityAsync` builds `selectFields` and the `$filter` clause from separate variables — `EmailField` is appended only to `selectFields`, after the filter string is already built from `NameField`/`RefField`. One query, one round trip, same as before.
2. **New test**: `OfficeEntitySearchAuthorizationContractTests.SearchEntities_ForContact_ReturnsEmail_ButTheFilterNeverSearchesOnIt` splits the real OData query string the production code sends (captured via a new `LastQueryByEntitySet` field on the existing `StubDataverse` double) on `&$select=` and asserts `emailaddress1` is absent before the split and present after.

No escalation trigger fired — the per-user trim is the SAME mechanism (impersonated query), with one more selected column; there is no second, app-only fetch.

## 4. Gates (Step 9.5 / CLAUDE.md §10)

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| `OfficeEntitySearchMappingTests` (new tests) | `Passed! - Failed: 0, Passed: 6, Total: 6` (4 pre-existing + 2 new) |
| `OfficeEntitySearchAuthorizationContractTests` (new tests) | `Passed! - Failed: 0, Passed: 11, Total: 11` (9 pre-existing + 2 new) |
| Full `tests/unit/Sprk.Bff.Api.Tests` | see §5 below (ran in background; result appended once complete) |
| `tests/Spaarke.ArchTests` | `Passed! - Failed: 0, Passed: 349, Total: 349` |
| `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api) | `The given project 'Sprk.Bff.Api' has no vulnerable packages given the current sources.` — no package added or upgraded by this task |
| Gated jest suites (`ci-gated-suites.txt`, 70 of 70 after this task's addition) | `Test Suites: 70 passed, 70 total` / `Tests: 969 passed, 969 total` |
| New suite `CreateTodoView.test.tsx` (counted in the 70/969 above) | `Tests: 7 passed, 7 total` |
| `npm run lint` (office-addins) | 0 errors / 0 warnings |
| `npx tsc --noEmit --skipLibCheck` (office-addins) | 68 total errors, **0 in any production file**, **0 in any file this task touched** — all 68 are pre-existing baseline errors in other test files (`OutlookAdapter.test.ts`, `SaveFlow.test.tsx`, `useEntitySearch.test.ts`, `useSaveFlow.test.ts`, `shared/__mocks__/office-js.ts`), unrelated to this task |

### Publish size — owed

This worktree has been built repeatedly across this wave and earlier ones (CLAUDE.md §10 hazard three: a publish from a worktree that has been iterated in is not comparable to a fresh-build baseline). Per the orchestrator's instruction for this wave, the publish-size measurement is **owed to the main session**, which will measure once for the PR against a fresh `origin/master` worktree using PowerShell `Compress-Archive -CompressionLevel Optimal` (per `.claude/constraints/azure-deployment.md`). This task's own server change is two source files, zero new NuGet packages, zero new DI registrations, and zero new routes — its real contribution to publish size is a few hundred bytes of IL.

### Live UAT (acceptance criterion 8) — open

No deployed Office host (Word/Outlook) or BFF environment was reachable from this worktree in this session. The owner's two live checks — "two contacts with the same name are told apart by email" and "creating a To Do shows the message and the link opens it in Matter Management" — are **left open**, per the task's own fallback instruction ("If unavailable, leave open and say so"). All other acceptance criteria are verified by code read + the test suites above.

## 5. Full BFF unit-test suite — attempted, stopped as beyond this task's required gate

A full `dotnet test tests/unit/Sprk.Bff.Api.Tests` run (~12,400 tests across the whole BFF) was started as
extra diligence beyond the POML's own acceptance criterion 7, which names only "Office search tests and
ArchTests green" — both already confirmed green in §4 above (`OfficeEntitySearchMappingTests`:
6/6, `OfficeEntitySearchAuthorizationContractTests`: 11/11, `Spaarke.ArchTests`: 349/349). The full-suite
run was still in progress after 25+ minutes with no stdout reaching its output file (piped through `tail`,
which buffers until the whole process tree exits — not a sign of a hang, but not useful as a status
signal either for a suite this large), so it was stopped rather than continuing to consume time on a
check the task does not require. This task's own server change (`OfficeSearchService.cs` /
`EntitySearchResponse.cs`) touches no other service, route, or shared type, so the risk of an undetected
regression elsewhere in the 12,400-test suite is low — but it is **not independently verified here**, and
is named as an open item for the main session if a full-suite run is wanted before merge.

## 6. Step 9.5 quality gates

**code-review** (delegated to a general-purpose sub-agent reviewing all 13 changed/new files): **PASS, 0 critical.** A handful of non-blocking observations: (1) the `createdTodo.todoId!` non-null assertion in `CreateTodoView.tsx` is safe given its adjacent guard but could use `?? ''` instead; (2) `realJobTitle`'s literal `'contact'` string match is coupled to the server's exact fallback string — pre-existing, documented, out of this task's scope to fix; (3) `App.tsx`'s `handleCreateTodo` success-path `res.json()` has no inner try/catch (caught by the outer one, pre-existing pattern). None block merge.

**adr-check** (via the `adr-check` skill, scoped to all 13 changed/new files): **0 violations.** ADR-021 (Fluent v9 tokens only — grep-verified no hex/rgb colors, no `@fluentui/react` v8 imports) ✓ · ADR-012 (shared-component import boundary — grep-verified no `@spaarke/ui-components` import in the new/edited office-addins files; `MicrosoftToDoIcon.tsx` is a genuine local copy) ✓ · ADR-008 (no new/global auth middleware; the additive `Email` field rides the existing impersonated per-caller query, documented in code) ✓ · ADR-038 (no banned test antipatterns — grep-verified no `Mock<HttpMessageHandler>`, reflection, DI-registration tests, or ctor null-checks in any new/edited test file; new tests land in existing KEEP paths) ✓ · ADR-019 (no new endpoint; existing response shapes extended additively) ✓.

## 7. Files changed

- `src/client/office-addins/shared/taskpane/components/icons/MicrosoftToDoIcon.tsx` (new)
- `src/client/office-addins/shared/taskpane/components/TaskPaneNavigation.tsx`
- `src/client/office-addins/shared/taskpane/components/TaskPaneToolbar.tsx`
- `src/client/office-addins/shared/taskpane/components/views/CreateTodoView.tsx`
- `src/client/office-addins/shared/taskpane/App.tsx`
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeSearchService.cs`
- `src/server/api/Sprk.Bff.Api/Models/Office/EntitySearchResponse.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEntitySearchMappingTests.cs`
- `tests/integration/contract/Api/Office/OfficeEntitySearchAuthorizationContractTests.cs`
- `src/client/office-addins/shared/taskpane/components/views/__tests__/CreateTodoView.test.tsx` (new)
- `src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneNavigation.test.tsx`
- `src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneShell.test.tsx`
- `src/client/office-addins/ci-gated-suites.txt`

## 8. Deviations from the literal POML steps (directional mode)

None material. The POML's 4 steps were followed in order (read background → server email field + test → client tab/icon/spacing/rows/chip/confirmation + tests → gates/notes). One addition beyond the POML's literal step 3 wording: a dedicated `CreateTodoView.test.tsx` was written (not explicitly named in `<relevant-files>`) because several of the task's acceptance criteria are `testable="true"` and scoped tightly to this component, and — unlike task 049's prior App.tsx-level judgment call — mocking just `CreateTodoView`'s own props (no `fetch`/`authService` needed) was a proportionate lift. Registered in `ci-gated-suites.txt` in the same change per ADR-038.
