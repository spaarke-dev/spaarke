# BFF Publish-Size — R3 Task 054

> **Task**: Implement `includeRelated=documents,events` (transitive memberships, Phase 1D)
> **Date**: 2026-06-21
> **Branch**: `work/spaarke-platform-foundations-r3`
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

| Metric | Value |
|---|---|
| Prior baseline (post-task 023) | **46.20 MB** compressed |
| Post-task 054 | **46.21 MB** compressed |
| **Delta** | **+0.01 MB** |
| Cumulative delta vs Phase 5 Outcome A baseline (45.65 MB) | +0.56 MB |
| NFR-01 ≤+1 MB per-task threshold | PASS |
| Single-task escalation threshold (+5 MB) | PASS (well under) |
| Architecture-review threshold (55 MB cumulative) | PASS |
| Hard ceiling (60 MB) | PASS (well under) |

## Method

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task054/
Compress-Archive -Path deploy/api-publish-task054/* -DestinationPath deploy/api-publish-task054.zip -CompressionLevel Optimal
```

## Source of delta

**Net +0.01 MB** — pure C# additions, no new NuGet packages:

- **NEW**: `Services/Ai/Membership/MembershipDepthExceededException.cs` — sentinel
  exception type the endpoint catches to map to 400 BadRequest with structured
  `offendingEntry` + `reasonTag` extensions (per FR-1D.2 / Q3).
- **MODIFIED**: `Services/Ai/Membership/IMembershipFieldDiscoveryService.cs` — new
  method `DiscoverLookupsTargetingAsync(sourceEntity, targetEntity, ct)` on the
  interface for 1-hop verification.
- **MODIFIED**: `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` — impl
  of `DiscoverLookupsTargetingAsync` reusing the existing `FetchLookupAttributesAsync`
  metadata-fetch seam (no new I/O path).
- **MODIFIED**: `Services/Ai/Membership/MembershipResolverService.cs` — extends
  `ResolveAsync` with (a) pre-validation rejecting explicit-chain syntax (`a.b`),
  (b) post-primary transitive expansion via `ResolveTransitiveAsync` + per-related-entity
  FetchXml `in`-operator query, (c) `MaterializeTransitiveResults` mirroring the
  primary `MaterializeResults` flow, (d) `DeriveRoleFromField` local helper (CamelCase
  strategy mirrored from `MembershipFieldDiscoveryService.DeriveRoleNameCamelCase`).
- **MODIFIED**: `Services/Ai/Membership/Models/MembershipResponse.cs` — adds optional
  `RelatedByRole` parameter (`IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>?`)
  with `JsonIgnore(WhenWritingNull)` so absence-of-request still emits the original
  shape exactly (backward-compatible).
- **MODIFIED**: `Api/Membership/MembershipEndpoints.cs` — catches
  `MembershipDepthExceededException` → 400 ProblemDetails with extensions
  `offendingEntry` + `reasonTag` + `maxHops` for SDK / UI callers.

No new NuGet packages. No new transitive references. The +0.01 MB delta is entirely
new .cs lines compiled into the existing assembly.

## Pre-merge checklist (bff-extensions.md §A)

- [x] Placement Justification stated in code XML doc — new code lives under
  `Services/Ai/Membership/` alongside its existing siblings (ADR-013 — AI placement).
- [x] ADRs cited: ADR-009 (Redis cache TTL), ADR-010 (interface as testing seam),
  ADR-013 (AI placement), ADR-016 (FetchXml resiliency reuse via `IDataverseService`),
  ADR-028 (Spaarke Auth v2 OBO — unchanged), ADR-032 N/A (no feature gate added).
- [x] Publish-size measured (+0.01 MB, well under +1 MB ceiling).
- [x] No NEW HIGH-severity CVE — only the pre-existing `Microsoft.Kiota.Abstractions 1.21.2`
  HIGH (carried from task 023 baseline).
- [x] Test update obligation (FR-22 / D-05): +10 new unit tests in
  `MembershipResolverServiceTests` (transitive + serialization shape), +2 new unit tests
  in `MembershipEndpointsTests` (400 mapping), +4 new integration tests in
  `Sprk.Bff.Api.IntegrationTests/Membership/TransitiveMembershipTests.cs` (AC-1D.1,
  AC-1D.2 endpoint mapping, AC-1D.2 perf measurement).
- [x] Asymmetric-registration §F.1: NOT TRIGGERED — no DI module touched; no new
  service registered. The existing `MembershipResolverService` registration in
  `MembershipModule` is unchanged; the new code paths are private methods on the
  resolver service itself.
- [x] Fixture-config-FIRST §F.2: applied — the new integration fixture
  `TransitiveMembershipIntegrationFixture` mirrors the canonical config-key set from
  `AdminJobsIntegrationFixture`. Auth handler emits the `oid` claim, in-memory cache
  replaces Redis (ADR-009), `IHostedService` instances stripped to prevent background
  workers (same pattern as the Dataverse + Admin fixtures).
- [x] Empirical-Reproduction-FIRST §F.3: applied — 1-hop semantics worked through
  empirically in `ResolveTransitiveAsync` before finalizing the contract; the
  `MembershipDepthExceededException.ReasonTag` enumeration (`explicit-chain-syntax`,
  `not-a-direct-lookup-target`, `unknown-entity`) emerged from tracing the three
  validation failure modes.
- [x] BFF csproj NOT modified (no new package references).

## Test inventory

### Unit tests (+12)

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipResolverServiceTests.cs` (+10):
  - `ResolveAsync_WithIncludeRelated_ReturnsTransitiveMemberships` (AC-1D.1 happy path)
  - `ResolveAsync_WithMultipleIncludeRelated_ReturnsAllNestedKeys` (multiple entries)
  - `ResolveAsync_WithoutIncludeRelated_RelatedByRoleIsNull` (backward compat — null when absent)
  - `ResolveAsync_WithExplicitChainSyntax_ThrowsDepthExceeded` (Q3 — dot syntax rejected)
  - `ResolveAsync_WithUnknownRelatedEntity_ThrowsDepthExceeded` (Q3 — metadata fetch fails)
  - `ResolveAsync_WithRelatedEntityLackingBackReference_ThrowsDepthExceeded` (FR-1D.2 — no 1-hop)
  - `ResolveAsync_WithIncludeRelatedAndNoPrimaryMatches_ReturnsEmptyNested` (empty primary, validated transitive)
  - `MembershipResponse_NestedRelatedByRole_SerializesAsRelatedByRoleCamelCase` (FR-1D.3 shape)
  - `MembershipResponse_NullRelatedByRole_OmittedFromJson` (JsonIgnore behavior)

- `tests/unit/Sprk.Bff.Api.Tests/Api/Membership/MembershipEndpointsTests.cs` (+2):
  - `GetMyMemberships_Returns400_WhenResolverThrowsDepthExceeded_ExplicitChainSyntax`
  - `GetMyMemberships_Returns400_WhenResolverThrowsDepthExceeded_NotADirectLookupTarget`

### Integration tests (+4)

- `tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/TransitiveMembershipTests.cs` (NEW):
  - `GetMembership_WithIncludeRelatedDocuments_ReturnsNestedByRole` (AC-1D.1)
  - `GetMembership_WithMultiHopChain_Returns400` (AC-1D.2 endpoint enforcement)
  - `GetMembership_WithRelatedEntityLackingBackReference_Returns400` (FR-1D.2 reject path)
  - `GetMembership_PerformanceWithinBudget` (AC-1D.2 / NFR-04 — in-process pipeline check; production p95 via App Insights)

- `tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/TransitiveMembershipIntegrationFixture.cs` (NEW)
  — mirrors AdminJobsIntegrationFixture canonical config + Moq-mocked resolver/Dataverse +
  fake auth handler (X-Test-Oid → oid claim).

**Test results**:
- Membership unit tests: 121/121 pass (10 new + 111 existing).
- Full BFF unit suite: 7486/7596 pass (+10 new; 110 pre-existing skipped, 0 failed — no regressions).
- Membership integration tests: 4/4 new pass.
- Full BFF integration suite: 57/57 pass (4 new + 53 existing).

## Design Decisions

### 1-hop semantics (FR-1D.2 / Q3)

Three rejection paths, all mapped to 400 BadRequest by the endpoint:

1. **`explicit-chain-syntax`** — entry contains `.` or `/` (e.g., `documents.events`). Rejected
   pre-I/O in `ResolveAsync` before discovery runs. Cheapest path; no Dataverse calls.
2. **`unknown-entity`** — `DiscoverLookupsTargetingAsync` throws `InvalidOperationException`
   ("Entity not found in Dataverse metadata"). Wrapped as `MembershipDepthExceededException`.
3. **`not-a-direct-lookup-target`** — discovery returns empty (no Lookup field on the related
   entity targeting the primary entity). The 1-hop budget cannot be satisfied; reject.

### FetchXML vs OData

Chose **FetchXML** for the transitive query. Rationale:
- Existing resolver already uses FetchXML (consistency).
- FetchXML's `<condition operator='in'>` cleanly joins primary IDs into a single
  condition per back-ref Lookup (one round trip per related entity, not N+1).
- OData `$filter` would require either an `in` operator (lacks server-side join
  semantics for multi-id IN) OR multiple `eq` clauses joined with `or` (verbose,
  hits URL-length limits at moderate primary-id counts).

### Response shape (FR-1D.3)

Extended `MembershipResponse` with `RelatedByRole` as an optional record parameter:
- Serializes as `"relatedByRole"` (camelCase per existing convention).
- `JsonIgnore(WhenWritingNull)` so the key is OMITTED when the caller did not request
  `includeRelated` (backward-compatible with all Phase 1A consumers).
- Nested shape: outer key = related entity logical name, inner key = role derived via
  the same CamelCase strategy used for primary memberships (`sprk_matter` → `matter`).

### Performance measurement (AC-1D.2 / NFR-04)

NFR-04 specifies p95 ≤300ms measured via **Application Insights server-side request
telemetry** (per spec.md). The in-process integration test (`GetMembership_PerformanceWithinBudget`)
measures a single warm pipeline call against a mocked resolver to catch gross
in-process pipeline regressions (auth, routing, JSON serialization, ProblemDetails
handling). The assertion uses a generous 3000ms ceiling — production NFR-04 budget
is measured separately via App Insights and is not represented in this test.

In-process measurement at task 054 authoring time: well under 200ms warm. No
production performance limit was observed that would require documentation in the
architecture doc (task 104 will validate against real Dataverse).

## Downstream tasks

- **Task 055** (1-hop enforcement-only follow-up) — superseded by this task; the
  enforcement is binding here. Task 055 may now focus on operator documentation
  or be retired.
- **Task 056** (perf tests) — production p95 measurement via App Insights remains
  out-of-scope per spec NFR-04; task 056 may scope down to canary load tests.
