# Task 081 — escalation fired, owner decided. **RESOLVED: option B.**

> **Status**: ⛔ blocked 2026-08-26 → ✅ **unblocked 2026-08-26.** Owner chose **option B (classify the
> caller)**. The POML has been rewritten to B; this file is the decision record.
> *(Filename retains `-BLOCKED` because other files link to it. It is no longer blocked.)*

> **⚠️ This file originally recommended option C. That recommendation is WITHDRAWN — see §"Why C was
> withdrawn".** The table below is preserved as the record of what was weighed; the recommendation line
> in it is superseded by §Decision.

---

## The defect is confirmed

`src/server/api/Sprk.Bff.Api/Endpoints/Diagnostics/TenantContainerResolverEndpoint.cs`
(`GET /api/diagnostics/tenant-container-resolver`, added by `customer-provisioning-orchestration-r1`
commit `9b2163011`, now on master):

```csharp
var tenantId = httpContext.Request.Query["tenantId"].FirstOrDefault();
if (string.IsNullOrWhiteSpace(tenantId))
    tenantId = httpContext.User?.FindFirst("tid")?.Value ?? ...;   // FALLBACK, not authority
...
result = await resolver.ResolveAsync(tenantId, cancellationToken);  // unchecked
```

Route carries `RequireAuthorization()` + rate limiting and nothing else. Any authenticated caller — a
normal Spaarke end user — can pass `?tenantId={someone-else}` and get that tenant's SPE container id.
Plus a **tenant-enumeration oracle**: the documented mapping is "tenant not served by this stamp → 400"
while a served tenant returns 200, so status code alone reveals which tenants this stamp hosts.

## Why the POML's originally-prescribed fix could not be applied as written

The fix "make the JWT `tid` the authority" is already implemented as a reusable component —
`Api/Filters/TenantAuthorizationFilter.cs`, which per its own summary enforces *"User's Azure AD 'tid'
claim must match the tenantId in the request"* and already reads tenantId **from query parameters**.
Attaching it is a one-line change and would satisfy CLAUDE.md §11 (reuse over new).

**It would break the caller.** `Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/SpeContainerResolverInvariantProbe.cs`:

- **:138** — *"ADR-028 UAMI outbound: probe uses the shared TokenCredential singleton (DefaultAzureCredential pinned to L2 UAMI)"* → an **app-only machine identity in the L2 control-plane tenant**.
- **:270-271** — sends `?tenantId={request.TenantId}` → the **customer** tenant.
- **:44** — its verdict *depends* on the endpoint echoing the requested tenant back.

So the probe is, by design, one tenant's identity asking about another tenant's resolution. `tid`-matching
denies it 100% of the time. Asking about a foreign tenant **is the legitimate operator capability here** —
the bug is not that the parameter exists, it is that *nothing distinguishes the operator from an end user.*

## 🔔 Owner decision — the three options as weighed

| Option | What it does | Cost |
|---|---|---|
| **A — Attach `TenantAuthorizationFilter`** | Closes the hole completely; reuses an existing component; one line | **Breaks the shipped L2 H13 I4 probe.** `customer-provisioning-orchestration-r1` would need a new mechanism |
| ✅ **B — Classify the caller (CHOSEN)** | Allow-listed app-only/operator identities may pass an arbitrary tenantId; user principals denied | Needs an inbound caller-kind primitive + an operator allow-list |
| ~~**C — Move the probe onto a named API key scheme**~~ | Require a named inbound key on this route; no user principal reaches it | ~~Recommended~~ **WITHDRAWN** — see below |

## Decision: **option B**, 2026-08-26

### Why C was withdrawn

Three things the original recommendation underweighted:

1. **C is not "reuse" from the caller's side.** It moves the L2 probe off a Managed Identity onto a
   **static shared key** — and the probe's own ADR-alignment header says it uses the shared
   `TokenCredential` singleton *"parity with sibling probes 173 (I2) and 174 (I3)"*. C reuses BFF
   machinery by **fragmenting the L2 probe fleet**. That trade was invisible in the original framing
   because it only looked at the BFF side.
   *(Inbound named API keys are sanctioned by ADR-028, so C was never an ADR violation — but it walks
   against the secret-free direction the platform has deliberately been taking.)*
2. **C destroys attribution.** An API key says "a holder of the key". A Managed Identity says *which
   principal* performed a cross-tenant read. On this route specifically, that is the log line you most
   want to have.
3. **B's cost was overstated.** "New security surface, needs design" implied something large. It is:
   read a caller-kind claim, plus a config allow-list of permitted app ids.

### What makes B durable rather than throwaway

The owner's condition was: *"if 081 is a quick win but then ends up having to be replaced or redone as
part of the evaluator then don't waste time."* Two separable pieces answer that:

- **The classification primitive is PERMANENT.** The unified evaluator cannot decide whether ADR-034
  membership derivation applies without knowing whether the caller is a service principal or a person.
  It needs this seam regardless of 081.
- **The policy is route-specific and upstream of the evaluator.** The evaluator answers
  `(recordId → rights)`; it never answers "which tenant may you ask about." Different axis — not work
  the evaluator later duplicates.

**⚠️ BINDING PLACEMENT CONSTRAINT.** The primitive MUST live in `src/server/shared/Spaarke.Core/Auth/`
(namespace `Spaarke.Core.Auth`), beside `AuthorizationService.cs`. `Spaarke.Core` **cannot** reference BFF
`Infrastructure/**` (one-way layering, enforced by `tests/Spaarke.ArchTests/LayerDependencyTests.cs`), so
a BFF-side primitive is unreachable by the evaluator and gets rebuilt. This is the exact trap that shrank
task 032's scope when `CallerRecordAccessProbe` landed BFF-side. Verified: `Spaarke.Core.csproj` is
`net10.0` and `ClaimsPrincipal` is BCL — **no new package reference required**.

### Two design rules that came with the decision

- **User principals are DENIED outright**, not `tid`-matched. A provisioning invariant diagnostic has no
  end-user use case; denying gets option C's "no user reach" property without C's credential downgrade.
  Stricter than the original POML prescribed — deliberately.
- **An empty OR absent allow-list denies everyone.** "Empty means allow all" is the classic failure of
  this pattern, and it fails open on a fresh environment where config has not been set yet.

## ⚠️ Corrections to the 2026-08-26 handoff — two claims were WRONG

Both were recorded in `current-task.md`'s START HERE block and are **false**. Verified by grep against
`src/server` on 2026-08-26:

| Claim as recorded | Reality |
|---|---|
| *"Zero reads of `idtyp`/`appid` as claims anywhere in `src/server`"* | **False.** `Infrastructure/Logging/AuditEnrichmentMiddleware.cs` reads `appid`/`azp` (:102-104) and `idtyp` (:132) |
| *"`Sprk.Bff.Api/CLAUDE.md` falsely claims `AuditEnrichmentMiddleware` enriches with `appid`"* | **False — that doc is correct.** It does. No doc fix needed |

**This changes the task, and for the better.** `AuditEnrichmentMiddleware.IsOnBehalfOfFlow` (:129-145)
already classifies caller kind — it is just a `private static` method inside a logging middleware, so it
is unreachable, and it answers a logging question rather than an authorization one. It has **no tests and
no other consumers**. So 081 is now *promote and extend one classifier* (CLAUDE.md §11 reuse) rather than
*write a new one* — and the acceptance criteria require that exactly ONE classifier exist afterwards.

## The trap that must not be shipped

`appid`/`azp` is **present in user-delegated tokens too** — it names the client application that
requested the token, not the caller's kind. So `if (allowedAppIds.Contains(appId))` lets a **human**
signed into the L2 app registration name an arbitrary tenant. The gate must be a **conjunction**: a
POSITIVE app-only determination AND `appid` ∈ allow-list. And "positive" is load-bearing — inferring
app-only from the *absence* of `scp`/`oid` classifies malformed tokens as service principals. Absence ⇒
indeterminate ⇒ deny.

`IsOnBehalfOfFlow`'s `hasDelegatedScope && hasUserOid` is fine for logging (two-valued, biased toward
"not OBO") and is **not** acceptable for authorization — that bias points the wrong way.

## Coordination

Option B's advantage over C is that the **L2 probe is untouched** — it keeps its Managed Identity and its
`?tenantId=` convention. `customer-provisioning-orchestration-r1` owns that file and has an active
worktree; under B, coordination is informational rather than blocking. If 081 finds itself needing to edit
the probe, the design has drifted back toward C — that is an escalation trigger in the POML.

The endpoint's doc comment also asserts *"missing/invalid JWT → 401 via standard auth middleware
(`RequireAuthorization`) — parity with all other BFF endpoints"*, which is true and beside the point:
`RequireAuthorization()` establishes THAT a caller is authenticated, never WHICH tenant's data they may
ask for. That sentence is corrected as part of the fix so it stops reading as an assurance.

## Interim risk

Unmitigated on master right now. Not a document-content disclosure — it leaks container ids and the
customer-tenant list. Rate-limited (`graph-read`) and requires a valid Spaarke token, so it is not
anonymous.
