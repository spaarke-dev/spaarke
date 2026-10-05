# H8 — the root container is bound to its business unit (from unified-access-control-r2 task 165)

> **Owner of the change:** unified-access-control-r2 task 165 (follow-up round f2, 2026-10-04), under owner round 35 item 1
> ("every container-creation path stamps … the control-plane H8 handler … Task 165 owns all three. The H8 change is
> recorded in customer-provisioning-orchestration-r1's notes for that project.") This note is that record.
> **Branch:** `task/uac-r2-165-f2` (integrates through `work/unified-access-control-r2`).

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
| `Reconciler/DagAdvancer.cs` | `H8` now depends on **`H3` and `H5`** (was `H3` only). H8 needs the environment H5 creates, to read its root business unit. Nothing else depends on H8; H7 already waits on H8's `InterStepState.SpeContainerId`. `DagAdvancerTests`: `AfterH3_UnlocksH9_ButH8WaitsForH5`, `AfterH3AndH5_UnlocksH8`. |
| `Handlers/SpeContainerType/H8SpeContainerTypeHandler.cs` | (5b) before ANY external side effect: `InterStepState.DataverseEnvUrl` required (`spe-missing-dataverse-env-url`, Resumable) and the environment's root business unit read through `IDataverseRootBusinessUnitReader` (none / more than one / read fault → `spe-root-business-unit-unresolved`, Resumable — nothing is created). (7c) after the app-only verification, BEFORE the KV write and before the success state hands the container to H7: `ISpeContainerTypeProvisioner.BindRootContainerAsync`. A bind failure is QuarantineRequired — `spe-container-binding-failed` (container removed), `spe-container-binding-failed-not-removed` (removal failed too: bind it with `scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind <containerId>=<rootBusinessUnitId> -Apply` or remove it), `spe-container-binding-infra-fault` (cert load failed before any Graph call; the container exists unbound) — and `InterStepState.SpeContainerId` is NOT set. The T6 gate evidence carries `owningBusinessUnitId`. The replication-pending wait (24h) does not bind (the container may be unaddressable). |
| `ISpeContainerTypeProvisioner` | `+ BindRootContainerAsync(SpeContainerBindRequest)` → `SpeContainerBindOutcome.Bound` / `.NotBound(Diagnostic, Removed)`. Extends the existing Graph seam (same T6 cert identity that created the container) rather than adding a second Graph collaborator. |
| `GraphContainerTypeProvisioner` | `BindRootContainerAsync` (cert → Graph client) and `internal BindNewContainerAsync(graph, containerId, businessUnitId)`: `PATCH /storage/fileStorage/containers/{id}/customProperties` with `{"spaarkeBusinessUnitId":{"value":"<unit D-form>","isSearchable":false}}` as the BODY ROOT (Graph merges), a single-container read-back (`$select=id,customProperties` — the collection drops custom properties), and `DELETE /storage/fileStorage/containers/{id}` when the stamp did not land for any reason (`Guid.Empty` included). The same wire shape as the BFF's `SpeAdminGraphService.WriteBusinessUnitStampAsync`. |
| `Handlers/SpeContainerType/DataverseRootBusinessUnitReader.cs` (new) | `IDataverseRootBusinessUnitReader` + `DataverseWebApiRootBusinessUnitReader`: `GET businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid&$top=2` with `DefaultAzureCredential(TenantId)` — the same query and token idiom as H10's `DataverseWebApiAppUserCreator.FindRootBusinessUnitIdAsync`; two roots are refused (never guess an owner). Registered in `Worker/Program.cs` with `AddHttpClient<…>()`. |
| `Sprk.Provisioning.ControlPlane.Core.csproj` | Compiles `src/server/shared/Contracts/SpeContainerBusinessUnitBinding.cs` (source-linked, `internal`) — the ONE C# constant for the property name, shared with the BFF (L2 cannot reference the BFF or Spaarke.Core). |

## Tests (L2 project)

- `H8SpeContainerTypeHandlerTests` AC1 (bind asserted) and AC23-AC29: bound to the root unit after verification and before
  the KV write (call order pinned); missing env URL / no root unit / read fault → Resumable with nothing created; a bind
  failure (removed or not) and a bind infra fault → QuarantineRequired with no H7 handoff and no KV write; the idempotent
  re-run reads and binds nothing; AC22 (replication wait) binds nothing.
- `GraphContainerTypeProvisionerBindTests` (real `GraphServiceClient`, hand-written fake transport): the PATCH body shape
  and read-back; removal when the stamp does not read back (none / another unit / malformed), when the PATCH fails, and
  for `Guid.Empty`; "left unbound" when the removal fails too.
- `Spaarke.ArchTests/SpeAdminContainerBindingGuardTests.TheDeferredBinderIsReal`: H8 verifies, then binds, then writes KV,
  then completes; the provisioner's bind step stamps, reads back and deletes.
- Full L2 suite on this branch: see the 165 task note §12.

## Observed, not changed (for this project to decide)

- **Resume after the 24h replication wait re-provisions.** `MarkWaitingOnGateAsync` persists the container-type and root
  container ids and returns; a later resume re-runs `HandleAsync` from the top, and step (6) calls
  `ISpeContainerTypeProvisioner.ProvisionAsync` unconditionally (no get-or-create), creating a SECOND container type and
  root container. The comment there says "only the app-only GET is retried", which the code does not do. With task 165's
  change the first root container stays UNBOUND (never verified, never bound) and, if H7 already consumed its id from
  `InterStepState.SpeContainerId`, becomes the environment's `sprk_SharePointEmbeddedContainerId` while being reachable
  by no SPE admin route. Complete fix (this project's): on re-entry with `InterStepState.ContainerTypeId` /
  `SpeContainerId` already set and no CompletedPhase, skip (6) and resume at (7) with the persisted ids; and H7 should not
  consume `SpeContainerId` while the T6 gate is Pending. Until then: after a replication-pending run, bind the persisted
  root container with the backfill (`-Bind <containerId>=<rootBusinessUnitId> -Apply`).
- **Live verification of the H8 bind** needs a real provisioning run (cert in the customer KV, 24h SPE replication). The
  wire shape is the one the BFF has used live since 2026-08-28; the stamp read-back on a v1.0 single-container GET was
  measured on dev 2026-10-04 (task 165 note §11.1).
