# ISS-010 Part D1: one answer per request for business-unit and team configuration reads (design draft, 2026-11-26)

> Status: DRAFT for direction from uac-r2 and field-mapping-r1 (their files). Not built.
> Source: a read-only code-mapper trace on 2026-11-26 (static, per-commit counts estimated from call sites). It has not been profiled; the profile harness (`DecisionCommitCallProfile`) adds the measured counts when this is built.

## 1. Problem
A four-action decision commit (Create budget, Assign Task, Add To Do, Assign Work) reads the same few business-unit and team rows again and again through the app-only SDK client (`IGenericEntityService`, about 90 ms a call live). The commit writes none of these rows.

| # | Read | Where | Per commit (est.) | Distinct |
|---|---|---|---|---|
| R1 | `businessunit` by name (`SecureRecord:BusinessUnitName`) | `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync` (`SecureRecordOwnerTeam.cs:368-374`); 7 call sites in `RecordOwnershipResolver.cs`, plus share sync and budget currency | 8–14 | 1 |
| R2 | `team`, the secure owner team (BU + name + type, not default) | `SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync` (`:1360-1380`), from `SyncChildAsync` (`RecordCreationService.cs:2467`) and `:527` | 4–8 | 1 |
| R3 | `team` by id: "can this team be the secure owner" | `SecureChildShareSynchronizer.cs:1407-1430` | 2–6 | 1–2 |
| R4 | `team`, the BU's default owner team | `RecordOwnershipResolver.ResolveDefaultOwnerTeamAsync` (`:5506-5550`) | 4–8 | 1–2 |
| R7 | `systemuser` by AAD object id, reading `businessunitid` | `RecordOwnershipResolver.cs:5314-5323`, `RecordContainerResolver.cs:2560-2572` | 4–8 | 1 |

The estimated saving is 14–33 calls, about 1.3–3 s live, excluding R7. With R7 it is 17–40.

**Out of scope, never memoised (ADR-009):** the caller's Deep-depth BU test (`CallerWriteAuthorization.cs:700`), `RetrievePrincipalAccess` and `RetrieveUserSetOfPrivilegesByNames` (those are Part C's), and the caller-scoped matter read in `DecisionParameterSources.cs:375`. Budget currency (R5) is already memoised per request (`DecisionParameterSources._resolvedCurrencies`). The email post-send reads (R10) are left to Part E.

## 2. Proposal: extend `RecordOwnerLookupCache` and attach one per request
The nearest existing component is `RecordOwnerLookupCache` (`RecordOwnershipResolver.cs:289-321`). It already holds `SecureBusinessUnit` (R1) and `DefaultOwnerTeams` (R4), and the resolver already reads it. Today only the reconciler creates one (`SecureRootInheritance.Cascade.cs:2093`). The create path leaves `RecordOwnershipContext.LookupCache` unset (`RecordCreationService.cs:1469`).

1. **Scope.** Carry it per request in `HttpContext.Items` under a private key, built only by `ChildRecordEndpoints`. This copies `CallerMetadataReadMemo.RequestScope` from #3193, including its single-caller ArchTest guard. `RecordCreationRequest` passes it through (like `MetadataReads`), and `RecordCreationService` sets `RecordOwnershipContext.LookupCache` from it. Off-route creates (jobs, reconciler) are unchanged. The reconciler keeps building its own cache per run.
2. **Thread safety.** The cache is not thread-safe today. Make the per-request instance safe for the commit's sequential use and for any parallel step. Store a `Lazy<Task<T>>` or a `ConcurrentDictionary` entry per key, so concurrent first reads issue one call. The reconciler gets the same safety for free.
3. **Add R2 and R3.** Add `SecureOwnerTeam` (R2, one per request) and a per-team-id "can be secure owner" answer (R3). `SecureChildShareSynchronizer`'s static readers take the cache as an optional parameter. A null cache keeps today's behaviour.
4. **What is kept.** Only successful, non-empty answers. A miss or a fault is read again next time, so a transient fault is never pinned for the rest of the request. The configuration keys are constant within a request.
5. **R7 (acting user's BU).** Optional, keyed by AAD object id. It is the caller's own user row, not an access answer. Include it only if uac-r2 agrees.

## 3. Safety argument
- **No cross-request or cross-tenant sharing.** The store dies with the request, and the BFF serves one Dataverse org per deployment.
- **No access answer is kept.** R1–R4 are routing facts (which BU, which team becomes `ownerid`). R3 feeds share decisions, so it is borderline; see Q2.
- **No stale read inside the commit.** No commit step writes `businessunit` or `team` rows. To verify while building: does any create path auto-provision a missing secure owner team (`ProveUnoccupiedAsync` is in provisioning, not the commit)? See Q3.

## 4. Questions for the owners
- **Q1 (uac-r2, field-mapping-r1): placement.** Extend `RecordOwnerLookupCache` with a per-request instance, as proposed? The alternative is a memo behind the `IGenericEntityService` boundary, keyed by query. That is wider, because it would cover every app-only read, and harder to bound.
- **Q2 (uac-r2): is R3 safe to keep for one request?** "Can this team be the secure owner" feeds the share sync.
- **Q3 (uac-r2): can anything in a decision commit create or change a `team` or `businessunit` row?** If yes, that path must clear the cache.
- **Q4 (uac-r2): R7 in or out?**
- **Q5 (field-mapping-r1): `RecordOwnershipResolver` and `RecordCreationService` are your files.** Do you agree with adding the optional cache field? Who reviews?

## 5. Tests (when built)
- **Must fire:** the decision-commit profile shows R1/R2/R4 read once per request. It fails if the cache is per create.
- **Must not fire:** two requests share nothing. A fault on the first read is not kept (the second read goes to the wire). An off-route create and the reconciler behave as today.
- **ArchTest:** the per-request cache is built only in `ChildRecordEndpoints`, with must-fire and must-not-fire controls.

## 6. Order
After #3193 merges, because it reuses #3193's `RequestScope` plumbing on the same route. It is independent of Part C (C wraps the caller's `IDataverseUserClient`; D1 covers app-only reads), but both touch `RecordCreationService` and `ChildRecordEndpoints`. Build one after the other on separate branches, never in parallel.
