# Task 140 — child-record contract schema delta (live on spaarkedev1, 2026-10-12)

Decisions: D-131, D-132 (owner approved 2026-10-12: the 14 `sprk_decisionrecord` columns exactly as listed in the POML,
delete + recreate of `sprk_signal.sprk_regardingrecordtype` as a Lookup, dev only). Contract: uac-r2
`ontology-standard-child-contract-2026-10-12.md` (C1, section 4 item 1).

Method: Dataverse Web API (`POST .../Attributes`, `POST RelationshipDefinitions`), explicit PascalCase `SchemaName`,
header `MSCRM.SolutionUniqueName: OntologyPlatformSolution`, then `PublishXml` for the two entities only. No
`PublishAllXml`, no solution import, no `create_table` MCP tool. No Redis key was evicted, no security role changed.

## 1. Read-only checks (before the delete)

| Check | Result |
|---|---|
| `RetrieveDependenciesForDelete` (Attribute `bf1b9898-15a8-4a10-937a-195ae7242666`, ComponentType 2) | `value: []` |
| `RetrieveDependentComponents` for the same attribute | `value: []` |
| Alternate keys on `sprk_signal` | one: `sprk_signaldedupekey` = [`sprk_dedupekey`], Active. Does not include the column |
| Views (`savedquery`, 7 on `sprk_signal`) | none references `sprk_regardingrecordtype` (fetchxml and layoutxml) |
| Forms (`systemform`, 3) | none references it |
| Charts, personal views, plug-in steps, classic workflows on `sprk_signal` | none exist |
| Cloud flows (3 in the environment) | none mentions `sprk_signal` or `regardingrecordtype` |
| Field security permissions on `sprk_signal` | none |
| `sprk_signal` row count | 0 (so the "existing dev rows are test data" case is empty) |
| Repo source (`src/solutions`, `src/client`) reading the Signal pair type | none. Only `SpaarkeMaster/Entities/sprk_Signal/Entity.xml` (the column definition) |
| Repo BFF code touching the Signal pair type as text | one writer: `Services/Signals/SignalWriter.cs:1206` (`entity["sprk_regardingrecordtype"] = subjectEntity;`, rationale at lines 238-245). Its tests assert the string: `tests/unit/domain/Signals/SignalWriterTests.cs:155,823`, `tests/integration/seam/Signals/SignalWriterSeamTests.cs:191`. **Task 141 owns all of it**; D-132 supersedes the SignalWriter.cs:238 rationale. `BudgetInquiryExecutor`, `DecisionActionExecutors:1083`, `InvoiceReviewService`, `SecureRootInheritance` use the pair type on other tables (lookup already) |
| Deployed BFF on dev (`spaarke-bff-dev`, web app last modified 2026-10-12 12:07 UTC) | **No deployed component writes the Signal pair type**: `Ontology__Writer__ManagedIdentityClientId` is not set (211 app settings listed, none matches Ontology/Signal), so `OntologyWriterCredentialFactory` fails closed on first use and the writer cannot run; `sprk_signal` has zero rows. No escalation trigger fired |

Result: no blocker, no deployed text reader/writer, so the delete went ahead.

### 1a. BFF source that reads or writes `sprk_signal.sprk_regardingrecordtype` as a string (for task 141)

Searched with `git grep -i sprk_regardingrecordtype -- src/server` at the head of every worktree branch under `C:\wts-*`
and `C:\wc70`/`C:\wt081` (about 75 branches, the docs branch among them) and at `origin/work/unified-access-control-r2`.
None of these is deployed to dev (see the table above), so none blocks the delete; **141 rewrites all of them before the 035 deploy**.

| File:line | Branch(es) | What it does with the Signal pair type |
|---|---|---|
| `Services/Signals/SignalWriter.cs:1206` | docs branch and every lane branch carrying the writer (45 branches) | **Writes** it: `entity["sprk_regardingrecordtype"] = subjectEntity;` (a logical-name string). Becomes an `EntityReference` to the `sprk_recordtype_ref` row for the subject type |
| `Services/Signals/SignalWriter.cs:238-245` (XML doc) | same | Documents the text column as load-bearing; D-132 supersedes it. Also lines 1203-1205 (comment). The dedupe key (`{policycode}\|{subjecttype}\|{subjectid}\|{episode}`, line 693) still needs the logical-name string, so derive it from the subject entity, not from the stored value |
| `Services/Signals/SignalCoreRecordAccess.cs:115` | `task/ontology-043-commit-route` (PR #1665) only | **Reads** it: `$select=...sprk_regardingrecordtype,sprk_regardingrecordid...` on `sprk_signals({id})`. A lookup must be selected as `_sprk_regardingrecordtype_value` (the value is then the `sprk_recordtype_ref` row id and the lookup-logical-name annotation names `sprk_recordtype_ref`, not the subject entity, so resolve the entity name from that catalog row) |
| `Services/Signals/SignalCoreRecordAccess.cs:194` | same | `ReadString(signalRow, "sprk_regardingrecordtype")?.Trim().ToLowerInvariant()` = the subject type for the Do-lane subject read check. Breaks when the value is a GUID |
| `Services/Signals/DecisionCommitService.cs:285` | same | Column list for a Signal read (`"sprk_resolutiontype", "sprk_resolvedon", "sprk_regardingrecordtype", "sprk_regardingrecordid", "sprk_matter", ...`) |
| `Services/Signals/DecisionCommitService.cs:1354` (`ToRow`) | same | `e.GetAttributeValue<string>("sprk_regardingrecordtype")?.Trim().ToLowerInvariant()`: an SDK `Entity` read of the column as `string`. Against a lookup the value is an `EntityReference`, so `GetAttributeValue<string>` returns null and the subject type is lost silently (no exception) |
| `Services/Signals/DecisionCommitService.cs:1391`, `DecisionRecordWriter.cs:52`, `SignalCoreRecordAccess.cs:57` (XML docs) | same | Describe the `ItemSubject` (`sprk_regardingrecordtype` / `sprk_regardingrecordid`) taken from the Signal; update with the code |

Hits that are NOT readers of the Signal column (checked, no change needed): `BudgetInquiryExecutor.cs:125,216,283,284` and
`DecisionActionExecutors.cs:1083` write the pair type of the **To Do / Communication** they create (already a lookup);
`Services/Access/SecureRootInheritance.cs:332` (`PairTypeColumn`), `Services/Dataverse/CoreAncestorResolver.cs:294`
(`RegardingRecordTypeColumn`), `OwnedChildWrite.cs:512` and `Api/ChildRecordEndpoints.cs:100` handle the pair on root and child
tables that are already lookups; `Services/Access/SecureChildLineage.cs` lists `sprk_signal` and `sprk_decisionrecord` by their typed
`sprk_matter`/`sprk_decisionrecord` lookups, not the pair. No hit on `origin/work/unified-access-control-r2`. Tests that assert the string today:
`tests/unit/domain/Signals/SignalWriterTests.cs:155,823`, `tests/integration/seam/Signals/SignalWriterSeamTests.cs:191`.
`src/solutions`, `src/client`, `src/server/shared` and `scripts` have no reader or writer of the Signal column.

## 2. Changes made

`sprk_decisionrecord` (before: no `sprk_regarding*` column): 14 columns added.

- String, copied from `sprk_todo`: `sprk_RegardingRecordId` (100), `sprk_RegardingRecordName` (200), `sprk_RegardingRecordNumber` (100),
  `sprk_RegardingRecordUrl` (500, Format Url). Audit enabled (as on `sprk_todo`).
- Lookups (relationship `sprk_decisionrecord_reg*`, cascade Delete = RemoveLink, others NoCascade; the same shape as the
  matching `sprk_signal_reg*` relationships): `sprk_RegardingCommunication`, `sprk_RegardingDocument`, `sprk_RegardingEvent`,
  `sprk_RegardingInvoice`, `sprk_RegardingMatter`, `sprk_RegardingProject`, `sprk_RegardingServiceRequest`,
  `sprk_RegardingTodo`, `sprk_RegardingWorkAssignment`, and `sprk_RegardingRecordType` -> `sprk_recordtype_ref`
  (relationship `sprk_decisionrecord_regrecordtype_recordtype_ref`).
- Dataverse adds a read-only `*name` String companion for each of the 10 lookups (`AttributeOf` the lookup). Those are
  platform-generated, not new columns of ours.

`sprk_signal`: `sprk_regardingrecordtype` deleted (String 100) and recreated under the same logical name as Lookup ->
`sprk_recordtype_ref` (relationship `sprk_signal_regrecordtype_recordtype_ref`). DELETE returned 204, CREATE returned 204,
19 s later; the pre-flight (0 rows, no dependencies) was re-checked in the same script just before the delete. Nothing else
on `sprk_signal` changed (the 9 subject lookups and `sprk_regardingrecordid` (50) / `name` (400) / `number` (100) / `url`
(500) are as before). The recreate under the same name was accepted.

### 2a. Source catch-up for two pre-existing columns (NOT part of the 14)

The `sprk_decisionrecord` Entity.xml diff also carries `sprk_AssignedToExternal` and `sprk_AssignedToInternal` (Lookup -> `contact`).
These two columns **already existed live** (the owner added them for D-129, team-ownership) and were absent from the repo
source; this task did not create them and they are **not** among the 14 columns. They arrived in the source only because the
Entity.xml was refreshed from the live export. The matching relationships `sprk_decisionrecord_AssignedToInternal_contact` and
`sprk_decisionrecord_AssignedToExternal_contact` were missing too; they are now added to `Other/Relationships/Contact.xml` and
`Other/Relationships.xml`, so the source matches live. Cascade values read from live metadata (`RelationshipDefinitions`,
spaarkedev1, 2026-10-12): Assign NoCascade, Delete RemoveLink, Archive RemoveLink, Reparent NoCascade, Share NoCascade,
Unshare NoCascade, RollupView NoCascade, Merge Cascade (Merge is not an export attribute); IsValidForAdvancedFind true;
IsHierarchical false; menu behaviour UseCollectionName / Details / 10000. The relationship XML copies the shape of
`sprk_event_AssignedToInternal_contact` / `sprk_event_AssignedToExternal_contact` (the repo has no
`sprk_todo_AssignedToInternal_contact`; `sprk_todo` has only `sprk_todo_AssignedTo_contact`).

Publish: `PublishXml` with `<entity>sprk_decisionrecord</entity><entity>sprk_signal</entity>` only.

Note: the first metadata list read after the recreate (a few seconds later) still omitted the new lookup and its `*name`
companion; a direct attribute GET returned it and a list read a few minutes later showed it. Reading metadata straight
after a metadata write can lag; verify twice before concluding a create failed.

## 3. Final live column list (metadata read, spaarkedev1, after publish) — for uac-r2

### sprk_decisionrecord

```
sprk_regardingcommunication                sprk_RegardingCommunication        Lookup  targets=sprk_communication
sprk_regardingdocument                     sprk_RegardingDocument             Lookup  targets=sprk_document
sprk_regardingevent                        sprk_RegardingEvent                Lookup  targets=sprk_event
sprk_regardinginvoice                      sprk_RegardingInvoice              Lookup  targets=sprk_invoice
sprk_regardingmatter                       sprk_RegardingMatter               Lookup  targets=sprk_matter
sprk_regardingproject                      sprk_RegardingProject              Lookup  targets=sprk_project
sprk_regardingrecordid                     sprk_RegardingRecordId             String  maxlen=100 format=Text
sprk_regardingrecordname                   sprk_RegardingRecordName           String  maxlen=200 format=Text
sprk_regardingrecordnumber                 sprk_RegardingRecordNumber         String  maxlen=100 format=Text
sprk_regardingrecordtype                   sprk_RegardingRecordType           Lookup  targets=sprk_recordtype_ref
sprk_regardingrecordurl                    sprk_RegardingRecordUrl            String  maxlen=500 format=Url
sprk_regardingservicerequest               sprk_RegardingServiceRequest       Lookup  targets=sprk_servicerequest
sprk_regardingtodo                         sprk_RegardingTodo                 Lookup  targets=sprk_todo
sprk_regardingworkassignment               sprk_RegardingWorkAssignment       Lookup  targets=sprk_workassignment
-- platform-generated read-only companions (AttributeOf the lookup): sprk_regarding{communication,document,event,invoice,matter,project,recordtype,servicerequest,todo,workassignment}name
```

Navigation properties (Web API `@odata.bind`): `sprk_RegardingCommunication`, `sprk_RegardingDocument`, `sprk_RegardingEvent`,
`sprk_RegardingInvoice`, `sprk_RegardingMatter`, `sprk_RegardingProject`, `sprk_RegardingServiceRequest`, `sprk_RegardingTodo`,
`sprk_RegardingWorkAssignment`, `sprk_RegardingRecordType` (the same on `sprk_signal`). Existing typed context lookups
`sprk_matter` / `sprk_project` / `sprk_workassignment` and the immutable `sprk_corerecordid` / `sprk_corerecordtype` are untouched.

### sprk_signal (the `sprk_regarding*` set after the change)

```
sprk_regardingcommunication                sprk_RegardingCommunication        Lookup  targets=sprk_communication
sprk_regardingdocument                     sprk_RegardingDocument             Lookup  targets=sprk_document
sprk_regardingevent                        sprk_RegardingEvent                Lookup  targets=sprk_event
sprk_regardinginvoice                      sprk_RegardingInvoice              Lookup  targets=sprk_invoice
sprk_regardingmatter                       sprk_RegardingMatter               Lookup  targets=sprk_matter
sprk_regardingproject                      sprk_RegardingProject              Lookup  targets=sprk_project
sprk_regardingrecordid                     sprk_RegardingRecordId             String  maxlen=50  format=Text
sprk_regardingrecordname                   sprk_RegardingRecordName           String  maxlen=400 format=Text
sprk_regardingrecordnumber                 sprk_RegardingRecordNumber         String  maxlen=100 format=Text
sprk_regardingrecordtype                   sprk_RegardingRecordType           Lookup  targets=sprk_recordtype_ref   <-- was String(100)
sprk_regardingrecordurl                    sprk_RegardingRecordUrl            String  maxlen=500 format=Text
sprk_regardingservicerequest               sprk_RegardingServiceRequest       Lookup  targets=sprk_servicerequest
sprk_regardingtodo                         sprk_RegardingTodo                 Lookup  targets=sprk_todo
sprk_regardingworkassignment               sprk_RegardingWorkAssignment       Lookup  targets=sprk_workassignment
```

Differences to know when 141 shares the builder: `sprk_signal.sprk_regardingrecordid` is 50 (a GUID fits) where DR/To Do use 100, and
`sprk_signal.sprk_regardingrecordurl` is plain text (500) where DR/To Do use Format Url (500).

## 4. Solution source

Both tables are whole-table components (`rootcomponentbehavior 0`, subcomponents included) of `OntologyPlatformSolution`
1.0.0.0, `SpaarkeMaster` 1.2.1.0 and Default, so the new columns and relationships flow into the SpaarkeMaster export. The repo's
source of record is `src/dataverse/solutions/SpaarkeMaster/` (there is no separate `OntologyPlatformSolution` folder in git). It
was refreshed from the live export with `scripts/solution-authoring/Export-SpaarkeMasterSource.ps1` into a temp folder; only the
files that describe these two tables (and the relationship files that reference them) are copied into the repo, so unrelated
dev drift does not enter this PR. See the PR description for the exact file list.

The SpaarkeMaster **version was not bumped** (a release step, `Assemble-SpaarkeMasterSolution.ps1 -Version`; per
SPAARKE-SOLUTION-RELEASE-PROCESS.md Major = breaking schema change, which this is for any environment that already has the String column).
The new components keep `IntroducedVersion` `1.0.0.0`; the bump belongs to the release step and must be **Major**, in two steps for managed environments (an intermediate version without the String column, applied with StageAndUpgrade, then the version with the lookup; see the deployment guide section 10.5.B).

## 5. Environments that have `sprk_signal` today

| Environment | `sprk_signal` / `sprk_decisionrecord` / `sprk_policy` |
|---|---|
| spaarkedev1 (SPAARKE DEV 1) | present; converted by this task |
| spaarke-demo (production) | absent (404) |
| spaarke-model1-prod | absent (404) |
| Live chat requests (Teams, orgd59e0b4e) | absent (404) |
| Spaarke Inc. (default) | no Dataverse instance |

The published packages `dataverse-solution-SpaarkeMaster-1.2.0.0` and `1.2.1.0` (provisioning store `sprkcpartifactsdev`) carry the old String
column, so an environment provisioned from them would need the runbook; none exists today. Runbook (unmanaged and managed variants):
`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` section 10.5.

## 6. Redis metadata cache (affects the 146 live gate)

`MetadataService` caches entity metadata under `sdap:dv:entitymetadata:{logicalName}` with a 6 h absolute TTL
(`Services/Dataverse/MetadataService.cs:47`). The BFF's cached view of `sprk_decisionrecord` (and `sprk_signal`) predates these changes. **The
146 live gate must not run until the key has expired (up to 6 h after the last cache fill) or the owner approves an eviction.** This task
did not evict anything.

## 7. Noticed, not changed

- `projects/spaarke-ontology-platform-r1/notes/schema-draft.md` (Signal row, line 146) and `spec.md` FR-03 described the Signal pair
  type as text / read by the writer; both are corrected in the fix round for this PR (D-132). `docs/data-model` has **no page that
  describes the Signal pair type or the Decision Record regarding columns** (checked: no page documents `sprk_signal` or
  `sprk_decisionrecord`; `sprk_spendsignal.md` is an unrelated table), so there is nothing to fix there; a new page is a 141/146
  decision.
- Both tables are in `Default` as well; no action.
- The forms on `sprk_decisionrecord` / `sprk_signal` were not touched (no regarding picker was requested).
