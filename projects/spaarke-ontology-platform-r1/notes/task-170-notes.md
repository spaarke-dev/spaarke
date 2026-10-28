# Task 170 — faster decision commit: per-request caller identity and cached entity-set names (D-196)

Branch `task/170-commit-latency` from `origin/docs/ontology-platform-design`. Rigor FULL (opus @ high).

## Measured source (App Insights / Log Analytics `spe-logs-dev-67e2xz`, operation `855d86446126417c11e942af2542d358`, 2026-10-28 15:10:51Z, 24.0 s)

| Call class | Count | Time | Where from |
|---|---|---|---|
| `WhoAmI()` | 35 | 3.0 s | 27 from `CallerRecordAccessProbe.ResolveCallerSystemUserIdAsync` (every `GetCallerRightsAsync` / `CallerHoldsPrivilegeAsync` / `GetCallerSystemUserIdAsync`); 8 via `IDataverseUserClient` (`CallerWriteAuthorization.WhoAmIAsync` in the child-records create/update cores) |
| WCF `RetrieveEntityRequest` (entity set) | 6 | 1.8 s (0.08–0.79 s each) | `DataverseServiceClientImpl.GetEntitySetNameAsync` — `sprk_matter` each time (DecisionCommitService `CallerHoldsOnAsync` in preflight and execute, executors) |
| OBO `EntityDefinitions(...)` | 34 | 3.1 s | `CallerWriteAuthorization` (Privileges, IsSecured attributes, EntitySetName, OwnershipType) + `ChildRecordEndpoints:465` + `DataverseWriteItemMapper` — child-records cores (field-mapping-server-write-path-r1 / uac-r2) |
| `RetrievePrincipalAccess` | 29 (3 × 400) | 2.6 s | probe (executors' preflight + execute, FR-51) and `CallerWriteAuthorization.RightsOnAsync` |
| `RetrieveUserSetOfPrivilegesByNames` | 15 | 1.2 s | probe `CallerHoldsPrivilegeAsync` (executors) + `CallerWriteAuthorization` |
| OBO `sprk_recordtype_refs(id)` | 6 | 0.5 s | `CallerWriteAuthorization.PairTargetAsync` / `CallerMayAppendToOrganizationOwnedAsync` — read AS THE CALLER (an access proof) |
| WCF `sprk_recordtype_ref` (app-only) | 2 | 0.17 s | `SignalCoreRecordAccess` (task 036) and `DecisionCommitService.CoreTableAsync` |

## What changed

1. **`CallerWhoAmIMemo`** (new, `Infrastructure/Dataverse/CallerWhoAmIMemo.cs`): the caller's successful `WhoAmI()` body, stored in the request's
   `HttpContext.Items`, keyed by SHA-256 of `normalised environment URL + "
" + exact Dataverse token` `WhoAmI` ran under (fix round: uac-r2 condition; the same token string under two orgs is two misses; trim, trailing slash, case-insensitive scheme/host). Per request, per environment and credential, successes only; an ended or recycled request (`ObjectDisposedException`, `InvalidOperationException`) is a miss on read and a no-op on write, and both callers re-read the current request at the point of use; static class with
   no data of its own; no DI registration. Outside a request nothing is remembered. Each answer is held as the parsed id plus the body
   as text; every body read gets its own freshly parsed copy (a shared `JsonDocument` is not safe for parallel readers, and one request
   asks in parallel), and reads of `Items` take the same lock as the write.
2. **`CallerRecordAccessProbe`** (uac-r2-owned, own commit): production `ResolveCallerSystemUserIdAsync` reads/writes the memo; the HTTP moved verbatim
   into a new `protected virtual SendWhoAmIRequestAsync` wire seam (task 064's `SendPrincipalAccessRequestAsync` precedent). No constructor change
   (6 Moq doubles pass exact argument lists); the memo reads the request through `HttpContextAccessor`'s shared per-request slot.
3. **`DataverseUserClient`** (shared infra, moved by uac-r2 task 126; own commit): a GET of `WhoAmI`/`WhoAmI()` is answered from the memo when the same
   token already has an answer; a successful one is remembered. Full body kept (the reporting route reads `BusinessUnitId`).
4. **`DataverseServiceClientImpl.GetEntitySetNameAsync`**: process-wide (singleton) `ConcurrentDictionary` of resolved names, ordinal key, null/empty
   and failures not kept, each entry good for at most 15 minutes (`EntitySetNameLifetime`, ADR-009 metadata row; internal `EntitySetNameClock` test seam, constructor unchanged); WCF read extracted to `protected virtual RetrieveEntitySetNameAsync` (test seam). Mirrors `DataverseWebApiService._entitySetNameCache`.

Access results are not cached: `RetrievePrincipalAccess`, privilege checks and every record read are asked as before. No DecisionCommitService code changed.

## Before → after (call counts)

| | Before | After |
|---|---|---|
| `WhoAmI` per commit (production, ESTIMATE, not measured) | 35 | est. 1 if the probe and the user client hold the same MSAL-cached OBO token; at most 2 if they hold different ones. The test proves only the commit service's own 2 → 1; the production figure depends on that token equality and is checked post-deploy (below) |
| `WhoAmI` per commit (test rig, `T170_OneCommit_AsksWhoAmIOnce…`) | 2 | 1 |
| WCF entity-set lookups for a resolved table | every call (6 in the sample) | 0 after the first per process |
| Estimated wall time saved (warm process) | — | ≈ 2.9 s (WhoAmI) + ≈ 1.8 s (entity set) ≈ 4.7 s of 24.0 s — just under the 5 s target; not measured live (no deploy in this task) |

## Tests (fail before → pass after)

Proved by running the new tests against the same seams with the memo and the cache bypassed: 10 failed / 9 passed (the 9 are the must-not-fire
controls whose answer does not change: two requests ask twice, background work asks every time, the `IsWhoAmI` matcher, an empty name). After: 19/19.
- `tests/integration/auth/Signals/CallerWhoAmIMemoIsolationTests.cs` — one request asks once and still asks every rights question; **two callers in
  parallel** (both WhoAmIs in flight together) each get their own id and principal; same caller in two requests asks twice; **app-only vs OBO**
  credentials in one request never mixed; a failed answer (503, empty id, no id) is not remembered; no request → nothing remembered; full body kept;
  only `GET WhoAmI`/`WhoAmI()` matched.
- `tests/unit/domain/Dataverse/EntitySetNameCacheTests.cs` — once per table; failure and empty name not remembered; **`AnEntryOlderThanTheAdr009Lifetime_IsAskedAgain_AFresherOneIsNot`** (15-minute lifetime on an injected clock) and `TheLifetime_IsNoMoreThanTheAdr009MetadataCeiling`.
- Fix round (PR #2404 review): `TheSameToken_UnderTwoEnvironments_IsTwoMisses_AndEachEnvironmentThenHitsItsOwnAnswer`, `TheSameEnvironment_WrittenWithAndWithoutATrailingSlash_OrInADifferentCase_SharesOneAnswer`, `ARequestThatHasEnded_IsAMiss_AndAWrite_IsANoOp_NeverAThrow_TheQuestionStillAsksDataverse` (a context whose `Items` throws `ObjectDisposedException` / `InvalidOperationException`; proved failing with the catch disabled). `TheUserClientsWhoAmI_…` was renamed `TheMemo_StoresTheFullWhoAmIBody_…`: it tests the memo's stored body, not `DataverseUserClient`.
- `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.CallerIdentityCount.cs` — real commit service + shipped probe (wire seams): one
  WhoAmI, every rights question asked. Rig change: class made `partial`, optional `ProbeOverride` (existing tests untouched).

Runs (rebased on 2260916112): new tests 19/19; filtered `Sprk.Bff.Api.Tests` (DecisionCommit|Signals|CallerRecordAccessProbe|Dataverse|Probe|
ExternalAccess|UnifiedAccessControl|Ontology) 8907 passed / 0 failed / 2 skipped; `Spaarke.ArchTests` 1593/1593.

Test scope beyond the stated contract: none.

## The three 400s (constraint "400s") — not ours; documented for the main session to file

Source: `src/server/api/Sprk.Bff.Api/Services/Dataverse/CallerWriteAuthorization.cs:333` (`CheckCallerMayAppendToAsync` → `RightsOnAsync`, the GET at
`:569`) at `origin/docs/ontology-platform-design` 36372fb8fb. For each child-records create/update whose ADR-024 regarding pair is typed (budget,
event, To Do in the sample) the pair target `sprk_recordtype_ref` is organization-owned; Dataverse refuses `RetrievePrincipalAccess` on it with
400 `0x80040800` ("does not support entities of type 'sprk_recordtype_ref'"), and the code then falls back to `CallerMayAppendToByPrivilegeAsync`
(`EntityDefinitions` OwnershipType/Privileges → `RetrieveUserSetOfPrivilegesByNames` → row read as the caller). Each 400 is immediately followed by
exactly that sequence in the trace. The behaviour is correct and documented in the code comment at `:335-342`; the cost is 3 × ~90 ms plus three
failed dependencies per commit in telemetry. Owner: field-mapping-server-write-path-r1 (moved the code) under uac-r2's rule. Fix approach: read the
target's `OwnershipType` (cacheable metadata) first and skip `RetrievePrincipalAccess` for organization- and business-owned targets. Done when:
no 400 on `RetrievePrincipalAccess` during a commit with a typed pair, and the AppendTo answers are unchanged (their existing tests).

## Deviations

- **No `sprk_recordtype_ref` cache in `DecisionCommitService.CoreTableAsync`** (allowed by the POML, not required by its goal or acceptance criteria).
  A process-wide cache there makes two refusal paths unreachable on a warm process — the 503 on a catalog read fault and the 422 for a deleted
  catalog row — which the D-17 constraint says stay the same, and it breaks `UacF4b_ACoreTypeReadFault_Is503_AMissingCoreType_Is422` depending on
  test order. It would save one ~85 ms read per commit. The six OBO `sprk_recordtype_refs(id)` reads are the caller's access proof
  (`CallerWriteAuthorization`), so they are not cacheable without changing access semantics.
- **K3: DataverseUserClient's memo hit (and its ended-request tolerance) is not driven end to end by a test**: reaching it needs a real MSAL OBO exchange. Its logic is in
  `CallerWhoAmIMemo` (`IsWhoAmI`, `TryGetBody`, `Remember`), which is tested; the wiring is four lines. (K3: wiring of a tested helper.)

## ADR-009: path C (comply)

Both caches sit inside ADR-009's "Allowed L1 Exceptions" table, so no exception or amendment is needed.
- **The caller-WhoAmI memo** is "Per-request (`HttpContext.Items`) | Request | Always OK". Its lifetime is the request's, it is absent outside a
  request, and it takes a lock around `Items` (a plain dictionary) so parallel asks inside one request are safe. (`RequestCache` was considered and
  not used: it is not thread-safe, and a consumer resolved from a singleton or a job scope would carry one across requests; the probe's constructor is
  pinned by six Moq doubles.)
- **The entity-set-name cache** is "Metadata (entity definitions) | <=15 min | Document in code". Each entry lives at most 15 minutes
  (`DataverseServiceClientImpl.EntitySetNameLifetime`) and the code comment cites the row.
- **"MUST NOT cache authorization decisions" holds.** Only identity (who the caller is) is remembered. `RetrievePrincipalAccess`,
  `RetrieveUserSetOfPrivilegesByNames` (RUSOP) and every record read are asked every time. The memo is keyed by environment AND token, so another
  caller, the app-only credential, or another organisation never receives an answer.

## Post-deploy done-when

In App Insights, for one decision commit (one operation id): at most 2 `WhoAmI` dependencies (1 if the probe and the user client hold the same OBO
token; 2 if not), and record the commit's wall time against the 24.0 s sample.

## Remaining known limits (K)

- **K1** The memo lives as long as the request. A very long request (an SSE stream) keeps a caller identity for its whole length. Identity does not
  change within a token's subject, and the token is part of the key, so this is an accepted limit; access answers are not remembered.
- **K3** `DataverseUserClient` is not driven end to end (see Deviations): the wiring is tested only through `CallerWhoAmIMemo` and the probe.
- **K4** An entity-set rename is picked up within 15 minutes (the ADR-009 ceiling), not immediately.
- **K4** The `Items` lock only protects code that takes it. The memo takes it on every read and write, but any other code writing `HttpContext.Items`
  directly does not use the memo's key, so cannot corrupt it (the key is a private object).

## Remaining sinks — proposals (for uac-r2 / field-mapping review; not done here)

| # | Sink (sample) | Proposal | Est. saving |
|---|---|---|---|
| P1 | 34 OBO `EntityDefinitions` reads, 3.1 s (`CallerWriteAuthorization`, `ChildRecordEndpoints:465`, `DataverseWriteItemMapper`) | Process-wide cache of `EntitySetName`, `PrimaryIdAttribute`, `OwnershipType`, `Privileges` by logical name (immutable). Keep `IsSecured` attribute reads uncached (a newly secured column must take effect). | ≈ 2.5 s |
| P2 | **Declined by uac-r2 (2026-10-28): access can change mid-request; TOCTOU.** 29 `RetrievePrincipalAccess` (2.6 s; `sprk_matters(<same id>)` asked 10×, `contacts(<same id>)` 6×) + 15 RUSOP (1.2 s; `prvCreatesprk_Budget` 2× etc.) | Per-request memo of access answers keyed by (credential, entity set, id) / (credential, privilege). Changes when an access answer is re-read inside one request — uac-r2's decision. | ≈ 2.5–3 s |
| P3 | **Declined by uac-r2 (2026-10-28): access can change mid-request; TOCTOU.** Preflight then execute re-ask the same rights (`CallerMayDecideAsync` at `DecisionCommitService.cs:816` and `:978`; executors' preflight and execute) | Carry the preflight answers into execute within the request (same caller, same request); TOCTOU window is the review question. | ≈ 1 s |
| P4 | **Acceptable to uac-r2 if bounded, fail-closed for the whole create, and deterministically ordered.** Serial per-create scans: 6–9 WCF `RetrieveMultiple` per child create (todo/event/communication/analysis/memo/signal/decisionrecord, ~0.12 s each; secure-child / core-ancestor reconciliation) | Run the independent reads in parallel (bounded) or one batch request. | ≈ 1.5–2 s |
| P5 | The three 400s above | Ownership-first AppendTo (see "The three 400s"). | ≈ 0.27 s |
| P6 | `sprk_recordtype_ref` app-only reads (2) | Cache by id only if the 503/422 refusal paths are explicitly accepted as unreachable when warm (owner/uac-r2). | ≈ 0.17 s |

## Placement (bff-extensions §A)

In the BFF: the change removes repeated round trips inside the existing synchronous commit route and the shared Dataverse client; nothing new runs
outside a request, so ADR-052 placement does not apply. No endpoint, DI registration, package or background work added (ADR-010: static helper, no
registration).

## Publish size and packages (§10)

`dotnet publish -c Release` of `origin/docs/ontology-platform-design` 2260916112 (fresh worktree `C:\wts-170b`) vs this branch, each zipped
with `Compress-Archive`: raw 132,477,704 → 132,484,312 B (**+6,608 B**), zip 39,736,827 → 39,738,541 B (**+1,714 B**), 192 → 192 files.
`Sprk.Bff.Api.dll` +4,608 B, `Spaarke.Dataverse.dll` +512 B. Total ≈ 39.7 MB zipped (< 55 MB review line). No package added;
`dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
