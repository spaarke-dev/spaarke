# Console prototype v4 vs what the real solution can do

> **Date**: 2026-10-07 · **Status**: for owner decision. Read-only investigation; nothing else was edited.
> **Prototype baseline**: `HANDOFF.md` @ **`ae1cc9f`** (v4, findings 1–40), repo `spaarke-prototype`, path
> `projects/2026-10-spaarke-console/`. The local worktree differs from `ae1cc9f` only in pin text, so `src/` line numbers hold.
> Cited as `ae1cc9f:src/...` or `H§x Lnn` (= `HANDOFF.md` section, line).
> **Real-solution baseline**: `origin/master` @ `6e26a07b6` (after #1302 events repair and #1312/#1319 route authorization) ·
> ontology backend on `docs/ontology-platform-design` @ `775b48011` (cited `branch:`) · Dataverse **spaarkedev1**, queried
> read-only on 2026-10-07 (`describe` + SQL on `roleprivileges`).
> **Sibling notes, not repeated here**: `notes/v4-reconciliation.md` (94 rows, v4 vs spec; cited as **R-11**, **S-10**, **C-1** …)
> and `notes/modal-wizard-canonical-approach.md` (the modal decision; cited as **MODAL §n**). This note asks a different
> question: **can the built solution actually do what v4 shows?**

---

## Summary

**47 inconsistencies: 14 Block the build · 27 Change behaviour · 6 Cosmetic.** 20 v4 assumptions checked out as true (end of note).
Decisions you need to make, in the order they block work:
1. **R-11: the re-raise key** (#13, #40). Today one dismissal on a matter mutes Path B on that matter for good. Decide before task 031.
2. **Who can see a Signal** (#17). Signals on Restricted/Limited matters, and everyone's overdue tasks, are readable by the whole business unit. Decide before 031 writes rows.
3. **Which rules R1 ships** (#12, #2, #3). Today the rule grammar and the writer support only Path B. The Do lane, the inquiry SLA, the spend threshold and the email rule can't be written at all.
4. **How "Record decision" commits** (#24, #23, #16, #6). One server call that creates the follow-ons first and writes the one Decision Record last. Who writes it, and what happens on a partial failure.
5. **Cuts to the action list** (#25–#31). Approve variance, Escalate, Close inquiry and Record the response have nowhere to write. Revise budget has no identity allowed to write it.
6. **Ontology admin** (#41, #42, #47). Which identity writes. The admin role has no rights on categories. Nothing checks roles on screen. Where it lives.
7. **Smaller rulings**: headlines composed by the UI rather than templated (#10); kit scope vs FR-28 (#20); the overdue cut-off time zone (#18).

---

## How to read an entry

**Severity**: **Blocks build** means the v4 behaviour cannot be built until a backend, schema, privilege or owner decision changes. **Changes
behaviour** means it can be built, but not the way v4 shows it. **Cosmetic** means wording, labels or invented data. Each entry gives *v4* (what the
prototype shows), *Reality* (evidence), *Options* and a **Recommendation**, and names any reconciliation row it overlaps.

---

## 1. Data: what v4 shows vs the built schema

**1. Severity has no source, and different labels.** · *Changes behaviour*
- *v4*: every rule has `severity: high | medium | low` (`ae1cc9f:src/model/types.ts:136,147`), and it is the first rank key (`derive.ts:16-24`).
- *Reality*: `sprk_signal.sprk_severity` is **Info / Warning / Critical** (100000000–02) and optional. `sprk_policy` and `sprk_policyversion` have no
  severity column (live describe). The writer accepts null (`branch:SignalWriter.cs:43-44`). With no source, rank's first key is empty for every row.
- *Options*: add `sprk_policy.sprk_severity`, or derive it in code. Map high→Critical, medium→Warning, low→Info.
- **Recommend**: a policy column reusing the Signal's three values, with the UI showing the built labels. Overlaps **S-3, C-2, W-13**.

**2. Do-lane rows need a due date and an assignee that Signals don't carry, and the subjects have many candidate columns.** · *Blocks build*
- *v4*: `Flag.due` and `Flag.assignee` (`types.ts:191-193`). Rows read *5d overdue*, *No response from Okafor Lane LLP* (`screenshots/v4/v4-01-do-lane.png`).
- *Reality*: `sprk_signal` has no due-date or assignee column. On the subjects:
  - `sprk_event` has three due dates (`sprk_duedate`, `sprk_finalduedate`, `sprk_tododuedate`, all Date Only since task 098) and **nine contact assignee lookups**.
  - `sprk_todo` has `sprk_duedate` and `sprk_assignedto`, which points to a **contact**.
  - `sprk_workassignment` has `sprk_responseduedate` and eight assignee lookups, including a law firm (`sprk_assignedlawfirm1` → `sprk_organization`).
  - Assignees are contacts, not users, so "assigned to me" needs the user→contact mapping (097 decision B).
  - Open owner question (current-task #4): Reschedule writes `sprk_duedate`, but the Briefing reads `sprk_finalduedate` first.
- *Options*: denormalize a due date and an assignee onto the Signal (S-2), naming the one source column per subject type; or read the subject live at render.
- **Recommend**: denormalize. Use one due column per subject, the same one Reschedule writes (decide owner question #4 first); assignee = `sprk_assignedto`.
  Overlaps **S-2, W-16, C-8**.

**3. A work assignment has no "response" to record.** · *Blocks build*
- *v4*: rule *"no response recorded"* (formal `responsedate = null`, `mockData.ts:127-131,541`); action *Record the response* closes the assignment
  with an outcome (`mockData.ts:772-781`); the work type is *"Work you assigned"* (`mockData.ts:633`).
- *Reality*: `sprk_workassignment.statuscode` is Active/Inactive only. There is no response, outcome or responded-on column, and no assigned-by column
  other than `sprk_createdbyperson` / owner (live describe).
- *Options*: (a) add response columns to a table another domain owns; (b) define "responded" as Inactive and keep the outcome only in the
  Decision Record step; (c) leave the rule out of R1.
- **Recommend**: (b). The rule becomes "active assignment past `sprk_responseduedate`", the assigner is `sprk_createdbyperson`, and
  *Record the response* deactivates the assignment and records the outcome in the Decision Record. Overlaps **X-7** (partly).

**4. The inquiry (`sprk_servicerequest`) has none of v4's states or outcomes.** · *Changes behaviour*
- *v4*: status *Awaiting reply / Replied / Escalated / Closed*, `sentAt`, `dueAt` and a typed outcome (`types.ts:63-73`). *Close inquiry* offers
  *Reply received outside Spaarke · Resolved by phone · No longer needed · Withdrawn* (`mockData.ts:719`). *Escalate* sets *Escalated* (`store.tsx:283-285`).
- *Reality*: statuscode is Active/Inactive. `sprk_direction` is Inbound/Outbound, `sprk_responseduedate` is Date Only, and there is no sent date
  (only `createdon`). `sprk_disposition` = *Write-off · Budget Revised · Scope Approved · No Action*: a **financial** outcome by design (FR-37 note), a
  different question from v4's "how did it close".
- **Recommend**: keep FR-37's disposition. Store v4's close reason as a value in the Decision Record step, not as a new column. Drop the *Escalated*
  state (escalating = an email plus a Decision Record).

**5. Classifier confidence doesn't exist anywhere.** · *Changes behaviour*
- *v4* uses a classification confidence in several places:
  - evidence and Email Review show it (*Interpretation 94%*, `mockData.ts:316`; H§7 E7);
  - the rule version carries a confidence threshold (`types.ts:108`);
  - admin shows calibration bands of acted vs dismissed by confidence (`ae1cc9f:src/admin/AdminView.tsx:343-360`).
- *Reality*:
  - the triage output is `CommunicationTriageResult(Category, Summary, Obligations, Priority, ReviewOutcome)`, with no confidence
    (`master:Services/Ai/PublicContracts/ICommunicationTriageAi.cs:82-87`);
  - `sprk_communication` stores none. `sprk_riconfidence` is an urgency × agreement queue score (`master:Services/Communication/CommunicationEnrichmentService.cs:594-601`), not category confidence;
  - `sprk_policyversion.sprk_confidencethreshold` exists but has nothing to compare against.
- *Options*: (a) show *"classified as X"* with no percentage; (b) add confidence to the triage Action and a column. That is an AI and
  email-project change, and a model's self-reported confidence is poorly calibrated.
- **Recommend**: (a) for R1. Record that the threshold column is unused, and drop calibration from Health (#44). Overlaps **H-4** (partly).

**6. The Decision Record can't hold what v4 records, and its number isn't known before saving.** · *Blocks build*
- *v4*: `steps[]` (each action taken or skipped, with values, gate and outcome), `followOns[]`, gate tier, decider role, *because*, `emailId`
  (`types.ts:283-307`). The read-only replay of a decided item depends on all of these. Confirm says *"writes **DR-0109**"* before recording
  (`v4-06-do-confirm-judgement.png`).
- *Reality*: 22 `sprk_` columns, including `sprk_actioncode` (text, 100 characters), `sprk_action` (lookup → `sprk_analysisaction`), `sprk_authority` and
  `sprk_gatesessionid`. There are no steps, follow-ons, gate tier, role or communication columns. v4's action ids (`complete-item`, `reschedule-item`…) are not
  `sprk_analysisaction` rows, so only `sprk_actioncode` can carry them. `sprk_decisionnumber` is assigned when the record is created.
- **Recommend**: the S-10 JSON columns. Use `sprk_actioncode` (not the lookup) for the catalog action. Confirm says *"a new Decision Record"* rather than a
  predicted number. Overlaps **S-10, R-6, C-5**.

**7. The policy row lacks v4's admin fields, and the live seed contradicts v4.** · *Changes behaviour*
- *v4*: short name, work type, *In force / Dormant / Retired*, retired reason, severity, quiet window, decision plan (`types.ts:115-143`).
  `POL-COMMIT-BUDGET` is named *"Classified fee or scope change, with no budget revision since"*, priority 100 (`mockData.ts:26-27`).
- *Reality*: `sprk_policy` statuscode is Active/Inactive. The decision plan is one text field (`sprk_proposedaction`). The live row is named **"Budget
  Variance Policy"**, priority 500, `sprk_enabled = No`. Its template states a classification as fact: *"A commitment with financial consequence was
  raised…"* (live query).
- **Recommend**: as reconciliation **S-5, S-6, H-2** (task 008 re-seed before 031).

**8. Matter facts and "sources" are invented or come from somewhere else.** · *Cosmetic*
- *v4*: phase (*Phase 3 · Discovery*), firm, *Invoiced to date* from Legal Tracker, and a top-bar *"6 sources · 2 need attention"* with freshness
  (`mockData.ts:12-19,169-178`; `ae1cc9f:src/shell/TopBar.tsx:41-72`).
- *Reality*: `sprk_matter` has no phase column. Budget and spend are Spaarke finance roll-ups (`sprk_totalbudget`, `sprk_totalspendtodate`,
  `sprk_budgetutilizationpercent`), not a Legal Tracker mirror. Connectors are out of R1 (spec §2.2). No table records sources or freshness.
- **Recommend**: map the panel to the real columns and drop phase and sources until the landing contract exists. Overlaps **H-5, C-17**.

**9. Option-set and naming differences to absorb.** · *Cosmetic*
- v4 *Task* = `sprk_event` (subject type *Event*, 100000006). v4 *Inquiry* = *Service Request* (100000003).
- **Trap**: `sprk_todo.sprk_priority` runs **Urgent = 100000000 … Low = 100000003**, the reverse of `sprk_event.sprk_priority` (Low = 100000000 …
  Urgent = 100000003). Any rank input that reads priority must map them.
- `sprk_todo` has its own status *Dismissed* (659490002), which is not a Signal dismissal. Keep the words apart in the UI.
- Communication triage priority is *Urgent / High / Medium / Low*; v4 Email Review shows *High / Normal / Low*.
- v4 card *Send Notification Email*; the shared card is *Send Email* (`master:…/WizardFollowOns/followOnTypes.ts`). In Spaarke, "Notification" means
  the in-app notification outbox (`master:Api/Notifications/NotificationsEndpoints.cs`).
- Signal state is two columns (`sprk_signalstatus` Open / Acknowledged / Resolved, plus `sprk_resolutiontype`). v4 has one `FlagStatus`. The mapping is in **R-14**.

---

## 2. Semantics the backend enforces and v4 ignores

**10. The template rule rules out almost every v4 headline and sentence.** · *Changes behaviour*
- *v4*: the templates quote values from matching rows:
  - `{email.sender}'s email of {email.date} was classified as a {email.category}` (`mockData.ts:40-41`);
  - `“{task.name}” was due {task.due}` and `No response from {assignment.assignee}` (`mockData.ts:105,130`).
  The wording check also allows `matter.name` and `matter.number` (`derive.ts:372-374`).
- *Reality*:
  - **The rule**: a token may only be a subject `when` field, or a field an `exists` clause pins to one value (`branch:PredicateCompiler.cs:20-38`).
  - **Path B gives no tokens**: the live body has `when: {}`, a two-value `in` on `sprk_triagecategory` (not pinned), a date range and a `<>`
    (live query). The sentence must be literal, which it is today.
  - **Do rules can't name their subject**: a task name or assignee is never read by a predicate, so they can't appear in the sentence either.
  - **The headline is never templated**: it is stored as literal text (`branch:SignalWriter.cs:39-41,631`).
  - **Dates render as ISO** (`yyyy-MM-dd`, UTC; `SignalWriter.cs:749-755`), not *12 Sep*.
- *Options*: (a) the UI builds the row headline from the Signal's own display columns (subject name, due state, rule short name). That isn't a claim
  the rule makes, so §0.3 isn't touched. (b) Widen what templates may use, which reopens task 022's settled rule.
- **Recommend**: (a). The sentence stays the rule's literal text, and witness values (sender, date) go in evidence lines. Overlaps **H-3, H-6, C-13**.

**11. *How this was determined* and clause-linked wording have no data behind them.** · *Changes behaviour*
- *v4*: the sentence is made of `Segment`s, each tagged with the clause it rests on. Each clause has a plain-language and a formal form. Each
  evidence line names the clause it tested (`types.ts:151-183`, `derive.ts:356-359`).
- *Reality*: `sprk_sentence` is plain text. `SignalEvidenceRef(Kind, Ref, Tier, Confidence)` has no clause, text, quote or as-of
  (`branch:SignalWriter.cs:23`). The rule body is JSON whose filter values are GUIDs (live body), and no column holds a plain-language clause.
- **Recommend**: a small read-side describer in the BFF that turns the rule body into words (GUIDs → category names). Generate it, don't store it.
  Per-clause evidence as in **H-4 / FR-50**.

**12. Only one of v4's ten rules can be written or evaluated today.** · *Blocks build*
- *v4*: ten rules (`mockData.ts:23-159`): Path B, spend threshold, inquiry SLA, travel, rate card, overdue task, task due soon, assignment response,
  overdue To Do, email match. The rule editor offers Inquiry, Task, Work assignment, Document and Matter measures (`ae1cc9f:src/admin/catalog.ts:26-39`).
- *Reality*, each a hard refusal:
  - **Threshold and Switch** are refused as `RuleTypeUnsupported` (`branch:PolicyVersionValidator.cs:237-241`).
  - **Existence** needs at least one `exists` / `notExists` clause (`branch:…/Schemas/existence-rule.schema.json:22-28`, `minItems: 1`). Every
    clause must be a **verified join**, and only *(matter → communication)* and *(matter → budget revision)* exist (`PredicateCompiler.cs:174-179`).
  - **Relative dates** are only `now` or `now-Nd` (`PredicateCompiler.cs:703`). "Due within 3 days" (`now+3d`) is refused, and master has no
    business-day calculation (grep).
  - **Tables the evaluator can read org-wide** leave out `sprk_workassignment` and `sprk_servicerequest` (`PredicateCompiler.cs:148-159`).
  - **The writer can group a Signal under a matter** only when the subject is a matter or a communication. It refuses event, To Do, work assignment and
    service request subjects (`branch:SignalWriter.cs:239-244,485-495`).
  - **Tasks filed under a project** have no matter, so they would be refused too.
- *Options*: (a) a small grammar extension: allow a subject-only rule (`when` filter, no clauses), allow `now+Nd`, and add matter derivation for
  event, To Do and work assignment via `sprk_regardingmatter`. (b) Author the Threshold schema and compiler. (c) Keep the Do lane on the Daily
  Briefing collector for R1.
- **Recommend**: (a) for the Do lane. It is the least new code and keeps one evaluator. Defer Threshold, Switch and the inquiry SLA. Decide what a
  task filed under a project does (skip it, or group it under the project's matter). Overlaps **W-17, W-15** (with more depth than the spec).

**13. R-11: a resolved Signal can never come back, and v4 depends on it coming back in six places.** · *Blocks build*
- *v4*:
  - a quiet window after a dismissal (`mockData.ts:44`);
  - 30-day suppression that then lets the rule fire again (H§1.4 L143);
  - *Reschedule*: *"the rule raises it again if the new date passes"* (`mockData.ts:734,740`);
  - *Extend SLA* and *Extend response date* (`mockData.ts:707,764`);
  - publishing closes items as *Superseded* and raises them again (H§8 L302; `ae1cc9f:src/admin/RuleWizard.tsx:429`);
  - *Lift* suppression *"raises the item again"* (`AdminView.tsx:280`).
- *Reality*: `sprk_dedupekey = {policycode}|{subject type}|{subject id}` is a unique key (`branch:SignalWriter.cs:482-483`). A second write
  reconciles to the existing row and touches only `sprk_lastevaluated` (`SignalWriter.cs:335-375`), so a Resolved row stays Resolved.
- **Path B makes this worse**: its subject is the **matter**. After one dismissal, Path B never fires on that matter again, not even for a new
  email months later.
- **Recommend**: reconciliation **C-1** (an episode number in the key, plus a re-raise rule). **Decide before 031.**

**14. Suppression bookkeeping has one column to live in.** · *Changes behaviour*
- *v4*: a suppression record per (rule, matter) with a count, the reasons, an until-date, a quiet window and *"would it fire today?"* (`types.ts:309-317`).
  A *Wrong matter* dismissal never counts (`store.tsx:298`).
- *Reality*: only `sprk_signal.sprk_suppresseduntil` exists.
  - **The count** must come from Dismissal-class Decision Records grouped by (version → policy, matter).
  - **Reasons** are free text (`sprk_reason`), so a non-counting reason can't be recognised reliably.
  - ***"Would it fire today?"*** needs the dry run (#39).
- **Recommend**: coded reasons stored as `code — detail` (**R-8**), plus the **R-10** reason flag. Show the until-date from the last dismissed Signal.

**15. Closing items: timing and a double close.** · *Changes behaviour*
- *v4*: *Retire* closes open items **at once** (`store.tsx:781-790`). *Mark complete* closes the item as acted.
- *Reality*:
  - FR-16 closes retired policies at the **next evaluation pass**.
  - Completing a task through the decision also makes the rule false, so the next pass would try to close the same Signal again as
    *ConditionCleared*. The same thing happens with a budget revision recorded through the decision (**X-6**).
  - The closure values themselves match v4 exactly (**R-14**).
- **Recommend**: the evaluator skips Signals that are already Resolved, and Retire calls 033's PolicyRetired path immediately. Overlaps **M-5, X-6**.

**16. Who writes the Decision Record, and whether "every resolution writes one" can be enforced.** · *Changes behaviour (owner decision)*
- *v4*: *"Recording … writes DR-0109 … You confirm as Maria Reyes"* (`v4-06`), and the record shows the decider's role.
- *Reality*:
  - **Users hold write rights**: Console User has Create on Decision Record and **Write on Signal** (Parent: Child BU depth;
    `security-roles.md` §3). A user could close a Signal from the browser with **no** Decision Record, so FR-18's *"no human-resolved Signal has
    a null record"* is not enforced by privilege.
  - **Task 040 plans a BFF writer** (`tasks/040-decision-record-writer.poml` steps 1/3). The record is then created by the writer identity, with
    the human only in `sprk_confirmedby`. The writer can't update a record (HTTP 403 proven, `security-roles.md` §9).
  - **The role** has no column.
- **Recommend**: every resolution goes through one BFF route (#24). Once the BFF closes Signals, remove `prvWritesprk_Signal` from Console User so
  a user can't close one silently. Put the decider's role in the fact snapshot.

**17. Signals leak past matter security, and the Do lane shows everyone's work.** · *Blocks build (security, needs your decision)*
- *v4*: the sentence and evidence quote the email itself (*"…we will need to add three additional custodians…"*, `mockData.ts:315`). The Do lane
  shows *my* overdue work (`mockData.ts:631-633`).
- *Reality*:
  - **Who can read a Signal**: it is owned by the writer, its owning BU is the matter's BU (`branch:SignalWriter.cs:136-148`), and users read it
    at Parent: Child BU depth.
  - **Restricted and Limited matters exist in dev** (one each, live query).
  - **The secure-record machinery doesn't cover Signals**: it isn't in `SecureChildLineage` on master, and `sprk_signal` doesn't appear in master's
    server code at all. So every Console User in the BU tree can read Signals, and their sentence and evidence, on a Restricted matter they can't open.
  - **The same mechanism shows each BU reader every colleague's overdue tasks.**
- *Options*: (a) the evaluator skips Restricted and Limited matters in R1; (b) Signals are read only through a BFF route that checks the caller can
  read each row's matter; (c) extend the secure-child mirror to `sprk_signal` and `sprk_decisionrecord`.
- **Recommend**: (a) now, (b) for the worklist read, with the Do lane scoped per reader as in **C-7/C-8**. **Decide before 031 writes the first
  Signal.** Overlaps **W-16, C-7, C-8**; the secure-matter exposure is new.

**18. Which day "overdue" means.** · *Changes behaviour*
- *v4*: *"1 day past due"*, *"due today"* (`mockData.ts:103-104`; `derive.ts:188-194`).
- *Reality*: the compiler turns `now` into a UTC instant (`branch:PredicateCompiler.cs:93-100`). Event dates are now Date Only (task 098). Task 098 moved
  the Briefing to the **user's local** today. A nightly, organisation-wide pass has no single user time zone, so it can fire up to a day early or late
  compared with the Briefing and SmartTodo colours.
- **Recommend**: compare Date Only columns to a date, "today" in the organisation's default time zone, and compute the due-state label client-side in
  the user's zone.

---

## 3. Components v4 assumes are in the shared library

**19. `WizardShell` lacks five things v4 needs.** · *Blocks build*
- *v4*: H§4.1 L218-225.
- *Reality*: these match the HANDOFF exactly:
  - `WizardStepStatus = 'pending' | 'active' | 'completed'`, with no *skipped* (`master:…/Wizard/wizardShellTypes.ts:18`);
  - its own Fluent `Dialog`, not `SprkModal` (`WizardShell.tsx:47`);
  - no `nav`, `statusBar`, `dismiss` or `onBeforeClose` props (`wizardShellTypes.ts:223-334`).
- **Recommend**: per **MODAL §5–§7** and **Z-1 / C-18**. Not repeated here.

**20. Most of the v4 kit doesn't exist, and the spec allows one new component.** · *Changes behaviour (scope)*
- *v4*: `Disclosure`, `ObjectLink`, `EvidenceLine`, `StatusBar`, `RecordRow`, `AggregateCard`, `MatterCard`, `IssueLine`, `StatusChip`, `BrowseNav`,
  `DiscardDialog`, `CountFilters` (H§1.3 L93-108; `ae1cc9f:src/kit/Kit.tsx`).
- *Reality*: none of these is exported anywhere under `src/client/shared` on master or the branch (grep). FR-28 allows exactly **one** new
  shared component, a generic badge (spec L338-342).
- **Recommend**: amend FR-28 to name the kit. Reuse first: Fluent `Accordion` for `Disclosure`, `MessageBar` for `StatusBar`, `Link` for `ObjectLink`,
  `ConfirmModal` for `DiscardDialog`. Overlaps task **057** and current-task item 2.

**21. `MetricCard` isn't a filter toggle.** · *Changes behaviour*
- *v4*: `CountFilters` are toggle buttons (`aria-pressed`), each with a note line (*"3 past due"*) and progress (*"0 of 7 done today"*), and
  disabled when empty (`Kit.tsx:422-432`; `v4-01`).
- *Reality*: `MetricCard` is a square tile (`aspectRatio: '1'`) with a required icon, `value`, `trend`, `badge` and `onClick`. It has no selected or
  pressed state, no note line and no progress (`master:…/WorkspaceShell/MetricCard.tsx:24-44,58`).
- **Recommend**: extend it with `selected`, `note` and `progress` props and a non-square layout option, per **W-7**. Not a new component.

**22. `StatusBadge` has no "success" tone and is on the branch only.** · *Cosmetic*
- *v4*: *Decided / Done* in the success colour (H§1.1 L54-56).
- *Reality*: task 012's badge tones are `neutral | info | warning | critical` (`branch:…/StatusBadge/StatusBadge.tsx:40`), and it isn't on master
  (the branch is unmerged; master's only "StatusBadge" is a doc example).
- **Recommend**: add a `success` tone when it merges. Overlaps **R-15**.

**23. Follow-on cards create their records in the browser after a create. v4 needs them inside the decision, and the record can't be amended afterwards.** · *Blocks build*
- *v4*: Next steps are *"created only when the decision is recorded; linked to it"* (H§1.4 L131). The record lists them with ids (`store.tsx:320-331`).
- *Reality*:
  - **`FollowOnGrid` exists and works** (Space/Enter, `role="checkbox"`).
  - **The steps only collect values.** Each host wizard creates the record in its own browser-side `onFinish`, seeded to *"the JUST-CREATED CHILD
    record"* (`master:…/WizardFollowOns/steps/AddTodoFollowOnStep.tsx:19`):
    - a To Do through `TodoService.createTodo`;
    - an email through `POST /api/communications/send`;
    - a work assignment through browser `createRecord('sprk_workassignment')` (`…/CreateWorkAssignmentWizard/workAssignmentService.ts:532`).
  - **No regarding column points to a Decision Record**: there is no `sprk_regardingdecisionrecord` on To Do, event or work assignment.
  - **The Decision Record is append-only**, so follow-on ids **can't be added after** it is written.
- **Recommend**: the commit (#24) creates follow-ons **before** writing the one record, then lists their ids in `sprk_followons` (**C-5**). Reuse the
  step forms as-is; move the create calls server-side. Assign Work needs its own path (#31). Overlaps **Z-5, C-5**.

---

## 4. Actions: is there a real write path?

How to read the table: ✓ = exists on master today · ◐ = possible, but not the way v4 does it · ✗ = no write path. The identity and privilege are what a real call would need under the
#1312 route authorization (every new route must be classified in `master:tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` and carry
a record-level filter).

| v4 action | Write path on master | Identity / privilege | R1 scope (spec) | Entry |
|---|---|---|---|---|
| Send budget inquiry | ✗ (task 070; gate reachable only from chat) | caller: Create service request (Core User ✓) + AppendTo on matter; Graph send | FR-36 ✓ | #25 |
| Revise budget | ✗ no route | **only Ontology Administrator** can create `sprk_budgetrevision` | not listed (X-6) | #26 |
| Approve variance | ✗ no table | none | not listed | #27 |
| Escalate · Extend SLA · Close inquiry | ✗ | caller Write on service request | not listed | #28 |
| Mark complete: task | ✓ `POST /api/v1/events/{id}/complete` | caller Write on the event (`master:Api/Events/EventEndpoints.cs:168`) | FR-18 ✓ | #29 |
| Mark complete: To Do | ✓ `PATCH /api/v1/child-records/sprk_todo/{id}` | caller's own update (`master:Api/ChildRecordEndpoints.cs:71-73,162`) | FR-18 ✓ | #29 |
| Reschedule (task / To Do) | ◐ event: no route (only `/filing`); To Do ✓ child-records | caller Write | FR-18 ✓ | #29 |
| Reassign (task) | ◐ no route; assignee is a contact | caller Write | FR-18 ✓ | #29 |
| Send a reminder (assignment) | ✓ `POST /api/communications/send` (`master:Api/CommunicationEndpoints.cs:62`) | caller AppendTo on associations; Graph | FR-18 ✓ | #30 |
| Extend response date | ◐ no route; work assignment isn't in child-records | caller Write | FR-18 ✓ | #30 |
| Record the response | ✗ no column (#3) | — | — | #30 |
| Dismiss | ✗ until 040 + 034 | see #16 | FR-17/18 ✓ | #24 |
| Next step: Add To Do | ✓ `POST /api/v1/child-records/sprk_todo` | caller Create + AppendTo; app writes, team owner | ✓ | #23 |
| Next step: Create Event | ✓ child-records `sprk_event` / `POST /api/v1/events` | caller Create + AppendTo | ✓ | #23 |
| Next step: Send (Notification) Email | ✓ communications/send | as above | ✓ | #23 |
| Next step: Assign Work | ◐ browser `createRecord` only | user, client-side business-unit cascade | ✓ | #31 |

**24. "Record decision" as one commit doesn't exist, and can't be atomic.** · *Blocks build*
- *v4*: *"Nothing executes before Record decision … one review writes exactly one Decision Record"*. One click sends an email, revises a budget, creates
  follow-ons and closes several Signals (H§1.1 L60; H§1.4 L139; `store.tsx:233-374`).
- *Reality*:
  - **No route exists** that writes a record and closes a Signal (040 and 043 aren't built).
  - **The side effects can't be one transaction**: a Graph send (irreversible) mixes with several Dataverse writes.
  - **The record can't be fixed afterwards**: it is append-only, so a failure after it is written can't be corrected on it.
  - **Any new route** must pass the #1312 route-authorization census, checking as the caller that they can write each Signal and append to the matter.
- *Options*: (a) one BFF commit route with a fixed order: validate → run the internal writes → send the email last → write the record listing
  the actual outcomes → close the Signals. On failure, write **no** record and report which writes landed. (b) Write the record first, with a
  status the follow-ups reference.
- **Recommend**: (a), idempotent per review. Partial-failure semantics are the escalation trigger reconciliation already gives task 043. Overlaps **Z-8**.

**25. Send budget inquiry: the gate only works inside a chat session.** · *Blocks build*
- *v4*: the inquiry passes a *"Confirmation required · financial-external"* gate (`mockData.ts:657`), with an Assistant-drafted body and a *5 business days* SLA.
- *Reality*:
  - **The gate is chat-only**: `ConfirmationPolicyEngine` and `GateDecisionV2` live in `Services/Ai/Chat/Gate`. Their only HTTP entry is
    `POST …/sessions/{sessionId}/gates/{gateId}/resolve` (`master:Api/Ai/ChatEndpoints.cs:368`). Calling them from non-AI code needs the
    `PublicContracts` facade (ADR-013 as refined).
  - **No create route for the inquiry row**: `sprk_servicerequest` isn't in the child-records create list (`ChildRecordEndpoints.cs:60-62`).
  - **No business-day calculation** exists in master.
  - **Task 070 isn't built.**
- **Recommend**: the wizard's Confirm step **is** the confirmation, so the gate's job becomes computing the tier text (a pure function). Expose
  it through a facade instead of opening a chat session. Store the SLA as a date (`sprk_responseduedate`) and say "5 working days" only if a
  business-day helper is added. Overlaps **053, 070, current-task item 2** ("no non-chat gate entry point").

**26. Revise budget: no identity in the design may create a budget revision.** · *Blocks build*
- *v4*: *Revise budget* records a new budget (`mockData.ts:671-682`; `store.tsx:273-279`).
- *Reality*:
  - **Only the admin role can create one**: `prvCreatesprk_BudgetRevision` is held only by **Spaarke Ontology Administrator** among Spaarke roles
    (live `roleprivileges` query). Console User and the Ontology Service writer have Read only (`security-roles.md` §3).
  - **No route exists.**
  - **Which budget?** `sprk_budgetrevision.sprk_budget` points at one `sprk_budget`, and a matter can have several.
  - **Nothing applies the new amount**: no code updates `sprk_budget` or the matter's `sprk_totalbudget`.
- *Options*: grant Console User Create on budget revisions, or let the BFF writer create them (a privilege change on a table Path B reads, so it
  is audited); or drop it from R1.
- **Recommend**: keep it (Path B's whole point is "go revise the budget"). Grant the **writer** Create, and make the commit route check the caller
  can write the target `sprk_budget`. Decide whether a revision also updates the budget amount. Overlaps **X-6**.

**27. Approve variance has nowhere to write.** · *Blocks build*
- *v4*: *"Records an approved variance … The budget itself is not changed"* (`mockData.ts:683-695`). Its outcome is text only (`store.tsx:281-282`).
- *Reality*: no variance or approval table or column exists (Dataverse search), and the spec doesn't mention it.
- **Recommend**: drop it from R1, or keep it as a **record-only** action (its whole effect is the Decision Record step). That needs your ruling, because
  then the action changes nothing outside the record.

**28. The inquiry-SLA actions (Escalate, Extend SLA, Close inquiry) have no rule and no states.** · *Changes behaviour*
- *v4*: `mockData.ts:696-723`.
- *Reality*: the rule can't be written (#12) and the inquiry has no Escalated or closed-with-outcome states (#4).
- **Recommend**: defer `POL-INQ-SLA` and its three actions to after R1. FR-37's disposition-on-reply stays.

**29. Do actions on tasks and To Dos: complete works; reschedule and reassign need routes and a contact picker.** · *Changes behaviour*
- *v4*: *Mark complete*, *Reschedule* (pick a date), *Reassign* (pick *J. Whitfield / J. Ortiz / K. Patel*) (`mockData.ts:725-751`).
- *Reality*:
  - **Event complete exists** and is authorized per record. *Reassigned* events can be completed (097 decision A).
  - **Event reschedule and reassign have no BFF route**: #1312 deleted the general PUT, and `/filing` accepts only the regarding columns.
    Today the browser writes the due date through `Xrm.WebApi`, and which date column is still an open owner question.
  - **Reassign writes a contact lookup** (`sprk_assignedto`), sets statuscode Reassigned (659490007) and `sprk_reassignedby` (a contact).
    v4 shows people's names, so the picker must offer contacts.
  - **To Do** complete, reschedule and reassign all go through the child-records PATCH.
- **Recommend**: the commit route calls the existing event and child-record cores. Add one narrow event write for due date and assignee to that
  route family (#1312's "one route per table" rule), and use a contact picker.

**30. Work-assignment actions: the reminder works; extending and recording the response don't.** · *Changes behaviour*
- *Reality*:
  - **The reminder works**: `/api/communications/send`. But when the assignee is a law firm (`sprk_assignedlawfirm1`), the recipient must be
    resolved to a contact with an email address.
  - **Extend response date** writes `sprk_responseduedate`, with no BFF route, and work assignments aren't in the child-records re-file list.
  - **Record the response**: see #3.
- **Recommend**: as #3. Extend runs through the commit route as a caller-authorized update.

**31. The Assign Work follow-on writes from the browser.** · *Changes behaviour*
- *Reality*: work assignments are created only by browser `createRecord`, with a client-side business-unit cascade
  (`workAssignmentService.ts:532`). A work assignment is a secure-record root, and WP-3 says invariant-bearing tables are written through the BFF.
- **Recommend**: in R1, either leave Assign Work out of the decision's Next-steps sets, or open the existing Create Work Assignment wizard
  **after** recording, linked only by the record's text. A server create path is the work-assignment owner's job.

**32. Assistant drafts inside the wizard have no endpoint.** · *Changes behaviour*
- *v4*: action and follow-on fields are *"Assistant drafts, badged until edited"* (H§1.1 L43-44; `derive.ts:132-159,309-338`).
- *Reality*: drafting runs in chat. No non-chat draft endpoint exists, and calling the AI from the commit route needs the `PublicContracts` facade (ADR-013).
- **Recommend**: one *draft field* call per step through the facade, or ship R1 with templated (non-AI) drafts. Overlaps **P-2** (partly).

---

## 5. Host and platform: a mocked single-page app vs the Console inside the model-driven app

**33. v4's top bar and its own theme switch don't exist, and its theme fallback breaks ADR-021.** · *Cosmetic (but do not port the theme code)*
- *v4*: a top bar with search, sources, **Admin**, **Prototype**, a dark-mode switch and an avatar (`ae1cc9f:src/shell/TopBar.tsx:74-170`). The first theme
  follows the OS (`ae1cc9f:src/App.tsx:9-15`).
- *Reality*: the Console is `ThreePaneShell` (Assistant · Workspace tabs · Context) with no top bar. Its theme comes from
  `resolveCodePageTheme`, and *"MUST NOT use OS prefers-color-scheme"* (`master:src/solutions/SpaarkeAi/src/App.tsx:269-275`). There is one shared toast
  host at the top (`ThreePaneShell.tsx:576,692`).
- **Recommend**: matter search lives in the Assistant or Navigator. Admin's home is #47. Don't port v4's theme code.

**34. The Assistant can't open the decision wizard or answer "why not".** · *Changes behaviour*
- *v4*: *Prepare my decisions* lists proposals in worklist order, and each opens the wizard. *Why isn't Tessera on my list?* quotes an evaluation log
  (`ae1cc9f:src/assistant/script.ts:65-135`). The Context pane shows *"Inquiry reply SLA — Not firing, last evaluated 1 Oct"* (`v4-01`).
- *Reality*:
  - **The launch mechanism**: the surface-launch registry's `wizard` kind opens a web-resource wizard through `navigateTo`, which brings the
    Dataverse "white header" chrome (`master:…/surfaceHandoff/surfaceLaunchRegistry.ts:84-112`; MODAL §2).
  - **No Assistant tool reads Signals.**
  - **No evaluation log is recorded**: the evaluator records only the rules that fire, so "not firing — last evaluated" for a rule on a matter has no source.
- **Recommend**: an in-app launch kind (a PaneEventBus event to the worklist widget) instead of `navigateTo`. Defer *why / why not* (**P-2**). The
  Context pane lists only rules with something open.

**35. The wizard opens inside a model-driven app frame.** · *Changes behaviour*
- *Reality*: the Console may itself run in an Xrm dialog (80% × 80%; `master:src/solutions/SpaarkeAi/src/App.tsx:122,205`). A `wizard`-size modal
  inside that frame gets a smaller viewport than v4's full browser window. Opening records (*Open in Matter Management ↗*) uses `Xrm.Navigation`
  from inside the frame.
- **Recommend**: as **MODAL §5** (launch in-app; size against the frame). Test the 4K and small-frame cases in the UAT walkthrough.

**36. The Email Review count must match the Email Review tab.** · *Changes behaviour*
- *v4*: the aggregate row reads *"Eight emails await a match confirmation · 4 to confirm · 2 to choose between · 2 with no good match"* (`v4-01`).
- *Reality*: the tab is **already** a registered workspace widget, `communications-reconciliation`
  (`master:…/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts:1000-1033`). Its list comes from
  `CommunicationQueueFeedService` (mailbox and user scope). A separate count of `sprk_associationstatus ≠ Resolved` would disagree with it.
  The confirm / choose / find split needs candidate scores the feed doesn't return.
- **Recommend**: take the count from the same feed. R1 shows the total only (**W-11**).

**37. "Counts in the Matter Report Card" isn't true yet.** · *Cosmetic*
- *v4*: Confirm says *"Recorded as Judgement — … Counts in the Matter Report Card"* (`derive.ts:207-211`; `v4-06`).
- *Reality*: nothing on master reads `sprk_decisionrecord` (grep).
- **Recommend**: drop the clause until the Report Card reads records.

**38. The narrative is written by a model.** · *Changes behaviour (already planned)*
- *Reality*: v4's narrative is deterministic code (`ae1cc9f:src/workspace/narrative.ts`). The real Briefing narrative is written by a model through a
  playbook, so *"It doesn't choose or order the list"* needs the 062 work.
- **Recommend**: as **W-10**, plus a rule that the narrative may not name an item that isn't on the list.

---

## 6. Ontology admin (H§8)

**39. *Test* (the dry run) is feasible and cheap, but must run as the evaluator, not as the admin.** · *Changes behaviour*
- *v4*: *"Run the condition against what's in Spaarke now. Nothing is written"*, listing matters with *open / suppressed / new*
  (`v4-27-rule-test.png`; `catalog.ts:119-152`).
- *Reality*:
  - **Nothing runs a rule yet**: there is no evaluator (031). But the compiler is pure (rule body in, one FetchXML query out), so a read-only run is
    **one query** per Test: distinct subjects, at most 15 joins, pages of 5,000. Cost is negligible at dev scale.
  - **It must run as the evaluator principal**, which reads org-wide. As the admin (Parent: Child BU depth), Dataverse trims rows and a `notExists`
    clause is **true for every matter**, so the test would disagree with production (`branch:PredicateCompiler.cs:114-117`; `security-roles.md` §9).
  - **The result can leak**: run as the evaluator, it lists matters the admin may not be allowed to read, so filter the displayed rows by the
    caller's access.
- *Limits*: Existence only (#12), no witness evidence, suppression only if 034's counter is read, and no re-raise counts until R-11 is settled.
- **Recommend**: task 101 as planned, with those two identity rules as acceptance criteria. Overlaps **M-7**.

**40. *Publishing supersedes open items and re-raises them* can't work today.** · *Blocks build*
- *v4*: H§8 L302; the Save step counts what will be superseded (`RuleWizard.tsx:429`). The old version gets an end date (`store.tsx:785-786`).
- *Reality*: re-raising hits the R-11 unique key (#13). The re-raised item would silently stay closed. No role can write `sprk_policyversion`, so
  `sprk_inforceto` can't be set.
- **Recommend**: **C-1** and **C-12**. The count on the Save step comes from the dry run (#39). Overlaps **M-8, S-7**.

**41. Who writes rules and categories: the BFF writer can't, and the admin role can't touch categories.** · *Blocks build*
- *v4*: the admin writes, versions, enables and retires rules, and creates and edits categories (`AdminView.tsx:4-9`; `v4-33`).
- *Reality*:
  - **The writer is read-only on policies**: Ontology Service holds Read only on `sprk_policy` and `sprk_policyversion`.
  - **So admin routes must write as the caller.** That is possible: `IDataverseUserClient` is already used by the child-records routes.
  - **The admin role has no rights on categories**: **Spaarke Ontology Administrator holds no privilege at all on `sprk_triagecategory`**. Only
    Core User and Ontology Service can read it (live `roleprivileges` query). The table is organisation-owned.
  - **Nobody holds the admin role** (`security-roles.md` §7.7).
- **Recommend**: admin writes go through the BFF **as the caller** (as **C-11**). Add Create, Write and Read on `sprk_triagecategory` to the admin role
  before slice (c). Assign the role to one real user now.

**42. Nothing gates a screen by Dataverse role.** · *Changes behaviour*
- *v4*: *"Top bar → Admin (the Spaarke Ontology Administrator role)"* (H§8 L297).
- *Reality*:
  - **No role checks in the client**: there is no client-side role check anywhere in `src/client` or `src/solutions` on master (grep for
    `userSettings.roles`, `RetrieveUserPrivileges`).
  - **BFF policies don't know this role**: they cover app roles (`SystemAdmin`, the SPE admin), not Dataverse security roles.
- **Recommend**: a small BFF capability probe (*"can the caller create `sprk_policyversion`?"*, checked as the caller) that the UI uses to show or
  hide Admin. Dataverse privileges stay the real gate. Overlaps **M-14**.

**43. The audit page can't read the audit log.** · *Changes behaviour*
- *v4*: *Access & audit*: *"every admin change, audited"*, listed in the Console (`AdminView.tsx:404-408`).
- *Reality*: auditing is on (spec §8.1), but **no Spaarke role holds an audit-read privilege** (live query found none).
- **Recommend**: build *Change history* from the immutable `sprk_policyversion` rows (`sprk_authoredby`, `createdon`) plus `sprk_policy`'s
  modified-on and modified-by. Leave raw audit to the model-driven app.

**44. Admin metrics: about half have data.** · *Changes behaviour*

| v4 metric | Captured? | Source / gap |
|---|---|---|
| Action rate, noise ("Needs a look") | ✓ once Signals exist | `sprk_signal.sprk_resolutiontype` by `sprk_policyversion`, `sprk_resolvedon` (**H-7** definition) |
| Open items, last fired | ✓ | `sprk_signal` |
| 30-day volume per category | ✓ today | `sprk_communication.sprk_triagecategory` + `sprk_receiveddate`, aggregate query |
| Recall vs the 80% / 50-item floor | ✗ | nothing stores it (**S-11**) |
| Confidence calibration | ✗ | no classifier confidence (#5) |
| Dismissal reasons clustered | ✗ | free-text `sprk_reason` (**R-8**) |
| Nightly run (id, duration, matters, rules) and trigger status | ✗ | no run table; App Insights only |
| Source freshness, *Dormant* | ✗ | no landing columns, no connector (**H-5**) |

- **Recommend**: R1 Health shows the four ✓ rows. The rest wait for **S-11** and the landing contract.

**45. *Lift suppression writes a Decision Record* contradicts *"admin changes are never Decision Records"*.** · *Cosmetic*
- *v4*: `AdminView.tsx:280` vs H§8 L307.
- *Reality*: a record needs a policy version, a matter, an outcome (Authorized / Denied / Dismissed) and a class (Judgement / Routine / Dismissal). A lift fits none of them.
- **Recommend**: Lift is an audited admin change (clear `sprk_suppresseduntil`), not a record. Overlaps **M-12**.

**46. The rule editor offers conditions the evaluator can't run.** · *Changes behaviour*
- *v4*: Condition is built from a word list per entity (*"awaiting reply"*, *"no response recorded"*, *"Business days since sent"*,
  *"Invoiced to date ÷ budget"*; `catalog.ts:26-39`).
- *Reality*: a body is JSON over real columns with GUID values, limited to two verified joins (#12). Nothing maps v4's words to filters.
- **Recommend**: the editor's catalog is generated from the compiler's verified joins and readable tables, so it can only offer what compiles.
  For R1 that is effectively Path B variants (window length, which categories). Overlaps **M-6**.

**47. Where admin lives in the real Console.** · *Changes behaviour (owner decision)*
- *v4*: a full-page *Ontology admin* behind a top-bar button (`v4-22`).
- *Reality*: the Console has no top bar and no full-page mode, only three panes. None of the five tables is in any model-driven app (`security-roles.md` §7.6).
  Task 022's owner decision keeps model-driven app authoring allowed, with checks again at evaluation.
- *Options*: (a) a workspace-widget tab, *Ontology admin*, shown only when the probe (#42) passes; (b) a separate code page launched from the
  Spaarke Platform app; (c) the model-driven app only.
- **Recommend**: (a), plus adding the five tables to *Spaarke Platform* for read-only inspection. Overlaps **M-1, M-2, C-11**.

---

## 7. v4 assumptions verified as TRUE (no discussion needed)

1. **Event status codes** match: Draft = 1, Open = 659490001, Completed = 659490002, Closed = 659490003, Cancelled = 659490004, Transferred = 659490005,
   On Hold = 659490006, Reassigned = 659490007, No Further Action = 2. Event priority runs 100000000–100000003 (live describe).
2. **Lane** Decide / Do · **record class** Judgement / Routine / Dismissal · **outcome** Authorized / Denied / Dismissed. **Resolution types**
   Acted / Dismissed / Condition Cleared / Superseded / Policy Retired: all exist with v4's meaning (live describe). The status-bar mapping is **R-14**.
3. **Rule type** is the closed set Threshold / Switch / Existence (`sprk_ruletype`). It is enforced at validation, though only Existence compiles (#12).
4. **Decision Record 1 → N Signals, foreign key on the Signal** (`sprk_signal.sprk_decisionrecord`). There is no Signal lookup on the record. A Signal has
   both policy and version lookups.
5. **Users can't create a Signal**: Console User lacks `prvCreatesprk_Signal`; only Ontology Service has it.
6. **Records are append-only for every Spaarke role, the writer included**: update and delete return 403 (`security-roles.md` §8–9).
7. **Signal ownership**: the owner is the writer and the owning business unit is the matter's, checked after create (`branch:SignalWriter.cs:136-148,377-402`).
8. **Rank is deterministic and has its columns**: `sprk_rankscore` (int), `sprk_firstdetected` (oldest) and the matter number exist. Nothing uses a model to order.
9. **Suppression expiry** has its column (`sprk_suppresseduntil`). D-11's 30 days is a value; only the re-raise is blocked (#13).
10. **System closures write no record** (FR-16), matching *"Cleared on its own — no person, no record"*.
11. **Path B's live body matches v4's clauses**: a fee or scope classification within 30 days, not dismissed in review, **and** no budget revision within
    30 days (live `sprk_rulebody`).
12. **New policies are inert by default**: `sprk_enabled` defaults No and priority 500 (FR-10; the live row is disabled).
13. **The task complete route exists**, is authorized per record, and completes Draft, Open, On Hold and Reassigned (`master:Api/Events/EventEndpoints.cs:168`).
14. **To Do create and update** go through `/api/v1/child-records` as the caller, with the owner decided on the server (`master:Api/ChildRecordEndpoints.cs:60-73,147-162`).
15. **Outbound email** has a real, authorized route: `POST /api/communications/send`.
16. **`FollowOnGrid`** exists as v4 assumes: config-driven cards, `addDynamicStep` / `removeDynamicStep` with canonical order, and Space/Enter
    toggles (`master:…/WizardFollowOns/FollowOnGrid.tsx:203-219`).
17. **`SprkModal`** has `nav` (‹ N of M ›), `dismiss: 'explicit'` and `maximizable`. **`BrowseModal`** has `onBeforeNavigate`. **`ConfirmModal`** takes custom
    labels (*Keep deciding* / *Discard and close*), a destructive style and alert dismiss (`master:…/SprkModal/presets/ConfirmModal.tsx:44-91`).
18. **The Email Review surface already mounts in the Console** as the workspace widget `communications-reconciliation`, so FR-29's registration
    largely exists.
19. **Writing as the caller is available to the BFF** (`IDataverseUserClient`), so admin and decision writes can be authorized by Dataverse itself.
20. **The dismissal reason has a home**: `sprk_reason` is on the record, and `sprk_resolutionnotes` on the Signal.
