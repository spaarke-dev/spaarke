# H8 — the root container is bound to its business unit (from unified-access-control-r2 task 165)

> **Owner of the change:** unified-access-control-r2 task 165 (follow-up round f2, 2026-10-04), under owner round 35 item 1
> ("every container-creation path stamps … the control-plane H8 handler … Task 165 owns all three. The H8 change is
> recorded in customer-provisioning-orchestration-r1's notes for that project.") This note is that record.
> **Branch:** `task/uac-r2-165-f2`, then `task/uac-r2-165-f2-v1` (integrates through `work/unified-access-control-r2`).
> **Round 41 (2026-10-05, BINDING):** "Round 35 item 1 includes H8's replication-pending path, and task 165 fixes it. It is
> not handed to another project." The resume defect this note first recorded as "observed, not changed" is FIXED — see
> "Resume — what H8 created is recorded and reused" below. Nothing in this note is owed by this project.
> **Round 49 (2026-10-05, BINDING) — the f2-v1 fix did NOT hold in production; corrected on `task/uac-r2-165-f2-v2`.**
> Round 41's record kept the root container in the `h8-t6-verified` gate's `JsonElement` evidence, and the Cosmos SDK's
> default (Newtonsoft) serializer stored that as `{"valueKind":1}`: every re-entry found only the type, created a SECOND
> root container and left the first UNBOUND. The tests passed because their fake repository handed back the in-memory
> object. Now: the record is TYPED (`InterStepState.SpeContainerCreation`), the H8 tests persist through the production
> serializer, and no fault after a Graph write leaves an orphan type or an unbound root — see "Round 49" below. The same
> serializer also lost EVERY gate's evidence and camel-cased the reconciler's retry-counter keys; both are fixed here too
> (scope found by the production-serializer test, added under the standing directive).

## Why

The BFF's SPE admin plane authorizes every container, item, permission and bulk route PER CONTAINER against the
container's owning business unit, carried as the `fileStorageContainer` custom property `spaarkeBusinessUnitId`. Since
owner round 35 item 2 an **unbound** container is reached by **no** admin route — not even a root-unit administrator's
(under Model 1 a root admin of any environment whose config names a shared container type would otherwise reach another
customer's unbound containers). H8 created its root container unstamped, so every L2-provisioned customer's default
container would have been invisible to that customer's own administrators until someone ran a backfill.

## What changed in L2

| Surface | Change |
|---|---|
| `Reconciler/DagAdvancer.cs` | `H8` now depends on **`H3` and `H5`** (was `H3` only). H8 needs the environment H5 creates, to read its root business unit. **`H7` now depends on `H6` and `H8`** (round 41): H7 writes H8's root container into `sprk_SharePointEmbeddedContainerId`, which H8 hands off only once BOUND (CompletedPhase H8) — before this edge H7 was dispatched while H8 waited out the 24h replication window. `DagAdvancerTests`: `AfterH3_UnlocksH9_ButH8WaitsForH5`, `AfterH3AndH5_UnlocksH8`, `AfterH6_H7WaitsForH8_WhichHandsOffOnlyABoundContainer`, `AfterH6AndH8_UnlocksH7`. |
| `Handlers/SpeContainerType/H8SpeContainerTypeHandler.cs` | (5b) before ANY external side effect: `InterStepState.DataverseEnvUrl` required (`spe-missing-dataverse-env-url`, Resumable) and the environment's root business unit read through `IDataverseRootBusinessUnitReader` (none / more than one / read fault → `spe-root-business-unit-unresolved`, Resumable — nothing is created). (7c) after the app-only verification, BEFORE the KV write and before the success state hands the container to H7: `ISpeContainerTypeProvisioner.BindRootContainerAsync`. A bind failure is QuarantineRequired — `spe-container-binding-failed` (container removed), `spe-container-binding-failed-not-removed` (removal failed too: bind it with `scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind <containerId>=<rootBusinessUnitId> -Apply` or remove it), `spe-container-binding-infra-fault` (cert load failed before any Graph call; the container exists unbound) — and `InterStepState.SpeContainerId` is NOT set. The T6 gate evidence carries `owningBusinessUnitId`. The replication-pending wait (24h) does not bind (the container may be unaddressable). |
| `ISpeContainerTypeProvisioner` | `+ BindRootContainerAsync(SpeContainerBindRequest)` → `SpeContainerBindOutcome.Bound` / `.NotBound(Diagnostic, Removed)`. Extends the existing Graph seam (same T6 cert identity that created the container) rather than adding a second Graph collaborator. |
| `GraphContainerTypeProvisioner` | `BindRootContainerAsync` (cert → Graph client) and `internal BindNewContainerAsync(graph, containerId, businessUnitId)`: `PATCH /storage/fileStorage/containers/{id}/customProperties` with `{"spaarkeBusinessUnitId":{"value":"<unit D-form>","isSearchable":false}}` as the BODY ROOT (Graph merges), a single-container read-back (`$select=id,customProperties` — the collection drops custom properties), and `DELETE /storage/fileStorage/containers/{id}` when the stamp did not land for any reason (`Guid.Empty` included). The same wire shape as the BFF's `SpeAdminGraphService.WriteBusinessUnitStampAsync`. |
| `Handlers/SpeContainerType/DataverseRootBusinessUnitReader.cs` (new) | `IDataverseRootBusinessUnitReader` + `DataverseWebApiRootBusinessUnitReader`: `GET businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid&$top=2` with `DefaultAzureCredential(TenantId)` — the same query and token idiom as H10's `DataverseWebApiAppUserCreator.FindRootBusinessUnitIdAsync`; two roots are refused (never guess an owner). Registered in `Worker/Program.cs` with `AddHttpClient<…>()`. |
| `Sprk.Provisioning.ControlPlane.Core.csproj` | Compiles `src/server/shared/Contracts/SpeContainerBusinessUnitBinding.cs` (source-linked, `internal`) — the ONE C# constant for the property name, shared with the BFF (L2 cannot reference the BFF or Spaarke.Core). |
| `Handlers/SpeContainerType/H8SpeContainerTypeHandler.cs` (round 41) | **(5c)** reads its own creation record (`ReadRecordedCreation`: `InterStepState.ContainerTypeId` + the `h8-t6-verified` gate's evidence `rootContainerId`) and withdraws any `SpeContainerId` written before completion; a recorded root container → **no `ProvisionAsync`**, resume at (7) with it; only a type recorded → `ProvisionAsync` with `ExistingContainerTypeId` (a root container only); a root container recorded without a type → QuarantineRequired `spe-creation-record-inconsistent`, nothing created. **(6b)** `RecordCreationAsync` persists the record IMMEDIATELY after creation (merged over a concurrent write, up to 5 attempts; a record that cannot be written → QuarantineRequired `spe-creation-record-not-persisted` naming both ids). **(7b)** the replication wait no longer writes `SpeContainerId`. **(7c)** a bind that removed the container keeps the type on record and drops the container. **(9)** `MarkCompleteAsync` is the ONLY writer of `SpeContainerId` (an ArchTest pins it). |
| `ISpeContainerTypeProvisioner` / `GraphContainerTypeProvisioner` (round 41) | `SpeContainerTypeProvisionRequest.ExistingContainerTypeId` (create only a root container in the type this run already made — container types cannot be deleted); `SpeContainerTypeProvisionOutcome.Failure.CreatedContainerTypeId` (the type created before a later step failed, so the handler records it). The Graph part is `internal CreateAsync(graph, request)`, tested over the fake transport. |
| `Handlers/EnvVarValues/H7DataverseEnvVarValuesHandler.cs` (round 41) | Refuses (`MissingUpstreamState`, Resumable) a `SpeContainerId` from a run where H8 has not completed — the handler's own fail-closed check behind the DAG edge. |
| `Models/SpeContainerCreationRecord.cs` (new) + `InterStepState.SpeContainerCreation` (round 49) | H8's creation record as TYPED fields: `rootContainerId`, `additionalContainerIds`, `containerTypeInDoubtSince`, `rootContainerInDoubtSince`, `owningBusinessUnitId` (set on completion), `status`, `updatedAt`. Controlled schema extension (the `ImportedSolutions` / `SpeContainerId` precedent). The type stays `InterStepState.ContainerTypeId`. `ReadRecordedCreation` reads ONLY these typed fields (an ArchTest forbids `Evidence` / `JsonElement` in it); a pre-round-41 run's `SpeContainerId` (its only surviving record — its evidence was lost) is MOVED into the record at (5a). |
| `GraphContainerTypeProvisioner.CreateAsync` (round 49) | Every fault after a Graph write was sent is a `Failure`, never an exception: an `ODataError` is Graph's answer (nothing in doubt); anything else — a client timeout (`LinkedTimeout`'s `OperationCanceledException`), the caller's cancellation, a dropped connection, a 2xx without an id — marks the write in flight `ContainerTypeInDoubt` or `RootContainerInDoubt` and reports the type it created. On a resume with a recorded type it LISTS the type's containers (`$filter=containerTypeId eq …`, every page; 404 = none visible yet) and ADOPTS the oldest (`Adopted`, the others in `AdditionalContainerIds`) before it creates one; with `RootContainerCreationInDoubt` and nothing listed it returns `RootContainerNotYetVisible` and creates nothing. `BindNewContainerAsync`: a DELETE answering 404 is "removed" (already gone). |
| `H8SpeContainerTypeHandler` (round 49) | Records a failure's type / in-doubt state BEFORE anything else (with `CancellationToken.None`). A type in doubt with no recorded type → QuarantineRequired `spe-container-type-creation-in-doubt` (new code): no type is created until an operator checks with a DELEGATED SharePoint Embedded admin token (`GET /storage/fileStorage/containerTypes`), records a type it finds in `interStepState.containerTypeId`, and clears the quarantine (`ClearedAt` after the in-doubt time, by H8's quarantine). A root container in doubt within `RootContainerInDoubtWindow` (24h) → the provisioner must not create (WaitingOnGate if the type lists none). (7d) `BindAdditionalContainersAsync` binds every adopted further container to the root unit (or removes it) before the KV write; one that is neither stays on record and quarantines the run. A bind that removed the root container is recorded with the merge-safe record write. |
| `Models/NewtonsoftJsonElementConverter.cs` (new) on `GateEntry.Evidence` (round 49) | Gate evidence is written as its JSON and read back as a `JsonElement` (date-shaped strings stay strings). Before it, EVERY gate's evidence was stored as `{"valueKind":1}` and read back as an Undefined element that `GET /api/runs/{id}` could not serialize (it threw). Old documents read back as `{"valueKind":1}` — valid. |
| `Models/NewtonsoftVerbatimKeysDictionaryConverter.cs` (new) on `GateStates`, `HandlerRetryAttempts`, `RunParameters.NonSecret` / `.Secrets` (round 49 — scope found) | The SDK's camelCase option (Newtonsoft `CamelCasePropertyNamesContractResolver`, `ProcessDictionaryKeys = true`) LOWERED each dictionary key's first letter: `HandlerRetryAttempts["H9"]` came back as `"h9"`, so `HandlerOutcomeApplier` re-sent attempt 1 on every retry — the same MessageId, dropped by Service Bus duplicate detection (task 107's counter never counted); `RunParameters.NonSecret` documents "persisted verbatim" and was not for an upper-case key. Keys now persist verbatim; the retry counter compares case-insensitively so a run persisted before the fix still counts. |
| `Modules/CosmosModule.cs` (round 49) | `internal static BuildCosmosClient(endpoint, credential)` — THE construction of the client (serializer, connection mode, retries); the DI registration calls it, and the tests take `BuildCosmosClient(…).ClientOptions.Serializer` as the production serializer (no connection is opened). |

## Tests (L2 project)

- `H8SpeContainerTypeHandlerTests` AC1 (bind asserted) and AC23-AC29: bound to the root unit after verification and before
  the KV write (call order pinned); missing env URL / no root unit / read fault → Resumable with nothing created; a bind
  failure (removed or not) and a bind infra fault → QuarantineRequired with no H7 handoff and no KV write; the idempotent
  re-run reads and binds nothing; AC22 (replication wait) binds nothing.
- `GraphContainerTypeProvisionerBindTests` (real `GraphServiceClient`, hand-written fake transport): the PATCH body shape
  and read-back; removal when the stamp does not read back (none / another unit / malformed), when the PATCH fails, and
  for `Guid.Empty`; "left unbound" when the removal fails too.
- `Spaarke.ArchTests/SpeAdminContainerBindingGuardTests.TheDeferredBinderIsReal`: H8 reads its creation record, creates,
  records, verifies, binds, writes KV, then completes; `SpeContainerId` is given a container only in `MarkCompleteAsync`;
  the provisioner's bind step stamps, reads back and deletes.
- Round 41 (`H8SpeContainerTypeHandlerTests` AC30-AC38): replication wait then resume — ONE `ProvisionAsync`, the same
  container verified, bound and handed off; a resume during the wait creates nothing; the record is written before
  verification (order `provision, write, verify`) so a quarantined verify resumes with the same container; a bind that
  removed the container resumes with a new root container in the SAME type; a provisioner failure after the type records
  the type; a run left by the pre-round-41 pending path resumes with its container and loses its unbound hand-off; an
  inconsistent record is quarantined with nothing created; the record survives a concurrent write; an unrecordable
  creation is quarantined naming both ids. `H7DataverseEnvVarValuesHandlerTests.ASpeContainerIdWrittenBeforeH8Completed_IsNeverConsumed_NoWriterCall`.
  `GraphContainerTypeProvisionerBindTests.AnExistingContainerType_IsReused_OnlyARootContainerIsCreatedInIt`,
  `…AFailureAfterTheTypeWasCreated_ReportsTheType_SoTheHandlerRecordsIt`.
- Round 49: the H8 tests' `FakeRepository` stores the JSON the PRODUCTION serializer writes and every read deserializes
  a fresh run (`Models/ProductionCosmosSerializer` — `CosmosModule.BuildCosmosClient(…).ClientOptions.Serializer`), so
  AC30-AC36 now see what Cosmos sees (with round 41's evidence record they FAIL: `provisioner.CallCount` 2). New AC41-AC50:
  the record is a typed field in the stored JSON; a root-container POST with no answer is recorded and the resume waits
  (in-window) or creates again (past the window); an adopted root container and every further container are bound before
  the hand-off; a further container neither bound nor removed stays on record, quarantines, and is bound on resume; a
  type in doubt is quarantined, a re-delivered dispatch creates nothing, and only an operator's clear lets a type be
  created; a type the operator recorded is resumed; a pre-round-41 hand-off next to a different typed root is bound too;
  a removal record survives a concurrent write; an unrecordable in-doubt record is quarantined.
  `GraphContainerTypeProvisionerBindTests` (+10): a non-OData fault on the root-container POST (the verifier's probe), a
  client-side timeout and the caller's cancellation are Failures with the container in doubt; a type POST with no answer
  (dropped / 2xx without id) is a Failure with the type in doubt; an `ODataError` is nothing in doubt; on a resume a
  container already in the type is adopted (one; several across pages, oldest first) and nothing created; a root container
  in doubt that is not listed (200 empty / 404) is waited for; a failed list creates nothing; a DELETE 404 is "removed".
  `Models/ProvisioningRunProductionSerializerTests` (8): a fully-populated run round-trips unchanged; evidence survives and
  a read-back run serializes for `GET /api/runs/{id}`; a pre-converter document reads back; the typed record survives; the
  retry counter keeps `"H12b"` and a legacy `"h9"` still counts; run-parameter keys persist verbatim.
- Full L2 suite on this branch: see the 165 task note §12 / §13 / §14.

## Resume — what H8 created is recorded and reused (FIXED, round 41 item 1)

**The defect (found in f2, fixed in f2-v1).** `MarkWaitingOnGateAsync` persisted the container-type and root container ids
— the root container's into `InterStepState.SpeContainerId`, H7's hand-off, while it was still UNBOUND — and a later
re-entry ran `HandleAsync` from the top, where step (6) called `ProvisionAsync` unconditionally: a SECOND container type
and root container on every re-entry (the reconciler re-dispatches an incomplete H8 once the dispatcher's 24h processed
marker expires, so after the replication wait at least once — and container types are capped per tenant and cannot be
deleted), with the first root container orphaned UNBOUND. And H7, which depended only on H6, could consume the unbound
id. The same orphaning followed any post-creation quarantine (verification, bind, KV write) that an operator resumed.

**The fix (this branch, in this project's code — owner round 35 assigned H8 to task 165; round 41 confirmed it):**
1. H8 RECORDS what it created immediately after creation, before anything that can fail, wait or crash (the record is
   H8's own: `InterStepState.ContainerTypeId` + the `h8-t6-verified` gate, Pending, naming the root container), merged
   over a concurrent write if needed.
2. Every re-entry reads the record first: a recorded root container is verified, bound, written to KV and handed off —
   never re-created; a recorded type with no container (a failed bind removed it, or creation stopped after the type)
   gets only a new root container IN THAT TYPE.
3. `InterStepState.SpeContainerId` — H7's hand-off — is written only by `MarkCompleteAsync`, after the bind and the KV
   write; a value an older run wrote before completion is withdrawn on re-entry.
4. H7 depends on H8 in the DAG, and H7 itself refuses a container id from a run where H8 has not completed.

So on every path the root container is stamped and read back before anything durable consumes it (KV write, H7
hand-off, CompletedPhase), or removed (round 41 item 1, round 35 item 1). While a run waits on the replication window
the recorded container exists unbound, inside an incomplete H8 that will bind or remove it; the record names it, and the
backfill's `-Verify` lists any unbound container an environment's configs reach.

**Correction (round 49).** Steps 1-2 above were true of the in-memory run and FALSE in production: the record named the
root container in the gate's `JsonElement` evidence, which the Cosmos serializer stored as `{"valueKind":1}`. Every claim
above that a resume "never creates a second … root container" is true only from `task/uac-r2-165-f2-v2` on, where:
1. the record is typed — `InterStepState.ContainerTypeId` + `InterStepState.SpeContainerCreation` — and H8 resumes only
   from those fields; the tests persist through the production serializer;
2. a resume with a recorded type and no recorded root container LISTS the type and adopts a container already there before
   it creates one (and binds every further container it finds, or removes it);
3. a creation that got no answer is recorded as in doubt: a container TYPE in doubt is QuarantineRequired until an operator
   has checked with a delegated SharePoint Embedded admin token — app-only can neither list nor delete container types —
   and a ROOT container in doubt is waited for (WaitingOnGate) through the replication window rather than created twice;
4. every fault after a Graph write is returned as a `Failure` that says what may exist — an exception (a client timeout's
   `OperationCanceledException` included) never escapes `CreateAsync`, so the handler always records before it fails.

What the code still cannot see — said plainly: a container TYPE created by a POST whose answer was lost cannot be found by
H8's app-only identity, so H8 never creates another type while one may exist; the operator's delegated check (the
quarantine's diagnostic gives the exact request) is the only way to name it.

## Live verification (manual gate (d) of the task 165 note §12.9 / §13)

- **The H8 bind** needs a real provisioning run (cert in the customer KV, 24h SPE replication). The wire shape is the one
  the BFF has used live since 2026-08-28; the stamp read-back on a v1.0 single-container GET was measured on dev
  2026-10-04 (task 165 note §11.1).
- **H8's root-business-unit read** (`DataverseWebApiRootBusinessUnitReader`, round 41 item 5 / the f2 verifier's item 6)
  uses `new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId })` against
  `{environment}/.default` — byte-for-byte the credential, tenant and audience of H5's `DataverseWebApiHealthProbe`
  (WhoAmI). H5 completes ONLY on a `Reachable` probe (`H5DataverseEnvCreationHandler`: Unreachable and a timed-out
  InProgress both fail the handler), and H8 is dispatched only after H5's CompletedPhase — so when H8 reads, that same
  identity has just been answered 200 by this environment; it is also the identity H10's `DataverseWebApiAppUserCreator`
  WRITES `systemusers` and role associations with, before any app user exists for it. The comments that say "the
  MI-Dataverse App User (H10) has not yet been created" (`DataverseWebApiEnvVarValuesWriter`, and the DataGrid seeder's
  "H10 has already registered the L2 UAMI") are about the identity H10 REGISTERS (`InterStepState.MiClientId`, which
  `InterStepState` documents as the customer App Service's UAMI) — the project's comments disagree on whether that is
  L2's own identity, and if it were, H5's gate and H10's writes could not work either. So H8 is no worse placed than H5
  and H10: a stall there would be the whole pipeline's, not H8's. What a WhoAmI 200 does NOT prove is a security role
  that reads `businessunit`; that is the one thing left for gate (d): on the first real run, H8 completes without
  `spe-root-business-unit-unresolved` (a 401/403 there names the status in the run's error), and the run's TYPED record
  `interStepState.speContainerCreation.owningBusinessUnitId` = the environment's root unit (`status` = `bound`). The T6
  gate evidence carries `owningBusinessUnitId` too — evidence now persists (round 49) — but the typed field is the check.
