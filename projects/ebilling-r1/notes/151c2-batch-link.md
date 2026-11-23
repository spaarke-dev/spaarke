# Task 151c-2: link each AP email to its export batch

Branch `task/ebill-151c2-batch-link`, from `work/ebilling-r1` at `7d493b53db`. Rigor FULL. Model: Sonnet @ high. Decisions EB-D-182, EB-D-184. Design agreed with unified-access-control-r2 (a plain reference, not a filing; no cascade).

## What was built

| Piece | Where |
|---|---|
| Column `sprk_communication.sprk_billingexport` ("Export Batch"), lookup to `sprk_billingexport`, optional | `scripts/ebilling/Deploy-EbillingCommunicationBatchLink.ps1` (idempotent, `-WhatIf`, `SpaarkeMaster`, publishes `sprk_communication` only) |
| Relationship `sprk_communication_billingexport_billingexport` | same script (`Add-Lookup -Cascade`, `-MenuBehavior DoNotDisplay`: the batch form has the subgrid, so no Related-menu duplicate) |
| Solution source | `Entities/sprk_Communication/Entity.xml` (attribute), `Other/Relationships/sprk_billingexport.xml` and `Other/Relationships.xml` (relationship), billing export main form plain and `_managed` |
| "AP emails" subgrid on the batch main form | `Deploy-EbillingFormsViews.ps1` Spec (`Subgrids`, view "All Outgoing Email"), and the two form XML files |
| The send writes it | `ServerSendOptions.BillingExportId` -> `CommunicationService.BuildDataverseRecordForUserAsync`; set by `ApExportDeliveryService` |
| Missing-column fallback | `CommunicationService.CreatePreparedRecordAsync` + `IsMissingColumnFault` |
| Guard | `tests/Spaarke.ArchTests/CommunicationBatchLinkNotFilingGuardTests.cs` |
| No "New" on the batch's AP emails subgrid (fix round) | `NO_NEW_HOSTS = { sprk_billingexport: true }` in `src/client/webresources/js/sprk_secure_child_ribbon.js` and its byte-identical mirror `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_/scripts/secure_child_ribbon.js`; `nativeNewAllowed` and `newChildAvailable` return false for that host. Jest case in `secureChildRibbonScript.test.ts` |
| Subgrid relationship check (fix round) | `Assert-SubgridRelationship` in `Deploy-EbillingFormsViews.ps1`, called in a step-0 loop before any write |
| Relationship-name check (fix round) | `Test-ExistingBatchLink` in the link script: the lookup exists but its relationship has another name -> exit 2 |
| Batch/owner agreement (fix round) | `SendAsync`: `BillingExportId` and `OwnerFollows` that name different records -> 409 `server_send_batch_mismatch` before anything is sent or written |
| Docs | `docs/data-model/sprk_billingexport.md` ("Email to batch reference"), `docs/data-model/sprk_communication.md` |

## Cascade (an access requirement)

| Member | Value |
|---|---|
| Assign | NoCascade |
| Share | NoCascade |
| Unshare | NoCascade |
| Reparent | NoCascade |
| Merge | NoCascade |
| RollupView | NoCascade |
| Delete | RemoveLink (Restrict is not wanted) |

The script passes all seven explicitly to `Add-Lookup -Cascade` (not the module default), and verifies them on EVERY run: step 0 (read only, before any write) reads the existing relationship and stops with exit 2 when any member differs; step 2 reads the relationship back after a create and stops the same way. The script never alters an existing relationship. Exit 2 also for: the column exists but is not a lookup to `sprk_billingexport`; a second lookup from `sprk_communication` to `sprk_billingexport`. The comparison function was exercised on a good and a bad configuration (Assign Cascade, Delete Restrict, Share Cascade -> three mismatches named).

`-WhatIf` against https://spaarkedev1.crm.dynamics.com (read only):

```
0. Pre-flight (read only)
1. sprk_communication.sprk_billingexport
  WOULD CREATE sprk_billingexport (Lookup -> sprk_billingexport, custom cascade: delete RemoveLink, assign/share/unshare/reparent NoCascade/...)
2. Verify the cascade
  WhatIf: the relationship does not exist yet; it would be created with the cascade above
  WhatIf: publish skipped          created : 1   changed : 0   skipped : 0   exit 0
```

`Deploy-EbillingFormsViews.ps1 -FormsOnly -WhatIf` (before the relationship check was added): exactly one change, the `sprk_billingexport` main form rebuilt (13 other forms unchanged). With the check (fix round, item 2) the same command against dev now stops with exit 1 and "relationship ... does not exist in https://spaarkedev1.crm.dynamics.com (run Deploy-EbillingCommunicationBatchLink.ps1 first). Nothing was written.", which is correct until step 1 of the deploy order has run.

## The column stays out of every filing registry

Read, not assumed. The registries below are static tables; none is built by reflection over the entity's lookups. The one place that reads a row's `EntityReference` values by column, `RecordOwnershipContext.ParentsOf(IEnumerable<KeyValuePair<string, object>>, table)`, keeps a reference only when `RecordOwnershipResolver.IsOwnershipParent(target)` is true, and `sprk_billingexport` is not in `OwnershipParentEntities`, so the reference is never read as a parent. The send also writes the column AFTER the owner is resolved, and the server send asks the resolver the content-row question about the batch (`ContentOf`), never `ForChild(communication)`.

| Searched | Result |
|---|---|
| `RecordFamilyEdges.All` (+ its parity test) | no `sprk_billingexport` row |
| `SecureChildLineage.Children` | no row; `sprk_communication` lists the thread and the eight `sprk_regarding*` lookups; the file says in a comment that the batch is not a codified table |
| `SecureRootInheritance.TypedFilingColumns` / `IntermediateFilingColumns`, `CoreAncestorResolver` (`IntermediateRootColumns`, `StampSourceColumns`, `CoreAncestorLookups`) | no row |
| `RecordOwnershipResolver.OwnershipParentEntities`, `IsOwnershipParent`, `CitationLookups` | batch not a parent |
| `RegardingFieldMap.All` / `AllRegardingFields`, `RegardingParentEntityMapper`, `CommunicationService.RegardingLookupMap` | no entry |
| Field-mapping and set-regarding code (`Api/FieldMappings`, `FieldMappingPushLookups`, `FieldMappingEndpoints`) | no reference; those tables are Dataverse data (`sprk_fieldmapping` rows), and no row targets the new column |
| `grep -rln sprk_billingexport src/server` | `ApExportEndpoints`, `ApExportAuthorization`, `ApExportService`, `StoredFileContracts` (Finance), `ServerSendOptions` (the option); comments only in `SecureChildLineage.cs` and `ParentLineage.cs` |
| `grep "StartsWith(\"sprk_regarding"` over `src/server` | none: no scan keyed on the prefix, and the column is not named `sprk_regarding*` anyway |

Guard (`CommunicationBatchLinkNotFilingGuardTests`, 10 tests after the fix round): (1) reads the real rows of ten registries (including the private `RegardingLookupMap`, by reflection, with a size floor) and fails on any row that names `sprk_billingexport` as the column or target of a `sprk_communication` lookup, or makes the batch an ownership parent, a stamp target or a regarding target; (2) behavioural: a communication carrying the reference has no ownership parent through `ParentsOf`, `OrderedParentsOf` and `SecureOnlyParentsOf`; (3) source scan: no code (comments stripped) in `Services/Dataverse`, `Services/Access`, `Services/Communication`, `Services/ExternalAccess`, `Infrastructure/ExternalAccess`, `Api/Records`, `Api/ExternalAccess`, `Api/FieldMappings` names the literal `"sprk_billingexport"` or `BillingExportColumn` / `BillingExportEntity` / `BillingExportId`, except `ServerSendOptions.cs` and `CommunicationService.cs`. Must-fire controls: a seeded row of each registry shape (one at a time, exactly that row is reported), the real `ParentsOf` keeping a real filing lookup, each source token alone. Must-not-fire controls: other tables' batch lookups, other communication lookups, `sprk_billingevent`, a comment, the two allowed files, a look-alike name.

**Seeding proof (registries).** Added `"sprk_billingexport"` to `RecordOwnershipResolver.OwnershipParentEntities` and `("sprk_billingexport","sprk_billingexport")` to the `sprk_communication` row of `SecureChildLineage`: 5 of 8 guard tests red (the registry guard names the ownership-parent row; the behavioural check shows `ParentsOf` keeping the batch reference; the source scan names `RecordOwnershipResolver.cs`; the two controls that assert the real registries are clean). Both files restored and `cmp`-identical.

## Fallback when the column does not exist (chosen: retry the create once without it)

The BFF can be deployed before the schema. The email is sent BEFORE the row is created (`SendAsUserAsync` step 5, best effort), so a create that faults on the missing column must not lose the record. Chosen: in `CreatePreparedRecordAsync`, when the row carries `sprk_billingexport` and the create throws a fault that NAMES that column and is a missing-attribute fault (`IsMissingColumnFault`), log a Warning and create the row once more without the lookup. The classifier walks inner exceptions and, for an exception whose message names the column, accepts (a) a typed `FaultException<OrganizationServiceFault>` whose `Detail.ErrorCode` is 0x80041103 (QueryBuilderNoAttribute; the same constant `RecordOwnershipResolver.AttributeDoesNotExistErrorCode` uses; locale-independent), or (b) the message text ("doesn't contain attribute", "does not contain attribute / an attribute", "Could not find a property named", "Invalid property", "does not exist on type"). Rejected: reading the column's presence first (an extra metadata read and cache per send, stale after the schema lands). Not retried: any other fault (timeout, privilege, a fault naming another column, another error code), and any row that does not carry the reference (every other send). One retry only; its own fault goes to the existing best-effort handler.

**What a BFF deployed before the schema logs.** On every AP email in that window: an Error from the failed create (`[DATAVERSE] Error creating sprk_communication`, `DataverseServiceClientImpl.cs`, around line 2606, written by the Dataverse client before the exception reaches the fallback), then the fallback's Warning. The Error can trip error-rate alerts, so deploy the schema FIRST (deploy order below); the fallback protects the email, not the log.

Not verified live: the real fault shape for a missing attribute on create is from memory of the platform and the SDK, not from a run against an environment without the column (no Dataverse writes in this task). The text matcher and the typed code cover the two shapes we know; if both miss, the effect is the pre-existing behaviour (the row is not created and a warning is logged). A live check on a pre-schema environment is pending (K3 below).

## Tests

| File | Covers |
|---|---|
| `tests/unit/.../Communication/CommunicationServiceServerSendTests.cs` (+17) | the send sets the reference in the create (one create, no later update; owner still the batch's team; no `sprk_regarding*`; the resolver asked only about the batch); a server send without a batch id, `Guid.Empty`, and a normal send create the row without the column; a JSON body cannot set it (`serverSend`, `ServerSend`, top-level `billingExportId`, `sprk_billingexport`, `@odata.bind`); the missing-column fallback records the email (attempts `true, false`; owner untouched); other faults are not retried (3); a normal send's fault is not retried; the classifier (6 shapes); fix round: the classifier on the typed 0x80041103 fault (localized message true; same code for another column false; another code false), and the batch/owner agreement (theory "other-batch" / "other-table": 409, code, nothing sent or created, no owner request; and the agree / link-alone / owner-alone cases accepted). 36 tests in the class |
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/__tests__/secureChildRibbonScript.test.ts` (24) | fix round: the AP export batch offers no "New" (native and BFF) while other hosts are unchanged. The script and its mirror are byte-identical (`SecureChildNewCommandAgreementTests` still passes) |
| `tests/integration/auth/Finance/ApExportDeliveryTests.cs` | the success test now asserts `ServerSend.BillingExportId == batchId` and still no `Associations` |
| `tests/Spaarke.ArchTests/CommunicationBatchLinkNotFilingGuardTests.cs` (10) | above, plus the registry size floor (`EveryRegistry_WasReadAtItsMinimumSize` and its must-fire control `SizeGuard_FiresOnAnEmptyRegistry`) and the `ApExportService.BatchEntity` token |

Fail-before proofs (production line changed, tests red, restored, `cmp`-identical):

| Mutation | Red |
|---|---|
| `ServerSend` made public and `[JsonIgnore]` removed (with the option types public) | 2: the new JSON test and the 151c-1a JSON test |
| retry disabled (`when (false && ...)`) | 1: the fallback test |
| the reference write disabled (`if (false && ...)`) | 2: the create test and the fallback test |
| fix round: the batch/owner agreement check disabled (`&& false &&` in its condition) | 2: both theory cases |
| fix round: the typed-fault branch of the classifier disabled | 1: the typed-fault test |
| fix round: `NO_NEW_HOSTS` check removed from the ribbon script (HEAD copy restored over it) | Jest 1 failed / 23 passed (the new case); restored, mirror `cmp`-identical |
| fix round: a registry removed from the read (size floor) | covered by the must-fire control `SizeGuard_FiresOnAnEmptyRegistry`, which removes each of the ten registries in turn and expects exactly that one reported |
| fix round: relationship-name check | not exercised with a conflicting relationship (that needs a conflicting live relationship, a Dataverse write); `-WhatIf` against dev runs it on the real metadata and exits 0 (no conflict) |

**Fix-round notes.** (1) The source scan in the guard skips `Services/Ai`, `Services/Signals`, `Services/Workspace`, `Services/Finance`, `Infrastructure/Dataverse`, `Api/Filters` and the top-level `Api/*.cs`; the class remarks say why the risk is low (every reader of a row's lookups for ownership filters through `IsOwnershipParent`, and the registry and behavioural checks pin that the batch is not a parent). (2) Registry minimums are about half of the measured sizes (RecordFamilyEdges 162, SecureChildLineage 153, Typed 2, Intermediate 2, IntermediateRoot 38, StampSource 44, CoreAncestorLookups 4, RegardingFieldMap 12, OwnershipParents 19, RegardingLookupMap 12). (3) The batch/owner agreement is a 409 with `server_send_batch_mismatch`, raised in `SendAsync` after the shape check and before any send or write; `ApExportDeliveryService` always passes both for the same batch, so it never fires on the real path.

## Component justification (CLAUDE.md section 11)

**Column `sprk_communication.sprk_billingexport` (and its relationship)**
1. Existing: the email already carries the batch's id in its subject text only; `sprk_regarding*` columns are filings and `RegardingFieldMap` is the filing map; `sprk_invoice.sprk_billingexport` is the invoice's batch, not the email's. Nothing relates an email to a batch.
2. Extension: a `sprk_regarding*` column or an `Associations` entry was rejected by the owner and uac-r2 (it would make the email a child of the batch: filed, owner-resolved and lineage-walked). Extending `ServerSendOptions` (one more internal field) and the existing create carries it with no new service, route or DI registration.
3. Cost of doing nothing: the batch form cannot list its AP emails, finance finds them by reading subjects, and 151c-1b cannot show "emailed to AP on <date>" from the batch's own email record.

**Subgrid "AP emails"**: reuses the forms script's existing `Subgrids` support and the existing "All Outgoing Email" view; no new view.

**ArchTest guard**: reuses `SourceScan` and the registries' own accessors; its cost of doing nothing is a later edit that adds the column to a registry and silently lets a batch ownership change reach an email.

## Placement, publish size, CVE (CLAUDE.md section 10)

BFF change is a field on an existing internal option, one `if` in an existing builder and a `catch ... when` in an existing method: no endpoint, service, DI registration, package or background work. Publish size: no package or asset added; the delta is a few hundred bytes of IL. No new vulnerable package (none added).

## Deploy order

1. `pwsh scripts/ebilling/Deploy-EbillingCommunicationBatchLink.ps1 -WhatIf`, then without it (after notices). Publishes `sprk_communication` only; the Publish step also publishes that entity's pending forms/views, so check for unpublished `sprk_communication` customizations first.
2. `pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -FormsOnly -WhatIf`, then without it: adds the "AP emails" subgrid to the batch form and publishes `sprk_billingexport` (needs step 1; the form XML names the relationship).
3. The BFF, AFTER steps 1 and 2. Deploying it before step 1 does not lose emails (the create falls back to recording the email without the link), but every AP email then logs an Error plus the fallback Warning until the column exists (see above). An email sent before the column exists keeps no link (no backfill).

Step 2 stops before writing anything if the relationship is missing (`Assert-SubgridRelationship`: it reads the relationship definition and throws "run Deploy-EbillingCommunicationBatchLink.ps1 first"). Checked against dev with `-FormsOnly -WhatIf` today: exit 1 with that message, because the schema has not been run there.

## Known limits and things noticed

- **K1: re-pointing the link changes no access.** Anyone with Write on a communication and AppendTo on a batch can re-point the lookup (Add Existing on the subgrid, `Xrm.WebApi`, chat `update_record`, or an app-only playbook "Update Record"). A lookup grants nothing: the cascades are NoCascade, the batch is not an ownership parent, and no resolver reads the column. The only effect is which emails the batch's subgrid lists. No BFF route can set it: `/send` (the field is internal and `[JsonIgnore]`), `PATCH /filing` (400), and the child-record create and re-file routes (`sprk_communication` is in none of them). "Add Existing" is left on the subgrid: hiding it needs a new `CommandDefinition` override of `Mscrm.AddExistingRecordFromSubGridStandard` in the `sprk_communication` RibbonDiff, which is out of scope here.
- **K2: users could already create a `sprk_communication` from the model-driven app.** With "New" now hidden on the batch's subgrid (`NO_NEW_HOSTS`, below), the batch subgrid adds no new way to do that.
- **K3: the fallback is matched by code and text, and not yet seen live.** If the real fault matches neither the 0x80041103 code nor the phrases, the behaviour is the pre-existing one (a Warning, no row). A live check against an environment without the column is pending; this task makes no Dataverse writes, so it was not done.
- **The batch form is not "all read-only".** An earlier draft of these notes and the forms script header said so; only `sprk_senttoapdate` is disabled. The header is corrected, and the POML criterion no longer says "read-only subgrid".
- **Privileges.** The BFF writes the reference as its app identity (the sysadmin entity service), so no `prvAppendTosprk_BillingExport` grant is added to any role.
- **Pre-existing, not touched:** `CreatePreparedRecordAsync` runs after the send with the request token (the 151c-1a note on cancellation still stands).
- The 151c-1b UI (Sent to AP Date on views) can now read the email through `sprk_billingexport` if it needs the email record; it does not need it for the date.
