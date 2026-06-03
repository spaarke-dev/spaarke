# R5 User Testing Kickoff — Scoping + Session Prompt

> **Purpose**: This doc is the launchpad for the new session that scopes R5 user testing. Read it FIRST in the new session, then `ground-truth-spaarkeai-state.md`, then start scoping.
> **Created**: 2026-06-03

---

## How to use this doc

1. Start a new Claude Code session in `C:\code_files\spaarke`.
2. Paste the **"Recommended kickoff prompt"** below as your first message.
3. Claude loads the listed documents as primary context.
4. Claude then asks scoping questions from §"Scoping questions" before producing artifacts.
5. After scoping is settled, work proceeds: design.md OR a working notes pack, plus test instrument(s).

---

## Recommended kickoff prompt

Paste this verbatim:

```
We're starting R5 (spaarke-ai-platform-unification-r5) as a user testing +
feedback capture round for SpaarkeAi functional requirements.

Load these documents as primary context BEFORE doing anything else:

1. projects/spaarke-ai-platform-unification-r5/README.md
2. projects/spaarke-ai-platform-unification-r5/notes/ground-truth-spaarkeai-state.md
   (READ THIS FIRST — code-grounded survey of what's actually shipped in
   SpaarkeAi as of R4 merge. This is the truth, not aspirations.)
3. projects/spaarke-ai-platform-unification-r5/notes/user-testing-kickoff.md
   (this file — the scoping questions are here)
4. docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md (the two-wrapper
   architecture canonical doc; required reading)
5. projects/spaarke-ai-platform-unification-r4/spec.md (the 14 FRs / 9 NFRs /
   7 DRs / 2 PRs shipped 2026-06-03)
6. projects/spaarke-ai-platform-unification-r4/notes/lessons-learned.md
   (what shipped, what surprised, what was deferred — R5 backlog items)

After loading: do NOT generate artifacts yet. Ask the scoping questions
from the kickoff doc one at a time and capture my answers. Once scope is
settled, propose the deliverable shape and we'll proceed.
```

---

## Scoping questions (Claude asks these one at a time)

### Q1 — Scope of surfaces

**Question**: "Which SpaarkeAi surfaces are in scope for testing?"

Reference §7 of `ground-truth-spaarkeai-state.md` — "High readiness" surfaces are the realistic candidates today.

Options to consider:
- a) **Workspace pane only** — dropdown, auto-install, pinned, tab persistence, custom layouts
- b) **Workspace + Attachment uploads** — adds the 25 MB cap testing
- c) **All three panes (Workspace + Assistant + Context)** — adds chat/playbook + context cards. Note: Assistant→Workspace and Context→Workspace mount sources are **partial**, so this scope must accept "infrastructure-only" caveats for FR-02 + FR-03
- d) **A specific user task end-to-end** — e.g., "complete a Create Matter playbook from intent to result" — cuts across all surfaces but follows one workflow

**Why it matters**: determines test script breadth. (a)/(b) → 1-session study. (c) → multi-session. (d) → guided task-flow study.

---

### Q2 — Audience

**Question**: "Who is testing?"

Options:
- a) **Internal team / dogfood** — Spaarke staff; low setup, can do think-aloud freely
- b) **Pilot customers** — needs NDA, scheduling, possibly recording consent
- c) **Both, sequentially** — internal first, then pilot
- d) **A specific role** — e.g., paralegals, attorneys, ops staff — narrows scenarios

**Why it matters**: drives recruitment, NDA needs, feedback format, level of polish required for the instrument.

---

### Q3 — Test type

**Question**: "Are you testing what's shipped, or piloting a new flow?"

Options:
- a) **Discoverability + correctness on shipped state** — "can users find X? does it behave as expected?" — exploratory, no scripted tasks
- b) **Guided task-flow with think-aloud** — scripted tasks, observation, post-task interview
- c) **A/B comparison** — comparing two implementations (R4 vs R3 surface? new flow vs legacy?)
- d) **Concept validation** — testing W-4/W-5 mockups before building the dispatcher side

**Why it matters**: drives instrument design (open observation vs scripted), session length, analyst burden.

---

### Q4 — Feedback capture destination

**Question**: "Where does feedback land structurally?"

Options:
- a) **A new project doc in R5** — `notes/findings.md` synthesized post-test; flows into R6/backlog
- b) **GitHub issues** — one issue per finding tagged `r5-feedback`; tracked alongside code work
- c) **External tracker** — Notion, Confluence, Jira, Linear — needs URL/access setup
- d) **Mixed** — high-severity → issues; everything → R5 findings doc

**Why it matters**: determines the capture artifact template and the post-test synthesis workflow.

---

### Q5 — Definition of done

**Question**: "What's 'done' for R5 user testing?"

Options:
- a) **Just a testing plan + instrument** — operator/team runs sessions themselves
- b) **Plan + instrument + executed sessions + raw notes** — Claude helps run, transcribe (if no NDA blocker)
- c) **Plan + instrument + sessions + synthesized backlog** — full study including findings → ranked backlog
- d) **All of (c) + scoped follow-on project for fixes** — `r6` or specific cleanup projects

**Why it matters**: determines effort scope. (a) ~1 day. (d) → weeks-long study + scoping work.

---

### Q6 — Cadence

**Question**: "Is this a one-time study, recurring iterations, or ongoing operational practice?"

Options:
- a) **One-time study** — ship findings → R5 closes
- b) **Recurring per release** — R5 becomes a template; reusable instrument
- c) **Ongoing operational practice** — embed user-testing into the project lifecycle (every R-round closes with a study); needs durable infrastructure

**Why it matters**: determines whether deliverables are one-off documents or reusable infrastructure (scripts, templates, capture forms).

---

## After scoping — likely deliverable shapes

| Scope answer | Likely deliverables |
|---|---|
| Internal, shipped state, discoverability, R5 findings doc, one-time, plan + sessions | `design.md` (light) + `notes/test-script.md` + `notes/capture-template.md` + `notes/findings.md` (post-test). No `spec.md` needed. |
| Pilot customers, guided task-flow, NDA + recording, GitHub issues, plan + sessions + synthesized backlog + R6 project | Full `/design-to-spec` pipeline → `spec.md` → tasks/ POML files (likely 10–15 tasks: recruit, schedule, run, analyze, file issues). |
| Ongoing operational practice | `design.md` framed as a template; `docs/procedures/user-testing.md` published; reusable Notion/Excel template; integration into `task-execute` skill's wrap-up flow. |

Claude will propose the specific shape after Q1–Q6 answered.

---

## Pre-loaded context check — verify in the new session

After Claude loads context, ask it to confirm by stating:

- The current shipped count of registered workspace widgets (expected: 16 + 1 R4 W-4 demo = 17 registered, but DocumentViewerWidget is dispatcher-incomplete)
- The four documented mount sources + which two (W-4, W-5) are partial
- The three R5-backlog items deferred from R4 (Kiota, BFF test cleanup, iframe-wizards pattern impl)

If Claude can't answer these from the loaded docs, the context isn't loaded — stop and reload before continuing.

---

*This file is a launchpad. Once the new session starts and scoping settles, the actual project artifacts (design.md / spec.md / tasks/) take over as the source of truth.*
