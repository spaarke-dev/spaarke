# Task 052b — The scheduled repair pass; the contact owner rule

Slice b of task 052 (FR-25, NFR-01, NFR-03, NFR-04). The on-demand trigger, the report and the family cascade are 052c, 052d
and 052e. Rigor: FULL. POML: `tasks/052b-repair-job.poml`. Nothing here enables the pass on any environment.

**Before enabling the pass on any environment:** read that environment's live Assign-cascade metadata for every table the pass
covers (contact first: its out-of-box one-to-many relationships are likely to cascade on Assign) and decide, per relationship,
whether a re-owned parent may re-own those children. Until a relationship is reviewed and added to
`RecordCreationService.AcceptedAssignCascades` (empty today), Repair re-owns no row of that table (see "Assign cascade").

## Rules

- **Forward-only.** The pass writes a row only when its `createdon` is at or after `RecordRepair:Pass:GoLive` and its
  `createdby` is not a product principal. The rule is asked three times: by the scan query, by the job over each scanned row,
  and by Repair over the row it read (`RecordRepairPassContext.RowOutsideThePass`), before anything is decided or written. A
  row whose `createdon` or `createdby` cannot be read is outside the pass.
- **Product principals (task 050's marker).** The BFF identity (WhoAmI); the Dataverse Web API client's managed identity
  (`ManagedIdentity:ClientId`, then `Graph:ManagedIdentity:ClientId`): the one application user whose `applicationid` is that
  client id; the ontology writer's identity when `Ontology:Writer:ManagedIdentityClientId` is set; and the ids in
  `RecordRepair:Pass:ProductPrincipalIds`. People are never in the set. A WhoAmI fault or empty answer, a Web API client id
  that is not a GUID or matches other than exactly one application user, a writer-identity fault, or a configured entry that
  is not a GUID: the run reads no table, writes nothing and fails.
- **Off until configured.** `RecordRepair:Pass:GoLive` unset: nothing is read or written, a warning is logged, status
  `not_configured` (the run is not counted as failed, so a deliberately-off environment is not alarmed every 5 minutes).
  Go-live parses only as an ISO 8601 round-trip instant (`"O"`, invariant culture, which carries its offset); anything else
  is logged as an error, nothing is read or written, and the run fails.
- **Tables.** Every registry table except `RecordRepairPassJob.ExcludedTables`: `task`, `account` (owner 2026-11-08), the
  append-only Decision Record, the Procedure Version (created by its own service), and every `CreatedOnlyByItsWriter` table
  (Signal, Decision Record, Budget Revision: every row is a product principal's). Repair itself refuses `task` and `account`
  (`record_repair.table_excluded`) before reading them, whoever calls it.
- **Contacts (owner 2026-11-08).** Owner-only (the registry's `ChatPlatformTableIsNotMapped`; no stamp). The owner rule is a
  branch inside `RecordOwnershipResolver`, so the chat create's step 7 and Repair's owner step ask the same rule, there is no
  new owner-write site and the owner-write census is unchanged. In order:
  1. **The secure decision first.** A contact filed under parents is decided as any child is (`ResolveFromParentsAsync`): a
     refusal (for example a primary parent that cannot be decided, or a secure parent the caller may not file under) stays a
     refusal, and a secure parent's owner is the answer. A contact with secure-only parents takes the secure decision the same
     way. The customer-team rule never overrides either.
  2. **Then the customer team.** Otherwise the owner is the single default Owner team of `ExternalAccess:CustomerBusinessUnitId`,
     answered by `ContactIdentityBinder.ResolveCreateOwnerAsync` (the identity-contact creates' lookup, reused unchanged),
     composed into the resolver as an optional dependency, as the Direct message team is.
  - Unset, empty, the Secure Record unit, an ambiguous Secure Record unit, no default team, two default teams, a binder over
    another environment, or no lookup composed: refused, `record_owner_customer_contact_team_unresolved`
    (`RecordOwnerRefusal.CustomerContactTeamUnresolved`), logged. The chat create creates nothing and shows a plain sentence
    with that code; Repair keeps the owner and the pass asks the row again.
  - An unreadable lookup (`LookupFailed`) is a fault and propagates: the chat create answers with the tool's fault code and a
    plain sentence (never a bare 500, never blamed on configuration); Repair keeps the owner and the pass retries it.
  - Never the creator's or the acting user's team.
- **Assign cascade (review F1-a).** Before Repair writes an owner, it reads the table's one-to-many relationships whose
  `CascadeConfiguration.Assign` is anything but `NoCascade` (`IGenericEntityService.GetAssignCascadesAsync`; an unstated value
  counts as cascading). The pass keeps the answer for the run (one read per table, an empty answer kept too). If any such
  relationship's child is not on `RecordCreationService.AcceptedAssignCascades` (keyed `"{table}->{child}"`, empty), the owner
  is not written: `record_repair.owner_assign_cascades` (not transient: the answer only changes when the allow-list does),
  logged once per table per pass, counted `ownerAssignCascades`; the mapping and the stamp are still written. A metadata fault
  writes no owner and is `record_repair.owner_assign_cascade_unknown` (transient, retried). The cascade reads are not counted
  as column-metadata reads.
- **Claims (ADR-036 A1).** The host's lease holds the tick. Each table: an owner-scoped claim
  (`record-repair-pass:{table}`, owner = run id, 15 min), the marker checked again under the claim, then a per-run marker
  (`record-repair-pass:{table}:{runId:N}`, 1 h) once the table's state is saved. A table another runner holds is not read; the
  run fails. The host retries a run only when the job throws, which it never does, so the marker guards a second runner of
  the same run id, not a host retry.
- **State** (distributed cache, `record-repair-pass:{table}:state`, 30 days): the watermark (end of the last finished window),
  a stopped sweep's window end and cursor, and the rows to ask again with their attempt count. No store, or a state that
  cannot be read or saved: the table is not swept (or its progress not kept) and the run fails.
- **Window.** `[max(go-live, watermark − 15 min), now − 2 min)`, ordered by id, `RecordRepair:Pass:MaxRowsPerTable` rows per
  run (default 200, at most 5000). The 15-minute overlap catches a row committed late with an earlier `createdon`; Repair is
  fill-only, so a row read twice is written once. A window the cap stops keeps its end and continues after the last id next
  run; the watermark moves only when the window is finished.
- **The customer team gates the contact table (review F2-b).** Once per run, before the contact table, the job asks the same
  lookup (`ContactIdentityBinder.ResolveCreateOwnerAsync`; it decides no owner). Unresolved (unset, not one default team, no
  lookup composed) or a lookup fault: the contact table is not swept, its watermark does not move, no row's attempt is
  counted, one warning is logged, `tablesWaiting` is counted and the run is partial (Success=false).
- **Retry (NFR-04).** `RecordRepairOutcome.NeedsRetry`: the read or write faulted, the row changed under the repair, the owner
  was unavailable, the contact's customer team is unresolved, or a step faulted (`TransientReasons`: mapping unavailable,
  mapping filing unavailable, mapping source unreadable, source or target metadata unavailable, stamp unavailable, Assign
  cascade unknown). A fault deciding a filed row's owner blocks its mapping as `record_repair.mapping_filing_unavailable`
  (transient); a deterministic refusal (the resolver's answer) stays `record_repair.mapping_filing_undecided` and is not
  retried. Kept rows are asked first each run. Only faults count toward the 12 attempts (`MaxAttempts`); a contact refused
  for the customer team uses none. At 12 a row is logged (error) as abandoned and dropped. A row whose mapping source does not
  exist (deleted) reads as unreadable and is retried until abandoned.
- **Metadata cache (052a's condition).** `RecordRepairPassContext` keeps each table's column metadata for the run: one read per
  table per pass however many rows need it; a fault or an empty answer is never kept and is asked again.
- **Observability.** One heartbeat line per attempt (status, every count, go-live, duration, trigger, run id, correlation id):
  Information when the status is `ok`, Warning otherwise. The same counts and the correlation id in the result JSON. Success
  only when every table finished with nothing to ask again, no table waiting, no claim held, no scan or state fault and no row
  outside the pass.

## Behaviour change (chat contact create)

A contact created through chat used to be owned by the creator's business-unit team (the resolver's acting-user rule). It is
now owned by the customer business unit's default Owner team, after the secure decision. In Model 1 the customer business unit
is usually the users' own unit, so the owner is usually the same team. Where `ExternalAccess:CustomerBusinessUnitId` is not
set, a chat contact create is refused (see K7).

## Callers whose `ChildEntityLogicalName` can be `contact` (review F1-b)

- `RecordCreationService` step 7 (`RecordCreationService.cs` ~1457/1475): the only production path that asks the resolver with
  `contact` as the child. It serves the chat create and Repair (through `ResolveOwnerOverAsync`). Contact is registered in
  `RecordTableRules.cs` (~453).
- `DataverseCreateRecordHandler.RefuseSecureFilingAsync` (~984): not reached for a contact. Contact is user-owned, so
  `OwnedChildWrite.PathFor` answers Owned and the create goes to `RecordCreationService`.
- Reparent paths, gated by `RecordOwnershipResolver.IsReparentableChild` (false for a contact: it is not an ownership
  parent table), so none re-owns a contact: `OwnedChildWrite` (~1541/1577), `FieldMappingEndpoints` (~1603), `DataverseUpdateHandler` (~201),
  `UpdateRecordActionCore` (~378). `ReparentAsync` over a contact is pinned directly in `CustomerContactOwnerTests`.
- `SecureChildReconciler`, `ExternalTeamShareSynchronizer` and `SecureRootInheritance.Cascade` walk the lineage tables; contact
  appears only in the exclusion in `SecureChildLineage.cs` (~178).
- Every other resolver call site names a fixed non-contact table. No unplanned caller.

## Config keys

| Key | Default | Meaning |
|---|---|---|
| `RecordRepair:Pass:GoLive` | unset (off) | ISO 8601 round-trip instant (`"O"`); no row created before it is written |
| `RecordRepair:Pass:ProductPrincipalIds` | empty | comma-separated retired product `systemuser` ids |
| `RecordRepair:Pass:MaxRowsPerTable` | 200 | rows swept per table per run (1–5000) |
| `ManagedIdentity:ClientId` / `Graph:ManagedIdentity:ClientId` | (existing keys) | when set, the Web API client's application user joins the principals |
| `Ontology:Writer:ManagedIdentityClientId` | (existing key) | when set, the writer's WhoAmI joins the principals |
| `ExternalAccess:CustomerBusinessUnitId` | (existing key) | the contact owner's business unit; unset refuses chat contact creates and holds the contact table |

## Component Justification (CLAUDE.md §11)

- **`RecordRepairPassJob`** (new class, new `AddScheduledJob` registration in `OfficeModule`). (1) Existing:
  `CoreAncestorStampReconciliationJob` repairs stale stamps only, for rows filed under an intermediate; Repair mode had no
  caller. (2) Extension: merging would tie the stamp safety net to the go-live switch and mix two cursors with different
  questions; this job reuses Repair mode, the claim store and the state store. (3) Cost of doing nothing: a row created on a
  form, by a flow or by an import never gets its mapping, team owner or stamp (FR-25).
- **`RecordRepairPassContext`** (new class). (1) Existing: `MappingColumnMetadata.ReadAsync` reads and does not keep;
  `MetadataService`'s 6 h cache is a DataGrid contract, too long for `IsSecured` (052a). (2) Extension: Repair's own read goes
  through it only when a pass hands one; it also keeps the Assign-cascade answer per table. (3) Cost of doing nothing: two
  whole-table metadata reads per repaired row (052a's ship condition), and the forward-only rule asked by the query alone.
- **`IGenericEntityService.GetAssignCascadesAsync` and `AssignCascadeRelationship`** (new interface method and record in
  `Spaarke.Dataverse`). (1) Existing: `IGenericEntityService` reads column metadata (`RetrieveEntityRequest` with attributes)
  but not relationships; `MetadataService` reads attributes for the DataGrid. (2) Extension: one more method on the same
  interface, a default body that throws `NotSupportedException` (so other implementations are not broken), implemented in
  `DataverseServiceClientImpl` with the same request and `EntityFilters.Relationships`. (3) Cost of doing nothing: Repair's
  owner write (an Assign) re-owns every child on a cascading relationship, rows the pass never decided on.
- **`RecordCreationService.AcceptedAssignCascades`** (new empty allow-list). (1) Existing: none. (2) Extension: n/a.
  (3) Cost of doing nothing: no reviewed cascade could ever be allowed without code that bypasses the check.
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
- **`RecordRepairOutcome.NeedsRetry`, Repair's optional `pass` argument, the `RecordRepairReason` codes** (`table_excluded`,
  `outside_pass`, `owner_assign_cascades`, `owner_assign_cascade_unknown`, `mapping_filing_unavailable`, and the others added
  here) **and the counts `ownerAssignCascades`, `tablesWaiting`.** Extensions of the existing report, method and heartbeat;
  without them the pass cannot decide retry, re-check the row Repair read, or show why a table or an owner was held.

**Placement Justification (§10, ADR-052).** In the BFF on the in-process `Spaarke.Scheduling` host, registered with
`AddScheduledJob`: BFF domain code (Repair mode, the ownership resolver, the field-mapping service) over BFF-owned tables,
the BFF identity, low volume (rows created outside the product). No new scheduler, store, package or endpoint. Publish-size
delta and the CVE check are the main session's.

## Coordinator review points (UAC-r2), each pinned by a test

1. **Contact owner from `ExternalAccess:CustomerBusinessUnitId` only; refused and logged when unresolved or ambiguous;
   the unset case (dev today) is clean.** `CustomerContactOwnerTests.AContact_WhoseCustomerTeamCannotBeResolved_*` (6 causes),
   `WithTheSettingUnset_TheRefusalIsLogged_AndNoTeamIsNamed` (error logged, no team looked up),
   `RecordRepairPassJobTests.WithTheCustomerUnitSettingUnset_*` (the contact table waits: no read, no write, owner kept),
   `SecureChildOwnershipAiToolTests.AChatContact_WhoseCustomerTeamCannotBeResolved_*` (nothing created).
2. **Go-live unset: nothing written, no row and no watermark.** `WithGoLiveUnset_TheJobReadsAndWritesNothing_AndLogsAWarning`
   (no WhoAmI, no scan, no write, no table state in the cache).
3. **Every owner write through `RecordOwnershipResolver`.** The job writes no owner; it reaches owner writes only through
   Repair (`RepairOwnerAsync`, censused Routed, unchanged). The contact rule is a branch INSIDE the resolver that answers with
   the existing `Owned`/`Refused` shapes; no other code makes an Owned resolution (`NoServerCodeForgesAnOwnedResolution`).
   Pinned by `RecordOwnerAssignmentCensusTests.TheRepairPass_WritesNoOwner_AndDecidesNoOwner` (with a must-fire seed) and
   `RecordRepairReachabilityTests` (the job is Repair's one listed caller). The resolver change (113ff1d841) was confirmed by
   UAC-r2 with the conditions below.
4. **UAC-r2 conditions.** (a) The chat contact refusal and `LookupFailed` each reach the user as a plain sentence:
   `SecureChildOwnershipAiToolTests.AChatContactRefusal_ReachesTheUserAsAPlainMessage_NeverABareFault` (both cases; the
   refusal carries `record_owner_customer_contact_team_unresolved`, the fault the tool's fault code). (b) The caller list is
   above. (c) The behaviour-change note is above. (d) The deployment dependency is K7.

## Review fixes (Opus adversarial review of 1e72d50962)

| Finding | Fix | Pinned by |
|---|---|---|
| F1-b — the customer team overrode a secure parent | the contact branch runs after the parents' decision | `CustomerContactOwnerTests` (secure, ordinary, unreadable invoice; `ReparentAsync` theory); `AChatContactUnderASecureInvoice_*`, `AChatContactUnderAnOrdinaryInvoice_*`; `RecordRepairPassJobTests` contact-under-invoice tests |
| F1-a — Repair's owner write could cascade on Assign | "Assign cascade" rule above | `RecordRepairPassJobTests` (cascading child not re-owned, mapping kept, no cascade re-owned, allow-list empty, metadata fault retried); `AssignCascadeProjectionTests` (6) |
| F2-a — an owner-resolution fault and an unreadable source were not retried | `mapping_filing_unavailable`; both transient | `RecordRepairModeTests` (resolver fault, kept-owner fault, source fault retried; deterministic refusal not retried) |
| F2-b — an unconfigured customer team used up every contact's attempts | the contact-table gate; `AttemptOf` | `RecordRepairPassJobTests` (unset, not one team, lookup fault, attempts not counted) |
| K5 — a late-committed row could fall behind the watermark | 15-minute overlap | `ANewWindow_StartsAQuarterHourBeforeTheWatermark_*`, `TheOverlap_NeverReachesBeforeGoLive` |
| K6 — the Web API client's identity was not a product principal | `ManagedIdentity:ClientId` application user | `RecordRepairPassJobTests` (joins the set; not a GUID / zero / two matches read nothing) |
| K7 — go-live parsed culture-dependently | `TryParseExact(..., "O", ...)` | `RecordRepairPassJobTests` theory + offset test |
| K9 — not_configured logged as an error | Warning | `WithGoLiveUnset_*_AndLogsAWarning` |
| K10 — the host-retry marker test proved an unreachable path | deleted | — |
| K8 — the retry set is uncapped | noted (K8 below) | — |

## Evidence

- **Fail with the rule switched off, then pass** (production code mutated, test project rebuilt, restored with
  `git checkout`):
  - A — go-live check and product-principal check off in `RowOutsideThePass`: 6/6 fail (`ARowCreatedBeforeGoLive_*`,
    `Repair_HandedARowOutsideThePass_*` ×2, `ARowCreatedByAProductPrincipal_*` ×2, `TheOntologyWritersIdentity_*`).
  - B — task/account exclusion emptied, contact branch off, claim bypassed: 15/15 fail (task/account ×3, contact rule ×10
    across the resolver, the chat create and the pass, the claim ×1, the unset-setting pass test ×1).
  - C — marker bypassed, watermark not moved, an undecided principal set treated as empty, unset go-live treated as set:
    7/7 fail (at the time; the host-retry test is since deleted, K10).
  - D — metadata never kept, `NeedsRetry` always false: 4/4 fail (`WithinOnePass_*`, `AFaultedMetadataRead_*`,
    `ARowWhoseRepairFailed_*`, `AContactWhoseCustomerTeam_*`).
  - F1-b — the old ordering (customer team before the parents): 5 must-fires fail (resolver ×3, Repair, chat).
  - F1-a — Repair's cascade check bypassed, and the projection keeping only `Cascade`: 7 fail.
  - F2-a — the old fault branch, the reasons not transient: 3 fail.
  - F2-b and review Ks — the contact-table gate off, `AttemptOf` always 1, no window overlap, the Web API identity not
    added, a culture parse, an error-level log: 14 fail.
  - Restored: all pass.
- `RecordRepairPassJobTests`: 49/49.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `tests/Spaarke.ArchTests`: 2537/2537.
- Sprk.Bff.Api.Tests filtered (Repair, RecordOwnership, Scheduled, Job, Contact, ExternalAccess, RecordCreation,
  SecureChildOwnership, AssignCascade, ColumnMetadata; `!~Api.Office.`): 11620/11620.
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
- **K5 — Not configured is reported as success** with status `not_configured` and a warning, by design (off is the default
  everywhere); an unparseable go-live fails the run.
- **K6 — Abandoned rows** after 12 faulted attempts are logged (error) and dropped from the state; a later on-demand repair
  (052c) or report (052d) is where they surface.
- **K7 — Deployment dependency.** Without `ExternalAccess:CustomerBusinessUnitId` (or with a unit that has not exactly one
  default Owner team), chat contact creates are refused and the pass holds the contact table. Set it, and confirm its default
  team, before enabling either on an environment.
- **K8 — The retry set is uncapped.** The rows kept to ask again are not capped in number; each is bounded by its 12 attempts,
  except a contact refused for the customer team, which uses none (the contact table is not swept while the run's own ask of
  the team fails, so such rows arise only when a row's answer differs from that ask). A large outage could make one table's state
  entry large; 052d's report is where that would show.
- **K9 — Assign cascades not yet reviewed.** With `AcceptedAssignCascades` empty, a table with any cascading-on-Assign
  relationship has its mapping and stamp repaired but no owner written. Read the live metadata and review it before enabling
  (top of this file).

## Deviations from the parent POML (052)

- The job lives in `Services/RecordCreation/` beside Repair mode (not `Services/Dataverse/FieldMapping/`): it calls an internal
  method of the internal `RecordCreationService`, and its state belongs to Repair, not to the field-mapping service.
- The on-demand trigger, the report and the family cascade are deferred to 052c, 052d and 052e (the 052 split).
- AC4's refusal code is `record_owner_customer_contact_team_unresolved` (the `record_owner_` prefix every resolver refusal
  carries), not the `customer_contact_owner_team_unresolved` first written in the POML; the POML is corrected.
- One line changed in `RecordCreationService.cs` (045d's file): the fault branch of `RepairOwnerAsync` now sets
  `blockedOnFault = filed ? RecordRepairReason.MappingFilingUnavailable : null` (review F2-a). No other edit to 045d's files.
- Review F2-b and the review Ks are one commit (abfba9f828): they change the same job and test file.
