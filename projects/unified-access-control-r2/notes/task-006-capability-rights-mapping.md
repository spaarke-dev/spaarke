# Task 006 — caller-scoped `PermissionsEndpoints`: the mapping table and the design

> **Date**: 2026-08-21 · **Spec**: FR-05 (+ closes the remainder of FR-02) · **Finding**: A-4 (High)
> Predecessor reasoning: [`task-004-caller-scoped-design.md`](task-004-caller-scoped-design.md) §6, which
> named this task as the owner of the direct-call path.

---

## 1. What was wrong

```
src/server/api/Sprk.Bff.Api/Api/PermissionsEndpoints.cs:76   (single document)
src/server/api/Sprk.Bff.Api/Api/PermissionsEndpoints.cs:159  (batch)
    await accessDataSource.GetUserAccessAsync(userId, documentId, userAccessToken: null, ct)
```

Both handlers called `IAccessDataSource` **directly**, bypassing `AuthorizationService` entirely. The
`userAccessToken: null` selected app-only evaluation, so the capabilities returned described what the
**application** could do — and they were returned to anyone who could authenticate.

Task 004 could not reach this path, which is why FR-02's acceptance criterion ("`userAccessToken: null`
no longer reaches `IAccessDataSource` on any caller-scoped path") stayed open after 004 landed. It is
closed now: a repo grep for `userAccessToken: null` returns **zero** production call-sites.

## 2. The capability → rights mapping table

Unchanged by this task, and deliberately so — it already derived every flag from
`OperationAccessPolicy`, the same table the enforcement path consults. What changed is **whose rights**
are fed into it. Recorded here because the POML asks for the table explicitly.

| Capability | Operation string | Required rights |
|---|---|---|
| `CanPreview` | `driveitem.preview` | Read |
| `CanDownload` | `driveitem.content.download` | **Write** — Spaarke policy: download is not a read |
| `CanUpload` | `driveitem.content.upload` | Write + Create |
| `CanReplace` | `driveitem.content.replace` | Write |
| `CanDelete` | `driveitem.delete` | Delete |
| `CanReadMetadata` | `driveitem.get` | Read |
| `CanUpdateMetadata` | `driveitem.update` | Write |
| `CanShare` | `driveitem.createlink` | Share |
| `CanViewVersions` | `driveitem.versions.list` | Read |
| `CanRestoreVersion` | `driveitem.versions.restore` | Write |
| `CanMove` | `driveitem.move` | Write + Delete |
| `CanCopy` | `driveitem.copy` | Read + Create |
| `CanCheckOut` | `driveitem.checkout` | Write |
| `CanCheckIn` | `driveitem.checkin` | Write |

**Interim consequence until task 005 lands.** `DataverseAccessDataSource.QueryUserPermissionsAsync`
still ceilings the snapshot at `AccessRights.Read` (finding A-20), so in production only `CanPreview`,
`CanReadMetadata` and `CanViewVersions` can ever be true — the other eleven are false for everyone. That
is the honest current state, not a regression introduced here: before this task they were false too,
just app-scoped-false. Flagged in the PR per the POML's `<notes>`.

## 3. The design: one snapshot accessor, not fourteen decisions

`AuthorizeAsync` answers "may this caller do X?" and returns a boolean. A capabilities endpoint needs
fourteen answers. Calling `AuthorizeAsync` fourteen times per document would have been the most literal
reading of "route through `AuthorizationService`", but for the batch route that is **1,400 rule-chain
evaluations for a 100-document request** — and the batch endpoint exists specifically to avoid N+1.

So `AuthorizationService` gained a snapshot accessor instead:

```csharp
public async Task<AccessSnapshot> GetCallerAccessAsync(
    string userId, string resourceId, string? userAccessToken, CancellationToken ct = default)
```

and `AuthorizeAsync` was refactored to call it. One data-source round trip per document; fourteen
capability projections computed locally from the result.

### Why the token parameter has no default value

This is the load-bearing detail, and it is a different mechanism from task 004's.

Task 004 used `required` on `AuthorizationContext.UserAccessToken`. That works for an object initializer
but has no equivalent for a method parameter. The relevant insight is that **A-4's root cause was the
`= null` default** on `IAccessDataSource.GetUserAccessAsync`, not a missing null check: the default let a
new direct caller inherit app-only evaluation by simply not thinking about it. A **mandatory positional
parameter** reproduces the forcing function exactly — the method cannot be called without stating intent,
and an intentional app-only caller has to write a visible `null` that a reviewer sees.

### Why "single source" is now structural rather than asserted

`GetCallerAccessAsync` is the **only** member of `AuthorizationService` that touches
`_accessDataSource`. Acceptance criterion 5 ("capabilities derive from the same snapshot as enforcement")
is therefore checkable by grep, not by reading two code paths and hoping they agree. A test pins it
directly: `AuthorizeAsync_AndGetCallerAccessAsync_PresentIdenticalArgumentsToTheDataSource` asserts both
paths hand the data source identical argument tuples, so a future change that gave `AuthorizeAsync` its
own path fails loudly.

### §11 three-question gate

- **Existing**: `AuthorizationService.AuthorizeAsync` — returns a decision, not rights; cannot serve a
  fourteen-capability projection without fourteen calls.
- **Extension**: yes — a method on the existing service, not a new type or service. ADR-003's "MUST NOT
  create new service layers for auth" is respected: no new layer, no new interface, no new registration.
- **Cost of doing nothing**: the endpoint keeps its own direct `IAccessDataSource` call, and the
  `= null` default remains reachable from a second place. Concretely: the next developer adding an
  affordance endpoint copies line 76 and reintroduces A-4.

### Placement Justification (CLAUDE.md §10 / `bff-extensions.md`)

Required for every BFF-touching change, and stated even though nothing moved.

| Component | Placement | Why |
|---|---|---|
| `GetCallerAccessAsync` | **`Spaarke.Core`** (`Auth/AuthorizationService.cs`) — extend in place | It is authorization data resolution, which is exactly what this class already owns. Placing it in the BFF would fork access resolution across two assemblies and reintroduce the "two disjoint authorization systems" this project exists to remove |
| Endpoint changes | **BFF** (`Api/PermissionsEndpoints.cs`) — extend in place | HTTP surface and token extraction are BFF concerns by definition; `HttpContext` cannot cross into `Spaarke.Core`, which has no ASP.NET Core dependency (`LayerDependencyTests` guards this — still 36/36 green) |

No new endpoints, no new DI registrations, no new packages, no background work. Publish size is
unchanged at **43.65 MB** compressed (baseline 44.96, ceiling 60) — a 0.00 MB delta, as expected for a
change that adds no dependencies. No new HIGH/CRITICAL CVE. No new CRUD→AI dependency (this surface is
unrelated to `Services/Ai/`).

### Deliberately NOT added to `IAuthorizationService`

The interface exists as a testing seam for filters that need `AuthorizeAsync`. Widening it would force
every mock and implementor to change for no benefit (ADR-010 DI minimalism). `PermissionsEndpoints`
injects the **concrete** `AuthorizationService`, exactly as `DocumentAuthorizationFilter` already does
(`DocumentAuthorizationFilter.cs:26`) — established precedent, and the correct test seam per ADR-003 is
`IAccessDataSource` one level down anyway.

## 4. Response shape for a caller with no access — 200 + all-false

**Decision: 200 with every capability false, not 403.** (POML step 3.)

| Reason | Detail |
|---|---|
| FR-05's own wording | "a user without access receives `CanPreview=false`" presupposes a body to read the flag from |
| The batch route cannot express it otherwise | A 100-document request where 3 are inaccessible has no sensible single status code |
| It is what the endpoint is *for* | This is an affordance query. "You can do nothing here" is a valid, useful answer — the UI renders a read-only view rather than an error |
| No existence disclosure | An inaccessible document and a nonexistent one both return all-false, so the response does not distinguish them. A 403-vs-404 split would |
| Consistent with the pre-existing error path | The `catch` already returned 200 + all-false ("Fail-closed: Return no permissions on error") |

All fail-closed paths now go through one `NoCapabilities(...)` factory, so a capability added to the DTO
later cannot default to `true` on one error path and `false` on another.

## 5. Second disclosure found and closed: body-supplied `UserId`

Not part of A-4's original wording; found while reading the batch handler for step 1.

`PermissionsEndpoints.cs:134` preferred a `UserId` supplied in the **request body** over the caller's
claims. That is straightforwardly incompatible with a caller-scoped answer — but it is worse than
cosmetic, and the reason is worth recording because it is not obvious from the endpoint alone:

`DataverseAccessDataSource.cs:184-199` treats `userId` and `userAccessToken` as **independent** inputs.
`userId` (the Entra `oid`) selects *whose Dataverse principal is looked up*; the token only selects
*which auth mode the query runs under*. So after caller-scoping, a body-supplied id would have:

1. run the Dataverse query **as the caller** (OBO), while
2. asking about a **different principal's** permissions, and
3. written task 014's cache key `sdap:auth:access:obo:{userId}:{resourceId}` under the **victim's** oid —
   a cache-poisoning primitive that outlives the request by up to the 60s TTL.

`BatchPermissionsRequest.UserId` is therefore **removed**, and identity comes from the validated token
only. Removal is wire-compatible: `System.Text.Json` ignores unknown members, so a stale client still
sending the field is ignored rather than served someone else's capabilities. A test asserts the spoofed
oid never reaches the data source.

## 6. Escalation trigger — evaluated, did not fire

The POML trigger: *"If SPA/PCF clients break when capabilities become caller-scoped (e.g. UI relies on
always-true CanPreview), STOP and report the affected consumers before merging."*

Verified by two independent greps:

| Search | Result |
|---|---|
| `/permissions` route string across `*.{ts,tsx,cs,razor,html}` | Only SPE-admin's unrelated `/api/spe/**` routes and task 001's characterization test |
| `api/documents` across all `*.{ts,tsx}` | 30 hits, all `preview-url` / `open-links` / `content` / `upload` / `analyze` / `bulk-download` — **none** `/permissions` |

**Zero clients consume `/api/documents/{id}/permissions` or `/permissions/batch`.** No consumer can
break, so the trigger correctly does not fire. (The `canPreviewDocument` identifiers in
`ComposeWorkspace.tsx` and `NarrativeBullet.tsx` are unrelated local variables — they gate a preview
dialog on whether a document id exists, and never call this endpoint.)

This also means the endpoint has been shipping a disclosure that **nothing was even using** — worth
noting for the wrap-up, because "retire it" was a legitimate alternative to "fix it". Fixing was chosen:
the endpoint is the natural home for affordance queries the Manage Access PCF (tasks 065–067) will need,
and a correct caller-scoped implementation is a prerequisite for that rather than dead weight.

## 7. Test coverage

`tests/integration/auth/UnifiedAccessControl/` — the ADR-038 §2 security-auth KEEP path.

**New file `PermissionsEndpointCallerScopedTests.cs`** (7 tests) with a `CallerScopedAccessTestFixture`
that substitutes `IAccessDataSource` — the module boundary ADR-003 names as a seam and ADR-038 §4 names
as the correct substitution point. No transport-level mocking (ban B1).

### The vacuity problem this fixture exists to solve

With the **real** data source, the offline test host fails closed to `AccessRights.None` for every
request. So "all capabilities false" was true **before and after** the fix, and an endpoint test
asserting only that would have passed vacuously — the exact failure mode this project has already hit
twice. The double grants `Read|Write` **only** to the holder of the fixture's bearer token **and** only
on one specific document, so:

- a caller with access → `CanPreview=true`, `CanDownload=true` (Write), `CanDelete=false` (needs Delete),
  `CanUpload=false` (needs Create) — proving the mapping is still rights-based, not blanket
- the same caller on another document → every capability false — FR-05's acceptance, now meaningful

### Empirically verified to discriminate

Not merely reasoned about. The token forwarding was temporarily reverted in each handler and the suite
re-run:

| Reverted | Result |
|---|---|
| Single-document handler token → `null` | **2 tests fail** |
| Batch handler token → `null` as well | **3 tests fail** |
| Both restored | 35/35 pass |

### Flipped and added elsewhere

| Test | Change |
|---|---|
| `EndpointAuthorizationCharacterizationTests.Characterization_GetPermissions_..._Returns200WithCapabilities` | Flipped → `GetPermissions_ForCallerWithoutDocumentAccess_ReturnsNoCapabilities`. Its doc comment states explicitly that it asserts the FR-05 **shape only** and does NOT prove caller-scoping, pointing at the new file for that — so it cannot be mistaken for the guarantee |
| `AuthorizationServiceCharacterizationTests` | +3: `GetCallerAccessAsync_WithNoCallerToken_ReturnsNoRightsAndNeverConsultsDataSource` (`[Theory]` over null/empty/whitespace, with a double that would GRANT if reached), `GetCallerAccessAsync_WithCallerToken_ForwardsItAndReturnsTheCallersRights`, and the single-source pin in §3 |

## 8. Follow-on obligations created

| # | Obligation | Owner |
|---|---|---|
| 1 | The eleven Write+ capabilities stay false for everyone until the Read ceiling is lifted | **task 005** (already carries the `AppendToAccess` obligation from task 003) |
| 2 | `AuthorizeAsync` and `GetCallerAccessAsync` must keep sharing one data-source call. Phase 1's evaluator spine (task 032) replaces this method's body — it MUST NOT reintroduce a second access path | **task 032** |
| 3 | If the Manage Access PCF (065–067) needs record capabilities for non-document entities, extend this endpoint rather than adding a sibling — the mapping table and the fail-closed factory are already here | **tasks 065–067** |

## 9. Drive-by correction

`CachedAccessDataSource.cs:27-28` claimed `AuthorizationService` "always calls with
`userAccessToken: null` (app-only)". True when task 014 wrote it; made false by task 004. Left in place
it would tell the next reader of the cache decorator that the main authorization path is app-only — a
security-relevant falsehood in a file whose whole subject is separating the two modes. Corrected, with
the history preserved so the `authMode` flag's continuing purpose stays clear.
