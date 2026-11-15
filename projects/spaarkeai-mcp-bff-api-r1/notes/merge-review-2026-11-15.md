# Master merge into the project branch — 2026-11-15

PR #2587 conflicted with master (448 commits behind), so GitHub ran none of its pull_request workflows. Master was merged in (a merge, never a rebase: ontology condition 3), carrying ontology task 191 (#2792, `e5f5c9a6df`: a decision whose inquiry email failed can be finished from its item; `sendChoice`; replayed stored failure answered 409) into this branch's decision capability and MCP tools.

## Commits

| Commit | What |
|---|---|
| `387f6bf88b` | Merge of origin/master. Conflicts: `.gitignore` and `projects/INDEX.md` (both sides kept); `decisionDisplay.ts` (invoice-review check first, master's checks after); `DecisionCommitEndpoints.cs` and `DecisionPlanEndpoints.cs` (thin adapters kept, master's task-191 rules moved into `DecisionResultMapper` and `DecisionCapability.GetPlanAsync`, stamp before the unfinished read as on master); ArchTests ledger (both chains, count 155). `DecisionCommitService.cs` is exactly master's. |
| `39c993c58c` | `spaarke_complete_decision` maps task 191's outcomes like REST (409 stored failure with its reasonCode, 422 `review_invalid`); `review.sendChoice` passes through (schema from master's `DecisionReview`); `spaarke_get_decision_plan` returns `plan.unfinished`; widget shim titles a failed 409 as REST does. |
| `a66e9b5509`…`66060a8f40` | Fix round (below). |
| `288f558ff4` | Re-check follow-ups: plan summary wording; permanent test for the guard after a failed record. |
| `f9d1061111` | Merge of the reviewed branch into `work/spaarkeai-mcp-bff-api-r1`. |

REST changes in this merge come from master and are recorded in the goldens: the plan gains `unfinished` (and task 190's Cc/Bcc fields), commit bodies gain `sendChoiceRequired`, and a retry of a non-resumable failure is 409, not 500 (baseline renamed `decision.409.not-resumable`).

## Review (one Fable adversarial pass, then a scoped Sonnet re-check)

REST was byte-equal to master everywhere compared: the re-recorded goldens passed against master's endpoint code (54/54, plus nine task-191 probes). Fixed:

| # | Class | Finding | Fix |
|---|---|---|---|
| 1 | F2 | Master's Send again / Record without sending / Retry recording ignored the Confirm guard: a double click on Next could send a second inquiry email | `a66e9b5509`, guard re-armed when the choice buttons appear; tests for resumed and failed-record paths (`288f558ff4`) |
| 2 | F2 | `plan.unfinished` (free-text review) could push `spaarke_get_decision_plan` past 64 KiB (12,000 Cyrillic chars = 83,178 B) | `f7f4c8b28b`: whole-result budget; evidence budget = min(20,000, 65,536 − 12,000 − plan bytes); a plan that still cannot fit returns `mcp.plan_too_large` (413, fixed text, no review text). Rule in `decisions.md` |
| 3 | F1 | MCP `review_unfinished` message wrong when another user holds the review | `c83e5396e0`: one text true in both cases; REST unchanged |
| 4 | F4 | `contract-baseline.md` still called `not_resumable` a 500; no goldens for 409 `send_choice_required` / `review_unfinished` | `229d4cb1b6` |
| 5 | F4 | `closeOnEscape` and widget `wizardClose` tests starved on Fluent's aria-hidden under load | `de43314a6a`, `66060a8f40` (`hidden: true` queries; assertions unchanged) |

Verification on the fix branch: BFF build 0 warnings; MCP/Signal/Decision tests 6,154 passed, 8 skipped; ArchTests 2,630/2,630; conformance exit 0; widget tsc clean, jest 347 passed (2 skipped), build 1.54 MB; shared DecisionWizard + LookupField 308 passed (+1 in `288f558ff4`, which fails with the guard removed from Send again).

## Known limits

- An unfinished review whose Signal the budget-revision trigger closed cannot be reached from Copilot (`wizardItems.ts` answers ITEM_GONE); finish it in the Console.
- The MCP error body omits `reviewId`/`actionCode` and step `warning` (pre-existing; the widget does not need them).
- An unfinished review on a billing Signal stored before the billing refusal shipped answers 409 `decided_in_invoice_review` until its TTL (deploy edge only).
- The ArchTests ledger's narrative line numbers for `DecisionCommitService.cs` are stale after master's +250 lines (no test reads them).
- The guard re-arms only on the transition into the choice footer; a second failure while the buttons already show relies on single-flight.
- The capability writes the plan-read stamp before the tool returns `mcp.plan_too_large`.
- Other widget tests look up Next synchronously, after the dialog is already warm; only the cold-dialog helper was changed.
- Ontology owns `Services/Signals/**` and `Api/Signals/**`: this merge needs their review in PR #2587.

Found outside the project: `projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md` on master still holds a conflict marker (reported to the owner).
