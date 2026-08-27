# Task 081 — caller classification: Step 0 claim evidence + perturbation record

> **Status**: ✅ implemented 2026-08-26. Option B (classify the caller) as decided in
> [`task-081-tenant-diagnostic-BLOCKED.md`](task-081-tenant-diagnostic-BLOCKED.md).
> Commit `15b5dc6a3`.

---

## 1. Step 0 — which claim positively distinguishes app-only from user-delegated?

The POML made this the gating question, with an escalation trigger if no POSITIVE discriminator
exists in the tokens this BFF actually receives. **A positive discriminator does exist. The trigger did
not fire.** The evidence, and its limits, are below.

### What the L2 probe actually presents

`SpeContainerResolverInvariantProbe.cs:280-286` acquires its token as:

```csharp
var scope = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/.default";
token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
```

`_credential` is the shared `TokenCredential` singleton — `DefaultAzureCredential` pinned to the L2
UAMI (per the file's own ADR-alignment header, :138-140). A managed identity requesting a resource's
`/.default` scope is the **client-credentials flow**, so the BFF receives an **app-only token**.

### What the BFF validates

`AuthorizationModule.cs:47-49` — `AddMicrosoftIdentityWebApi`, which validates issuer / audience /
lifetime / signing key. The route carries a bare `RequireAuthorization()`, whose default policy is
`RequireAuthenticatedUser()`. **Nothing in that chain looks at caller kind.** That is the defect.

### Claim-by-claim finding

| Claim | Available? | Verdict as a discriminator |
|---|---|---|
| `idtyp` | ⚠️ **Expected ABSENT** | Entra's most direct signal (`idtyp=app`), but it is an **optional claim** configured on the resource app registration. A repo-wide grep for `optionalClaims` / `idtyp` found **no configuration anywhere** — `scripts/Register-EntraAppRegistrations.ps1` does not set it, and the only `idtyp` occurrence in `src/server` was the middleware read this task promoted. **Honoured when present; never relied upon.** |
| `sub` == `oid` | ✅ **Always available** | In an Entra app-only token both are the calling service principal's object id. In a user token `sub` is a pairwise subject identifier scoped to (user, application) and never equals the user's `oid`. Both are **core claims** (always emitted), so unlike `idtyp` this needs no tenant configuration. **This is the signal that actually fires for the L2 probe.** |
| `scp` | ✅ | Positive **user-side** signal. Checked FIRST, so nothing carrying a delegated scope can reach an application branch. |
| `appid` / `azp` | ✅ but ✗ as a discriminator | **THE TRAP.** Present in user-delegated tokens too — it names the client application, not the caller kind. Used only as the allow-list KEY, never as evidence of kind. |
| `roles` | ✅ but ✗ | Deliberately **not** used. `roles` appears in user tokens as the signed-in user's app-role assignments, so "has roles" ≠ app-only. The usual "`roles` present AND `scp` absent" formulation smuggles in an absence test, which is exactly what the design refuses. |

### The resulting precedence (fail-closed at every step)

```
1. not authenticated                      → Indeterminate
2. scp present  → with oid                → UserDelegated      (positive user signal, checked FIRST)
                → without oid             → Indeterminate      (unmodelled shape; deny beats guess)
3. idtyp == "user"                        → UserDelegated
4. idtyp == "app"                         → Application
5. sub == oid, both non-blank             → Application        (the signal that fires in practice)
6. otherwise                              → Indeterminate
```

Step 2 running before steps 4–5 is the structural guarantee behind the trap: no token carrying a
delegated scope can be classified `Application`, whatever its `appid` says.

### ⚠️ What this evidence does NOT establish

**No live token was decoded.** This session has no access to a deployed environment or a real L2 UAMI
token, so the claim set above is derived from the probe's token-acquisition path plus documented Entra
behaviour — not from an observed JWT. The specific residual risk: if a real L2 UAMI token were to omit
`oid`/`sub` or emit them unequal, the classifier returns `Indeterminate` and the probe gets **403**.
That fails **closed** (the I4 probe reports InfraFault, which is its documented handling for 401/403 —
`SpeContainerResolverInvariantProbe.cs:341-347`), so the failure mode is a visible operator signal, not
a silent hole. **Recommended before/at first live H13 run: decode one real probe token and confirm
`sub == oid`.** If it does not hold, the fix is to configure `idtyp` as an optional claim on the BFF app
registration — which needs `customer-provisioning-orchestration-r1`'s agreement, and is precisely the
escalation the POML anticipated.

---

## 2. Why the L2 probe did not need to be touched

Option B's advantage over option C. The probe keeps its Managed Identity and its `?tenantId=`
convention; the endpoint keeps echoing the requested tenant. The only new requirement is deployment
configuration: the L2 UAMI's app id must appear in
`Diagnostics:TenantContainerResolver:AllowedOperatorAppIds` on each customer BFF.

**⚠️ Operator action required — this is a deployment prerequisite, not a code change.** Until that key
is set, the endpoint answers 403 to everyone and I4 reports InfraFault. That is deliberate: an absent
allow-list must not fail open on a freshly provisioned environment. It needs to be added to the H9/H13
provisioning path (owner: `customer-provisioning-orchestration-r1`).

---

## 3. Perturbation record — proof each guard is load-bearing

Every guard was neutralized **individually**, rebuilt, and re-run. A guard whose removal leaves the
suite green is not a guard. Build result was read before every test result (stale-assembly trap).

| # | Perturbation | Expected RED | Observed |
|---|---|---|---|
| **P1** | Gate drops `caller.IsApplication`, keeps `appid` matching | the trap test | ✅ 4 RED — incl. `TheTrap_UserDelegatedCallerWhoseAppIdIsAllowListed_IsDenied` |
| **P2/P3** | Empty **or** absent allow-list means "allow all" | both allow-list tests | ✅ 3 RED — `EmptyAllowList_*`, `AbsentAllowList_*`, `EveryDenialReason_*` |
| **P4** | Resolver called before denial (gate no longer precedes it) | every "never reach the resolver" assertion | ✅ 9 RED — incl. `DeniedCallers_CannotDistinguishAServedTenantFromAnUnservedOne` |
| **P5** | Reintroduce the JWT `tid` fallback | the no-fallback test | ✅ 1 RED — `NoTenantIdInQuery_Returns400_WithNoResolverCall_AndNoTidFallback` |
| **P6** | Drop the non-blank guards on `sub == oid` (so `null == null` promotes) | the blank-claims tests | ✅ 2 RED — `BlankSubAndBlankOid_*`, `TokenWithNoDeterminativeClaims_*` |
| **P7** | Check `idtyp=app` BEFORE the delegated-scope branch | the contradictory-token test | ✅ 1 RED — `ContradictoryToken_IdtypAppWithDelegatedScope_*` |
| **P8** | Swap `ObjectId` ↔ `TenantId` in the middleware's scope dictionary | the middleware scope tests | ✅ 5 RED — added *because* code review found the middleware rewiring untestable-as-shipped (see §6) |

All perturbations were reverted; the working tree was confirmed identical to the committed state.

### The fixture defect this also fixed

The pre-existing `FakeResolver` returned one canned result for **any** `tenantId`. A handler that
resolved the WRONG tenant would still have produced a green test — the double encoded what the call was
*for*, not what it *did*. It now **throws on a tenant it was not explicitly configured for**, so a
wrong-tenant resolution is a loud failure rather than a silent pass. (Same failure shape as the two
vacuous passes in this project's previous batch.)

---

## 4. Verification results

| Check | Result |
|---|---|
| `Spaarke.Core.csproj` | **unchanged** — no `PackageReference`, no `FrameworkReference`. `ClaimsPrincipal` is BCL. Escalation trigger #2 did not fire. |
| Build | BFF + both test projects + ArchTests: **0 errors, 0 warnings** |
| `Spaarke.Core.Tests` | **61/61 pass** (16 new) |
| `Sprk.Bff.Api.Tests` | **11183 pass / 82 skipped** (19 in this file, 12 new) |
| `Spaarke.ArchTests` | 105 pass / 9 fail — **identical to the baseline on parent commit `2b3b07de2`**, verified by checking out the parent and re-running. Zero new failures. `LayerDependencyTests` and `RouteAuthorizationGuardTests` (census, 111) are in the passing set in both runs. |
| Publish size | **45.09 MB compressed incl. PDBs** (44.17 excl.) vs 44.96 MB baseline → **+0.13 MB**. Ceiling 60 MB. Note the baseline predates 285 merged master commits, so this delta is not attributable to this task alone. |
| CVE | `Sprk.Bff.Api` and `Spaarke.Core`: **no vulnerable packages** (`--include-transitive`) |
| One classifier | `grep` finds **zero** `FindFirst("idtyp"/"appid"/"azp")` in `src/server` outside `CallerIdentity.cs`. The three carve-outs (`GraphClientFactory.cs:238`, `AuthorizationModule.cs:160`, `MiddlewarePipelineExtensions.cs:114-115`) read `jwt.Claims` off a **decoded token** for logging, not an inbound `ClaimsPrincipal` — left alone as the criterion requires. |

---

## 6. Step 9.5 quality gates

**`code-review`: PASS** — 0 Critical, 2 Warnings, 3 Suggestions.

- **W-1 (FIXED in-task)**: `AuditEnrichmentMiddleware` was rewired onto the new classifier and its four
  private readers deleted, but the file **had no tests** — parity was argued in a comment and proved by
  nothing. A swapped property (`ObjectId` ↔ `TenantId`) would compile, pass all 11,183 tests, and
  silently corrupt the audit trail customers pipe into Sentinel. Closed by adding
  `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Logging/AuditEnrichmentMiddlewareTests.cs` (8 tests
  pinning all five scope fields incl. both claim-name forms and the `tenant_id` fallback), then
  perturbation **P8** confirmed the swap is caught.
- **W-2 (accepted, documented)**: `HandleAsync` now carries 5 concerns and ADR-008 favours endpoint
  filters. Kept inline deliberately — a filter would be a NEW component for ONE route (§11), and a
  `*AuthorizationFilter` would fall under `RouteAuthorizationGuardTests` **Rule B**, which requires
  consulting an authorization *decision service* that this config-driven caller-kind gate does not use.
- Suggestions (all accepted as-is with rationale): redundant `ThrowIfNull` on non-nullable
  `IConfiguration` (matches the 3 sibling guards); per-request `HashSet` allocation (preserves
  config-reload semantics); the `Anonymous` caller-shape test exercises a path middleware normally
  prevents (defense-in-depth, not transport proof).

**`adr-check`: PASS** — 0 Violations, 2 Warnings, 8 ADRs compliant.

- **A-1 ADR-003** "MUST implement new auth logic as `IAuthorizationRule`" — not followed, because
  `IAuthorizationRule` is **record-scoped** (`AuthorizationContext` + `AccessSnapshot`) and this decision
  has no record in it. Already covered by this project's documented **path B** ADR-003 tension (task 030).
  Both MUST NOTs are satisfied: `CallerIdentity` is not a service layer (no DI, no interface, no deps),
  and no authorization decision is cached. No new exception required.
- **A-2 ADR-008** — scope is *resource-based* authorization; its MUST NOTs target global middleware and
  pre-routing checks. Neither is violated. Recorded rather than silently passed.

---

## 7. Placement Justification (CLAUDE.md §10 / §11)

**Existing**: `AuditEnrichmentMiddleware.IsOnBehalfOfFlow` (:129-145) already classified caller kind and
`ResolveAppId` (:102-104) already read `appid`/`azp` — both `private static` inside a logging middleware,
unreachable by any authorization path, answering a *logging* question. `TenantAuthorizationFilter`
already enforces tenant-claim matching but cannot express "operators may name a tenant".

**Extension**: those readers were PROMOTED into `Spaarke.Core.Auth.CallerIdentity` and the middleware now
consumes it; its four private helpers are deleted. Net-new *logic* is the three-valued result and the
allow-list policy. The alternative — a second claim reader in the BFF — is what §11 exists to prevent.

**Placement**: the primitive is in `Spaarke.Core/Auth/`, **not** BFF `Infrastructure/Auth/`.
`Spaarke.Core` cannot reference BFF `Infrastructure/**` (one-way layering, `LayerDependencyTests`), so a
BFF-side primitive would be unreachable by the unified evaluator in
`Spaarke.Core/Auth/AuthorizationService.cs` and would be rebuilt later — the trap that shrank task 032's
scope when `CallerRecordAccessProbe` landed BFF-side. The **policy** (which app ids may name a tenant on
this route) correctly stays BFF-side: it is route-specific and upstream of the evaluator, which answers
`(recordId → rights)` and never "which tenant may you ask about".

**Cost-of-doing-nothing**: any authenticated user of any tenant could enumerate which tenants this stamp
serves and resolve their SPE container ids. On a multi-tenant platform that is customer-list disclosure.
It was live on master.
