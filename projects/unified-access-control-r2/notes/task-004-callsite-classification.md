# Task 004 — `AuthorizeAsync` call-site classification (POML Step 1, pre-implementation)

> **Date**: 2026-08-21 · **Spec**: FR-02 · **Finding**: A-2
> Recorded **before** any code change, per the POML's prescriptive Step 1.

---

## Headline: there are ZERO app-only consumers

Every consumer of the core `Spaarke.Core.Auth.AuthorizationService.AuthorizeAsync` runs inside an HTTP
request with an authenticated caller. That makes the fix materially simpler than the POML anticipated
and **removes the work in Step 3** ("route app-only call-sites through an explicitly-named app-only
entry point") — there is nothing to route.

| # | Call-site | Mechanism | Caller context | Classification |
|---|---|---|---|---|
| 1 | `Api/Filters/DocumentAuthorizationFilter.cs:79` | endpoint filter | `HttpContext` | **caller-scoped** |
| 2 | `Api/Filters/EntityAccessFilter.cs:154` | endpoint filter | `httpContext` (explicit param) | **caller-scoped** |
| 3 | `Api/Filters/FinanceAuthorizationFilter.cs:85` | endpoint filter | `HttpContext` | **caller-scoped** |
| 4 | `Api/Filters/OfficeDocumentAccessFilter.cs:132` | endpoint filter | `httpContext` | **caller-scoped — but ORPHANED** (zero routes; finding A-23, task 018) |
| 5 | `Infrastructure/Authorization/ResourceAccessHandler.cs:73` | `AuthorizationHandler<ResourceAccessRequirement>` | hard-requires `context.Resource is HttpContext` (:44), else fails | **caller-scoped** |
| 6 | `Api/Ai/ChatDocumentEndpoints.cs:911` | Minimal API handler, DI-injected `IAuthorizationService` | request handler | **caller-scoped** |

### Deliberately NOT consumers of this service

| Component | Uses instead | Why it is out of scope |
|---|---|---|
| `AiAuthorizationFilter` | `IAiAuthorizationService` | Already passes the caller bearer (`AiAuthorizationService.cs:176-180`) — the one genuinely caller-scoped path today, and the A-19 evidence base |
| `AnalysisAuthorizationFilter` | `IAiAuthorizationService` | same |
| `VisualizationAuthorizationFilter` | `IAiAuthorizationService` | same |
| `DataverseAuthorizationFilter` | `IDataversePrivilegeChecker` | separate privilege mechanism; never touches `OperationAccessPolicy` or `IAccessDataSource` |

Verified by grep 2026-08-21: the three AI filters declare `private readonly IAiAuthorizationService`,
not the core type. An earlier grep on the field *name* `_authorizationService` matches them too — that
is a naming coincidence, not a dependency. Do not be misled by it.

---

## What this means for the implementation

1. **No app-only entry point is needed.** Every path can require the caller token. This avoids adding a
   second public surface to `AuthorizationService`, which also keeps ADR-010 satisfied (no new
   authorization service layer) without argument.
2. **"Missing token → Deny" is unambiguous**, because there is no legitimate caller of this service
   that lacks a request context. The POML's escalation trigger — *"if any consumer cannot supply a
   caller token AND is not clearly a background path, STOP"* — does **not** fire on the current call
   graph. If a future background caller appears, it must add an explicit app-only entry point rather
   than reintroducing the null default.
3. **Call-site 4 is dead** (A-23). It should be deleted by task 018 rather than plumbed here. Plumbing a
   token into an orphaned filter is wasted work; deleting it is the correct disposition. This task
   should leave it alone and let 018 remove it.
4. **`ChatDocumentEndpoints.cs:911` is the only non-filter consumer** and injects the *interface*
   (`IAuthorizationService`) rather than the concrete type the filters use. Any signature change must
   keep that injection working, or that endpoint breaks. Worth noting because it is easy to miss when
   only the `Api/Filters/` directory is in view.

## Ordering

Task 014 (auth-mode cache key) **must be merged before** this task flips the switch, or the new OBO
calls hit stale SP-mode cache entries — the exact 60-second poisoning window A-19 describes, widened
rather than closed. Confirmed as a hard dependency in the POML `<deps>`.
