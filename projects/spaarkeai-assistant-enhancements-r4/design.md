# SpaarkeAI Assistant Enhancements R4 — Design

> **Status**: Design draft (seed for `/design-to-spec` → `/project-pipeline`)
> **Created**: 2026-08-13
> **Owner**: Ralph Schroeder
> **Predecessor**: `spaarkeai-assistant-enhancements-r3` (shipped + deployed to dev 2026-08-11 — awareness re-point, overview parity tools, per-item email/document cards, tool-economy PreFilter, registration contract)
> **Theme**: **From deterministic launcher to grounded proactive assistant** — close the gap between what the Assistant *promises* and what it *delivers*, and build the feedback loop that lets it improve.

---

## 1. Problem statement (from owner UAT, 2026-08-10 → 2026-08-13)

R3 made the Assistant *aware* of the workspace and gave it grounded parity tools. UAT on the deployed build surfaced that the Assistant still **under-delivers on its own promises** — three concrete failures, one root cause, plus a viewport defect:

- **P1 — "what do I need to do today"** → opens the Task widget and emits a thin *"I opened your task list"* with **no actual summary**. The user wants it to *also* produce a quick grounded summary and, optionally, open the **Daily Briefing** and **Smart To Do**.
- **P2 — the follow-on chip "Help me prioritize my tasks"** → the Assistant asked the user for their **user ID** (which the system already knows via OBO) and dead-ended. Follow-on chips frequently promise actions no wired capability fulfills.
- **P3 — the feedback loop question**: how does the Assistant *learn* (per-user and system-wide), and what should the user's role be in training it? "My Assistant" and "memory" exist — how do we make them a real loop?
- **P4 — D9 viewport clipping**: the Assistant pane's transcript does not fill the pane when SpaarkeAi is opened via a document's **"Open in Compose"** modal (Xrm dialog iframe) — content clips mid-row with dead whitespace below. Full-page + widget presentations are correct. (Handoff doc: `notes/assistant-viewport-clipping-open-in-compose-handoff.md`, compose-r6 defect **D9**.)

### The unifying root cause (P1 + P2)
Spaarke's AI is **deterministic-by-authoring**: behavior comes from closed-catalog Action + Binding rows (ADR-039), not emergent model behavior. That is correct for *not fabricating facts*. But the capabilities are currently authored at the **extreme-deterministic end**: e.g. the `list-tasks` Action is `allowstools=false` with a one-field `acknowledgement` output schema and a system prompt that **explicitly forbids the model from fetching, counting, or narrating anything** (`infra/dataverse/actions/list-tasks.action.json`). So the thin ack isn't a bug — it is *exactly what is authored*, and there is **no quick wording tweak that adds a summary**, because the capability structurally cannot call a tool or reason over data. Likewise, follow-on chips are suggestion-strings with no guaranteed backing capability, so "Help me prioritize" flails.

**This is the design tension the owner named (point 1): if every interaction path must be hardcoded to this degree, we have a deterministic workflow script, not an AI assistant — and we forfeit the core value of the model.**

---

## 2. The central thesis — separate *grounded facts* from *free recommendations*

ADR-039 (closed catalogs / no classifier / no second dispatch surface) exists to prevent two real failure modes: (a) the model **inventing capabilities** that don't exist, and (b) the model presenting **probabilistic output as fact**. Both are non-negotiable. **But neither requires forbidding the model from reasoning or recommending.** The fix is to draw the line in the right place:

| Dimension | Must stay deterministic / grounded | Where LLM latitude IS the value |
|---|---|---|
| **Capability existence** | The set of tools/actions is a closed catalog (ADR-039). The model cannot invent a capability. | — |
| **Facts / data** | Counts, records, dates, names come from **tools over OBO** (grounded), never the model's memory. | — |
| **Tool selection & chaining** | — | The model may **choose and chain** existing grounded tools (e.g. call `grid_overview` + `daily_briefing_overview`, then narrate) — *within* the closed catalog. |
| **Recommendations / guidance** | Never stated as fact; always framed as advice over grounded data. | The model **prioritizes, suggests, and proactively guides** ("based on your 3 overdue items, I'd start with X") — this is the AI value the owner wants. |
| **Follow-on suggestions** | Only offered if a wired capability backs them (no dead-end promises). | The model may *phrase* and *rank* the offered follow-ons. |

**Design principle for R4**: introduce a **"grounded-recommend" capability tier** — capabilities that (a) are allowed to call a bounded, allow-listed set of grounded tools, (b) reason over *only* the tool-returned data, and (c) may make recommendations clearly framed as advice. This keeps ADR-039's guarantees (closed catalog, no fabricated facts) while restoring proactive value. The current `allowstools=false` surface-launch tier remains for pure navigation; the new tier powers the daily agenda, prioritization, and proactive guidance.

> **ADR-039 relationship**: this is expected to be a **Path A/B tension** (per CLAUDE.md §6.5) — either a project-scoped exception documenting the grounded-recommend tier, or a small ADR-039 amendment clarifying that "closed catalog" governs *capability existence and fact-grounding*, not *reasoning latitude over grounded results*. `/design-to-spec` must surface this explicitly.

---

## 3. Enhancement areas

### E1 — Proactive grounded assistance (fixes P1)
Author a **daily-agenda capability** in the grounded-recommend tier that, for intents like "what do I need to do today":
1. Calls R3's already-shipped grounded tools — `spaarke.grid_overview(My Tasks configId)` (OBO, server `today`) and `spaarke.daily_briefing_overview` (wraps `BriefingService`) — both already in the same per-turn tool catalog.
2. Narrates a **grounded** summary (counts, top items) **with citations**, never fabricated.
3. Launches the relevant surfaces — Tasks + optionally **Daily Briefing** + **Smart To Do** — via the `surfaceLaunchRegistry` (one entry per surface; ASSISTANT-SURFACE-LAUNCH-MECHANISM).
4. Offers a genuine recommendation ("I'd tackle the 2 due today first").

**Reuse**: the tools + surface-launch mechanism already exist (R3 + surface-launch registry). New work = the grounded-recommend Action/Binding authoring + the tier that permits bounded tool use, + eval cases.

### E2 — Follow-on chips that actually work (fixes P2)
- **Capability-backed follow-ons**: a follow-on chip renders **only** when a wired Action/tool fulfills it (extend R3 task-041's deterministic-from-registration discipline from per-item cards to *query* chips). No dead-end promises.
- **Kill the OBO identity dead-end**: every user-scoped tool description must assert *"returns the calling user's own records over OBO; never ask the user for their id or name."* (Small, safe defect-fix — see §6 candidate quick wins.)
- Chips that map to a grounded-recommend capability (E1 tier) rather than an unbacked string.

### E3 — The learning / feedback loop (P3) — the two-destination model
Grounding: memory is wired end-to-end (`memory.write` → `IMemoryItemStore`/Cosmos → retrieved into `userFragment` every turn via `ContextBinder`), and "My Assistant" (`sprk_userprofile.sprk_assistantpreferences`) is read into every prompt — **but** (a) the stated preference is *advisory-only by design* (injection defense, does not steer tool selection), (b) there is **no "preference" fact type**, and (c) the thumbs/feedback subsystem (`FeedbackService` → `feedback` Cosmos container) is **reporting-only and never feeds back** into memory or behavior. That disconnect is the loop to build.

| Signal | Destination | Governance |
|---|---|---|
| "For **me**, always summarize + open my briefing" | Per-user preference → memory / My Assistant → applied per-turn | Automatic, **bounded** (a preference may bias/hint, and — new — trigger an *allow-listed* grounded capability; it may never grant a capability or change a fact) |
| "This response was bad / everyone should get X" | Aggregated feedback → **operator review queue** → catalog authoring | **Human-in-the-loop** (deliberate; ADR-039 forbids auto-mutating the global catalog) |

**Build items**:
- **Feedback → memory pipeline** (the missing link): a thumbs-down + comment (or an explicit "do this every time") writes a governed `preference` memory item.
- **`preference` fact type** + a **governed preference-producer** that converts an *allow-listed* standing directive ("always summarize my tasks") into a **pre-turn tool hint** — the one place a stated preference is permitted to influence tool selection (bounded, ADR-015/injection-defense preserved).
- **Operator promotion queue**: wire the existing feedback aggregates into a review surface so recurring gaps are promoted **once** into catalog authoring for everyone.
- **Eval-case guardrail** on every behavior change (maker-guide obligation) to prevent regressions.

### E4 — D9: Assistant viewport clipping in "Open in Compose" modal (fixes P4)
Client-only flex-chain defect. Per the handoff doc: the app/shell height chain is correct; the break is **below the shell** in the `ConversationPane → SprkChat` subtree — a wrapper missing `flex:1; min-height:0` (or a measured height taken before the Xrm dialog iframe settles). Same `ThreePaneShell` mounts in modal + full-page since compose-r1 task 092, so **one flex-chain fix is host-proof**. Needs one live-DOM session to name the exact element (diagnosis recipe in the handoff doc §4); fix pattern in §5. Ships with a `sprk_spaarkeai` rebuild + `Deploy-SpaarkeAi.ps1` — no BFF, no coordination window. Verification checklist in the handoff doc §6 (modal + full-page + widget + resize + long/empty conversation, light/dark).

---

## 4. What exists vs what R4 builds (honest inventory)

| Capability | Exists (R1–R3 + redesign-r2) | R4 builds |
|---|---|---|
| Grounded overview/briefing tools | ✅ `GridOverviewHandler`, `DailyBriefingOverviewHandler` (R3) — callable, in-catalog | Author capabilities that **chain + narrate** them (grounded-recommend tier) |
| Surface launch (open widgets/tabs) | ✅ `surfaceLaunchRegistry` + `handleSurfaceLaunch` | Add Daily Briefing / Smart To Do launch entries; multi-surface launch |
| Memory store + recall | ✅ `memory.write` → store → `userFragment` recall (redesign-r2) | `preference` fact type; feedback→memory pipeline; governed preference-producer |
| "My Assistant" stated profile | ✅ `sprk_userprofile` → `userFragment` (advisory-only) | Allow-listed standing directives that *steer* a bounded grounded capability |
| Thumbs / feedback | ✅ `FeedbackService` (reporting aggregates only) | Feedback→memory ingestion + operator promotion queue |
| Deterministic per-item follow-ons | ✅ R3 task 041 (per-item cards) | Extend to **query chips** (capability-backed) |
| Assistant pane layout | ✅ full-page + widget correct | D9 host-proof flex-chain fix (modal iframe) |

**The headline**: most of E1/E2's *capabilities* already exist — R4 is largely **authoring + a new capability tier + the feedback loop**, not net-new tools. That is why the owner's instinct ("shouldn't need to hardcode everything") is right: the missing piece is *permitting bounded reasoning over existing grounded tools*, not building more tools.

---

## 5. The feedback-surfacing process (answers P5)

**Question**: do we just do comprehensive UAT / use-case review and document when Spaarke AI misbehaves? **Answer**: yes, but make it a *lightweight standing loop*, not a one-time audit — because behavior gaps are continuous and route to two different fixes.

Proposed process (to formalize in R4):
1. **Capture** (any source): operator UAT, a user's in-conversation thumbs-down/comment, or an observed dead-end. Each becomes a **behavior-gap record** with: the exact user turn, what the Assistant did, what was expected, and the surface (which Action/Binding/tool). *(This design doc's P1–P4 are the first four such records.)*
2. **Triage → destination**:
   - *Per-user preference* ("I want it this way") → My Assistant / memory (individual).
   - *Systemic gap* ("everyone hits this") → the operator promotion queue → catalog authoring (global).
   - *Defect* (crash/clip/dead-end) → normal bug/defer track.
3. **Author + eval**: fix via config (Action/Binding) or code; **every AI-behavior change lands with an eval case** so the gap can't silently regress.
4. **Measure**: the feedback aggregates + eval suite show whether the gap closed.

**Tooling to consider in R4**: a standing "Assistant behavior-gap" register (like R3's `defer-issues.md` but behavior-focused), fed by both UAT and the in-product feedback subsystem, with a periodic operator review that promotes recurring items. This *is* the feedback loop — UAT is one input, not the whole mechanism.

---

## 6. Candidate quick wins (config / minimal — deployable without the full R4 pipeline)

Honest scoping: the P1/P2 *enrichments* are **not** quick config (the `allowstools=false` design proves it) — they need the grounded-recommend tier + eval cases, i.e. R4. The genuinely-minimal items:

- **QW1 — OBO identity wording** (safe defect-fix): add to `GridOverviewHandler` (+ its byte-equal JSON row; the D-4 parity test now guards this) and other user-scoped tool descriptions: *"returns the calling user's own records over OBO; never ask the user for their identity."* Reduces the P2 dead-end whenever the tool is selected. Cost: 1 string in C# + JSON + re-seed + BFF redeploy. Low regression risk.
- **QW2 — D9 flex-chain fix** (E4): client-only CSS; needs one live-DOM session to name the element, then a `min-height:0; flex:1` correction + `sprk_spaarkeai` redeploy. Self-contained, host-proof.

Everything else (daily-agenda grounded-recommend capability, capability-backed follow-on chips, the feedback loop) is R4 project scope.

---

## 7. ADR Tensions (per CLAUDE.md §6.5 — to be finalized in spec)

| ADR | Rule challenged | Tension | Likely path |
|---|---|---|---|
| **ADR-039** | Closed catalog / deterministic dispatch / no classifier | The grounded-recommend tier lets the model chain tools + recommend over grounded data — more latitude than the current `allowstools=false` surface-launch tier | **A (project-scoped exception) or B (amendment)** — clarify that "closed catalog" governs capability *existence* + *fact-grounding*, not *reasoning latitude over grounded results*. **This is R4's defining decision — surface it first.** |
| **ADR-015** | Data governance; injection defense; preferences are advisory | A preference-producer that steers a bounded grounded capability crosses from advisory to (bounded) directive | **A** — allow-listed directives only; a preference may hint/trigger an allow-listed grounded capability, never grant a capability or alter a fact |
| **ADR-047** | Reactive card surface distinct from notification spine | Proactive daily-agenda suggestions must stay reactive/local, not become a push channel | **C (comply)** |

---

## 8. Scope boundaries

**In scope**: the grounded-recommend capability tier + daily-agenda capability (E1); capability-backed follow-on chips + OBO wording (E2); the feedback loop — memory ingestion, `preference` type, governed preference-producer, operator promotion queue (E3); D9 viewport fix (E4); the behavior-gap process (P5).

**Out of scope (for now)**: free-roaming agent behavior outside the closed catalog; auto-mutation of the global catalog from model/user activity (stays human-in-the-loop); memory trust/provenance enforcement (separate governance project); net-new grounded tools beyond what E1 needs.

**Coordinate with**: `spaarke-ai-architecture-redesign-r2` (sole owner of `Services/Ai/` internals — consume `PublicContracts/`, no fork); `spaarkeai-compose-r5/r6` (D9 originates there; `ConversationPane`/`ThreePaneShell` shared); the memory subsystem owner (redesign-r2) for the `preference` fact type + preference-producer.

---

## 9. Open questions for `/design-to-spec`

1. **The ADR-039 decision (§2/§7)** — exception vs amendment for the grounded-recommend tier. This gates everything; resolve first.
2. How far may a per-user preference steer behavior (E3 preference-producer) before it violates injection-defense? Define the allow-list shape.
3. Daily-agenda surface set — always open Tasks+Briefing+SmartToDo, or make it a per-user preference (E3)?
4. Operator promotion queue — new lightweight surface, or extend the existing feedback-aggregate reporting?
5. D9 — batch the fix into R4, or fast-track as a standalone client-only hotfix ahead of the pipeline (QW2)?

---

*Design seed authored 2026-08-13 from R3 deployment UAT. Next: resolve §9 Q1 (ADR-039), then `/design-to-spec projects/spaarkeai-assistant-enhancements-r4`.*
