# G-P2 Browser UAT — Round 1 Findings (2026-07-06, operator on spaarkedev1)

> Deployed build under test: `29f079ee4` (BFF + sprk_spaarkeai). Screenshots in operator session.

| # | Finding | Triage | Disposition |
|---|---|---|---|
| 1 | [Summarize] chip should sit BENEATH the "Classified…" chat entry (inline in the transcript, not in the strip above the composer); label should read like a phrase — "Summarize this document" | UI placement (client) + chip label (catalog data on chat-classify chipTransitions) | FIX NOW |
| 2 | "Insert" affordance under every assistant message — purpose unclear; remove/hide until needed | Pre-existing SprkChat insert-into-document affordance rendering in a host with no insert target | FIX NOW (hide when no target) |
| 3 | Follow-on instruction "provide a more concise summary" → generic clarifying question instead of a concise rewrite | Loop context gap: Event/Click outputs render as CLIENT-local messages; investigate whether stored SessionOutputs (ledger) reach the loop's context (task-002 digest-with-outputs seam) | FIX NOW (investigate root cause first) |
| 4 | Second upload + typed "summarize this document" → "cannot find the content of the file … in the current session" | Suspected session-manifest propagation race on the LOOP path (Event path got a readiness probe at 027-fix; the loop/capability path did not) | FIX NOW (investigate first) |
| 5 | "Summarize again" chip after that DID execute the summary | Confirms round-2 P1 fix (dispatch-time FR-08 default-all) + supports the race hypothesis in #4 | ✅ PASS evidence |
| 6 | "create a new matter" → Confirm Action popup, but confirm produced no record, no progress, no completion message | Gate SUSPEND is correct (037's SideEffectGateAIFunction working as designed); confirm-RESUME for typed-handler tools is the documented P3 seam (422 today; lands with FR-P3-03). Missing-feedback-after-confirm is a UX bug NOW | FIX NOW: honest interim message on confirm (no silent nothing). Resume-executes lands in P3 |
| 7 | Dark mode works | — | ✅ PASS |

## Chip label ruling applied (from finding 1)
- chat-classify single-file transition label: "Summarize" → **"Summarize this document"**
- bulk label stays dynamic ("Summarize all N files?"); chat-summarize transition label "Summarize again" unchanged.

## Gate status
FAIL-with-findings → fix wave dispatched, redeploy, operator re-UAT (round 2).
