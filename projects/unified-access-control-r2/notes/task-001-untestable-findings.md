# Task 001 — characterization coverage, and the 10 findings it could not reach

> **Date**: 2026-08-21 · **Task**: 001 (access-path characterization + negative suite, spec NFR-07)
> **Status**: suite delivered and green (62 tests). **Escalation open** on the uncovered half.
> **POML escalation trigger fired**: *"If pinning a behavior requires modifying production code (e.g. an
> internal seam is unreachable), STOP and escalate per CLAUDE.md §6 rather than changing `src/**`."*

---

## 1. What shipped

62 tests at the ADR-038 §2 security-auth KEEP path `tests/integration/auth/UnifiedAccessControl/`,
compiled into `Sprk.Bff.Api.Tests` via a new `<Compile Include>` glob. **Zero `src/**` changes.**

| File | Findings pinned |
|---|---|
| `OperationPolicyCharacterizationTests.cs` | A-3, A-20 |
| `AuthorizationServiceCharacterizationTests.cs` | A-2 |
| `AccessCacheCharacterizationTests.cs` | A-19 |
| `ExternalScopeCharacterizationTests.cs` | A-17 |
| `GrantLifecycleCharacterizationTests.cs` | A-11 |
| `EndpointAuthorizationCharacterizationTests.cs` | A-1, A-3, A-4, A-6 (+ 401 floor) |
| `ImpersonationFailClosedTests.cs` | fail-closed guard for FR-20 / task 036 |
| `ExternalCollaborationTestFixture.cs` | (fixture — see §4) |

### Coverage against Phase 0 findings

**9 of 20 fully pinned** · **1 partial** · **10 not reachable**

| Finding | Flipping task | Covered? |
|---|---|---|
| A-1 download unauthorized | 002 | ✅ |
| A-2 `userAccessToken: null` | 004 | ✅ |
| A-3 `"read"` unknown → 403 | 003 | ✅ |
| A-4 app-scoped capabilities | 006 | ✅ (200-with-payload) |
| A-6 bare `RequireAuthorization()` | 008 | ✅ (handler-entry proof) |
| A-11 grant non-idempotent | 010 | ✅ |
| A-17 self-join guard | 011 | ✅ |
| A-19 cache key omits auth mode | 014 | ✅ |
| A-20 Read-ceiling + always-deny ops | 003, 005 | ✅ |
| A-7 To Do PATCH scope | 009 | 🟡 fail-closed gate only — handler unreachable |
| A-5 expiry never read | 007 | ❌ |
| A-10 membership paging | 015 | ❌ |
| A-12 closure cascade `$select` | 016 | ❌ |
| A-13 SPE revoke matcher | 017 | ❌ |
| A-14 anonymous share links | 012 | ❌ |
| A-15 orphaned filter | 018 | ❌ (no behavior to pin) |
| A-16 uncapped `in`-clause | 018 | ❌ |
| A-18 email fallback no-hijack | 013 | ❌ |
| A-22 `["*"]` throws | 019 | ❌ |

---

## 2. Why the other 10 are not reachable

They divide into three causes. **None is a matter of effort** — each is a structural boundary.

### (a) The assertion would be about an OData/Graph wire format built inline — 6 findings

**A-5, A-10, A-12, A-13, A-14, A-18.**

Each defect lives in a query string or SDK call constructed *inside* the same method that performs the
I/O. Example — A-5, `ExternalParticipationService.cs:405-407`:

```csharp
var query = $"{apiUrl}/sprk_externalrecordaccesses" +
            $"?$filter=_sprk_contact_value eq {contactId} and statecode eq 0" +
            $"&$select=_sprk_project_value,...,sprk_accesslevel";   // no sprk_expiresdate anywhere
using var request = new HttpRequestMessage(HttpMethod.Get, query);
var response = await _httpClient.SendAsync(request, ct);
```

The service takes a raw `HttpClient` (ctor: `HttpClient, ITenantCache, IConfiguration, TokenCredential,
IHttpContextAccessor, ILogger`). There is no seam between "build the query" and "send it", so observing
the query requires intercepting the transport. That is **`Mock<HttpMessageHandler>` — ADR-038 §7 ban B1**
("transport-level mock; encodes wire format; breaks on refactors"). A hand-written `DelegatingHandler`
evades the letter of B1 but not its purpose: the assertion would still be *"the `$filter` string does not
contain `sprk_expiresdate`"*, which is wire format by definition.

A-13 and A-14 are the same shape one layer out — the defect is in how a Graph permission collection is
matched (`RevokeExternalAccessEndpoint.cs:222-235`) and how a sharing link is minted
(`FileAccessEndpoints.cs:640-642`).

### (b) The handler is unreachable without real Dataverse identity data — 2 findings

**A-7 (partial), A-22.**

A-7's defect is that `UpdateTodo` performs no record-scope check. Reaching `UpdateTodo` requires passing
`CallerPrincipalAuthorizationFilter`, which resolves an external `CallerPrincipal` from live participation
data. Offline the filter correctly fails closed (401 — verified, and pinned as a negative test), so the
handler never executes and the missing check cannot be observed.

A-22's exception originates in a Dataverse **metadata** fetch for entity `"*"`
(`MembershipResolverService.ResolveTransitiveAsync:957-978`), not in the pre-validation that a test can
reach.

### (c) There is no behavior to characterize — 2 findings

**A-15, A-16.**

A-15 is dead code: `AccessibleRecordSetAuthorizationFilter` is attached to no route, and the
`WorkforcePrincipal.HttpContextItemsKey` it reads is never written. Nothing observable changes either
way — this is a grep/code-review finding, correctly verified as such in pass 10, and task 018 should
delete it rather than test it. Asserting "no endpoint has this filter" would be a structural assertion
about DI/route wiring, close to ban B3.

A-16 needs an accessible set large enough to exceed Dataverse's `in`-clause bounds AND a live query to
observe the failure. Offline, `Tier2ScopeFilterInjector.Inject` happily emits any number of `<value>`
elements — the absent cap is visible by reading, not by running.

---

## 3. Options — owner decision needed

| Option | What it buys | Cost |
|---|---|---|
| **A. Real-tenant integration tests** (recommended for (a)) | Genuine coverage of all 6 wire-format findings, ADR-038-blessed (`tests/CLAUDE.md`: *"Real test tenants > in-memory emulators where the production code is exercising Dataverse-specific behavior"*) | Needs the environment prerequisites this project already tracks: E-2 `prvActOnBehalfOfAnotherUser`, E-3 Org-scoped app user, and a non-admin test user. Slower suite; needs credentials in CI |
| **B. Extract a query-builder seam per finding** | Cheap, fast, offline; each fix task gets a unit-testable target | Requires `src/**` changes — **forbidden by this task**, so it would move into each fix task (007/012/013/015/016/017) as a refactor-then-fix. Defensible: a fix task may legitimately extract a seam to test its own fix |
| **C. Accept read-only verification** | Zero cost | The 10 findings ship with no regression protection. For A-14 and A-18 (both **High**) this is the weakest option |
| **D. `DelegatingHandler` doubles** | Offline coverage of (a) now | Violates the intent of ADR-038 B1; would need a documented path-A exception per CLAUDE.md §6.5 |

**Recommendation**: **B for the six wire-format findings** — fold "extract the query-builder seam, then
test it" into each fix task (007, 012, 013, 015, 016, 017). Those tasks are already `FULL` rigor and
already modify the exact files involved, so the seam extraction costs almost nothing there and is
forbidden only *here*. Add **A** later for the end-to-end expiry/closure behavior once the UAT
environment exists. **C** for A-15 (delete it) and A-16 (a cap is a code-review assertion, not a test).

---

## ✅ OWNER DECISION — 2026-08-21: recommendation ACCEPTED

Path **B** for the six wire-format findings; path **C** for A-15 and A-16. Binding for this project.

**What each affected task now owes** (added to each POML as a `<constraint source="task-001">`):

| Task | Finding | Obligation |
|---|---|---|
| 007 | A-5 | Extract the grant-query builder from `ExternalParticipationService.QueryGrantSetAsync` (currently inline at `:405-407` before `_httpClient.SendAsync`) into an internal, pure, testable member. Assert the built `$filter`/`$select` includes `sprk_expiresdate` **and** that an expired grant confers no access |
| 012 | A-14 | Extract sharing-link option construction (`FileAccessEndpoints.cs:640-642`) so `scope`/`expiration` are assertable without Graph |
| 013 | A-18 | Extract the contact-by-email resolution decision from `IdentityNormalizationService:242-281` so the no-hijack `oid` check is assertable without Dataverse |
| 015 | A-10 | Extract `MembershipResolverService.BuildFetchXml` assertions: `<order>` present, and no row lost at a page boundary |
| 016 | A-12 | Extract the closure-cascade query builder (`ProjectClosureEndpoint.cs:181`); assert `_sprk_contact_value` and that org rows are included |
| 017 | A-13 | Extract the SPE permission matcher (`RevokeExternalAccessEndpoint.cs:222-235`) so email-vs-GUID matching is assertable without Graph |
| 018 | A-15, A-16 | **A-15: delete the orphaned filter, do not test it.** A-16: the cap is verified by code review, not a test — state so in the PR |

**Rules for the extraction** (applies to 007, 012, 013, 015, 016, 017):

- The seam must be a **pure** function of its inputs (no I/O), reachable via the existing
  `InternalsVisibleTo("Sprk.Bff.Api.Tests")`. Extract only — do **not** change behaviour in the same
  commit as the fix, so the characterization test can pin the pre-fix state first.
- Tests go at `tests/integration/auth/**` (the KEEP path task 001 backfilled), following the
  `Characterization_` + `FLIPPED BY` convention already established there.
- `Mock<HttpMessageHandler>` remains banned (ADR-038 B1). If a task concludes it cannot avoid transport
  mocking, that is a **§6.5 path A** escalation, not a silent exception.
- Each task states the extraction in its PR description as part of its Placement Justification (§10).

**This does not block Phase 0.** Tasks 002, 003, 004, 005, 006, 008, 010, 011, 014 all have their
baseline pinned and can proceed now.

---

## 4. Two things discovered while building this

### 4a. `tests/integration/auth/**` had zero compiled files

The KEEP path existed as a README placeholder only (*"Bulk move pending"*), and **no csproj globbed it** —
so a test placed there would have silently not run. Meanwhile the task POML specified
`tests/unit/Sprk.Bff.Api.Tests/AccessControl/`, which is **not** a KEEP path (only
`tests/unit/domain/**` is, per ADR-038 §2), and the POML's own ADR-038 constraint mislabels
`tests/unit/**` as one.

Resolved as **CLAUDE.md §6.5 path C (pivot to comply)**: authored at the KEEP path and added the
`<Compile Include="..\..\integration\auth\**\*.cs" LinkBase="AuthTests" />` glob, following the five
documented precedents in that csproj (contract CICD-084, regression W8-060, seam E-10, domain
agreements-r1/050, tenant nda-r1/052). `tests/integration/data-mutation/**` remains the last
un-backfilled KEEP category.

### 4b. The `/api/v1/external` group is not testable with the shared fixtures

`AuthPolicies.ExternalCollaboration` pins two named schemes — `AuthSchemes.Ciam` and
`JwtBearerDefaults.AuthenticationScheme` (`AuthorizationModule.cs:278-286`). Naming schemes bypasses the
default scheme, so `WorkspaceTestFixture`'s `FakeAuthHandler` is never consulted. The real `Ciam`
JwtBearer handler then builds its authority from `Ciam:Instance` / `Ciam:TenantId` — config no fixture
supplies — producing a malformed authority and an OIDC-metadata fetch that throws. **Observed: 500 where
production returns 401.**

This is a fixture-config gap, not a production defect (`bff-extensions.md` § F.2 Fixture-Config-FIRST —
inspecting fixture config before concluding is exactly what surfaced it). Fixed locally by
`ExternalCollaborationTestFixture`, which re-registers the policy against the fake scheme.

**Why it mattered beyond the one test**: before the fix, an authorization characterization asserting
*"not 403"* on this group **passed vacuously** — the request 500'd in the auth pipeline and never reached
the handler. Any future task testing `/api/v1/external` must use this fixture or reproduce the override,
or it will write tests that pass while proving nothing. All "not 403" assertions in this suite now carry
an explicit guard against that failure mode.

---

## 5. Register additions

- **A.1 addendum** — A-15 reclassified: dead code, delete-not-test (task 018).
- **G addendum** — `tests/integration/auth/README.md` inventory line ("25 KEEP-security-auth files…
  Bulk move pending") is stale; the path now has 62 compiled tests.
- **E addendum** — E-9: **`tests/integration/data-mutation/**` is still un-backfilled** (zero compiled
  files, not globbed by any csproj). Any task that adds a write-path test there must add the glob too, or
  the test will silently not run.
