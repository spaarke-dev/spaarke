# Task 080 — Vertical-Slice Seam Coverage Map

> **Purpose**: reuse-first record of what task 080 added vs. what already existed, and a MAINTAIN-class
> classification feeding task 090's `/test-diet` (ADR-038 §7 build-vs-maintain reconciliation).
> **Rigor**: TEST-MODIFYING (root §8 override row) — Step 9.5 `code-review` + `adr-check` run UNCONDITIONALLY.

## New file

`tests/integration/seam/Communication/CommunicationWorkspaceReadSeamTests.cs` — 17 new tests, all green.

### Code-review finding fixed before sign-off

Step 9.5 `code-review` caught a real issue in the first draft of gap #1 (the 11-entity theory): the test data
was generated at test-time via `RegardingFieldMap.All.Select(...)`, deriving BOTH the input `entityType` and the
expected OData field from the SAME map the production code reads. That made the 7 additional families (beyond
the 4 the unit suite already hand-codes) a **tautology** (ADR-038 §7 B6 mirror-test risk) — it would only prove
"the service calls the map", never that the map's VALUES are correct (e.g. a copy-paste typo mis-mapping
`sprk_event` to `sprk_regardingbudget` would sail through). **Fix applied**: the 11-family table is now a
HARD-CODED literal `[MemberData]` array (matching `CommunicationByRegardingReadTests`' existing precedent of
hand-coding expected values), giving genuine regression protection in the Theory. A companion
`AllElevenAdr024RegardingFamilies_StaysInSyncWith_RegardingFieldMapAll` fact closes the loop the other
direction (catches the literal table drifting from `RegardingFieldMap.All` if a family is added/removed/renamed),
so the two tests together give two-way protection without either being tautological.

Placed under `tests/integration/seam/**` (ADR-038 vertical-slice-seam KEEP category — the DoD for this wave's
new read endpoints + resolver policy), per §11 EXTENDING the R1 seam-test harness (adds a 4th class alongside
`AssociationSpineSeamTests`, `MessagingSpineSeamTests`, `ThreadResolverSeamTests` — no parallel harness stood up).

## Composition gaps closed (reuse-first — what did NOT already exist)

| # | Gap | Why it's a genuine gap, not a clone | New test(s) |
|---|---|---|---|
| 1 | **Full 11-entity by-regarding pass** | `CommunicationByRegardingReadTests` (unit, task 010) explicitly covers 4 of 11 ADR-024 families (sprk_matter, contact, account, sprk_project) — satisfies the spec's "≥3 explicit" floor but not the full-11 matrix. | `ReadByRegardingAsync_AllElevenAdr024Families_ResolvesOwnTypedLookupAndBehavesIdentically` — `[Theory]` data-driven straight off `RegardingFieldMap.All` (11 cases: sprk_matter, sprk_project, sprk_invoice, sprk_servicerequest, sprk_workassignment, sprk_event, sprk_budget, sprk_analysis, sprk_organization, account, contact). Future 12th family auto-covered (no hand-added 12th case needed). |
| 2 | **All-facets-composed filtered query** | `CommunicationFilteredQueryTests` (unit, tasks 011/051) asserts each facet independently, plus ONE 2-facet AND (channel+participant). No test composes thread+channel+from+to+participant together. | `QueryCommunicationsAsync_AllFiveFacetsComposedTogether_AndsThreadChannelDateRangeAndParticipant` — asserts all 5 clauses present + exactly 4 `" and "` joins (not just pairwise). |
| 3 | **Filtered-query private-thread-hidden (generic)** | `CommunicationFilteredQueryTests` has a participant-*specific* negative-access case; no generic `thread=`-facet private-thread-hidden case (the by-regarding equivalent already exists in `CommunicationByRegardingReadTests` and is NOT duplicated here). | `QueryCommunicationsAsync_ThreadFacetOnPrivateThreadWithoutGrant_ReturnsEmptyNotError`. |
| 4 | **Auto-threading 3-tier ladder w/ REAL per-channel strategy** | `ThreadResolverTests` (unit) exercises the ladder with a **stub** `IThreadKeyStrategy` (isolates the resolver's own branching). `ThreadResolverSeamTests` (existing seam) composes the **real** `MessagingThreadKeyStrategy` but only for Tier 1 (JOIN/CREATE via channel-ref). Neither composes the real strategy's `Skip` outcome (no ACS thread id yet — the genuine real-world trigger) through Tier 2/3. | `ResolveAndAssignThreadAsync_RealStrategySkipsWithRegarding_LadderCreatesRecordDefaultThenIdempotentlyJoins` (Tier 2 create + idempotent 2nd-orphan join) and `..._RealStrategySkipsWithNoRegarding_LadderCreatesPerUserMasterThenIdempotentlyJoins` (Tier 3 create + idempotent 2nd-orphan join). |
| 5 | **No-membership-union regression guard** | No existing test asserts the retired union path (`../messaging-communication-app-r1/notes/access-model-decision.md`, retired 2026-07-16) cannot silently creep back into either read surface's dependency graph. | `NoMembershipUnionRegression_ReadServiceAndAccessFilter_NeverDependOnRetiredGrantOrMembershipSeams` — structural (NetArchTest-style) reflection assertion on `CommunicationThreadReadService` + `CommunicationAccessFilter`'s OWN constructor parameter types; not a DI-registration test, not a private-member reflection test (ADR-038 §6 sanctioned architecture-guard shape). |

## What was NOT re-tested (cited, not cloned)

| Existing coverage | File | Why not duplicated |
|---|---|---|
| by-regarding DTO shape, multi-thread grouping, unknown-entity 400, no-visible-threads, unresolved-caller 403, private-thread-hidden (by-regarding), internal-only-hidden (anchor case) | `CommunicationByRegardingReadTests.cs` | Already MAINTAIN-class, already green; task 080 only extends the entity-family matrix. |
| Per-facet filtered query (thread/regarding/channel/date individually), participant facet (resolved-person, unresolved-address, 2-facet AND, no-match graceful degrade, participant negative-access, malformed 400, blank no-op), no-facet 400, malformed-facet theory, internal-only-hidden (anchor case) | `CommunicationFilteredQueryTests.cs` | Already MAINTAIN-class, already green. |
| Tier-1 JOIN/CREATE (email ancestry + record-anchored/Direct create), no-strategy guard, NFR-02 swallow (characterization region); Tier-2/3 ladder branching incl. idempotency, never-null guarantee, NFR-02 swallow (extension region); FR-07 naming re-derive (marker-gated) | `ThreadResolverTests.cs` | Characterization + extension regions are the load-bearing NFR-03/FR-09 pins from tasks 070/071 — referenced as the characterization-guard baseline, confirmed green in the same `dotnet test` run, not cloned. |
| Tier-1 JOIN/CREATE composed with REAL `EmailThreadKeyStrategy`/`MessagingThreadKeyStrategy` (fresh chat message + join-existing-thread via channel-ref; email reply-join + fresh-outbound-create); NFR-02 swallow; ADR-024 anchor-reuse | `ThreadResolverSeamTests.cs` (existing R1 seam file) | Task 080 EXTENDS this seam category with a 2nd file for the ladder gap rather than editing this file (kept the R1 file's own scope — Tier 1 only — intact per "extend, don't clone"). |
| Participant write invariants (resolved XOR contact/systemuser, unresolved-address row Q-D, no-dual-lookup, best-effort/non-fatal, idempotent per message) | `CommunicationParticipantIndexerTests.cs` | Write-side characterization for task 050; referenced as the characterization baseline, confirmed green, not cloned. |
| Fresh-inbound spine (raw ACS event → normalizer → dispatcher → ingestor → resolver → persist, both fresh-thread and join-existing-thread) | `MessagingSpineSeamTests.cs` (existing R1 seam file) | Composition-gap closed by R1; task 080 does not touch it. |
| Association dispatch spine (rungs → mapper → gate → resolver) | `AssociationSpineSeamTests.cs` (existing R1 seam file) | Out of scope for task 080 (no FR-01/02/08/09/NFR-03 surface touched). |

## Test-diet feed (for task 090)

All 16 new tests are **MAINTAIN-class** per the three-question test (`tests/CLAUDE.md` "Expect to Defend at Project
Close"):

1. **What breaks if deleted?** The 11-entity theory protects a future family regression (a family silently
   resolving the wrong typed lookup or behaving differently). The all-facets test protects the AND-composition
   contract as more facets are added. The private-thread-hidden (filtered-query) test protects the NFR-03 no-leak
   guarantee on the `query` endpoint specifically. The ladder tests protect the real per-channel `Skip` → Tier 2/3
   fall-through (the actual production trigger, not a synthetic one). The no-union-regression test protects against
   the retired membership-union path being silently reintroduced as a dependency.
2. **KEEP path?** Yes — `tests/integration/seam/Communication/**` (vertical-slice-seam category, ADR-038 §2).
3. **Behavior vs. implementation?** All assertions are on caller-observable behavior (DTO shape, OData filter
   composition, non-null thread resolution, join-not-duplicate) or on a class's public dependency contract (the
   no-union-regression test) — none assert on private/internal implementation detail.

No AMBIGUOUS or SCAFFOLDING-class tests were added by this task.

## Gates

- `dotnet build src/server/api/Sprk.Bff.Api/` — clean (0 errors; pre-existing warnings only, unrelated to this task).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter FullyQualifiedName~Communication` — **584 passed, 8 skipped
  (pre-existing), 0 failed** (592 total; includes the 16 new seam tests + all existing Communication-area tests,
  proving the characterization baseline stayed green before/after).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter FullyQualifiedName~CommunicationWorkspaceReadSeamTests` — 17
  passed, 0 failed (isolated confirmation of the new file).
- Full solution `dotnet test` (whole `Sprk.Bff.Api.Tests` assembly — unit + contract + regression + seam) —
  **8654 passed, 101 skipped (pre-existing), 0 failed, Total 8755**. Confirms the characterization baseline
  (`ThreadResolverTests`, `CommunicationParticipantIndexerTests`, and every other pre-existing Communication test)
  stayed green before and after this task's additions, with zero regressions anywhere in the suite.
- Banned-pattern grep (`Mock<HttpMessageHandler>`, DI-registration, ctor null-check, `Stopwatch`) over the new
  file — 0 matches.
- **Publish-size / CVE**: unchanged — this task is test-only (`tests/**`), no `src/` file touched, no new package
  reference added. Not re-measured (N/A per the task's own constraint).
- **Live Dataverse verification**: DEFERRED — MCP unavailable this session (consistent with every prior R2 BFF
  task). All tests compile against field logical-name string literals and are unit/seam-tested against a mocked
  `IImpersonatedCommunicationQuery` / `IGenericEntityService` boundary (ADR-038-compliant module-boundary mock,
  not a transport-level mock). Live verification is the owner's responsibility once tasks 002/003 schema deltas
  are applied to the live environment (see those tasks' notes).

## No-membership-union regression — explicit statement (NFR-03)

`CommunicationThreadReadService`'s constructor takes exactly 4 dependencies: `IImpersonatedCommunicationQuery`,
`ICommunicationAccessFilter`, `ICallerSystemUserResolver`, `ILogger<CommunicationThreadReadService>`.
`CommunicationAccessFilter`'s constructor takes exactly 1: `ILogger<CommunicationAccessFilter>`. Neither
references `IThreadPrivateGrantProvider` (retained but explicitly retired-for-reads per its own doc comment) or
any membership-resolution seam. The new structural test pins this shape; the existing
`ReadByRegardingAsync_PrivateThreadWithoutGrant_IsAbsentAndItsMessagesNeverFetched` test
(`CommunicationByRegardingReadTests.cs`) is the behavioral proof that a private thread absent from the
impersonated set stays invisible — together these are the "no union path exists on reads" regression NFR-03
requires.
