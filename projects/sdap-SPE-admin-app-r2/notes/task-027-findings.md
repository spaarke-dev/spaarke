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
