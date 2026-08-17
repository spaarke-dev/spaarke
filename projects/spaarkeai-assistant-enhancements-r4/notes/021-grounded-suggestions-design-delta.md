# Design Delta — FR-04: grounded, capability-backed follow-on suggestions

> **Status**: For owner review (design delta; no code written)
> **Author**: task-execute (task 021 investigation, 2026-08-17)
> **Supersedes the approach in**: task 021 as scoped ("gate the free-string `SprkChatSuggestions` client-side")
> **Nature**: FR-04 approach change — reshapes a client-only gate into a BFF+client consolidation onto the existing grounded proposer. Requires a spec note + task re-scope (CLAUDE.md §6.5).

---

## 1. Why this delta exists

Task 021 as written says "gate the free-string suggestions client-side so only backed ones render." Investigation showed that is the wrong shape:

- A plain SSE `suggestions[]` string carries **no structural backing signal**, so the client cannot tell a backed action-promise from a conversational follow-up from an unbacked promise **without a keyword heuristic** — which the task itself bans (`ASSISTANT-UI-ELEMENT-CRITERIA`; the escalation trigger fires here).
- The owner's requirement is stronger and correct: keep the LLM's contextual intelligence — *"understand the conversation and suggest things that make sense AND will actually work"* — while making a dead-end **structurally impossible**. You cannot script every scenario; that is the LLM's value.

The resolution is not "suppress vs bolt-on." It is: **the LLM selects from the real, context-scoped capability catalog and phrases it; the system guarantees the identity.** That is ADR-039 applied to suggestions — and it is **already built** for the proactive-tab moment.

---

## 1a. Owner decisions (2026-08-17) — resolves §9

1. **Question chips: KEEP them** (richer UX, safe by construction), styled as a distinct variant of ONE chip family — see §5a for the visual rules (the owner flagged that today the two styles look unrelated with no learnable reason).
2. **Cadence: ONE predictable grounded pass after every assistant turn.** Today's inconsistency ("sometimes suggestions, sometimes nothing") has three hidden causes in `ChatEndpoints` (below); the fix makes *absence meaningful* instead of random.
3. **Scope = the conversation session + the currently-active tab.** Candidate menu is scoped by the union of *open-tab* context-types + session/host; content grounding comes from the *active* tab. Multi-tab is NOT hard — the machinery exists (see §8a).
4. **Split into two closely-coordinated tasks: 021a (BFF) + 021b (client)** + a short `spec.md` FR-04 note.

**Why cadence is inconsistent today** (root cause, `Api/Ai/ChatEndpoints.cs`):
- **Keyword hijack** (`:977` `EmitMissingContextChipsIfNeededAsync`) — a document/upload keyword match emits `[action:…]` chips *instead* of suggestions (mutually exclusive).
- **<150-char skip** (`:1002`) — after a capability runs and the assistant gives a short ack, suggestions are skipped *entirely* (the exact moment you most want "what next?" goes silent).
- **2s timeout / failure** — silent skip.
Plus the grounded `/suggest` path is a *separate* proactive (tab-focus) trigger. Four triggers, four skip rules ⇒ feels random. **The code's own R7 comment (`ChatEndpoints.cs:990–998`, from user feedback 2026-06-10) already prescribes this delta's direction**: action-declared followups + a `followups` SSE event + click → capability dispatch by binding id.

---

## 2. The principle: separate the intelligence from the guarantee

| Job | Owner | Mechanism |
|---|---|---|
| **Intelligence** — what's relevant now, and how to phrase it | the LLM | reads the conversation + the *real* candidate menu, picks ≤3, writes a context-specific label |
| **Guarantee** — the suggestion will actually work | the system | the LLM only ever chooses from real, wired capabilities (structured selection over a closed set); a chosen id that isn't on the supplied menu is **dropped** |

The model authors the **words** (full creativity); it never authors the **routing target** for a capability (that's a real `bindingId` it selected). A suggestion for a capability that doesn't exist cannot be formed — there is no menu item for it.

**What stays finite:** the set of things the product can *do* (every capability = one catalog entry; you already maintain this). **What stays infinite (LLM-owned):** *which* capabilities matter this turn, and *how to phrase them* — and the menu itself re-computes every turn from the deterministic context pre-filter, so relevance tracks the conversation without scripting.

---

## 3. What already exists — the reference implementation

`Services/Ai/Chat/AssistantSuggestionService.SuggestAsync` (FR-B3/B5, behind `POST /api/ai/chat/sessions/{id}/suggest`) is exactly this pattern:

1. **Candidate menu** = `IConsumerRoutingService.ListTextProjectableBindingsAsync()` → `FilterByContextType(...)` — the loop-projectable capability catalog, deterministically pre-filtered (ADR-039 context scoping; the only permitted aid), capped at 25.
2. **The model is given the real menu with descriptions** — `BuildInput` emits `{ contextType, activeTab: {widgetType, content}, candidates: [{bindingId, description}, …] }`.
3. **One grounded turn** runs the maker-authored `SUGGEST-FOLLOWUPS` Action (`sprk_consumertype = "assistant-suggest"`) via the existing `IActionRunner` — no SprkChat fork, no new dispatch protocol, no new store.
4. **Closed-catalog guard** — `ParseSuggestions` keeps a suggestion **only if its `targetBindingId` is one of the supplied candidate ids**; hallucinated ids, blanks, and duplicates are dropped; capped at 3.
5. **Output** = `SuggestedChip(TargetBindingId, Label, Reason)` — a *proposal*; the chip rides the existing deterministic Click path on click.

This is the whole answer — the LLM proposes contextually, the system guarantees the id is real. It is just currently wired to the **focused-tab proactive** moment, not the **after-a-chat-response** moment.

---

## 4. The gap (and the token-budget clarification)

The dead-ends come from the **other**, older path: `ChatEndpoints.GenerateAndEmitSuggestionsAsync`, which runs after each chat response and:

- feeds the model **only** the last user message + the **first 500 characters** of the response (`ChatEndpoints.cs:3235`) — **no conversation history, and no list of what the system can do**;
- asks for "2–3 brief follow-up strings" as free text;
- emits them as the untyped SSE `suggestions: string[]` event.

**On the ~100-token question:** `MaxOutputTokens = 100` (`ChatEndpoints.cs:3238`) is the **output** cap — that's how much the model may *write*, and 3 short labels genuinely fit. The real deficiency is the **input**: the model is told almost nothing (last message + 500 chars) and, crucially, is **never shown the capability menu**, so it *cannot* make a backed suggestion even in principle — it's guessing follow-up text blind. That is the root of the dead-ends.

The grounded design inverts the emphasis correctly: **input context is generous** (the conversation slice the model needs to be relevant + the real candidate menu *with descriptions* + relevant grounded state), while **output stays small** (a structured ≤3-item selection). "Full context for what it is suggesting" is precisely what the grounded proposer already supplies and the legacy generator withholds.

---

## 5. Two honest kinds of suggestion

The "Help me prioritize my tasks" dead-end was a **category error** — an action-promise treated as a conversational re-prompt. The fix makes the two categories structurally distinct:

- **Capability suggestion** — "do something." Carries a real `targetBindingId` (the model selected it from the menu). Clicking dispatches that exact binding via the Click path. **Guaranteed to work.**
- **Question suggestion** — "ask the assistant something" (e.g. *"What are the risks in section 3?"*). Carries only text. Clicking re-enters the grounded agent loop, which is safe **by construction** (ADR-039 grounded outcomes: dispatch, a cited answer, a clarifying question, or an honest refusal — never a hard dead-end). Rendered visually as a question, never as an action.

The `SUGGEST-FOLLOWUPS` prompt instructs the model: *if the follow-on needs one of the offered capabilities, propose it as a capability chip with its bindingId; otherwise, if it's a question you can answer from context, propose it as a question.* Because the model is given the capability menu, it can make this split cleanly. Any bare, untyped "action-looking" free string is **never** rendered.

## 5a. Visual design — make the two kinds legible (owner point #1)

Today the two styles look like unrelated UI (bordered-arrow `ConsumerChips` vs light free-string pills) and the difference is an **accident of which pipeline drew them**, not what they do — so a genuine action ("Send the email to Jon James Wiley") renders as a conversational pill. The style must be driven by the **typed `kind` (structural)**, and encode a promise the user can learn:

| | Capability chip | Question chip |
|---|---|---|
| **Promise** | "does / opens / sends something" | "asks the assistant, answered right here" |
| **Affordance** | bordered + trailing **→** (optionally a leading action glyph) | lighter pill, **no arrow** (optionally a leading `?` / chat glyph) |
| **Label grammar** | imperative, verb-first — "Summarize this document", "Send the email" | interrogative — "What are the risks?", "How does this compare to a standard NDA?" |
| **Click** | dispatches the carried `bindingId` via the Click path | re-enters the grounded agent loop |

Rules:
- **One chip family, two variants** — not two components. Differentiate by the single arrow/affordance signal, so the difference reads as intentional.
- **Grammar reinforces affordance, authored deterministically by the SUGGEST prompt** (capabilities imperative+arrow; questions interrogative+no-arrow) — the words and the look always agree, so the user learns "arrow = acts, no arrow = asks" in ~two turns. No legend needed.
- **Order:** actions first (what you can *do*), then questions (what you can *ask*); optional hairline separator. Keep it light — these are throwaway followups per `ASSISTANT-UI-ELEMENT-CRITERIA`, don't over-chrome.
- **Kind is structural** (`bindingId` present ⇒ capability), so mislabeling like the screenshot's "Send the email" pill cannot happen: it renders as a capability chip if a send-email binding exists, or becomes an explicit question ("Should I send this to Jon James Wiley?") if it's a confirm-in-chat.
- **Deliverable:** extend `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` with this action-chip-vs-question-chip sub-distinction (sibling of the existing bubble/chip/card/tab decision).

---

## 6. The design moves

| # | Move | Layer | Notes |
|---|---|---|---|
| **M1** | Retire `GenerateAndEmitSuggestionsAsync`; stop emitting the untyped free-string `suggestions[]`. | BFF | The sole source of dead-ends. |
| **M2** | Generalize the grounded proposer to the conversational moment: after a chat response, run `SUGGEST-FOLLOWUPS` keyed off **(a)** a conversation tail (last user msg + assistant response + a little recent history) and **(b)** the context-scoped candidate capabilities (assistant-surface projectable set, scoped by host record / active document / open tabs). | BFF | Reuse `AssistantSuggestionService` + the existing Action; extend the operand with the conversation tail so labels are relevant to *what was just said*, not just the tab. |
| **M3** | Add the typed **question** suggestion kind (§5) so conversational richness survives without dead-ends. | BFF (prompt+schema) + client | Model emits both kinds in one typed structure. |
| **M4** | Client renders **only** the typed structure: capability items dispatch via the Click path (bindingId); question items re-enter the loop. No untyped free string is ever rendered. | Client | `SprkChatSuggestions`/`SprkChat`; the SSE `suggestions` event shape changes from `string[]` → typed array (wire-contract change — coordinate). |
| **M5** | Size budgets correctly: **generous input** (conversation slice + real menu + descriptions + grounded state); **modest output** (structured ≤3; bump `MaxOutputTokens` from 100 to ~300–500 to fit `{kind, id/text, label, reason}` per item). | BFF | Directly answers the token concern. |

**Deferred (phase 2, optional):** fold the three keyword-driven `[action:upload/search/select]` chips (`MissingContextKeywords`, `ChatEndpoints.cs:3301`) into the grounded menu too — the model, seeing an "upload document" capability in its candidates and the assistant asking for a document, would select it without a keyword list. Keep them as-is for now (they're deterministic + backed).

---

## 7. ADR / hygiene posture

- **ADR-039** — fully compliant, and *more* so than today: one grounded **proposer** (not a decider); closed-catalog selection with a hallucinated-id drop; chips ride the existing Click path; the pre-filter is the only aid. This is the pattern `AssistantSuggestionService` already documents.
- **ADR-040** — the suggestion turn consumes no tool reads and persists nothing (ephemeral UI); store-before-render is vacuously satisfied (as today).
- **ADR-015** — grounded state passed to the model stays server-derived + bounded (the same compact visible-state shape the chat turn uses).
- **§11 reuse-first** — no new service, no new dispatch surface, no new store: extend `AssistantSuggestionService` + the maker-authored `SUGGEST-FOLLOWUPS` Action (catalog data) + the existing Click path. The net change *removes* a surface (the ungrounded generator).
- **§10 BFF** — modest; measure publish + CVE on the BFF task as usual.

---

## 8. Scope + impact (why this needs your sign-off)

- **Bigger than task 021 as written**: it's a BFF+client consolidation, not a client gate. Recommend re-scoping 021 into (021a BFF: retire the generator + generalize the grounded proposer + typed schema; 021b client: render the typed structure) and adding a one-paragraph FR-04 note to `spec.md` recording the approach change (Path A/B decision per §6.5).
- **Wire-contract change**: the SSE `suggestions` event goes from `string[]` to a typed array. Touches shared `SprkChat` (all hosts) + the SSE contract → `/conflict-check` with compose-r5/r6; version the event shape carefully.
- **Product-visible**: purely-conversational free-string follow-ups are replaced by grounded capability chips + typed question chips. Net: fewer but *working* + more relevant suggestions.
- **Closes the loop honestly**: when the model repeatedly wants to suggest something with no backing capability, that's **signal for the behavior-gap register (P5 / FR-12)** — build the capability, don't fake it. Grounding surfaces the gap instead of hiding it behind a dead-end.

---

## 8a. Scope machinery — active tab + multi-tab (owner point #3)

The owner's model (conversation session + currently-active tab, with the Assistant knowing which tab is active after a switch) is already supported — no hard "scope N tabs" problem:

- **Active tab is already tracked** — the client stamps the focused `tabId` on every turn (`activeContextTabId`); the workspace-state block marks it "(active)". So "which tab is active" survives tab switches within the session.
- **Multi-tab awareness already exists** — the set of *open* tabs flows in as `liveTabs` and feeds the deterministic context pre-filter (task 030 tool-economy).
- **The clean split**: **candidate menu** = capabilities relevant to the *union of open-tab context-types* + the session/host (a next-step for any open surface can surface); **content grounding** = the *active* tab's server-derived visible state (labels specific to what's focused right now). Both inputs already exist; the suggestion operand just needs to carry them + the conversation tail.

---

## 9. Open questions — RESOLVED (see §1a, 2026-08-17)

All four owner questions are answered in §1a: keep question chips (§5a visual rules); one predictable grounded pass per turn; scope = session + active tab (§8a); split into 021a (BFF) + 021b (client) + a `spec.md` FR-04 note.

Remaining author follow-ups (not owner-blocking): (a) does the after-a-capability-dispatch moment use the existing `sprk_chiptransitions` ConsumerChips (deterministic action-declared followups — task 023) with the grounded proposer reserved for conversational turns, or do both sources merge into one ranked list? (b) exact `MaxOutputTokens` for the structured two-kind output (start ~400, tune).

---

## 10. Recommendation

Adopt the grounded-proposer consolidation (M1–M5), keep conversational-question chips, re-scope 021 into a BFF + client pair with a short `spec.md` FR-04 update, and file the `[action:*]` keyword fold-in + any repeatedly-wanted-but-unbacked suggestions to the behavior-gap register. This delivers "smart **and** guaranteed to work," reuses machinery you already shipped, and *removes* a fragile surface rather than adding one.
