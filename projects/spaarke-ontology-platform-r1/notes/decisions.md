# Decisions log — Spaarke Ontology Platform R1

> **What this is**: the consolidated log of every owner decision for this project — one line per decision, its
> source, and whether it still binds. The binding subset is restated as one-liners in [`../CLAUDE.md`](../CLAUDE.md)
> §3; full rationale stays in the source documents linked here (not copied).
> **Created**: 2026-10-07 (project CLAUDE.md restructure). **Add** new decisions here AND as a one-liner in
> CLAUDE.md §3 in the same commit; when a decision is replaced, move it to "Superseded" below with the date.

Sources: **design** = [`design.md`](../design.md) §8 (D-1..D-12, §8.0c) and §10 (29 settled items) ·
**spec** = [`spec.md`](../spec.md) §9 Owner clarifications (D-9..D-56) and §6 ADR tensions ·
**v4** = [`v4-prototype-vs-solution.md`](v4-prototype-vs-solution.md) (`#n` entries) and
[`v4-reconciliation.md`](v4-reconciliation.md) (R-/C-/S- rows).

---

## Design-time decisions (2026-09-30 → 2026-10-03)

| ID | Outcome (one line) | Source | Status |
|---|---|---|---|
| §10 ×29 | 29 settled design decisions — "do not re-litigate" | design §10 | Binding |
| D-1 | Spend-data spike resolved by **seeding dev data** (§8.1 checklist) | design §8 | Done (task 005) |
| D-2 | Decision Record field list approved: mandatory `sprk_factsnapshot`; nullable action; nullable decision ref on the Signal; **Decision Record 1 → N Signals** (FK on the Signal) | design §8, schema-draft §2 | Binding |
| D-3..D-8 | Remaining design-phase resolutions (taxonomy reuse D-4: `sprk_memo` reuses `sprk_triagecategory`; MM connector out of scope D-5; …) | design §8 | Binding as written there |
| D-9 | **ADR-039 → path A** (Policy decides what is *true*, Binding what *executes*); **ADR-040 → path B** (`SessionGate` and Decision Record are siblings) | spec §9, §6; design §8.0c | Binding (ADR-040 amended, task 003) |
| D-10 | Classifier recall floor **≥ 80% on ≥ 50 labelled items** — exit gate (task 074) | spec §9 | Binding |
| D-11 | Suppression: 3 dismissals → suppressed, **expires after 30 days**. Grain amended by D-34/D-36/D-39 (per policy + core record; per item when no core record) | spec §9 | Binding (as amended) |
| D-12 | **One evaluator, cadence by lane, two event hooks** (nightly `IScheduledJob` + classification and budget-revision triggers) | spec §9; design §8.3 | Binding |

## Earlier owner decisions recorded during execution (2026-10-04 → 2026-10-06)

| Date | Outcome | Source | Status |
|---|---|---|---|
| 2026-10-04 | **ADR-028 path A**: the Signal writer uses its own managed identity (`mi-ontology-writer-dev`), never the BFF's System Administrator identity, with no credential-chain fallback | spec §6; task 006 | Binding — shape amended 2026-10-07 (owner): tenant-pinned `DefaultAzureCredential` locked to the writer's UAMI, every other source excluded + `AZURE_TOKEN_CREDENTIALS` refused unless MI, pinned by tests; I5 satisfied |
| 2026-10-05 | Task 022: validate rule bodies **at evaluation** and fail closed; model-driven app authoring stays allowed | task 022 | Binding |
| 2026-10-05 | **ADR-009 path A** for task 096's in-process verdict cache | spec §6 | Done (#1294) |
| 2026-10-05 | Prototype **v4 `HANDOFF.md` @ `ae1cc9f`** is the UI/UX baseline; where v4 shows something the solution can't do, flag it to the owner | design header, spec §8.2 | Binding |
| 2026-10-05 | **Ontology admin is in R1** (coordinator's call, delegated) | spec D-22 | Binding |
| 2026-10-05 | One canonical modal approach (dark mode, no white OOB header) → became D-26 | spec D-26 | Binding |
| 2026-10-05 | SmartTodo's palette is the ONE due-urgency scheme (overdue red · 0–3 dark orange · 4–7 yellow · 8–10 grey) | notes/081-progress.md | Binding |
| 2026-10-05 | Unrelated defects found by this project ship as **their own PRs** to master (081, 084–099 …) | task POMLs | Binding |
| 2026-10-05 | Event date columns → **Date Only** (task 098) | task 098 | Binding |
| 2026-10-06 | Event routes use the solution's record-level authorization; after #1312: **one mechanism, `RecordRouteAccessAuthorizationFilter`**, #1312's route deletions stand | task 097 completion | Done (#1302) |
| 2026-10-06 | **Reassigned events are completable**: one predicate `EventStatusCode.IsOpenWork` (Draft, Open, On Hold, Reassigned) for the complete gate and all To Do generation rules | task 097 | Binding |
| 2026-10-06 | **"My events"** = owner OR assigned to caller's linked contact OR `sprk_createdbyperson` = caller; runs as the caller | task 097 | Binding |
| 2026-10-06 | To Do generation's "today" = the **To Do recipient's** time zone (→ owner if a systemuser → UTC), cached per user | task 098 (PR #1359) | Binding |
| 2026-10-06 | The Xrm capability guard is a **blocking Tier 1 job**; `ci-router.yml` docs-only = every changed file is documentation — **path A exception** to ci-cd-unit-test-remediation-r1 FR-A02 | spec §6 | Done (#1309) |

## v4 consolidated decisions (2026-10-07) — spec §9 D-13..D-56

| ID | Outcome | Status |
|---|---|---|
| D-13 | **R-11**: episode-scoped dedupe key `{policycode}|{type}|{id}|{episode}`; re-raise when not suppressed AND (Decide) the per-rule quiet window (default 14 d) passed, or (Do) the subject's date changed | Binding |
| D-14 | Spaarke Ontology Administrator gets **Write on `sprk_policyversion`** so publishing stamps `sprk_inforceto`; the rule body stays immutable by code | Binding (granted, task 008) |
| D-16 | R1 ships Path B + the Do-lane grammar extension + the **Threshold** rule type; Switch and the inquiry SLA deferred | Binding |
| D-17 | **One BFF commit route, record last**; no record on failure; Console User loses Write on Signal (task 049) | Binding |
| D-18 | The **writer creates budget revisions** (after a caller check) and the revision **updates the budget amount** | Binding (privilege granted, task 008) |
| D-19 | Approve variance = **record-only** action | Binding |
| D-20 | Inquiry SLA + its three actions **deferred after R1** | Binding |
| D-21 | This project builds the **server-side work-assignment create** | Binding |
| D-22 | Ontology admin = a **gated workspace tab**, writes as the caller; admin role granted on `sprk_triagecategory`; five tables in Spaarke Platform read-only; owner's account holds the admin role | Binding (role + app done, task 008) |
| D-23 | The **UI composes the row headline**; the rule sentence stays literal | Binding |
| D-24 | **FR-28 amended** to name the UI kit; reuse first | Binding |
| D-25 | "Today" for each item = **assignee's → owner's → UTC** time zone | Binding |
| D-26 | **Adopt the canonical modal** (`SprkModal` + `WizardShell`, in-app, ADR-050 amendment) **and migrate existing wizards** in this project | Binding |
| D-27 | Events: the Do lane, Reschedule and the Daily Briefing use **`sprk_duedate` always**; `sprk_finalduedate` informational | Binding |
| D-28 | **`statuscode` is authoritative**; `sprk_eventstatus` deprecated (inventory readers first) | Binding |
| D-29 | To Do score on **calendar days**; writer **AppendTo** on communication/event/todo/workassignment; caller-unresolved stays **#1312's single 403**; Tier 2 ADR Compliance timeout **5 min** | Binding (grants + CI done) |
| D-30 | **Severity column on `sprk_policy`** (Info/Warning/Critical, shown High/Medium/Low) | Binding (column live, task 007) |
| D-31 | Overdue-To Do rule applies to **all To Dos** | Binding (no-core visibility per D-35) |
| D-32 | Switching a rule Off → On **re-raises** still-true subjects as new episodes | Binding |
| D-33 | **Signals and Decision Records are secure children** of secured records (access-control mechanism), with three role edits | Binding (roles done, task 008; registration = task 039, awaiting uac-r2 review #1355) |
| D-34 | Signals and Decision Records group under the item's **core record** (not "matter" only), reusing `CoreAncestorResolver` | Binding (extended by D-36) |
| D-35 | Items with **no core record** → Signal visible to the item's **owner only**, "Not filed" group | Binding |
| D-36 | **All four core types** (matter, project, work assignment, service request), and the model is **extensible**: generic core-record reference (catalog type + id) on Signal and Decision Record; typed lineage lookups on the Decision Record only where the secure mechanism needs them | Binding (columns live, task 007) |
| D-37 | Two core records → the item's **direct filed-under** record wins; ambiguous → matter over project | Binding |
| D-38 | **No skips** anywhere | Binding |
| D-39 | No-core items: suppression per **(policy, item)**; their Decision Record is owned by the item's owner | Binding |
| D-40 | Rule date fields may take **one lower + one upper bound** (nothing more) | Binding (done, task 024) |
| D-41 | **To Do date columns → Date Only**, own task 106 (031 depends on it) | Binding |
| D-42 | Rank: **severity → `sprk_highpriority` → oldest → record number** | Binding (tasks 038, 062) |
| D-43 | Do-lane overdue starts at **1 day** (a knob; tune in UAT) | Binding (061, 064) |
| D-44 | A work assignment shows in the Do lane of **both** assigner and assignee | Binding (038) |
| D-45 | *Reassign* and *Extend response date* are **Routine** | Binding (036) |
| D-46 | **Drop** the `DocumentRowMenu` / `OutcomeCard` reuse (FR-27); status bar on `MessageBar`; C-9 continues on its own | Binding (052) |
| D-47 | Console **Decision Record tab** in R1 | Binding (045) |
| D-48 | **Association-confirmed** trigger (third enqueue site; email project calls it) | Binding (032) |
| D-49 | Recall stored in **three `sprk_triagecategory` columns** (task 074, conflict-check with email project) | Binding (074, 103) |
| D-50 | Null fact renders **Missing**; source freshness filed as a **GitHub issue** | Binding (057) |
| D-51 | Know-promotion rule offers **Assign Work** | Binding (063) |
| D-52 | Wizard **Confirm is the confirmation**; gate tier from a pure `PublicContracts` function; no chat session | Binding (043, 070) |
| D-53 | **Templated (non-AI) drafts** in R1 | Binding (058) |
| D-54 | **Response columns on `sprk_workassignment`** (exact set decided in task 047, with the WA owner and uac-r2) | Binding (047, 044, 061) |
| D-55 | Budget **amount written as the signed-in user**; the writer only creates the revision | Binding (044) |
| D-56 | **No response-due date** on the inquiry in R1 | Binding (070) |

## Execution decisions (2026-10-07, late) — spec §9 D-57..D-62

| ID | Outcome | Status |
|---|---|---|
| D-57 | ~~Draft events: no data change; the Daily Briefing uses `IsOpenWork`~~ **SUPERSEDED by D-74 (2026-10-08)** | Superseded |
| D-58 | Work-assignment response columns `sprk_respondedon` + `sprk_responseoutcome` approved by the owner as area owner | Binding (task 047) |
| D-59 | Server-side work-assignment create on uac-r2's `RecordCreationService` (team-owned), under their review | Binding (task 046) |
| D-60 | Remove the 11 foreign tables from `OntologyPlatformSolution` (reference only) | Binding |
| D-61 | Evaluator writes Signal fields **only when the result changes** (uac-r2 100k-rows-per-pass limit) | Binding (task 031) |
| D-62 | Remove Console User **Create on Decision Record** with task 049 | Binding |
| D-63 | `sprk_finalduedate` informational **everywhere** (notification playbook node, VisualHost card, CalendarVisual) | Binding (task 068) |
| D-64 | Recall gate: **synthetic set, owner labels blind**, ~80–100 items, gate = combined fee-OR-scope recall ≥ 80%, one run | Binding (task 074) |
| D-65 | **One canonical server create path per table**; uac-r2 to pick one of `RecordCreationService` / `OwnedChildWrite` for work assignments and retire the other; 046 follows it, never a third (supersedes D-59's component choice) | Binding (046, awaiting uac-r2) |
| D-66 | Move the shared Dataverse write core out of `Services/Ai` to `Services/Dataverse` (AI → core only), own PR, uac-r2 review; **no ADR-013 exception** | Binding (task 048) |
| D-67 | Merge #1390 after uac-r2 approves → deploy master to dev → finish 039 live gate | Binding |
| D-68 | Merge #1391 (uac-r2 org-owned AppendTo fix) when green + uac-r2 approves | Binding |
| D-69 | WizardShell's skipped-step marker (dashed ring) is **opt-in** per wizard; existing wizards keep the tick for a skipped step; the 058 decision wizard opts in (from the #1386 review F1) | Binding (056, 058) |
| D-100 | **Retire the notification playbooks** (all seven inactive in dev; no fix). Bell notifications are not wanted: the Daily Briefing never reads `appnotification` (DailyBriefingCollector bypasses it) and the ontology worklist / Do lane cover overdue and due-soon work. SUPERSEDES for notifications: D-76, D-79, D-89 steps 3/5 onward, D-94, D-95, D-98 items 1-8 and 10. Kept: fixing the non-playbook appnotification writers (D-98 item 9, task 132) and Designer protection (D-97, task 133). Retirement = task 131 (owner 2026-10-09) | Binding (131) |
| D-101 | AI playbooks: fix **Matter Health only** (matter-health-single, D-96) and deactivate the broken demo + junk playbooks (D-99); no wider AI-playbook programme. Workspace ai-summary fallback, predict-matter-cost, universal-ingest and the executor-contract issues are **explicitly deferred by the owner** (owner 2026-10-09) | Binding (135) |
| D-102 | Merge PR #1467 (task 130, scoped import/publish) when CI is fully green, after a merge-tree check against current master; then apply `notes/task-130-skill-amendments.md` to the deploy skills (owner 2026-10-09) | Binding (130) |
| D-103 | Scoped publish: when publishing a table (or an app, via an app setting) would also publish someone else's pending unpublished change, the script **stops before any import or publish** and lists them; an explicit opt-in flag proceeds with a warning. Not warn-and-continue (owner 2026-10-09) | Binding (130) |
| D-104 | D-100 follow-through: (a) **delete** the dev Azure alert `notification-playbook-total-failure-dev` (done 2026-10-09 11:37Z, read back ResourceNotFound); (b) **delete the notification code verified unused** (CreateNotification executor, scheduler type-2 path + job, their tests) in a second task-131 PR; anything still used is kept and listed (owner 2026-10-09) | Binding (131) |
| D-105 | **Refocus on the ontology; wind down playbook work.** (1) Finish the notification retirement: #1493 + #1507 go through review and merge when green. (2) **Cancel 135** (Matter Health: the Insights card is mounted nowhere; #1496 closed unmerged; dev deactivations + node syncs left in place), **137** and **138** (Designer protection, demo environment: outside ontology scope). (3) **133** (#1497) finishes only because it is in its last round: re-check, merge when green. (4) Lanes return to the ontology critical path (039 → 037 → 031, uac-r2 #1390/#1391) and the non-playbook PRs (#1467, #1494, #1501, #1415). Demo is not this project's environment (owner 2026-10-09) | Binding |
| D-106 | **Ontology critical path only** (supersedes the 2026-10-08 no-parking rule): fix an issue in this project only if ontology functionality (spec + goals) needs it; document every other issue in `notes/defer-issues.md` (D-106 section, with GitHub links) and review it with the owner after the project; don't bury or ignore. Deferred: tasks 121, 122, 125, 129, 131, 132, 133, 134, 136 (their PRs left open). Kept: the ontology tasks; 130 (scoped publish, needed to deploy ontology solution components under D-83; fix only its blocking defects); 111/114 (D-26/D-70 spec commitments); 123 (classifier input); 126 + #1391 (child creates under a matter, needed by the decision executors) (owner 2026-10-09) | Binding |
| D-107 | **Standing approval:** ontology task PRs whose base is the project branch `docs/ontology-platform-design` are merged by the main session once their independent review (and re-checks) pass and CI is green; the owner is told afterwards. Anything targeting master (incl. #1111) still needs a per-PR owner decision (owner 2026-10-09) | Binding |
| D-108 | Add to **Spaarke Ontology Service** (dev): Append on `sprk_budgetrevision`, AppendTo on `sprk_budget`, AppendTo on User, Assign on `sprk_budgetrevision` (all Global) so revise-budget works live and a Secure-matter revision is owned inside the wall. AppendTo only on the budget: no Write (owner 2026-10-09; applied 15:04Z, see notes/security-roles.md) | Binding (044) |
| D-109 | Read on `sprk_budgetrevision`: **Spaarke Basic User** (Deep) and **Secure Record Owner** (Basic, via uac-r2's script + #1515 config entry), mirroring `sprk_budget`; revisions are owned by the matter's owning team, which must hold Read (owner 2026-10-09; applied 16:06-16:12Z with negative/positive controls, notes/security-roles.md) | Binding (044) |
| D-110 | **Standing approval (owner 2026-10-09):** add whatever security privileges users need for ontology features to work — **Read on Spaarke Basic User, CRUD on Spaarke Core User** — without asking each time. Add only what a feature needs; read back before/after; match the repo role XML; record in notes/security-roles.md + deploy-log; tell uac-r2 on #1355 when it touches their role model. Other roles (writer, Secure Record Owner, admin roles) still need an owner decision | Standing |
| D-111 | **Budget inquiry reply (criterion 10):** (a) the disposition is chosen by a human: the matter's **Assigned To Internal** (`sprk_matter.sprk_assignedtointernal`, a contact) via that contact's system user (`contact.sprk_systemuser`); no AI writes it (ADR-013). (b) Recording the disposition **closes** the inquiry (service request inactive). (c) Action rate excludes administrative closures — **H-7** applies: acted ÷ (acted + dismissed), per policy version, last 90 days, "too few to judge" below 5; Superseded, Policy Retired, Condition Cleared and open Signals are not in the denominator (owner 2026-10-09) | Binding (071, 100) |
| D-112 | **A full live deploy into dev is required to test the ontology end to end, including the Console** (tasks 035 BFF + 055 Console/shared components + the ontology solution). Task 130's scoped import is proven live on that deploy; no separate test imports (owner 2026-10-09) | Binding (035, 055, 130) |
| D-113 | **uac-r2 answers (#1355 comment 6089838200 (uac-r2, 2026-10-09)):** #1390 (039) approved (merge when green; owner merges, it targets master); #1391 approved, uac-r2 merges and deploys it; #1515 lineage entries confirmed; **046** uses `OwnedChildWrite.CreateAsync` via `POST /api/v1/child-records/sprk_workassignment` (add to `ChildRecordEndpoints.CreateTables`), no new route or third path, and never sends `sprk_issecure`/`sprk_accesspermission`/`sprk_accessinheritance`; **048** agreed, after #1391 and before 046 (#1501 deferred, so it rebases later); **126** rule = AppendTo privilege depth covering the target row's BU (Global/Deep EqOrUnder/Local/Basic=self), row readable as caller, else uniform 404; Basic User AppendTo on `sprk_recordtype_ref` (Global) already in master XML (8566eab0f) and live on dev, no change; 403 naming the privilege allowed only for organization-owned catalog targets. After 039 deploys, the recent-changes reconcile re-owns existing Signals/Decision Records under Secure roots within ~2 min (expected; include in the 039 live gate). Filed by uac-r2: PB-07 #1546, AI-EX7 #1547, PB-28 #1548 (keep New Documents/Emails/Events notifications paused until #1548). | uac-r2 | 2026-10-09 |
| D-114 | **Inquiry disposition permission (closes F-48 part 1):** anyone who can edit (Write) the service request may record its disposition, not only the matter's Assigned To Internal. D-111 names who is expected to choose it; the server does not restrict it further (owner 2026-10-09) | Binding |
| D-115 | **Standing approval for master merges (owner 2026-10-09, extends D-107):** ontology PRs targeting master are merged by the main session once their independent review (and re-checks) pass, CI is green, and uac-r2 has approved any change to their files; the owner is told afterwards. Supersedes D-107's "anything targeting master still needs a per-PR owner decision". | Binding |
| D-116 | **Recall answer key (amends D-64, owner 2026-10-10):** Claude labels the 92-item synthetic set instead of the owner. Safeguards: a fresh agent labels from the email text and the live category definitions only (never the classifier output, the sealed drafting intent or the owner's labels); it labels all 92 including the owner's 24, and its agreement with the owner on those 24 is reported with the recall result; the final key uses the owner's label where one exists and Claude's for the other 68. The report states that the key is AI-labelled (a different model family from the classifier) and so is weaker evidence than a human key. D-10's floor (≥ 80% on ≥ 50 items) is unchanged | Binding (task 074) |
| D-117 | **Classifier made robust and data-tunable (owner 2026-10-10, "whatever is the most robust"):** after 074 runs 1-2 (21.8%, 34.5%) both classification steps read ONE editable taxonomy, the `sprk_triagecategory` rows (name + guidance): (a) the TRIAGE-EMAIL Action (data) decides from the email text against the category definitions, using rung 5's category only as a hint; (b) rung 5 (`CommunicationClassificationAi`, BFF code, one-time change) reads the same category names + guidance instead of relying only on its code-constant vocabulary, without breaking its existing consumers. After that, refinement is data-only (edit guidance; later the 103 admin screen) and every change is measured with the saved 92-email fixture. Then re-measure once against the D-10 floor (>= 80% fee-or-scope recall, >= 50 items) | Binding (task 074) |
| D-118 | **Work-assignment parity accepted (owner as WA area owner, 2026-10-10; task 046):** a work assignment created through `POST /api/v1/child-records/sprk_workassignment` may differ from the old browser wizard exactly in parity rows 14-17 and 19: owner = the regarding record's BU default team (or the Secure Record Owners team under a secure parent) instead of the user; owning BU follows that team; `createdby` = the BFF application with the user in `sprk_createdbyperson`; under a secure parent the row is created isolated immediately instead of by the <= 5-min job. All other fields identical (live parity: 0 unexpected differences) | Binding (task 046) |
| D-94 | Notification dedup: same record + same user + same notification type within **7 days**, read or not, unless removed from the Briefing; if the dedup check itself fails → **skip** (fail closed) and count it as a run failure (alerts) (owner 2026-10-09, sweep PB-05) | Binding (task 132) |
| D-95 | Users **are** notified about records they created themselves (no self-exclusion) (owner 2026-10-09, sweep PB-11) | Binding (132) |
| D-96 | Fix **matter-health-single** now as its own task (step wiring, prompt, output path, repo JSON); no hiding of the Insights card (owner 2026-10-09, sweep AI-01) | Binding (task 135) |
| D-97 | Repo-deployed **system playbooks are read-only in the Playbook Designer** (save refused); fixing the sync to preserve non-canvas nodes may follow (owner 2026-10-09, sweep PB-08). Until it ships: never save a system playbook in the Designer (CLAUDE.md §6) | Binding (task 133) |
| D-98 | Sweep §13 recommendations approved as a set (owner 2026-10-09): priority Overdue=High, others Normal · notification dates in the user's local date format · documents with no matter notify without a matter line · fix the Briefing-channel WRITER (`customData.category`) · partial-failure alert when >50% of users fail or a user fails 3 runs in a row, retry failed users next run · run at each playbook's set time and catch up missed windows from `lastrundate` (task 131) · fan-out = licensed interactive users only · restricted/secure records in notifications = uac-r2 decides (#1355) · also fix the non-playbook priority bugs (held-email alert, grant-expiry reminder) · delete the dead config blocks · AgentService nodes read the prompt from the linked Action · this project owns all of it as tasks (no parking) | Binding (132, 131, 135-138) |
| D-99 | AI playbooks: matter-health-single first (D-96); rebind workspace ai-summary to a proper summary Action; **deactivate** the broken demo playbooks and the 6 junk rows; deploy universal-ingest to dev with the AI task (owner 2026-10-09) | Binding (task 136) |
| D-93 | Merge #1480 (task 113) when fully green after its last three small fixes; the Console then auto-deploys to dev (owner 2026-10-09) | Binding (113) |
| D-91 | Pause in dev (set inactive) the failing notification playbooks: Tasks Overdue, Matter/Project Activity, New Documents, New Emails, New Events, until the fix is deployed; Due Soon + Work Assignments stay on (owner 2026-10-09) | Binding (120) |
| D-92 | Before any further fix, a full top-model sweep of the playbook engine and every playbook for defects of this kind (Condition false-branch skip, invalid option values, date formatting, membership lag found in the first live run) (owner 2026-10-09) | Binding (120) |
| D-89 | Merge #1461 when green; then in dev: deploy master BFF, turn on sprk_playbooknode auditing (D-80), sync FOUR playbooks (Overdue, Due Soon, Matter Activity, Work Assignments), deploy the failure alert bicep, prove delivery with zz-120 rows. Docs/Emails/Events wait for uac-r2 (owner 2026-10-08) | Binding (120) |
| D-90 | Merge #1459 when green (owner 2026-10-08) | Binding (124) |
| D-87 | Merge #1455, #1460 and #1463 each when fully green (owner pre-approval) | Binding (121, 128, 129) |
| D-88 | Merge #1456: the Console auto-deploys to dev on qualifying master pushes (production stays manual) (owner 2026-10-08) | Binding (127) |
| D-85 | Move the four dev definitions (two "Open 7 Days" views, TASKS & EVENTS calendar, Matter Tasks overdue count) from `sprk_finalduedate` to `sprk_duedate`, scoped publish (owner 2026-10-08) | Binding (129) |
| D-86 | Import VisualHost 1.4.39 to dev WITHOUT tenant-wide publish (first live test of the scoped procedure; stop if a publish-all would be needed) (owner 2026-10-08) | Binding (129, 130) |
| D-82 | LegalWorkspace: its two deploy scripts are DELETED if nothing uses them (else retired); the orphaned `sprk_corporateworkspace` web resource is deleted from dev; task 106's LW changes are already live in the Console bundle (owner 2026-10-08) | Binding (121) |
| D-83 | Tenant-wide publish is removed **repo-wide**: one shared scoped "import, then publish only the imported components" procedure for every script and the dataverse-deploy / pcf-deploy / ribbon-edit skills (owner 2026-10-08) | Binding (task 130) |
| D-84 | Deploy the external SPA to dev (Static Web App) from master 885d0c5f9, shipping one day of merged master including uac-r2 and spaarkeai changes (owner 2026-10-08) | Binding (122) |
| D-77 | **ISS-018 fix as recommended (option d), one PR in this project:** GUID-only list helper emitting `<value>` children (empty list selects nothing); shape check in executor + tests + deploy lint; Condition `Left` accepts numbers; no early render of `item.*`; rebuild Due Soon nodes; update all 7 repo playbooks + dev nodes (owner 2026-10-08) | Binding (task 120) |
| D-78 | Scheduler: when every user's run of a playbook fails → log Error, mark Failed, alert, and DO NOT advance `sprk_lastrundate` (retry next tick) | Binding (task 120) |
| D-79 | Restore matter-membership scope on all notification steps the repo designs that way (documents, emails, events, overdue tasks; extends D-76), roles confirmed with uac-r2 first | Binding (task 120) |
| D-80 | Turn on Dataverse entity auditing for `sprk_playbooknode` in dev | Binding (task 120) |
| D-81 | **No parking:** issues found are fixed in this project as tasks; the LegalWorkspace deploy (no tenant-wide publish) and the external SPA deploy (106/056 changes) are this project's tasks, not hand-offs | Binding (tasks 121-123) |
| D-76 | The overdue-task notification step **regains its matter-membership scope** (`sprk_regardingmatter in myMatters`), matching the repo and the 2026-06-25 deployed design; the live drift (owner-only) is corrected in dev (owner 2026-10-08) | Binding (068) |
| D-75 | Deploy **master's BFF + Console (SpaarkeAi)** to spaarkedev1 now, to clear the HTTP 400s left by the 106 schema conversion; redeploy again after #1390 for D-67 (owner 2026-10-08) | Binding |
| D-74 | **Supersedes D-57.** The Daily Briefing task channels stay **Open only** (matches notifications, D-72); PR #1384 closed unmerged; task 060 returns to the data fix: classify the platform-created Draft `sprk_event` rows and move the wrongly-Draft ones to Open in dev (approved), never a blanket update (owner 2026-10-08) | Binding (060) |
| D-72 | The overdue-task notification ("Query Overdue Tasks" playbook step) **keeps its Open-only status filter**: Draft / On Hold overdue tasks are not notified. A known, accepted difference from the Briefing's IsOpenWork (D-57) (owner 2026-10-08) | Binding (068) |
| D-73 | The same step **orders by `sprk_duedate`**, not `sprk_finalduedate` first; the repo's due-soon playbook copy drops its either-date filter too, so Final Due Date decides nothing in notifications (D-63 everywhere; dev data change approved) | Binding (068) |
| D-71 | Task 111 may deploy to **spaarkedev1** (SpaarkeAi, SmartTodo, DocumentRelationshipViewer, external SPA code pages + SemanticSearchControl PCF) and run the modal regression live; code in its own PR, owner merges (approved 2026-10-08) | Binding (111) |
| D-70 | **ADR-050 Path A** for 056's transitional `SprkModal.legacySize` and `WizardShell` `maxWidth`/`height`; **removed in task 111** with a test that they are gone (from the #1386 review F2) | Binding (056, 111) |

## Superseded or withdrawn

| ID | What it said | Replaced by | Date |
|---|---|---|---|
| D-15 | Skip **Restricted/Limited** matters in R1 and read Signals only through a BFF route | **D-33** — the premise was wrong: Restricted/Limited only exclude external contacts (ADR-003); the staff wall is the **Secure** flag. The BFF read route part of D-15 still stands (FR-24) | 2026-10-07 |
| D-31 (wording) | A no-matter To Do's Signal is "secured by the To Do owner's business unit" | **D-35** (owner only) | 2026-10-07 |
| D-34 (scope) | Core record = matter **or project** only | **D-36** (all four core types, extensible) | 2026-10-07 |
| D-11 (grain) | Suppression per **(policy, matter)** | **D-34/D-36** per (policy, core record); **D-39** per (policy, item) for no-core items | 2026-10-07 |
| C-12 option | Derive a version's end from its successor (never write `sprk_inforceto`) | **D-14** (grant Write; stamp at publish) | 2026-10-07 |
| task 053 | Gate host + acting | Tasks **058** (decision wizard) + **043** (commit route), per D-17/D-26 | 2026-10-07 |
| Narrow skip | Skip a To Do under a Secure project with no matter | **D-38** (no skips) | 2026-10-07 |
