# 079: coordination with uac-r2 for the two secure-child entries (D-33, D-36)

> **Date**: 2026-10-07 · **Task**: 079 · **Status**: refusals recorded; review requested; **uac-r2 answer pending**
> **Review request**: https://github.com/spaarke-dev/spaarke/issues/1355 (label `unified-access-control-r2`)

## 1. What uac-r2 files were read (origin/master @ `dbc58d139`, fetched 2026-10-07)

| File | Last commit on origin/master |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs` | `d254d7166` (#1312, 2026-10-06) |
| `config/secure-record-owner-role.json` | `d254d7166` (#1312) |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` | `d254d7166` |
| `src/server/api/Sprk.Bff.Api/Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `d254d7166` |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | `428c5bfae` (2026-10-01) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalCallerContext.cs` | `d254d7166` |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `dae5869d2` (2026-10-07, task 171) |
| `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` | `c7f016c53` |
| `tests/unit/domain/Access/SecureChildLineageTests.cs` | `d254d7166` |
| `.claude/adr/ADR-034-user-record-membership.md` / `docs/adr/ADR-034-...` | `d254d7166` |
| `.claude/adr/ADR-003-authorization-seams.md` / `docs/adr/ADR-003-lean-authorization-seams.md` | `8c5c517a1` / `367a6209c` |

The two target files and the resolver are unchanged since the scoping note (2026-10-07) was written. Neither table is in the lineage map or the role config today. **The plan is not invalidated.**

Overlap check: the uac-r2 worktree exists at `C:\code_files\spaarke-wt-unified-access-control-r2` (branch `work/unified-access-control-r2`, head `bbb6cfde0`). Its open PRs are #1353 (task 171 live-regression fix) and #1342 (task 114); neither touches the lineage map or the owner-role config on the evidence of their titles. uac-r2's `current-task.md` says batch 5 is in progress (116 of 168 tasks), with 154, 113, 114, 105, 101, 064 and others still to run. It lists no task touching `SecureChildLineage.cs`.

## 2. Preconditions confirmed live (spaarkedev1, read-only, before the probes)

- Role `Secure Record Owner` `e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`, 26 privileges. It holds **neither** `prvReadsprk_Signal` (`226178d2-9528-4cde-b903-a8a84788b09e`) **nor** `prvReadsprk_DecisionRecord` (`3e9b0585-c7a8-4cfa-b194-3bb12623d910`). For contrast it holds `prvReadsprk_SpendSignal` (depth mask 1, Basic).
- Task 008's Secure Record Owner edit had **not** been applied. The refusals were therefore recorded honestly, and the escalation trigger did not fire.

## 3. Recorded live refusals

Method (config `howToExtend`, create-then-assign variant per this task's POML): create a probe row as the dev operator, `Assign` it to team `Secure Record Owners` (`6eabc7f9-13be-f111-a05b-0022482913fc`), capture the refusal, delete the probe. 3 polls per table. Dev operator caller: `1d02f31c-1872-f011-b4cb-7c1e52671ad0`.

**`sprk_signal`**, HTTP 403, `0x80040299`, at 2026-10-07T14:58:39Z, 14:58:41Z and 14:58:44Z (3 of 3):

> Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=26, MetadataCachePrivilegesCount=7334, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_Signal privilege (Id=226178d2-9528-4cde-b903-a8a84788b09e) on OTC=11003 for entity 'sprk_signal' (LocalizedName='Signal'). context.Caller=1d02f31c-1872-f011-b4cb-7c1e52671ad0. Consider adding missed privilege to one of the principal (user/team) roles.

**`sprk_decisionrecord`**, HTTP 403, `0x80040299`, at 2026-10-07T14:58:48Z, 14:58:51Z and 14:58:54Z (3 of 3):

> Principal team (Id=6eabc7f9-13be-f111-a05b-0022482913fc, type=9, teamType=0, privilegeCount=26, MetadataCachePrivilegesCount=7334, businessUnitId=d9ec0b6f-80a0-f111-aaac-000d3a99d1d7), is missing prvReadsprk_DecisionRecord privilege (Id=3e9b0585-c7a8-4cfa-b194-3bb12623d910) on OTC=11002 for entity 'sprk_decisionrecord' (LocalizedName='Decision Record'). context.Caller=1d02f31c-1872-f011-b4cb-7c1e52671ad0. Consider adding missed privilege to one of the principal (user/team) roles.

`privilegeCount=26` equals the role's current privilege count, so the reading was current.

Probe rows (all deleted, confirmed absent by id and by the `zz-079` name prefix on both tables):

| Table | Probe row id |
|---|---|
| `sprk_signal` | `1a411d83-5fc2-f111-a05c-3833c5e9614d` |
| `sprk_decisionrecord` | `f1536c84-5fc2-f111-a05c-0022482913fc` |

Note: the Web API action is `Assign`, not `AssignRecord` (the first attempt returned 404 on the segment and changed nothing).

**Sequencing with task 008**: both refusals are recorded here, before any Secure Record Owner Read grant. Task 008 may now apply that edit.

## 4. Live re-check of the lineage lookups (2026-10-07)

- `sprk_signal` has: `sprk_matter`, `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingworkassignment`, `sprk_regardingcommunication`, `sprk_regardingtodo`, `sprk_regardingevent`, `sprk_regardinginvoice`, `sprk_regardingdocument`, `sprk_decisionrecord`, plus the non-filing `sprk_regardingservicerequest`, `sprk_policy`, `sprk_policyversion`, `sprk_resolvedby`. **New since the scoping note**: `sprk_corerecordtype` (to the catalog `sprk_recordtype_ref`), added by task 007. It is not a filing parent and must stay out of the lineage entry.
- `sprk_decisionrecord` has only `sprk_matter` as a filing lookup today (plus `sprk_action`, `sprk_policyversion`, `sprk_confirmedby`, which are not filing parents). `sprk_project` and `sprk_workassignment` (D-34, D-36) are **not live yet**: they come from task 007, which is in flight. The Decision Record lineage entry in task 039 therefore depends on 007 and must be re-checked against live metadata at that time.

## 5. How uac-r2 was contacted

- **ListAgents is not available in this run**, so I could not tell whether a uac-r2 Claude session is live, and `SendMessage` needs a name from that listing. No message was sent.
- **Fallback taken**: one GitHub issue, https://github.com/spaarke-dev/spaarke/issues/1355, labelled `unified-access-control-r2` (the label their deferred-work issues carry, e.g. #1313).
- The package contains: the two config entries and the two lineage entries (Decision Record shape stated as final-intended, pending 007); both refusals verbatim; the three D-33 role edits and the deploy-order rule; the writer change summary; the "not in R1" items (`OwnershipParentEntities` unchanged, `RecordOwnerAssignmentCensusTests.ChildTables` not extended, `sprk_createdbyperson` stamping); the load-measurement plan; four explicit questions.
- Nothing was pushed to uac-r2's branch and none of their files were edited.

## 6. uac-r2's answer

**Pending.** No reply yet. When it arrives, record it here (accepted, or the changes requested) and reference this note from task 039. Escalation applies: if uac-r2 rejects an entry or asks for a uac-r2 code change, stop and take it to the owner (scoping section 6 fallback is a skip keyed on the Secure flag, not applied without the owner).

## 7. Is task 039 unblocked?

**Partly.** The refusal evidence (039 constraint "each citing its recorded live refusal") is complete, and task 008 can now apply the Secure Record Owner Read. 039's coordination gate (uac-r2's review, PR linked) is **not** yet satisfied, and the Decision Record entry also waits on task 007. No code, role or config change was made by this task.


## 6. uac-r2 review received (2026-10-07)

https://github.com/spaarke-dev/spaarke/issues/1355#issuecomment-6047438504 — approved with conditions: (A) list only lookup columns that already exist (their one-pass reconciliation fails for every secure child table on a missing column); (B) ONE PR for SecureChildLineage.cs + secure-record-owner-role.json. Deploy order per environment: schema (both tables + every lookup) -> role edits (+ Assign and Share for the BFF identity where not System Administrator) -> BFF. Their role script never removes privileges (our early Reads are safe). Basic User Read is needed (unsecure hands children to the BU default team). Census extension deferred to #1379. Both jobs run in dev (21:40Z). Required change for us: no nightly lastevaluated stamp on every open Signal (100k changed-rows-per-pass limit) -> D-61. Their question (can users create Signals/DRs directly?) -> answered by D-62. Their own defect filed: #1378. 079 complete; 039 unblocked.
