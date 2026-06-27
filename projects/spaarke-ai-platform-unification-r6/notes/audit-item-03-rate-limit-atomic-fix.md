# R6 Audit Item 03 — Rate-Limit Atomic Fix (Scope Endpoints)

> **Date**: 2026-06-07
> **Scope**: `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs`
> **ADR**: ADR-016 (rate limiting) — atomic compliance across all 5 scope LIST endpoints

---

## Summary

Brought the 5 scope LIST endpoints (`/api/ai/scopes/{skills,knowledge,tools,actions,personas}`) into atomic ADR-016 compliance. Pre-fix, the 4 canonical endpoints lacked the `RequireRateLimiting("ai-context")` policy; the 5th (personas, added by task 002) inherited the gap via sibling parity. R6 audit item 03 closed the gap on all 5 in one change rather than re-implementing the bug for the new endpoint.

---

## Endpoints Modified — Before / After

All 5 in `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs`:

| # | Route | Before | After |
|---|---|---|---|
| 1 | `GET /api/ai/scopes/skills` | `RequireAuthorization` (group); no rate limit | `RequireAuthorization` (group) + `RequireRateLimiting("ai-context")` |
| 2 | `GET /api/ai/scopes/knowledge` | `RequireAuthorization` (group); no rate limit | `RequireAuthorization` (group) + `RequireRateLimiting("ai-context")` |
| 3 | `GET /api/ai/scopes/tools` | `RequireAuthorization` (group); no rate limit | `RequireAuthorization` (group) + `RequireRateLimiting("ai-context")` |
| 4 | `GET /api/ai/scopes/actions` | `RequireAuthorization` (group); no rate limit | `RequireAuthorization` (group) + `RequireRateLimiting("ai-context")` |
| 5 | `GET /api/ai/scopes/personas` (R6 task 002) | `RequireAuthorization` (group); no rate limit | `RequireAuthorization` (group) + `RequireRateLimiting("ai-context")` |

Each endpoint also gained `.ProducesProblem(429)` for OpenAPI fidelity (matches the `ai-context` peer endpoints — `AnalysisChatContextEndpoints`, `StandaloneChatContextEndpoints`, `SummarizeSessionEndpoint`, `InsightsSearchEndpoint`, etc.).

---

## Policy Definition Confirmed

`ai-context` policy is already defined in `src/server/api/Sprk.Bff.Api/Infrastructure/DI/RateLimitingModule.cs:242`:

```csharp
// 13. AI Context - Read-heavy context resolution endpoints (ADR-016: 60 req/min/user)
options.AddPolicy("ai-context", context =>
{
    var userId = GetUserId(context);
    return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
    {
        Window = TimeSpan.FromMinutes(1),
        PermitLimit = 60,
        QueueLimit = 5,
        SegmentsPerWindow = 6
    });
});
```

**No new policy added.** No new feature flag. No ADR change. The policy was authored for read-heavy context-resolution endpoints — scope LIST is exactly that traffic shape.

Sibling endpoints already use `ai-context`:
- `Api/Ai/AnalysisChatContextEndpoints.cs:38`
- `Api/Ai/StandaloneChatContextEndpoints.cs:44`
- `Api/Ai/SummarizeSessionEndpoint.cs:107`
- `Api/Insights/InsightsSearchEndpoint.cs:91`
- `Api/Insights/InsightsAssistantEndpoint.cs:85`
- `Api/Insights/InsightEndpoints.cs:65`

The scope endpoints are now consistent with this peer set.

---

## Tests Added / Updated

Extended `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ScopePersonasEndpointTests.cs`:

1. **New `[Theory]` `AllScopeEndpoints_HaveAiContextRateLimitPolicy`** — 5 InlineData rows, one per endpoint. Introspects live `EndpointDataSource` and asserts `EnableRateLimitingAttribute.PolicyName == "ai-context"` on each. Catches regression if any endpoint loses the policy.
2. **New `[Fact]` `AllScopeEndpoints_PreserveAuthorizationFilter_AfterRateLimitAdd`** — Confirms group-level `RequireAuthorization()` remains in metadata on all 5 endpoints after the rate-limit addition (ADR-008 unchanged per stop-and-report trigger). Iterates all 5 endpoints; expects `IAuthorizeData` metadata to be non-empty on each.
3. Added `_factory` instance field + `using` directives for `Microsoft.AspNetCore.RateLimiting`, `Microsoft.AspNetCore.Routing` (for `RouteEndpoint`), and `Microsoft.Extensions.DependencyInjection` (for `GetRequiredService`).

**Behavioral 429 verification** is covered by existing `RateLimitingIntegrationTests.AiUploadPolicy_Returns429_WhenRateLimited` and `Policy_AllowsMultipleRequestsWithinLimit` (theory includes `ai-context`). Per the test design comment, the `OnRejected` handler is shared across all policies, and the `ai-context` policy's `Policy_AllowsMultipleRequestsWithinLimit` row already validates the policy is registered and accepts within-limit requests. No new behavioral test was added per the guidance "don't over-engineer the rate-limit test."

---

## Test Results

| Suite | Result | Count |
|---|---|---|
| `ScopePersonasEndpointTests` (extended) | All passed | 18 / 18 (was 11 / 11; +7 from new theory + new fact) |
| `RateLimitingIntegrationTests` (existing) | All passed | 22 / 22 |

**Total**: 40 / 40 passing, 0 failures.

---

## Build

`dotnet build src/server/api/Sprk.Bff.Api/ -c Release`: **0 errors, 16 warnings** (matches baseline; no new warnings introduced).

---

## BFF Publish-Size Delta (NFR-02 / ADR-029)

| Measurement | Compressed (ZIP) |
|---|---|
| Pre-fix (baseline this task)   | 45.5847 MB |
| Post-fix                       | 45.8845 MB |
| **Delta**                      | **+0.2998 MB** (~0.3 MB) |

Per the BFF Publish-Size Per-Task Verification Rule:
- Per-task delta ≥ +5 MB → escalation required. Delta is +0.3 MB → **PASS**.
- Cumulative size ≥ 55 MB → architecture review. 45.88 MB → **PASS** (~9 MB headroom to the soft threshold; ~14 MB to the hard 60 MB ceiling).
- Delta is consistent with compressor noise (no new types, no new assemblies, no new dependencies — pure method-chain additions on existing minimal-API registrations).

Cleanup: `deploy/audit-item-03-before/`, `deploy/audit-item-03-after/`, and the corresponding `.zip` files can be removed at project close. Listed here for traceability.

---

## ADR Compliance Snapshot

| ADR | Status |
|---|---|
| ADR-016 (rate limiting) | **Brought into compliance** — the purpose of this fix. |
| ADR-008 (endpoint filters / authorization) | Unchanged. Group-level `RequireAuthorization()` preserved on all 5 endpoints (verified by new test). |
| ADR-013 (AI architecture / facade boundary) | Unchanged. No `PublicContracts` modifications. |
| ADR-029 (BFF publish hygiene) | Within budget. +0.3 MB compressed, 45.88 MB total (vs 60 MB ceiling). |
| ADR-001 (Minimal API) | Unchanged. Standard `MapGet(...).RequireRateLimiting(...)` pattern, identical to sibling endpoints. |
| ADR-010 (DI minimalism) | Unchanged. No new DI registrations. |
| NFR-04 (no Microsoft Agent Framework) | Unchanged. No MAF references introduced. |
| NFR-03 (no new ADRs) | Honored. No ADR authored. |

---

## Stop-and-Report Triggers — All Cleared

- [x] `ai-context` policy IS defined (RateLimitingModule.cs:242). No fabrication needed.
- [x] No existing scope endpoint test broke. All 22 `RateLimitingIntegrationTests` + all 18 (post-extension) `ScopePersonasEndpointTests` pass.
- [x] No `PublicContracts` modification.
- [x] No ADR / feature-flag modification.
- [x] No other compliance gaps surfaced on these 5 endpoints. Authorization filter is at the group level (unchanged); rate-limit is the only ADR-016 surface that was missing.

---

## Files Touched

1. `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs` — added `.RequireRateLimiting("ai-context")` + `.ProducesProblem(429)` to all 5 endpoint mappings; updated class XML doc to reference ADR-016 and document the atomic-fix history.
2. `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ScopePersonasEndpointTests.cs` — added 1 theory (5 rows) + 1 fact verifying rate-limit metadata + preserved authorization; added `_factory` field + 3 using directives.
