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

Open: (1) non-billing seed for the test user; (2) whether the run was signed in as the non-admin test user (the model's reply greets the user by first name).
