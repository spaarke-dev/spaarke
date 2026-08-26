# Task 081 — BLOCKED at Step 0. Escalation trigger fired as designed.

> **Status**: blocked, awaiting owner decision · filed 2026-08-26
> The POML's own `<escalation><trigger>` fired: *"If the L2 probe genuinely cannot present a per-tenant
> token, STOP. The answer is then a scoped operator credential or moving the probe off a public route —
> NOT restoring caller-supplied tenant selection."* It cannot. This is that stop.

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

## Why the POML's prescribed fix cannot be applied as written

The fix "make the JWT `tid` the authority" is already implemented as a reusable component —
`Api/Filters/TenantAuthorizationFilter.cs`, which per its own summary enforces *"User's Azure AD 'tid'
claim must match the tenantId in the request"* and already reads tenantId **from query parameters**.
Attaching it is a one-line change and would satisfy CLAUDE.md §11 (reuse over new).

**It would break the caller.** `Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/SpeContainerResolverInvariantProbe.cs`:

- **:138** — *"ADR-028 UAMI outbound: probe uses the shared TokenCredential singleton (DefaultAzureCredential pinned to L2 UAMI)"* → an **app-only machine identity in the L2 control-plane tenant**.
- **:270-271** — sends `?tenantId={request.TenantId}` → the **customer** tenant.
- **:44** — its verdict *depends* on the endpoint echoing the requested tenant back: *"echoed tenantId matches request.TenantId ordinal (any mismatch means …)"*.

So the probe is, by design, one tenant's identity asking about another tenant's resolution. `tid`-matching
denies it 100% of the time. Asking about a foreign tenant **is the legitimate operator capability here** —
the bug is not that the parameter exists, it is that *nothing distinguishes the operator from an end user.*

There is **no inbound caller-classification mechanism** in the BFF auth infrastructure today (the
`GraphAppRoles` hits are *outbound* Graph permissions, not inbound classification). So the correct fix
needs a decision, not just an edit.

## 🔔 Owner decision required

| Option | What it does | Cost |
|---|---|---|
| **A — Attach `TenantAuthorizationFilter`** | Closes the hole completely; reuses an existing component; one line | **Breaks the shipped L2 H13 I4 probe.** `customer-provisioning-orchestration-r1` would need a new mechanism |
| **B — Classify the caller** | Allow-listed app-only/operator identities (`appid`/`oid`, or a required app role) may pass an arbitrary tenantId; user principals must match their own `tid` | Builds inbound caller classification that does not exist yet + an operator allow-list. **New security surface, needs design** |
| **C — Move the probe onto a named API key scheme (recommended)** | The BFF already has *"named API key schemes for inbound from trusted external systems (BuilderAdmin, Rag)"* with constant-time compare. The L2 control plane **is** such a system. Require that scheme on this route; no user principal reaches it at all | Touches the probe (another active project's code) → needs coordination. But it reuses existing machinery and closes the hole for end users entirely |

**Recommendation: C.** It is the only option that both preserves the operator capability and removes end-user
reach, and it reuses a mechanism the BFF already ships rather than inventing caller classification. A is a
clean security outcome that breaks a shipped probe; B is correct but invents new auth surface for one route.

**Coordination note**: whichever is chosen, `customer-provisioning-orchestration-r1` owns the probe and has
an **active worktree**. The endpoint's doc comment also asserts *"missing/invalid JWT → 401 via standard
auth middleware (`RequireAuthorization`) — parity with all other BFF endpoints"*, which is true and beside
the point: `RequireAuthorization()` establishes THAT a caller is authenticated, never WHICH tenant's data
they may ask for. That sentence should be corrected as part of the fix so it stops reading as an assurance.

## Interim risk

Unmitigated on master right now. Not a document-content disclosure — it leaks container ids and the
customer-tenant list. Rate-limited (`graph-read`) and requires a valid Spaarke token, so it is not
anonymous. No interim mitigation applied, because every candidate mitigation is one of the three options
above and picking one silently is the thing this escalation exists to prevent.
