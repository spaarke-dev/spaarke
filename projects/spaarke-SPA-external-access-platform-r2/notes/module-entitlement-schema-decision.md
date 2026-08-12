# Module-entitlement schema — shape decision (PROPOSAL — awaiting owner sign-off)

> Task 020 · 2026-08-10 · Status: **DESIGN COMPLETE, schema NOT yet created** (Step-4 escalation gate —
> the shape was deferred to the owner in the spec's Unresolved Questions + TASK-INDEX high-risk section,
> and creating Dataverse schema in the shared dev env is hard to reverse, so it awaits a one-line sign-off).

## Problem (FR-07 / FR-08 / FR-09 + NFR)

A NEW Tier-1 **module-entitlement** layer that sits ALONGSIDE the existing `sprk_externalrecordaccess`
(Tier-2 record participation). Two distinct concerns:

1. **FR-09 external**: record that a specific **Contact** is entitled to a named **module**.
2. **FR-08 internal**: map an Entra **App-Role name → module**, resolvable **WITHOUT any Contact record**
   (the NFR: never fabricate a Contact merely to grant internal access).

Verified closest neighbor: `sprk_externalrecordaccess` is record-participation only (3 access levels,
typed root lookups) — it cannot express "entitled to a module with no record" nor "App-Role → module."
Confirmed via MCP search: **no existing custom entitlement entity** (only OOB `role`/`appmodule*` tables).
Module identity is **code-registered** in R2 (widgetRegistry.ts) — Out-of-Scope forbids a `sprk_module`
catalog entity.

## Decision: TWO lightweight tables, keyed by a STRING module code (no catalog entity)

Module identity = a **string code** matching the code-registered `requiredEntitlement` ids already in
`src/client/external-spa/src/registry/widgetRegistry.ts`: **`legal-front-door`**, **`policy-library`**,
**`assigned-work`**, **`admin`** (Messages = no entitlement). A string (not an option-set) satisfies
FR-08's "add a module later with only a data row, no schema change" most cleanly — a new module needs no
option-set metadata edit, and the code registry stays the single source of module identity.

### Table 1 — `sprk_moduleentitlement` (external per-Contact entitlement, FR-09)

| Logical name | Type | Notes |
|---|---|---|
| `sprk_moduleentitlementid` | Uniqueidentifier (PK) | |
| `sprk_name` | Text (primary) | display, e.g. "`<contact> → assigned-work`" |
| `sprk_contact` | Lookup → `contact` | the external grantee (single-target — no polymorphism needed) |
| `sprk_modulecode` | Text | the entitlement code, e.g. `assigned-work` (matches widgetRegistry) |
| `statecode`/`statuscode` | State/Status | Active = entitled; Inactive = revoked |

One active row = "this Contact is entitled to this module." Revoke = deactivate. (Admin UI = task 026.)

### Table 2 — `sprk_approlemodulemap` (internal App-Role → module mapping, FR-08) — CONTACT-FREE

| Logical name | Type | Notes |
|---|---|---|
| `sprk_approlemodulemapid` | Uniqueidentifier (PK) | |
| `sprk_name` | Text (primary) | display, e.g. "`FrontDoorUser → legal-front-door`" |
| `sprk_approlename` | Text | the Entra App-Role value, e.g. `FrontDoorUser` |
| `sprk_modulecode` | Text | the module this role grants, e.g. `legal-front-door` |
| `statecode`/`statuscode` | State/Status | Active mapping |

**No Contact FK** → internal entitlement is expressible with zero Contact rows (satisfies the NFR).
FR-08 "multiple per-module roles later": a role→N-modules = N rows; a module←M-roles = M rows. Adding a
mapping = one data row, **no attribute/entity change**. This is BFF-read config (task 021 resolver), not
a UI-managed catalog.

## ADR-024 (polymorphic regarding)
Satisfied vacuously: neither table needs a multi-target relationship. `sprk_contact` is inherently
single-target (contact); `sprk_modulecode` is a string, not a lookup. No single-target lookup is masking
a polymorphic need — there is no "regarding multiple entity types" requirement in the entitlement layer.

## Rejected alternatives
- **One combined table with an optional Contact FK** (null Contact = internal mapping). Rejected: an
  always-nullable FK invites the exact NFR violation (a stray Contact on an internal row) and muddies the
  resolver's two code paths. Two purpose-built tables keep the "internal = Contact-free" invariant structural.
- **`sprk_module` catalog entity + module lookups.** Rejected: Out-of-Scope ("modules are code-registered
  in R2"); a catalog adds a maintenance surface with no R2 consumer and would need seeding to match code.
- **Option-set for module code.** Rejected: every new module would require an option-set metadata edit +
  publish, violating FR-08's "data-row-only" future extension. String code aligns with the code registry.
- **Overload `sprk_servicerequest`** (a stub). Rejected: conflates intake with entitlement (§11).

## Downstream binding (tasks 021/022/023/026)
- 021 resolver: external plane → active `sprk_moduleentitlement` rows for the caller's Contact →
  `sprk_modulecode` set; internal plane → caller's App-Role claims ∩ active `sprk_approlemodulemap` →
  `sprk_modulecode` set. Both yield the `/me.entitlements` string list the client already gates on.
- 026 admin UI: create/deactivate `sprk_moduleentitlement` rows (reuse `AccessGrantModal`).

## What creating this will do (for sign-off)
Create 2 custom tables (`sprk_moduleentitlement`, `sprk_approlemodulemap`) + their attributes in the
**dev** environment with the `sprk_` prefix, publish, and seed the R2 App-Role mapping row
(`FrontDoorUser → legal-front-door`). No changes to existing entities. Reversible only by table delete
(hence the sign-off).
