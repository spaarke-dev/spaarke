# Console prototype v4 ↔ Ontology Platform R1 — reconciliation

> **Date**: 2026-10-05 · **Status**: draft for owner decision. Nothing in `spec.md`, `design.md` or any POML has been edited.
> **Baseline**: `HANDOFF.md` @ **`ae1cc9f`** (v4, findings 1-40) in `spaarke-dev/spaarke-prototype`, branch `feature/2026-10-spaarke-console`,
> path `projects/2026-10-spaarke-console/`. The local worktree (`c:\code_files\spaarke-prototype-wt-spaarke-console\`, HEAD `4b8e860`)
> differs from `ae1cc9f` only in pin text. Spec and design line numbers are as of ontology-branch commit `cac41341a`.
> Sibling note: `notes/v4-prototype-vs-solution.md` (what v4 shows that the real solution cannot do).
> **Owner rule**: before any UI task starts, check it against HANDOFF §1 (binding), §3 (data needs) and §4.1 (wizard host).
> Prototype code is never ported. Per-task checklist in §B.6.
> Cited as `H§x` = `HANDOFF.md`, `F-n` = README finding n, `src/…` = prototype source.
> **Owner decisions that bind (2026-10-05)**: (1) v4 is the baseline, refined in UAT here; (2) Ontology admin is in R1 as its own
> phase after 031-034 and the worklist core, in three slices: (a) read-only Rules + On/Off + Retire, (b) rule wizard (needs 031
> dry run, 033 supersede, calls `PolicyVersionValidator.ValidateForSave`), (c) Classification admin; (3) H§7 Email Review is a
> **hand-off**, not R1; (4) the wizard host is **not decided here**. This note only records what v4 assumes and which tasks it touches.

**Status key**: **MATCHES** · **CHANGES-SPEC** (spec/task text must change, the intent holds) · **NEW-SCOPE** (nothing in spec/tasks
covers it) · **CONFLICT** (= CONFLICTS-WITH-SETTLED-DECISION) · **DROPS** (= DROPS-SPEC-ITEM: v4 removes something the spec requires).

---

## A. Delta table

### A.1 Worklist surface

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| W-1 | Two lanes, **Decide always above Do** (H§1.2 L71-73) | FR-34 (spec L373) "Decide always above Do"; BR-5; 064 AC "Decide appears above Do" | MATCHES | — |
| W-2 | Each lane has a heading with its count and its own start button, *Start review* / *Work through these*, which walks that lane's **visible (filtered)** list (H§1.2 L72-73; `WorklistV3.tsx:88-93`) | Nothing | NEW-SCOPE | Into new task 059 (worklist assembly) |
| W-3 | **One card per matter, one line per item**: headline, rule display name, age (Decide) or **due state** (*5d overdue · 3d late · due 2 Oct*) (H§1.2 L74-76) | FR-25 (L312) one row component; 051 step 2 "short headline, matter and policy identity, the sentence, an expandable evidence block…, a why-this-fired disclosure… and an action area" | CHANGES-SPEC | Re-scope 051 to `MatterCard` + `IssueLine` with those three fields only. Still **one** row component (FR-25 holds) |
| W-4 | **"No buttons, rule codes, clause marks or badges in rows"** (H§1.2 L76) | FR-27 (L333-334) "`DocumentRowMenu`… for the row's ⋮ menu"; 052 L35, L40 (C-9 `RowActionMenu`), AC L113, L118 | **DROPS** | Remove the ⋮ menu from the worklist. Keep **C-9** as cleanup (§5.0: fixed or scheduled) but **decouple** it from the worklist row: new task 06x/08x or its own issue. Needs owner sign-off because design §1.3 (L231-240) calls the reuse "BINDING" |
| W-5 | **The whole line opens the wizard** (decided lines too) (H§1.2 L75-76) | FR-26 req 4 (L324) "it offers at least one action that changes something"; 051 AC L136 "no row renders without at least one state-changing action"; ui-test L113-117 "Inspect the action area" | CHANGES-SPEC | Reword req 4: *"the Work Item's decision plan contains ≥ 1 state-changing action"* (a data check at save/evaluation), not a control in the row |
| W-6 | Evidence tiers and *How this was determined* move **out of the row** into the wizard's *What was found* step (H§1.1 L42) | 051 L37, L67-68; AC L132-133 put the evidence block and disclosure **in the row** | CHANGES-SPEC | Evidence and disclosure become kit pieces (`EvidenceLine`, `Disclosure`) used by the wizard step (Z-2), not by 051 |
| W-7 | Count filter cards per lane (H§1.2 L77-79, H§1.3 `CountFilters` L105) | FR-27 (L331-332) `WorkspaceShell/MetricCard` + `MetricCardRow`, not `StatTiles`; 052 L34, L38 "lenses on membership" | MATCHES | Keep 052's MetricCard extension. Add v4 card contents: count, work-type label, a note line (*oldest n days · n past due · next due*), and the "All" card's *n of m done today* progress (`WorklistV3.tsx:69-82`) |
| W-8 | Cards split by **what has to be done** so counts add up: Decide = *Ask outside counsel · Approve or rebudget · Chase a reply*; Do = *Finish or reschedule · Coming due · Chase a response*. Decide work type comes from the rule's recommended action; a **Do rule declares** its work type (F-29, `derive.ts:176-180`) | FR-34 "each lane carries its own count filters" (no partition rule); schema has no work type | CHANGES-SPEC | FR-34 states the partition rule. New column `sprk_policy.sprk_worktype` (S-5) and `workType` in the action catalog (S-8) |
| W-9 | Filter honesty: *Showing n of m · Show all*; *"+1 other open item on this matter, not in this filter"*; **filters not remembered between sessions** (H§1.4 L121-123) | Nothing (052 has only "lenses") | NEW-SCOPE | Add to FR-34 as acceptance criteria. Lands in 052'/059 |
| W-10 | Narrative **summarises** Do volume (*"You also have seven things to do — three past due…"*); hidden while a filter is on; tagged *"Written by the Assistant… It doesn't choose or order the list"* (H§1.2 L80; `WorklistV3.tsx:121-130,152-159`) | FR-34 (L374-375); 062 L36, step 5 | MATCHES | Add "hidden while a filter is on" to 062 |
| W-11 | **One aggregate row at the top of the Do lane**, *"Eight emails await a match confirmation"* → Email Review; **not in either lane's counts**; hidden under a Do filter (H§1.2 L81-82; `WorklistV3.tsx:166-171`) | FR-29 (L343); A-5 (L712) "computed count… not a Signal"; 054 L34-35, AC L107-109 | MATCHES | 054: place it at the top of the Do lane; R1 shows the total only (the *confirm / choose / find* split is email-project data, H§7 E10) |
| W-12 | *Closed today*, *Suppressed* (with until-date) and *Closed earlier* behind Disclosures, both lanes together (H§1.2 L83) | 050 L33 selects only Open/Acknowledged and excludes suppressed; nothing reads closed or suppressed Signals | NEW-SCOPE | Three secondary queries in 050' (closed today, suppressed, closed earlier); render in 059 |
| W-13 | **Rank**: per lane, severity → oldest → matter number, a matter's items kept together; no model orders (H§1.4 L115; `derive.ts:15-24`) | spec §11 Q1 (L720-724) "Which rank function?" — **unresolved**; 050 step 2 "ordered by `sprk_rankscore` then matter" | CHANGES-SPEC | v4 answers §11 Q1. Close it in the spec. The evaluator (031) writes `sprk_rankscore` from the declared formula, so FetchXML can still order by it |
| W-14 | v4's rank has **no `sprk_highpriority` input** | FR-32 (L366) "`sprk_highpriority` becomes a **rank input**"; 062 L34, AC L120 | **DROPS** | Owner choice (C-9). Recommendation: keep it as the second key (severity → high priority → oldest → matter number) |
| W-15 | Do lane holds tasks **and To Dos**: a fourth rule, *Overdue To Do* (F-33; `mockData.ts:136-144`) | FR-30 (L353-361) and 061: **three** rules, no To Do rule. FR-30 note: To Do is "per-user by construction" | NEW-SCOPE | Owner choice (C-7). If yes, it becomes 061 rule 4, with the matterless-To-Do problem (S-4) |
| W-16 | Do lane is **the reader's** work: tasks and To Dos assigned to me, and work assignments **I assigned** (*"Work you assigned, past its response date"*, `mockData.ts:631-633`) | FR-24 "never by a user-authored filter"; FR-14 owns Signals by the matter's BU, so every BU reader would see everyone's overdue tasks | NEW-SCOPE | New FR-51: scope the Do lane by the denormalized assignee/assigner (not a user filter, a lane rule). See C-8 |
| W-17 | Do rules are **Thresholds** on *days past/until a date* (F-34); overdue fires at **1 day** (*"surfaces more than today's briefing does"*) | FR-15/FR-30/061 call them "`Temporal`" policies, which is **not** in the closed set (FR-05 L169: Threshold · Switch · Existence); `TaskOverdueDaysPast = 5` (FR-30 L357) | CHANGES-SPEC | FR-30/061: name the rule type (Threshold, or an Existence `when` filter on the subject's own date). The overdue value is a knob on the rule. Initial value: see C-6 |
| W-18 | **Decision Record tab**, filtered by **record class** (All · Judgements · Routine · Dismissals); every row has **Open ›**, which opens the item (or email) in its wizard, read-only (H§1.2 L84-85) | FR-19 (L279) "Report Card… filter on read"; D-3 (design L601): the MDA does the auditing (*"Decision Record list view and matter subgrid"*). No Console DR surface in any task | NEW-SCOPE | New task 045 (a DataGrid config over `sprk_decisionrecord` plus the class filter). See C-14 |
| W-19 | Worklist is a **card list**, and its actions come from each item's decision plan | FR-24 (L309) "Membership, **columns and actions** come from a `sprk_gridconfiguration` row"; 050 step 3 "the actions available per row" | CHANGES-SPEC | FR-24: the grid configuration supplies **membership and order** (FetchXML), read by the worklist widget, not `<DataGrid>`. Actions come from the version's decision plan (FR-45) |

### A.2 The decision wizard

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| Z-1 | **Every** decision, in both lanes, happens in one wizard: **`WizardShell`** + the *Create New Matter* sidebar + `SprkModal`'s ‹ N of M ›, `wizard` size (H§1.1 L35-38, H§4.1 L200-232) | 053 L33 and step 4 "Use SprkModal presets for the confirmation and dismissal dialogs"; FR-27 "SprkModal + its six presets"; design §1.3 L214 lists `WizardModal` as the preset | CHANGES-SPEC | **Host not decided here (owner decision 4).** v4 assumes `WizardShell` with five changes (H§4.1 L218-225): `nav` + `onBeforeNavigate`, a `'skipped'` step status (missing today: `wizardShellTypes.ts:18` is `'pending'\|'active'\|'completed'`), a `statusBar` slot + read-only footer + stay-open-after-finish, `dismiss="explicit"`, and an `onBeforeClose` seam. Verified: `WizardModal` is imported only by its tests and `SprkModal/index.ts`. **Tasks touched**: 053 (superseded), new 056 (shell changes), 058 (consumer), 102 (rule wizard), 055 (deploy verification) |
| Z-2 | Step **What was found**: the rule sentence, display name, date raised, days open, the Do subject, evidence one line per item, *How this was determined* collapsed, other open items on the matter **in the same lane** (H§1.1 L42; `DecisionWizard.tsx:372-408`) | Content partly in 051 (as row content) | NEW-SCOPE | New task 058 |
| Z-3 | **One step per rule action**, in the rule's order, recommendation first (badged); *What it does* lines; **Next takes it, Skip leaves it out**; Next disabled until required fields are valid; Skip never is (H§1.1 L43, H§1.4 L128) | 053 assumes **one** action per confirmation; `sprk_policyversion.sprk_proposedaction` is a single NVARCHAR(400) (004 notes L64-70) | NEW-SCOPE | New FR-45 (decision plan), FR-47 (wizard behaviour); tasks 036, 058 |
| Z-4 | **`excludes`**: taking an earlier action removes the later step and says so (*Mark complete* removes *Reschedule* and *Reassign*; *Approve variance* removes *Revise budget*) (H§1.1 L48-50; F-19) | Nothing | NEW-SCOPE | FR-46 action catalog (S-8) |
| Z-5 | **Next steps** step: the shared `FollowOnGrid` cards for the work type (*Add To Do · Send Notification Email · Create Event · Assign Work*). Each ticked card adds its own step before Confirm, with an Assistant draft. Created **only when recorded**, **not gated**, linked (*"Created by decision DR-…"*) (H§1.1 L44-45, H§1.4 L131; F-21) | Nothing. `FollowOnGrid` exists (`Spaarke.UI.Components/src/components/WizardFollowOns/FollowOnGrid.tsx`) | NEW-SCOPE | FR-47/FR-49; tasks 044 (creators), 058 (UI). Link storage: C-5 |
| Z-6 | **Confirm**: a ✓/– list of every action and follow-on; the **record class** and why; *Also resolve*; a collapsed record preview; **Record decision** (H§1.1 L46; `DecisionWizard.tsx:307-351`) | 053 steps 1-2: a confirmation dialog per action; AC L114-115 | CHANGES-SPEC | 053 is superseded by 058 + 043 |
| Z-7 | Skip every action → Confirm becomes a **dismissal**: reason required, suppression effect shown, **Dismiss & record** (H§1.1 L46, H§1.4 L130) | FR-26 req 5 (L326) "dismissing writes one too, with a reason"; 053 L34, AC L117 | MATCHES | Keep 053's reason-required AC in 058. *Suppression effect* reads the (policy, matter) count from 034 |
| Z-8 | **Nothing executes before *Record decision***; one commit runs every taken action, creates the follow-ons and writes one record (H§1.1 L60, H§1.4 L139) | FR-36 (L382) and 070 dispatch one Inquiry through the gate; 053 step 1 "Wire the **row action area** to the gate endpoint"; 040 step 1 takes "the action taken" (singular) | CHANGES-SPEC | New FR-48 + task 043: a **decision commit endpoint**. 070 becomes one executor it calls |
| Z-9 | **‹ N of M ›** in the header walks the list the wizard was opened from (a filtered lane, *Closed today*, *Closed earlier*, the Decision Record). **Doesn't wrap**; decided items stay in it; values typed are kept **per item** while the wizard is open (H§1.1 L51-53, H§1.4 L132) | Nothing | NEW-SCOPE | 056 (`nav` prop) + 058 |
| Z-10 | **A decided item opens in the same wizard, read-only**, under a **status bar** on every step (*Decided / Done · Dismissed · Cleared on its own · Superseded · Closed — rule retired*), with record id, class, who and when. It opens on its last step; Back walks the values actually sent (H§1.1 L54-57, H§1.4 L133) | Nothing | NEW-SCOPE | 056 (`statusBar` slot, read-only footer) + 058 + `StatusBar` kit component (057) |
| Z-11 | Read-only *What was found* shows the record's **fact snapshot**, not today's data (H§1.1 L57; F-20) | FR-20 (L279) `sprk_factsnapshot` mandatory on DR and Signal; 040 AC L103 | MATCHES | The snapshot JSON must carry renderable evidence lines (see H-4) |
| Z-12 | Recording **flips the item to the read-only view in place**: *Recorded*, *n open left*, primary **Next open issue / item** (or **Done**); **focus moves to the status bar** (H§1.1 L58-59, H§1.4 L134) | Nothing | NEW-SCOPE | 058 |
| Z-13 | **Explicit dismiss**: Escape and the backdrop never close it; × / Cancel / Close with unrecorded choices on **any** item in the review ask *Discard what you haven't recorded?* (**Keep deciding** left, **Discard and close**), and that confirmation is **nested** in the wizard (H§1.1 L60-63, H§1.4 L135-136; F-25, F-40) | Nothing (`SprkModal` has `dismiss="explicit"`; `WizardShell` closes on Escape, H§4.1 L223) | NEW-SCOPE | 056 (`dismiss`, `onBeforeClose`) + 058 (the unrecorded-check, `ConfirmModal` nested) |
| Z-14 | A multi-action decision passes the **strictest** gate of the actions taken; the record carries the gate tier (F-18; `derive.ts:257-261`; H§3 L178 "gate tier") | 042 maps one gate outcome; DR schema has no gate-tier column | NEW-SCOPE | 043 computes it; schema in S-10 |
| Z-15 | Post-decision state shows as the **status bar** (H§1.3 L102) | FR-27 (L334-335) "`OutcomeCard`… extended with the Signal statuses for the post-action surface"; 052 L36, step 4, AC L114 | **DROPS** | Drop the OutcomeCard extension (F-37 README L46: *"FR-27's OutcomeCard reuse no longer applies"*). §11: build `StatusBar` on Fluent `MessageBar`, not a new primitive. Owner sign-off (design §1.3 is binding) |

### A.3 Decisions, records and lifecycle

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| R-1 | **Every human resolution writes one** record, in the Decide lane, the Do lane and Email Review (H§1.4 L140) | FR-18 (L269); 040 L34 | MATCHES | Email Review half → hand-off (§B.5) |
| R-2 | Record class is **derived, never chosen**: *Dismissal* if nothing taken; *Routine* if only bare completions / reschedules / reassignments / response-date moves of one's own work; *Judgement* otherwise, and **always** when a Next step was added. Confirm shows it (H§1.4 L140; `derive.ts:196-205`) | FR-19 (L278-282); A-2 (L702-705); 040 L36, AC L98-101 | MATCHES | — |
| R-3 | **Reassign** and **Extend response date** are Routine; *Send a reminder* and *Record the response* are Judgement (F-27; `mockData.ts:742-781`) | A-2 lists Routine = "complete, reschedule" and Judgement = "send-email, create-follow-on, close-record". Neither list has reassign or extend; 040's escalation trigger L79 would fire | CHANGES-SPEC | Rule on it now (C-3) and put the full mapping table in A-2 / FR-46 |
| R-4 | A Routine record **displays as "Done"**; the stored `sprk_decisionoutcome` stays *Authorized* (F-28) | Silent | MATCHES | A display mapping only, in 057/058. No schema change |
| R-5 | Outcome *Rejected* → **Denied** (H§0 L24) | Schema `Authorized · Denied · Dismissed` (schema-draft L208); FR-37 note (L390-393) | MATCHES | In R1, Denied is produced only by the gate deny path (042). The v4 wizard has no reject path |
| R-6 | **One review writes exactly one record** listing **every action the rule offered** (taken or skipped, with values, gate, outcome) and the Next steps created. Cancel, × and browsing away write nothing (H§1.4 L139; F-18) | FR-18 "writes a `sprk_decisionrecord`" (one per resolution, no step list); 040 takes a single action; `sprk_action` is a single lookup (001 notes L11) | CHANGES-SPEC | FR-18: add the step list. Schema S-10 (`sprk_steps`, `sprk_followons` JSON). `sprk_action` = the first action taken (`types.ts:299`) |
| R-7 | **Also resolve**: one decision may close several **Decide** issues on the same matter, only those ticked; **Do items never resolve each other** (H§1.1 L46, H§1.4 L141; F-26) | FR-22 (L296) 1 → N with the FK on the Signal; 041 AC L77 | MATCHES | Add the "Decide only, ticked only" constraint to FR-22/043 |
| R-8 | **Dismissal reasons per lane**, closed lists with *Other → detail required*. Decide: *Already approved offline · Not material · Wrong matter — misresolved · Duplicate · Handled outside Spaarke · Other*. Do: *Already done outside Spaarke · Not mine · No longer needed · Duplicate · Other* (`mockData.ts:636-652`; F-26) | 053 L34 requires a reason; `sprk_reason` / `sprk_resolutionnotes` are free text | NEW-SCOPE | Reason catalog in FR-46 (code). Store `code — detail` in `sprk_reason` so clustering works (C-4) |
| R-9 | **3 dismissals per (rule, matter) → not raised for 30 days, then it may fire again**; a rule set *not to count* never suppresses (H§0 L25, H§1.4 L143) | FR-17 (L255-265), D-11; 034 AC L85-90 | MATCHES | — |
| R-10 | **A *Wrong matter — misresolved* dismissal never counts** (H§1.4 L143; F-7) | FR-17 has only the per-**policy** switch `sprk_countstowardsuppression` | CHANGES-SPEC | FR-17 + 034: add a per-**reason** exclusion, flagged in the reason catalog |
| R-11 | After a 1st or 2nd dismissal, the next run must not re-raise at once; after suppression lapses it **may fire again**; after *Reschedule* the rule **raises it again if the new date passes** (`mockData.ts:740`); publishing a version **closes the item as Superseded and raises it again** under the new version (H§8 L302; F-32) | FR-03 (L154-158): `sprk_dedupekey` = `{policycode}\|{type}\|{id}`, a **unique alternate key**. The built writer re-touches a Resolved row and **keeps it Resolved** (`notes/030-signal-writer-progress.md` L279-284: *"re-fire-after-resolution… is the EVALUATOR's decision (task 031)"*). 031 has no constraint on it | **CONFLICT** | **Decide before 031 starts.** A Resolved row holds the only key value, so no new Signal for that subject can ever be created: dismissed, rescheduled and superseded items never come back, and the D-11 expiry and 033's *"raised again"* are unreachable. Proposal: episode-scoped key `{policycode}\|{type}\|{id}\|{episode}` plus a re-raise rule (C-1) |
| R-12 | *Cleared on its own*: no person, no record, never counts (H§1.4 L142) | FR-16 (L249-254); 033 AC L83-87 | MATCHES | — |
| R-13 | Append-only, **no edit or delete affordance anywhere in the UI** (H§1.4 L144) | FR-21 (L290); 041 | MATCHES | Add a UI negative AC to 057/058/045 |
| R-14 | Status-bar states map to closure kinds (H§1.1 L55-56) | `sprk_resolutiontype` = Acted · Dismissed · ConditionCleared · Superseded · PolicyRetired (schema-draft L168) + `sprk_recordclass` | MATCHES | *Decided* = Acted+Judgement · *Done* = Acted+Routine · *Dismissed* · *Cleared on its own* = ConditionCleared · *Superseded* · *Closed — rule retired* = PolicyRetired |
| R-15 | `StatusChip`: one chip shape, 9 states incl. **Done** (H§1.3 L102) | Task 012's `StatusBadge` (4 tones + free label, `StatusBadge.tsx:40`) | MATCHES | Map the 9 states onto `StatusBadge` tones. Do not add a second badge |

### A.4 Honesty and content (H§1.4 "Determinism and honesty")

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| H-1 | Every claim in a sentence or headline maps to a tested clause (`unmappedClaims`) (H§1.4 L116) | NFR-02 (L463); 022's `ValidateForSave` refuses a token outside `TemplateEligibleFields` (`notes/022-progress.md` L113-129) | MATCHES | — |
| H-2 | Claims resting on a classifier are phrased **as classifications** (*"was classified as"*). The display name describes the predicate and nothing more (H§1.4 L116; F-1, F-15) | The seeded `POL-COMMIT-BUDGET` (`notes/004-seed-policy-rows.md` L9, L55-59) has `sprk_name` = **"Budget Variance Policy"** (F-1: *"a variance rule's name on a predicate that computes no variance"*) and template **"A commitment with financial consequence was raised…"** (F-1: *"states clause ①'s witness… as established fact"*) | CHANGES-SPEC | Add the classification-phrasing rule to NFR-02. New task 008: rename the policy and publish **v2** with a corrected template **before 031 writes Signals** (0 Signals exist, so this is free now) |
| H-3 | v4 copy puts the **witness's values** in the sentence/headline: *"Fenwick & Calder's email of 12 Sep was classified as…"*, headline *"…, 12 Sep · no budget revision since"* (F-1, F-14) | **Settled by task 022/030 review rounds** (031 L45-46; `SignalWriter.cs:49-62`): tokens come only from the subject's `when` fields or an `exists` clause's **pinned literal**, and **"never a value picked from one of several matching related rows"** | **CONFLICT** | Keep 022's rule. Witness values (sender, date, *"one other message also matched"*, F-2) go in **evidence lines**, not in the templated sentence. Treat v4's sentence copy as illustrative (H§2: row copy is draft) |
| H-4 | Evidence per item: tier (Fact / Interpretation n% / Missing), text, detail, quote, confidence, **source + as-of**, **the clause it tested, or *"context, not tested"*** (H§1.4 L117, H§3 L173; F-2) | `SignalEvidenceRef(Kind, Ref, Tier, Confidence)` (`SignalWriter.cs:23`). **No task builds per-clause witness queries** (F-2) or populates evidence | CHANGES-SPEC | New FR-50: the JSON contract (+ `clause`, `asOf`, `text`, `quote`, `source`). 031 amended: one witness query per `exists` clause, plus context evidence (e.g. the spend snapshot, *context, not tested*) |
| H-5 | **Missing or stale data shows as Missing**, never as zero and never omitted (H§1.4 L118); Sources: name · mode · last sync · cadence · freshness (H§3 L183) | Spec §2.1 (L86-88) puts the landing contract in scope ("freshness is load-bearing in the UI") but there is **no FR and no task** for it | NEW-SCOPE | R1: render a null fact as *Missing* (cheap, in 057). Source freshness: schedule it with the landing-contract columns (C-17) |
| H-6 | The **headline** is a template under the same §0.3 check (F-14) | `ValidateForSave(ruleType, ruleBody, messageTemplate)`. The headline is not checked, and the writer stores `ShortHeadline` as a literal (`SignalWriter.cs:39-41`) | CHANGES-SPEC | Extend 022's check and the writer's render to `sprk_shortheadline` (small; in 101) |
| H-7 | **Action rate = acted ÷ (acted + dismissed)**, per version in force, 90 days, *too few to judge* below 5 (F-8; `derive.ts:65-74`) | Spec §7 standing metric (L649) "acted ÷ surfaced"; 071 AC L86 | CHANGES-SPEC | Adopt v4's definition in §7 (F-8: "surfaced" punishes still-open and self-clearing items). Do rules still read `sprk_resolutiontype` |

### A.5 Panes and accessibility

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| P-1 | Context pane = an audit-only matter dossier (no inputs, no action buttons, *Open ›* allowed). Do rules appear under *Rules on this matter* only when they have something open (H§1.4 L147, H§4 L197) | FR-31 (L362) "Know items become narrative + Context pane"; 062 step 2 | NEW-SCOPE | Owner choice: R1 does the Know half only (062). File the dossier widget (`ContextWidgetRegistry`) as an issue |
| P-2 | Assistant *why / why not / draft*: tools that read the **evaluation log** and the Decision Record (H§4 L198) | No FR. No evaluation log exists (031 emits only metrics) | NEW-SCOPE | Defer to an issue. R1 keeps Assistant drafting of action fields (070 L35) |
| P-3 | Light and dark, Fluent v9 tokens only; everything keyboard-reachable; Next-steps cards toggle with Space/Enter (H§1.4 L148) | FR-28, ADR-021 ACs in 050-054 | MATCHES | — |
| P-4 | The confirmation is rendered **inside** the wizard's tree; after *Keep deciding* the wizard is still reachable by keyboard and screen reader (H§1.4 L136; F-40) | Nothing | NEW-SCOPE | AC on 056/058. F-40 suggests auditing every `SprkModal` + `ConfirmModal` pair; file that sweep as an issue |

### A.6 H§3 data needs vs the BUILT schema (`notes/schema-draft.md`, `notes/001-schema-verification.md`, current-task "What exists now")

| # | v4 needs | Built | Status | Proposed resolution |
|---|---|---|---|---|
| S-1 | `sprk_signal`: polymorphic subject (matter · inquiry · task · To Do · WA), matter lookup, lane, policy + version, severity, raised/evaluated, status/resolution, closed by/at, resolving decision | All present: `sprk_regarding*` ×9 incl. `regardingevent/todo/workassignment`, `sprk_matter`, `sprk_lane`, `sprk_policy(version)`, `sprk_severity`, `sprk_firstdetected`/`lastevaluated`, `sprk_signalstatus`/`resolutiontype`, `sprk_resolvedby`/`resolvedon`, `sprk_decisionrecord` (schema-draft L130-181) | MATCHES | — |
| S-2 | Do items: the subject's **due / response date** and **assignee** (and assigner, for WA), denormalized for the row (H§3 L171) | Absent | NEW-SCOPE | Add `sprk_duedate` (DateOnly), `sprk_assignee` (lookup systemuser), `sprk_assignedby` (lookup). The writer must **refresh** them on re-evaluation: today it updates only `{sprk_lastevaluated}` (`030-signal-writer-progress.md` L277-278) |
| S-3 | **Severity** per rule (`types.ts:136`) | `sprk_signal.sprk_severity` exists; **nothing on policy or version supplies it** | NEW-SCOPE | `sprk_policy.sprk_severity` (C-2) |
| S-4 | A To Do **with no matter** (F-33) | `sprk_signal.sprk_matter` is required and "ALWAYS populated" (D-3, schema-draft L136); 030 AC L110 refuses a null matter | **CONFLICT** | Only arises if the To Do rule is adopted (W-15). Recommendation: exclude matterless To Dos in R1 (C-7) |
| S-5 | `sprk_policy`: **short label**, **work type** (Do), **status + dormant reason**, retired reason (H§3 L174) | `sprk_name`, `sprk_policycode`, `sprk_enabled`, `sprk_priority`, `sprk_tenant`, `sprk_matter`, `sprk_lane`, `sprk_subjecttype`, `sprk_currentversion`, `sprk_countstowardsuppression` (schema-draft L232-243). No short label, work type or status | NEW-SCOPE | Add `sprk_shortname`, `sprk_worktype` (choice), `sprk_retiredreason`. Retired = `statecode` Inactive / `statuscode` *Retired*. *Dormant* (unmet bindings) has no meaning in R1 (no connector): drop it |
| S-6 | `sprk_policyversion`: the **decision plan**, i.e. the ordered actions (first = recommendation) and the Next-steps cards (F-30) | `sprk_proposedaction` NVARCHAR(400), a single slug (004 notes L64-70) | NEW-SCOPE | `sprk_decisionplan` (JSON) on the **version**, so a plan change is a new version and old decisions stay explicable |
| S-7 | Version **in force from / to**; publishing closes the old version's window (H§3 L175; `store.tsx:785-786` writes `to` on the old version) | `sprk_inforceto` "Null = currently in force" (schema-draft L261), **but no role holds `prvWritesprk_PolicyVersion`** (`security-roles.md` L290-293, L357, L375). FR-21 "refused… for `sprk_policyversion` after create" | **CONFLICT** | Never write `sprk_inforceto`. *In force* = `sprk_policy.sprk_currentversion`; the end of a window = the successor's `sprk_inforcefrom` (C-12). Correct schema-draft L261 |
| S-8 | **Action definitions**: label · work type · risk tier + gate · parameters · effect lines · `excludes` · record class when taken (H§3 L176) | `sprk_decisionrecord.sprk_action` → `sprk_analysisaction` (001 notes L11); that table has none of these. The Action **Engine** is out (spec §2.2) | NEW-SCOPE | R1: a **closed C# catalog** in the BFF, exposed read-only (FR-46, task 036). A-2 already says the class mapping is "code, not configuration". R2 moves it to data (C-4) |
| S-9 | **Next-steps sets** per work type (H§3 L177) | Absent | NEW-SCOPE | In the same catalog (036) |
| S-10 | `sprk_decisionrecord`: **steps** (taken/skipped, gate, outcome, values) · **follow-ons** · **gate tier** · decider **role** · *because* (H§3 L178) | Present: outcome, class, proposed, policyversion, confirmedby, reason, nullable action, factsnapshot, gatesessionid (schema-draft L200-216, 001 L11-12). Absent: steps, follow-ons, gate tier, role, because | NEW-SCOPE | Add `sprk_steps` (JSON), `sprk_followons` (JSON), `sprk_gatetier` (text). Put *because* and the role in the fact snapshot. Communication lookup and nullable matter: hand-off only (§B.5) |
| S-11 | `sprk_triagecategory`: **measured recall** + labelled-set size (H§3 L180) | name, enabled, guidance exist (004 notes L95-101). No recall | NEW-SCOPE | 074 writes `sprk_measuredrecall`, `sprk_labelledsetsize`, `sprk_recallmeasuredon` (a cross-domain table: needs `/conflict-check`) — or admin reads 074's committed fixture (C-16) |
| S-12 | **Suppression** per (rule, matter): dismissals + reasons + until (H§3 L181) | Derivable from Dismissal-class records grouped by (policy, matter) + `sprk_signal.sprk_suppresseduntil` (034 L33-35) | MATCHES | Expose 034's counter read-only (Confirm's *suppression effect*, admin) |
| S-13 | Admin audit: who · when · what · which rule (H§3 L182) | Auditing on all five tables + org (spec §8.1 L664) | MATCHES | — |

### A.7 H§8 Ontology admin vs D-3 and FR-05..FR-17, FR-38..FR-40

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| M-1 | Authoring lives **in the Console** (Admin → Ontology admin); the MDA keeps raw-row inspection (H§8 L309-312; F-31) | D-3 (design L601) "Console acts, the MDA authors/administers/audits"; decision 19 | **CONFLICT** | **Resolved by owner decision 2 (2026-10-05).** Record it as **D-13 amending D-3** in design §8.0c and spec §9 |
| M-2 | The MDA keeps **inspection** only | Owner decision 2026-10-03 (`notes/022-progress.md` L100-106; TASK-INDEX 022 note): *"Admins can author… directly in the Spaarke Platform app, bypassing the BFF… (app authoring stays)"*, hence validate at evaluation | **CONFLICT** | Is direct MDA authoring still permitted? Either way 022's evaluation-time gate stays (C-11) |
| M-3 | **Rules** list (*All · Decide · Do · Needs a look*) + table + a rule's page (condition in words and formal, wording, the plan, scope & noise, behaviour, versions, change history), read-only (H§8 L301) | No FR, no task | NEW-SCOPE | Slice (a), task 100 |
| M-4 | **On/Off**: *Off* stops raising; open items close at the next run (`store.tsx:776-779`) | FR-10 default Off; FR-16 "disabled or deleted → `PolicyRetired`"; 033 AC L85 | MATCHES | UI in 100 |
| M-5 | **Retire…** with a required reason: open items close **at once** as *rule retired*, no record; admin-audited (H§8 L301; `store.tsx:781-790`) | FR-16 treats retire = disable/delete, closed at the next pass; no retired state and no reason | CHANGES-SPEC | FR-16: separate *Retire* (statuscode Retired + `sprk_retiredreason`, immediate closure via 033's PolicyRetired path) from *Off* |
| M-6 | **Rule wizard** (same shell): Basics → Condition (closed set; invalid body can't pass) → Wording (only fields the condition reads) → Actions (plan, exclusions, Next steps, dismiss reasons, live sidebar preview) → Scope & noise → Test → Save; **a new rule saves Off** (H§8 L302) | FR-05, FR-08 (022 `ValidateForSave`), NFR-02, FR-10 cover the **rules**; no UI and no BFF write path ("No BFF write path for `sprk_policyversion` exists today", 022-progress L105) | NEW-SCOPE | Slice (b): tasks 101 (BFF) + 102 (UI). Wording uses 022's `TemplateEligibleFields`, **not** the prototype's `readableTokens` (`derive.ts:371-374`, which also allows `matter.name`/`number` and `rule.threshold`) |
| M-7 | **Test** = a dry run over current data; **Save needs a test run**; any edit invalidates the test (`RuleWizard.tsx:9,105`) | No FR. 031 builds a writing job only | NEW-SCOPE | FR-53; 031 amended so the evaluation core is side-effect-free and reusable read-only |
| M-8 | Publishing a version **supersedes** its open items, **re-raises** those that still hold, and the Save step says how many (H§8 L302) | FR-16 Superseded; 033 step 2, AC L84 | MATCHES | Blocked by R-11 (the re-raise needs a new key) |
| M-9 | **Quiet window** per rule after each dismissal, labelled unresolved (F-32; `RuleWizard.tsx:371`) | Nothing | NEW-SCOPE | Part of C-1. If adopted, a knob in `sprk_rulebody` (A-1, decision 14) or a policy column |
| M-10 | **Classification**: categories with guidance, On/Off, which rules read each, 30-day volume, **recall vs the 80% / 50-item floor**; edit in a form; rename blocked while a rule reads it; a new category saves Off (H§8 L303) | FR-38 (072), FR-40 / D-10 (074), criterion 8. No UI | NEW-SCOPE | Slice (c), task 103. Rules reference categories by **GUID** (004 notes L31), so a rename does not break evaluation; it does change the decoding `enum` name. Keep v4's block |
| M-11 | **Actions** catalog page, read-only (H§8 L304) | Out (Action Engine, R2) | NEW-SCOPE | Not in slices a-c. Issue (C-16) |
| M-12 | **Suppression** page; **Lift** writes a Decision Record (H§8 L305) | Nothing | NEW-SCOPE | Not in slices a-c. Issue (C-16) |
| M-13 | **Health**: nightly run, triggers (*incl. the missing on-filing trigger*), recall, action rate per rule, confidence calibration, dismissal clusters, freshness (H§8 L306) | 031 L43 metrics + diagnostic health check; criterion 5 | NEW-SCOPE | Not in slices a-c. Issue. App Insights covers operations in R1 |
| M-14 | **Access & audit**: the three roles, every admin change audited (H§8 L307) | security-roles.md; auditing on | NEW-SCOPE | Not in slices a-c. Issue |

### A.8 Cross-cutting

| # | v4 element | Spec / tasks today | Status | Proposed resolution |
|---|---|---|---|---|
| X-1 | The contract is **HANDOFF v4, pinned, findings 1-40** (README L37-47) | Fixed in `cac41341a`: spec header L15-22 and §8.2 L675-677, design header L15-21 and §11 L886, current-task L17/L259 now cite `HANDOFF.md` @ `ae1cc9f` (v4, findings 1–40) | MATCHES | — (was stale v2.1 citation; corrected by the main session) |
| X-2 | The wizard replaces *outcome cards and a gate host* (README L45) | FR-25 *Why* (L316-318) and criterion 7 note (design L568-573) | CHANGES-SPEC | Reword both to "the decision wizard" |
| X-3 | Criterion 7 *"zero new UI code"* does not hold (H§4.1 L231-232) | Already amended in design rev 4 (L568-573) | MATCHES | — |
| X-4 | **D-12's triggers miss filing**: an email classified before it has a matter raises nothing when filed, until the nightly run (F-39, H§7 E11) | FR-13 (L224-234) has two triggers; 032 | CHANGES-SPEC | The trigger is **R1's** (the evaluator is R1's): add (c) "association confirmed" to FR-13/032. The enqueue call site is in the email project's filing path (C-15) |
| X-5 | Every rule has a decision plan with ≥ 1 action | 063 (Know promotion, *new matter with no budget*) defines **no action**, so it fails FR-26 req 4 | CHANGES-SPEC | 063: declare a plan (e.g. *Create budget* (new executor) or *Assign Work*). Owner picks |
| X-6 | `POL-COMMIT-BUDGET`'s plan = **Send budget inquiry** + **Revise budget** (`mockData.ts:655-682`) | Only the Inquiry is built (070). Nothing executes *Revise budget* (create `sprk_budgetrevision`) | NEW-SCOPE | Executor in 044. Note: a revision recorded through the decision closes the Signal as **Acted**; 032's budget-revision trigger must not close it again as ConditionCleared |
| X-7 | Do-lane actions: Mark complete · Reschedule · Reassign · Send a reminder · Extend response date · Record the response (`mockData.ts:724-781`) | FR-18 names complete/reschedule/reassign/send email/close record as resolutions, but **no task executes any of them** | NEW-SCOPE | Task 044 (reuse `TaskActionCore`, the To Do and work-assignment services) |

### A.9 Every H§1.4 acceptance rule → spec

| H§1.4 rule (line) | Maps to | Coverage |
|---|---|---|
| Membership/order from rule evaluation; per lane severity → oldest → matter number; Decide above Do; no AI top action (L115) | FR-24, FR-26.2, FR-32, FR-34; §11 Q1 | covered, **changed** (W-13, W-14) |
| Every claim maps to a tested clause; classifier claims phrased as classifications (L116) | NFR-02, 022 | covered, **changed** (H-2, H-3) |
| Untested evidence labelled *context, not tested*; no implied comparison (L117) | — | **new** → FR-50 |
| Missing/stale shows as **Missing** (L118) | §2.1 prose only | **new** → H-5 |
| Each lane's cards describe its full list; *Showing n of m · Show all* (L121) | FR-34 | covered, **changed** (W-9) |
| *+1 other open item… not in this filter* (L122) | — | **new** |
| Filters not remembered (L123) | — | **new** |
| Email aggregate not counted (L124) | FR-29, A-5 | covered |
| Steps come from data; the UI does not switch on rule/lane/shape (L127) | FR-25 (spirit) | **new** → FR-45/46/47 |
| Each action its own step, skippable; invalid → can't take, can skip (L128) | — | **new** |
| Excluding actions never both run (L129) | — | **new** |
| Skip all → dismissal, reason required (L130) | FR-26.5 | covered |
| Next steps created only on record, linked, not gated (L131) | — | **new** |
| ‹ N of M › browses the source list, no wrap, nothing lost (L132) | — | **new** |
| Decided → same wizard read-only, status bar, fact snapshot (L133) | FR-20 (snapshot only) | **new** |
| After recording: decided in place, focus to status bar (L134) | — | **new** |
| Escape/backdrop never close; discard check (L135) | — | **new** |
| Confirmation inside the wizard tree (L136) | — | **new** |
| One review → exactly one record listing every offered action + Next steps; Cancel writes nothing (L139) | FR-18 | covered, **changed** (R-6) |
| Every human resolution writes one; class derived, shown on Confirm (L140) | FR-18, FR-19, A-2 | covered (+R-3 gap) |
| Also resolve only ticked Decide issues; Do never (L141) | FR-22 | covered (+ constraint) |
| Cleared itself: no person, no record, no count (L142) | FR-16, FR-17 | covered |
| 3 dismissals → 30 days; not-counting rule; misresolved never counts (L143) | FR-17, D-11 | covered, **changed** (R-10) |
| Append-only, no edit/delete affordance (L144) | FR-21 | covered |
| Context pane audit-only; Do rules shown only when open (L147) | FR-31 (partial) | covered, **changed** (P-1) |
| Light/dark tokens; keyboard; cards toggle on Space/Enter (L148) | FR-28 / ADR-021 | covered |

**Tally (26 rules)**: 7 covered as-is · 6 covered with a change · 13 new.

---

## B. Proposed edits

### B.1 Spec edits (exact text)

| Where | Change |
|---|---|
| FR-03 | Append: *"The key carries an **episode** (`…\|{episode}`) incremented when a subject re-raises after its Signal was resolved (dismissed, acted, superseded or cleared). Re-raise is allowed only when the subject is not suppressed and the policy's re-raise rule holds (FR-17a)."* (pending C-1) |
| FR-13 | Add **(c)** *"association confirmed: a communication filed to a matter enqueues the same evaluator for that matter, so a classified email that is filed later raises its Work Item without waiting for the nightly pass"* (pending C-15) |
| FR-16 | Split: *"**Off** (`sprk_enabled = No`): open Signals close as `PolicyRetired` at the next pass. **Retire** (admin, reason required; `statuscode` Retired + `sprk_retiredreason`): open Signals close as `PolicyRetired` immediately. Neither writes a Decision Record; both are audited. **Superseded** closes the old Signal and, where the new version still holds, raises a new episode citing the new version."* |
| FR-17 | Append: *"A dismissal whose **reason** is flagged non-counting (`Wrong matter — misresolved`) is recorded but never increments the counter."* New **FR-17a** (pending C-1): *"After a 1st or 2nd dismissal a subject is not re-raised for the policy's quiet window (declared in the rule body, decision 14)."* |
| FR-18 | Append: *"**One review writes exactly one record**, listing every action the rule offered (taken or skipped, with the values used, gate and outcome) and every Next step created. Cancel, close and browsing away write nothing."* |
| FR-19 / A-2 | Replace A-2's lists with one table: Routine = *Mark complete, Reschedule, Reassign, Extend response date* of one's own work; Judgement = any Decide-lane action, *Send a reminder*, *Record the response*, any Next step; Dismissal = nothing taken. *"Routine displays as **Done**; `sprk_decisionoutcome` stays Authorized."* (pending C-3) |
| FR-22 | Append: *"Only **Decide** Signals on the same matter, and only those the user ticks under *Also resolve*. A Do item never resolves another."* |
| FR-24 | → *"Membership **and order** come from a `sprk_gridconfiguration` FetchXML row, read by the worklist widget (a card list, not `<DataGrid>`). Actions come from the policy version's decision plan (FR-45), never from the grid configuration."* |
| FR-25 *Why* | "outcome cards and a gate host" → "the decision wizard (FR-47)" |
| FR-26 | Req 3: *"…carried on the Work Item and shown in the wizard's *What was found*"*. Req 4: *"the Work Item's decision plan contains at least one action that changes something"*. Req 5: *"recording the wizard writes the Decision Record and closes the Signal; a dismissal writes one too, with a reason"* |
| FR-27 | Delete the `DocumentRowMenu` and `OutcomeCard` clauses (W-4, Z-15). Add: *"`FollowOnGrid` (as-is) for Next steps · `ConfirmModal` nested in the wizard for the discard check · the wizard host per the owner's canonical-modal decision (v4 assumes `WizardShell` + H§4.1)."* Keep `MetricCard`/`MetricCardRow` |
| FR-29 | Append: *"placed at the top of the Do lane; hidden while a Do filter is on"* |
| FR-30 | *"Temporal"* → *"Threshold rules on days past (or until) a date"*. Add the work type each rule declares, the denormalized due date / assignee / assigner (FR-51), and the To Do decision (C-7) |
| FR-32 / §11 Q1 | Close Q1: *"Rank per lane = severity → [high priority, pending C-9] → oldest first-detected → matter number. The evaluator writes `sprk_rankscore`; a matter's items stay together at its best-ranked item's position."* |
| FR-34 | Append the H§1.4 filter rules (L121-124) as acceptance criteria |
| NFR-02 | Append: *"Claims resting on a classification are phrased as classifications (*was classified as*). The headline template is held to the same check as the message template. Witness values (sender, date) appear in evidence, not in templated prose (task 022 rule)."* |
| §7 standing metric | → *"acted ÷ (acted + dismissed), per version in force, 90 days, 'too few to judge' below 5 decisions"* |
| §9 | Add **D-13**: *"Rule authoring, versioning, enable/retire and classification categories are administered in the Console (Ontology admin, slices a-c) over BFF endpoints that call `ValidateForSave`. The MDA keeps raw-row inspection. Amends D-3."* (+ the C-11 answer) |
| §2.2 | Add: *"Email Review redesign (H§7 E1-E13) — the email-communication-intelligence project's. Actions, Suppression, Health and Access admin pages — scheduled as an issue"* |
| **New FRs** | **FR-45** decision plan on `sprk_policyversion` (`sprk_decisionplan` JSON: ordered action codes, first = recommendation, + the Next-steps set). **FR-46** closed action catalog in the BFF (label, work type, risk/gate, params, effects, `excludes`, record class; dismiss-reason catalogs per lane with a counts flag; Next-steps sets per work type), read-only endpoint. **FR-47** decision-wizard behaviour = H§1.1 + H§1.4 "The wizard" (L127-136). **FR-48** decision commit endpoint: validates, runs taken actions in plan order through the gate, creates follow-ons, writes one record (strictest gate), closes the resolved Signals; Cancel writes nothing; idempotent per review. **FR-49** executors: Revise budget, the six Do actions, four Next-step creators. **FR-50** evidence JSON contract + per-clause witnesses. **FR-51** Do-lane denormalized fields + reader scope. **FR-52/53/54** admin slices a/b/c (53 includes the dry run and *"no save without a test run"*). **FR-55** Decision Record tab (pending C-14) |

**`design.md`** (citations already fixed in `cac41341a`): §1.3 table L214: the modal row names the owner's pending host decision · L236-240: strike the `DocumentRowMenu` and `OutcomeCard` rows · §8.0c add D-13 · decision 22 → v4. **`notes/schema-draft.md`** L261: `sprk_inforceto` is never written (S-7).

### B.2 POML changes

| Task | Change |
|---|---|
| **031** (next to run) | Add constraints + ACs: (a) a side-effect-free evaluation core (`Evaluate(version, scope)` → hits + witnesses), wrapped by the writing job, and reusable by the dry run; (b) the re-raise/episode rule (C-1) **before the first Signal is written**; (c) `sprk_evidencerefs` per FR-50 with one witness per `exists` clause and context evidence; (d) writes `sprk_rankscore` (FR-32), severity (S-3) and the Do denormalized fields (S-2), refreshed on re-evaluation |
| 032 | Add trigger (c), association confirmed (if C-15 = yes). Constraint: a decision-recorded budget revision has already closed the Signal as Acted; the trigger must not re-close it |
| 033 | Superseded → new episode under the new version (R-11). Split Retire (immediate, reason) from Off (next pass) |
| 034 | Per-reason exclusion; quiet window (if C-1); a read endpoint for the (policy, matter) count (Confirm + admin) |
| 040 | One record per review: `sprk_steps`, `sprk_followons`, `sprk_gatetier`; `sprk_action` = the first action taken; `sprk_proposedaction` = the labels joined; class from the FR-46 catalog. Remove "the action taken" (singular) |
| 041 | Add a UI negative AC: no edit/delete affordance in 045/057/058 |
| 050 | Membership + order only (FR-24 rewrite); per-lane queries; Do-lane reader scope; three secondary queries (closed today, suppressed, closed earlier); remove step 3's "actions available per row" |
| 051 | Retitle *"The one row: MatterCard + IssueLine"*. Content per W-3. Remove the action area, evidence block and disclosure (W-5, W-6). Replace AC L136 with *"every IssueLine opens the wizard; no button, menu, code or badge in a row"*. **Can build against fixtures now** |
| 052 | Keep MetricCard/MetricCardRow (W-7, W-8, W-9). **Delete** the DocumentRowMenu and OutcomeCard extensions and their ACs (L113-114, L118). Move **C-9** to its own task/issue |
| 053 | **Superseded** by 058 (UI) + 043 (commit). Carry its reason-required and "no client-side carve-out" ACs into 058 |
| 054 | Place the aggregate at the top of the Do lane; hidden under a Do filter; total only |
| 055 | Deps → 059, 043. Add verification of one full wizard review (2 actions + 1 Next step) against real Dataverse |
| 061 | Rule type named (W-17); work type per rule; due/assignee written; initial overdue value (C-6); To Do rule (C-7) |
| 062 | Rank per FR-32 rewrite; narrative hidden under a filter; highpriority per C-9 |
| 063 | Declare a decision plan with ≥ 1 action (X-5) |
| 070 | Deps 001, **036**; the Inquiry is an executor called by 043, not wired from a row (L67 step 3) |
| 072 / 074 | 074 also persists recall for admin (S-11 / C-16). 072 unchanged |

### B.3 New tasks

| # | Title | Deps | Size | Tier / effort | Rigor |
|---|---|---|---|---|---|
| **007** | Schema: v4 data needs (S-2, S-3, S-5, S-6, S-10; FR-03 key change if C-1) via the Web API recipe | 001 | S (3-4 h) | sonnet/high | FULL |
| **008** | Re-seed `POL-COMMIT-BUDGET`: rename (F-15), publish **v2** with a classification-phrased template + decision plan; retire v1 cleanly while 0 Signals exist | 007, 022 | XS (1-2 h) | sonnet/medium | STANDARD |
| **036** | BFF action catalog + decision-plan read endpoint (FR-45/46): Decide + Do actions, `excludes`, record class, dismiss reasons per lane, Next-steps sets | 007 | M (4-6 h) | sonnet/high | FULL |
| **043** | Decision commit endpoint (FR-48): multi-action, follow-ons, Also-resolve, strictest gate, one record, partial-failure semantics, idempotency | 040, 036, 044, 070 | L (1-1.5 d) | **opus**/high | FULL. Escalation trigger: an external action succeeds and a later one fails |
| **044** | Executors (FR-49): Revise budget, the six Do actions, four Next-step creators (reuse `TaskActionCore`, the To Do / event / WA services) | 036, **097** (event *complete* status code + To Do reassign lookup target, the write paths *Mark complete* and *Reassign* reuse) | M-L (6-8 h) | sonnet/high | FULL |
| **045** | Decision Record tab (FR-55): DataGrid config over `sprk_decisionrecord`, class filter, **Open ›** → read-only wizard (pending C-14) | 058 | S (2-3 h) | sonnet/medium | STANDARD |
| **056** | Wizard host changes per H§4.1 (nav, `skipped`, status bar + read-only footer + stay open, explicit dismiss, `onBeforeClose`/nested confirm), **gated on the owner's canonical-modal decision**; regression across its 6 shipped consumers | owner decision | M (1 d) | **opus**/high | FULL |
| **057** | Console kit in `@spaarke/ui-components`: `EvidenceLine`, `StatusBar` (on `MessageBar`), `RecordRow`, `AggregateCard`, `ObjectLink`, `Disclosure` (reuse first; §11 per item); fixtures from the built schema; Mode-2 harness (`/prototype-harness-setup`) | 012 | M (6-8 h) | sonnet/high | FULL |
| **058** | DecisionWizard consumer (FR-47): plan → steps, skip, excludes, Next steps, Confirm + class, Also resolve, dismissal + suppression effect, per-item drafts across browse, discard check, read-only view + fact snapshot, focus | 056, 057, 036 (contract); 043 (live) | L (1.5-2 d) | sonnet/**xhigh** | FULL |
| **059** | Worklist widget assembly: two lanes, per-lane cards, narrative slot, aggregate row, disclosures, browse sets; `WorkspaceWidgetRegistry` | 050, 051, 052, 054, 058 | M (4-6 h) | sonnet/high | FULL |
| **100** | Admin (a): Rules list + rule page read-only, On/Off, Retire (FR-52) + BFF read/enable/retire endpoints | 033, 057, 007 | M (6-8 h) | sonnet/high | FULL |
| **101** | Admin BFF (b): dry run (031's read-only core), save version (`ValidateForSave` incl. headline H-6), new rule saves Off, publish → supersede count; write identity per C-11 | 031, 033, 022, 036 | M (1 d) | **opus**/high | FULL |
| **102** | Admin (b): Rule wizard UI (Basics → Condition → Wording → Actions → Scope & noise → Test → Save) | 056, 101 | L (1.5 d) | sonnet/xhigh | FULL |
| **103** | Admin (c): Classification admin (FR-54) | 072, 074, 057 | M (4-6 h) | sonnet/high | FULL |
| **105** | v4 UAT rounds: owner walkthrough against `walk-v4.cjs`'s 17 steps on dev, refinements back into HANDOFF | 055, 064, 102 | M (recurring) | main session | STANDARD |
| issue | Admin Actions / Suppression + Lift / Health / Access pages (M-11..M-14); Context dossier (P-1); Assistant why/why-not (P-2); source freshness (H-5); `SprkModal`+`ConfirmModal` a11y sweep (P-4) | — | — | — | file per §5.0 |

### B.4 Revised dependency sketch

```
CAN START NOW — built schema + fixtures, no real Signals            MUST WAIT FOR REAL SIGNALS (031-034)
────────────────────────────────────────────────────────────        ─────────────────────────────────────
007 schema v4 ─┬─> 008 re-seed v2 ────────────────────────────────> 031* (amended: read-only core, episodes,
               │                                                         witnesses, rank, Do fields)
               ├─> 036 action catalog + plan API (static)                 ├─> 032* (+filing trigger) ─┐
               │      └─> 044 executors (act on event/todo/WA/budget)     ├─> 033* (supersede/retire) ├─> 035 deploy BFF
               └─> 057 kit + fixtures + Mode-2 harness                    └─> 034* (reasons, quiet)  ─┘
051' MatterCard+IssueLine (fixtures)                                 040* ─> 041, 042
052' CountFilters on MetricCardRow (fixtures)                        070* ─┐
056 wizard host  [blocked: owner modal decision only]                043 commit <- 040*, 036, 044, 070*
058 DecisionWizard (fixtures + mocked 036/043 contract)              050' membership <- 034* ─> 059 assembly ─> 055 deploy UI
   <- 056, 057, 036                                                  061* Do lane <- 060, 034*, 007 ─> 062*, 063* ─> 064
054 aggregate + registration (count is computed, not a Signal)       045 DR tab <- 058 + real records
                                                                     ── Phase 10 admin (after 031-034 + worklist core) ──
                                                                     100 (a) <- 033*, 057 · 101 BFF <- 031*, 033*, 022
                                                                     102 (b) <- 056, 101 · 103 (c) <- 072, 074, 057
                                                                     105 UAT <- 055, 064, 102
```

**Before 031 starts**, the owner answers C-1 (re-raise/key) and C-12 (in-force window), and 007 + 008 land. Otherwise 031 writes Signals
under a key and seed that v4 needs changed, and fixing them costs a data migration.

### B.5 Hand-off to email-communication-intelligence (H§7 E1-E13). Not R1 tasks

File **one GitHub issue** against the owning project (§5.0: scheduled, not listed) carrying E1-E13, with these R1 touchpoints called out:

| E# | Suggestion (H§7) | R1 touchpoint |
|---|---|---|
| E1, E2, E9, E12 | Steps sidebar; nothing writes until Confirm; read-only reviewed email + status bar; discard check | Reuses the wizard host and the 056 changes. Sequence after 056 |
| E3 | One Decision Record per review (Judgement/Dismissal); `sprk_emailreviewlog` becomes a producer | **R1 schema**: needs `sprk_decisionrecord.sprk_communication` (lookup) and a **nullable or "no matter"** `sprk_matter` (required today), F-37. R1 owns the table: agree before either side builds |
| E4 | *Not matter work* + reason, never counts toward suppression | Uses R1's FR-17 switch / reason flag (R-10) |
| E5, E7, E10, E13 | Why-it-matched; interpretation + confidence; count cards; affinity on file | None (E10 count split could feed R1's aggregate row detail later) |
| E6 | *Reply to sender* as a Next step | Reuses FR-49's Next-step creators |
| E8 | Gated fields aren't field updates (*Budget* goes through *Revise budget*) | **Path B correctness**: a Fields-tab budget edit writes no `sprk_budgetrevision`, so R1's rule keeps firing (F-38). Reuses 044's Revise-budget executor |
| E11 | Filing wakes the evaluator | **R1-owned trigger** (X-4 / C-15). The email project only enqueues |
| F-37 | Is "awaiting a match" a Signal? | R1 keeps **A-5** (computed count, not a Signal). Decision 28b's Switch-rule framing is the email project's to settle |

### B.6 Pre-start check for every UI task (owner rule)

Before `task-execute` starts any task below, confirm it against these HANDOFF @ `ae1cc9f` sections and record the result in
the task's notes. Where v4 asks for something the real solution can't do, follow `notes/v4-prototype-vs-solution.md` and
**raise it with the owner. The implementer does not decide it.** Prototype source is never ported (H§2 L156-163).

| Task | H§1 binding behaviour | H§3 data needs | H§4.1 wizard host |
|---|---|---|---|
| 051' MatterCard + IssueLine | §1.2 L74-76 (row content, no buttons), §1.3 `MatterCard`/`IssueLine` | S-1, S-2 (due/assignee) | — |
| 052' CountFilters | §1.2 L77-79, §1.4 L121-124 | `sprk_worktype` (S-5), action `workType` (S-8) | — |
| 054 aggregate | §1.2 L81-82, §1.4 L124 | A-5 computed count | — |
| 056 wizard host | §1.1 L51-63, §1.4 L132-136 | — | **all of §4.1** (pending the owner's canonical-modal decision) |
| 057 kit | §1.3 L93-110 (one shape per verb), §1.4 L117-118, L144 | evidence contract (H-4), Missing (H-5) | — |
| 058 DecisionWizard | §1.1 (all), §1.4 L127-144 | decision plan (S-6), catalog (S-8, S-9), DR shape (S-10) | §4.1 L218-226 |
| 059 worklist assembly | §1.2 (all), §1.4 L115, L121-124 | 050' queries, rank (W-13) | browse sets (§1.1 L51-53) |
| 045 DR tab | §1.2 L84-85, §1.4 L144 | `sprk_recordclass`, S-10 | read-only open (§1.1 L54-57) |
| 100 / 102 / 103 admin | §1.3 `RuleWizard` row L97 (and H§8) | S-5, S-6, S-7, S-11 | same host as 058 |
| 055 / 064 deploy + cutover | §1.4 (full list as UAT script) | real Dataverse, not fixtures | — |

---

## C. Open questions for the owner

| # | Question | Recommendation |
|---|---|---|
| **C-1** | How does a subject re-raise after its Signal was resolved (dismissal, reschedule, supersede, suppression expiry), given `sprk_dedupekey` is unique and the writer keeps Resolved rows Resolved? | **Episode-scoped key** `{policycode}\|{type}\|{id}\|{episode}`. Re-raise when not suppressed **and** (Decide) the per-rule quiet window has passed (default 14 d, a knob in the rule body per A-1), or (Do) the subject's date has changed since resolution. Decide **before 031** |
| C-2 | Where does severity come from? | `sprk_policy.sprk_severity` (S-3) |
| C-3 | Record class for *Reassign* and *Extend response date* (F-27)? | **Routine**: own-work moves, as v4 has it |
| C-4 | Home of the action catalog and dismiss reasons in R1? | A **closed BFF C# catalog** with a read endpoint (A-2 already says "code"). Data-driven in R2 (Action Engine) |
| C-5 | How is a follow-on linked "created by decision"? | JSON refs in `sprk_decisionrecord.sprk_followons`, rendered from the record side. No new columns on four cross-domain tables |
| C-6 | Overdue threshold: v4's 1 day or the collector's 5? | Start at **5** for a like-for-like 064 cutover, then tune in UAT. It is a knob on the rule (criterion 1) |
| C-7 | Is the *Overdue To Do* rule in R1? Matterless To Dos? Who owns a To Do Signal? | Yes, but **exclude matterless To Dos**, and own the Signal by the **To Do's owner**, not the matter BU (a To Do is per-user, FR-30; BU ownership would show it to the whole BU). A recorded deviation from FR-14 for this subject type |
| C-8 | Is the Do lane scoped to the reader? | Yes: assignee for tasks/To Dos, assigner for work assignments, via the denormalized columns. Not a user-authored filter (FR-24 holds) |
| C-9 | Keep `sprk_highpriority` as a rank input (FR-32)? | Yes, as the 2nd key: severity → high priority → oldest → matter number |
| C-10 | Accept dropping the worklist's ⋮ menu (`DocumentRowMenu`) and the `OutcomeCard` extension (design §1.3 "binding")? | **Accept.** C-9 (`RowActionMenu`) continues as its own cleanup item. The status bar is built on Fluent `MessageBar` |
| C-11 | After D-13, may admins still author `sprk_policyversion` directly in the MDA (022 owner decision 2026-10-03)? And which identity does the BFF write with (`Spaarke Ontology Service` holds only R on both policy tables)? | Console + BFF is the **sanctioned** path, writing **on behalf of the admin (OBO)** so Dataverse enforces the Administrator role and audit names the human. Keep 022's evaluation-time gate as defence. Make the MDA policy forms read-only (form setting, no plugin). Do not grant the service role Create |
| C-12 | Superseding needs the old version's window closed, but no role can write `sprk_policyversion`. | Never write `sprk_inforceto`. *In force* = `sprk_currentversion`; the window end = the successor's `sprk_inforcefrom`. Fix schema-draft L261 |
| C-13 | v4 sentence copy names the witness (sender, date); 022 forbids values picked from matching rows. | Keep 022. Witnesses render as evidence lines |
| C-14 | Is a Console **Decision Record tab** in R1 (D-3 left auditing to the MDA)? | Yes. It is small (a DataGrid config) and is the only way to reach a decided item's read-only wizard from history |
| C-15 | Add an *association confirmed* trigger to 032 (F-39)? | Yes. Same handler, third enqueue site; the email project calls it |
| C-16 | Admin pages beyond slices a-c (Actions, Suppression + Lift, Health, Access), and recall storage for slice (c)? | File one issue for the four pages. For recall: 074 writes three columns on `sprk_triagecategory` (after `/conflict-check` with the email domain) |
| C-17 | Source freshness / *Missing* (spec §2.1 has no FR or task)? | R1 renders a null fact as *Missing* (057). Schedule the landing-contract columns + freshness as an issue, or add an FR if it stays in R1 |
| C-18 | Wizard host (record only, **not decided here**) | v4 assumes `WizardShell` + H§4.1, re-based on `SprkModal`; fallback: grow `WizardModal` (H§4.1 L227-229). **Touches** 053 (superseded), 056, 058, 102, 055, and the email hand-off E1/E12 |
| C-19 | Should 063 (Know promotion) get an action, and which? | Yes. *Assign Work* (exists as a Next-step creator) as the recommendation; a *Create budget* executor only if wanted |

---

## Tally

| Status | Count |
|---|---|
| MATCHES | 25 (incl. X-1, fixed in `cac41341a`) |
| CHANGES-SPEC | 21 |
| NEW-SCOPE | 39 |
| CONFLICTS-WITH-SETTLED-DECISION | 6 (R-11, H-3, S-4, S-7, M-1, M-2) |
| DROPS-SPEC-ITEM | 3 (W-4, W-14, Z-15) |
| **Rows** | **94** |

H§1.4 acceptance rules (26): 7 covered as-is · 6 covered with a change · 13 new.
