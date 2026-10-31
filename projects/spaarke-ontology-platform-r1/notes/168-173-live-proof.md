# Tasks 168 and 173: live proof on dev (runbook)

> Written 2026-10-30. Target: `spaarke-bff-dev` = master `9f4afe82ac` (R3 readback 14:40:39Z). Rules: D-194, D-199 and the D-199
> amendment. Results go in section 5. No test data is created: the steps use open POL-MATTER-NOBUDGET v3 items that the owner would
> act on anyway. Each Assign Task creates one real `sprk_event` (type Task); the owner keeps or deletes it afterwards.

## 1. Fixtures (read 2026-10-30)

All three matters are in BU1 (`cb15f587`), not secure. Each has one open v3 item (`sprk_signalstatus` Open). The v1 and v2 rows are
already Resolved/Superseded; only `sprk_signalstatus` counts, `statecode` stays Active by design.

| Run | Matter | Open item (Signal) | Assignee (contact) | Expected recipient kind |
|---|---|---|---|---|
| A, internal, box ticked | PAT-191184 `e9638630-6a63-f111-ab0c-000d3a582930` | `a5094ce2-cfd2-f111-84e8-002248359bb2` | Test User 1 `ac6d7b68-fd21-f111-88b5-7ced8d1dc988` (testuser1@spaarke.com; Workforce plane) → systemuser testuser1 `8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c`, BU1 | Internal |
| B, external, box ticked | CMRCL-788888 `042f4462-860e-f111-8342-7c1e520aa4df` | `517e44e8-cfd2-f111-84e8-002248359bb2` | Ralph Schroeder `2e419a4f-010d-f111-8342-7ced8d1dc988` (ralph.schroeder@hotmail.com; External/CIAM plane; active grant `6e59257b-b0c1-f111-a05c-0022482913fc` on this matter) | External |
| C, box unticked | CMRCL-197894 `764eefa8-a453-f111-a825-7c1e520aa4df` | `551f72ee-cfd2-f111-84e8-002248359bb2` | any contact | none (notify off) |

Optional D, no access (gate negative): an internal contact whose one linked user cannot read the matter (walled or secure). Pick at run
time; expected: no email, no bell, the wizard's quiet warning "no access" (`assignee_email_not_sent_no_access`).

## 2. Owner steps (Console, as the owner)

For each run: open the matter's no-budget item in the Decide lane → Assign Task → assignee as in the table, a short What to do and a
Due date → "Send email notification" ticked (A, B) or unticked (C) → Confirm. Note the time (UTC) and any warning on Confirm.

Then, for B only: open ralph.schroeder@hotmail.com, and sign in to the external portal as that contact.

## 3. What each run must show

| Run | Email | Bell | Wizard | Other |
|---|---|---|---|---|
| A | One email to testuser1@spaarke.com **from the owner's mailbox**: matter link, What to do, Due | One `appnotification` for testuser1, "A task was assigned to you", "Open matter" opens PAT-191184 | No warning | `sprk_communication` with `sprk_correlationid = assign-task-{eventId:N}`, filed under the matter |
| B | One email to ralph.schroeder@hotmail.com **from the system mailbox** (not the owner's address); portal link; assigner by display name only; no '@' of the assigner in the body | **None** (external: no bell) | No warning | Same tracked communication. In the portal, the task shows on CMRCL-788888 (generic event row, no deep link: DI-135 #2445) |
| C | None | None | No warning | The task is still created |

## 4. Checks I run after each step (read-only)

Dataverse (MCP `read_query`):
- The task: `SELECT sprk_eventid, sprk_eventname, sprk_regardingmatter, createdon FROM sprk_event WHERE sprk_regardingmatter = '{matter}' AND createdon > '{run time}'`.
- The tracked email: `SELECT sprk_communicationid, sprk_correlationid, sprk_from, sprk_to, sprk_regardingmatter, createdon FROM sprk_communication WHERE sprk_correlationid = 'assign-task-{eventId:N}'`.
- The bell (A): `SELECT appnotificationid, title, createdon FROM appnotification WHERE ownerid = '8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c' AND createdon > '{run time}'`. For B: the same query for any user mapped to the contact returns nothing.
- The item: the Signal is Resolved/Acted with a Decision Record.

App Insights (`spe-insights-dev-67e2xz`, appId `6a76b012-46d9-412f-b4ab-4905658a9559`; pass `--offset` or the CLI reads only the last hour):

```
traces | where timestamp > datetime({run time})
| where customDimensions.EventId in ("50390","50391","50392","50393","50394","50395")
| project timestamp, customDimensions.EventName, message
```

Expected: A = 50390 `EmailSentAsUser`; B = 50395 `EmailSentFromSystem`; C = nothing. 50391 (fell back) or 50394 (job) means the first send
failed: read the reason before calling it a pass. 50393 `BellNotCreated` on A is a fail.

## 5. Task 170 and the BUD- number (same session)

- **170, commit latency:** after any one decision commit, query `requests | where name has "/decision" and timestamp > datetime(2026-10-30T14:40:39Z)`
  for duration, and the dependency count per operation (`dependencies | where operation_Id == ...`). Before: 28 WhoAmI, 22.4 s per commit.
- **BUD- number (D-193):** one Create budget on a no-budget item, then `SELECT sprk_budgetid, sprk_name, sprk_budgetnumber FROM sprk_budget WHERE createdon > '{run time}'`;
  expect a `BUD-` autonumber.

## 6. Results

| Run | Time (UTC) | Result | Evidence |
|---|---|---|---|
| A | | | |
| B | | | |
| C | | | |
| D (optional) | | | |
| 170 | | | |
| BUD- | | | |
