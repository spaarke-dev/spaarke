# 🔔 ADR Conflict — Resolution Record: ADR-023

> **Task** 012 (spec FR-08) · **Date** 2026-09-04 · **Format**: CLAUDE.md §6.5

- **ADR in question**: ADR-023 — Choice Dialog Pattern
- **Specific rule challenged**: the ADR carries **zero MUST/MUST NOT rules** and `Status: Superseded — demoted to pattern (2026-03-19)`. The challenge is whether a superseded, ruleless ADR should remain in the concise ADR tier at all.
- **Conflict**: it is the only ADR in the estate classified **stale**. It governs nothing — the pattern it described was demoted to `.claude/patterns/` and its subject was later absorbed by **ADR-050 (Canonical Modal Shell)**, which explicitly *"preserves the Choice Dialog pattern via `ChoiceModal`"*. An agent loading the ADR index still sees it as an entry, which costs context and implies a live decision where none remains.
- **Proposed path**: **C — comply.** Confirm the 2026-03-19 supersession as correct and leave it in place, routed *deliberately unenforced*.
- **Rationale**: the supersession decision was already made, correctly, and ADR-050 carries the live rule. Nothing needs changing. The right treatment for a superseded ADR is to **keep it as a tombstone** — deleting it would break the numbering and lose the pointer from ADR-023 to ADR-050 that a reader following an old reference needs. Its routing (`deliberately unenforced`) is already the honest answer.
- **Impact of accepting path C**: none. ADR-023 stays superseded, stays unenforced, and stays in the index as a tombstone. No amendment, no code change.
- **Alternative considered and rejected**: **Path B (amend to remove it from the index).** Rejected on two grounds: it breaks the ADR-023 → ADR-050 breadcrumb that gives an old reference somewhere to land, and "delete the tombstone" optimises index length — a context-cost proxy — over the reader's ability to follow a stale link. That is the wrong trade for a single row.
- **Enforcement status**: ⛔ / n-a — routed *deliberately unenforced*, which is the terminal state. It is not waiting on anything.
