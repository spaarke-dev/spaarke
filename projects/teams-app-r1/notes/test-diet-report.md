# teams-app-r1 — Test Diet Report (project-close gate, root CLAUDE.md §7 / ADR-038 §7)

> **Date**: 2026-08-06 · **Mode**: read-only (classification only — emits recommendations, does not auto-execute).
> **Classifier**: ADR-038 §7 build-vs-maintain + the 17 scaffolding bans.
> **Verdict**: **No SCAFFOLDING-class tests to delete.** All project-added tests are MAINTAIN-class (auth regression, endpoint contract, vertical-slice seam, or branched domain logic). **0 `git rm` recommended.**

## Tests added / modified by this project

| Test file | Class | KEEP category | Rationale |
|---|---|---|---|
| `Infrastructure/ExternalAccess/CallerPrincipalTests.cs` (new, 025) | MAINTAIN | domain / behavior | CIAM rights-mapping **regression** + workforce record-scope semantics (NFR-08). Deleting it would let the CIAM `/me` payload or workforce scope drift silently. |
| `Infrastructure/ExternalAccess/CallerPrincipalResolverTests.cs` (new, 025) | MAINTAIN | auth / behavior | Plane selection (spoof-safe), workforce NFR-08 scope, CIAM deny semantics. Security-critical branched logic. |
| `integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs` (modified, 025) | MAINTAIN | contract + security-auth | Download authz-before-stream 403-no-bytes + no-pointer through the HTTP surface. Real `WebApplicationFactory`, module-boundary doubles only (no banned `Mock<HttpMessageHandler>`). |
| `integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs` | MAINTAIN | seam | Vertical-slice standing-grant union (ADR-038 seam KEEP category). |
| `Api/ExternalAccess/AccessibleRecordSetAuthorizationFilterTests.cs` | MAINTAIN | auth | record∉set → 403 enforcement (the authz gate). |
| `Api/ExternalAccess/WorkforceCollaborationDownloadEndpointTests.cs` | MAINTAIN | security-auth | Broker-only + document→project scoping negative path. |
| `Infrastructure/ExternalAccess/AccessibleRecordSetServiceTests.cs` | MAINTAIN | domain | Per-plane composition (systemuser membership / contact grants ∪ standing). Branched business logic. |
| `Infrastructure/ExternalAccess/WorkforcePrincipalResolverTests.cs` | MAINTAIN | auth | oid→systemuser / contact / deny resolution branches. |
| `Infrastructure/ExternalAccess/ContactStandingGrantReaderTests.cs` | MAINTAIN | domain | FLS-gated standing-grant flag read (the negative-case gate). |
| `Infrastructure/ExternalAccess/ExternalParticipationServiceInvalidationTests.cs` | MAINTAIN | domain | Cache-invalidation key contract (ADR-009). |
| `Infrastructure/Routing/TenantEnvironmentRouterTests.cs` | MAINTAIN | tenant | `tid`→env deny-by-design (unmapped/ambiguous tid denied — cross-tenant-exposure guard). |
| `Infrastructure/Routing/TenantEnvironmentRoutingFilterTests.cs` | MAINTAIN | tenant | Routing filter enforcement. |
| `Api/ExternalAccess/ExternalAccessEndpointTests.cs` (1 test modified, 025) | MAINTAIN* | domain | Grant/revoke/invite validation + DTO contracts. *Note: a few `ExternalCallerContext` domain-logic cases now cover the inert legacy class (see below).* |

## AMBIGUOUS / flagged for reviewer judgment
- **`ExternalAccessEndpointTests.cs` — the `ExternalCallerContext` domain-logic cases** (HasProjectAccess / GetEffectiveRights / GetAccessLevel). After task 025, `ExternalCallerContext` is inert (superseded by `CallerPrincipal`; the equivalent semantics are covered by `CallerPrincipalTests`). These cases are not *harmful* (the class still compiles) but test superseded code. **Recommendation (reviewer judgment):** delete alongside the `/api/v1/collab` + `ExternalCallerAuthorizationFilter` cleanup (tracked in `r2-coordination-response.md` §7/§8-D2). NOT deleted now (out of the 025 scope; bounded deferral). No `git rm` emitted this pass.

## Banned-pattern audit (17 bans)
- No `Mock<HttpMessageHandler>` (B1), no DI-registration tests (B3), no ctor-null-only tests (B4), no mirror/pass-through/coverage-filler tests (B6/B9/B10) introduced by this project. Contract tests mock only at sanctioned module boundaries. ✅ Clean.

## Emitted commands
_None._ (No SCAFFOLDING deletions. The one AMBIGUOUS item is a reviewer-judgment deferral tied to a separate cleanup, not a diet deletion.)
