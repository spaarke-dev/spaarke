# Routing eval — task 042 step 6 (success criterion 4)

> Purpose: does Copilot call `spaarke_show_decisions` from a plain prompt, **without** the `/spaarke-decisions` skill? And does it leave unrelated prompts alone?
> Dev BFF `3067fb3bdc`. Run as the test user `demo@…` (licensed, non-admin). Prepared 2026-11-25 by mcp-r1; the owner runs it.

## How to run

- **Copilot Chat**, a **new chat for every prompt**. Select the **Spaarke MCP** source in Sources (record if you could not).
- Do **not** type `/spaarke-decisions` or pick the skill. Paste the prompt exactly as written.
- For each prompt, record:
  - **Called?** Did a Spaarke tool run? (`Y`/`N`). The tool name shows in the answer's activity or reasoning panel; in Copilot it is prefixed `SpaarkeMCP_`.
  - **Widget?** Did the Decide lane widget appear? (`Y`/`N`)
  - **Category**, if shown: which card the lane opened on (All, Invoice decisions, Ask outside counsel, Approve or rebudget, Chase a reply, Other).
- Only `spaarke_show_decisions` is visible to the model; the other four tools are app-only, so the model can't call them. A call to any other Spaarke tool name is a defect: screenshot it.
- Do not complete any decision from the text answer. Close the widget without confirming anything.

Prompts 1–15 are **targets**: they should call the tool. Prompts 16–20 are **controls**: they should not. Targets 1–8 don't name Spaarke; 9–15 do.

## Prompts

| # | Prompt | Expect | Called? | Widget? | Category | Note |
|---|---|---|---|---|---|---|
| 1 | What decisions are waiting on me? | call | | | | |
| 2 | Show me the work items I need to decide on. | call | | | | |
| 3 | Which legal operations items need my decision today? | call | | | | |
| 4 | Do I have any invoices waiting for a decision? | call (Invoice decisions) | | | | |
| 5 | What needs my approval in legal ops right now? | call | | | | |
| 6 | Is anything waiting for me to ask outside counsel? | call (Ask outside counsel) | | | | |
| 7 | Open my decision queue. | call | | | | |
| 8 | Which matters have budget decisions pending for me? | call (Approve or rebudget) | | | | |
| 9 | What decisions are waiting on me in Spaarke? | call | | | | (the spec's own example) |
| 10 | Show my Spaarke decide lane. | call | | | | |
| 11 | How many items do I have to decide in Spaarke? | call | | | | |
| 12 | In Spaarke, which replies do I need to chase? | call (Chase a reply) | | | | |
| 13 | List my open Spaarke work items that need a decision. | call | | | | |
| 14 | Spaarke: anything urgent for me to decide? | call | | | | |
| 15 | Let me review my pending Spaarke decisions. | call | | | | |
| 16 | Summarise the key risks in a standard NDA. | no call | | | | control |
| 17 | Draft a short email asking a vendor for an updated W-9. | no call | | | | control |
| 18 | What is the capital of Australia? | no call | | | | control |
| 19 | List my meetings tomorrow. | no call (Work IQ) | | | | control |
| 20 | Explain what a limitation-of-liability clause does. | no call | | | | control |

## Result

- Target hit rate: __ / 15 (prompts 1–8 without "Spaarke": __ / 8; prompts 9–15 with it: __ / 7)
- Control false-call rate: __ / 5
- Category correct, where an expected category is given (4, 6, 8, 12): __ / 4
- Privileged-marker check (FR-04, step 5): for any answer that called the tool, did the text show only "N privileged items — open to review" and no privileged detail? Record only yes or no; no screenshots of privileged content.

Pass bar: the spec sets none beyond "the rate is recorded" (success criterion 4). Prompt 9 must call the tool; if it does not, escalate.
