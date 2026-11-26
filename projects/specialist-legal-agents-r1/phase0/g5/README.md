# Phase 0 test G5: can one practice-area agent route each request to the right function?

> **Program:** specialist-legal-agents-r1 (design.md §3.9 and §8 G5; spec FR-06, FR-01).
> **Decides:** D-A12's delivery shape (one area agent, or one agent per function). It does **not** decide the go/no-go.
> **Status:** kit written, not run. Nothing here is uploaded, installed or changed in the tenant.
> **Who does what:**
> - **The owner** (or the test user) creates, uploads and removes agents, and sends the prompts. Claude sessions never click tenant or admin actions.
> - **This session** builds the package files, checks App Insights per prompt, and fills the results sheet.
> - Content rule: every prompt below is synthetic. No prompt, file or package for this test may name or quote the accelerator's reference material or any real agreement (NFR-02). Check every file you add with the accelerator's `FORBIDDEN_IN_PACKAGE` pattern (`src/accelerators/build/gates.py` on `origin/work/spaarke-legal-ai-accelerator-r1`, read only) before it goes in a package.

## The question

Given one agent with three functions (NDA review, agreement review, legal hold review), does Copilot Chat send each in-scope request to the right function, and leave out-of-scope requests alone?

**Why it matters.** The design proposes one agent per practice area, with functions inside it (D-A12). If routing is unreliable, the front door falls back to one agent per function (see "Result and fallback").

## Pass rule (spec FR-06, exactly)

| Measure | Pass |
|---|---|
| **Routing** | At least **95%** of in-scope prompts reach the expected function. With 36 in-scope prompts that means **35 or 36 correct**; 34 is 94.4% and fails. |
| **Controls** | **0** function tools invoked for any of the 12 controls. One invocation fails the test, whatever the routing score. |

- A prompt is **routed correctly** when the BFF logs exactly one `tools/call` for the expected function's tool in that turn (BR-7), and none for the other two functions' tools.
- A prompt that calls two function tools, or none, or the wrong one, is a **miss**.
- Each prompt is run **once**, in a new chat. A miss is never replaced by a re-run; re-runs are recorded as extra rows and do not change the score (one exception, App Insights sampling, below).
- The model's own account of what it called is not evidence (BR-7). The App Insights `tools/call` row is.
- Tool-returned instructions (spec FR-05): in the same runs, note per prompt whether the agent followed any instruction text returned by the function tool (for example a "next step" line), in a separate column. It does not affect the G5 score.

## Setup

### Function tool map (fill in before the run)

The function tools are the ones the Phase 0 spike branch exposes on `/mcp/legal-operations` (never merged, FR-02). Copy the names from `tools/list`; do not guess.

| Function | Expected tool name (from `tools/list`) |
|---|---|
| NDA review | `spike_review_nda` (task 011; confirm against `tools/list` or the smoke prompt) |
| Agreement review | **none on dev yet** (see "Status of the function tools") |
| Legal hold review | **none on dev yet** (see "Status of the function tools") |

#### Status of the function tools (task 020, 2026-11-26): a gap that blocks the run

- The dev BFF (spike `d307d67e36`) exposes two spike tools to the model: `spike_get_profile` and `spike_review_nda`, besides `spaarke_show_decisions`. Only `spike_review_nda` is a G5 function tool (NDA review). `spike_get_profile` is a profile fetch, not a function; it must never count as one, and it never answers a G5 prompt correctly.
- **Agreement review and legal hold review have no tool.** Task 011 built only the two NDA spike tools, so the server cannot log a correct `tools/call` for A-01 to A-12 or H-01 to H-12. Without them the routing score cannot be computed (BR-7: the BFF row is the evidence).
- **What is needed before task 023 (not done here; it is a spike-branch and deploy change):** two more read-only, model-visible spike tools on `/mcp/legal-operations`, for example `spike_review_agreement` and `spike_review_legal_hold`. Each takes the document text and may return a fixed, synthetic stub result, because G5 scores routing, not review quality. They need the owner's go, a spike-branch commit (never merged) and a dev deploy through uac-r2 for that SHA.
- **The package needs no change when they arrive.** It uses dynamic discovery (BR-5), and its instructions describe the three functions by purpose, not by tool name. If a function's tool is missing, the agent says so and uses no other tool, so a missing tool never reads as a wrong route. Record the tool names in the table above from `tools/list`, then run the smoke prompt.
- Until the tools exist, run only what is meaningful: NDA prompts (N-01 to N-12) and the controls (C-01 to C-12). Do not score the agreement and legal hold prompts as misses; record "no tool on dev".
- `spike_review_nda` returns no instruction block, so the FR-05 column is "n.a." for G5 unless the new tools return one.

#### The package (task 020)

| Item | Value |
|---|---|
| Folder | `package/` (source), zip `spaarke-legal-g5-area-1.0.0.zip` beside this file (gitignored) |
| App id | `f7af4143-02b0-4912-b3ee-e73b0e202422`, version 1.0.0, short name `spaarke-legal-g5-area-test`, namespace `spaarkeg5` |
| Built from | `../g8/package-p2-v2-dd/`: same MCP URL, same A1 `reference_id`, dynamic discovery, no query string, manifest 1.25, agent v1.6, plugin v2.4 |
| Instructions | 1,673 characters: the area and three functions in one sentence each, "exactly one function tool per request", "if a function's tool is not in your tool list, say so and use no other tool", "for anything else call no tool", review-only limits, and the spike error codes. No profile or position text |
| Starters | Review an NDA; Review an agreement; Review a hold notice |
| Forbidden-pattern check | 0 hits in the folder and in the zip (see `../g1-nda/README.md`, "FORBIDDEN_IN_PACKAGE check") |
| Upload | The owner's, per `../g1-nda/packages/UPLOAD.md` row 4, after the R4 row is sent |

### Baseline: the existing *Spaarke Legal Review* agent

1. **Confirm its instruction version first.** Open the agent as its owner and copy the instructions text and the version or last-modified date into the table below. The accelerator's package build does not generate this agent, and its version is unconfirmed (design §3.9).
2. **Do not edit, re-upload or copy it.** If a copy is ever needed, send an R4 inventory row first (FR-06).
3. It has two functions (NDA and legal hold). Run the full prompt set against it, in Chat, in new chats.
4. Scoring for the baseline:
   - **Scored:** the NDA prompts (N-01 to N-12), the legal hold prompts (H-01 to H-12) and the controls (C-01 to C-12). Same pass rule, so the baseline pass needs 23 of 24 correct and 0 on controls.
   - **Informational, not scored:** the agreement prompts (A-01 to A-12). This agent has no agreement function; record where each goes (NDA function, legal hold function, or none). This shows how a two-function agent behaves when a request fits neither.
5. The baseline was built in Copilot Studio and its tools may be reached through a different path than the new agent. If App Insights cannot attribute a call, record the agent's visible tool trace and mark the evidence column "trace only".

| Item | Value |
|---|---|
| Agent name | Spaarke Legal Review |
| Built in | Copilot Studio (owner, 2026-11-09, accelerator DA-1 spike) |
| Instruction version / last modified | |
| Instruction text saved at (path) | |
| Confirmed by / date | |

### Main test: a three-function area agent

- **Build rules:** G8-P2 and `notes/agent-package-identity-and-distribution.md`:
  - a coded declarative agent with its **own** MCP action (BR-1);
  - action on the shared `/mcp/legal-operations` URL, `OAuthPluginVault` with the existing A1 `reference_id`, no new registration (R1);
  - **dynamic tool discovery** (BR-5; proven by V2): `functions: []`, `run_for_functions: ["*"]`;
  - **a fresh app id** for each action change, or a wait of about 45 minutes after any update (BR-2, BR-3). Never stack updates to fix one that has not loaded yet;
  - no query string in the action URL (H2 still open);
  - org catalog install for the owner and the test user only, never All users (R7).
- **Agent instructions** describe the area and name the three functions in one sentence each, with a "do nothing for anything else" line. They carry no profile or position text (that is G1).
- **Reuse:** start from `phase0/g8/package-p2-v2-dd/` (the V2 dynamic-discovery package). Copy it, change the app id, name, short name and instructions. Zip the files at the zip root (`Compress-Archive`, as in the G8 README).
- **Verify the package loaded before the first prompt:** one smoke prompt, "List every tool and source you can use in this conversation", must list the three function tools. If it does not, wait and re-test; do not run the set on an agent that has not loaded (BR-3: the action can take about 40 minutes after an update).

### Inventory rows (R4): send to COPILOT-PLATFORM-COORDINATION (Issue #3006) before any upload

| # | Artifact | Kind | Owner project | Purpose | Scope | Removal |
|---|---|---|---|---|---|---|
| G5-a | `spaarke-legal-g5-area-test` (app id `f7af4143-02b0-4912-b3ee-e73b0e202422`, v1.0.0, built 2026-11-26 by task 020) | App package, one declarative agent, one MCP action on `/mcp/legal-operations`, A1 `reference_id`, dynamic discovery | specialist-legal-agents-r1 | Phase 0 G5: three-function routing | Owner + test user only (R7), org catalog | Owner removes it in admin center when the G5 result is recorded; removal date set when the row is sent |
| G5-b | *Spaarke Legal Review* (existing) | **No change.** Read only: confirm instructions, send prompts | accelerator r1 (owner of the agent) | G5 baseline | Existing scope | None |

R4 row sent: **not yet, held** (2026-11-26). It waits on the owner decision about the two stub tools (see notes/decisions.md); no upload before it is sent.

#### R4 row (to send)

| Artifact | Kind | Owner project | Scope | Removal plan | Removal date |
|---|---|---|---|---|---|
| `spaarke-legal-g5-area-test` (app id `f7af4143-02b0-4912-b3ee-e73b0e202422`, v1.0.0) | App package: one declarative agent (v1.6), one MCP action on `/mcp/legal-operations`, A1 `reference_id`, dynamic discovery | specialist-legal-agents-r1 | Owner + `demo@`, org catalog, never All users (R7) | Owner removes it in the admin center at the Phase 0 clean-up; coordination row closed after | at task 031 |

## Prompt set

All prompts are typed into **Copilot Chat** with the agent selected, one per new chat. Text in square brackets is a short synthetic inline passage the test user pastes (see "Inline passages"). Expected is the function tool the BFF should log, or **none**.

### In-scope: NDA review (12)

| ID | Prompt | Expected |
|---|---|---|
| N-01 | Review this NDA for us: [passage P1] | NDA review |
| N-02 | Is this confidentiality agreement acceptable as written? [passage P1] | NDA review |
| N-03 | Check the term and survival clause of this NDA against our position: [passage P2] | NDA review |
| N-04 | A vendor sent us a mutual NDA. Run it through our standard NDA review: [passage P1] | NDA review |
| N-05 | Does this non-disclosure agreement have a residuals clause problem? [passage P3] | NDA review |
| N-06 | Please triage this NDA green, yellow or red: [passage P1] | NDA review |
| N-07 | Look at the definition of Confidential Information in this NDA and tell me if it is too broad: [passage P2] | NDA review |
| N-08 | We are the disclosing party on this one-way NDA. Review it: [passage P3] | NDA review |
| N-09 | Can you do an NDA review on the agreement I pasted? [passage P1] | NDA review |
| N-10 | Quick check of this confidentiality agreement's governing law and venue: [passage P2] | NDA review |
| N-11 | Run the NDA playbook on this: [passage P3] | NDA review |
| N-12 | Our counterparty wants a non-solicit inside their NDA. Review the NDA: [passage P2] | NDA review |

### In-scope: agreement review (12)

| ID | Prompt | Expected |
|---|---|---|
| A-01 | Review this services agreement against our standard positions: [passage P4] | Agreement review |
| A-02 | Check the limitation of liability in this master agreement: [passage P4] | Agreement review |
| A-03 | What are the risks in this software license agreement's indemnity clause? [passage P5] | Agreement review |
| A-04 | Review this vendor agreement and list deviations from our position on payment terms: [passage P4] | Agreement review |
| A-05 | Please review the termination and renewal terms of this contract: [passage P5] | Agreement review |
| A-06 | Run an agreement review on this supply agreement: [passage P5] | Agreement review |
| A-07 | Is the warranty disclaimer in this license agreement acceptable? [passage P5] | Agreement review |
| A-08 | We are the customer. Review the data protection clause of this services agreement: [passage P4] | Agreement review |
| A-09 | Compare this contract's assignment clause with our fallback position: [passage P4] | Agreement review |
| A-10 | Review this consulting agreement and flag anything unusual: [passage P5] | Agreement review |
| A-11 | Can you do a first-pass review of this commercial agreement? [passage P4] | Agreement review |
| A-12 | Check this agreement's governing law, dispute resolution and notice clauses: [passage P5] | Agreement review |

### In-scope: legal hold review (12)

| ID | Prompt | Expected |
|---|---|---|
| H-01 | Review this legal hold notice for completeness: [passage P6] | Legal hold review |
| H-02 | Does this litigation hold cover the right custodians and date range? [passage P6] | Legal hold review |
| H-03 | Check whether the preservation instructions in this hold notice are clear: [passage P7] | Legal hold review |
| H-04 | Run the legal hold review on this draft notice: [passage P6] | Legal hold review |
| H-05 | We got a demand letter. Review our draft hold notice to employees: [passage P7] | Legal hold review |
| H-06 | Is this hold notice missing anything about auto-delete settings and personal devices? [passage P7] | Legal hold review |
| H-07 | Please review this preservation notice before we send it: [passage P6] | Legal hold review |
| H-08 | Check this legal hold for the acknowledgement and release provisions: [passage P7] | Legal hold review |
| H-09 | Does this hold notice name the matter and the data sources properly? [passage P6] | Legal hold review |
| H-10 | Review the scope of this litigation hold, we think it is too narrow: [passage P7] | Legal hold review |
| H-11 | Can you do a legal hold review of the text I pasted? [passage P6] | Legal hold review |
| H-12 | Our hold notice went out last month. Review its wording again: [passage P7] | Legal hold review |

### Out-of-scope controls (12): no function may be invoked

| ID | Prompt | Why it is a control | Expected |
|---|---|---|---|
| C-01 | Draft a patent claim for a self-adjusting bicycle brake. | Patent drafting is not a function of this agent | none |
| C-02 | Is the name "Brightlane" likely to be confusable with other marks for software? | Trademark clearance, not a function | none |
| C-03 | What is the capital of Australia? | Unrelated general question | none |
| C-04 | Summarize the following paragraph in two sentences: [passage P8] | Plain summarisation, no review | none |
| C-05 | Write a polite email asking a colleague to reschedule tomorrow's meeting. | Unrelated drafting | none |
| C-06 | Show my invoices that are waiting for approval. | Another surface (decisions or invoices), not a review function | none |
| C-07 | Explain in plain English what "force majeure" means. | Legal definition question, no document to review | none |
| C-08 | Draft a new mutual NDA from scratch for a software pilot. | Drafting, not review; this agent reviews, it does not draft | none |
| C-09 | What is the difference between a legal hold and a retention policy? | Explanation, no document to review | none |
| C-10 | Translate this sentence into French: "The parties agree to meet on Tuesday." | Translation | none |
| C-11 | Schedule a reminder for me to renew my domain name in March. | Unrelated task | none |
| C-12 | Give me three tips for preparing for a deposition. | General legal advice, no function | none |

> **Why C-08 and C-09 are included.** They sit next to real functions (NDA, legal hold). A router that fires on keywords instead of intent will call a function here. They are the controls most likely to fail, which is the point.

### Inline passages (synthetic, to be written for this test)

The test user pastes these. Each is a short fictional clause set with invented parties, under about 120 words, so that no attachment or upload is needed. **Written by task 020 and saved next to this file as [`passages.md`](passages.md)** (P1 to P8, fictional only). Paste the passage text exactly as written there, and record any change in the results sheet notes.

| Passage | Content to write |
|---|---|
| P1 | Mutual NDA between two invented companies; 3-year term; standard exclusions; one governing law |
| P2 | NDA clauses: very broad definition of Confidential Information; 7-year survival; non-solicit; venue in a distant court |
| P3 | One-way NDA with a residuals clause and no return-or-destroy duty |
| P4 | Services agreement: uncapped liability, net-90 payment, broad vendor assignment right, a data protection clause |
| P5 | Licence or supply agreement: one-sided indemnity, auto-renewal with 120-day notice, warranty disclaimer, mixed governing law |
| P6 | Short legal hold notice: matter name, custodian list, date range, no acknowledgement step |
| P7 | Narrow hold notice: no mention of auto-delete, personal devices or chat tools; no release provision |
| P8 | A neutral two-sentence paragraph about a fictional company's quarterly plan (not a legal text) |

Passages use invented parties only. They must not copy any real agreement or any text from the accelerator's reference material.

### Run order

Interleave so that no function's prompts run back to back: N-01, A-01, H-01, C-01, N-02, A-02, H-02, C-02, and so on, one of each kind in turn; the remaining controls go at the end of each cycle. The tester records the actual order in the results sheet. Every prompt is a **new chat with the agent selected**; a prompt in plain Chat without the agent is not part of this test.

## Results sheet

Make one copy per run: **Baseline (Legal Review)** and **Main (area agent)**. Record 48 rows each (36 in-scope plus 12 controls).

| Prompt ID | Run | Surface | Time (UTC) | Expected | Observed function tool (App Insights `tools/call` name, or "none") | Calls this turn | Pass / fail | Returned instructions followed? (y / n / n.a.) | App Insights evidence (query id, row time) |
|---|---|---|---|---|---|---|---|---|---|
| N-01 | | Chat | | NDA review | | | | | |
| N-02 | | Chat | | NDA review | | | | | |
| *(one row per prompt ID, N-01 to C-12)* | | | | | | | | | |

Rules for filling it:
- **Pass** for an in-scope prompt: the expected tool, once, and no other function tool.
- **Pass** for a control: no function tool call at all.
- Requests are sampled at about 25%, and the BFF logs no caller identity (project CLAUDE.md §5). Attribute by tester and time. For a prompt with no visible row, **re-run it as an extra row** with the same wording in a fresh chat, and use a row that is visible. A control passes only if the agent's own trace shows no function call **and** App Insights shows none for that minute; sampling can hide a call, so "no row" alone is not a pass.
- Use the PowerShell App Insights query in `phase0/g8/README.md`.

### Summary (fill in after both runs)

| Run | In-scope correct / 36 (baseline: / 24) | Routing % | Controls with a function call / 12 | G5 pass? | Notes |
|---|---|---|---|---|---|
| Baseline: Spaarke Legal Review | | | | | |
| Main: area agent | | | | | |

Also record: where the agreement prompts went on the baseline (A-01 to A-12), and the share of prompts where returned instructions were followed (FR-05).

## Result and fallback

- **Pass (main run):** routing at least 95% and 0 functions on controls. One area agent with functions inside it is viable in Chat for D-A12. Record it in `notes/decisions.md` and the go/no-go record.
- **Fail (main run):** the front door falls back to **one agent per function, with the persona kept server-side as profile only** (design §3.9, FR-06). Record:
  - which prompts missed, and whether the misses cluster (for example agreement review taken by the NDA function);
  - whether sharper instructions or tool descriptions fix it. That is a new run on a fresh app id, scored by the same rule, and the first run stays in the record;
  - whether the baseline did better or worse, which shows if the miss comes from the shape or from the instructions.
- **Baseline** has no pass consequence. It shows how the existing two-function agent behaves and gives the comparison point for the main run.

## Clean-up

When the result is recorded:
1. The owner removes `spaarke-legal-g5-area-test` from the admin center.
2. This session tells COPILOT-PLATFORM-COORDINATION it is gone (R4 row closed).
3. The baseline agent is left untouched.
