# H8 — the root container is bound to its business unit (from unified-access-control-r2 task 165)

> **Owner of the change:** unified-access-control-r2 task 165 (follow-up round f2, 2026-10-04), under owner round 35 item 1
> ("every container-creation path stamps … the control-plane H8 handler … Task 165 owns all three. The H8 change is
> recorded in customer-provisioning-orchestration-r1's notes for that project.") This note is that record.
> **Branch:** `task/uac-r2-165-f2`, then `task/uac-r2-165-f2-v1` (integrates through `work/unified-access-control-r2`).
> **Round 41 (2026-10-05, BINDING):** "Round 35 item 1 includes H8's replication-pending path, and task 165 fixes it. It is
> not handed to another project." The resume defect this note first recorded as "observed, not changed" is FIXED — see
> "Resume — what H8 created is recorded and reused" below. Nothing in this note is owed by this project.

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
- Full L2 suite on this branch: see the 165 task note §12.

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
  `spe-root-business-unit-unresolved` (a 401/403 there names the status in the run's error), and the T6 gate evidence
  carries `owningBusinessUnitId` = the environment's root unit.
