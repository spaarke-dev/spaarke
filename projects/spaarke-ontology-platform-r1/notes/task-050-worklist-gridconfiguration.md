# Task 050 - the worklist sprk_gridconfiguration row: record

Lane `task/ontology-050-worklist-gridconfig` (worktree `C:\wts-050`), off `origin/docs/ontology-platform-design` @ `d3b4fdd5c`.
Rigor STANDARD (POML). **Authored as a repo artifact; NOT applied.** Creating the row on spaarkedev1 is a dev data change and
needs the owner's approval, which has not been given. `TASK-INDEX.md`, `current-task.md` and the POML `<status>` are left to the
main session (the POML carries a completion block).

## What was built

| File | What |
|---|---|
| `config/worklist.gridconfiguration.json` | The definition: `kind: worklist`, one FetchXML per lane (`lanes.decide`, `lanes.do`) and three secondary sets (`secondary.closedToday`, `.suppressed`, `.closedEarlier`). 8,402 of the 20,000 characters `sprk_configjson` holds. |
| `scripts/Apply-WorklistGridConfiguration.ps1` | Idempotent create/update with `-WhatIf`, read-only `-Verify` (lint + run all five queries + row state), read-back after a write. Fixed row id `00000000-0000-4000-8000-000000005010`. |
| `tests/integration/data-mutation/Signals/WorklistGridConfigurationTests.cs` | Offline guard: structure, the acceptance negatives, and every column and option value checked against the repo solution source (`sprk_Signal`, `sprk_Policy` `Entity.xml`). 10 seeded-defect controls. |
| `tests/integration/seam/Signals/WorklistGridConfigurationLiveTests.cs` | The real-schema pairing (ADR-038): the same queries run against live Dataverse, with live metadata checks. Opt-in (`SIGNALS_LIVE_DATAVERSE_URL`), read-only. |

## Row shape (what task 038 reads)

`sprk_gridconfiguration` row: `sprk_name` = "Ontology Worklist - Signal membership and order", `sprk_entitylogicalname` =
`sprk_signal`, `sprk_configjson` = the definition. Id `00000000-0000-4000-8000-000000005010` (5001-5003 are the reconciliation rows;
the route can find the row by this id).

```
{ _version: "1.0", kind: "worklist", entity: "sprk_signal", description,
  lanes:     { decide: { laneValue: 100000000, fetchXml }, do: { laneValue: 100000001, fetchXml } },
  secondary: { closedToday:   { widenHours: 26, fetchXml },
               suppressed:    { fetchXml },
               closedEarlier: { windowDays: 30, fetchXml } } }
```

It is deliberately NOT a `DataGridConfiguration` (no `source`): `isValidDataGridConfiguration` returns false, so the browser
`<DataGrid>` cannot run it. That is the point (D-15): a browser run would read Signals as the user with no matter-access trim. The
BFF wizard picker (`GridConfigurationService`, by entity) will list the row for `sprk_signal`; picking it in a DataGrid section
degrades to framework defaults, it does not execute the query.

## Decisions (each a choice the POML left open)

1. **Status column.** `sprk_signalstatus` (Open 100000000, Acknowledged 100000001, Resolved 100000002) is the Signal status the
   writer, closure and suppression services all use. `statuscode` on `sprk_signal` is only Active/Inactive (verified live), so D-28
   ("`statuscode` authoritative") is about events, not Signals.
2. **`statecode = 0` on every query.** Not in the POML. An inactive Signal is not worklist material and nothing in the Signal code
   deactivates one; the extra condition costs nothing and cannot hide a live item.
3. **"Not suppressed".** `sprk_suppresseduntil` is null OR `olderthan-x-minutes 1`. Dataverse rejects `olderthan-x-minutes 0`
   ("The condition value should be greater than zero"), so a stamp that lapsed under a minute ago stays hidden for up to a minute.
   Verified on `asyncoperation.postponeuntil`: `olderthan-x-minutes 1` returned 464 rows, exactly the 464 of a literal `le <now>`.
4. **Suppressed set.** Open or Acknowledged Signals with `sprk_suppresseduntil` `next-x-years 100` (the future). Restricted to
   Open/Acknowledged because task 034 also stamps dismissed (Resolved) Signals in the window; those are closed items, not suppressed
   ones, and would otherwise appear twice. Verified on the same `postponeuntil` column: `next-x-years 100` returned 17 rows = the 20
   rows after now minus the 3 year-9999 sentinels, so it selects exactly the future.
5. **Closed today / closed earlier and "today".** D-25 (amended by D-129) judges "today" per item in the assignee's linked user's
   time zone, else UTC. A local midnight is never more than 24 hours old, so `last-x-hours 26` (24 plus a DST hour plus one) is a
   superset of every item's "today"; **task 038 must keep an item in `closedToday` only if it was resolved today in its own zone,
   and put the rest in `closedEarlier`.** `closedEarlier` is `last-x-hours 720` (`windowDays` 30 x 24) with no lower exclusion,
   so it overlaps `closedToday` by design: dropping the overlap in the query would lose items resolved late yesterday in one zone
   and today in another. Verified on `sprk_communication.createdon`: `last-x-hours 26` = 9 and `today` = 9; `last-x-hours 720` = 43.
   The 30-day window is my choice (the POML says "bounded to a window stated in the row"; HANDOFF gives none); it matches the 30-day
   suppression window and is one number in the row.
6. **Grouping and order (D-34, D-36, D-42).** `sprk_corerecordtype`, `sprk_corerecordid` first (a core record's items are contiguous,
   including rows with no core record and project/work-assignment/service-request groups: no filter on the pair), then
   `sprk_firstdetected` ascending (lanes) / `sprk_resolvedon` descending (closed sets) / `sprk_suppresseduntil` ascending, then
   `sprk_signalid` as a stable tiebreak so paging is deterministic. No `sprk_rankscore`, severity or high-priority column in any order.
   The POML step 2 wording "then matter number" is superseded by D-36/D-42: the record number comes from the catalog row and is
   applied by task 038.
7. **Selected columns.** Signal: id, name, lane, status, first detected, `sprk_duedate` (task 007), the generic subject trio
   (`sprk_regardingrecordtype/id/name/number`), the core pair, policy and policy version, `sprk_evidencerefs`, `ownerid` (task 038's
   D-35 owner-only rule needs it); the sets add `sprk_resolvedon`/`sprk_resolutiontype` or `sprk_suppresseduntil`. Policy (outer link,
   alias `policy`): `sprk_shortname`, `sprk_worktype` (the 052 F4 requirement), `sprk_severity` (so 038 can rank without a second
   read). **Not selected:** `sprk_sentence`, `sprk_factsnapshot` (rows never show a sentence or witness value, D-23), per-type
   lookups (`sprk_matter`, `sprk_regardingmatter` ...; D-36: the configuration names no core type). The POML constraint text that
   says "select `sprk_matter`, `sprk_project`" (D-34) is superseded by its own D-36 amendment, which this follows; `sprk_project`
   is not a Signal column in any case.
8. **The policy link is `outer`.** A display link must not change membership: an inner join would silently drop a Signal whose
   policy the caller cannot read.
9. **`sprk_configjson` is limited to 20,000 characters** (live `MaxLength`). The script and the offline test both guard it; the
   definition uses single-quoted XML attributes so the JSON carries no `\"` escapes.

## Verification (read-only, spaarkedev1, 2026-10-13/14)

`pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify`:

| Query | Rows |
|---|---|
| lane:decide | 0 |
| lane:do | 0 |
| secondary:closedToday | 0 |
| secondary:suppressed | 0 |
| secondary:closedEarlier | 0 |
| Row 5010 | ABSENT (not applied) |

**Why all zero: `sprk_signal` holds no rows on spaarkedev1** (aggregate count = 0), and no ontology Signal has been written there
(task 031 evaluates against the task 005 seed but the policy `POL-COMMIT-BUDGET` is Off, `seed-data-state.md`). So the POML's step 4
("the membership query returns the seeded positive matter") and the first three acceptance criteria cannot be demonstrated
on live rows without writing a Signal, which is a dev data change that was not approved. What the run does prove: every column,
operator, option value, link and order is accepted by the deployed schema (a seeded unknown column and an unknown operator each
fail: `0x80041103`, `0x80041120`), and the time operators behave as decision 3-5 state on tables that have rows. Membership against
rows is the live seam's next step once a `zz-050-` Signal set is approved: Open+matter -> in; Acknowledged -> in; Resolved -> out of
both lanes; suppressed -> out of the lanes and in `suppressed`; no-core and project-core rows -> in the Do query.

The offline guard asserts the same conditions on the definition text, so a change to which Signals appear is a one-line edit of the
row plus `Apply-WorklistGridConfiguration.ps1`, with no code change (criterion 4): the apply script is the demonstration.

## For task 038 (reader of the row)

- Read the row by id `00000000-0000-4000-8000-000000005010`. Execute `lanes.*.fetchXml` verbatim (as the caller, `IDataverseUserClient`);
  page with the paging cookie (no `count`/`page` set). Do not edit the FetchXML to filter by user.
- Alias `policy` columns come back as `policy.sprk_shortname`, `policy.sprk_worktype`, `policy.sprk_severity` (AliasedValue).
- `sprk_regardingrecordtype` and `sprk_corerecordtype` are Lookups to `sprk_recordtype_ref` (task 140); `sprk_corerecordid` and
  `sprk_regardingrecordid` are strings; `sprk_duedate` is Date Only.
- Apply per-item "today" to `closedToday`/`closedEarlier` as in decision 5, and the D-42 rank to the lanes.

## Deferred / noticed (not fixed here, D-106/D-127)

- **New-environment seeding.** `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/AppConfigSeed/DataverseWebApiDataGridSeeder.cs`
  seeds the reconciliation grid configurations for a new customer environment; the worklist row (id 5010) is not in it, so a freshly
  provisioned environment will have no worklist row until it is added or the apply script is run. Dev only is in scope for the MVP; filed as
  [#1836](https://github.com/spaarke-dev/spaarke/issues/1836) (`ontology-deferred`), recorded as F-71 in `defer-issues.md`.
- `isValidDataGridConfiguration` + `GridConfigurationService` list the worklist row in the section picker for `sprk_signal` (see "Row shape").
