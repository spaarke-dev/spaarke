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

Open: (1) non-billing seed for the test user; (2) whether the run was signed in as the non-admin test user (the model's reply greets the user by first name).
