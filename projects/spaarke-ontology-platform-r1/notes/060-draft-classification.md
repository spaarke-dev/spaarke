# Task 060 — Draft `sprk_event` classification (spaarkedev1, 2026-10-08, D-74)

**FINAL OUTCOME (2026-10-08, after owner decision): 1 row opened, 48 left Draft. Draft count 49 -> 48.**

Sections 1-6 below are the original read-only classification, written when 4 of 49 rows could not be classified and the task was escalated (nothing was written at that point). The owner then decided the four (section 7).

## 1. Count (fresh, 2026-10-08)

`SELECT COUNT(sprk_eventid) FROM sprk_event WHERE statuscode = 1` -> **49** Draft (statuscode 1). Earlier counts (49/48/49) are not relied on.

Draft by event type: no type 22, Task 20, Action 5, Milestone 1, Deadline 1. Whole table by status: Task Open 21, Task Completed 6, Task Draft 20, plus other types.
Only the **20 Draft rows typed Task** can ever appear in the Briefing's Upcoming/Overdue task channels; the other 29 are untyped or non-Task, so flipping them to Open would not surface them in the Briefing at all.

## 2. Evidence that separates platform rows from person rows

| Evidence | Finding |
|---|---|
| Creator of every `sprk_event` | `Ralph Schroeder` 73 rows, `# mi-bff-api-dev` (the BFF app identity) 10 rows. `TaskActionCore` creates app-only (its own comment on `TaskActionInput`), so a platform row carries the BFF app user as Created By. |
| The 10 BFF-created rows | All 10 are "Review assessed communication: ..." (RI/communication-intelligence), created 2026-09-29 .. 2026-10-08, **all already Open (659490001)**, 8 of 10 with `sprk_regardingcommunication`. **None is Draft.** |
| Creator of the 49 Draft rows | **All 49 = Ralph Schroeder** (a person, a user session). Zero are the BFF app user. |
| Platform fingerprint columns on the 49 | `sprk_source`, `sprk_regardingcommunication`, `sprk_emailsubject`, `sprk_createdbyperson`, `createdonbehalfby`: **null on all 49**. |
| Code history | `TaskActionCore` has written `sprk_event` (type Task) only since **2026-08-06** (9b572260b); before that it created the OOB `task` activity, so no Draft row created before 2026-08-06 can come from it. Status Open was set from 2026-09-29; PR #1032 merged 2026-10-02T15:38:36Z. |
| Audit history | Not used: no audit-history column on the table is exposed through the MCP read; `modifiedon`/`createdon` only. |

Conclusion: **0 rows are provably platform-created and wrongly Draft.** The platform defect behind ISS-003 (TaskActionCore not setting Open) produced no row that survives in Draft today: the BFF-created rows are all Open.

## 3. Classification

Categories: **P** = person-created in a user session (forms, wizards, test data), not platform; leave Draft. **U** = unresolved, escalated. **W** = wrongly Draft (platform): **none**.

Note on P: this is "not a platform row, so out of scope for a platform-bug fix", not proof that each author meant Draft. Draft is `sprk_event`'s form default, and these rows show test-data naming ("Task 5 7/9/2026", "Test Event #20", "Test new event"). Leaving them Draft is the conservative choice.

Totals: **W 0 · P 45 · U 4 = 49.**

### P (45) — person-created, leave untouched

| Group | Created | Rows (sprk_eventid) | Basis |
|---|---|---|---|
| Nameless "Task"/"Case filed" test rows (14) | 2026-02-03 .. 2026-03-03 | a67d88d8-3e01-f111-8407-7ced8d1dc988; a6d00177-4101-f111-8407-7ced8d1dc988; 094c75e0-4b01-f111-8407-7ced8d1dc988; f60c9c91-4e01-f111-8407-7ced8d1dc988; f064dbe5-4e01-f111-8407-7ced8d1dc988; ee4fa424-5001-f111-8407-7ced8d1dc988; 67ef2f33-5201-f111-8407-7ced8d1dc988; 0c6e31b3-6801-f111-8407-7ced8d1dc988; 4aca40a1-0305-f111-8407-7ced8d1dc988 ("Test Event #20"); 626c482d-990d-f111-8342-7ced8d1dc988; d9caa2d1-9b0d-f111-8342-7ced8d1dc988; 1a8223d8-9b0d-f111-8342-7ced8d1dc988; 8478ea81-0311-f111-8342-7c1e520aa4df; f5a992ed-6917-f111-8343-7c1e520aa4df | Created by Ralph; months before `TaskActionCore` wrote `sprk_event` (2026-08-06); no source / communication fields; generic names. |
| "Assign Work" rows (5) | 2026-03-12 | c28ed03e-1a1e-f111-88b3-7c1e520aa4df; eff7f026-371e-f111-88b3-7ced8d1dc988; 2fec2df5-551e-f111-88b3-7ced8d1dc988; 8640606e-741e-f111-88b3-7ced8d1dc988; a30254d0-7f1e-f111-88b3-7ced8d1dc988 | Work-assignment test events typed by hand (descriptions "New work assignement"); pre-2026-08-06. |
| 2026-04-02 test session (6) | 2026-04-02 | 3dd69d7e-992e-f111-88b5-7ced8d1dc988; 8fe31b8b-992e-f111-88b5-7ced8d1dc988; a0cb27af-992e-f111-88b5-7ced8d1dc988; 4f3c48bb-992e-f111-88b5-7ced8d1dc988; 8e1344d3-992e-f111-88b5-7ced8d1dc988; b52562e5-992e-f111-88b5-7ced8d1dc988 | Six rows in 3 minutes, names "Event", "Task 2 (Spaarke)", "Event Added from Daily Briefing"; pre-2026-08-06. Note a0cb27af carries `sprk_eventstatus` Completed but statuscode Draft (a mismatched legacy field) — see section 5. |
| June 2026 form tests (4) | 2026-06-04 .. 06-30 | 61a69ac1-2a60-f111-ab0b-70a8a59455f4; 04ca0ad3-2d60-f111-ab0b-7c1e521b425f; a3f27c2a-3c60-f111-ab0b-70a8a59455f4; 0fd0c436-b674-f111-ab0e-7ced8ddc4a05 | Names "New Event for Matter 6/4/2026", "New Event on Matter Create"; pre-2026-08-06. |
| July 2026 form/wizard tests (15) | 2026-07-05 .. 07-12 | 196c6325-9e78-f111-ab0e-7ced8ddc4cc6; 5b717402-fe79-f111-ab0e-7ced8ddc4a05; 937881af-1c7a-f111-ab0e-7ced8ddc4cc6 ("testing resolver"); 6edb8aff-1d7a-f111-ab0e-7ced8ddc4cc6 ("created from subgrid"); 160b284b-2a7b-f111-ab0e-7ced8ddc4cc6; a2bd239a-477b-f111-ab0e-7ced8ddc4cc6; 9ac9b0ca-9b7b-f111-ab0e-7ced8ddc4cc6; 1a4de81d-af7b-f111-ab0e-7ced8ddc4a05; 2c50e230-b07b-f111-ab0e-7ced8ddc4a05; ca4973aa-b07b-f111-ab0e-7ced8ddc4cc6; 2e63abb4-b27b-f111-ab0e-7ced8ddc4cc6; deb00f4b-b47b-f111-ab0e-70a8a590c51c; 0d88ccb3-b47b-f111-ab0e-70a8a590c51c; edb28fa9-bd7b-f111-ab0e-7ced8ddc4a05; 40abfbed-1b7e-f111-ab0e-7ced8ddc4a05 | Self-describing numbered test rows ("Task 5 7/9/2026", "Task 9 create 7/9/2006"); pre-2026-08-06. |
| Post-fix (1) | 2026-10-02T15:40:38 | 171b8d24-99be-f111-aaaf-0022482913fc ("Test new event") | Created 2 minutes AFTER #1032 merged and by Ralph, not the BFF identity: cannot be the pre-#1032 platform defect. A user-session form create. |

### U (4) — cannot be classified confidently; escalated

| sprk_eventid | Name | Created | Why it is not confidently P or W |
|---|---|---|---|
| 8a6b371f-8a96-f111-b8db-0022482fb5a7 | Review and finalize amendments to Canadian Patent Application No. 3,116,549 claims | 2026-08-12 16:12 (edited 16:14) | Inside the 08-06..10-02 window in which `TaskActionCore` wrote `sprk_event` without Open. Long AI-style description, priority High, assigned-to contact set, regards a real matter (b68299c6...), due 2026-08-21. Looks like real work, but created by Ralph and edited 2 minutes later: a user-confirmed AI task or a hand-built one is indistinguishable. Candidate W. Due 2026-08-21 = overdue today if opened. |
| cbaa61fa-6f96-f111-b8dc-7ced8ddc4a05 | Create New Matter Follow-up Task | 2026-08-12 13:05 | Same window; regards matter 6e6869ee...; description is a test-style date stamp. Candidate W or P. Due 2026-08-14. |
| 9c9de352-3b7a-f111-ab0e-70a8a590c51c | Review engagement letter document | 2026-07-07 15:37 | Description "Provenance: source document Engagement Letter.docx; source analysis ...@t1" = AI-proposed task from an analysis. Created before 2026-08-06, so not the sprk_event-writing `TaskActionCore`; the path that wrote it is not identified. Due 2024-06-14 (a stale date from the document), so opening it would put a 2-year-overdue task into the Briefing. Candidate W (if AI-created) but the date argues for review. |
| 007b8ac2-7a80-f111-ab0f-7ced8ddc4a05 | Follow up on Engagement Letter | 2026-07-15 14:26 | Description "Provenance: source document Engagement Letter.docx"; same unidentified AI path, pre-2026-08-06. Due 2026-07-17 (overdue). Candidate W. |

All four are typed Task, so all four would appear in the Briefing's Upcoming/Overdue channels (the Overdue one in every case) if set Open. They need the communication-intelligence / assistant domain owner or the person who created them to say whether each is live work.

## 4. Step 5 (collector query shape, read-only)

`DailyBriefingCollector` (`Services/Ai/Narrators/DailyBriefingCollector.cs` lines 917-918) filters `_sprk_eventtype_ref_value eq <Task 124f5fc9...> AND statuscode eq 659490001 (Open)` plus a `sprk_duedate` window (NextXDays / OnOrBefore). Live with no write:
- Task + Open rows matching the type and status part of that shape now: **21** (before the due-date window; the 10 BFF-created rows are all Open, whether they are typed Task was not separately checked).
- Task + Draft rows invisible to it: **20** (the 16 P Task rows + the 4 U rows).
Corrected-row visibility cannot be demonstrated because no row was corrected. Reproduction: `SELECT sprk_eventtype_ref, statuscode, COUNT(sprk_eventid) ... GROUP BY sprk_eventtype_ref, statuscode`.

## 5. Observations to file (not fixed here)

- `sprk_eventstatus` (the legacy choice) disagrees with `statuscode` on many rows (null on most, Open/Completed on Draft statuscode rows, e.g. a0cb27af shows Completed). The Briefing reads `statuscode`, so this is a legacy-field divergence, outside this task.
- `sprk_event` has no `sprk_source` set on any Draft row, so there is no durable "platform vs person" marker for past rows; future platform writers set the BFF identity as Created By, which is what this classification relied on.

## 6. Decision needed from the owner

For each of the 4 U rows: set Open (live work), leave Draft, or delete as test data. Until then the 45 P rows and the 4 U rows stay Draft and issue #1050 stays open.
Recommended: leave the 45 P rows alone; set Open only the two rows the domain owner confirms as real (likely 8a6b371f and 007b8ac2); treat 9c9de352 (due 2024) and cbaa61fa as leave-Draft/test unless told otherwise.

## 7. Owner decision and outcome (dev data change, spaarkedev1 only)

| sprk_eventid | Name | Decision | Before | After |
|---|---|---|---|---|
| 8a6b371f-8a96-f111-b8db-0022482fb5a7 | Review and finalize amendments to Canadian Patent Application No. 3,116,549 claims | **Open** (real work) | statecode 0 Active, statuscode 1 Draft | statecode 0 Active, statuscode 659490001 Open (read back; modifiedon 2026-10-08T11:20:30) |
| cbaa61fa-6f96-f111-b8dc-7ced8ddc4a05 | Create New Matter Follow-up Task | leave Draft | Draft | Draft (untouched) |
| 9c9de352-3b7a-f111-ab0e-70a8a590c51c | Review engagement letter document | leave Draft | Draft | Draft (untouched; due 2024-06-14 stale) |
| 007b8ac2-7a80-f111-ab0f-7ced8ddc4a05 | Follow up on Engagement Letter | leave Draft | Draft | Draft (untouched) |

Only the one row was written; the statecode/statuscode pair used is Active(0)/Open(659490001), the pair TaskActionCore uses.

**Counts:** before 49 Draft, after **48** Draft (re-counted, `statuscode = 1`).

**Why the 48 remain Draft:** 45 are person-created rows (test-style data created by Ralph Schroeder in user sessions; not platform rows; section 3 P); 3 are the unresolved rows the owner chose to leave Draft (cbaa61fa, 9c9de352, 007b8ac2). No platform-created row is among them.

**Briefing collector check (read-only):** the collector's filter shape (event type Task `124f5fc9-98ff-f011-8406-7c1e525abd8b`, statuscode Open 659490001, `sprk_duedate` before today 2026-10-08) returns `8a6b371f-8a96-f111-b8db-0022482fb5a7` (due 2026-08-21). It appears in the Overdue channel.

**Finding: 0 rows were platform-created.** The ISS-003 premise (the platform left tasks stranded in Draft) did not hold in dev. The #1032 code bug (TaskActionCore not setting Open) was real, but it left no stranded data here: all 10 BFF-created events are Open and none of the 49 Draft rows was created by the BFF identity. The one row opened was opened because the owner judged it real work, not because it was a platform casualty.

**Follow-up input for task 066:** `sprk_eventstatus` disagrees with `statuscode` on many rows (see section 5).
