# Assistant UI Element Criteria — Bubble vs Chip vs Card vs Tab

> **Status**: Standard (2026-07-22). Decision criteria for which surface element the Assistant uses to present a given thing.
> **Sibling docs**: [MODAL-DECISION-CRITERIA.md](MODAL-DECISION-CRITERIA.md) (how to *open* a record/form) · [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md) (how a surface is *routed*).
> **Applies to**: the SpaarkeAi Assistant pane (`src/solutions/SpaarkeAi`) and the shared `SprkChat` (`@spaarke/ui-components`).

---

## 1. The four elements

The Assistant presents things in exactly **four** element types. Each has a distinct job; mixing them up is the most common UX defect.

| Element | Component | It is… |
|---|---|---|
| **Bubble** | `SprkChat` message transcript | **Dialogue** — a message someone *said* (user right, assistant left) |
| **Chip** | `SprkChatSuggestions` / `ConsumerChips` | A **grounded next-step action for the *current* turn** — small, inline, ephemeral |
| **Card** | `SuggestionCard`, Get-Started cards | A **standalone item to act on** — proactive nudge, launcher, or discrete unit of work; persistent |
| **Tab** | Workspace pane widget (grid, editor, analysis) | A **rich output or tool** too big for the chat |

## 2. The decision — four questions, in order

Ask them top-down; the first "yes" wins.

1. **Is it dialogue — something the user or Assistant *said*?** → **Bubble.**
   *(A question, an answer, a status line, an acknowledgement.)*
2. **Is it a throwaway follow-on to the turn that just happened?** → **Chip.**
   *("Now that I did X, you might do Y." Grounded in the last turn; disappears on the next.)*
3. **Is it a persistent thing the user acts on, not tied to the immediate turn?** → **Card.**
   *(A proactive suggestion, a capability launcher, a discrete piece of work.)*
4. **Is it rich content or a tool?** → **Tab.**
   *(A grid, an editor, an analysis, search results — anything that needs its own pane.)*

## 3. One line each

- **Bubble = conversation.** If it reads as speech, it's a bubble. Never a bubble for a set of actions or a piece of work.
- **Chip = a next-step for *this* turn.** Ephemeral, grounded, lightweight. Replaced by the next turn. Max ~2–3.
- **Card = a standalone thing to act on.** Higher prominence, survives across turns, has its own affordances (open / dismiss).
- **Tab = a surface.** Output or tool that doesn't belong inline in the chat.

## 4. Worked distinctions (why each element, not the others)

| Thing | Element | Why not the others |
|---|---|---|
| "To create a task, what's the title/due date?" | **Bubble** | It's dialogue — the Assistant asking. Not an action set. |
| "Suggested next steps" after an action (e.g. *Draft a response*, *Add a related matter*) | **Chip** | Grounded in the just-completed turn; throwaway; replaced next turn. |
| A proactive "💡 Review Acme v. Beta" (Daily Briefing) | **Card** | Arrives *without* a turn; persistent; the user opens or dismisses it. Not a chip (not tied to a turn). |
| "Summarize a document / Create a matter / Compose" launchers | **Card** | Persistent capability launchers, not turn-specific next-steps. |
| "My Tasks" list, a document analysis, search results | **Tab** | Rich/tabular; too big for the chat; needs its own pane. |

## 5. Rules (do / don't)

- **DO** keep chips ephemeral and grounded — they are next-steps for the *last turn*, capped at a few. If a set of chips would persist across turns or arrives without a turn, it's a **card**, not chips.
- **DON'T** render internal codes in a chip/card label. Chips carrying routing markup (`[action:<id>] <label>`, AIPU-058) MUST display **only the label**; the raw string is for routing (`SprkChatSuggestions` strips the prefix — regression fixed UAT 2026-07-22).
- **DON'T** fire a chip set from a keyword heuristic that can match unrelated replies. The document/matter "missing-context" chips (`EmitMissingContextChipsIfNeededAsync`) must only fire for genuine document-missing flows — not e.g. a task-creation clarification (tightened UAT 2026-07-22). *Long-term:* this pre-ADR-039 keyword heuristic should be retired in favor of grounded routing.
- **DON'T** put a set of actions or a unit of work in a **bubble** — bubbles are speech only.
- **DO** give **cards** their own affordances: a clickable region (open) and, where the item is transient, a **dismiss 'x'** (like a tab close). Hover highlight belongs on the clickable region, not on non-clickable headers.
- **DO** collapse a *stack* of proactive cards behind a single disclosure header ("You have N new notifications") so they don't dominate the conversation space (UAT 2026-07-22) — the header is a toggle (no hover), the cards drop down.
- **DO** open rich output/tools as **tabs** via the workspace event bus, never as a wall of bubbles or a giant card.

## 6. Where each lives (code map)

| Element | Where |
|---|---|
| Bubble | `@spaarke/ui-components` `SprkChat` transcript |
| Chip (next-step) | `SprkChatSuggestions.tsx` (SSE `suggestions`) · `ConsumerChips` (chiptransitions) |
| Card (proactive) | `SuggestionCard.tsx` + `useSuggestionCards.tsx` (notification spine) |
| Card (launcher) | `GetStartedCardsWidget` |
| Tab | Workspace `widget_load` (see [SURFACE-LAUNCH-MECHANISM](../architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md)) |

---

## Related
- [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md) — how the surface a card/chip opens is routed (registry) vs the proactive spine.
- [MODAL-DECISION-CRITERIA.md](MODAL-DECISION-CRITERIA.md) — when acting opens an OOB record modal vs a proprietary dialog.
- [SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md](../architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md) — the proactive-card (suggestion) delivery path.
