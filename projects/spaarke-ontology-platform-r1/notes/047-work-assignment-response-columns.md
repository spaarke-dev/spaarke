# Task 047 — response columns on `sprk_workassignment` (D-54, D-58)

> **Date**: 2026-10-07 · **Env**: `spaarkedev1` · **Status**: ✅ **DONE (2026-10-07).** First pass stopped at the
> escalation trigger (sections 1-6 below are that record, unchanged). The owner then answered as **area owner (D-58,
> spec §9)**: approve §4 as proposed. Columns created and read back: §7.

## 7. Executed after D-58 (2026-10-07)

**Agreement**: the project owner, acting as work-assignment area owner, approved §4 as written on 2026-10-07: the
column set, the three outcome values, no note column ("notes go in the Decision Record") and home solution
`SpaarkeCore`. Recorded as **D-58** in `spec.md` §9, relayed by the coordinator session.
**uac-r2**: the request in §6 was posted on issue #1355,
<https://github.com/spaarke-dev/spaarke/issues/1355#issuecomment-6049366401>. Per D-58, plain columns do **not** wait on
the reply. If uac-r2 names a needed change, that is follow-up work.

**Method**: Web API, `POST EntityDefinitions(LogicalName='sprk_workassignment')/Attributes` with explicit PascalCase
SchemaName and header `MSCRM.SolutionUniqueName: SpaarkeCore`. `DateTimeBehavior` was set on the date column. Then
`PublishXml` ran for `<entity>sprk_workassignment</entity>` **only**. No MCP `create_table`, no `PublishAllXml`.
Responses: create ×2 HTTP 204, publish HTTP 204.

**Read back (live)**:

| Column | SchemaName | Type | Detail | MetadataId | Before | After |
|---|---|---|---|---|---|---|
| `sprk_respondedon` | `sprk_RespondedOn` | DateTime | Format **DateOnly**, DateTimeBehavior **DateOnly**, RequiredLevel None | `61464111-acc2-f111-a05c-3833c5e9614d` | absent | present |
| `sprk_responseoutcome` | `sprk_ResponseOutcome` | Picklist, **local** option set `sprk_workassignment_sprk_responseoutcome` | `100000000` Received outside Spaarke · `100000001` Delivered on the matter · `100000002` No longer needed; RequiredLevel None, no default | `67244417-acc2-f111-a05c-3833c5e9614d` | absent | present |

**Solution membership**: neither attribute has a componenttype-2 row of its own. As task 007 found, a table that a
solution holds with rootcomponentbehavior 0 carries all of its attributes through the entity. So both columns belong to
`SpaarkeCore` (rcb 0), the header's target, and to the table's other rcb-0 holders. After D-60 that list no longer
includes `OntologyPlatformSolution`.

**Negative**: no other column or table was changed by this task, and no tool without a publisher parameter was used.
Later tests that assert these names pair with this read-back (ADR-038).

## 1. Who is the work-assignment area owner?

Nobody is named. Evidence:

- `projects/INDEX.md` has **no active project** whose scope is `sprk_workassignment` (the only row naming it,
  `record-header-and-notepad-r2`, binds a header PCF to its form; it does not own the table).
- `projects/record-header-and-notepad-r2/notes/issues/ISSUE-work-assignment-schema-drift.md` (#1035): **"Owner:
  unassigned — for review."**
- The wizard's history (`CreateWorkAssignmentWizard/`) is a sequence of unrelated projects (workspace UI, smart-todo-r3,
  multi-container, field-mapping, uac-r2 076/142, assistant-r1, this project's 081/097) — no steward.
- The only party that has made **binding decisions about the table's write path** is **uac-r2 under owner rounds**:
  it is a secure root (task 144: secure work assignments owned by the named "Secure Record Owners" team), one filed
  under a secure matter/project is made secure (task 158), and its BFF create route was **retired** (task 166, S-76).

**Proposal for the area-owner role**: the project owner (human) decides, with uac-r2 consulted as the table's
access-model owner. That is a guess the POML does not let me make, so it is part of the escalation (§6).

## 2. uac-r2 re-read — `origin/master` @ `0ea74d1c3` (fetched 2026-10-07)

| File | Last commit on master |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs` | `d254d7166` 2026-10-06 (#1312) |
| `config/secure-record-owner-role.json` | `d254d7166` 2026-10-06 |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` 2026-10-06 |
| `src/server/api/Sprk.Bff.Api/Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `d254d7166` 2026-10-06 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | `428c5bfae` 2026-10-01 |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `3556061e1` 2026-10-07 |
| `.claude/adr/ADR-034-user-record-membership.md` / `docs/adr/ADR-034-…` | `d254d7166` 2026-10-06 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalCallerContext.cs` (ADR-003) | `d254d7166` 2026-10-06 |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` | `d254d7166` 2026-10-06 |

What they say about this task:
- `sprk_workassignment` is a **root** (`SecureRecordRoot.WorkAssignment`, `CoreAncestorResolver.CoreRecordEntities`), and
  a **target** in `SecureChildLineage.Children` (children point at it through `sprk_regardingworkassignment`); it is not
  itself a child. `secure-record-owner-role.json` grants `prvReadsprk_WorkAssignment` (table-level).
- Lineage, the owner-role config and the route census are keyed on **lookup columns, tables and routes**. Two plain
  data columns (a Date Only and a local choice) are none of those, so **no lineage, census or role change is expected**.
  Table-level privileges cover new columns; no field security profile is proposed (CLAUDE.md §5 forbids adding one).
  This is my reading, **not uac-r2's answer** — the POML requires theirs.
- Nothing on master or in uac-r2's worktree (`8bf068828`, batch 5) plans a schema change on `sprk_workassignment`.
- Open PRs (2026-10-07): none touch `sprk_workassignment` metadata or the wizard (`#1372` touches a child-record test
  only). `/conflict-check` hot-path watchlist: this task touches Dataverse metadata only, no watched file.

## 3. Live describe — BEFORE (step 1, read-only, 2026-10-07)

`sprk_workassignment`: MetadataId `7bf841a8-f882-f011-b4cc-7c1e52004e03`, SchemaName `sprk_WorkAssignment`, OTC 10731,
**UserOwned**, customizable.

- `statuscode`: **Active (1, state 0)**, **Inactive (2, state 1)** — nothing else.
- Only DateTime: `sprk_responseduedate`. **No** column matching `respon|outcome|answer` other than
  `sprk_responseduedate`. `sprk_respondedon` and `sprk_responseoutcome` do **not** exist.
- 39 `sprk_` columns (non-derived): accesspermission, ai_search_index, assigned* (×10 lookups),
  containerid, createdbyperson, description, highpriority, issecure, mattertype, monitor, name (required), practicearea,
  priority, recordsummary, regarding{communication,event,invoice,matter,project} + the five ADR-024 resolver fields,
  responseduedate, searchindexname, searchprofile, securitybu, workassignmentnumber.

**Solutions holding the table** (componenttype 1): `SpaarkeCore` (rcb 0), `SpaarkeMaster` (0), `SpaarkeMasterTest2`
(0), `SPRKDOCINTELLIGENCE` (0), **`OntologyPlatformSolution` (0, added 2026-10-02 21:23)**, `Default` (0), and the
ribbon solutions `WorkAssignmentRibbons` (2), `ThemeMenuRibbons` (2), `SpaarkeAccessRibbons` (1), `Cr2b7d5` (1).

## 4. The proposal (to be agreed — nothing created)

| Column | SchemaName | Type | Detail | Source |
|---|---|---|---|---|
| `sprk_respondedon` | `sprk_RespondedOn` | DateTime | Format **DateOnly**, `DateTimeBehavior` **DateOnly**, RequiredLevel None, display "Responded On" | D-54 (owner-approved name + type) |
| `sprk_responseoutcome` | `sprk_ResponseOutcome` | Picklist (**local**) | `100000000` Received outside Spaarke · `100000001` Delivered on the matter · `100000002` No longer needed; RequiredLevel None, no default, display "Response Outcome" | values = v4 *Record the response* radio @ `ae1cc9f` (`mockData.ts` `record-response.params.outcome`) |

- **No note column.** v4's optional *Note* belongs in the Decision Record (`sprk_steps`), which is the record of what
  was decided (D-17); a second copy on the assignment would drift. (Open to the area owner adding
  `sprk_responsenote` Memo 2000 if they want it on the form.)
- **Not proposed, flagged for the area owner**: whether *Record the response* also deactivates the assignment
  (`statecode` Inactive) as v4 does ("Confirm — close assignment"). That is task 044's behaviour, not schema, but the
  WA rule in task 061 ("active, past `sprk_responseduedate`, no response recorded") reads correctly either way.
- **Solution**: `MSCRM.SolutionUniqueName: SpaarkeCore` (the table's home solution) — **proposed**, to be agreed. Note
  that every solution holding the table with rcb 0 (including `OntologyPlatformSolution`) carries new columns anyway,
  so the header only decides the explicit component row.
- **Recipe** (task 007): `POST EntityDefinitions(LogicalName='sprk_workassignment')/Attributes`, explicit PascalCase
  SchemaName, the solution header, `DateTimeBehavior` on the date column; then `PublishXml` for **sprk_workassignment
  only**; then read both back. Never MCP `create_table`, never `PublishAllXml`.

## 5. Found in passing — `OntologyPlatformSolution` holds 11 other-domain tables WHOLE

Not this task's scope; surfaced per task-execute Step 9.5 ("every defect found is fixed or surfaced").
`OntologyPlatformSolution` contains, as componenttype 1 with **rootcomponentbehavior 0 (include all subcomponents)**,
added 2026-10-02 21:17–21:23 (when the five tables' lookups were created): `sprk_budget`, `sprk_communication`,
`sprk_document`, `sprk_event`, `sprk_invoice`, `sprk_matter`, `sprk_project`, `sprk_servicerequest`, `sprk_todo`,
`sprk_workassignment` (and `sprk_recordtype_ref`, added 2026-10-07; `systemuser` is rcb 1). **Failure scenario**:
exporting `OntologyPlatformSolution` for another environment ships those tables' every column, form, view and ribbon
as of dev, overwriting the target's customizations (unmanaged) or adding a managed layer over core tables (managed).
The fix is to re-add them as rcb 1/2 (metadata only) or remove them — an ALM decision for the owner. Needs a
`/project-defer-issue-tracking` entry + GitHub issue (main session; see the C2 report).

## 6. Escalation (POML triggers 1 and 2, and the orchestrator's instruction)

🔔 **Human Input Required — task 047**
- **Situation**: the POML requires the work-assignment area owner's agreement (column set, outcome values, solution)
  and uac-r2's "no lineage change" answer before any metadata change. No area owner is named anywhere; uac-r2 cannot be
  reached synchronously from this session.
- **Options**: (A) the project owner acts as the area owner and approves §4 as written (or edited); uac-r2 is asked on a
  GitHub issue (or on #1355) for the one-line "plain columns, no lineage/census/role change" confirmation. (B) Name
  another area owner and route the proposal to them. (C) Defer 047 (blocks 044, 061).
- **Recommendation**: (A). The change is two additive, optional, non-lookup columns; the proposal is fully specified.
- **Ready-to-post request to uac-r2** (durable channel per CLAUDE.md §6 gotcha 2026-10-07):
  > ontology-r1 task 047 (D-54) proposes two plain data columns on `sprk_workassignment` (a secure root):
  > `sprk_respondedon` (Date Only) and `sprk_responseoutcome` (local choice: Received outside Spaarke / Delivered on the
  > matter / No longer needed), RequiredLevel None, no field security. They are not lookups, so we expect no
  > `SecureChildLineage`, owner-role config, route-census or role change. Please confirm, or name what is needed.
