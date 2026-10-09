# Child Access Permission — `sprk_accesspermission` on To Do, Event, Communication and Document

> **Last Reviewed**: 2026-10-08
> **Reviewed By**: unified-access-control-r2 task 173 (owner rounds 81 and 84, GitHub #1423)
> **Status**: Current (BFF and scripts in source; live rollout is the main session's gate)
> **Solution**: `SpaarkeCore` (the column ships with each table: rootcomponentbehavior 0)

## The rule

| Record | `sprk_accesspermission` holds | Who writes it | On the form |
|---|---|---|---|
| **Has a parent** (any non-null filing lookup) | The MOST RESTRICTIVE own value of the records at the TOP of its filing: Restricted (100000002) over Limited (100000001) over Standard (100000000). A null, or a top whose table has no such column, counts as Standard | The BFF only: the shared stamp path at create (`CoreAncestorResolver`), and `SecureChildReconciliationJob` every 2 minutes for everything else | Locked, with the info notification "Access permission is inherited from {parent}" |
| **No parent** | The value its user set | The user | Editable |

- **Display only.** No access decision reads a child table's `sprk_accesspermission`. A child's access comes from its parent root (`sprk_project`, `sprk_matter`, `sprk_workassignment`): `ExternalParticipationService.GetRootRecordFlagsAsync` and the secure-root path read the ROOT flags (owner Q6, 2026-09-30). A parentless child's own value is recorded only — owner, 2026-10-08: "This is fine (BUT in future we might need to revisit)".
- **The top of a filing.** Walk the record's filing lookups up to records that have no filing parent of their own: a root (project, matter, work assignment), or a parentless child (a document under nothing, an unfiled invoice, a thread with no regarding). A To Do under a document under a Restricted matter is Restricted; a To Do under a parentless document whose own value is Restricted is Restricted.
- **Un-filing keeps the last value.** A record whose last parent is removed keeps the value it had, which becomes editable (round 81).
- **Undecidable filings are left alone.** A parent that cannot be read, does not exist, a filing deeper than 6 levels, or one that loops without reaching a record that has no parent: nothing is written and a warning is logged (`[INHERITED-ACCESS-PERMISSION]` at create, `[CHILD-ACCESS-PERMISSION]` in the job). The create never fails because of it.
- **Work assignments and projects** filed under a parent follow their parents for `sprk_accesspermission` AND `sprk_issecure` (owner round 84): enforcement through the filing walk (task 174), the stored values through `SecureRootInheritance.FollowParentsAsync` (task 175; write-path invariant I-17), locked on the work assignment and project main forms by the same library.

## Which lookups are "filing" lookups

The ONE map is `SecureChildLineage` (BFF, `Services/Access/SecureChildLineage.cs`), minus one link that is the record itself (`ParentLineage.NotFiling`): a document's `sprk_currentversionid` names its own file version. The form library carries the same map literally; `ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap` fails the build when they differ.

| Table | Filing lookups (column → parent table) |
|---|---|
| `sprk_todo` | `sprk_regarding{agreement, analysis, budget, communication, document, event, invoice, matter, project, reportcard, workassignment}` |
| `sprk_event` | `sprk_regarding{agreement, analysis, budget, communication, event, invoice, matter, project, reportcard, workassignment}` |
| `sprk_communication` | `sprk_communicationthread`; `sprk_regarding{analysis, budget, event, invoice, matter, project, reportcard, workassignment}` |
| `sprk_document` | `sprk_matter`, `sprk_relatedmatter`, `sprk_project`, `sprk_relatedproject`, `sprk_invoice`, `sprk_relatedinvoice`, `sprk_workassignment`, `sprk_relatedworkassignment`, `sprk_relatedagreement`, `sprk_relatedcommunication`, `sprk_relatedevent`, `sprk_relatedtodo`, `sprk_canonicaldocument`, `sprk_parentdocument` |

**Not filing** (they never lock the field, and never give it a value): a contact, an organization or an account (parties), a service request (its own root, never looked through), an email activity, a document's own current version.

## The columns

All four are Choice columns bound to the GLOBAL choice `sprk_accesspermission` (MetadataId `184fab40-6d75-f111-ab0e-7ced8ddc4a05` on spaarkedev1) — the same choice the three roots use, so a copied value means the same thing. Default value Standard; not required.

| Entity Display Name | Entity Logical Name | Logical Name | Schema Name | Display Name | Attribute Type | Description | Custom Attribute | Type | Additional data |
|---|---|---|---|---|---|---|---|---|---|
| Document | sprk_document | sprk_accesspermission | sprk_AccessPermission | Access Permission | Choice | Inherited from the records the document is filed under (most restrictive); a document with no parent keeps its own value. Display only. Added live by the owner 2026-10-08 (MetadataId `4b469c31-1ac3-f111-a05a-7c1e520a989f`); reproduced in any environment by `scripts/Set-DocumentAccessPermissionSchema.ps1` | True | Simple | Global choice `sprk_accesspermission`: 100000000 Standard, 100000001 Limited, 100000002 Restricted. Default: Standard |
| To Do | sprk_todo | sprk_accesspermission | sprk_AccessPermission | Access Permission | Choice | As above (display copy of the parent's) | True | Simple | Same global choice |
| Event | sprk_event | sprk_accesspermission | sprk_AccessPermission | Access Permission | Choice | As above | True | Simple | Same global choice |
| Communication | sprk_communication | sprk_accesspermission | sprk_AccessPermission | Access Permission | Choice | As above. Was to be retired by task 138 (owner Q6); round 81 keeps it as a display copy | True | Simple | Same global choice |

`scripts/Set-DocumentAccessPermissionSchema.ps1 -Verify` checks the document column: it exists, is bound to the roots' choice, and ships in SpaarkeCore.

## Where it is written, and how fast

| Path | Mechanism | When the value is right |
|---|---|---|
| Every BFF create through `CoreAncestorResolver.StampAsync` / `DeriveForHostAsync` (Office To Do, workspace To Do builder, AI task action, communication service, AI email draft, event create) and the inbound association update | Inline, in the same payload | At create |
| Every other create or re-file: documents (all paths), client `Xrm.WebApi` and model-driven forms, imports, flows, BFF re-file routes (`PATCH /api/v1/child-records`, event and communication filing, `PUT /api/v1/documents/{id}`) | `SecureChildReconciliationJob` → `ChildAccessPermissionReconciler`: rows changed since its watermark and everything filed under them | Within one 2-minute run |
| A parent's value changes (a matter turns Restricted) | The same pass walks DOWN from every changed row (at most 6 levels) | Within one run |
| A hand edit of a parented child | The same pass (the row changed) | Reverted within one run |
| Rows nobody changed (created before task 173; drift after a restart) | The same pass's capped sweep window (`SecureChild:Reconciliation:MaxAccessPermissionRowsPerRun`, default 1000) until the instance has covered every row once | Within ceil(rows / cap) runs of a start |

Report: the job's `ResultJson.accessPermission` (`changed`, `parentless`, `undetermined`, `deferred`, `writeCapReached`, `changes[]`, `sweep`). A listing fault or a failed write makes the run unsuccessful; an undecidable row is reported and carried.

**Write cap.** At most `SecureChild:Reconciliation:MaxAccessPermissionWritesPerRun` writes per run (default 500). A run that reaches it stops writing and keeps its window (the watermark and the sweep cursor stay), so the next run lists the same rows and writes the next ones; only differing rows are written, so a matter with thousands of children converges over several runs. The pass runs after the secure pass has saved its own cursor and watermark.

## The form

`sprk_accesspermission_inherited` (`src/client/webresources/js/sprk_accesspermission_inherited.js`), OnLoad `Spaarke.AccessPermissionInherited.onLoad`, registered by `scripts/Set-InheritedAccessPermissionFormLock.ps1` on every Main and Quick Create form of the four tables that shows the column (the TrackingFieldTrio pill on the To Do and Event main forms; a plain control the script adds to the Communication "Message main form" and the "Document main form" — no TrackingFieldTrio on Communication, owner round 81). The library writes nothing: it disables the controls it found enabled, stops the column being submitted, and puts back a change a bound PCF makes, while the record has a parent.
