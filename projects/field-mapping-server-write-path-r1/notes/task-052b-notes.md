# Task 052b — The scheduled repair pass; the contact owner rule

Slice b of task 052 (FR-25, NFR-01, NFR-03, NFR-04). The on-demand trigger, the report and the family cascade are 052c, 052d
and 052e. Rigor: FULL. POML: `tasks/052b-repair-job.poml`. Nothing here enables the pass on any environment.

## Rules

- **Forward-only.** The pass writes a row only when its `createdon` is at or after `RecordRepair:Pass:GoLive` and its
  `createdby` is not a product principal. The rule is asked three times: by the scan query, by the job over each scanned row,
  and by Repair over the row it read (`RecordRepairPassContext.RowOutsideThePass`), before anything is decided or written. A
  row whose `createdon` or `createdby` cannot be read is outside the pass.
- **Product principals (task 050's marker).** The BFF identity (WhoAmI), the ontology writer's identity when
  `Ontology:Writer:ManagedIdentityClientId` is set, and the ids in `RecordRepair:Pass:ProductPrincipalIds`. People are never
  in the set. A WhoAmI fault or empty answer, a writer-identity fault, or a configured entry that is not a GUID: the run reads
  no table, writes nothing and fails.
- **Off until configured.** `RecordRepair:Pass:GoLive` unset: nothing is read or written, an error is logged, status
  `not_configured` (the run is not counted as failed, so a deliberately-off environment is not alarmed every 5 minutes).
  Unparseable: the same, and the run fails.
- **Tables.** Every registry table except `RecordRepairPassJob.ExcludedTables`: `task`, `account` (owner 2026-11-08), the
  append-only Decision Record, the Procedure Version (created by its own service), and every `CreatedOnlyByItsWriter` table
  (Signal, Decision Record, Budget Revision: every row is a product principal's). Repair itself refuses `task` and `account`
  (`record_repair.table_excluded`) before reading them, whoever calls it.
- **Contacts (owner 2026-11-08).** Owner-only (the registry's `ChatPlatformTableIsNotMapped`; no stamp). The owner is the
  single default Owner team of `ExternalAccess:CustomerBusinessUnitId`, answered by `ContactIdentityBinder.ResolveCreateOwnerAsync`
  (the identity-contact creates' lookup, reused unchanged), composed into `RecordOwnershipResolver` as an optional dependency,
  as the Direct message team is. Every contact question goes there first — the chat create's step 7 and Repair's owner step
  alike — so there is no new owner-write site and the owner-write census is unchanged.
  - Unset, empty, the Secure Record unit, an ambiguous Secure Record unit, no default team, two default teams, a binder over
    another environment, or no lookup composed: refused, `record_owner_customer_contact_team_unresolved`
    (`RecordOwnerRefusal.CustomerContactTeamUnresolved`), logged. The chat create creates nothing; Repair keeps the owner and
    the pass asks the row again.
  - An unreadable lookup (`LookupFailed`) is a fault and propagates (the create answers 503-unavailable, Repair keeps the owner
    and the pass retries it).
  - Never the creator's or the acting user's team.
- **Claims (ADR-036 A1).** The host's lease holds the tick. Each table: an owner-scoped claim
  (`record-repair-pass:{table}`, owner = run id, 15 min), the marker checked again under the claim, then a per-run marker
  (`record-repair-pass:{table}:{runId:N}`, 1 h) once the table's state is saved. A table another runner holds is not read; the
  run fails. A host retry of the run no-ops every marked table.
- **State** (distributed cache, `record-repair-pass:{table}:state`, 30 days): the watermark (end of the last finished window),
  a stopped sweep's window end and cursor, and the rows to ask again with their attempt count. No store, or a state that
  cannot be read or saved: the table is not swept (or its progress not kept) and the run fails.
- **Window.** `[max(go-live, watermark), now − 2 min)`, ordered by id, `RecordRepair:Pass:MaxRowsPerTable` rows per run
  (default 200, at most 5000). A window the cap stops keeps its end and continues after the last id next run; the watermark
  moves only when the window is finished.
- **Retry (NFR-04).** `RecordRepairOutcome.NeedsRetry`: the read or write faulted, the row changed under the repair, the owner
  was unavailable, the contact's customer team is unresolved, or a step faulted (mapping unavailable, source or target metadata
  unavailable, stamp unavailable). Kept rows are asked first each run, up to 12 runs, then logged as abandoned.
- **Metadata cache (052a's condition).** `RecordRepairPassContext` keeps each table's column metadata for the run: one read per
  table per pass however many rows need it; a fault or an empty answer is never kept and is asked again.
- **Observability.** One heartbeat line per attempt (status, every count, go-live, duration, trigger, run id, correlation id);
  the same counts and the correlation id in the result JSON. Success only when every table finished with nothing to ask again,
  no claim held, no scan or state fault and no row outside the pass.

## Config keys

| Key | Default | Meaning |
|---|---|---|
| `RecordRepair:Pass:GoLive` | unset (off) | ISO 8601 instant; no row created before it is written |
| `RecordRepair:Pass:ProductPrincipalIds` | empty | comma-separated retired product `systemuser` ids |
| `RecordRepair:Pass:MaxRowsPerTable` | 200 | rows swept per table per run (1–5000) |
| `Ontology:Writer:ManagedIdentityClientId` | (existing key) | when set, the writer's WhoAmI joins the principals |

## Component Justification (CLAUDE.md §11)

- **`RecordRepairPassJob`** (new class, new `AddScheduledJob` registration in `OfficeModule`). (1) Existing:
  `CoreAncestorStampReconciliationJob` repairs stale stamps only, for rows filed under an intermediate; Repair mode had no
  caller. (2) Extension: merging would tie the stamp safety net to the go-live switch and mix two cursors with different
  questions; this job reuses Repair mode, the claim store and the state store. (3) Cost of doing nothing: a row created on a
  form, by a flow or by an import never gets its mapping, team owner or stamp (FR-25).
- **`RecordRepairPassContext`** (new class). (1) Existing: `MappingColumnMetadata.ReadAsync` reads and does not keep;
  `MetadataService`'s 6 h cache is a DataGrid contract, too long for `IsSecured` (052a). (2) Extension: Repair's own read goes
  through it only when a pass hands one. (3) Cost of doing nothing: two whole-table metadata reads per repaired row (052a's
  ship condition), and the forward-only rule asked by the query alone.
- **`RecordOwnerRefusal.CustomerContactTeamUnresolved`** (new code). (1) Existing: `NoDefaultOwnerTeam`, `ActingUserUnresolved`
  describe a parent's or the acting user's unit. (2) Extension: one more code in the set the create and Repair render.
  (3) Cost of doing nothing: a misnamed cause, and the pass could not tell this refusal (an administrator's fix) from one it
  need not retry.
- **Resolver's optional `ContactIdentityBinder`** (new constructor parameter, no new registration: DI injects the existing
  singleton). (1) Existing: the binder's lookup is the one rule; the resolver's record-first rule. (2) Extension: composed like
  `DirectMessageOwnerTeam`. (3) Cost of doing nothing: a chat contact and a repaired contact would be owned by the acting user's
  team, against the owner decision.
- **Config keys** `RecordRepair:Pass:GoLive`, `:ProductPrincipalIds`, `:MaxRowsPerTable`. (1) Existing: none for the pass.
  (2) Extension: n/a. (3) Cost of doing nothing: the pass could not be off by default, could not know retired identities
  (they cannot be resolved at runtime), and could not be throttled per environment.
- **`RecordRepairOutcome.NeedsRetry`, four `RecordRepairReason` codes, Repair's optional `pass` argument.** Extensions of the
  existing report and method; without them the pass cannot decide retry or re-check the row Repair read.

**Placement Justification (§10, ADR-052).** In the BFF on the in-process `Spaarke.Scheduling` host, registered with
`AddScheduledJob`: BFF domain code (Repair mode, the ownership resolver, the field-mapping service) over BFF-owned tables,
the BFF identity, low volume (rows created outside the product). No new scheduler, store, package or endpoint. Publish-size
delta and the CVE check are the main session's.

## Coordinator review points (UAC-r2), each pinned by a test

1. **Contact owner from `ExternalAccess:CustomerBusinessUnitId` only; refused and logged when unresolved or ambiguous;
   the unset case (dev today) is clean.** `CustomerContactOwnerTests.AContact_WhoseCustomerTeamCannotBeResolved_*` (6 causes),
   `WithTheSettingUnset_TheRefusalIsLogged_AndNoTeamIsNamed` (error logged, no team looked up),
   `RecordRepairPassJobTests.WithTheCustomerUnitSettingUnset_AContactIsNotReOwned_*` (no write, owner kept, no default team
   read), `SecureChildOwnershipAiToolTests.AChatContact_WhoseCustomerTeamCannotBeResolved_*` (nothing created).
2. **Go-live unset: nothing written, no row and no watermark.** `WithGoLiveUnset_TheJobReadsAndWritesNothing_AndLogsAnError`
   (no WhoAmI, no scan, no write, no table state in the cache).
3. **Every owner write through `RecordOwnershipResolver`.** The job writes no owner; it reaches owner writes only through
   Repair (`RepairOwnerAsync`, censused Routed, unchanged). The contact rule is a branch INSIDE the resolver that answers with
   the existing `Owned`/`Refused` shapes; no other code makes an Owned resolution (`NoServerCodeForgesAnOwnedResolution`).
   Pinned by `RecordOwnerAssignmentCensusTests.TheRepairPass_WritesNoOwner_AndDecidesNoOwner` (with a must-fire seed) and
   `RecordRepairReachabilityTests` (the job is Repair's one listed caller). **For the coordinator:** the resolver now gives a
   contact answer it did not give before (the customer unit's team, or `record_owner_customer_contact_team_unresolved`). It
   was added before the review points arrived; it is local only (not pushed) and is the decision to confirm.

## Evidence

- **Fail with the rule switched off, then pass** (production code mutated, test project rebuilt, restored with
  `git checkout`):
  - A — go-live check and product-principal check off in `RowOutsideThePass`: 6/6 fail (`ARowCreatedBeforeGoLive_*`,
    `Repair_HandedARowOutsideThePass_*` ×2, `ARowCreatedByAProductPrincipal_*` ×2, `TheOntologyWritersIdentity_*`).
  - B — task/account exclusion emptied, contact branch off, claim bypassed: 15/15 fail (task/account ×3, contact rule ×10
    across the resolver, the chat create and the pass, the claim ×1, the unset-setting pass test ×1).
  - C — marker bypassed, watermark not moved, an undecided principal set treated as empty, unset go-live treated as set:
    7/7 fail (`ASecondRun_*`, `AHostRetry_*`, `ARunThatCannotDecideItsPrincipals_*` ×4, `WithGoLiveUnset_*`).
  - D — metadata never kept, `NeedsRetry` always false: 4/4 fail (`WithinOnePass_*`, `AFaultedMetadataRead_*`,
    `ARowWhoseRepairFailed_*`, `AContactWhoseCustomerTeam_*`).
  - Restored: all pass.
- New and changed task tests: 49 pass (`RecordRepairPassJobTests`, `CustomerContactOwnerTests`, the chat contact tests).
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `tests/Spaarke.ArchTests`: 2537/2537.
- Sprk.Bff.Api.Tests filtered (Repair, RecordOwnership, Scheduled, Job, Contact, ExternalAccess, RecordCreation,
  SecureChildOwnership; `!~Api.Office.`): 11572/11573. The one failure,
  `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_*`, times out at 57 s only under the
  full filtered run and passes alone (55/55, twice); it touches none of this task's code.
- `dotnet format whitespace --verify-no-changes` on every touched .cs file: clean.

## Known limits (K)

- **K1 — Invoice family.** Repair refuses to write an invoice-family row until ebilling-r1's guard lands (#1911), reported as
  skipped (`record_repair.invoice_family_guard_unavailable`), not retried. Rows in the window while the guard is missing are
  not revisited by the pass after the guard lands (the watermark has passed them); the on-demand trigger (052c) covers them.
- **K2 — No claim renewal.** The per-table claim is 15 minutes without a heartbeat renewal; a table whose sweep outlived it
  could be swept by a second runner at the same time. Harmless: Repair is fill-only and conditional on the row version, so the
  second write is a no-op or a refused version. The host's lease already prevents overlapping ticks.
- **K3 — Ordering by id.** The scan orders by the primary key and continues with `id > last`; Dataverse's (SQL) GUID order
  is used for both, so the cursor is consistent there. The test scan uses .NET's GUID order consistently.
- **K4 — The ontology writer's identity** is asked through its own connection (`OntologyWriterDataverseClient`); the tests
  stand in for it through the job's `OntologyWriterIdentity` seam.
- **K5 — Not configured is reported as success** with status `not_configured` and an error log line, by design (off is the
  default everywhere); an unparseable go-live fails the run.
- **K6 — Abandoned rows** after 12 runs are logged (error) and dropped from the state; a later on-demand repair (052c) or
  report (052d) is where they surface.

## Deviations from the parent POML (052)

- The job lives in `Services/RecordCreation/` beside Repair mode (not `Services/Dataverse/FieldMapping/`): it calls an internal
  method of the internal `RecordCreationService`, and its state belongs to Repair, not to the field-mapping service.
- The on-demand trigger, the report and the family cascade are deferred to 052c, 052d and 052e (the 052 split).
- AC4's refusal code is `record_owner_customer_contact_team_unresolved` (the `record_owner_` prefix every resolver refusal
  carries), not the `customer_contact_owner_team_unresolved` first written in the POML; the POML is corrected.
