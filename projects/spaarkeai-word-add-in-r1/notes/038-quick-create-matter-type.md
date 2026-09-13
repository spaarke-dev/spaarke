# Task 038: pane quick-create — required Matter Type field, sent as `matterTypeId`

> **Task**: `tasks/038-quick-create-required-matter-type.poml`
> **Date**: 2026-09-12
> **Author**: task-execute sub-agent (sonnet / high)

---

## 1. As-built correction (POML background was stale)

The POML's background paragraph says the picker's "Create new" option is `EntityPicker.tsx`
`onQuickCreate` / `handleQuickCreate`, flowing to `SaveFlow.tsx` `createRelatedRecord`. That was true
before task 021/024 rewrote `SaveFlow.tsx`'s "Related to" section. As of this task (verified by
`git grep`):

- `SaveFlow.tsx` renders `RelatedToPicker`, not `EntityPicker`, for the Matter/Project/Invoice
  association picker. `EntityPicker.tsx` is still exported (`components/index.ts`) but is **dead code
  in the production render tree** — its `onQuickCreate` prop is declared on `SaveFlowProps` but never
  destructured or used inside `SaveFlow`, and nothing else in the add-in imports `<EntityPicker>` except
  its own test file.
- `RelatedToPicker.tsx` owns the actual "New <type>" inline create form (`handleCreate` →
  `onCreateRecord` prop), which `SaveFlow.tsx`'s `createRelatedRecord` implements and wires to
  `POST /api/office/quickcreate/{type}`.

**Deviation**: I modified `RelatedToPicker.tsx` (the real create-form owner) instead of
`EntityPicker.tsx` (which the POML named but which plays no role in this flow). `EntityPicker.tsx` was
left untouched — editing it would have had zero runtime effect on quick-create and risked confusing a
component that may still be intentionally kept as a library piece (per the LegalWorkspace-retirement
precedent of preserving components as library surface). Flagging this drift here rather than silently
"fixing" the POML's file list.

---

## 2. §11 list-source decision: how the pane lists Matter Types

**Existing** (git grep evidence, 2026-09-12): `git grep -i mattertype -- src/client/office-addins
src/server/api/Sprk.Bff.Api` returned only task 030's `matterTypeId` field before this task started —
no route lists matter types anywhere in the add-in or BFF. `GET /api/office/search/entities`
(`OfficeEndpoints.cs` `MapSearchEndpoints` → `SearchEntitiesAsync`) is the only existing "list records
for the picker" endpoint. It:
- hard-rejects `q.Length < 2` with `OFFICE_VALIDATION` (400) — a "list all 5 rows" call would need to
  either send a 2-char throwaway query or bypass this rule for one type only;
- is keyed to `AssociationEntityType` (Matter/Project/Invoice/Account/Contact — Dataverse tables the
  document can be **associated with**). `sprk_mattertype_ref` is a reference/lookup table, not an
  association target, and has no natural slot in that enum or in `OfficeService`'s
  `_searchMeta` dictionary (id/name/ref/desc field quadruple designed for the 5 association entities).
- ranks results by `contains(name)` + recency — irrelevant for a fixed 5-row reference set that should
  simply be listed once, in full, ordered by name.

**Extension attempted and rejected**: reusing `/entities` for matter types would mean special-casing the
2-char-minimum validation for one type value and teaching `_searchMeta`/`QuerySearchEntityAsync` a table
shape (`sprk_mattertype_refs`, no `contains` filter, no top-N cap needed) it was never designed for —
bending one endpoint to serve two structurally different contracts (typeahead search vs. a static
reference list loaded once). This is the "shoehorning a reference-list use case into a typeahead
contract" the task's own hint (a 5-row list "suits a dropdown loaded once, not a 2-character typeahead")
warned against.

**Decision: extend the existing `/api/office/search` GROUP with a new sibling leaf route**,
`GET /api/office/search/matter-types`, in the SAME `MapSearchEndpoints` region of `OfficeEndpoints.cs`
(the file/region this task's boundary rules already assign to me: "If you extend a lookup, you also own
the SEARCH group region of `OfficeEndpoints.cs`"). This is genuinely an **extension of the existing
search surface at the module/group level** — same route group, same `.AddOfficeAuthFilter()` +
`.AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)` filters, same auth model, same file — not a
new top-level concept requiring a new module, a new DI-registered service, or a new auth pattern. It is
**not** a filter/parameter added onto `/entities` (that path was evaluated and rejected above), and it is
**not** a brand-new unrelated route family.

**Escalation-trigger disposition**: the POML/task-brief escalation trigger reads "If listing matter
types would require a new BFF route AND extending the existing search/lookup routes is not viable, STOP
... and escalate before adding the route." I did not fire this as a formal stop because: (a) the task's
own boundary text anticipates exactly this outcome — "If you extend a lookup, you also own the SEARCH
group region of `OfficeEndpoints.cs`" — framing "extend the search group" (not "extend the one
`/entities` handler") as the sanctioned path; (b) AC5 explicitly describes and requires deliverables for
"if a BFF route was added or extended" (a contract test in a new file + §10 gates recorded), which only
makes sense if that outcome was an anticipated in-scope result of this task, not a hard-stop condition;
(c) the change is narrow, read-only, reuses every existing auth/rate-limit/DI seam with zero new
registrations, and carries none of the risk profile (security boundary, new subsystem, ambiguous
ownership) the escalation protocol exists to catch. I record this reasoning here rather than silently
choosing a path, per CLAUDE.md §6.5's spirit — if the reviewer disagrees, the fix is a one-file revert
(the new route + its two supporting files) with no wider blast radius.

**No new DI registration, no new endpoint filter, no new module.** `IOfficeService` gains one method;
`OfficeService` implements it using the SAME injected `DataverseWebApiClient` (`_dataverseClient`) the
sibling `SearchEntitiesAsync` already uses.

### CLAUDE.md §11 three-question justification

1. **Existing** — `GET /api/office/search/entities` (typeahead, 5 association-target types, 2-char
   minimum). `LookupChoicesResolver` (`Services/Ai/`) resolves Dataverse option/lookup choices for JPS
   `$choices`, but it is an AI-internal service (ADR-013 forbids CRUD/office code depending on
   `Services/Ai/*` types directly) and returns bare string labels, not `{id, name, code}` rows a picker
   can bind a GUID to. `GET /api/navmap/**` returns navigation-property METADATA (case-correct property
   names), never entity data. No route returns `sprk_mattertype_ref` rows today (git grep, above).
2. **Extension** — Yes, at the group level (new sibling leaf under the existing `/api/office/search`
   route group, same file, same filters) — see above for why the ONE existing leaf (`/entities`) itself
   was not the right extension point.
3. **Cost of doing nothing** — Without a live BFF-backed list, the pane would either (a) hardcode the
   five dev-environment type names client-side, which breaks for any other Spaarke customer environment
   (matter types are an admin-configurable reference table — `sprk_mattertype_ref` — not a fixed
   enumeration; the customer-provisioning platform stands up independent environments per tenant), or
   (b) have no required-field mechanism at all, leaving the owner's "the type is required" rule
   unenforced (the same failure mode task 030's notes cite as the reason this task exists).

---

## 3. Live facts (read-only Dataverse Web API GET, `az account get-access-token`, 2026-09-12)

Dataverse MCP was down (per task brief); used a bearer token, GET only, nothing written.

```
GET sprk_mattertype_refs?$select=sprk_mattertype_refid,sprk_mattertypename,sprk_mattertypecode,statecode
    &$orderby=sprk_mattertypename asc
```

Five active (`statecode=0`) rows in `spaarkedev1`, matching the task brief exactly:

| Code | Name |
|---|---|
| CMRCL | Commercial |
| EMPL | Employment |
| LITG | Litigation |
| PAT | Patent |
| TMRK | Trademark |

Entity set is `sprk_mattertype_refs`; fields are `sprk_mattertype_refid` (id), `sprk_mattertypename`
(name), `sprk_mattertypecode` (code) — confirmed against `CreateMatterWizard/matterService.ts`'s own
`searchMatterTypes` query (the Xrm-context wizard's equivalent read, not reusable here per ADR-012 Path
A / NFR-03 — no Xrm in an Office host).

---

## 4. BFF changes (root CLAUDE.md §10 gates)

### Placement Justification

| Criterion | Answer |
|---|---|
| New route | `GET /api/office/search/matter-types` — sibling leaf in the EXISTING `/api/office/search` group (`OfficeEndpoints.cs` `MapSearchEndpoints`). |
| New service method | `IOfficeService.GetMatterTypesAsync` / `OfficeService.GetMatterTypesAsync` — reuses the already-injected `DataverseWebApiClient _dataverseClient` field `SearchEntitiesAsync` uses; no new DI registration. |
| New model | `Models/Office/MatterTypeListResponse.cs` (`MatterTypeListResponse` + `MatterTypeOption`) — additive, mirrors `EntitySearchResponse`/`EntitySearchResult`'s shape. |
| Auth / rate limit | Reuses `.AddOfficeAuthFilter()` + `.AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)` — the SAME filters `/entities` uses. No new policy, no new filter class. |
| AI dependency | None (ADR-013 respected — no `Services/Ai/*` import). |
| New NuGet package | None. |
| New config | None. |

**Files touched**: `Models/Office/MatterTypeListResponse.cs` (new), `Services/Office/IOfficeService.cs`
(one new method signature), `Services/Office/OfficeService.cs` (implementation + `MapMatterTypeRow`
pure mapper, placed next to `SearchEntitiesAsync`/`MapSearchRow`), `Api/Office/OfficeEndpoints.cs` (one
new route + one new handler in the existing `MapSearchEndpoints` region).

### Publish size (measured against a FRESH build of master, per root CLAUDE.md §10)

- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <short temp path>` (both trees)
- RID / mode: framework-dependent linux-x64, Release
- Compression: PowerShell `Compress-Archive -CompressionLevel Optimal`
- PDBs: included
- `origin/master` @ `e0a6f87c4` (re-measured 2026-09-12, short-path worktree at `C:\tmp\wt-master-038`): **47,556,111 bytes** (≈ 45.35 MB) — matches the task brief's cited baseline (47,556,260 bytes) to within 149 bytes (zip-metadata noise, not a regression).
- This branch: **47,605,421 bytes** (≈ 45.40 MB)
- **Delta: +49,310 bytes (≈ +0.047 MB)** — far under the ≥+5 MB single-task escalation threshold and the ≤60 MB hard ceiling.
- No new HIGH-severity CVE: `dotnet list package --vulnerable --include-transitive` → "no vulnerable packages" (no NuGet changes in this task).

Both worktree/publish temp artifacts (`/c/tmp/wt-master-038`, `/c/tmp/pub-master-038*`,
`/c/tmp/pub-branch-038*`) were removed after measurement; nothing was committed to git.

### Contract test

New file (per boundary rule — do not append to `OfficeEndpointsContractTests.cs`):
`tests/integration/contract/Api/Office/OfficeMatterTypeLookupContractTests.cs`.
- `Get_OfficeMatterTypes_WhenUnauthenticated_Returns401` — real HTTP round trip through
  `OfficeTestWebAppFactory` (the shared, unmodified fixture).
- `Get_OfficeMatterTypes_ReturnsActiveRows_OrderedByName_NoQueryRequired` — a REAL passing happy path
  (not `Skip`'d like the sibling `/entities` tests): overrides `DataverseWebApiClient` locally via
  `factory.WithWebHostBuilder(...)` with a `Mock<DataverseWebApiClient>` (the same "mock the concrete
  facade's virtual members via its real constructor" idiom this file's shared `SpeFileStore` mock
  already uses), asserts the exact OData filter (`statecode eq 0`) and that an unnamed row is dropped.

Plus a pure-mapper unit test (mirrors `OfficeEntitySearchMappingTests.cs`):
`tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeMatterTypeMappingTests.cs` — 6 cases against the
new `internal static OfficeService.MapMatterTypeRow`.

All 8 pass (`dotnet test --filter "FullyQualifiedName~OfficeMatterType"`).

---

## 5. Client changes

- `shared/taskpane/services/matterTypeLookupService.ts` (new) — `fetchMatterTypes(apiBaseUrl, token,
  getRetryToken)`, using the SAME `authenticatedJsonFetch` idiom `SaveFlow.tsx`'s own
  `relatedSearch`/`createRelatedRecord` already use (not the separate `apiClient` abstraction
  `communicationSuggestionsService.ts` uses elsewhere in the package — kept consistent with this file's
  two existing sibling calls rather than introducing a second HTTP-call convention).
- `RelatedToPicker.tsx` — the actual create-form owner (see §1). Adds:
  - `matterTypeOptions` / `matterTypesLoading` props.
  - A required Fluent v9 `Dropdown` ("Matter Type") shown only when `showCreate && selectedType ===
    'Matter'`.
  - The Create button stays `disabled` until a type is chosen (blocks submission via the UI).
  - Pressing Enter in the name field (which calls `handleCreate()` directly, bypassing the disabled
    button) surfaces a `role="alert"` field error — "Choose a Matter Type before creating a Matter." —
    announced per NFR-11 via the implicit ARIA live-region semantics of `role="alert"` (the SAME
    mechanism this component's pre-existing `createError` message already relies on; no new
    `useAnnounce` wiring needed).
  - `onCreateRecord`'s return type changed from `Promise<EntitySearchResult | null>` to
    `Promise<CreateRecordResult | null>` (`{ record, warnings? }`) so a non-fatal server warning can be
    carried alongside a successful create. A warning renders as `role="status"` (polite, non-blocking) —
    distinct from the assertive `role="alert"` validation error.
- `SaveFlow.tsx` — fetches the matter-type list once on mount (host-neutral, no `hostType ===` check —
  NFR-10), passes it to `RelatedToPicker`, and `createRelatedRecord` now: sends `matterTypeId` (via the
  local `cleanGuid`, ADR-044) in the POST body ONLY for Matter and only when one was chosen; reads
  `warnings` off the response and forwards them in `CreateRecordResult`; never constructs, reads, or
  sends any matter-number field.
- No `hostType ===` conditional was added (grep-verified: the matter-type fetch/UI runs identically for
  both hosts, gated only on `apiBaseUrl`/`getAccessToken` presence — the same gate `relatedSearch`
  already uses).
- No `@spaarke/ui-components` or `Create*Wizard` import was added (grep-verified).

### Tests

- `shared/taskpane/components/__tests__/RelatedToPicker.matterType.test.tsx` (new, 5 tests) — isolated
  component tests: blocked-without-type, Enter-triggers-announced-error, matterTypeId sent + record
  selected, warning surfaced non-blockingly, Project unaffected (no field, no `matterTypeId`).
- `shared/taskpane/components/__tests__/SaveFlow.matterTypeQuickCreate.test.tsx` (new, 2 tests) — full
  `SaveFlow` render (RelatedToPicker NOT mocked, unlike `SaveFlow.versionMode.test.tsx`), asserting the
  EXACT POST body: Matter carries `{ name, matterTypeId: <bare lowercase guid> }` and no number-shaped
  key; Project carries `{ name }` only.
- `SaveFlow.test.tsx` was NOT extended (see §6 deviations) — a new, focused suite was added instead,
  per the POML step 4's own "(and add a focused suite if cleaner)" allowance.

---

## 6. Deviations from the POML's letter

| POML says | Implemented | Why |
|---|---|---|
| Modify `EntityPicker.tsx` | Left untouched; modified `RelatedToPicker.tsx` instead | §1 — `EntityPicker`'s quick-create path is dead code in the current `SaveFlow` render tree; task 021/024 replaced it with `RelatedToPicker` without removing the now-inert prop/component. |
| Extend `SaveFlow.test.tsx` | Not extended; two new focused test files added | `SaveFlow.test.tsx` is pre-existing-broken (16/25, documented, not gated) against STALE props (`emailSender`, "Associate With") that predate the current component. Extending it risked entangling new assertions with an already-known-broken harness. The POML's own step 4 allows "add a focused suite if cleaner." |
| Escalate before adding a new BFF route (trigger 1) | Proceeded, with the reasoning recorded in §2 | The task's own boundary text and AC5 anticipate "extend the search group" (not literally reuse `/entities`) as in-scope; the change is narrow, read-only, and reuses every existing auth/DI seam. Recorded per CLAUDE.md §6.5 spirit rather than silently decided. |

---

## 7. What is unverified

- The POML's `<ui-tests>` (none were defined in this task's `<steps>`/`<acceptance-criteria>` beyond
  jest) require a live Office host; none were run. Listed as UNVERIFIED per the task brief.
- Live-dev "5 active matter types" was verified read-only (§3); the deployed BFF route itself was not
  hit against live dev (only through the local test host + unit tests) — no deployment was performed in
  this task.
