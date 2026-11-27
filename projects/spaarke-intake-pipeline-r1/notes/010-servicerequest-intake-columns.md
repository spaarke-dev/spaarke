# Task 010 - sprk_servicerequest intake columns (2026-11-27)

Script: `scripts/Add-ServiceRequestIntakeColumns.ps1` (modes: `-WhatIf`, real run, `-Verify`). Doc: `docs/data-model/sprk_servicerequest.md`. Rigor STANDARD.

## Deviations from the POML

1. **Step 1 (ontology coordination)** was done by the main session before this task ran; the agreement is the 2026-11-27 row in `decisions.md`. Names and option values used exactly as agreed.
2. **No attorney column.** Owner chose to reuse `sprk_assignedtointernal` (lookup to contact); the POML escalation trigger (not a systemuser lookup) is answered, not fired.
3. **Field security reuses the existing task-133 profile pair** ("Spaarke BFF-Managed Field Readers / Writers") instead of creating a new profile. The pair already exists on dev, its writer members are exactly the BFF application users, and the reader is on every default team. One mechanism for every BFF-only column (see `Set-SecureFlagFieldSecurity.ps1`). The script creates and edits no profile or member; it adds two field-permission rows for stage.
4. **Columns land in the SpaarkeMaster solution** (`MSCRM.SolutionUniqueName` header; precondition checks the table is in it). Task 011's script uses no header; not changed here.
5. **Entity XML not exported (step 6, "if maintained").** `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ServiceRequest/Entity.xml` is maintained, but this script never edits solution source (same rule as `Deploy-EbillingDecisionNoteColumns.ps1`) and a solution export is a heavy cross-cutting step with other agents committing in this worktree. The solution-sync deploy step must carry the five columns and the two field permissions. The data-model doc says so in its status line. Open item.
6. **TASK-INDEX.md and current-task.md not edited** (main session owns them, per the brief).
7. **Own defect, fixed on dev.** My first real run created the two choice columns with BLANK option labels: the script indexed an ordered dictionary by an int, which is positional, so every label read null. Option values were right. Two further bugs surfaced in the same run's read-back (a gap list returned with `, $out` printed as empty failure lines; the label comparison). Fix: options are now arrays of `@{Value; Label}`, gaps return `.ToArray()`. The corrected script's conflict check (p5) caught the blank labels on its next run and escalated (exit 2, nothing changed), as designed. Repair on dev: a one-off `UpdateOptionValue` (labels only, `MergeLabels=true`) for the 10 options plus an entity-scoped publish (scratch script, not committed). Then the real run was repeated and `-Verify` passed. The columns hold no data, so the repair touched nothing else. A fresh environment gets correct labels from the committed script.

## -WhatIf (before the real run) - dev

Read-only. Preconditions all OK: both tables exist; `sprk_servicerequest` is in SpaarkeMaster; reused `sprk_requesttype` = Picklist, `sprk_assignedtointernal` = Lookup -> contact; no intake column existed; both profiles exist; reader profile on all 7 default teams; writer profile members = exactly the two BFF application users, no team. Would create: `sprk_intakechannel`, `sprk_intakestage`, `sprk_sendemailupdates`, `sprk_correlationid`, `sprk_currentrun` (lookup, RemoveLink); would secure stage and grant writer (read/create/update = 4) then reader (read = 4); would publish `sprk_servicerequest` and `sprk_processingjob`. Summary: created 5 (would), unchanged 0, gaps 0, exit 0.

## Real run - dev (owner OK 2026-11-27)

Run 1 created all five columns, secured stage, granted both rows (masked window 15.7 s, only the new column, no data), published. Its read-back reported the blank-label defect above (deviation 7). After the label repair: run 2 (the idempotency proof) created 0, unchanged 6 (five columns plus the stage security), only the idempotent entity-scoped publish ran, gaps 0, exit 0. `-Verify` exit 0. `-WhatIf` after the real run: created 0, unchanged 6.

BFF application users on dev (looked up as application user records, not assumed): `mi-bff-api-dev` appId `5967251e-171c-46fe-a6c2-ef843c90309d` (systemuserid `8793f4b0-01db-f011-8406-7c1e520aa4df`) and `SDAP-BFF-SPE-API` appId `1e40baad-e065-4aea-a8d4-4b7ab273458c` (systemuserid `bb5a90e5-4ca8-f011-bbd3-7c1e5215b8b5`). Both are the writer profile's members.

Profile ids: Writers `917b64c5-47bf-f111-a05b-3833c5e9614d`; Readers `42e542c8-47bf-f111-aaaf-0022482913fc`. Stage attribute metadata id `e9a80603-7eea-f111-84ea-7ced8ddb6f85`.

## Type facts (dev)

| Column | Fact |
|---|---|
| `sprk_requesttype` | Picklist, local option set: NDA Assessment 100000000, Invention Submission 100000001, Policy Question 100000002, Trademark Search 100000003. Reused. |
| `sprk_assignedtointernal` | Lookup -> **contact** (not systemuser). Reused as the assigned attorney by owner decision. BFF resolves systemuser to contact on claim/approve. |
| `sprk_direction`, `sprk_disposition`, `sprk_responseduedate` | Exist (Picklist, Picklist, DateTime), last modified 2026-11-26 (before this task ran). Not created, renamed, altered or written by this task. |
| new five | Picklist, Picklist (secured), Lookup -> `sprk_processingjob`, Boolean (default No), String(36). Schema names `sprk_IntakeChannel`, `sprk_IntakeStage`, `sprk_CurrentRun`, `sprk_SendEmailUpdates`, `sprk_CorrelationId`. All RequiredLevel None. |

## Step 5 - what was and was not proven

Proven:
- Profile pair exists; the writer profile holds read/create/update = 4 on stage and its members are exactly the two BFF application users; the reader profile holds read = 4 (create/update 0); no other profile except the platform System Administrator profile can create or update stage; the column reads `IsSecured = true`.
- The platform's own effective-privilege computation (`systemusers(id)/Microsoft.Dynamics.CRM.RetrievePrincipalAttributePrivileges()`, read-only) for the stage attribute: both BFF application users CanUpdate 4 / CanCreate 4 / CanRead 4; three non-admin internal users (Jessica Broyles, Test User 1, Ralph Dewey; role Core User or Basic User) CanUpdate 0 / CanCreate 0 / CanRead 4. This is the platform answering for those principals, not a read of our own rows.

Not proven (stated plainly): an actual refused write by a non-BFF user. My az identity (ralph.schroeder@spaarke.com) holds the System Administrator role, which bypasses field security. A stage PATCH on a throwaway row (created, patched, deleted) as that identity was ACCEPTED, as expected, and proves nothing about the negative case. No second interactive non-admin session was available. The BFF application user's write also was not exercised live (no BFF code writes stage yet; task 013). A live refusal for an ordinary user (form or Xrm.WebApi) and a live BFF write remain to be checked when task 013 lands or by a non-admin user session. System Administrators (and the BFF application users, which hold that role) keep full access by platform rule: the residual accepted for every BFF-managed column (unified-access-control-r2 owner decision F4).

## For tasks that follow

- Every intake read excludes rows with a null `sprk_intakestage` (ontology's outbound rows carry none of the five columns). Documented in the data-model doc.
- The BFF must treat `sprk_sendemailupdates` null as No and apply the per-channel default (email door ON; Front Door and Console OFF) at create. Copilot has no stated default (spec line 526): it gets No until the owner says otherwise.
- Task 013 names these columns in BFF code; the ebilling `InvoiceNoteColumnsGuardTests` pending-list pattern does not cover this table, so no ArchTest forces a deploy order. Run `-Verify` on an environment before a BFF build that writes these columns is deployed there.
- The script defaults `-SolutionUniqueName SpaarkeMaster` and requires `-BffApplicationIds`; other environments need their own application ids.
