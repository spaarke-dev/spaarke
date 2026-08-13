# Assistant Behavior-Gap Register — R4 (P5)

> **Purpose (spec FR-12)**: a lightweight *standing* loop for Assistant behavior gaps — NOT a one-time audit and NOT a system surface. Each row captures the exact user turn, what the Assistant did, what was expected, the surface (Action/Binding/tool), and a triage destination.
> **Triage destinations**: `preference` (per-user → My Assistant / memory) · `systemic` (everyone → CX/product-owner catalog-authoring exercise, per owner Q4) · `defect` (crash/clip/dead-end → normal bug/defer track).
> **Process**: Capture (UAT / in-conversation thumbs-down / observed dead-end) → Triage → Author + **eval case** → Measure. Every AI-behavior change lands with an eval so the gap can't silently regress.

---

## Register

| # | Date | User turn | Assistant did | Expected | Surface | Triage | Status / FR |
|---|---|---|---|---|---|---|---|
| P1 | 2026-08-10 | "what do I need to do today" | Opened the Task widget, emitted thin "I opened your task list" — no summary | A grounded summary (real counts/top items, cited) + a recommendation, then open Tasks | `list-tasks` Action (`allowstools=false`, ack-only output) | systemic | **R4 E1 — FR-01/02/03** |
| P2 | 2026-08-10 | follow-on chip "Help me prioritize my tasks" | Asked the user for their user ID (already known via OBO) and dead-ended | Prioritize the user's own tasks over OBO without asking for identity; or don't offer the chip if unbacked | free-string `SprkChatSuggestions` + user-scoped tool descriptions | systemic | **R4 E2 — FR-04/05** |
| P3 | 2026-08-13 | (meta) "how does the Assistant learn, per-user and system-wide?" | Memory + "My Assistant" + thumbs exist but never form a loop | An explicit standing directive ("do this every time") persists + biases behavior within bounds | memory / feedback / preference-producer | systemic + preference | **R4 E3 — FR-07/08/09** |
| P4 | 2026-08-13 | (open a document → "Open in Compose") | Assistant transcript clips mid-row with dead whitespace in the Xrm-dialog iframe host | Bounded transcript, internal scroll, composer pinned — host-proof | `ConversationPane → SprkChat` flex chain (D9) | defect | **R4 E4 — FR-11** |

---

## Notes

- P1–P4 are the first four records; the register is fed continuously by operator UAT + the in-product feedback subsystem (thumbs/comments) going forward.
- The **operator promotion queue is intentionally NOT a system feature** (owner Q4, 2026-08-13) — recurring `systemic` items are promoted into catalog authoring as a CX/product-owner exercise, reviewing this register + the `feedback` aggregates.
- `preference`-triaged items feed the E3 governed narrow-allow-list producer (FR-09) — bounded, injection-defense preserved.
