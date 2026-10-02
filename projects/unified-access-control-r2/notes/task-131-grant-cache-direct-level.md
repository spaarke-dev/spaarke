# Task 131 (#1055): defect C3, the grant cache drops `DirectAccessLevel`

**Status:** code complete on `task/uac-r2-131`. The live gate (criterion 11) is a pending MANUAL gate.
**Rigor:** FULL. **Model:** Opus 5.5.

## What was wrong (re-verified against HEAD `bd7ce8b55` before editing)

Every anchor in the POML's background held:

- `CacheGrantSetAsync` wrote only `(id, AccessLevel)`.
- `CachedParticipation` and `CachedRootGrant` had no `DirectAccessLevel`, so `ToParticipation()` and `ToGrant()` left it null.
- `CacheVersion` was 4.

On a cache hit, `AccessibleRecordSetService.GrantedRightsFor` read `DirectAccessLevel` for a secure root, found null, and returned `AccessRights.None`. A DIRECT grant on a secure project, matter or work assignment therefore lost its rights for most of every 60-second window.

## The fix

All of it is in `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalParticipationService.cs`.

1. **Both cached DTOs carry `int? DirectAccessLevel`.** It is written in `CacheGrantSetAsync` for all three root types; matters and work assignments go through one `CachedRootGrant.From`. `ToParticipation()` and `ToGrant()` restore it **null as null**: never defaulted to `AccessLevel`, never inferred (ADR-003 fail-closed; the over-grant constraint).
2. **`CacheVersion` is now 5.** The const comment has a v5 entry. The `CachedGrantSet` remarks record the recurrence, and why the existing "bump on shape change" rule did not fire: task 037 edited the grant *types*, not the cached shape.
3. **`QueryGrantSetAsync` is `internal virtual` (was `private`).** This is a test seam with no behaviour change. It follows the `NoAccessListReader.QueryChunkAsync` convention. The test double overrides this method, so `GetGrantSetAsync` runs unmodified, including its cache logic.
4. **The `InvalidateAsync` doc comment** said `…:v1`. It now says `…:v{CacheVersion}`.

Two doc lines stated the same stale fact; this task includes them under the owner's completeness rule:

- `docs/architecture/uac-access-control.md:67` said "version 3". It now says 5, names the constant, and describes the two-level shape.
- `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md:256` also said "version 3", which the POML did not list. It now says 5.

**Placement (CLAUDE.md §10):** this is an in-place fix of an existing BFF cache. It adds no new service, endpoint, package or DI registration, and no NuGet change. CLAUDE.md §11 justification is not required: the change touches two private DTOs, one constant and one access modifier.

## Tests: `tests/integration/seam/ExternalAccess/GrantCacheRoundTripSeamTests.cs` (19 cases)

The double, `CacheBackedParticipationService`, subclasses the PRODUCTION service. It overrides only:

- `QueryGrantSetAsync`, counting calls;
- `GetRootRecordFlagsAsync`, for the secure flag;
- `QueryActiveOrgIdsAsync`, returning none;
- `GetReferencedOrganizationIdsAsync`, returning none.

It does **not** override `GetGrantSetAsync`.

The cache is the PRODUCTION `TenantCache` over `MemoryDistributedCache`, so the production serializer runs. The request carries a `tid` claim, because the cache is skipped without one. The REAL `AccessibleRecordSetService` composes on the miss and again on the hit.

Its other boundaries contribute nothing:

- standing grant: `NotHeld`;
- deny list: `Empty`;
- membership: Strict, so the contact plane can never walk it. The systemuser plane walks it and finds nothing, so membership cannot mask the grant term.

| Criterion | Test | Cases |
|---|---|---|
| 1 | `ComposeAsync_DirectCollaborateGrantOnSecureRoot_ComposesReadCreateWriteOnMissAndIdenticallyOnHit` | 3 roots × {contact-only plane, systemuser plane via linked contact} = 6 |
| 2 | `ComposeAsync_OrgInheritedOnlyGrantOnSecureRoot_IsAbsentOnMissAndOnHit` (renamed by task 136; was `..._ComposesNoneOnMissAndOnHit`) | 3 roots |
| 3 | `ComposeAsync_ViewOnlyDirectPlusCollaborateOrgOnSecureRoot_ComposesReadOnlyOnMissAndOnHit` | 3 roots |
| 4 | `ComposeAsync_OrgInheritedCollaborateGrantOnNonSecureRoot_ComposesReadCreateWriteOnMissAndOnHit` | 3 roots |
| 5 | `ComposeAsync_NullLevelGrant_ConfersNothingOnMissAndOnHit` (renamed and reversed by task 136; was `..._KeepsItsIdWithNoRightsOnMissAndOnHit`) | matter, work assignment |
| 6 | `GetGrantSetAsync_EntryCachedUnderThePreviousVersionKey_IsNotServedAndDataverseIsRequeried` | 1 |
| 7 | `GetGrantSetAsync_EveryPublicSettableGrantProperty_SurvivesTheRoundTripThroughTheProductionCache` | 1 |

**Why criteria 2–4 cover all three roots (criterion 12):** each root type has its own cache projection: projects use `CachedParticipation` with a non-nullable `AccessLevel`, while matters and work assignments use `CachedRootGrant`. A per-root test is the only way to cover each projection's write and read. Criterion 5 omits projects because a project grant cannot have a null level: the project partition drops such rows.

**Criterion 6 includes a control.** The same v4-shaped payload under the CURRENT key *is* served. Without that, the negative could pass just because the seed failed to deserialize. The `CacheVersion == 5` pin sits in the same test, as the criterion asks.

**Criterion 7 (the shape-parity guard)** works by reflection over PUBLIC members only; ADR-038 B8 bans non-public reflection.

- It enumerates the public instance properties of `ExternalGrantSet`, `ExternalParticipation` and `ExternalRootGrant`.
- Every property with a public setter (`init` included) is populated with a non-default value. Level-typed slots within one object get distinct levels, so a slot restored from its sibling is caught.
- The populated set is round-tripped, and every property is compared recursively.
- A public property with no public setter must be named in `NotCarriedByDesign` with its reason. Today that is `Matters` and `WorkAssignments`, the derived id views. This makes a new get-only property a conscious classification, not a silent skip.
- A property of a type the guard cannot populate throws, naming both the guard and the cached shape as the places to update.
- Task 132's never-cached fault flag is the expected next `NotCarriedByDesign` entry.

### Determinism approach (the TimeProvider constraint)

The production cache write is fire-and-forget (`_ = CacheGrantSetAsync(...)`). Over `MemoryDistributedCache` every await in that write completes synchronously: `JsonSerializer.SerializeAsync` into a `MemoryStream`, then `MemoryDistributedCache.SetAsync`. So the entry exists before `GetGrantSetAsync` returns, with no thread scheduling involved.

The tests do not rely on this silently. Every miss→hit pair does two things:

1. It reads the current-version entry back through the same `TenantCache` (`GetStringAsync`) **before** the second call.
2. It asserts the Dataverse read ran **exactly once** across both calls.

An unwritten entry therefore fails loudly; the "hit" can never quietly re-run the miss. Production behaviour is unchanged. The suite has no sleeps, delays or Stopwatch.

## Shape-guard and perturbation evidence (criterion 9 and step 5); all reverted

| Perturbation | Result |
|---|---|
| A: remove the `DirectAccessLevel` WRITE (project and root DTOs) | 10 of 19 red. Criterion 1: all 6. Criterion 3: all 3. Criterion 7: `Projects[0].DirectAccessLevel: wrote ViewOnly, read back null` (and the same for MatterGrants and WorkAssignmentGrants). The criterion-1 message reads `found AccessRights.None` (the C3 symptom exactly), on both planes. |
| B: remove the `DirectAccessLevel` READ (restore side) | The same 10 of 19 red. |
| C: restore null as `AccessLevel` (the over-grant default) | Criterion 2 red on all 3 roots: `found AccessRights.Read\|Write\|Create`. |
| D (step 5): add `public ExternalAccessLevel? ProbeLevel131 { get; init; }` to `ExternalRootGrant` | Criterion 7 red: `MatterGrants[0].ProbeLevel131: wrote Collaborate, read back null`, and the same for WorkAssignmentGrants. |

After reverting, all 19 pass, and a grep for `PERTURB|ProbeLevel131` over `src/` finds no matches.

## Criterion 6: invalidators (grep evidence)

```
Api/ExternalAccess/GrantExternalAccessEndpoint.cs:41   private const int CacheVersion = ExternalParticipationService.CacheVersion;
Api/ExternalAccess/RevokeExternalAccessEndpoint.cs:36  private const int CacheVersion = ExternalParticipationService.CacheVersion;
Api/ExternalAccess/ProjectClosureEndpoint.cs:41        private const int CacheVersion = ExternalParticipationService.CacheVersion;
Api/ExternalAccess/SetRecordShareExpiryEndpoint.cs:345 ExternalParticipationService.CacheVersion (used directly)
Infrastructure/ExternalAccess/ExternalParticipationService.cs  InvalidateAsync: CacheVersion (the constant itself)
```

None of the grant-cache invalidators uses an integer literal. The aliases were left as they are, because task 137 owns consolidating them. The `CacheVersion = 1` literals in `ImpersonatedRootSetSource.cs` and `ModuleEntitlementResolver.cs` belong to different cache resources, not this one. The unit test `ExternalParticipationServiceInvalidationTests` also aliases the shared constant.

## Escalation triggers

1. **Composition changes beyond C3: did not fire.** `GrantedRightsFor` is the only reader of `DirectAccessLevel` in `src/` (grep: `AccessibleRecordSetService.cs:244/250/256`). Criteria 2, 4 and 5 show no change outside the C3 scenario: org-only on a secure root stays None, the non-secure path is unchanged, and null-level rows are unchanged.
2. **Tasks 135/136 merged first: did not fire.** Both are `🔲 [open]` in TASK-INDEX at `bd7ce8b55`.
3. **No usable live test user: not evaluated.** This run was restricted to read-only, with no deploy and no grant writes, so the live gate is pending (below).

## Verification runs (criterion 10)

- `dotnet build tests/unit/Sprk.Bff.Api.Tests`: succeeded, 0 warnings.
- `GrantCacheRoundTripSeamTests`: 19 of 19 passed.
- Full BFF unit suite (`dotnet test tests/unit/Sprk.Bff.Api.Tests`): **13,067 passed, 0 failed, 56 skipped** (13,123 total).
- `dotnet test tests/Spaarke.ArchTests`: **337 passed, 0 failed**.
- `dotnet list package --vulnerable --include-transitive` on Sprk.Bff.Api: no vulnerable packages. No csproj or package change.
- Step 9.5 quality gates:
  - code-review: 0 Critical, 0 Warning. Info: during a rolling deploy, old instances invalidate the v4 key while new instances serve v5, so a grant or revoke handled by an old instance can be stale on a new one for at most 60 s. That is inside the existing TTL bound, and every earlier version bump had the same property. Suggestions: the `internal virtual` seam widening, and the undisposed `HttpClient` in the double, which matches existing doubles.
  - adr-check: ADR-003, -009, -038, -010, -001/-052 and §10 compliant; 0 violations.

## Pending manual gates

- **Criterion 11, the live gate in dev, after deploy.** Not run, because this run forbids deploys and live writes.
  - Setup per the POML: an existing non-admin test user, in its current BU, whose linked contact holds a DIRECT Collaborate grant on a provisioned, non-Restricted secure root, with no other membership on it. The grant may be added through the product's grant flow and must be revoked afterwards; both are recorded.
  - Run Teams/SPA to-do list and to-do create at least five times within 60 seconds, starting from a cold key. Every call must succeed.
  - Negative half: the same user on an org-only secure root gets 403 on every call.
  - Evidence of a hit: the `tenant:{tid}:external-access-grant:{contactId}:v5` key present in Redis, or the `[EXT-ACCESS] Cache HIT` debug line.
- **Publish size: skipped**, per the run rules; the main session measures once after merging the batch. There is no NuGet change.

## Deviations

- **Branch:** the worktree was created at master `b8fc4dc3e`, which does not contain this POML. `task/uac-r2-131` was fast-forwarded to `work/unified-access-control-r2` (`bd7ce8b55`, a descendant of that commit) before any work.
- **Step 8 (deploy and live gate) and step 9's TASK-INDEX update** were not done: deploys are forbidden, and the main session owns TASK-INDEX and current-task.
- **`entity-schema.md:256`** was corrected in addition to the POML's listed doc, because it is the same stale fact.
