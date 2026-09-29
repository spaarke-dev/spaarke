# Task 091 — nine `/api/spe` routes were registered outside the admin group

> **Completed** 2026-08-30 · FULL rigor · opus @ high
> **Files**: `Api/SpeAdmin/ContainerItemEndpoints.cs`, `Api/SpeAdminEndpoints.cs`,
> `Infrastructure/DI/EndpointMappingExtensions.cs`, `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`,
> `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`,
> **NEW** `tests/integration/auth/SpeAdmin/SpeAdminContainerItemRouteGateTests.cs`

---

## 1. The defect

`Api/SpeAdminEndpoints.cs:27-35` builds the `/api/spe` group with three things:

```csharp
app.MapGroup("/api/spe")
   .RequireAuthorization()             // authentication
   .AddSpeAdminAuthorizationFilter()   // layer 1 — is the caller an SPE admin at all?
   .AddSpeAdminTenantScopeFilter()     // layer 2 — whose data may that admin reach?
```

Eighteen child endpoint groups register **on that group** and inherit both filters.
`Infrastructure/DI/EndpointMappingExtensions.cs:380` registered `ContainerItemEndpoints` on the **root
app** instead, while the file spelled out absolute `/api/spe/...` paths. The routes therefore resolved
at admin URLs, looked like admin routes in every review, and carried neither authorization layer. Bare
`.RequireAuthorization()` means *authenticated*; `AuthorizationModule.cs:231` adds only named policies,
so there is no `DefaultPolicy`/`FallbackPolicy` raising that bar.

Nine routes, reachable by **any authenticated caller**, with the client-supplied `configId` unchecked
across tenants:

| Route | Effect |
|---|---|
| `GET …/items` | enumerate any container |
| `GET …/items/{itemId}/versions` | version history |
| `GET …/items/{itemId}/thumbnails` | content preview |
| `POST …/items/{itemId}/share` | **mint a sharing link** |
| `GET …/items/{itemId}/content` | **download the file** |
| `GET …/items/{itemId}/preview` | render the file |
| `DELETE …/items/{itemId}` | **destroy the file** |
| `POST …/folders` | create a folder |
| `POST …/items/upload` | **write bytes** |

The comment at the registration site explained the situation and drew the wrong conclusion:

> *"Registered separately because ContainerItemEndpoints maps absolute paths (not relative to the
> /api/spe group). Inherits auth via RequireAuthorization() called inside MapContainerItemEndpoints."*

Both sentences are true. Absolute paths were a reason to make them group-relative, not a reason to
bypass the group; and `RequireAuthorization()` supplies authentication, not the admin-role check or the
tenant scope. The file's own class docstring made the same slip — *"Follows ADR-008:
RequireAuthorization() applied to the route"* — which is how an ADR citation came to certify its own
violation. **Authentication is not authorization, and a remark asserting compliance is not compliance.**

## 2. Proven empirically before the fix

The POML required reachability be demonstrated, not read off the source. Two other instruments cannot
answer this question:

- The sibling `SpeAdminAuthorizationLayerTests` invokes the filter directly — right for "does the
  filter decide correctly", structurally unable to see that the filter was never attached.
- Endpoint reflection cannot see it either. As `RouteAuthorizationGuardTests` records,
  `AddEndpointFilter` appends to an internal filter-factory list compiled into the endpoint's
  `RequestDelegate` and contributes **nothing** to `EndpointBuilder.Metadata`; there is no
  `IEndpointFilterMetadata`.

So: real HTTP requests through the real pipeline. Written to assert the CORRECT behaviour (403) and run
before any fix — **9 failed / 11 passed**, and the failures carried the finding:

```
Expected 403 … but found HttpStatusCode.InternalServerError {value: 500}
Expected 403 … but found HttpStatusCode.BadRequest      {value: 400}
```

500 and 400 come **from inside the handler** — the request reached handler code and failed there
because the test host has no reachable Graph/Dataverse. Authorization never ran. In production, with
real infrastructure behind them, those handlers would have succeeded.

Meanwhile the control — `/api/spe/containertypes`, same caller, same fixture — **did** return 403.
That differential is what makes the result a finding rather than a broken fixture.

## 3. The fix

Nine routes converted to group-relative paths and registered on the group. URLs are byte-identical
(`/api/spe` + `/containers/{id}/items`), proven by the tests: they request the same absolute URLs and
now get **403, not 404** — a 404 would mean the route had moved.

Per-route `.RequireAuthorization()` and duplicate `.WithTags("SpeAdmin")` removed; both are inherited.

`MapContainerItemEndpoints` now takes **`RouteGroupBuilder`** rather than `IEndpointRouteBuilder`. This
is the load-bearing part: it makes the original defect a **compile error**, not a silent hole.

## 4. Verification

| Check | Result |
|---|---|
| Build | 0 warnings / 0 errors |
| New gate tests | **29 / 29** (9 non-admin 403 · 9 admin not-403 · 9 anonymous 401 · group control · census control) |
| Full BFF suite | **10,896 passed / 0 failed / 72 skipped** (was 10,876 — +20 mine) |
| ArchTests | 138 passed / **6 failed = the clean-tree baseline, proven by `git stash` at 137/6** (+1 = new Rule E) |
| Publish | **45.12 MB** compressed incl. PDBs — **0.00 MB delta**; ceiling 60 |
| CVE | `no vulnerable packages`; none added |

**Perturbations, both bit:**

1. Remove `.AddSpeAdminAuthorizationFilter()` from the group → **10 fail** (the nine + the control).
2. Attempt the original root-app registration → **compile error**:
   `cannot convert from 'WebApplication' to 'RouteGroupBuilder'`.

## 5. Escalation trigger did NOT fire

The POML's trigger: *stop if the SPE Admin client is used by anyone without the admin app role.*

`src/solutions/SpeAdminApp` is the only consumer of all nine routes, and it makes **64** `/spe/*` calls
including `containers`, `containertypes`, `configs`, `businessunits`, `environments` — all already
registered on the group with both filters. Its users must therefore already hold `Admin`/`SystemAdmin`,
or the app would be entirely broken today. Gating these nine makes nine outliers match the other 55
calls; it adds no new role requirement.

## 6. Census closed — and what the census got wrong

`ContainerItemEndpoints.cs` is now in `RouteAuthorizationGuardTests`' `GovernedFiles` under a new
**`Scope.GroupGated`**, with **Rule E** (`GroupGatedFilesRegisterNoAbsolutePaths`) asserting no route in
such a file declares an absolute `/api/...` path — the defect's signature.

A third scope was necessary rather than tidy. Classifying it `RouteLevelGate` would make Rule A demand
a per-route filter that correctly is not there, and per `tests/CLAUDE.md` a guard that flags the code it
protects gets deleted rather than obeyed. Leaving it unclassified was the other option — and that is the
blind spot this task closed.

**The count the census reported was wrong in an instructive way.** Task 083 filed this as *three routes*
because its instrument scanned for SPE **write sinks**. There are nine; the six read routes — including
file download and sharing-link minting — were invisible to a write-sink scan. *A tool finds what it was
built to look for, and the count it returns is not the size of the problem.*

## 7. Two findings this task did NOT fix

**(a) Pre-existing client/server route mismatches**, in
`src/solutions/SpeAdminApp/src/services/speApiClient.ts`:

1. `createSharingLink` posts to `…/items/{itemId}/sharing`; the server serves **`/share`** → 404.
2. `get` calls `GET …/items/{itemId}`; **no such server route exists** → 404.

Both have been dead since written and are unrelated to this change. They slightly *strengthen* the
safety of gating — nothing could have depended on them — but they are real defects and want their own
task.

**(b) An open modelling question, deliberately not resolved.** The three write sinks stay
`ClientSupplied`. This task closed the authorization half; it did not change provenance — an SPE admin
still names the container, because that IS the function of an admin tool, and record-less containers
legitimately exist (task 078 confirmed every shared BU/archive container). But the provenance guard
documents `ClientSupplied` as *"a work list that shrinks to zero, never exemptions"*, which may be
unreachable for an administrative surface. Options: (a) accept as permanently
client-supplied-by-design, or (b) add a distinct "administrative, gated by role + tenant scope"
provenance. **Owner decision required** — inventing an exemption inside a guard whose stated model
forbids exemptions is the quiet reclassification this project exists to stop.

## 8. The lesson worth carrying

`SpeAdminTenantScopeFilter`'s own doc comment predicted this class of failure:

> *"Fifteen endpoint files accept `configId`. A check written into each is a check that will be missed
> on the sixteenth — and the failure mode is silent cross-customer disclosure, which no test would
> notice unless it was written to look for it. **One filter on the group cannot be forgotten.**"*

Both halves were right. `ContainerItemEndpoints` is the sixteenth file, and the hole opened through a
third channel neither half covered: a file that **never joined the group**. A control that cannot be
forgotten can still be bypassed by not being applied — so the invariant worth enforcing is not "is the
filter correct" but "is every route actually behind it".

A smaller one, from this task's own test suite: the anonymous-caller control failed 10 tests on first
run because `HttpClient` **drops headers with empty values**, making "signed in with no roles" arrive
identically to "not signed in". The 401/403 distinction was untestable until a sentinel replaced the
empty string. Worth remembering whenever a test fixture encodes state in a header.
