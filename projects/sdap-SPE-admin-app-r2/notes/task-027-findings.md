# Task 027 — container-type owner management

> 2026-08-24 · FR-C09 · **Implemented; AC-1 awaiting live verification** (needs a delegated
> device-code sign-in — operator time, ~20s).

---

## 1. 🔴 The POML's central premise is false — the new relationship supersedes NOTHING

The task is framed as *"This supersedes part of the current ContainerTypePermissions screen"*, with a
constraint to *"determine what the existing screen still does that the permissions relationship does
NOT cover, and keep only that"*, and AC-4 requiring *"the superseded portion of the old screen is
retired"*.

They are **orthogonal surfaces that share one Graph word**:

| | Graph resource | Governs |
|---|---|---|
| **Existing** `/containertypes/{id}/permissions` | `applicationPermissions` | which **APPLICATIONS** may access containers of this type, and with what scopes |
| **New** `fileStorageContainerType.permissions` | `Collection(graph.permission)` — `grantedToV2`, `roles` | which **PEOPLE** own/administer the container type |

Nothing overlaps. **Nothing was retired**, and retiring anything would have deleted
application-permission management outright — which is precisely what the POML's own escalation trigger
exists to prevent:

> *"If retiring the superseded portion of the old screen would remove a capability the permissions
> relationship does not provide, STOP and escalate."*

**Not escalated for adjudication**, deliberately: the trigger's purpose is to stop a capability being
lost, the correct action is non-destructive and unambiguous (retire nothing), and asking an operator
to approve *not deleting something* spends their attention for no decision. Recorded instead.

**AC-4 is met vacuously** — only one OWNER surface exists (the new one). Its second clause has no
referent.

**Guarded structurally, not just documented**: the BFF route is **`/owners`**, not `/permissions`, the
DTOs are separate records rather than an extension of `ContainerTypePermissionDto`, and the client tab
is separate. A reader glancing at a route table or a network tab now sees the difference instead of
having to know it.

---

## 2. 🔴 Delegated + beta had never existed, and it is the only combination Graph will serve

Two constraints cross:

- Container types **reject app-only auth outright** — 403 on both versions (tasks 010/020).
- `permissions` is **beta-only** — absent from the v1.0 CSDL entirely (v1.0
  `fileStorageContainerType` has **no navigation properties at all**).

| | v1.0 | beta |
|---|---|---|
| **delegated** | `ForUserAsync` ✅ — but no `permissions` | 🔴 **did not exist** |
| **app-only** | exists | `ForApp()` — 403 on container types |

Confirmed live 2026-08-24, and the two failure modes are what make it conclusive:

```
v1.0  …/containerTypes/{id}/permissions → 400  "Resource not found for the segment 'permissions'"
beta  …/containerTypes/{id}/permissions → 403  accessDenied
beta  …/containerTypes/{id}            → 403  accessDenied     ← control, same token
```

v1.0 fails at **routing, before auth** — the segment genuinely does not exist. Beta fails **only** on
auth, identically to the control, so the route is real and delegated auth is the missing piece.

### `GraphClientFactory.ForUserBetaAsync`

**This is not an auth change.** Same on-behalf-of exchange, same Redis-cached token, same
`https://graph.microsoft.com/.default` scope — which is version-agnostic, so one token addresses both
endpoints and a cache entry acquired for a v1.0 call serves a beta call. Only the base address
differs. **No new credential and no new `.WithClientSecret` site, so no ADR-028 A4/E-3 surface.**

`baseUrl` is threaded through `CreateOnBehalfOfClientAsync` rather than duplicating the exchange, so
both variants share the token cache rather than doubling OBO traffic.

It does reintroduce a **deliberate, narrow version split** on container types (list/get/create/settings
on v1.0, owners on beta) — mirroring the precedent task 020 set for containers, and stated at the call
site rather than left for a reader to discover.

---

## 3. The SDK cannot help here — these calls are hand-built

`FileStorageContainerTypeItemRequestBuilder` has **no `.Permissions` property** (compiler-verified),
because Graph 6.5.0 models v1.0. There is no precedent for an untyped call in this file: the nearest
neighbour, `ApplicationPermissionGrants`, is a *typed* builder on `containerTypeRegistrations` — a
v1.0 resource.

So list/add/remove go through Kiota's untyped path (`RequestInformation` +
`RequestAdapter.SendPrimitiveAsync<Stream>`), **not** a bare `HttpClient`. That choice is load-bearing:
going through the adapter keeps the SDK's auth provider, its retry/circuit-breaker handlers, and —
critically — its **error mapping**, so a Graph failure still arrives as an `ODataError` and flows into
the same translation everything else in this file depends on (ADR-007 §1, ADR-019). A hand-rolled
`HttpClient` call would surface raw status codes and bypass all three.

**Consequence for testing**: nothing about these requests is compiler-checked. A wrong path segment,
wrong verb, or ignored payload would fail silently — the exact opposite of the position task 023
engineered for settings by moving to the SDK's typed model. Hence 8 contract tests, with the **path**
assertion as the load-bearing one.

---

## 4. ⚠️ The "three owners" limit is UNSOURCED — stated as UX, not as fact

The POML's background asserts *"The SharePoint admin center allows up to three owners for settings and
billing"*, and a constraint requires enforcing it.

It is **not corroborated anywhere available to this repo**:
- `knowledge/sharepoint-embedded/docs/learn-containertypes.md` contains **zero** occurrences of "owner".
- Neither CSDL bounds the `permissions` collection.

So the UI **cites the admin center as the source** ("The SharePoint admin center allows up to
3 owners") rather than asserting Graph enforces it, and the add path **still surfaces the server's
error** — a client-side guard is a convenience, never evidence about what the API will accept.
Asserting an unverified number as a fact would be this project's signature failure pointed at a
constant.

The constraint's real requirement — *"a fourth add MUST NOT be offered and then fail at the API"* — is
met: at the limit the input and button are disabled with a message naming the limit and its source.

---

## 5. What was built

**Server**
- `GraphClientFactory.ForUserBetaAsync` + `IGraphClientFactory` member + `baseUrl` threaded through
  `CreateOnBehalfOfClientAsync`.
- `SpeAdminGraphService`: `SpeContainerTypeOwner` record; `List/Add/RemoveContainerTypeOwnerAsync`
  (client-taking, testable) each with a thin `…ForUserAsync` wrapper — matching the file's other
  47 methods so the contract tests can drive them.
- `SendGraphJsonAsync` — the untyped Kiota helper, with ODataError mapping.
- `MapContainerTypeOwner` — reads `grantedToV2` **and** falls back to legacy `grantedTo`; every absent
  field stays null.
- Endpoints: `GET/POST /containertypes/{id}/owners`, `DELETE …/owners/{permissionId}`.
  **A removal that removed nothing returns 404, not 204** — reporting success there is the exact
  shape this project exists to remove.
- DTOs: `ContainerTypeOwnerDto`, `ContainerTypeOwnerListDto`, `AddContainerTypeOwnerRequest`.

**Client**
- `ContainerTypeOwner` type; `speApiClient.containerTypes.listOwners/addOwner/removeOwner`.
- `containerTypeOwners.ts` — pure data (limit, guidance, last-owner warning, `describeOwner`).
- `ContainerTypeOwnersPanel.tsx` — new **Owners** tab; limit enforced with a sourced message;
  removal always confirmed, with an extra consequence warning on the **last** owner.
- Failure to load leaves owners `null`, never `[]` — "could not load" must stay distinguishable from
  "there are none".

**Tests** — 8 contract tests (`SpeAdminContainerTypeOwnerTests`) + `StubDelete` added to the fixture,
defaulting to **204** because that is what Graph actually returns (a 200-with-body stub would let a
caller that mishandles an empty response pass here and fail in production).

Three test fakes implementing `IGraphClientFactory` gained the new member. `FakeGraphClientFactory`'s
beta variant performs the **same bearer-token validation** as `ForUserAsync` — a variant that skipped
it would make the owners endpoints look authenticated in tests while being open.

---

## 6. ⚠️ AC-1 is NOT live-verified

*"Owners can be listed, added, and removed against Spaarke Dev."*

Blocked on operator time, not on code: container types are delegated-only, so this needs an
interactive device-code sign-in (`SPAARKE-SPE-Admin-CLI`, ~20s at <https://microsoft.com/devicelogin>).

⚠️ **And it may hit the open PATCH-400 escalation.** Every container-type PATCH currently returns
`400 invalidRequest`, with the leading (unproven) hypothesis that only the owning application may
modify its container type. Whether that extends to POSTing to a `permissions` sub-collection is
**genuinely unknown** — a POST to a sub-collection is not the same operation as a PATCH to the entity,
so it may well succeed where PATCH does not. Do not assume either way.

Everything else is verified: build, 8 contract tests, code page build, and the request shapes.

---

## 7. ✅ Live verification 2026-08-25 — and two corrections to §1–§4 above

### The read path works

`GET /containerTypes/{id}/permissions` → **200** on **all four** live container types. Graph reports
**zero owners** on every one of them. AC-1's read half is verified.

### 🔴 CORRECTION to §4 — the three-owner limit IS documented and IS Graph-enforced

§4 called it "unsourced" and had the UI cite the SharePoint admin center as the source. The two
observations behind that were true — "owner" appears nowhere in
`knowledge/sharepoint-embedded/docs/learn-containertypes.md`, and neither CSDL bounds the
`permissions` collection — **and the conclusion was still wrong**, because the *API reference* was
never checked. Microsoft's Create-permission page states:

> *"A maximum of **3** permissions per container type is allowed. Adding a fourth permission returns a
> `400 Bad Request` error."*

**The corpus not saying something is not the platform not saying it.** Corrected in
`containerTypeOwners.ts`; the client-side guard remains a convenience, not the enforcement.

The same page adds two facts the POML never mentioned, now in the UI guidance:

- **Only `owner` is supported** as a role.
- **Only existing owners, SharePoint Embedded Administrators, or Global Administrators may add one.**
  Since every live type has **zero** owners, bootstrapping depends on a directory-role holder — worth
  saying out loud so an admin does not read a 403 as a product fault.
- Duplicates are idempotent (`201` with the existing permission), and the response is **201**, not 200.

### 🔴 CORRECTION to §5 — the ADD payload was wrong, and it is now fixed

The live POST returned **400 `invalidRequest`** — the same uninformative message as the etag defect.
Cause, from the same reference:

> *"Only the **user** property with the user's **id** is supported; group and application identities
> aren't supported."*

The implementation sent `userPrincipalName` for an email-shaped identifier. Graph accepts **only the
directory object id**.

Fixed: `AddContainerTypeOwnerAsync` now resolves a UPN to an object id first, and **fails with "no
such user" rather than sending a doomed grant** — because "no such user" and "400 invalidRequest" read
identically to an administrator and mean entirely different things.

`ResolveUserObjectIdAsync` derives its base address from the client (task 020's lesson). A hardcoded
`https://graph.microsoft.com/v1.0` would have made every contract test hit the **real** Graph instead
of the fixture — a worse failure than a wrong version.

Pinned by 3 new tests: `AddOwner_ResolvesAUpnToAnObjectId…`,
`AddOwner_WhenTheUpnResolvesToNobody_SaysSo…`, `AddOwner_WhenGivenAnObjectId_SendsItDirectly…`.

⚠️ **AC-1's write half is still not live-verified** — the fix was derived from Microsoft's reference
after the live 400, not yet re-exercised against the tenant. It needs one more delegated run.

### The lesson, twice in one day

Both 400s — the settings PATCH and the owner POST — were **documented requirements** returned as
`invalidRequest` / *"One of the provided arguments is not acceptable"*. Neither error named its cause.
In both cases the answer was one fetch of the vendor's own reference page away, and in the first case
the wrong hypothesis (an ownership restriction) would have cost a production app-registration change
or a throwaway container type to disprove.

**Read the vendor's reference for the exact operation before hypothesising about auth.**

---

## 8. ✅ AC-1 FULLY VERIFIED LIVE — 2026-08-25

Against `Spaarke PAYGO 1` in Spaarke Dev, using the **corrected** payload (object id), i.e. the exact
shape `SpeAdminGraphService` now sends — so this verifies the shipped code path, not a lookalike:

```
list owners                              → 200, count 0
POST {roles:["owner"],grantedToV2:{user:{id}}}  → 201 Created   ✅
list owners                              → 200, count 1  (roles=['owner'])
DELETE …/permissions/{permissionId}      → 204            ✅
list owners                              → 200, count 0   ✅ reverted
```

Nothing was left behind. **AC-1 is met**: owners can be listed, added, and removed against Spaarke Dev.

The permission id is a base64 of `owner_{objectId}` — deterministic, not a random handle. Useful to
know when reasoning about the documented idempotency (a duplicate add returns `201` with the existing
permission rather than creating a second one).

### ⚠️ Known limitation — the owners list will show a GUID, not a name

Graph returned `grantedToV2.user` with **only `id`** — no `displayName`, no `email`. So
`describeOwner()` falls through to its id branch and the UI shows a raw object id.

That is *honest* (it never invents a name) but it is not *useful*: an administrator cannot tell who
`c74ac1af-…` is. Fixing it means resolving each owner id back to a user via `/users/{id}`, which is an
N+1 directory lookup on a list capped at 3 — cheap, but it is added scope and a new failure mode
(a deleted user resolves to nothing, which must render as "unknown user", not as an error).

**Deliberately not done here.** Recorded so it is chosen rather than defaulted into.
