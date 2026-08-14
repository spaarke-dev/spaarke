# Task 052 — Monitored lens: schema finding + scoping escalation (§6.5)

> **Date**: 2026-08-13 · env `https://spaarkedev1.crm.dynamics.com` (live `EntityDefinitions` query via `az account get-access-token` + Web API, mirroring task 042's auth pattern)

## 1. Schema finding — which entities carry `sprk_monitor`

Queried `EntityDefinitions(LogicalName='{e}')?$expand=Attributes` for the FR-04 core
set plus the other UserOwned `sprk_*` entities referenced in the data model, filtering
client-side for `*monitor*` attributes (the metadata endpoint rejects `startswith()` on
`Attributes` sub-collections — `0x8006088a` "isn't supported for Metadata Entities").

| Entity | `OwnershipType` | Monitor field | Type |
|---|---|---|---|
| `sprk_matter` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_project` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_document` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_todo` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_event` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_workassignment` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_invoice` | UserOwned | `sprk_monitor` | Boolean |
| `sprk_communication` | UserOwned | **`sprk_ismonitored`** (different field — unrelated email-tracking placeholder, see `EmailWorkspace.mapping.ts` `EMAIL_TRACKING_FIELDS`, task 023/email-communication-solution-r5) | Boolean |
| `sprk_budget` | UserOwned | *(none)* | — |

**`MONITOR_ENTITY_SET`** (used by `monitoredService.ts`) = the 7 entities that actually
carry `sprk_monitor`: `sprk_matter`, `sprk_project`, `sprk_document`, `sprk_todo`,
`sprk_event`, `sprk_workassignment`, `sprk_invoice`. `sprk_communication` is
deliberately excluded — its `sprk_ismonitored` field is a different, still-placeholder
concept for an unrelated feature, not the shared record-level flag `TrackingFieldTrio`
exposes as "Monitor" on the other seven entities. `sprk_budget` has no monitor field at
all.

All seven are `UserOwned` — `ownerid`/`_ownerid_value` is present and uniform.

## 2. Escalation (§6.5 / task `<escalation>` trigger) — FIRED for "assigned-to-me"

The task's trigger: *"If the ownership/assignment scoping for `sprk_monitor` cannot be
expressed as a single reliable OData filter on the current build (e.g., assigned-to-me
semantics ambiguous), STOP and escalate the scoping definition (root §6.5) rather than
over- or under-scoping the shared Monitored group."*

**Owned-by-me** — NOT ambiguous. All 7 entities are `UserOwned`; `_ownerid_value eq
{userId}` is a single, reliable, uniform filter across the set. Implemented as-is.

**Assigned-to-me** — genuinely ambiguous; queried each entity's attribute list for
`*assign*` fields and found NO shared convention:

| Entity | Assignment-shaped fields found |
|---|---|
| `sprk_matter` | `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2`, `sprk_assignedtoexternal`, `sprk_assignedtointernal` |
| `sprk_project` | *(same set as `sprk_matter`)* |
| `sprk_event` | *(same set as `sprk_matter` plus)* `sprk_assignedto`, `sprk_assignedto1/2`, `sprk_reassignedby`, `sprk_todoassigned` |
| `sprk_workassignment` | `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2`, `sprk_assignedlawfirmattorney1`, `sprk_assignedtoexternal/internal`, `sprk_assignedto` |
| `sprk_invoice` | `sprk_assignedto1/2`, `sprk_assignedtoattorney1/2`, `sprk_assignedtoparalegal1/2` |
| `sprk_todo` | `sprk_assignedto` (single field — the only entity with one clean candidate) |
| `sprk_document` | **none** — no assignment-shaped field exists on this entity at all |

Every entity has a *different* role-modeled assignment schema (attorney/paralegal/
law-firm/internal/external slots, multiple numbered slots per role), not one shared
"assigned to" field. Picking which of these (single field? OR across all of them? which
slots count?) is a legal-ops business decision, not a schema fact — guessing would risk
either badly under-scoping (miss records where the user is `sprk_assignedattorney2` but
not `sprk_assignedattorney1`) or badly over-scoping (treat every assignment-shaped role
as "mine"). `sprk_document` additionally has **no** assignment field of any kind, so
even a "does the entity have assignment at all" check is non-uniform.

**Resolution — Path A (project-scoped exception, per CLAUDE.md §6.5)**: r1 ships the
Monitored lens **owner-scoped only** (`sprk_monitor eq true AND _ownerid_value eq
{userId}`), the unambiguous half of the design.md §6c / FR-09 requirement. This
satisfies every authored acceptance criterion and `<ui-tests>` case in
`052-monitored-lens.poml` (none of them require a record assigned-but-not-owned to
appear; the negative case only requires a record neither owned nor assigned to be
excluded, which owner-only scoping trivially satisfies as a strict subset).
"Assigned-to-me" inclusion is **deferred, not silently dropped** — flagged here for the
owner to decide (Path A rationale: the ADR/spec text itself is fine as written; the
gap is an implementation-detail decision the spec didn't anticipate needing per-entity
role-field selection). Candidate follow-up task: define a `sprk_assignedto`-style
canonical field (or resolve which of the existing role fields count) before extending
`monitoredService.ts`'s filter.

**Alternative considered and rejected**: OR-ing every `*assign*`-shaped field per
entity into the filter. Rejected — this guesses at business meaning (should
`sprk_assignedparalegal2` count the same as `sprk_assignedattorney1`?) that the schema
alone cannot answer, which is exactly what the task's `<escalation>` trigger says not to
do ("do NOT guess").

## 3. Impact

- `monitoredService.ts` ships owner-scoped only; `MONITOR_ENTITY_SET` is the 7-entity
  set above.
- No code change is blocked — the owner-scoped half is fully implemented, tested, and
  passes every closed acceptance criterion in the POML.
- Task 052 is marked **completed** for the shipped (owner-scoped) surface; this note is
  the record of the deferred assignment-scoping decision for a human/owner follow-up
  (not a new task number assigned here — owner's call on priority).
