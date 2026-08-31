# Task 004 — making `AuthorizationService` caller-scoped: the design and why

> **Date**: 2026-08-21 · **Spec**: FR-02 + success criterion 2 · **Finding**: A-2 (High)
> Call-site classification (POML Step 1) is in
> [`task-004-callsite-classification.md`](task-004-callsite-classification.md).

---

## 1. The constraint that decided the design

`AuthorizationService` lives in **`Spaarke.Core`**, which has **no ASP.NET Core dependency** — its
`.csproj` references only `Microsoft.Extensions.*.Abstractions` plus `Spaarke.Dataverse`. The caller's
bearer token lives in `HttpContext`, which is a BFF concern.

That rules out the two obvious approaches:

| Rejected approach | Why |
|---|---|
| Inject `IHttpContextAccessor` into `AuthorizationService` | Adds a **web dependency to a non-web shared library**. `tests/Spaarke.ArchTests/LayerDependencyTests.cs` guards the shared-lib boundary; this would erode it for one feature's convenience |
| Add a second, OBO-aware authorization service | Violates **ADR-010** ("MUST NOT introduce a new authorization service layer") and recreates the two-evaluator split Phase 1 exists to *remove* |

**Chosen**: carry the token on the existing `AuthorizationContext`. That extends the existing seam,
which is exactly what ADR-010 asks for, and keeps `Spaarke.Core` web-free.

```csharp
public required string? UserAccessToken { get; init; }
```

## 2. Why `required` on a *nullable* property

This looks contradictory and is the deliberate core of the fix.

`required` forces **every construction site to state its intent**. It does not force a non-null value —
it forces an explicit decision. A caller that genuinely wants app-only evaluation must write
`UserAccessToken = null` in the initializer, where a reviewer sees it.

That directly attacks how A-2 survived: the old signature had `userAccessToken: null` as a *default*,
so app-only was what you got by not thinking about it. Under `required`, forgetting is a **compile
error**, not a silent security downgrade. The refactor produced exactly that — 7 compile errors across
6 production and 5 test construction sites, each of which had to declare itself.

This also **subsumes POML Step 3**. That step asked for app-only call-sites to be routed through "an
explicitly-named app-only entry point". Step 3 turned out to be **vacuous** — the classification found
zero app-only consumers, so there was nothing to route. `required` delivers the same guarantee the step
was reaching for (app-only must be declared, never defaulted) without adding a second public entry
point that no current caller needs. Recorded here because Step mode for this task is **prescriptive**:
Step 3 was not skipped by preference, it had no applicable work.

## 3. Fail-closed behaviour

```
UserAccessToken null/empty/whitespace
  → DENY, ReasonCode = "sdap.access.deny.no_caller_token"
  → the data source is NOT consulted at all
```

The data source is deliberately never reached. That is stronger than "pass null and let it deny",
because passing null *is* the app-only evaluation A-2 describes — on the SPA/Teams surface app-only
always answers yes, since reads there are app-only and Dataverse row-level security is inert.

A distinct reason code matters: a caller-scoped denial attributable to a **missing credential** must be
distinguishable from `insufficient_rights` (the caller genuinely lacks access) and from
`unknown_operation` (task 003's failure mode). Collapsing them would make the next A-2-class defect
invisible in telemetry.

## 4. `TokenHelper.ExtractBearerTokenOrNull` — why a new method

`TokenHelper.ExtractBearerToken` **throws** `UnauthorizedAccessException` on a missing or malformed
header. Calling it from an authorization filter would turn a missing credential into a **500** via the
global exception handler — a server error where the correct answer is a fail-closed deny.

§11 three-question gate:

- **Existing**: `TokenHelper.ExtractBearerToken` (same file) — throws by contract, and OBO downstream
  callers depend on that.
- **Extension**: yes, a non-throwing sibling in the same class rather than a new type. The throwing
  overload is unchanged, so no existing caller is affected.
- **Cost of doing nothing**: every one of the six authorization call-sites would need its own
  `try/catch` around token extraction, and any that forgot would convert a missing credential into a
  500 instead of a deny. Concrete failure, not "flexibility".

## 5. Call-sites updated (6)

All six were already classified caller-scoped, so all six now forward the token:

| Call-site | Note |
|---|---|
| `Api/Filters/DocumentAuthorizationFilter.cs` | |
| `Api/Filters/EntityAccessFilter.cs` | |
| `Api/Filters/FinanceAuthorizationFilter.cs` | |
| `Api/Filters/OfficeDocumentAccessFilter.cs` | **Orphaned** (A-23) — updated only so the solution compiles; **task 018 deletes this file**. Deliberately not otherwise touched |
| `Infrastructure/Authorization/ResourceAccessHandler.cs` | `httpContext` comes from `context.Resource`, already hard-required at `:44` |
| `Api/Ai/ChatDocumentEndpoints.cs` | Only non-filter consumer; injects the **interface** `IAuthorizationService`, not the concrete type — signature compatibility preserved |

## 6. ⚠️ FR-02's acceptance criterion is NOT fully met by this task alone

The criterion reads: *"`userAccessToken: null` no longer reaches `IAccessDataSource` on any
caller-scoped path."* After this task, a grep still finds two:

```
src/server/api/Sprk.Bff.Api/Api/PermissionsEndpoints.cs:76
src/server/api/Sprk.Bff.Api/Api/PermissionsEndpoints.cs:159
    await accessDataSource.GetUserAccessAsync(userId, documentId, userAccessToken: null, ct)
```

These call `IAccessDataSource` **directly**, bypassing `AuthorizationService` entirely — so no change
to `AuthorizationService` can reach them. That is **finding A-4**, owned by **task 006**
("Caller-scoped `PermissionsEndpoints`", FR-05), which already lists 004 as a dependency.

**Do not read this task as closing the criterion.** 004 closes the `AuthorizationService` path (A-2);
006 closes the direct-call path (A-4). Only together do they satisfy FR-02's wording. Stated here so
the wrap-up does not credit the criterion to 004 and leave 006 looking optional.

The `required` modifier does **not** protect the direct-call path either, because that path never
constructs an `AuthorizationContext` — it calls the data source's own method, whose `userAccessToken`
parameter still has a `= null` default. Task 006 should route those calls through
`AuthorizationService` rather than re-plumbing the token at the endpoint, or the same defect can
reappear at the next direct caller.

## 7. Scope boundary held

This task changed **whose** access is evaluated. It did **not** change **what rights** the data source
returns — that is task 005 (FR-04, the Read ceiling). So the endpoints A-20 identified remain
functionally limited until 005 lands; 004 makes the evaluation caller-scoped, 005 makes the rights
expressible. Both are needed before those routes work end-to-end.

## 8. Test coverage

`tests/integration/auth/UnifiedAccessControl/AuthorizationServiceCharacterizationTests.cs`

Both task-001 A-2 characterizations flipped:

1. `AuthorizeAsync_ForwardsCallerTokenToDataSource_EvaluatingAsTheCaller` — the token reaches the data
   source verbatim.
2. `AuthorizeAsync_ForDifferentCallersOnSameResource_CanReachDifferentDecisions` — two callers now
   reach **different** decisions on the same record, which was impossible before. Uses a new
   `PerCallerAccessDataSource` double that grants only to the holder of a specific token, mirroring
   what the real OBO path does. A double that answered uniformly would make this assertion a tautology.

New guard (the thing that makes A-2 non-recurrable):

3. `AuthorizeAsync_WithNoCallerToken_DeniesAndNeverConsultsDataSource` — `[Theory]` over `null` / `""`
   / whitespace. Asserts the deny, the **specific** reason code, and that the data source was
   **never consulted**. The double would GRANT if reached, so a pass could only come from the app-only
   fallback this task removes — the assertion cannot succeed vacuously.
