# Messaging Communication App — R1

> **Status**: Initialized — ready for Wave 0 (task 001)
> **Created**: 2026-07-16 via `/project-pipeline`
> **Branch**: `work/messaging-communication-app-r1`
> **Portfolio**: [Project #654](https://github.com/spaarke-dev/spaarke/issues/654) · Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Status: Active (Planning) · Start 2026-07-16 · Target: _unset_

Add **messaging (real-time chat) as the second channel** on Spaarke's existing communication platform — not a new module or pipeline. Azure Communication Services (ACS) Chat is the transport; Dataverse `sprk_communication` is the system of record; the BFF is the sole policy-enforcement and token-minting point. R1 delivers the server-side plumbing (channel provider + inbound ingestor + ACS integration), a first-class communication thread data model, and a usable **async (polling)** message experience in the MDA. The live open channel, spine-pushed notifications, SMS, and Teams/portal surfaces are deferred to the next project / R2 / R3.

---

## Documents

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized spec (18 FRs, 8 NFRs) — implementation source of truth |
| [`design.md`](design.md) | Human design (rev 2) — rationale, current-state truth, hot-path declaration |
| [`plan.md`](plan.md) | Wave WBS + critical path + discovered resources + coordination |
| [`current-task.md`](current-task.md) | Active task state (context recovery) |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | All tasks + status + parallel groups + dependency graph |
| [`spaarke-messaging-solution-synopsis.md`](spaarke-messaging-solution-synopsis.md) | Original idea source (folded into design) |

---

## Graduation Criteria (spec Success Criteria)

1. [ ] User sends a message from the MDA; persists **once** as `sprk_communication` (type=Message), threaded, appears in the polling timeline within ~5s (echo-dedup).
2. [ ] Inbound ACS message captured via Event Grid appears in the thread; duplicate delivery yields **one** record.
3. [ ] An email reply and a chat message in the same conversation both carry a `sprk_thread`; the timeline renders them grouped.
4. [ ] Existing email inbound association still passes after the `IThreadResolver` extension (characterization tests green).
5. [ ] A private thread's messages are invisible to a user without a grant; an internal-only message is invisible to non-internal users (security-sensitive review).
6. [ ] A 1:1 direct thread works with explicit two-participant membership.
7. [ ] An email's content quotes into a new message and vice-versa.
8. [ ] A message attachment lands in SPE as a linked `sprk_document`, policy-enforced.
9. [ ] BFF publish size ≤ 60 MB, delta reported.
10. [ ] ADR-046 authored (concise + full); INDEX updated to Accepted.
11. [ ] No client-side ACS SDK import in R1 client code.

---

## Scope Guardrails (owner-locked MUST NOT)

- ❌ No Dataverse/Power Apps **Activities**, OOB `email`/activity entities, or **portal comments**.
- ❌ No **native Teams chat** capture (Graph chat/channel-message APIs).
- ❌ No second communication pipeline, second regarding mechanism, or second channel-provider contract.
- ❌ No **live client / ACS client SDK / ACS composites** in R1 (that is the next project).
- ❌ AI may **flag** privilege, never **decide** (ADR-015).
- ❌ No messaging-only notification hub (R1 polls; R2 consumes `notification-spine-r1`).

---

## Execution

All task work MUST use the `task-execute` skill (root CLAUDE.md §4). To begin:

```
work on task 001
```

or `continue` to pick up the first 🔲 in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

**Coordination**: run `/conflict-check` at start and before every BFF wave — this project edits shared `Services/Communication/` code. Coordinate the `threadId` contract with `spaarke-notification-spine-r1`.
