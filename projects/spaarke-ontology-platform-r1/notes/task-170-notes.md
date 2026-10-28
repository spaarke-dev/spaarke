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
   `HttpContext.Items`, keyed by SHA-256 of the exact Dataverse token `WhoAmI` ran under. Per request, per credential, successes only; static class with
   no data of its own; no DI registration. Outside a request nothing is remembered. Each answer is held as the parsed id plus the body
   as text; every body read gets its own freshly parsed copy (a shared `JsonDocument` is not safe for parallel readers, and one request
   asks in parallel), and reads of `Items` take the same lock as the write.
2. **`CallerRecordAccessProbe`** (uac-r2-owned, own commit): production `ResolveCallerSystemUserIdAsync` reads/writes the memo; the HTTP moved verbatim
   into a new `protected virtual SendWhoAmIRequestAsync` wire seam (task 064's `SendPrincipalAccessRequestAsync` precedent). No constructor change
   (6 Moq doubles pass exact argument lists); the memo reads the request through `HttpContextAccessor`'s shared per-request slot.
3. **`DataverseUserClient`** (shared infra, moved by uac-r2 task 126; own commit): a GET of `WhoAmI`/`WhoAmI()` is answered from the memo when the same
   token already has an answer; a successful one is remembered. Full body kept (the reporting route reads `BusinessUnitId`).
4. **`DataverseServiceClientImpl.GetEntitySetNameAsync`**: process-wide (singleton) `ConcurrentDictionary` of resolved names, ordinal key, null/empty
   and failures not kept; WCF read extracted to `protected virtual RetrieveEntitySetNameAsync` (test seam). Mirrors `DataverseWebApiService._entitySetNameCache`.

Access results are not cached: `RetrievePrincipalAccess`, privilege checks and every record read are asked as before. No DecisionCommitService code changed.

## Before → after (call counts)

| | Before | After |
|---|---|---|
| `WhoAmI` per commit (production, sample) | 35 | 1 (probe and user client share the MSAL-cached OBO token → one answer); worst case 2 if the two ever hold different tokens |
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
- `tests/unit/domain/Dataverse/EntitySetNameCacheTests.cs` — once per table; failure and empty name not remembered.
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
- **DataverseUserClient's memo hit is not driven end to end by a test**: reaching it needs a real MSAL OBO exchange. Its logic is in
  `CallerWhoAmIMemo` (`IsWhoAmI`, `TryGetBody`, `Remember`), which is tested; the wiring is four lines. (K3: wiring of a tested helper.)

## ADR tension — ADR-009 "MUST use `RequestCache` for within-request de-dupe" (🔔 for the main session / reviewer: path choice)

The memo stores in `HttpContext.Items`, not `RequestCache`. Proposed path **A (project exception)**; alternative B (amend ADR-009 to
"`RequestCache`, or `HttpContext.Items` when the caller is not request-scoped"). Rationale:
- The two readers are transient typed `HttpClient`s whose lifetime follows their consumer. Today's consumers are scoped (DecisionCommitService,
  the executors, Office, Compose), so an injected `RequestCache` would be per request there; but a consumer resolved in a background job's
  scope or from a singleton would carry one `RequestCache` across a whole job or across requests. `HttpContext.Items` cannot outlive the
  request and is absent outside one, so the memo is off in background work: strictly narrower than `RequestCache`.
- `RequestCache` is a plain `Dictionary` (not thread-safe), and nothing stops one request from asking from parallel tasks (the probe itself
  fans out with `Task.WhenAll`, `CallerRecordAccessProbe.cs:390`).
- Adding a constructor parameter to the probe breaks the six Moq doubles that pass an exact argument list.
- `HttpContext.Items` has the identical lifetime and isolation `RequestCache` exists to give: created with the request, dropped with it.
Rejected: C (comply with `RequestCache`) — needs the probe's constructor changed and a thread-safe wrapper, for no isolation gain.
Other ADR-009 rules: identity is not an authorization decision, and no access answer is cached ("MUST NOT cache authorization
decisions" holds). The entity-set cache is in-process metadata with profiling proof (6 lookups, 1.8 s in one commit) and follows the
`DataverseWebApiService._entitySetNameCache` precedent; no `IMemoryCache`.

## Remaining sinks — proposals (for uac-r2 / field-mapping review; not done here)

| # | Sink (sample) | Proposal | Est. saving |
|---|---|---|---|
| P1 | 34 OBO `EntityDefinitions` reads, 3.1 s (`CallerWriteAuthorization`, `ChildRecordEndpoints:465`, `DataverseWriteItemMapper`) | Process-wide cache of `EntitySetName`, `PrimaryIdAttribute`, `OwnershipType`, `Privileges` by logical name (immutable). Keep `IsSecured` attribute reads uncached (a newly secured column must take effect). | ≈ 2.5 s |
| P2 | 29 `RetrievePrincipalAccess` (2.6 s; `sprk_matters(<same id>)` asked 10×, `contacts(<same id>)` 6×) + 15 RUSOP (1.2 s; `prvCreatesprk_Budget` 2× etc.) | Per-request memo of access answers keyed by (credential, entity set, id) / (credential, privilege). Changes when an access answer is re-read inside one request — uac-r2's decision. | ≈ 2.5–3 s |
| P3 | Preflight then execute re-ask the same rights (`CallerMayDecideAsync` at `DecisionCommitService.cs:816` and `:978`; executors' preflight and execute) | Carry the preflight answers into execute within the request (same caller, same request); TOCTOU window is the review question. | ≈ 1 s |
| P4 | Serial per-create scans: 6–9 WCF `RetrieveMultiple` per child create (todo/event/communication/analysis/memo/signal/decisionrecord, ~0.12 s each; secure-child / core-ancestor reconciliation) | Run the independent reads in parallel (bounded) or one batch request. | ≈ 1.5–2 s |
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
