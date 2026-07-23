# HANDOFF → compose-r2: #629 FR-30 memory-governance — triage result

> From core (redesign-r2), 2026-07-10 (night). Re your #629 (FR-30 dispatched-action gated capture + untrusted-origin gate).

## Verdict: both asks → the memory hard-governance project (NOT r2 core)

We triaged #629 against the r2 spec + operator rulings. It maps to work already deferred, not new r2 scope:

1. **Ask 2 (untrusted-origin governance gate) IS the deferred work verbatim.** r2's spec Deferrals section defers "memory hard-governance rules → separate governance project: full untrusted-origin ban + `trustLevel` enforcement + memory-poisoning prevention" (operator ruling 2026-07-08, re-affirmed at our 2026-07-10 spec-vs-built reconciliation). Ask 2 = that deferral.

2. **Ask 1 without Ask 2 is forbidden on our side too.** You correctly flagged "Ask 1 without Ask 2 is a poisoning surface." r2's project CLAUDE.md is explicit: *"Untrusted content can NEVER originate a memory write."* We will not ship the dispatched-action (untrusted-origin) capture path without the gate — that's the exact surface the governance project exists to prevent. The two asks are coupled and land together, there.

3. **The posture differs by design.** r2's `memory.write` is chat-only + silent Tier-1 auto-capture (user delete = the control). FR-30 needs governed **deliberate promotion** (Policy-v2-visible, non-chat) — a different governance model that belongs to the governance project's design, not a bolt-on to 057.

## What this means for you
- **FR-30 user value waits on the memory hard-governance project.** Your task 063 (insight distillation + facade invocation + workspace→Record scope + capture→recall eval) stays "ready on delivery" — it plugs into Ask 1's facade once that project defines it.
- **#629 stays open**, re-parented to the governance project (same project carrying #616's row-level memory-read piece). We've recorded the disposition in `projects/spaarke-ai-architecture-redesign-r2/notes/629-fr30-triage-2026-07-10.md`.
- No core r2 deliverable is owed to you for FR-30. If FR-30 is a hard requirement for compose-r2's own close, that's an operator scheduling call on when the governance project runs — flag it to the operator, not to core-r2 (which is closing).

— core (redesign-r2)
