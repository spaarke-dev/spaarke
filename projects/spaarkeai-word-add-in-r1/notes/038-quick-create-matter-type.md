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

---

## 8. Coordinator follow-up 1 (2026-09-13): the initial-load failure was a dead end — Retry fix

**Finding** (code-review Warning 1, addressed on the coordinator's instruction): `fetchMatterTypes`
swallowed every failure to `[]`, and `SaveFlow`'s effect fetched exactly once on mount. A transient
BFF/Dataverse error or a network drop on that ONE call made the required Matter Type field permanently
unsatisfiable for the rest of the pane session — Matter quick-create was effectively dead until reload.
That blocks a core action on a transient fault.

**Fix**:
- `matterTypeLookupService.fetchMatterTypes` now **throws** on a non-2xx response, a network failure,
  or a malformed body, instead of swallowing to `[]`. This is the load-bearing change: it lets the
  caller tell "the call failed" apart from "the call succeeded and the table has zero active rows" —
  two states that need different UI (a Retry affordance vs. a quiet "none configured" note).
- `SaveFlow.tsx` replaced its one-shot mount effect with a `loadMatterTypes` `useCallback` (guarded by
  a mounted-ref, so a stale response never sets state after unmount) that is invoked once on mount AND
  is the exact function passed down as `onRetryMatterTypes` — the SAME code path serves the initial
  load and every retry, so there is no second, drifting implementation to keep in sync.
- `RelatedToPicker.tsx` renders three MUTUALLY EXCLUSIVE states for the Matter Type field, gated on
  `matterTypesLoading` / `matterTypesError` / `matterTypeOptions.length`: (1) loading → the Dropdown's
  placeholder reads "Loading matter types…" and it is disabled (never an empty-looking-final
  dropdown); (2) a genuine failure → a `role="alert"` message plus a **Retry** button (hidden while
  loading, so a click can't be double-fired mid-request — "one user-initiated retry at a time"); (3) a
  successful load with zero configured rows → the pre-existing quiet "No matter types are available
  right now." note. The Create button's existing disabled-until-a-type-is-chosen logic needed NO
  change — an empty `matterTypeOptions` (whichever of the three states caused it) already leaves
  `selectedMatterTypeId` unset.
- The failure message is announced via `useAnnounce('...', 'assertive')` in `SaveFlow.loadMatterTypes`
  (NFR-11) — the one state change here a screen-reader user could otherwise miss entirely, since the
  field just becomes a disabled, silent dropdown.
- Project/Invoice quick-create reads none of `matterTypesLoading`/`matterTypesError` — the block that
  renders them is gated on `selectedType === 'Matter'` in `RelatedToPicker`, unchanged from before this
  fix. Verified by a dedicated test (§10).

**No auto-retry loop**: there is no interval, no retry-on-mount-again — the ONLY way `loadMatterTypes`
runs a second time is the user clicking Retry (or, per §9 below, a cache-clearing warning making the
NEXT call a live fetch instead of a cache hit — still user-initiated, via the next create attempt or
pane reopen, never a background loop).

## 9. Coordinator follow-up 2 (2026-09-13): owner decision — keep the route, add a cache

**Owner decision (2026-09-13)**: keep `GET /api/office/search/matter-types` as built (§2's decision
stands — no route-shape change was made or requested), and cache the list in the pane so repeat opens
don't call the BFF.

**Rationale, as relayed by the coordinator**: matter-type ids ARE preserved across environments by
`scripts/Migrate-DataverseData.ps1`, so a hard-coded five-row list client-side WAS a real option (unlike
most reference data, this one wouldn't have silently drifted between a customer's dev/UAT/prod
environments). The owner chose the live call plus a cache instead specifically so that a **new or
customer-added matter type appears without an add-in redeploy** — a hard-coded list would require a
code change + release every time an admin added a type via `sprk_mattertype_ref`, while the live+cache
approach picks it up on the next uncached load.

**Implementation** (`matterTypeLookupService.ts`):
- **Cache only successful, non-empty results.** `setCachedEntry` returns immediately (writes nothing)
  when `items.length === 0`. A failed call already throws before reaching the cache-write path at all
  (§8's fix). This is deliberate: an empty or failed result cached would defeat the Retry fix — a retry
  would just re-serve the same absence from cache instead of trying the network again.
- **Scope**: an in-memory `let memoryCache` (module-level — lives for the page/pane session) checked
  first, then `window.localStorage` under the key `spaarke.officeAddin.matterTypes.v1`
  (`{ fetchedAt, items }`), TTL ~24h (`24 * 60 * 60 * 1000` ms). A localStorage hit is PROMOTED into
  `memoryCache` so the next call in the same session skips `localStorage` entirely.
- **Every `localStorage` read and write is wrapped in try/catch** (`readLocalStorageCache`,
  `writeLocalStorageCache`, `removeLocalStorageCache`) — storage can throw inside the Office webview
  (quota, disabled storage, a privacy mode). A throw, or a malformed stored value (bad JSON, wrong
  shape, a non-string id/name/code), is treated identically to "no cache" — the caller falls through to
  a live fetch. Never a broken field over a storage fault.
- **Invalidation**: an expired entry (`Date.now() - fetchedAt >= TTL`) is NOT returned by
  `getFreshCachedEntry`, so it triggers a fresh fetch. **A stale entry is NOT served while refetching**
  (no stale-while-revalidate) — kept simple per the coordinator's "optional" framing; noting the choice
  here as asked. `clearMatterTypesCache()` (exported) empties both memory and `localStorage`
  unconditionally; `SaveFlow.createRelatedRecord` calls it when a Matter quick-create response's
  warnings match `warningsIndicateMatterTypeNotFound` (case-insensitive "matter type" + "not found" —
  deliberately NOT matched against the distinct "…could not be checked…" transient warning, which does
  not mean the type is invalid and must not evict a good cache entry). Per the coordinator's literal
  ask, this ONLY clears the cache for the next fetch (the pane's next open, or this session's next
  uncached call) — it does not force an immediate in-session re-fetch of the currently-rendered list;
  noting that as a deliberate scope decision, not an oversight.
- Placement: kept inside the existing `matterTypeLookupService.ts` (no new file) — the cache is an
  implementation detail of "fetch the matter types," not a separate component; CLAUDE.md §11 extend
  question answered "yes, extend the existing fetch function" with no new service/DI surface created.

## 10. Tests added for the two coordinator follow-ups

- `shared/taskpane/services/__tests__/matterTypeLookupService.cache.test.ts` (new, 11 tests): cached
  entry served with no second fetch within the TTL; the SAME holds across a `jest.resetModules()`
  reload (a real pane reopen) because `localStorage` — unlike the in-memory cache — survives it; an
  expired entry refetches; `localStorage` throwing on every access still yields a working fetch; a
  malformed stored value is ignored; a failed fetch is not cached; a successful-but-empty result is not
  cached; `clearMatterTypesCache` empties both layers; `warningsIndicateMatterTypeNotFound`'s three
  cases (matches, does not match the transient warning, does not match no-warnings).
- `RelatedToPicker.matterType.test.tsx` gained a second `describe` block (4 tests): a failed load shows
  the error + Retry with Create disabled; the loading state never looks like an empty final dropdown
  and offers no Retry to double-click; a Retry that transitions loading → success lets the user pick a
  type and create (via `rerender` simulating the prop transitions a real retry drives); Project
  quick-create is unaffected by a Matter-types failure.
- `SaveFlow.matterTypeQuickCreate.test.tsx` gained one end-to-end test: a Matter Type cache is seeded
  directly, the pane is proven to serve the dropdown from it (`/search/matter-types` is asserted never
  called), a "not found" warning comes back from the quick-create call, and `localStorage`'s entry is
  asserted gone afterward. The file's `beforeEach` now calls `clearMatterTypesCache()` for full
  cross-test isolation (the two original tests do not seed or assert on the cache and are unaffected).

All new/changed test files: 23/23 pass together (multiple runs); the 18 gated suites remain 277/277;
the two pre-existing suites most exposed to `SaveFlow.tsx`/`RelatedToPicker.tsx` changes
(`SaveFlow.test.tsx` + `EntityPicker.test.tsx`) remain an EXACT match to their documented baselines
(13 failed / 44 passed / 57 total) — zero regression from either follow-up.
