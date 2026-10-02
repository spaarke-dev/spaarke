# Daily Briefing in the Ontology — why its items fit, and how

> ## ✅ STATUS: CURRENT — accepted and scheduled `[stamped 2026-10-01]`
>
> Accepted by the owner on 2026-10-01. Its §8 proposed decisions are **scheduled as BR-1..BR-6 in `design.md`
> §8.0b**, and its substance is in `design.md` §5 In ("the Daily Briefing dissolves into the worklist"),
> component model §6.2 + decision 24, and `mvp-technical-spec.md` §8 items 11–13. Per §5.0, nothing here is
> merely listed.
>
> This file stays the **reasoning of record** for *why* each Briefing item does or does not become a worklist
> row — in particular the Decide / Do / Know test and BR-1's conclusion that acting on one's own assigned work
> writes **no** Decision Record.

> **Status**: Working note for owner review, 2026-10-01. Not a spec. It proposes open decisions for `design.md` §8
> (§8 below). Per the owner's 2026-09-30 rule in `design.md` §5.0, each must be scheduled there or filed in
> `defer-issues.md` with an Issue, not left only in this note.
> **Prompted by**: owner question during the Console prototype review: *"does it make sense, given the
> objectives of the ontology-driven intelligence, to combine these — Tasks, To Do, Matters, Projects would also
> have actions associated?"*
> **Builds on**: [`ontology-component-model.md`](ontology-component-model.md) §6.1, §6.2, §7 (decisions 9, 16) ·
> [`ontology-architecture-feedback.md`](ontology-architecture-feedback.md) §1.5, §2.1, §4 · [`design.md`](../design.md)
> §0, §3.1, §5 · the Console prototype in `c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\`
> (its README, findings 1–17).
> **Vocabulary**: component model §3 is authoritative. ⓘ *Written before the 2026-10-02 vocabulary decision:
> read **"flag"** below as **Signal** (`sprk_signal`) wherever it means the stored row, and **"row" / "item"** as
> **Work Item**. §3 retired "flag" as a synonym precisely because this document uses it both ways.*
> **Audience**: owner · `/design-to-spec` · future sessions.

---

## 1. The short answer

**Yes. The Daily Briefing belongs *inside* the Console worklist, not beside it.** The design documents already
say so: *"Daily Briefing, the work queue and email triage are the same widget"* (component model §6.1). And:
*"One 'needs attention' widget, not four queues … four places to look means nobody looks"* (§7, decision 9).

But **"combine" must mean *convert*, not *merge*.** Each Briefing item can enter the worklist only on the worklist's
terms: the row contract in feedback §4.1. Applying that contract sorts today's Briefing content into three kinds of
item, each handled differently:

| Kind | What it is | Why it's on screen | What resolves it | Record |
|---|---|---|---|---|
| **Decide** | A flag raised by one of the customer's rules | A rule's predicate held | A gated action, or a dismissal with a reason | Decision Record |
| **Do** | Dated, assigned work: tasks, To Dos, work assignments, inquiries awaiting reply | A declared due-date rule holds | Complete, reschedule, reassign, or dismiss | The object's own history (see §4.2) |
| **Know** | Something changed: a new or updated matter, project or document; activity on a monitored record | — | Nothing. There is no action | None. **Not a worklist row** |

Decide and Do become two lanes of one worklist, Decide first. Know becomes the narrative and the Context pane.
Know turns into a row only when a rule says something about it is *missing or wrong*.

---

## 2. What the Daily Briefing is today (verified 2026-10-01)

Read from `Spaarke.DailyBriefing.Components` and `DailyBriefingCollector` on master:

| Part | What it shows | What decides membership | AI involvement |
|---|---|---|---|
| **Stat tiles** | Updates · Overdue · Critical · Documents · Matters & Projects | Deterministic counts from the render data (`DailyBriefingApp.tsx` ~L742). The code calls them *"independent lenses, not a total-and-subsets hierarchy"*. Critical can exceed Updates, a contradiction an operator raised in UAT on 2026-07-09 | None |
| **Today's summary** (`TldrSection`) | Summary prose, key takeaways, a **"Top action"** | — | LLM-written, including the Top action (`DailyBriefingApp.tsx` L210) |
| **Critical Today** (`HighPrioritySection`) | Records with **`sprk_highpriority` or `sprk_monitor` = true** across 7 entities: matter, project, invoice, document, work assignment, event/task, To Do (`DailyBriefingCollector.CollectHighPriorityAsync`, L465–495) | A person set a flag | None — it bypasses the narrator |
| **Activity channels** (`ActivityNotesSection`, `NarrativeBullet`) | Overdue tasks, upcoming tasks, matters, projects, documents… | The collector's channel queries | Bullet prose; row text built from record fields |
| **Row actions** (⋮ menu) | Open record · Mark as read · Dismiss · Add to To Do · Keep 7 days | — | None |

Tested against the four clauses of component model §1 (*consolidate · resolve · match + act · record*), the Briefing
exercises **presentation only**: no policy evaluation, no action that changes anything, no record. That is the
component model's own verdict (§6.2): *"Daily Briefing is the upgrade target, not a bad example … the shape is right."*

---

## 3. Why combine — the rationale

1. **One place to look.** Decision 9. If the Briefing and the worklist both claim to show "what needs you today",
   users pick one and miss the other. The one they keep checking is the one that has the reasons and the actions.
2. **It is the same archetype.** Component model §6.1 and decision 16: one configurable widget with a closed set of
   dimensions (facts shown · policies evaluated · actions offered · grouping/sort · narrative on/off). The Briefing
   is that widget with *narrative on*. Combining them adds **no new widget**. The prototype confirmed this: the
   narrative works as a wrapper around the same rows in the same order (prototype finding 11).
3. **It closes the action loop.** Palantir's framing, verified in the 2026-09 research notes: *"Closing the action
   loop as decisions are made in real-time is what distinguishes an operational system from an analytical system."*
   Today the Briefing is analytical: it tells you, then sends you elsewhere to act. Inside the worklist every
   item carries an action, and acting records what happened.
4. **It is cheap, because the fact layer is source-agnostic.** Component model §4.15: `DailyBriefingCollector` does
   not change when a source system is bound. The collector's queries stay; what changes is where *membership*,
   *reasons* and *actions* come from.
5. **One ranking, one set of reasons, one record.** Two surfaces mean two orderings and two definitions of
   "urgent". One surface means one deterministic rank, one "why is this here?", and one answer to "what did we
   decide?".

**The caution: §0 still applies.** Overdue tasks and new matters are **not differentiated**; every
matter-management system shows them. They join for completeness and adoption ("this is where I start my day"),
not for the pitch. The design must therefore stop them from **burying** the differentiated items: the
cross-source flags that no incumbent can raise. That is the job of the lanes (§6).

---

## 4. The test every item must pass — and how each kind passes it

The row contract (feedback §4.1):

1. A row resolves to an **object**, never to a text string.
2. **Membership and rank are deterministic.** The model writes the sentence; it never selects the queue or the order.
3. It carries **evidence with provenance and freshness**, separated by epistemic tier.
4. It offers **at least one action that changes something**. No action, no signal.
5. **Acting writes a Decision Record** and closes the flag. Dismissing writes one too, with a reason.

### 4.1 Decide — rule-raised flags

New to the Briefing. Policy evaluation (`sprk_policy` / `sprk_policyversion`) writes a flag (`sprk_signal`); the user
acts through the gate; a Decision Record is written. Passes all five requirements by construction. Examples in the
prototype: *email classified as a scope change with no budget revision since* (cross-source), *spend at or above
115% of budget* (threshold), *inquiry with no linked reply after 5 business days* (SLA).

**Ranks first, always.** These are the items that justify the product.

### 4.2 Do — dated, assigned work

Today: the Briefing's *Overdue* and *upcoming* task channels, To Dos, and work assignments with a response due date.

**Membership should come from a declared rule, not from the Briefing's own query.** "A task assigned to me, open,
and past its due date" is the **Temporal** detection shape (feedback §2.1). It is the same shape as the inquiry SLA
rule already in the prototype. Expressing it as a policy row rather than a collector query gives Do items the same
machinery as Decide items, for free:

- a stated reason on the row (*"due 24 Sep · 7 days overdue"*);
- versioning (*"we now count business days"* is a new policy version, not a deployment);
- suppression after repeated dismissals;
- freshness of the source it read;
- and a deterministic answer to *"why isn't this task here?"*

One evaluator and one flag entity instead of a second, parallel query path.

**Actions are declared on the object type**, once: a task offers *complete · reschedule (with reason) · reassign ·
dismiss*. The worklist shows them as the same outcome cards as Decide items (prototype component kit). Requirements
1–4 hold.

**Requirement 5 needs a decision — the one real tension in combining.** Completing your own task is not a gated
decision. If every completion wrote a Decision Record, the record would fill with housekeeping. The production Briefing
shared in the review showed 11 overdue tasks, against the prototype's 5 decisions. The Decision Record is the asset that becomes the
Matter Report Card (component model §4.13), and it would lose its signal. The component model's own rule points the
same way: *"No gate, no entry."*

**Recommended position** (proposed decision 1 in §8):

- A Do item resolved in the Console (complete, reschedule, reassign) closes its flag as **`Acted`** with **no
  Decision Record**. The task's own history (status, who changed it, when, the reschedule reason) is the record.
  This needs a **nullable decision reference** on `sprk_signal` for `Acted` flags, which D-2 must not foreclose.
- **`ConditionCleared`** stays reserved for the condition ending *outside* the Console: the task was completed in
  Outlook or To Do, or its due date was moved in Matter Management. Keeping the two apart keeps the action-rate
  metric honest: completed ÷ (completed + dismissed) means something for a Do rule.
- **Dismissing** a Do item ("not mine", "no longer needed") is a judgement about the rule, so it **does** write a
  Decision Record with a reason, and counts toward suppression, exactly as for Decide.
- This amends row-contract requirement 5 to: *"acting through a gate writes a Decision Record; acting on one's own
  assigned work is recorded on the object."*

**Inquiries are both.** An inquiry is *created* by a Decide action (and recorded), then *tracked* as Do (its SLA).
The prototype shows this chain: DR-0105 creates INQ-0039, and the SLA rule later flags INQ-0039.

**Dependency.** Do items are only as complete as the task data underneath. `design.md` §3.1 records that every task
the platform created was invisible to the Briefing until PR #1032 (*`TaskActionCore` now sets `statuscode = Open`*;
*`DailyBriefingCollector` column fix*). [ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050) covers the 49
`sprk_event` rows still stranded in Draft. A due-date rule over that data must not ship ahead of those fixes.

### 4.3 Know — awareness

Today: new or recently updated matters, projects and documents; activity on records someone is monitoring.

These **fail requirement 4**: there is nothing to do that changes anything. Under the contract, *"no action, no
signal"*, so **they are not worklist rows.** They are still valuable, and they go where awareness belongs:

- **the narrative** — *"Since yesterday: 1 new matter opened, 3 documents filed on Acme v. Northwind"*;
- **the Context pane** — activity on the matter you are looking at;
- **counts**, where useful, in the narrative rather than as filter cards (a filter card that leads to no action
  is a dashboard tile).

**The promotion rule — the ontology-driven part.** A Know item becomes a Decide or Do row **only when a rule says
something about it is missing or wrong**:

| News (Know) | Rule that makes it work | Detection shape | Action it offers |
|---|---|---|---|
| New matter opened | New matter with **no budget** after 5 days | Absence | Set budget |
| New matter opened | New matter with **no outside counsel assigned** after 5 days | Absence | Assign counsel |
| Matter status changed to Closed | Matter **closed with open obligations or unpaid invoices** | Transition | Reopen · resolve · confirm |
| Invoice flagged high priority | High-priority invoice **unapproved after 10 days** | Temporal | Approve · query · dismiss |

*"A new matter was opened"* is news. *"A new matter has no budget"* is work. That difference is exactly the
difference between a feed and an ontology: membership by rule evaluation, not by recency.

**Binding-mode constraint** (feedback §1.5): Absence rules are **impossible over reference-mode data**. A rule like
*"new matter with no engagement letter"* cannot run while documents are reference-only via MCP (component model
§4.7). It needs documents mirrored. Worth knowing before promising document-based promotion rules to a customer.

### 4.4 The two human flags behind "Critical": High priority and Monitor

The Briefing's *Critical Today* list is driven by two booleans a person sets: `sprk_highpriority` and
`sprk_monitor`. **Neither is a reason in the ontology sense.** A flag says *someone cares*, not *what is wrong*. They
keep their meaning, but each becomes an input rather than a list:

- **High priority → a rank input.** Any Decide or Do item on a high-priority object ranks above the same item on an
  ordinary one, inside the deterministic order. It can also be a rule input (*"high-priority matter with no
  activity in 14 days"*).
- **Monitor → a subscription.** Changes on a monitored object go to Know (narrative and Context pane), and its
  worklist items carry a small *monitored* mark.

So *Critical Today* as a separate list disappears, and nothing it meant is lost.

---

## 5. Where each Daily Briefing part lands in the Console

| Daily Briefing today | In the Console | Why |
|---|---|---|
| **Stat tiles** (independent, overlapping lenses) | **Count filter cards** that split the list by *what has to be done*, so the counts add up. One set per lane. Urgency (overdue, oldest age) is a line inside each card | The Briefing's own UAT found overlapping counts read as contradictions. Cards are filters, not tiles. Prototype finding 17 |
| **Today's summary** | **Narrative wrapper** around the same rows in the same order | Unchanged in spirit; already the archetype's narrative variant |
| **"Top action"** chosen by the LLM | **Removed.** The first row *is* the top action, because rank is deterministic | The model choosing priority breaks requirement 2. *"LLM classifies; deterministic code decides; a human acts"* (`design.md` decision 15) |
| **Critical Today** rows | Matter cards with one line per issue, each carrying its **reason**. High priority becomes rank; Monitor becomes subscription (§4.4) | A row must say *why it's here*, not just *that it's flagged* |
| **⋮ Open record** | A link to the object | Navigation, not a decision |
| **⋮ Dismiss** | Dismiss as an outcome card, **reason required**, written to the Decision Record (Decide and Do) | A dismissal is a record, not an absence |
| **⋮ Add to To Do** | A **declared action on the object type** (*create follow-up task*) | Actions belong to object types, declared once |
| **⋮ Mark as read · Keep 7 days** | Know-lane housekeeping in the narrative and Context; not decisions, never recorded | No change in the world, so no record |
| **Activity channels** (matters, projects, documents) | **Know**: narrative ("since yesterday") and the Context pane | §4.3 |
| **Overdue / upcoming task channels** | **Do lane**, membership from a declared Temporal rule | §4.2 |

### 5.1 Lanes, not one merged list

Decide and Do are separate lanes of the same worklist, **Decide first**. Each has its own count cards and its own
*Start review*. The reason is volume. Merged into one ranked list, the 11 overdue tasks in the production Briefing would push 5 decisions off the
screen, and the 5 decisions are the reason the product exists (§3, the §0 caution). The prototype's March 2026
*action-workspace* experiment explored the same split as *Do / Know* zones. That is prior art for keeping
"what needs a decision" visibly apart from "what needs doing".

### 5.2 What is prototyped and what is not

The Console prototype (`2026-10-spaarke-console`, v2.1) implements the **Decide** lane: rule-raised flags, the
review process, the count filter cards and the Decision Record. The **Do** lane and the **Know** promotion rules
described here are **not yet prototyped**.

---

## 6. What changes, and what does not

**Does not change**

- `DailyBriefingCollector`'s fact gathering and its source-agnostic queries (component model §4.15).
- Deterministic counts. The Briefing already holds this posture (*"no LLM, no fabrication"*, `DailyBriefingApp.tsx` ~L742).
- The narrative: still model-written, still a wrapper.

**Changes**

- **Where membership comes from.** The Overdue / upcoming channels become Temporal policies; Know items enter
  only through Absence / Transition / Temporal policies.
- **What "Critical" means.** Rank input and subscription, not a list.
- **Where priority comes from.** The deterministic rank; no AI "Top action".
- **How actions are offered.** Declared per object type and presented as outcome cards; the ⋮ menus go.
- **What the tiles do.** They become filters that split the list.

---

## 7. Logic, compressed

1. The ontology's worklist is defined by a contract: object, deterministic membership, evidence, an action,
   a record.
2. The Daily Briefing shows three different kinds of thing under one heading: rule-worthy conditions it never
   evaluated, dated work, and news.
3. Run each through the contract. Dated work passes **if its membership becomes a declared rule** and its record
   lives on the object. News fails requirement 4, so it stays awareness, **unless a rule finds something missing or
   wrong**, at which point it becomes work.
4. So the Briefing does not merge into the worklist. It **dissolves into it**: its work becomes lanes, its news
   becomes narrative and context, its tiles become filters, and its AI stops choosing priority.
5. The result is one place to start the day, ordered by the customer's own rules, where every item says why it is
   there and every action leaves a record of what was decided.

---

## 8. Proposed open decisions (to schedule in `design.md` §8)

| # | Decision | Recommendation | Blocks |
|---|---|---|---|
| 1 | **Do-lane resolution record.** Does acting on one's own assigned work write a Decision Record? | **No.** Close the flag as `Acted` with a null decision reference; the object's history is the record. Dismissal still writes a Decision Record. Amend row-contract requirement 5 accordingly | D-2 field list (nullable decision reference) · the `sprk_signal` shape decision |
| 2 | **Which Do rules ship first** | Overdue task · task due within 3 days · work assignment past `sprk_responseduedate`. All Temporal, all over data Spaarke already holds | Do lane |
| 3 | **High priority and Monitor semantics** | High priority = rank input (and optional rule input); Monitor = subscription to Know. Retire the separate Critical list | Worklist rank function · narrative |
| 4 | **Transition of the Daily Briefing widget** | Replace the Workspace's *Daily Briefing* tab with the worklist (narrative on) once the Decide and Do lanes exist; keep the old widget only until then | Console hosting · LegalWorkspace widget registry |
| 5 | **Lane order and volume rules** | Decide always above Do; each lane with its own cards and review; the narrative summarises Do volume rather than listing it | Worklist layout |
| 6 | **First Know-promotion rules** | *New matter with no budget after 5 days* (Absence, Spaarke-held data, so it works in mirror mode). Defer document-based Absence rules until documents are mirrored | Policy catalog · binding-mode feasibility (feedback §1.5) |

---

## 9. Sources

- `ontology-component-model.md` §1 (four clauses), §4.13 (Decision Record, *"no gate, no entry"*), §4.15 (fact
  layer), §6.1 (one archetype), §6.2 (Daily Briefing as upgrade target), §7 (decisions 9, 16)
- `ontology-architecture-feedback.md` §1.5 (shape × binding mode), §2.1 (eight detection shapes), §4.1 (row
  contract), §4.4 (action rate)
- `design.md` §0 (differentiation test), §3.1 (PR #1032 repairs), decision 15
- `Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` (stat tiles ~L742–781; Top action L210)
- `Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` (high-priority specs L403–433; flag filter L491–495)
- `c:\code_files\spaarke-website\content-platform\research\2026-09-loi-series\notes\palantir-intelligence-platform-model.verified.md` §2 (closing the action loop)
- Console prototype: `c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\README.md` (findings 1–17;
  v2 review process; v2.1 count filters)
