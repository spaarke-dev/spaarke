# Decisions — `unified-access-control-r2`

> **What this file is.** The decisions index for this project, plus every rule that has been superseded or withdrawn, with its date. The project `CLAUDE.md` §3 keeps only the one-line binding outcome of each decision still in force; the reasoning lives here or in the logs below.
> **Created 2026-10-07** by the CLAUDE.md restructure (procedure update, PRs #1335/#1336/#1354). The previous CLAUDE.md is archived verbatim at `handoff-history/CLAUDE-archive-2026-10-07.md`.

## Where the full decision record lives

Kept in place rather than moved: dozens of POMLs, notes and PRs link to them by path.

| Record | Holds |
|---|---|
| [`session27-owner-decisions-and-research.md`](session27-owner-decisions-and-research.md) | The numbered rounds, 2026-09-30 onward: owner rounds and main-session rounds under owner directives. The canonical log; new decisions are appended there as the next round. |
| [`design-register.md`](design-register.md) | Findings, decisions (D-xx), deferrals and prerequisites from design through 2026-09 (§A–I). |
| [`decisions/`](decisions/) | Long-form decision records: external grant expiry, Function impersonation (ADR-052/ADR-028 A5), SPE paging and revoke honesty, workload placement. |
| [`owner-decision-brief-2026-09-18.md`](owner-decision-brief-2026-09-18.md) | The 2026-09-18 owner brief (external licensed users, among others). |
| `task-NNN-*.md` | Each task's own decisions, escalations and known limits. |

## Index: where each standing decision in CLAUDE.md §3 comes from

| CLAUDE.md §3 item | Source |
|---|---|
| Continue autonomously; batch questions | session 27 working agreement (2026-10-01/03) |
| Main session decides the complete fix | round 15 (2026-10-03), as narrowed by rounds 56/59 |
| ADR delegation inside 036 | round 75 |
| "Save the email as documents" | 2026-10-06 owner terminology |
| D1/C9 access model; Created By = FOR, not OPEN | session 27 answers (C9), round 3 (D1) |
| Grant Access rules (C4; cap; contact-to-own-org; Assigned-To uncapped Collaborate) | session 27 answers (C4), round 2 (Q1, Q2, Q5), round 3 (A1), round 3b (A1), round 11 item 1 (ADR-034 A4) |
| Restricted vs Secure | round 2 item 3, round 67 |
| `sprk_isexternal` (blank = internal; Restricted refuses/removes; secure-only allows; guest script first) | rounds 67, 76, 78; task 114 V5 |
| Accepted business-unit read gap | round 77 |
| `sprk_issecure` locked; F3; filed-under-secure is secure; no unsecure cascade | round 2 item 2, round 3b (F3), round 6 |
| A secure record always has a reader (S5), except Restricted wins | round 3 (S5), round 76 |
| Team ownership; record-first; `sprk_createdbyperson`; G5 pattern | D-11 (2026-09-22), round 5, round 3b (G5), rounds 7, 9 |
| Access changes in minutes; ≤2 min child mirror | round 3 (R3/R4), round 11 item 2 |
| Two-root intersection; invoices follow matter; communications inherit; children show the parent's access permission | round 11 item 4, round 10 item 11, round 2 Q6, round 81 |
| No Access on internal users; author-Write enforcement; hidden in SPA; parent permissions control a filed child | round 2 Q4, round 3b (N2, N5), round 82 |
| A child's access follows its parent both ways, locked while it has a parent | round 84 (replaces round 6 item 4) |
| Notifications: Created By + Assigned To; no team fan-out | session 27 answers, round 2 item 9 |
| Broker-only SPE; standing BU writers; JIT Office edit | rounds 69, 70 |
| Locked item-id copy; share-link refusal; Modified by = BFF | round 72 |
| Upload binding for app-uploaded files | task 171 hotfix, PR #1353 (2026-10-07) |
| Reconciliation writes on dev; R4 | round 7 item 1, round 71, round 79 |
| Every route declares authorization; unused unpublished routes are deleted | round 9 item 3, round 14 item 2, round 10 item 1 |
| Finding bar | rounds 56, 59, 74; repo-wide in task-execute Step 9.5 |

## Superseded and withdrawn rules

Each entry: the old rule, what replaced it, and the date. Do not apply these.

- **"At most 2 fix rounds; escalate any F1 still open instead of starting round 3."**
  - WITHDRAWN 2026-10-06 (round 74; repo-wide PR #1336).
  - Review limits cut ceremony, never fixing. Every defect found is fixed in scope, or filed and reported.
- **"Never defer, never sideline; fix every finding in the lane"** (round 15, 2026-10-03).
  - REPLACED 2026-10-05 by round 56: fix real defects, record adversarial or rare fail-closed edges as known limits, guard against over-engineering.
  - Round 59 then clarified that required functionality is still built fully.
  - Round 56's "one more fix round per lane" is itself withdrawn by round 74.
- **"The external flag is irrelevant to sharing"** (2026-09-18 ruling, ISS-024).
  - REPLACED 2026-10-06 by round 67: the flag is load-bearing for Restricted, and blank is internal.
- **"A user flagged `sprk_isexternal` blank is refused" / "an unreadable flag is external"** (task 063's fail-closed reading).
  - REPLACED 2026-10-06 by round 67: blank = internal everywhere. Implemented by task 114.
- **"No user is ever granted an SPE container permission"** (2026-08-25, SECURE-DOCUMENTS-BUILD-PLAN §1).
  - REPLACED 2026-10-06 by rounds 69/70: standing writers on BU containers, and JIT writers on secure containers for Office edit only.
- **SPE writer-identity matching** (`.claude/patterns/.../spe-writer-identity-matching.md`).
  - SUPERSEDED 2026-10-06 by broker-only document bytes (task 171).
- **"The last reader can never be removed," applied to external-flagged users on Restricted records** (S5 as applied by task 114's first build).
  - SUPERSEDED 2026-10-06 by round 76 (Restricted wins).
- **"Integration branches merge with a merge commit and keep the branch."**
  - SUPERSEDED 2026-10-06: PRs squash-merge on Router green; delete the branch only with no check pending.
- **"`work/unified-access-control-r2` is ~314 commits behind master; never build or run code there."**
  - SUPERSEDED 2026-10-07: the work branch was merged with master.
  - Task and fix work still happens in fresh short-path worktrees from `origin/master`, because the work branch lags between merges.
- **"`gh pr checks` never lists Router; probe it by name via the check-runs API."**
  - SUPERSEDED 2026-10-07 (verified): `gh pr checks <N>` lists `Router` with its state.
  - Still gate on the full rollup with no check pending.
- **"The skill files reach this worktree on the next master merge."**
  - DONE 2026-10-07: the merge brought PRs #1335, #1336 and #1354.
- **"Master publish size measured 36.13 MB (2026-10-06)."** Removed as a copied number.
  - Re-measure master fresh for every BFF task (root `CLAUDE.md` §10 / `.claude/rules/bff-hygiene.md`).
- **NFR-07 hard gate** ("characterization suite exists BEFORE Phase 1 changes behaviour").
  - SATISFIED: Phase 0 built it.
  - The gate no longer constrains new work and was dropped from CLAUDE.md's gate list.
- **Line-numbered reuse pointers** (`DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989`, `PlaybookSharingService.cs:302-350`). Line numbers removed as stale; the type and member names remain in CLAUDE.md §2.

## Background moved out of CLAUDE.md (still true, kept for its reasoning)

### The MDA PCF disclosure (2026-08-25)

CLAUDE.md fact 1 used to read "on the MDA, Dataverse enforces natively and we write no code". That is false for MDA-hosted PCFs that read through the BFF.
- **What happened:** a non-admin who was denied Read on all 442 documents by Dataverse saw a matter's full document list, and opened and downloaded the files. The path was `SemanticSearchControl` → `POST /api/ai/search` on an MDA form.
- **What it means:** native MDA forms, grids and views are Dataverse-enforced; embedded PCFs that read via the BFF are not.
- **Sources:** design §4.1 correction, and `task-046-secure-project-owner-role.md` §7b.

### Service request is core but not externally grantable (2026-09-09, task 028)

- A service request is **core**: nothing else confers access to it. But it is **never grantable to an external contact**. Internal workforce users submit service requests through the SPA, and a law firm must never reach one.
- `sprk_externalrecordaccess` carries lookups for project, matter, work assignment, invoice and organization, with deliberately no service request. `CallerPrincipal` composes exactly three externally grantable root sets.
- Service-request scoping is a different mechanism: the `service-requests` external module scopes by requester (`sprk_requestedby == caller`), shipped by SPA-external-access-platform-r2 #028.
- Do not "complete the fourth root". Source: `task-028-service-request-root.md`.

### Why CLAUDE.md has a "standing directives" layer (2026-10-06)

`current-task.md` grew from 5 KB to 483 KB because every checkpoint stacked a new block over the old ones. The repo procedure now rewrites it as current state only (context-handoff "State, not history"). Standing items moved to CLAUDE.md. The old journal is at `handoff-history/current-task-archive-2026-10-06.md`, and the conversion review is at `handoff-history/2026-10-06-conversion-review.md`.
- **"`ExternalAccessReconciliationJob` is report-only until the owner reviews a report"** (round 7).
  - SUPERSEDED on dev 2026-10-08 by round 79: writes enabled after a 0-change report. Other environments still need the explain-and-confirm step.
- **"Communications inherit the parent's access permission; a child carries no permission of its own"** (round 2 Q6, as written in `docs/data-model/sprk_communication.md`).
  - EXTENDED 2026-10-08 by round 81: To Do, Event, Communication and Document show the parent's value in their own column; a parentless child keeps its own value (recorded only).
  - Task 173 (PR #1458): the column is a DISPLAY copy (most restrictive across the parents), written by the BFF and locked on the form while a parent exists. Q6's ACCESS rule stands: no access decision reads a child's copy. `scripts/Retire-CommunicationAccessPermission.ps1` is deleted (running it would destroy the column).
- **"Parent unsecured → its secured work assignments and projects STAY secure; no unsecure cascade"** (round 6 item 4, 2026-10-02; task 158 constraint).
  - REPLACED 2026-10-08 by round 84: a child's access always follows its parent, both ways, and is locked while it has a parent.
- **D-113, G5 AppendTo on business-owned lookup targets (systemuser, team, businessunit), as given on #1355 comment 6089838200 §6.** The original rule applied every grant's depth at the caller's own BU.
  - AMENDED 2026-10-16, after UAC's review of ontology PR #2012 (task 126): **each grant applies at its own anchor BU**, as Dataverse evaluates it. Roles can come from any BU, and an owner team's role is anchored at the team's BU.
  - The amended rule:
    - Global: any BU.
    - Deep: the anchor BU or a BU under it (`UnderOrEqual`, asked as the caller).
    - Local: the anchor BU only.
    - Basic: only the caller's own systemuser row. Team and businessunit at Basic are denied.
    - A missing anchor is denied.
    - The row must always be readable as the caller, and every refusal returns the same uniform 404.
  - Why: the literal rule over-grants. It would apply a foreign team's Local or Deep grant at the caller's own BU. PR #2012's own-BU-only narrowing gives a false 404 to matrix-BU and cross-BU owner-team users.
  - Still open: whether `RetrieveUserSetOfPrivilegesByNames` returns team-inherited grants. Ontology's live leg will settle it.

- **A work assignment's owning business unit follows its matter** (owner, 2026-10-29).
  - Primary-parent order for `sprk_workassignment`: matter, project, invoice, event (its filing order, `SecureRootInheritance.TypedLookupsOf`); any other lookup (e.g. `sprk_regardingcommunication`) after, by column name.
  - Before: a work assignment, being a root, had no order entry, so its parents ranked by column name (event before matter). Found by field-mapping's 071 review.
  - Shipped in PR #2451 (3d54ac845d): `SecureChildLineage.RootLookupOrder` entry derived from `TypedLookupsOf`. Dev: 0 of 31 work assignments change primary.
  - Communication ranking last: CONFIRMED by the owner 2026-10-29 ("a work assignment's parent is the core record; it's important that we have this family structure").
  - The two older mismatches (creation vs un-secure for a work assignment filed only under an invoice or event; the Decision Record gate writer vs the owner-check job): owner said FIX BOTH NOW in uac-r2 (2026-10-29); both resolve to the matter.
  - Person-owned work assignment that is filed: owner chose the MATTER's unit on every path (2026-10-29). Shipped on `fix/uac-r2-wa-owner-paths` (creation through intermediates, resolver look-through, core never content, cascade order, matter-move hop).
  - Dev row moves from that change (2 events to BU1; 1 document + 1 communication user-owned, reported only): owner GO (2026-10-29), after the report-only run confirms the list.

- **A family shares one ownership; records are owned by business units/teams, not named users** (owner, 2026-10-29: "records are not owned by named users they are owned by business unit/teams. But whatever the ownership the family members have the same ownership").
  - Consequence: a person-owned core record (a work assignment or project created from the app form) is an anomaly; its family follows its core record (matter, then project) like any other member. Projects follow their matter too.
  - Follow-up (not yet built): re-own person-owned work assignments and projects to their family's team, and make person-owned projects follow their matter on every owner path.
  - **Re-owning roots before task 195** (main session, 2026-10-30; clearly within the owner's 2026-10-29 rule). Fix 3 (`fix/uac-r2-root-family-owner`) re-owns an ordinary work assignment or project to its family's team. An unfiled, person-owned one goes to its person's business-unit default team.
    - Until task 195 (#2352) writes the project pair, every project's pair is empty, so a person-owned project counts as unfiled. Projects are NOT excluded from that rule.
    - Why: the target is the default team of the unit the project already sits in, so the move changes no business-unit visibility, and it removes person ownership now, as the owner asked. When task 195 backfills the pairs, a project filed under a matter in another unit moves on to that unit's team through the same pass.
    - Dev on 2026-10-30: 6 person-owned projects (1 in BU1, 5 in the root unit). All of them stay in their current unit.

## 2026-10-31: task 195, owner decisions on the design note (fix/uac-r2-195-project-filing-root, notes/task-195-project-filing-root.md)

1. **A project can be moved to another matter** ("Allow moving to another matter"), so a second matter is not refused.
   - There is one server step. It files a project under a matter and re-files it to another matter, writing the regarding pair app-only with If-Match and read-back. The client never writes the pair.
   - A move is a re-file and follows the existing guards:
     - nothing is ever released from isolation;
     - filing under a secure matter secures the project;
     - leaving a secure matter never un-secures it;
     - the owner and the documents' expected container follow through the root-owner and ordinary-moves passes.
   - The pair still names one root at a time.
2. **Adding a matter to a project's matter list goes through the wizard only** ("we should move the add a matter to project's matter list to use the wizard so there are not alternative paths; we want consistent 'Create' paths and not e.g. using OOB quick create or form").
   - Scope: replace the model-driven app's out-of-the-box "Add Existing" / quick-create path on the Project ↔ Matter subgrid with the wizard, which calls the server step. There must be no other way to add the link.
3. **The dev backfill is accepted, including all 3 unit moves:**
   - PRJ-000034 and PRJ-000038 move root → BU1;
   - Project 6 moves BU1 → root, following its matter LITG-153116, which is person-owned in the root unit.
   - It is applied after the feature is deployed, report first.

## 2026-11-01: BU1 move on dev, owner decisions (Q1–Q12 of the RBU1 plan)

The owner accepted every recommendation ("yes follow your recommendation for the bu 1 move"). Q1 had been decided on 2026-10-30: Core and Basic User read product configuration org-wide (#2474). The plan and the live re-read behind these answers are in the session scratch plan `rbu1_plan.md` §6–§7 (dev only).

- **Product data (Q2 + K4).** Product configuration rows owned by the administrator's own user are first assigned to the root default team, which is what the resolver answers for an unfiled root row. Records are owned by teams, not people. The administrator then moves to BU1 without moving their records. Their customer records follow their families into BU1. Platform components (apps, bots, AI models, environment variables) stay where they are.
- **Who moves.** Every enabled human in the root, including the administrator's admin-mode account (Q5) and "# Spaarke Demo Access" (to BU1, not to "Spaarke Demo", Q6), plus the two dev BFF application users. The ontology writer identity stays in the root (Q3). Identities that are not dev BFF identities stay where they are; their clean-up is a separate owner step, tracked privately (Q4).
- **Policy versions (Q7).** Accepted on dev for now: a new version lands in its publisher's unit. The fix is that a version follows its policy's owner (family rule), handed to the ontology session.
- **Customer business unit setting (Q8).** Already BU1 on spaarke-bff-dev; no change.
- **Ambiguous rows (Q9).** Communication accounts and the container record are environment configuration and stay in the root. A communication rule with a matter follows its matter. Personal workspace layouts and personal views stay with their user.
- **Scope (Q10).** Every customer scope (A–D) is re-owned in this change, from a fresh dry run. Roots first; children follow through the ordinary-moves pass.
- **Reconciler (Q11).** Not switched off: the setting restarts the BFF. The application users move in a quiet window, with their roles re-added in the same script.
- **Administrator role (Q12).** Pre-assign the BU1 System Administrator copy, only if the one-user probe proves that a pre-assigned target-unit role survives a move. Otherwise the owner restores the role from the Power Platform admin center.
- **Execution.** Each write step shows its dry run and waits for the owner's go. The timing is coordinated with the other sessions on shared dev (R3, ebilling).
- **Playbooks (2026-11-01, owner answer to the product hand-over dry run).** The BFF's playbook routes authorize by owner == caller (#2505), so a team-owned playbook could not be edited, shared or changed through the BFF. The 34 dev playbooks therefore stay owned by their author. The other 263 product configuration rows go to the root default team. The playbooks follow once #2505 is fixed.
- **Done so far (2026-11-01).**
  - **Product hand-over.** All 263 rows are assigned to the root default team and read back (after R3's "done").
  - **Step 1 probe.** The probe ran on the administrator's second account (`rbu1s1_probe_log.json` in the session scratch).
    - The move held: the account is in BU1 and the BU1 default team.
    - Its field-security profiles are unchanged (none).
    - With `DoNotMoveAllRecords`, all 5 of its rows stayed in the root: 2 mailboxes, a queue, user settings and 1 outbox row.
  - **A role from the target unit, assigned before the move, does NOT survive it.** Every role is stripped, so Q12's pre-assignment path is out. The owner restores the administrator's System Administrator role from the Power Platform admin center. Step 3 therefore runs last, while the owner is at the admin center.
  - **Probe roles restored.** The BU1 Console and Office Add In copies are assigned; Basic comes through the BU1 default team.
  - **Outbox row not moved.** Setting the outbox row's owning unit was refused: Dataverse requires the row's owner to hold Read on the table, and no role the account holds grants Read on Notification Outbox (only Core User and the admin and service roles do).
    - The row stays in the root with the same owner. Nothing reads it as that user.
  - **Before step 2.** Check, for each mover's owned tables, that the mover's BU1 roles hold Read, or expect the same refusal.
- **Step 2 done (2026-11-01).**
  - **Who moved.** All 10 humans are in BU1 with the planned BU1 role copies, all read back:
    - Core + Console + Office Add In: Chelsea, Jessica, Lori, Ralph Dewey, Demo Access;
    - Console + Office Add In: Jane, John, Eyal, Jake;
    - none: the admin-mode account.
  - **Rows.** Every owned row (mailboxes, queue, user settings, navitems, outbox) is now in BU1 under the same owner.
  - **How the rows move.** Under the default move, the mailbox and queue move inside the call. Every other row moves in a platform async job ("Update business unit for all records to Owner Business Unit", operationtype 95), which completed in about 30 s per user.
  - **Steps 4 and 5 must wait for that job before reading back.**
- **Step 4 done (2026-11-02).**
  - **Dry run.** A fresh dry run after step 2 gave the same 687 explicit Assigns as step 0. The 3 `sprk_workoffice` rows are skipped: the BU1 team's role (Basic User) has no Read on that table, so Dataverse would refuse that owner. They stay with the administrator.
  - **Roots.** 30 roots were assigned at 13:35:48Z. The reconciler's ordinary-moves pass carried all 119 children by 13:43Z (about 8 min).
  - **The rest.** Then 654 more: 651 assigned and read back (owner = BU1 team, BU = BU1); 3 contacts were already on the BU1 team.
  - **Grant rows.** The 32 grant rows were all non-secure: 17 under records that moved in this step and 15 under records already in BU1.
  - **Failures.** None, and no row was overwritten.
- **Step 5 done (2026-11-02, inside a window agreed with R3 and ebilling).** Both dev BFF app users are in BU1.
  - **Roles.** The roles are the BU1 copies of exactly the set each held before. The time with no roles was 6.8 s and 4.0 s.
  - **Profiles.** Field-security profile membership is unchanged, compared by id: Identity Link Writers, BFF-Managed Field Writers, Standing Grant Administrators, System Administrator.
  - **Records.** Both platform record-move jobs completed, with 0 rows left in the root (a live count per owned table).
  - **Health.** BFF /healthz and /healthz/dataverse were 200 afterwards.
  - **Remaining.** Step 3, the administrator's own account, is the last step. The owner restores System Administrator from the Power Platform admin center.
- **Step 3 not done, by owner decision (2026-11-02).** Dataverse refuses a self-move unless the org setting `DoNotRemoveRolesOnChangeBusinessUnit` is on. Nothing changed.
  - The owner chose to leave the administrator's account in the root, over turning that setting on.
  - **Consequence, accepted.** Records that account creates unfiled are owned by the root team (the creator's unit), so its own test data does not take the customer shape.
- **Move closed (2026-11-02).** All 11 interactive humans except the administrator, and both dev BFF app users, are in BU1. The customer families are owned by the BU1 team. The 156 other app users stay in the root (Q3, Q4).
  - Rollback source: `rbu1s0_snapshot_*` in the session scratch.

## 2026-11-05: Decision Records and Signals for flagged licensed users (owner, from the ebilling 082c-2 review)

**Question.** A system user flagged `sprk_isexternal` is a licensed user (for example, licensed outside counsel). The question was whether such a user should read Decision Records (`sprk_decisionrecord`) and Signals (`sprk_signal`), through the model-driven app or through the BFF's caller-scoped reads (the assistant `dataverse.*` tools and `GET /api/v1/records/{table}/{id}/family`).

**Facts checked on dev (2026-11-05, read-only).**
- These roles grant Read on both tables:
  - Spaarke Basic User (user depth);
  - Spaarke Console User (deep);
  - Spaarke Ontology Administrator;
  - Secure Record Owner;
  - the service and admin roles.
- **Every member of a business unit gets Spaarke Basic User through the unit's default team.** Records are team-owned (D-11), so a member reads team-owned Decision Records and Signals through that role alone.
- The one enabled flagged user on dev is the owner's guest account, and it holds Spaarke Console User. Removing that role would take away the Console and would not stop reads through Basic User.
- **No partner or contact path reads these tables.** The external modules don't list them, the external SPA doesn't read them, and the notifier sends fixed text. The only external touch is an existence check that returns true or false.

**Decision.** Owner, 2026-11-05: option "same as licensed users".
- A flagged licensed user reads Decision Records and Signals as their Dataverse roles allow, like any licensed user and the same as in the model-driven app.
- No role change and no code change; Console access is unchanged.
- This matches the solution approach: Decision Records are for licensed Power Apps users only; contacts and partners have none.
- It is the same class as round 77 (role depth for flagged users).
- The BFF's invoice-family hiding for flagged callers stays as it is: it narrows rows by invoice visibility and does not narrow columns.

**Rejected.** Option "exclude flagged users": refusing the two tables to flagged callers in the BFF read tools and the family route. Roles can't express it, because Basic User comes with unit membership. It would also limit expected functionality for licensed outside counsel, and the model-driven app would still show the rows through their roles.

## 2026-11-05: Security roles: owner rulings on field-mapping task 046 (feature permission matrix)

The matrix and gap table are in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.1 (fm task 046). uac-r2 applies the role changes on dev with the owner's go; field-mapping builds the role-file changes and the apply script.

- **Applied as additions** (owner, 2026-11-05):
  - G1: Core User gets Create, Append and AppendTo on `sprk_reportcard` (Deep). AppendTo lets a KPI assessment be filed under a report card.
  - G2: Office Add In User's AppendTo on `sprk_eventtype_ref` goes Deep → Global.
  - G4: Office Add In User's AppendTo on `contact` goes Basic → Deep.
  - G5: Billing Administrator gets Read on `sprk_procedure` (Deep). It is already on dev; the role file aligns.
  - G6: Office Add In User, not Basic User, gets Write on `sprk_document` (Deep), for the Word version save.
  - G7/G8: Office Add In User gets AppendTo on `account` and `sprk_budget` (Deep), for the typed event route.
- **Not applied:**
  - G3: Basic User stays as it is; the contact AppendTo comes through the Office add-on (G4).
  - Office To Do and the Console's Do lane stay Core User features.
- **Add-on roles may be held without a base role** (owner chose "standalone"). Each add-on must therefore meet its own features alone. Field-mapping extends the matrix and guard to check each add-on alone; the resulting additions come back to the owner as one batch before anything is applied.
- **Ontology Administrator is the designated cleanup role** for `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision`.
  - D-160/D-162 stand: it keeps Create, Write, Delete and Assign there. The owner wants a cleanup role other than System Administrator.
  - It is the only person role with those rights, held by a few named admins, with Dataverse auditing on for the three tables.
  - Every generic BFF path refuses changes to these tables (task 048), so the model-driven app, under this role, is the cleanup path.
  - Console User's Append and AppendTo on these tables stay.

## 2026-11-05: Security roles: owner rulings on the add-on gap batch (field-mapping task 046b)

field-mapping checked each add-on role alone against the feature matrix. Owner rulings, 2026-11-05, in this session:

- **Office Add In User and AI Analysis User are NOT standalone.** Neither gets the gaps it shows when held alone (Office: 6, including To Do save and events regarding an analysis; AI Analysis: Read, AppendTo and Write on `sprk_document`, the last being G9). Both need Core User. This narrows the earlier "add-ons standalone" ruling for these two roles.
- **Console User needs Core User.** It does not get the 28 Decide-lane rights it lacks alone. The guard records it as an exception to "standalone", like the two above.
- **Two removals (tightening):**
  - Ontology Administrator loses Create on `sprk_budgetrevision`. A cleanup role needs Write and Delete, not Create; creation stays with the table's writer. The rest of the cleanup ruling above stands.
  - Basic User loses AppendTo on `sprk_reportcard` (Deep). Core User now holds it (G1). field-mapping first confirms that no matrix feature needs it on Basic User.
  - The apply script only adds or raises, so the removals need their own reviewed step; uac-r2 applies them on dev under this go.
- **The root business unit's default team no longer holds Office Add In User on dev.** Removed 2026-11-05 by uac-r2 under the owner's go, before the 046b additions, so they reach only users who hold the role directly. It matches the Office add-ins guide: the role is optional and never assigned to a team.

## 2026-11-05: A secure project created from the Console wizard is secure from creation

The owner asked for the best technical and process answer.

**Decision (uac-r2, under the owner's delegation).** The wizard's create declares that the project is to be secure. The server then creates it through the same path as a record filed under a secure parent (field-mapping task 049): secure and isolated from creation, with the Assigned-To pass run after isolation completes. `provisionSecureProject` keeps the container and the rest of provisioning, and is a no-op for what is already done. field-mapping builds it in 049 (they own the wizard and the create path); uac-r2 reviews it.

**Why not change rule A3 as well.** A3 ("an Assigned-To grant that existed before the record became secure is kept") protects people already working on a record that is secured later. Changing it would remove their access, and this case doesn't need it: the fix is to stop the wizard's project being a non-secure record before it is secured.

**Checked on dev, 2026-11-05:** no secure project holds an active granted or shared Assigned-To row, so no existing data needs repair.
