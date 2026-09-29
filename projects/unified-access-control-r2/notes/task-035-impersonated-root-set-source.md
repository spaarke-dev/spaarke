# Task 035 — `ImpersonatedRootSetSource` (FR-20)

> Rigor FULL · `opus` @ `high` · directional steps · dep 031 ✅ (see §0)
> 2026-09-09. Builds the source; **task 036 owns the swap and the canary gate.**

---

## 0. Dependency annotation was stale — the third status home again

This POML declared `<dependency task="031" status="pending">`. **031 is completed** — both its own
POML and the index say so. Corrected in place, with a warning attached.

`scripts/check-task-status-drift.ps1` reconciles `<status>` against the index and **does not look at
`<dependency status>` attributes at all**, so this third copy drifts silently and has now been caught
twice (061 had two stale ones, this is a third). **Never trust a `<dependency status>`; re-derive from
the dependency's own POML.**

---

## 1. The finding that shrank the task

The POML's step 1 asks whether a `$select`-only overload on `DataverseWebApiService` is needed,
preferring **zero** changes to `Spaarke.Dataverse`. The answer is stronger than "no overload needed":

🔴 **`IImpersonatedCommunicationQuery` already IS the seam this task needs.**

```csharp
Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
    string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct);
```

That contract is **entirely generic**. It is communication-specific in its *name and namespace only*.
It wraps `RetrieveMultipleImpersonatedAsync` (the live primitive), and `CommunicationModule` registers
it **UNCONDITIONALLY** (ADR-032 null-object kill-switch discipline), so an authorization path
depending on it cannot be broken by a feature flag.

**Consequences:**

| POML expectation | Actual |
|---|---|
| Possibly add an overload to `DataverseWebApiService` | **Zero** changes to `Spaarke.Dataverse` |
| "One interface as the testing seam (mirror `IImpersonatedCommunicationQuery`'s precedent)" | **Do not mirror it — USE it.** A second identical interface is the duplication CLAUDE.md §11 exists to prevent |
| — | One seam IS added: `IImpersonatedRootSetSource`, which is a *different* seam — the swap point task 036 flips |

**The naming mismatch is real and is NOT fixed here.** An authorization class importing
`Sprk.Bff.Api.Services.Communication` is a cohesion smell. The right fix is to hoist that interface to
a neutral name/namespace — but that touches `CommunicationModule` (a contended DI file) plus four
communication services, which is a cross-module refactor, not a drive-by on an authorization path
mid-task. **Filed as a follow-up** rather than done silently or ignored silently.

---

## 2. What "fail closed" means here — and why it is not the obvious thing

Worth stating explicitly, because the obvious reading produces the wrong code.

**App-only is the silent default of the entire Dataverse client.** So the dangerous failure on this
path is *not* an exception. It is:

1. **Quietly issuing an app-only query** — which returns every record in the org. A missing
   impersonation parameter does this, and nothing about it looks like an error.
2. **Swallowing a fault into an empty set** — which reads as "this user can see nothing".

These are wrong in *opposite* directions and **both are silent**. An empty set is also a perfectly
valid answer, which is exactly why a fault must never be converted into one: the caller cannot tell
the two apart, and getting it wrong denies a user everything with no signal.

Design consequences:

- **No `Guid.Empty` guard in this class.** Deliberate. The primitive already refuses it, and that
  refusal must be the single point of truth. A second guard can drift from the first, and on this path
  drift toward leniency *is* an app-only query nobody intended. The refusal propagates; nothing
  catches it.
- **`QueryAsync` has no `try`/`catch` at all.** A non-success status throws inside the primitive and
  escapes.
- **Truncation is measured on rows returned, not distinct ids.** A capped page containing duplicates
  would collapse below the cap and report itself complete. `RootIdSet.Truncated` is consumed by 036
  for NFR-03; a caller treating a truncated set as complete **under-grants invisibly** — the affected
  user simply never sees the record and has no way to learn it exists.

---

## 3. Caching — fail-open on the CACHE, never on the ANSWER

Uses `ITenantCache.GetAsync`/`SetAsync` with an explicit `catch`, **not `GetOrCreateAsync`**.
`GetOrCreateAsync` would surface a Redis outage as the caller's error — turning a cache problem into a
failed authorization read. `MembershipResolverService` established exactly this pattern for exactly
this reason; copied, not reinvented.

`OperationCanceledException` is rethrown rather than swallowed: a cancelled request is not a cache
fault, and absorbing it would convert a caller's cancellation into a full uncached Dataverse round
trip.

Key shape follows the membership precedent —
`tenant:{tenantId}:impersonated-root-set:{systemUserId:D}:{entityType}:v{n}`. **The security property
is `systemUserId`**, which is always present; the tenant segment is a namespace, not the isolator.
Investigation 08 §3a-3 names the failure this prevents: a key omitting `systemUserId` serves one
user's accessible set to another — a cross-user disclosure produced entirely by a cache key.
`CacheVersion` exists so a change in *query shape* orphans old entries rather than serving an
authorization answer derived from a query nobody ran.

---

## 4. Live-metadata verification

Entity sets and id columns confirmed live 2026-09-09 (`EntityDefinitions?$select=LogicalName,
EntitySetName,PrimaryIdAttribute`), not derived from a pluralization convention:

| Root | Entity set | Id column |
|---|---|---|
| `sprk_project` | `sprk_projects` | `sprk_projectid` |
| `sprk_matter` | `sprk_matters` | `sprk_matterid` |
| `sprk_workassignment` | `sprk_workassignments` | `sprk_workassignmentid` |

Checking these was not ceremony: on these **same three entities**, two of the three primary *name*
attributes are non-obvious (`sprk_matter` has no `sprk_name`; `sprk_workassignment` has no
`sprk_workassignmentname` — task 029 §0.3), so the id columns were verified rather than assumed.

`sprk_servicerequest` is deliberately absent, and the `ArgumentException` message says so: it is core
but never externally grantable (owner decision 2026-09-09, task 028).

---

## 5. Perturbation results (mandatory)

Each applied alone, built, tests run, reverted. **16 green before and after.**

| # | Perturbation | Tests failed |
|---|---|---|
| 1 | Drop the id-only `$select` — query returns every column | **3** |
| 2 | Truncation measured on distinct ids instead of rows | **1** |
| 3 | Cache key omits `systemUserId` (investigation 08 §3a-3 cross-user leak) | **1** |
| 4 | Swallow a Dataverse fault into an empty set | **2** |
| 5 | Unsupported entity type resolves to a default binding instead of throwing | **3** |
| 6 | Cache read fault propagates instead of falling through to a live query | **1** |

⚠️ **P6 did not compile as first written.** Replacing the catch body with a bare `throw;` left `ex`
unused → `CS0168` → error under `--warnaserror`. Reshaped to keep the `LogWarning` (so `ex` stays
used) and replace only the `return null;`. This is the **third** time in this session that a
perturbation had to be reshaped to compile (029's (b), 062's P-series, now this). **The compiler
enforcing part of a contract is worth knowing and is NOT test coverage** — reshape, then count.

---

## 6. §11 component justification

| Question | Answer |
|---|---|
| **Existing** | `DataverseWebApiService.RetrieveMultipleImpersonatedAsync` (the primitive, live); `IImpersonatedCommunicationQuery` (the seam over it — **reused, not duplicated**); `MembershipResolverService` (the cache-key + TTL template, copied); `ITenantCache` (the cache). |
| **Extension** | `MembershipResolverService` was **not** extended — investigation 08 §4 watch-item 1: it is pattern-matching, not Dataverse's answer, and it serves non-auth consumers (briefing, todo, ACS reconcile) that need `byRole`. Entangling an authorization answer with an AI-scoping service is what the spec's Placement table row 1 forbids. |
| **New surface** | Two types in one file: `IImpersonatedRootSetSource` (the 036 swap seam) + `ImpersonatedRootSetSource`, plus the `RootIdSet` result struct. **No** new query interface, **no** package, **no** change to `Spaarke.Dataverse`. |
| **Cost of doing nothing** | Type 1 SPA access keeps disagreeing with the MDA in both directions — BU-matched records leak past role depth, and POA-shared records stay invisible. |

---

## 7. Registered but INERT — deliberately

`ExternalAccessModule` registers `IImpersonatedRootSetSource`, and **nothing consumes it**.
`AccessibleRecordSetService` is **byte-unchanged** (verified: `git diff --stat` is empty), which is
acceptance criterion 7.

That is the point of the 035/036 split: this task makes the source *exist and be provable*; 036 makes
it *live*, behind a flag, gated on the NFR-04 negative canary (task 034).

🔴 **A warning for whoever picks up 036.** Task 062 proved on 2026-09-09 that in `spaarkedev1` a user
with **zero directly-assigned roles reads every project including the secure one**, because the root
BU's default owner team holds `System Administrator`. In that environment the impersonated answer and
the app-only answer will **agree — because everyone is effectively an administrator**. NFR-04's canary
requires the impersonated read to return a **strictly smaller** set; equality means impersonation is
inert and must fail the build. So 036 flipped in the current environment would produce a confidently
wrong "it works". **Fix the BU exposure before trusting 036's gate.**

---

## 8. Follow-up filed

Hoist `IImpersonatedCommunicationQuery` to a neutral name/namespace (e.g.
`IImpersonatedDataverseQuery` under a shared Infrastructure namespace) so the authorization path does
not import a communication-named type. Mechanical, but it touches `CommunicationModule` + four
services — deliberately not bundled into an authorization-path task.
