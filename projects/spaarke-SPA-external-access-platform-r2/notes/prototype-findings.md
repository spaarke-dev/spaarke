# P0 Prototype Findings — production questions surfaced during UX prototyping

> Captured during P0 (tasks 001–004) in `spaarke-prototype/projects/2026-08-external-access-module-host`
> + the shared seed (`_infra/seed/**`). These are inputs to the named production tasks — the escalation
> triggers on those tasks reference them. Not blockers to finishing P0.

## For task 020 (module-entitlement Dataverse schema) — the FR-07 open owner decision

**Question**: Is Tier-1 module entitlement a **first-class Dataverse entity**, or a **virtual/computed
projection**?

- The prototype modeled Tier-1 as a standalone mock entity `sprk_moduleentitlement` with a
  `sprk_source` discriminator (`approle` / `workforce-fallback` / `contact-grant`) so the launcher
  can render entitlement-honestly — a convenience for the mock.
- **Production likely should NOT persist internal App-Role entitlement as rows.** Per FR-08, internal
  entitlement is resolved from the **App Role claim in the workforce token** (no Graph call, no stored
  grant), and per the MUST-NOT rule "no Contact merely to grant internal access." So the persisted
  store is plausibly **external per-Contact grants only**, with internal entitlement token-resolved at
  request time and merged in the `/me` resolver (task 021/022).
- **Owner decision for task 020**: (a) first-class entity for all entitlement, vs (b) store only
  external per-Contact grants + resolve internal from App Role claims live. This is the task-020
  escalation trigger.

## For task 030 (sprk_servicerequest intake schema) — request-type + status model sign-off

- The prototype invented a reasonable status set: **Draft → Submitted → InReview → Approved →
  ReadyForSignature → Completed** (NDA stops at ReadyForSignature per FR-15), and request types
  NDA / PolicyProcedures (extensible option set).
- Spec Prerequisites explicitly defer this: *"Legal Front Door intake schema sign-off (request types,
  status model)."* Task 030's escalation trigger requires **owner sign-off on the option set + status
  workflow** before creating the schema. The prototype values are a starting proposal, not approved.

## Prototype consumption note (task 001 experiment ↔ shared seed)
The task-001 experiment still uses an inline `mockData.ts` catalog + entitlement scenarios. It can
migrate to the shared `external-access-r2-personas` preset incrementally (module ids already align:
`assigned-work` / `legal-front-door` / `e-billing`); would need a vite alias + tsconfig `include` for
`_infra` (as `smart-todo-r4-uat` does). Left inline to avoid breaking the running experiment; task 003
may wire it.
