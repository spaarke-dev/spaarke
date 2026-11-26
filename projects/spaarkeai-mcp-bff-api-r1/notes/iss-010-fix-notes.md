# ISS-010 (#3184), part 1: profile and request-scoped metadata memo (2026-11-25)

> Branch `perf/decision-commit-latency`. Read with `iss-010-commit-latency.md` (live measurement, code map, reviewers' constraints; on `work/spaarkeai-mcp-bff-api-r1`).
> Done here: **A** (profile harness + equivalence tests) and **B** (request-scoped metadata memo). **Not done:** C (pre-write access memo; waits for a design with uac-r2) and D (the other repeats below). The Assign Task email is reported, not moved (owner decision).

## The harness (A)

`tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.DecisionCommitLatency.cs` runs the live four-action decision (Create budget, Assign Task, Add To Do, Assign Work) through the real `DecisionCommitService`, the four real executors, the real route core (`ChildRecordEndpoints.CreateAsync`), the real `RecordCreationService`, owner resolver, core-ancestor restamper, share synchronizer and secure-root gate, and the shipped `CallerRecordAccessProbe` with only its wire answered.

`tests/integration/data-mutation/Signals/DecisionCommitCallProfile.cs` counts every call at the faked boundary:
- SDK calls: by interface method and table (`DataverseServiceClientImpl` keeps its `ServiceClient` private; one method is one SDK request).
- Caller calls: by Web API path.
- For each call, the production method that made it and the decision step it ran in.

Set `ISS010_PROFILE_OUT=<file>` to write the full per-site table.

**Not modelled** (stubbed at their seam, so not counted):
- the Assign Task email: `DecisionCommitFollowUps.NotifyAssigneesAsync` (recipient gate, send, archive);
- `AfterRecordedAsync` (suppression, evaluation trigger);
- the "Assigned *" grants (`AssignedAccessMaterializer`: the create's scopes have none registered);
- the budget currency read (`DecisionParameterSources`);
- the OBO exchange.

These are the most likely home of the live SDK calls the harness does not reach (live 113, harness 49).

## Profile: the four-action decision, per kind (before → after B)

| Kind | Live (App Insights) | Harness before | Harness after |
|---|---|---|---|
| `EntityDefinitions(…)` reads (incl. `/Attributes`) | ~40 | 37 (21 distinct paths) | **25** (21 distinct) |
| `sprk_recordtype_refs(id)` reads | 8 | 8 | 8 |
| `RetrievePrincipalAccess` (probe + route core) | 51 together | 32 | 32 |
| `RetrieveUserSetOfPrivilegesByNames` (probe + route core) | (in the 51) | 18 | 18 |
| `WhoAmI` | memoised | 6 (route core's 4 go through the scripted client, not the real `DataverseUserClient` memo) | 6 |
| SDK side (app-only reads + writer) | 113 | 49 (6 writes) | 49 |
| All calls | ~220 | 152 | 140 |

The caller-access, metadata and `sprk_recordtype_refs` counts match the live trace, which validates those parts of the model. The SDK block does not match (see "Not modelled").

## Profile per site (before → after B)

| Seam | Target | Site | Before | After |
|---|---|---|---|---|
| caller (route core) | EntityDefinitions | `DataverseWriteItemMapper.MapCoreAsync` | 16 | 9 |
| caller (route core) | EntityDefinitions | `CallerWriteAuthorization.NoFieldSecuredColumnAsync` | 5 | 5 |
| caller (route core) | EntityDefinitions | `CallerWriteAuthorization.CallerMayCreateInTableAsync` | 4 | 4 |
| caller (route core) | EntityDefinitions | `CallerWriteAuthorization.CallerMayAppendToByPrivilegeAsync` | 4 | 2 |
| caller (route core) | EntityDefinitions | `DataverseWriteItemMapper.MapWebApiPayloadAsync` | 4 | 4 |
| caller (route core) | EntityDefinitions | `CallerWriteAuthorization.EntitySetOfAsync` | 3 | 0 |
| caller (route core) | EntityDefinitions | `RecordCreationService.TargetAttributesAsync` | 1 | 1 |
| caller (route core) | RetrievePrincipalAccess | `CallerWriteAuthorization.RightsOnAsync` | 15 | 15 |
| caller (route core) | RetrieveUserSetOfPrivilegesByNames | `CallerWriteAuthorization.CallerMayCreateInTableAsync` | 5 | 5 |
| caller (route core) | RetrieveUserSetOfPrivilegesByNames | `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` | 5 | 5 |
| caller (route core) | `sprk_recordtype_refs(id)` | `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` | 4 | 4 |
| caller (route core) | `sprk_recordtype_refs(id)` | `CallerWriteAuthorization.PairTypeTableAsync` | 4 | 4 |
| caller (route core) | `transactioncurrencies(id)` | `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` | 1 | 1 |
| caller (route core) | `WhoAmI()` | `CallerWriteAuthorization.WhoAmIAsync` | 4 | 4 |
| probe | RetrievePrincipalAccess | `DecisionExec.CallerHoldsAsync` (preflight 12 + execute 12 − shared) | 17 | 17 |
| probe | RetrieveUserSetOfPrivilegesByNames | `DecisionExec.CallerHoldsPrivilegeAsync` (preflight 4 + execute 4) | 8 | 8 |
| probe | WhoAmI | `DecisionCommitService.CommitCoreAsync` | 1 | 1 |
| owner resolver | `sprk_matter` / `businessunit` / `team` queries | `RecordOwnershipResolver.ReadParentAsync`, `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync`, `ReadDefaultOwnerTeamAsync` | 4 + 4 + 4 | same |
| share sync | `businessunit` / `team` / child queries | `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync`, `ResolveSecureOwnerTeamAsync`, `Run.LoadSecureChildAsync` | 3 + 3 + 3 | same |
| core-ancestor stamp | child row | `CoreAncestorResolver.DeriveStampsAsync`, `CoreAncestorRestamper.RestampChildAsync` | 5 | 5 |
| record creation | budget / event row | `RecordCreationService.WarnIfNumberMissingAsync` | 2 | 2 |
| secure-root gate | `sprk_matter` query | `SecureRootInheritance.ReadParentAsync` | 1 | 1 |
| executor | entity set name | `ChildCreateExecutorBase.FileUnderAsync` / `CreateBudgetExecutor.FileUnderAsync` | 4 | 4 |
| commit (own reads) | Signal, record type, matter, policy version, budget count, Decision Record lookup | `SignalCoreRecordAccess.*`, `DecisionPlanService.GetAsync`, `DecisionCommitService.FindRecordAsync`, `CoreAncestorResolver.CoreTableOfRecordTypeAsync`, `DecisionCallerRights.CallerHoldsOnAsync` | 9 | 9 |
| writes | budget, event, to-do, work assignment (app-only); Decision Record; Signal close | `RecordCreationService.CreateAsync` ×4, `OntologyWriterDataverseClient.CreateAsync`, `UpdateIfRowVersionMatchesAsync` | 6 | 6 |
| writer | `WhoAmI`, Signal page reads | `OntologyWriterDataverseClient.*` | 3 | 3 |

## Simulated totals (the four-action decision, modelled calls only)

| | Calls | 20 ms per call: serial sum, measured | 100 ms per call: serial sum |
|---|---|---|---|
| Before B | 155 | 3,100 ms, 3,826 ms | 15.5 s |
| After B | 141 | 2,820 ms, 3,493 ms | 14.1 s |

- The calls run almost entirely in series: measured time ≈ serial sum + CPU.
- Live, B removes 12 metadata reads at about 88 ms each: about 1.1 s of the 26.9 s.

## Fix B: what changed, and why it is safe

- `CallerMetadataReadMemo.For(user, scope)` replaces `new CallerMetadataReadMemo(user)` at the three caller-check sites in `RecordCreationService`:
  - steps 2–3 (`DecideAsync`);
  - step 5 (`DecideAsync`);
  - the type-only pair rewrite (`CompleteTypeOnlyRegardingPairAsync`, which gains a `metadataReads` parameter).
- `ChildRecordEndpoints.CreateAsync` sets `RecordCreationRequest.MetadataReads = CallerMetadataReadMemo.RequestScope.Of(httpContext)`. The decision commit's creates run through this route core, in-process, in the commit's own request.
- With a scope, every memo of that request and that caller client shares one store, and a row write does not clear it: a row write does not change a schema.
- Without a scope (the chat tool, jobs, every other caller of the service), the memo is per instance and clears on any write, exactly as before.

**What is kept is unchanged:** only a successful GET whose path starts with `EntityDefinitions(` (with `/Attributes`).

**Asked every time, as before (ADR-009):**
- row reads;
- `sprk_recordtype_refs` rows;
- `RetrievePrincipalAccess` and `RetrieveUserSetOfPrivilegesByNames`;
- failures.

**Scope:** the store lives in `HttpContext.Items` under a private key, keyed per caller-client instance (`ConditionalWeakTable`). Nothing is shared across requests or callers, and there is no process-wide cache.

**Equivalence:** the A tests were committed first and pass unchanged after B. They pin:
- the writes in order with their payloads, the Decision Record last;
- the response, including the follow-up warning;
- the replay (writes nothing);
- an access refusal before and after a write;
- a check fault before and after a write (never an allow);
- a metadata-read fault on the first read.

**Must not change, and did not:** D-17, execution order, validation, the response shape and warnings, the Signals contract baselines, and every REST response.

## Access methods touched (for uac-r2)

None. B changes no code in `CallerRecordAccessProbe` or `CallerWriteAuthorization`. It changes only which metadata answers their callers' memo serves: `CallerWriteAuthorization.CallerMayCreateInTableAsync`, `CallerMayAppendToByPrivilegeAsync`, `EntitySetOfAsync` and `NoFieldSecuredColumnAsync` now receive the request's metadata answers, and still ask every access question.

## The Assign Task email

- **Live:** ~3 s of 26.9 s (~11%): Key Vault footer secret, token, `sendMail`, sent-items read, SPE `.eml` PUT.
- **AI:** ~1.3 s more (~5%), most likely the queued `.eml` analysis sharing the operation id.
- **Harness:** not measured (the follow-up seam is stubbed).
- Moving it off the request drops "not sent" outcomes from the response, so it stays an owner decision.

## D candidates from the profile (not done)

1. **The work assignment's re-map reads metadata on the caller's own client.** The 4 remaining metadata repeats are all assign-work's `DataverseWriteItemMapper.MapCoreAsync` / `MapWebApiPayloadAsync` (the step that, by design, keeps the caller's own client).
2. **The secure business unit and owner team are looked up on every create:**
   - owner resolver: `businessunit` ×4 and `team` ×4;
   - share sync: `businessunit` ×3 and `team` ×3.

   These are configuration rows, not access answers. A request-scoped memo is a candidate, but it is not metadata, so it needs its own decision.
3. **Extend the harness before fixing the unmodelled SDK block:** `AssignedAccessMaterializer` (4 after-write runs) and `DecisionCommitFollowUps.AfterRecordedAsync` / `NotifyAssigneesAsync`. Then re-profile against the live 113.
4. **C (uac-r2 design): access repeats.**
   - Probe preflight vs execute: 12 repeated questions, all before the step's own write. Each step's execute re-asks its preflight.
   - Route core: `RightsOnAsync` 15, privileges 10, `sprk_recordtype_refs` 8, including the two passes of step 5 between the same writes.
   - The writes the memo must see are application writes (`IFieldMappingDataverseService`), not the caller client's.
5. **D candidate: cache Dataverse's "table not supported" `RetrievePrincipalAccess` 400.** Request-scoped, keyed on table only, that one error code only; every other 400 stays a fault (uac-r2 2026-11-25).
