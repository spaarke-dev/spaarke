# Task 052e: repair pass family cascade (design)

POML: `tasks/052e-repair-family-cascade-design.poml`. Rigor: FULL. This is a design note: no code, no live write, no setting changed. The read-only dev facts below were read on 2026-11-18 with the operator's `az` identity, using GETs only. Revision 2 applies the adversarial review (§7).

**Outcome required** (spec FR-25, amended 2026-11-02, owner, binding): for any record created outside the server-mediated owner process, the repair process brings **every member of that record's family to the same owner**:
- the root to its standard owner;
- each child to its family's owner, including children that exist when a root is repaired;
- including the cross-business-unit re-own that task 016 left open.

The mechanism is the project's choice.

**Recommendation in one paragraph.** Build no new family walk. Repair keeps deciding one row at a time with the create owner decision. The family follows through what already exists: UAC-r2's ordinary-moves pass (task 194), the secure pass and the re-file pass. Two blocks in Repair are replaced, and one guard is added:
- **The table-level Assign-cascade refusal.** Today it blocks every matter, project, event, document, invoice, analysis and contact. It is replaced by an allow-list of relationships whose cascade moves nothing or is intended, plus a per-row check for children along the rest.
- **The cross-business-unit skip.** It is replaced by a cross-unit re-own.
- **The added guard.** The rule "Repair never writes an owner in the Secure Record business unit" becomes its own explicit guard, so it does not depend on the skip being replaced.

Two follow-on tasks implement this: 052f and 052g. One narrow residual, the files of documents filed directly under a non-root row moved across units, is UAC-r2's (their issue 2903). Eight owner questions are in §5; UAC-r2 has answered theirs (§5).

## 1. Facts

### 1.1 What the pass covers (code)

The pass covers 20 tables. `RecordRepairPassJob.Tables` is `RecordTableRules.All` minus `ExcludedTables` (`RecordRepairPassJob.cs:149-175`):
- sprk_matter, sprk_project, sprk_workassignment, sprk_todo, sprk_event, sprk_memo, sprk_invoice, sprk_reportcard
- sprk_analysis, sprk_document, sprk_budget, sprk_kpiassessment, sprk_billingevent
- sprk_invoicesubmission, sprk_invoiceapproval, sprk_externalidentifier, sprk_invoicecorrection
- sprk_communication, sprk_servicerequest, contact

Excluded: task and account (not repaired), sprk_procedureversion (not created here), and sprk_signal, sprk_decisionrecord and sprk_budgetrevision (append-only, or created only by their writer).

Of the 20, 15 are ownership parents (`RecordOwnershipResolver.IsOwnershipParent`, list at `RecordOwnershipResolver.cs:2050-2083`). The five that are not are sprk_invoicesubmission, sprk_invoiceapproval, sprk_externalidentifier, sprk_invoicecorrection and contact.

### 1.2 How Repair decides an owner today

`RecordCreationService.RepairOwnerAsync` (`RecordCreationService.cs:1572-1722`) asks the create owner decision (`ResolveOwnerOverAsync` → `RecordOwnershipResolver.ResolveOwnerAsync`).

- **Which rows it may re-own.**
  - Only a `systemuser`-owned row is re-owned. A team-owned row is kept (`OwnerTeamOwned`).
  - A root whose `sprk_issecure` is not `false` is kept (`OwnerRootSecure`).
  - `OwnerDecided` (`:1513-1533`) refuses a Secure answer for an inheriting root and for a non-reparentable table. For a **reparentable child** it returns the Secure team.
- **The answers.**
  - **A child:** the default owner team of its primary parent's business unit.
  - **A person-owned primary parent** is looked through to its own filing, up to 4 levels (`RecordOwnershipResolver.cs:4375-4450`).
  - **An unfiled root:** its person's business-unit team, which is the unit it already sits in.
- **Where the owner is not written today.** In both cases below the mapping and the stamp are still written.
  1. **Cross-unit skip.** If the answer is in another business unit and the table is an ownership parent, Repair writes no owner: `record_repair.owner_moves_business_unit` (`:1707-1716`). This was task 016's fail-closed default pending the owner; the FR-25 amendment has since decided it.
     - Repair leaves isolated rows to the secure pass. 052g states this as its own guard (§3.3.1).
  2. **Assign-cascade refusal.** If a table has any one-to-many relationship that cascades on Assign to a child not on `RecordCreationService.AcceptedAssignCascades` (empty today), no row of that table gets an owner: `record_repair.owner_assign_cascades` (`RecordCreationService.Repair.cs:284-339`; 052b K9, K11).

### 1.3 What already moves a family (UAC-r2's code; we change none of it)

| Mechanism | What it moves | Trigger | Switch |
|---|---|---|---|
| **Ordinary-moves pass**, task 194: `SecureChildReconciler.ReconcileOrdinaryParentMoveAsync` (`SecureChildReconciler.cs:666`), driven by `SecureChildReconciliationJob` every 2 minutes | Every descendant of an ordinary matter, project, work assignment or service request that a business unit's **default team** owns, moved to the resolver's current answer. Fixpoint rounds make a grandchild follow its moved child. Each move is an If-Match write, logs the previous owner and is read back. It never moves a row into or out of isolation. | The root's `modifiedon` falls in the job's window. The listing does not filter on who modified the root, so a Repair owner write on a root triggers it. | `recentWrites && SecureChild:Reconciliation:OrdinaryMovesWritesEnabled` (`SecureChildReconciliationJob.cs:320-322`). The dev deploy procedure requires the latter to be `true` (`docs/procedures/dev-bff-deploy-coordination.md` step 6). |
| **Root-owner step**: `SecureRootInheritance.Cascade` → `SecureChildReconciler.ReownFiledRootAsync`, every 5 minutes | A person-owned, or other-unit default-team-owned, ordinary **work assignment or project**, moved to its family's team. Never matters. At most 25 a run. | Its own listing | `RootOwnerWritesEnabled` AND the switch above |
| **Re-file pass** (#2159) | A document's **file**, moved to the container of the record it is filed under | The document's own `modifiedon`; documents filed directly under a changed **root**; the document ids an ordinary walk hands it | — |
| **Secure pass** | Everything into and out of isolation | Its own | — |

None of the four moves the following:
- a **user-owned** row. The 194 pass reports one when its answer is in another unit (`SecureChildReconciler.cs:3018-3023`), when its question is refused or faults, or when the answer is the Secure team. **A same-unit answer is never reported** (`:2492`).
- a row owned by a non-default team;
- a root's access-grant rows.

The rows created outside the product are exactly those user-owned rows. **Repair is their one writer; the 194 pass is the one writer of default-team rows.**

### 1.4 Live Assign cascades on dev (read-only, 2026-11-18)

**Query.** `GET {org}/api/data/v9.2/RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencingAttribute,CascadeConfiguration,IsCustomRelationship` returned 13,207 relationships. Filtered to a referenced table that is `sprk_*` or `contact`, with Assign not `NoCascade`, that leaves 50, all `Cascade`. Ownership types come from `EntityDefinitions(LogicalName=…)?$select=OwnershipType,IsActivity`.

**Org setting.** `organization.sharetopreviousowneronassign` = `false` on dev. When it is on, every Assign shares the record back to its previous owner.

**No family filing edge cascades on Assign.** That covers every `RecordFamilyEdges` and `SecureChildLineage` lookup, so the platform cannot carry a family.

| Pass table | Cascades on Assign to (child, ownership) |
|---|---|
| sprk_matter | team (BusinessOwned); sharepointdocumentlocation (UserOwned, stored); sharepointdocument (UserOwned, **not stored**) |
| sprk_project | the same three |
| sprk_event | the same three; annotation (UserOwned) |
| sprk_document | team |
| sprk_invoice | team |
| sprk_analysis | sprk_analysischatmessage, sprk_analysisemailmetadata, sprk_analysisworkingversion (UserOwned; parental by design, `sprk_analysis.xml:200, 231, 340`); annotation; activities (appointment, chat, email, fax, letter, phonecall, recurringappointmentmaster, socialactivity, task, adx_inviteredemption, adx_portalcomment) |
| contact | team; annotation; contact (child contacts); the activities above; actioncard, socialprofile, mailboxtrackingfolder, syncerror, sharepointdocumentlocation, sharepointdocument (UserOwned); adx_externalidentity (OrganizationOwned) |
| the other 13 pass tables | nothing |

**Children an Assign does not move:**
- `team` and `adx_externalidentity` have no owner columns.
- `sharepointdocument` is not stored in Dataverse; its rows are the files SharePoint lists under a location. A read of it is refused while SharePoint integration is off, as it is on dev (`AssignCascadeChildOwners.cs`, remarks).

**What the empty allow-list blocks today:** no matter, project, event, document, invoice, analysis or contact is re-owned.

## 2. Options

| | A: accept the parental cascades, so Dataverse moves the children | B: Repair per row, plus the existing passes (recommended) | C: Repair walks the family itself |
|---|---|---|---|
| Moves the family? | **No.** No filing edge cascades. A cascade would also copy the parent's owner instead of I-6's per-child answer (UAC guide §7, "Why the platform cannot do it"). | **Yes.** Repair moves the user-owned rows it selects. The 194 pass moves default-team rows under a changed root. The secure pass owns isolation. | **Yes**, but it duplicates the task 194 engine (same walk, resolver and guards), giving two writers of one invariant (ADR-002; root §11). |
| Cross-unit | n/a | Allowed (§3.3) | Its own code |
| Secure | n/a | Never Repair's: a new explicit guard (§3.3) | Re-implements UAC-r2's guards |
| W7 reader | n/a | Not used: it reads as the caller (impersonated) and a system job has none. The app-only walk is the reconciler's. This deviates from FR-25's wording "uses the record family service (W7)": **owner question Q7.** | Needs an app-only mode added to W7 |

## 3. Recommendation (option B)

### 3.1 Allow-list (`AcceptedAssignCascades`, key `"{table}->{child}"`)

| Class | Entries | Reason |
|---|---|---|
| Moves nothing | `->team` from matter, project, event, document, invoice and contact; `contact->adx_externalidentity`; `->sharepointdocument` from matter, project, event and contact | No owner columns, or not stored in Dataverse. These relationships are **never read per row** (§3.2). |
| Follows the record (intended) | `->sharepointdocumentlocation` from matter, project, event and contact | The record's own document location. The 194 pass leaves it to the root's own Assign, and the owner accepted the same cascade for the forward move into the secure team (UAC-r2 round 4 item 3). |
| Follows the record (intended) | `sprk_analysis->sprk_analysischatmessage`, `->sprk_analysisemailmetadata`, `->sprk_analysisworkingversion` | The analysis's own content, parental by design |
| Follows the record (owner Q2, 2026-11-18) | `sprk_event->annotation`, `sprk_analysis->annotation` | **Accepted** (owner decision, confirmed in this session; UAC-r2 agrees). The Assign carries the notes owned by the record's owner (UserOwned cascade) to the new owner team, which can then read them. Notes owned by other people stay with them. Spaarke does not use the out-of-the-box Notes, so this is expected to be rare. The §3.3.1 guard sits on the parent: a parent whose answer or current owner is isolated, or whose isolation read faults, is skipped whole, and its notes never move. |
| Not accepted (UAC-r2, U4) | `contact->annotation` | The same widening, into the customer unit. A contact with a note keeps its owner and is counted. |

**Never accepted.** Listed explicitly so that a later reader cannot add them by habit:
- **every activity table**: a person's email, task or appointment changes hands only through that person or the product;
- **`contact->contact`**: a child contact is a customer in its own right;
- **`contact->` actioncard, socialprofile, mailboxtrackingfolder and syncerror**;
- **any relationship to `sprk_signal`, `sprk_decisionrecord` or `sprk_budgetrevision`** (D-204; none cascades today).

### 3.2 Per-row cascade check (replaces the per-table refusal)

**Today:** one unaccepted relationship on a table blocks every row of that table.

**Proposed:** a row is blocked only when it actually has a child along an unaccepted relationship.

- **The read.**
  - App-only, `top 1` per unaccepted relationship, on its referencing column.
  - The activity tables collapse into one `activitypointer` read on `regardingobjectid`, which covers every activity type including the `adx_*` ones.
  - The metadata read stays once per table per pass.
- **The outcome.**
  - A row with such a child keeps its owner: `record_repair.owner_assign_cascades`, now with the relationship schema names in the log line and a per-table count.
  - A row without one gets its owner written.
- **Fail closed:**
  - A failed or refused read is `record_repair.owner_assign_cascade_unknown` (transient, retried), never "no children". A row still unknown after its 12 attempts is abandoned, and 052d's report lists it.
  - "No children" is trustworthy only if the BFF app user reads every table checked at organization depth; an app-only read sees only what its role's depth reaches. On dev the BFF app users are System Administrators.
    - **Pre-enable check (§6):** the app user has organization-level Read on `activitypointer`, `annotation`, `contact` and each per-row table.
    - **Test (052f):** a read limited by depth is never "no children".
  - A relationship whose Assign value is unstated still counts as cascading.
- **The race.**
  - The check runs before the mapping reads (`Repair.cs:182`), and the write follows them (`:246`), so the window is seconds.
  - A child added in that window moves with the Assign. 052f closes the window after the fact: after a confirmed owner write, it reads each unaccepted relationship again for children created since the check began.
  - A hit is logged and counted as `owner_assign_cascade_raced`, with the child's table and id, so it can be put back. Known limit K1: the move itself is not undone automatically.
  - **UAC-r2 (U2): the log and count are enough; the snapshot is not extended.** A raced child goes to the parent's new owner, which is what the family rule gives anyway, and the reconcile jobs re-derive it. A parent in an isolation state is never written (§3.3.1), so a race cannot move a child into or out of isolation.
- **Skipped rows are not revisited.** A row skipped as `owner_assign_cascades` is not met again by the `createdon` window, even after its child is removed. The 052c on-demand run is how it is retried (K2).

### 3.3 Cross-unit re-own and the new Secure guard (replaces `owner_moves_business_unit`)

1. **New guard (in 052g): Repair leaves isolated rows to the secure pass.** Shape set by UAC-r2 (2026-11-18), binding.
   - **Where it sits.** At the single Repair owner write site (`RecordCreationService.cs:1709-1718`), before the cross-unit decision. The mapping cannot write `ownerid` (`CommonServerOwnedColumns`, `RecordTableRules.cs:57-58`).
   - **One predicate, keyed on the Secure Record unit.** It uses the existing shared isolation read, never a hard-coded team id:
     - for the current owner, `SecureChildReconciler.IsSecureTeamOwnedAsync` through `RefileIsolationRead.BeforeWriteAsync`;
     - for the answer team, its owning unit tested against the configured `SecureRecord:BusinessUnitName` unit.
   - **When it refuses.** The owner is kept (`record_repair.owner_left_to_secure_pass`, counted) when any of these holds:
     - the answer is in the Secure Record unit;
     - the current owner is in the Secure Record unit;
     - the answer or the current owner is one of the restricted-content owner teams (UAC-r2 194b R4b-2).
   - **Fail closed.** A faulted isolation read, or a row whose owning unit cannot be read, counts as isolated: skip and count, never write.
   - **Keeps the mapping block.** The guard keeps the row's F1-b mapping block (`mappingBlocked`, `:1679-1683`).
   - **Tests.** Must-fire for each condition. A must-not-fire twin: an ordinary cross-unit move. UAC-r2 reviews 052g's diff of this guard.
2. **Cross-unit write.** When the answer is in another ordinary business unit, Repair writes it as today, with If-Match, eviction and a read-back. `owner_moves_business_unit` is retired as a skip. The log line carries the table, the id and both unit ids, and is counted as `ownerMovedBusinessUnit`.
3. **When the row is a root** (matter, project, work assignment or service request), nothing more is needed. Its `modifiedon` changed, so the 194 pass walks its default-team descendants within about 2 minutes, under UAC-r2's own switches and emergency stop. The re-file pass moves the files of documents filed directly under it.
4. **When the row is a non-root ownership parent** (for example an event filed under a matter in another unit). Here "root" means matter, project, work assignment and service request, which step 3 covers.
   - **Children.** Each of the other 11 ownership parents in the pass is looked through when person-owned and filed (`IsLookThroughParent`, `RecordOwnershipResolver.cs:2209`; `:4365-4428`). So in-product children created while it was filed already sit on the family's unit, and re-owning the parent aligns it with them.
   - **Not looked through.** A matter, a service request and an unpaired project. They are roots, and Repair's answer for them is their own person's unit, so they never move across units.
   - **A child created while its parent was unfiled** keeps the parent's then unit (K3).
   - **Residual.** The files of documents filed directly under that parent. A file's container follows the record it is filed under, but the re-file pass watches documents under a changed root only.
   - **Recommendation: no in-process hand-off from Repair.** Repair logs and counts the move (`ownerMovedBusinessUnit`, with the family root's id when the row's stamp names one).
   - **The files of documents directly under a non-root parent.** These follow that parent's unit, and the same applies after any MDA Assign. That work is UAC-r2's, tracked in issue 2903 (U1). It does not block 052e, 052f or 052g.
   - **Why no hand-off.** A Repair call into `ReconcileOrdinaryParentMoveAsync` was considered and rejected:
     - it would need UAC-r2's two-key write predicate (switch and emergency stop);
     - it would need a defined family root (multi-root and no-root rows);
     - the document-id carry it would drop is the one the job feeds to the re-file pass;
     - it would run outside UAC-r2's caps and time slice;
     - it would make the repair pass itself write pre-go-live rows.

     In practice it would usually find nothing, because look-through has already aligned the children.

### 3.4 Two writers of a work assignment's or project's owner

Repair and the root-owner step both re-own a person-owned ordinary work assignment or project:
- Both ask the same resolver and write If-Match with a read-back. Whichever runs first wins; the other finds `AlreadyCorrect` or `OwnerTeamOwned`. It is one rule (I-6) with two triggers, not two owners of different rules.
- **Keep both** (UAC-r2, U3). Every ownership write goes through `RecordOwnershipResolver`. Repair covers the time before the root-owner step's 25-a-run cap reaches the row.
- **A 412 on the If-Match write means someone else settled the row.** Repair re-reads it once, with no retry loop.

### 3.5 Forward-only and the family

- **Repair's own writes.** Repair still never writes a row created before go-live. Under §3.3 it no longer hands off, so it has no indirect path to such a row either.
- **Pre-go-live default-team children of a root Repair re-owned.** The 194 pass moves them, exactly as after any MDA Assign. **Owner question Q3.** Recommended: accept. It is UAC-r2's existing behaviour, and FR-25's forward-only rule binds the repair pass's writes.
- **Pre-go-live user-owned children of a repaired root.** These stay where they are, and **no report lists them**: the 194 pass reports a user-owned child only when its answer is in another unit, and Repair does not walk. **Owner question Q4.** Recommended: accept as "no backfill", named here as known limit K3.

### 3.6 Who is not on the family owner after this design (named, not silent)

| Case | Why | Seen where |
|---|---|---|
| A secure child, and anything into or out of isolation | The secure pass owns it (§3.3 guard) | Repair: `owner_left_to_secure_pass` |
| `sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision` | Never written (D-204) | — |
| A pre-go-live user-owned child | Forward-only, no backfill (Q4) | Nowhere (K3) |
| A row owned by a non-default team | Repair keeps team-owned rows | 194 report, when in another unit |
| A row with an activity or child-contact child, or a contact with a note child | Never cascaded (Q5, U4) | `owner_assign_cascades` |
| A note owned by someone other than its event's or analysis's owner | The UserOwned cascade moves only the owner's notes (Q2) | Nowhere (the note keeps its author) |
| A root whose `sprk_issecure` is empty | Repair keeps it (`OwnerRootSecure`); the 194 job lists only `false` | Repair report |
| A communication that keeps its creator (E1), and its content rows | E1 rule (`RecordOwnershipResolver.cs:941-946`) | Repair report (kept) |
| Files of documents filed directly under a non-root row moved across units | UAC-r2 issue 2903 (U1) | `ownerMovedBusinessUnit` log line |
| An invoice-family row whose update the invoice-family guard refuses | The whole write is skipped, owner included (`Repair.cs:225-226`) | Repair report (that guard's code) |
| A row whose owner question is refused or faults (parent unresolved, lineage deeper than 4, read fault) | Kept, retried, abandoned after 12 attempts | Repair report; 052d abandoned list |
| A person-owned child created while its parent was unfiled; user-owned rows in tables the pass does not cover (agreement, policy, procedure assignment, ruleset, spend signal and snapshot, task, account) and user-owned activities under a moved row | Not selected by Repair; the 194 pass moves only default-team rows | Nowhere (K3) |

## 4. Follow-on tasks (created after approval)

### 052f: Assign-cascade allow-list and per-row check

FULL rigor, Opus. Files:
- `RecordCreationService.Repair.cs`
- `RecordRepairPassContext.cs`
- a child-presence read (`IGenericEntityService`, or FetchXML through the existing query seam)
- tests

ACs:
1. The §3.1 entries (plus Q2's answer), each with its reason in code. Moves-nothing entries are never read per row.
2. Re-owned: a matter, project, document, invoice or contact row with no unaccepted child.
3. Owner Q2 (2026-11-18): an event or analysis row with a note child is re-owned and its owner's note moves with it (must fire). Negative: an event or analysis whose answer is isolated is skipped whole and its note is untouched (must not fire; with 052g's guard). Negative: a contact row with a note child is not re-owned (`owner_assign_cascades`, counted; U4).
4. Negative: an analysis row with an email activity is not re-owned (`owner_assign_cascades`, with the relationship named).
5. Negative: a contact with a child contact is not re-owned.
6. Negative: a failed, refused or depth-limited child read gives `owner_assign_cascade_unknown`, never "no children".
7. Negative: a row still unknown after its attempts is abandoned and appears in the 052d report.
8. Negative: an unstated Assign value counts as cascading.
9. The post-write re-read logs `owner_assign_cascade_raced` for a child created after the check.
10. Negative: no write ever reaches an activity, a child contact, `sprk_signal`, `sprk_decisionrecord` or `sprk_budgetrevision`.
11. A must-fire test for each rule with that rule switched off. Known limits K1 and K2 recorded.

### 052g: cross-unit re-own and the Secure guard

FULL rigor, Opus, `security` tag (two adversarial passes). Files:
- `RecordCreationService.cs` (`RepairOwnerAsync`)
- the Repair report and pass result
- tests

UAC-r2 reviews the diff.

ACs:
1. A person-owned event filed under a matter in another ordinary unit is re-owned to that unit's team, with `ownerMovedBusinessUnit` counted and both units logged.
2. A person-owned root re-owned across units has its `modifiedon` changed (so the 194 pass reaches it), and Repair makes no other call.
3. Negative (must-fire): Repair never writes when the answer is the Secure team, another team in the Secure Record unit, or a restricted-content owner team. Each case results in `owner_left_to_secure_pass`, counted. Covered for a document, an event and a to-do under a secure matter.
4. Negative (must-fire): Repair never writes when the current owner is in the Secure Record unit or is a restricted-content owner team.
4a. Negative (must-fire): a faulted isolation read, or an unreadable owning unit, counts as isolated: skipped and counted, never written.
4b. The guard uses the shared isolation read (`RefileIsolationRead.BeforeWriteAsync` and `SecureChildReconciler.IsSecureTeamOwnedAsync`) and the configured `SecureRecord:BusinessUnitName`, with no hard-coded team id. It keeps the row's F1-b mapping block. Must-not-fire twin: an ordinary cross-unit move is written.
4c. A 412 on the owner write leads to one re-read, with no retry loop.
5. Negative: a secure root is never re-owned.
6. Negative: a pre-go-live row is never written.
7. `owner_moves_business_unit` no longer appears as a skip.
8. A must-fire test for each rule; for AC3, the test fails with the guard removed.

### Task 053 (exists)

The live check on dev gains these cases:
- a cross-unit family;
- a contact with no children;
- an analysis with an activity (refused);
- a child under a secure matter (left to the secure pass).

## 5. Questions

**Owner, to decide in this session:**
- **Q1.** Option B: Repair per row, plus the 194, secure and re-file passes, rather than a new family walk. *Recommended: yes.*
- **Q2.** Should notes (`annotation`) cascade with an event and an analysis? *Recommended: no* (UAC-r2 agrees). The cascade would widen who can read a note. A row with notes keeps its owner and is counted. Contact notes are not accepted (UAC-r2, U4). **Decided: yes** (owner, 2026-11-18; see below).
- **Q3.** Pre-go-live default-team children of a repaired root follow it through the 194 pass, as after any MDA Assign. *Recommended: yes.*
- **Q4.** Pre-go-live user-owned children of a repaired root stay where they are, and no report lists them (K3). *Recommended: yes; this is "no backfill".*
- **Q5.** Activities and child contacts never move through an Assign. A row that has them keeps its owner and is reported. *Recommended: yes.*
- **Q6.** Order of work: 052f, then 052g, then the pass can be considered for go-live on dev (§6). *Recommended: yes.*
- **Q7.** The system path uses the reconciler's app-only walk, not the W7 reader, which reads as the caller. This is a deviation from FR-25's wording. *Recommended: accept, and amend FR-25's sentence.*
- **Q8.** The cross-unit move of a non-root row hands nothing to the 194 engine (§3.3 step 4). The one residual, files of documents filed directly under that row, is UAC-r2's issue 2903 (U1). *Recommended: yes.*

**Owner, decided 2026-11-18 in this session.** Q1 and Q3–Q8: yes, as recommended. **Q2: yes** — event and analysis notes move with the re-owned record. The owner first left Q2 to the recommendation (recorded as no), then answered yes in UAC-r2's session and confirmed yes here. The owner states that Spaarke does not use the out-of-the-box Notes (`annotation`) or activities, so both cases are expected to be rare; a row skipped under Q5 or U4 is still counted and reported. Contact notes stay out (U4, unchanged). The isolation guard stays on the parent (§3.3.1), so a note never moves to an isolated owner. Dev held one note in total on 2026-11-18 (read-only count).

**UAC-r2, answered 2026-11-18.** Option B is fine: no new family walk, no in-process call.

| Question | Answer |
|---|---|
| U1. Files of documents under a moved non-root parent | Theirs, issue 2903. Not ours, and it does not block |
| U2. The race in §3.2 | The post-write re-read with a count is enough; the snapshot is not extended |
| U3. Two writers of a work assignment's or project's owner | Keep both; a 412 means re-read, with no retry loop |
| U4. The §3.1 entries | Accepted as listed, except `contact->annotation`: not accepted (reconfirmed 2026-11-18). Event and analysis notes: accepted after the owner's Q2 yes, with the guard on the parent and the must-fire / must-not-fire pair; UAC-r2 reviews the note-move path with 052g's guard diff |
| The §3.3.1 guard | Required, in their shape; they review 052g's diff of it |

## 6. Before the pass is enabled on any environment (consolidated)

1. 052f and 052g are on master and deployed.
2. The 052d audit read has been run once, read-only, end to end on that environment.
3. `ExternalAccess:CustomerBusinessUnitId` is set and its default team confirmed (052b K7).
4. That environment's live Assign-cascade metadata has been re-read and compared with §1.4. The comparison includes onward cascades from each accepted child (for example a document location's child locations). Any new cascading relationship needs a decision.
5. The BFF app user has organization-level Read on every table the per-row check reads (§3.2).
6. `organization.sharetopreviousowneronassign` has been read on that environment (`false` on dev).
7. The values of UAC-r2's `OrdinaryMovesWritesEnabled` and the recent-changes emergency stop on that environment are known. While either stops writes, the families of repaired roots wait, and the run report says so.
8. UAC-r2's answers to U1–U4 (recorded in §5, 2026-11-18), and their review of the 052g guard diff.
9. The owner's go for `RecordRepair:Pass:GoLive`, given in this session.

## 7. Review record

**Opus adversarial pass on revision 1 (2026-11-18)**, read-only against code and docs. Every finding is applied in this revision.

**F1 findings:**
- **F1-1:** "Repair leaves isolated rows to the secure pass" needs its own guard once the cross-unit skip goes. Added in §3.3.1 (052g AC3–AC4b), in the shape UAC-r2 set.
- **F1-2:** the per-row check would have read `sharepointdocument`, whose read is refused on dev, blocking every contact. Fixed by classing it as moves-nothing and never reading it per row (§3.1).

**F2 findings:**

| Finding | Fix |
|---|---|
| F2-1: depth-limited reads | §3.2 fail-closed, §6.5, 052f AC6 |
| F2-2: hand-off ignored the emergency stop | No hand-off (§3.3.4) |
| F2-3: hand-off dropped the document carry | No hand-off; residual is U1 |
| F2-4: hand-off placement and root undefined | No hand-off |
| F2-5: same-unit user-owned children silent | §3.5, Q4, K3 |
| F2-6: hand-off wrote pre-go-live rows | No hand-off; §3.5 |
| F2-7: contact notes' access consequence | `contact->annotation` not accepted until U4 |

**K findings:**

| Finding | Where addressed |
|---|---|
| K1: race of seconds | §3.2, post-write re-read |
| K2: inventory not transitive | §6.4 |
| K3: uncovered members not named | §3.6 |
| K4: hand-off cost | Removed with the hand-off |
| K5: W7 deviation | Q7 |
| K6: 052f AC depends on Q2 | 052f AC3 |

**Sonnet scoped re-check of revision 2 (2026-11-18).** Results by item:

| Item | Result | Fix |
|---|---|---|
| §3.3 step 4 (look-through) | Holds for the 11 non-root ownership parents. The roots outside look-through are covered by step 3; a child created while its parent was unfiled is a K | Step 4 text and K3 |
| §3.3 step 1 guard | Sufficient, with three conditions: fail closed on an unreadable unit, keep the mapping block, and state the case of a person-owned row already in the Secure unit (F2) | Step 1 and 052g AC4a and AC4b |
| §3.6 | Missing the invoice-family refusal (F2), owner refusals and faults, and tables outside the pass (K) | §3.6 rows added |
| §1.3 | "Only" overstated (K) | Reworded |
