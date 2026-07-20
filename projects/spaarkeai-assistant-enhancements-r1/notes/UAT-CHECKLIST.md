# SpaarkeAI Assistant — UAT Checklist

> Environment: **dev** (`sprk_spaarkeai` code page + `spaarke-bff-dev`). Branch/master: `a85fbb7c8`.
> Covers everything shipped in this project (P1/P2 + MA cluster + Chat-UX + UP-10 + Draft render/route + Files section + Context-pane + R4 batch + BFF history).
> ⚠️ = known-not-done (don't fail these — they're teed up for the next round).

## A. Assistant load / welcome
- [ ] Cold open shows get-started cards: **Summarize a document · Create a matter · Compose a document · More…** with clear whitespace above/below
- [ ] **"More…"** opens the Quick Start modal (full 7-card grid)
- [ ] Composer is noticeably taller (~6 rows) with **"Let's get started…"** placeholder on an empty chat
- [ ] The composer strip has **no slash/Prompt (sparkle) button**; typing `/` still opens the slash menu
- [ ] Get-started cards also appear on a **restored-but-empty** session (not just brand-new)
- [ ] Header order is **History icon · New session · ⋮ (vertical three-dots)**

## B. My Assistant (profile)
- [ ] Does **NOT** auto-open on load
- [ ] When profile incomplete: a dismissible **"Personalize your assistant"** nudge shows + a red-dot badge on the **⋮** Tools menu / "My Assistant" item
- [ ] ⋮ → My Assistant opens a **standard small 3-step modal**
- [ ] Step 1 — **Primary role** dropdown (populated) + **Primary work location** dropdown (Chicago / New York / Tampa)
- [ ] Step 2 — Practice areas + **Focus-area chips**
- [ ] Step 3 — **Preference chips**
- [ ] **No "Clear my profile"** button anywhere in the flow
- [ ] Save succeeds; reopening **prefills** prior selections; nudge + badge clear after save

## C. File attach
- [ ] Attach 1 file → tray shows **"1 file attached — {name} (1 indexed)"** inline
- [ ] Attach 2+ files → tray shows **"N files attached"** with a collapsible dropdown listing each filename
- [ ] Live **"Attaching file… / Classifying file…"** spinner shows while the composer is locked
- [ ] Attaching files does **NOT** auto-open Compose tabs
- [ ] **"Revise in Compose"** appears in the tray once indexed; clicking opens the file(s) in Compose (multiple → separate tabs)

## D. Post-upload chips + progress
- [ ] After classify, chips appear: **Summarize this file · Create a matter · Draft a response**
- [ ] A **"Working…"** spinner + composer lock shows while a chip capability runs (e.g. Summarize)

## E. Create a matter
- [ ] **Card/chip** path: drafts, then opens the Create Matter wizard **pre-seeded with the uploaded file** — no raw JSON dumped in chat
- [ ] **Natural-language** ("create a matter") path: same behavior (parity)

## F. Draft a response
- [ ] Renders a **readable draft** in chat (not raw JSON)
- [ ] Opens a **pre-filled Compose tab** with the drafted response (Subject / body / suggested recipients / sources)
- [ ] Chat shows a short **"opened in Compose"** confirmation (not the raw draft)

## G. Summarize files
- [ ] **"Working…"** spinner shows during summarization
- [ ] Summaries return and render readably

## H. Compose / Revise
- [ ] Natural-language **"revise this document"** still opens the file in Compose + applies a tracked edit
- [ ] Accept / Reject / Try-another / Keep-redline controls work on the suggested edit

## I. Context pane
- [ ] Opens on **Execution Trace** ("what's happening") by default — **not** the get-started cards
- [ ] Shows tool calls from the session ledger and updates as the assistant acts
- [ ] Context **Tools** dropdown lists **Execution Trace · Semantic Search · Pinned Memory** (no "Quick Start")

## J. History  ← newly fixed (BFF)
- [ ] Create 2–3 sessions (send a message in each), then open the **History (clock)** dropdown
- [ ] The dropdown **lists recent sessions** with a title + relative time (no longer "No recent conversations")
- [ ] Clicking a session **restores** it

## K. Create a project
- [ ] Assistant drafts a project, then launches the **Create Project wizard pre-seeded** (parity with create-matter)

## L. Regression sanity
- [ ] Daily Briefing / existing widgets still open
- [ ] New session (⋮/＋) clears the transcript and mints a fresh session
- [ ] Dark mode renders cleanly across the above

---

## ⚠️ Known not-yet-done (do not fail — next round)
- ⚠️ **R4-6** — after Draft-a-response, the **Send as email / Save to document / Create a matter** next-action cards are NOT implemented yet
- ⚠️ **R4-11** — post-summarize chip set is still **"Summarize again"** (replacement set pending your decision; catalog PATCH, no deploy)
- ⚠️ **R4-12** — Quick Start wizards do **NOT** yet carry the current conversation context (uploaded files)
- ⚠️ **R4-7 / R4-9** — the empty "Actions available for…" header + Context-consistency symptoms — re-capture the repro
