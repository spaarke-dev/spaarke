# Test-Diet Report — messaging-communication-app-r2

> **Gate**: root CLAUDE.md §7 project-close (ADR-038 build-vs-maintain classifier) · **Date**: 2026-07-19
> **Result**: **0 scaffolding / all MAINTAIN — 0 deletions, 0 moves.**

## Reconciliation

R2 added tests in three KEEP-path categories (ADR-038). All are behavior tests of shipped surface, at their correct KEEP paths — none are scaffolding.

| Tests added | KEEP path | Class | Disposition |
|---|---|---|---|
| `CommunicationByRegardingReadTests`, `CommunicationFilteredQueryTests` (010/011/051) | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/` | MAINTAIN (behavior: access-filter parity, facets, `participant=` join, negatives) | KEEP |
| `CommunicationParticipantIndexerTests` (050) | same | MAINTAIN (XOR invariant, best-effort, idempotent, resolved/unresolved rows) | KEEP |
| `ThreadResolverTests` +extension (070/071) | same | MAINTAIN (3-tier ladder, characterization, marker-gated re-derive, master guard) | KEEP |
| `CommunicationWorkspaceReadSeamTests` (080) | `tests/integration/seam/Communication/` | MAINTAIN (vertical-slice seam — the ADR-038 DoD category; 11-entity, no-membership-union guard) | KEEP |
| Client: `CommunicationTimeline.reducer`, `buildTimeline` (+regarding), `TimelineComposeBox`, `RecipientField` (+entityType), `userLookup.entityType`, PCF `hostContext` | shared-lib + PCF `__tests__/` | MAINTAIN (component/pure-logic behavior) | KEEP |

## Banned-pattern check (ADR-038 §7)

Task 080 grep-verified across the new tests: **0** `Mock<HttpMessageHandler>`, **0** `Mock<IServiceClient>`, **0** DI-registration tests, **0** ctor null-check tests, **0** `Stopwatch` (uses `TimeProvider`/mocked boundaries). Mocking is at module boundaries (`IImpersonatedCommunicationQuery`, Dataverse service), not the HTTP-handler level.

## Conclusion

No SCAFFOLDING-class tests were introduced; nothing to `git rm`/`git mv`. The reuse-first discipline (080 added only 5 composition-gap seam tests rather than re-covering unit-tested paths) kept the suite lean. Full solution: **8654 pass / 101 skip / 0 fail**.
