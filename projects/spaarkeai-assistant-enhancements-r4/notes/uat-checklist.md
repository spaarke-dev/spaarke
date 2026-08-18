# R4 UAT Checklist — spaarkeai-assistant-enhancements-r4

> **Env**: dev / spaarkedev1 · **Deployed**: 2026-08-18 (BFF 44.96 MB + advisory `list-tasks` Action seeded + `sprk_spaarkeai` code page published)
> **How to use**: run each step in the SpaarkeAi Assistant, mark ✅/❌, jot notes. Maps 1:1 to the 7 spec Success Criteria.
> **Precondition**: sign in as a user who **has some open assigned tasks** (so grounded counts are non-empty) + a populated Daily Briefing.

---

## P1 — Grounded task-agenda (E1 / FR-01,02,03) — *the headline fix*

| # | Do this | Expect | Result | Notes |
|---|---------|--------|--------|-------|
| 1.1 | In the Assistant, type **"what do I need to do today"** | A **grounded summary** — real open-task count + the few most pressing items (name + due date), each traceable to a record; NOT a thin "I opened your task list" ack | ⬜ | |
| 1.2 | Same turn | A short **recommendation** ("I'd start with the 2 due today…") that follows from the data | ⬜ | |
| 1.3 | Same turn | The **Tasks tab opens** (workspace grid) — and only **one** tab (no duplicate) | ⬜ | |
| 1.4 | Read the numbers/names in the answer | Every count/name/date is **real** (matches the grid) — **nothing fabricated**; if you have zero tasks it says so honestly | ⬜ | |
| 1.5 | Try variants: **"show me my tasks"**, **"help me prioritize my tasks"** | Same grounded-summary behavior (this is the old P2 dead-end utterance — it must now work) | ⬜ | |

## P2 — No dead-end follow-ons + OBO identity (E2 / FR-04,05,06)

| # | Do this | Expect | Result | Notes |
|---|---------|--------|--------|-------|
| 2.1 | After the P1 answer, look at the suggested follow-on **chips** | Every chip either **does something real** when clicked or isn't shown — **no chip that promises an unwired action** | ⬜ | |
| 2.2 | Anywhere in the task-agenda / prioritize flow | The Assistant **never asks for your user id or name** (it knows you over OBO) | ⬜ | |
| 2.3 | With **Daily Briefing tab CLOSED**, run P1 | A **"Open Daily Briefing"** follow-on card appears; clicking it opens the Briefing tab | ⬜ | |
| 2.4 | With **Smart To Do tab CLOSED**, run P1 | A **"Open Smart To Do"** follow-on card appears; clicking it opens that tab | ⬜ | |
| 2.5 | With **Daily Briefing already OPEN**, run P1 | Its follow-on card is **suppressed** (no card to open an already-open tab); no duplicate tab | ⬜ | |

## P3 — Feedback → preference loop (E3 / FR-07,08,09)

| # | Do this | Expect | Result | Notes |
|---|---------|--------|--------|--------|
| 3.1 | Tell the Assistant an explicit standing directive, e.g. **"always summarize my tasks"** | It's accepted/acknowledged as a standing preference (persisted for you) | ⬜ | |
| 3.2 | Start a **new session**, then run P1 (or just ask about your day) | The saved preference **biases** the default behavior (e.g. it summarizes proactively) | ⬜ | |
| 3.3 | Give an **off-list** directive (something outside the known preference set) | It has **no** effect on tool selection / capability grants (bounded — preference can't grant a capability) | ⬜ | |

## P4 / D9 — SprkChat viewport (E4 / FR-11) — *the layout fix*

| # | Do this | Expect | Result | Notes |
|---|---------|--------|--------|--------|
| 4.1 | Open the Assistant inside the **"Open in Compose"** dialog/iframe host | The transcript is **not clipped** at top/bottom; input box fully visible | ⬜ | |
| 4.2 | Hold a **long conversation** (scrolls) | No **dead whitespace** band; the **Refresh-suggestions footer row** is fully visible (not hidden under content) — *this was the residual clip you flagged* | ⬜ | |
| 4.3 | Resize the host / try **full-page + widget** hosts, **light + dark** | Layout holds in every host + theme; no clipped rows | ⬜ | |

## BFF hygiene (NFR) — informational (already verified at deploy)

| # | Check | Status |
|---|-------|--------|
| 5.1 | BFF publish ≤60 MB compressed | ✅ 44.96 MB |
| 5.2 | No new HIGH CVE | ✅ clean |
| 5.3 | compose-r7 identity-key guard = Active | ✅ exit 0 |

---

## Overall
- [ ] P1 grounded task-agenda works end-to-end
- [ ] P2 no dead-end chips + follow-on cards gate correctly + no identity prompt
- [ ] P3 preference loop persists + biases + stays bounded
- [ ] P4/D9 viewport clean in Compose host + long convo + all hosts/themes

**Feedback / issues found** (list → I'll triage each as fix-now vs new-issue vs known-better-owner):
1.
2.
3.
