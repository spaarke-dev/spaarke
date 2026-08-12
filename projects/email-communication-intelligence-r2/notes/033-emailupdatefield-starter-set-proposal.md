# Task 033 — `sprk_emailupdatefield` starter allow-list (proposal for operator review)

> **Status**: DRAFT for operator approval. GATED (seeding writes to `spaarkedev1`). Not seeded.
> **Spec**: FR-D4 — *"`sprk_emailupdatefield` ships empty → Job B can propose nothing until seeded."*

## What Job B does with this table

Job B (`CommunicationEnrichmentService.RunEmailProposeAsync`) proposes ALLOW-LISTED field updates
on the **core record a communication is associated with** (matter / project / etc.), each carrying an
old→new value + a verified citation. **The allow-list is the SOLE gate** on *which* fields may be
proposed (`sprk_emailupdatefield` where `sprk_enabled = true`). An empty table ⇒ Job B proposes nothing.

## The record shape (one row = one allow-listed field)

| Column | Meaning |
|---|---|
| `sprk_targetentity` | The core entity the field lives on (e.g. `sprk_matter`, `sprk_project`) |
| `sprk_targetfieldlogicalname` | The target field's logical name on that entity |
| `sprk_fieldtype` | Field type (drives value coercion) |
| `sprk_extractionguidance` | Optional prompt guidance for the propose Action |
| `sprk_enabled` | **Bool — only `true` rows are ever proposed** (defense-in-depth re-check) |

## Why there are no "initial mappings" to read

The spec deliberately ships the table **empty** and leaves the starter set to a seed-time decision —
*which* fields are safe to let email content propose updates to is a **business/operator judgment**,
not something defined in code. So there's nothing to "see" yet; this note proposes a conservative set.

## Proposed conservative starter set (ILLUSTRATIVE — confirm field names + safety)

The safest starters are **low-risk, email-derivable, human-confirmed** fields on the two core
association targets. **Each `sprk_targetfieldlogicalname` MUST be verified to exist on the target
entity before seeding** (I have not confirmed these against the live `sprk_matter`/`sprk_project`
schemas — that's the next step once you approve the direction):

| # | targetentity | targetfieldlogicalname (VERIFY) | fieldtype | guidance | enabled |
|---|---|---|---|---|---|
| 1 | `sprk_matter` | `sprk_description` (or matter summary) | Text | "Update only if the email materially changes the matter description." | true |
| 2 | `sprk_matter` | next-action / follow-up date field | DateTime | "Propose a follow-up date only when the email states an explicit deadline." | true |
| 3 | `sprk_project` | `sprk_description` | Text | same as #1 for projects | true |

**Deliberately NOT in the starter set** (too risky for auto-propose): status/stage option-sets,
financial fields, owner/assignment, privilege/security fields.

## Next steps (on your approval)
1. Confirm the direction + which fields you actually want (add/remove rows).
2. I verify each `sprk_targetfieldlogicalname` exists on its target entity (`dataverse-create-schema` / MCP read).
3. Seed the approved rows via Web API (gated deploy step — with your go-ahead).

**Recommendation**: keep the starter set to 2–3 genuinely-safe fields; expand later as UAT shows what
Job B proposes well. Job B always produces *human-confirmed proposals* (never auto-writes), so the
allow-list controls scope, not final authority.
