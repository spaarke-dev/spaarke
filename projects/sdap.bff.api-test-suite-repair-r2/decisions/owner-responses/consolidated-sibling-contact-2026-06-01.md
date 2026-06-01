# Consolidated Sibling-Coordination Contact — 2026-06-01

> **Status**: Resolved
> **Resolution date**: 2026-06-01 (r2 project initialization)
> **Source**: Owner clarification at r2 spec.md "Owner Clarifications" section + r2 CLAUDE.md "Decisions Made"
> **Authoritative for**: FR-05, FR-06, NFR-03

---

## Decision

`dev@spaarke.com` is the **consolidated contact for all sibling coordination** in the `sdap.bff.api-test-suite-repair-r2` project. The same address serves four distinct coordination roles that r1's project plan had assumed would be separate contacts:

| Role | Decision context | r2 reference |
|---|---|---|
| Security reviewer for HIGH-severity ledger entries | NFR-03 (Phase 1 tasks 010 + 011 merge gates) | spec.md NFR-03; CLAUDE.md "Decisions Made" |
| `ai-spaarke-insights-engine-r1` sibling owner for RB-T028-02 (Insights Layer 2 HOLD) | FR-05 outreach destination | spec.md FR-05; CLAUDE.md "Decisions Made" |
| `ai-spaarke-action-engine-r1` sibling owner | FR-06 priority-order sign-off | spec.md FR-06; r1 priority-order.md Owner Outreach Status |
| `x-email-communication-solution-r2` sibling owner | FR-06 priority-order sign-off | spec.md FR-06; r1 priority-order.md Owner Outreach Status |

---

## Implication for FR-05 (RB-T028-02 Insights Layer 2 HOLD)

The original FR-05 framing assumed `ai-spaarke-insights-engine-r1` had a distinct owner whose response window was 1 week (with `archived-pending-sibling-engagement` as path-c if the owner did not respond).

**Path-c is now N/A.** The Insights sibling-project owner IS `dev@spaarke.com` (same as the r2 owner). The 1-week timeout clause is therefore inapplicable.

**Phase 1 task 012 path**:
- Task 012 (Resolve RB-T028-02 Insights Layer 2 HOLD) contacts `dev@spaarke.com` directly when executed at Phase 1.
- Choice is between (a) sibling project takes the bug — 3 tests transferred to their backlog — or (b) r2 takes the bug — production fix here.
- (c) is OFF the table.
- No separate "ai-spaarke-insights-engine-r1 owner outreach" thread is needed; the contact is known.

See companion record [`../D-07-insights-layer2-resolution.md`](../D-07-insights-layer2-resolution.md) (placeholder at Phase 0; finalized at Phase 1 task 012).

---

## Implication for FR-06 (sibling sign-offs)

The original FR-06 framing populated three distinct sibling-owner sign-off slots in `projects/sdap-bff.api-test-suite-repair/priority-order.md` (Action Engine, Insights, Communications).

**Resolution**: All three slots in r1's priority-order.md "Owner Outreach Status" table are populated with `dev@spaarke.com` as the named owner + 2026-06-01 as the sign-off date + status `signed-off`. Change-log entry added to priority-order.md documenting the consolidation. FR-06 is satisfied.

Per-area annotation tables (HIGH/MEDIUM/INTEGRATION/LOW tier sections in priority-order.md) retain `TBD` cell text where the sign-off is implicit; the Owner Outreach Status table is the authoritative source per FR-06.

---

## Implication for NFR-03 (security reviewer)

`dev@spaarke.com` reviews all HIGH-severity production PRs in r2:
- Phase 1 task 010 (RB-T044-01 — `ConversationHistorySanitizer` cross-matter privilege leak)
- Phase 1 task 011 (RB-T028-03/04/05/06 cluster — conditional registration root cause)

The merge gate documented in those task POMLs cites `dev@spaarke.com` as the named reviewer.

---

## Implication for 1-week timeout clause

The 1-week timeout clause from the original FR-05 (archive-pending-sibling-engagement if no response) is **N/A**. The contact is known and is the same owner approving the project. There is no outreach window to track.

---

## Cross-references

| Reference | Purpose |
|---|---|
| [r2 spec.md "Owner Clarifications" section](../../spec.md) | Source of the 2026-06-01 owner resolution (5 questions resolved that day) |
| [r2 CLAUDE.md "Decisions Made" section](../../CLAUDE.md) | Project-level decisions table including this resolution |
| [r1 priority-order.md Owner Outreach Status](../../../sdap-bff.api-test-suite-repair/priority-order.md#owner-outreach-status) | Where the 3 sign-off slots are populated (FR-06 deliverable) |
| [D-07 Insights Layer 2 resolution placeholder](../D-07-insights-layer2-resolution.md) | Companion record; finalized at Phase 1 task 012 |
| [r2 task 002 POML](../../tasks/002-sibling-owner-outreach.poml) | Originating task |

---

*Captured by r2 task 002 (`/task-execute`) on 2026-06-01. This record supersedes the assumption in the original FR-05 / FR-06 framing that sibling contacts would be unknown at Phase 0.*
