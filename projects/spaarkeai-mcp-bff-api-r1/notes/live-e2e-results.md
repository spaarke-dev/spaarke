# Task 042: live E2E results (dev, BFF `3067fb3bdc`)

> Evidence rule (ADR-015): no token, claim value or privileged text here. FR-04 is recorded as marker yes/no only.

## Run 1: owner, 2026-11-25 (~04:20Z)

| Step | Result | Notes |
|---|---|---|
| 2 Seed check | **FAIL** | The lane holds 10 Work Items, all `invoiceDecisions` (Ask outside counsel 0, Approve or rebudget 0, Chase a reply 0). The spec needs ≥ 200 items across ≥ 3 matters, privileged items and every lookup-bearing action type. |
| 3 Cowork lane | Partial pass | `/spaarke-decisions` renders the widget in Cowork; counts match the Console (owner). Nothing can be resolved: every item is a billing Work Item, so Start review shows the "decided in Invoice Review" notice (EB-D-194, by design). The five lookup-bearing decisions cannot run until non-billing items exist. |
| 4 Chat | Partial pass | The direct prompt calls the tool, and Chat renders the same widget (source "Spaarke MCP"). Copilot also offers to save the model's table as a Microsoft List; that is Copilot platform behaviour, not ours. Nothing to resolve (billing only). |
| 5 FR-04, billing row, dark mode | Pass (owner "ok") | No privileged items are in the seed, so FR-04 is not yet tested against a real privileged item. |
| Do lane | Out of scope (v1) | Asked for the Do lane, the model answered that the connection shows only the Decide lane and pointed to the Console. That is correct v1 behaviour (spec: Do/Know lanes out of scope). |
| 6 Routing eval | Not run | |
| 7 NFR-01 | Not yet usable | App Insights since 00:40Z (~33% sampled) shows 1 `spaarke_list_decision_page` call at 04:24:34Z (1,942 ms, 10 rows). No plan, lookup or complete calls are possible with a billing-only lane. |
| 8 Runbook re-run | Not run | |

**Seed root cause (read-only Dataverse check, ~04:50Z 2026-11-25).**
- All of dev holds only 10 open Decide Signals, all `BILL-SYSTEM-SUBMISSION`. The test user already sees every one, so giving them access to more matters (the owner's choice) cannot add anything.
- 183 open `POL-MATTER-NOBUDGET` Decide Signals were resolved "Policy Retired" by `mi-ontology-writer-dev` between about 02:00 and 02:31Z 2026-11-25. The policy row is still Active.
- Asked ontology-r1 (a) why, and (b) the supported way to bring back non-billing Decide items.

**ontology-r1 answer (~05:00Z):**
- The owner's account switched the "No active budget" rule (`POL-MATTER-NOBUDGET`) Off at 2026-11-24 02:58:50Z. The 02:30Z nightly then closed its open Signals as designed (D-170). The rule being "Active" does not mean it is in force.
- **Way back:** the owner switches the rule On in the Ontology admin tab. The next 02:30Z nightly re-raises a Work Item for every qualifying matter, for all users.
- **Action coverage once the rule is on:**
  - "No active budget" covers Assign Task, Assign Work and Create/Revise budget.
  - Send budget inquiry needs a "Classified fee or scope change" item, raised by a classified inbound email.
  - **Set firm is an Invoice Review action, not a Decide-lane one, so it is not testable in Copilot.** The 042 list of five is corrected to four, with Send budget inquiry depending on an email.
  - A privileged item comes only from a Secure matter that qualifies.

**On-demand run (ontology-r1):** `POST /api/admin/jobs/{jobId}/trigger` (SystemAdmin) runs `SignalReevaluationJob` now. Take the jobId from `GET /api/admin/jobs`. It returns 202 plus a run id, and `GET …/status` gives the outcome. It is the full nightly write run over every enabled policy, so it needs the owner's go. `POST /api/ontology/admin/authoring/dry-run` previews which matters qualify and writes nothing.

**Seed restored (owner go "Yes, On + run now", 2026-11-25):**
- 04:51:17Z: rule switched On via `PUT /api/v1/ontology/admin/rules/{id}/enabled`, as the owner. Response 200, `enabled: true`.
- 04:51:23–04:54:01Z: `signal-reevaluation` run `7f929ff2-…` (ManualAdmin).
  - Status **Failed**, "refused=39". Hits 316, reraised 147, closed 3.
- Read-only count afterwards: **154 open Decide items**, 144 `POL-MATTER-NOBUDGET` (144 matters, all business-unit-owned, so no secure-child items) and 10 billing.
- The 39 refusals were asked of ontology-r1. Until they are explained, there may be no privileged Decide item.

**ontology-r1 interim answer on the 39 refusals:**
- A run reports Failed whenever any write is refused; that part is by design. The refusals themselves are not expected: the 2026-11-24 nightly had 0.
- Ontology is investigating a possible defect: the 04:42Z swap to `ef0c566d2c`, or the Off→On re-raise path.
- The run's log lines are also missing from App Insights for 04:51–04:56Z.
- No secure-child item was re-raised, so **no privileged Decide item exists today**. FR-04 stays untested live until one exists.
- Ontology asked that the run not be repeated.

**Cause of refused=39 (ontology-r1, verified read-only):**
- 154 `sprk_matter` rows were deleted interactively from the owner's account between 04:52:22 and 04:56:39Z, during the run. This session's only writes were the rule switch and the job trigger.
- Refusals on deleted matters fail closed (D-38); this is not a regression.
- **Only 30 of the 144 no-budget Work Items are on matters that still exist.** The other 114 should close as Condition Cleared at the next nightly.
- The missing run logs are a separate defect, #3125 (DI-175, ontology's).

Open: (1) non-billing seed for the test user; (2) whether the run was signed in as the non-admin test user (the model's reply greets the user by first name).

## Run 2: non-admin test user, 2026-11-25 (owner report)
- Signed in as the licensed non-admin user, the owner ran `/spaarke-decision` in Cowork and the `spaarke-legal-g8p2-dd` agent in Chat; both work. Connect-once and non-admin access: pass.
- FR-04 live spot-check: not testable on dev (owner 2026-11-25, `notes/decisions.md`); covered by the automated tests.
- Still to check: resolving items (one per action type, with a because rationale), a Chat resolve, the routing eval, widget-frame console errors, the Console smoke afterwards, NFR-01 (after the deploy carrying #3146/#3152/#3161) and the runbook re-run.

## Run 3: non-admin test user, 2026-11-25 ~21:00–21:40Z (owner report + App Insights)
- **Cowork resolve (Create budget, Assign Task, Add To Do, Assign Work; "Targeted Protein Degradation Patent Application").** The first Confirm showed "The decision may not have been recorded"; Try again finished at once.
  - Server: `spaarke_complete_decision` at 21:09:27Z took **26,924 ms**, outcome ok. The decision WAS recorded on the first attempt; the Copilot host gave up waiting before the answer came back (the widget sets no timeout of its own).
  - Retry at 21:09:57Z: 697 ms, outcome ok, 13 reads and **0 writes**: the server-side replay (idempotency key) answered. One Decision Record, no duplicate writes or email. The safety path worked as designed.
  - Where the 27 s went (one call): 113 SDK (Organization.svc) calls, 13.3 s; 51 caller-access calls on `systemusers(…)`, 5.1 s; ~40 uncached `EntityDefinitions` metadata reads, ~3.5 s; four writes (budget, event, to-do, work assignment); then in the same request a notification email (Key Vault, token, `sendMail`, sent-items read, SPE archive PUT, ~3 s) and two Azure OpenAI calls (~1 s).
  - **NFR-01 complete target 15 s: missed (26.9 s), and close to the 30 s hard bound.** The 30 s bound is not safe while follow-ups run inside the request.
- **Chat.** "what decisions do i have open in Spaarke" did not call the tool (see `routing-eval.md` "Off-sheet miss"): the old spike plugin is still connected and the model read "open decisions" as Decision Records.
- **Routing eval:** 15 / 15 targets, controls 0 / 5 confirmed (16 unclear). `routing-eval.md`.
- **Console smoke after the runs:** pass (owner).
- **Owner UX feedback (widget row card and wizard; shared components, so the Console too):**
  - The wizard's first step leads with a "What was found" heading; the finding itself ("Matter has no active budget") should lead, with no title-level "What was found" heading.
  - The row card's first line should be the finding ("Matter has no budget"), the second line the record name and number.
- **Timings seen (before the NFR-01 fixes reached dev):** show 3.1–6.5 s, page 1.5–3.3 s, plan 3.3–4.6 s, lookup 0.42–0.54 s, complete 26.9 s (n=1).
