# Phase 0 test G6: do Spaarke functions and Microsoft Legal for Cowork capture each other's tasks?

> **Program:** specialist-legal-agents-r1 (spec FR-07, design D-A13). G6 subsumes the accelerator's **T-P2**.
> **Throwaway test.** Nothing here is installed, uploaded or changed by Claude. All prompt content is synthetic.
> **Who does what:**
> - **The owner** switches on Legal for Cowork and runs or supervises the prompts (tenant and admin clicks are the owner's only).
> - **The owner or the test user** sends the prompts and records the results sheet.
> - **A Claude session** only authors this kit and later summarises the sheet.
> - Results go to accelerator-r1 (task 024).

## The question

When Spaarke's specialist functions and Microsoft's Legal for Cowork plugin are both available, does each task reach its **owner**?
- A Spaarke-owned task (for example an NDA review against the Spaarke playbook) must not be taken by a Microsoft skill.
- A Microsoft-owned task (for example an MSA review, a redline summary) must not be taken by a Spaarke function.
- Can a Spaarke agent **invoke** Microsoft's `review-contract` and `summarize-redlines`, or only **recommend** them?
- Is an exported test playbook honoured by `review-contract` and by Word's **Run Review**?

## Source of the base prompt set (T-P2)

- Source: `origin/work/spaarke-legal-ai-accelerator-r1:projects/spaarke-legal-ai-accelerator-r1/notes/component-protocol.md`, section 8, row **T-P2**.
  Read with `git show` only. The accelerator worktree is never opened.
- T-P2 states the test as: "With Legal for Cowork and Spaarke both on, does routing stay correct (NDA → ours, MSA → theirs, redline summary → theirs)? 5 prompts × 2 configurations in Cowork."
- The note holds the **expected routing** and the **count** (5 prompts, 2 configurations), but **no verbatim prompt text**. So prompts **T1 to T5** below are the five T-P2 prompts as this kit writes them, covering the three routings T-P2 names (NDA, MSA, redline summary). The main session logs this as a deviation in `notes/decisions.md`.
- **Two configurations** (as T-P2 counts them), defined for the G6 matrix:
  - **Config 1:** Legal for Cowork **on**, Spaarke surface **on** (the coexistence case).
  - **Config 2 (control):** only the owner of that task is on. It shows the intended owner can do the task at all, so a miss in Config 1 is capture and not a plain failure.
- G6 extends the five to **18 prompts** (T1 to T5, then X1 to X13), run in **Chat and Cowork**.

## Precondition (owner action)

1. The owner switches on **Legal for Cowork** for the owner and the test user only (R7). Record the date and the plugin version shown.
2. Spaarke specialist functions are available on the same surfaces (the G5 test agent or the spike tools, whichever Phase 0 has live). Record what is on.
3. If an R4 inventory row is needed for any tenant change, it goes to COPILOT-PLATFORM-COORDINATION (Issue #3006) first.
4. Inventory (T-P1 style): list the Microsoft skills' names and descriptions as shown, so each expected owner below is checked against what Microsoft actually ships. Note any skill missing or renamed.

## Method

- Run each prompt in a **new conversation**, in **Chat** and in **Cowork**, in Config 1. Run T1 to T5 also in Config 2.
- Use synthetic documents only. Put the same synthetic NDA text and synthetic MSA text in the conversation (or an attached file) so the input is identical across surfaces.
- Repeat each prompt **3 times** where a result differs between runs; record every run.
- Record, per run: the **owner reached** (Spaarke or Microsoft), the skill, function or tool named, and one line of evidence (the tool or skill label the UI shows, or the output's style).
- A prompt is **captured** when the owner reached is not the expected owner.
- For Spaarke-reached runs, check App Insights by tester and time (see `phase0/g8/README.md`; BFF sampling is about 25%, so use several turns).

## Prompt set and expected owner

Ownership rule: **Spaarke owns** NDA review and triage against a Spaarke playbook, and Spaarke-grounded matter, document and run tasks. **Microsoft owns** general contract review (`review-contract`), redline summary (`summarize-redlines`) and the other Legal for Cowork skills.

| ID | Prompt (synthetic) | Expected owner | Why |
|---|---|---|---|
| T1 | "Review this NDA against our playbook and list the deviations." (synthetic NDA attached) | Spaarke | NDA review is a Spaarke function |
| T2 | "Review this master services agreement and flag the risky clauses." (synthetic MSA) | Microsoft | MSA review is `review-contract` |
| T3 | "Summarize the changes in this redlined document." (synthetic redline) | Microsoft | `summarize-redlines` |
| T4 | "Is this mutual NDA acceptable to sign? Check the term and the governing law." | Spaarke | NDA check against the playbook |
| T5 | "Compare these two versions of the services agreement and tell me what changed." | Microsoft | Redline and compare |
| X1 | "Screen this one-way NDA for non-standard confidentiality exclusions." | Spaarke | NDA screening |
| X2 | "Review this software licence agreement and suggest edits." | Microsoft | General contract review |
| X3 | "Give me a plain-language summary of the redline in section 8 only." | Microsoft | Redline summary |
| X4 | "Which Spaarke playbook applies to this agreement?" | Spaarke | Spaarke playbook routing |
| X5 | "Review this vendor DPA clause by clause for a standard review." | Microsoft | General contract review |
| X6 | "Draft a short email to the counterparty explaining our NDA position on term length." | Spaarke | NDA position text from the Spaarke playbook |
| X7 | "Triage this inbound NDA as green, yellow or red." | Spaarke | NDA triage |
| X8 | "Run a review on this consulting agreement and give me a risk table." | Microsoft | General review (the Word Run Review equivalent) |
| X9 | "What did the other side change in their latest turn of this contract?" | Microsoft | Redline summary |
| X10 | "Find the matter this NDA belongs to and show its open tasks." | Spaarke | Matter data is Spaarke-grounded |
| X11 | "Summarise this NDA, then summarise the redlines in the attached MSA." (one prompt, two owners) | Split: NDA part Spaarke, redline part Microsoft | Tests handoff in one turn |
| X12 | "Review this employment contract." | Microsoft | General review; no Spaarke playbook |
| X13 | "Review this agreement." (no type given; the synthetic file is an NDA) | Spaarke | Ambiguous request; the file is an NDA, so the NDA owner should win. Record the owner reached and whether it asked first |

**Counts:** 18 prompts: 8 Spaarke-owned (T1, T4, X1, X4, X6, X7, X10, X13), 9 Microsoft-owned (T2, T3, T5, X2, X3, X5, X8, X9, X12), 1 split (X11). Each prompt runs in Chat and in Cowork: 36 shared-set runs in Config 1, plus 10 control runs (T1 to T5, both surfaces, Config 2).

Prompts T1 to T5 are the T-P2 set. Do not edit prompt text between surfaces.

## Probe P1: invoke or recommend

Question: can a Spaarke agent **invoke** Microsoft's `review-contract` and `summarize-redlines`, or can it only **recommend** that the user run them?

Steps, in Chat and in Cowork, Config 1:
1. Ask the Spaarke agent: "Review this MSA." (a Microsoft-owned task sent to a Spaarke agent). Record whether the agent: (a) runs `review-contract` itself, (b) says the user should run it, (c) tries to do the review itself, or (d) refuses or asks.
2. Ask the Spaarke agent: "Summarize the redlines in this document." Same four outcomes for `summarize-redlines`.
3. Ask the Spaarke agent: "Use the Microsoft review-contract skill on this MSA." Does an explicit instruction change the outcome?
4. Note the exact wording of the handoff and whether any skill label appears.

Reading the result:
- **Invoke** = the Microsoft skill runs and its output returns inside the Spaarke agent's conversation.
- **Recommend only** = the agent names the skill, and the user must start it.
- **Capture** = the Spaarke agent performs the Microsoft task itself (counts against the pass rule).

## Probe P2: exported test playbook

Question: is an **exported test playbook** honoured by `review-contract` and by Word's **Run Review**?

Setup (owner, synthetic content only):
1. Author a small test playbook, 3 to 5 topics, with a unique invented marker position per topic (for example a made-up limit such as "term no longer than 17 months"), so output that follows it is recognisable. Label the file `status: test`.
2. Export it in the format Legal for Cowork accepts (record the format used). Keep it in a library where the test user has Read access.

Steps:
1. **review-contract (Cowork):** point the skill at the exported playbook and review a synthetic MSA that breaches the marker positions. Record whether the output cites the marker positions.
2. **review-contract (Chat):** repeat.
3. **Word Run Review:** import the same playbook in Word, run Run Review on the same MSA. Record whether the marker positions drive the findings.
4. Control: run the same review with no playbook. Marker positions must not appear.
5. Record import errors, ignored topics and any position the skill rewrote.

Reading the result:
- **Honoured** = every marker position appears as a finding, correctly.
- **Partly honoured** = some do; list which.
- **Ignored** = none; the skill used its own default review.

## Results sheet

One row per run. Owner reached is `S` (Spaarke) or `M` (Microsoft); `-` for none.

| Run | Prompt | Surface (Chat/Cowork) | Config (1/2) | Expected owner | Owner reached | Skill/tool shown | Evidence | Captured? (Y/N) | Date, tester |
|---|---|---|---|---|---|---|---|---|---|
| 1 | T1 | | | S | | | | | |
| 2 | T1 | | | S | | | | | |
| ... | one row per run; 36 shared-set runs plus 10 control runs | | | | | | | | |

### Summary rows (fill after the runs)

| Measure | Chat | Cowork |
|---|---|---|
| Spaarke-owned prompts that reached Spaarke (of 8) | | |
| Microsoft-owned prompts that reached Microsoft (of 9) | | |
| X11 split handled correctly | | |
| Spaarke functions that captured a Microsoft task (count) | | |
| Microsoft skills that captured a Spaarke task (count) | | |
| Control (Config 2) runs that reached the intended owner (of 5) | | |

### Probe result rows

| Probe | Surface | Outcome (invoke / recommend / capture; honoured / partly / ignored) | Evidence (wording, screenshots, output) | Notes |
|---|---|---|---|---|
| P1 review-contract: invoke or recommend | Chat | | | |
| P1 review-contract: invoke or recommend | Cowork | | | |
| P1 summarize-redlines: invoke or recommend | Chat | | | |
| P1 summarize-redlines: invoke or recommend | Cowork | | | |
| P2 exported playbook, review-contract | Cowork | | | |
| P2 exported playbook, review-contract | Chat | | | |
| P2 exported playbook, Word Run Review | Word | | | |
| P2 control, no playbook | Cowork | | | |

## Pass rule (spec FR-07)

**G6 passes** only if, across the shared set (T1 to T5 and X1 to X13, both surfaces, Config 1):
1. **No Spaarke function captures a Microsoft-owned task**, and
2. **No Microsoft skill captures a Spaarke-owned one.**

Applying it:
- Any single captured run fails that direction. Repeats that differ all count, since unstable routing is not safe routing.
- X11 passes only if each half goes to its own owner.
- X13 is judged by the owner reached; a clarifying question first is recorded, not failed.
- Config 2 control failures are **not** capture; they mean the intended owner could not do the task. Re-run or reword the prompt before judging Config 1.
- Probes P1 and P2 do not pass or fail G6 by themselves, except that a Spaarke agent performing a Microsoft task itself (P1 capture) fails rule 1. They decide the **compose or recommend** shape (D-A13): if Spaarke can invoke the Microsoft skills, compose; if only recommend, recommend.

**On a fail:** record the prompt, the direction and the description text that probably caused it. Do not rewrite descriptions in this task. A description change is a new run with a fresh app id or about 45 minutes' wait (BR-2/BR-3).

## Reporting

- Send the filled sheet, summary rows and probe rows to **accelerator-r1** (task 024). G6 subsumes T-P2: T1 to T5 in Config 1 and Config 2 answer T-P2 directly.
- Record the compose-or-recommend finding for design D-A13.

## Constraints

- All prompts, documents and playbooks are synthetic or public. No real agreement, no client or counterparty names, and no held-back material from the accelerator.
- No tenant, Dataverse, Azure or BFF change is made by this kit. Owner clicks only.
- Remove the test playbook and any test file when the run is recorded.
