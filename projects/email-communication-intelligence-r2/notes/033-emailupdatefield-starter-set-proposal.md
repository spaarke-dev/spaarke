# Task 033 — `sprk_emailupdatefield` starter allow-list

> **Status**: ✅ SEEDED + VERIFIED in `spaarkedev1` (2026-08-12, operator-approved "seed all 4").
> **Spec**: FR-D4 — *"`sprk_emailupdatefield` ships empty → Job B can propose nothing until seeded."*
>
> **Seeded records** (via Dataverse MCP `create_record`; `sprk_targetentity` lookup → `sprk_recordtype_ref`; verified the JOIN resolves to matter/project):
> | id | targetentity | targetfieldlogicalname | fieldtype |
> |---|---|---|---|
> | `672900d3-7e96-f111-b8db-0022482fb5a7` | sprk_matter | sprk_matterdescription | Memo |
> | `748976da-7e96-f111-b8db-0022482fb5a7` | sprk_matter | sprk_nextreviewdate | DateTime |
> | `768976da-7e96-f111-b8db-0022482fb5a7` | sprk_project | sprk_projectdescription | Memo |
> | `72659ce0-7e96-f111-b8db-0022482fb5a7` | sprk_project | sprk_nextreviewdate | DateTime |
>
> All `sprk_enabled=true`, `sprk_requireconfirm=true`. Expand later post-UAT.

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

## Proposed conservative starter set (VERIFIED against live schema 2026-08-12)

Field logical names + types confirmed via Dataverse MCP `describe` on `sprk_matter` + `sprk_project`.
The table `sprk_emailupdatefield` is currently **empty** (verified — 033 not yet seeded).
`sprk_fieldtype` CHOICE integers per `EmailProposalShaping.cs`: Text=100000000, Lookup=100000001,
OptionSet=100000002, Number=100000003, DateTime=100000004, Boolean=100000005, Memo=100000006, Currency=100000007.

| # | sprk_targetentity | sprk_targetfieldlogicalname | sprk_fieldtype | sprk_enabled | sprk_extractionguidance |
|---|---|---|---|---|---|
| 1 | `sprk_matter` | `sprk_matterdescription` (MULTILINE TEXT) | Memo (100000006) | true | Propose only when the email materially adds to / corrects the matter description; cite the exact sentence. |
| 2 | `sprk_matter` | `sprk_nextreviewdate` (DATE ONLY) | DateTime (100000004) | true | Propose only when the email states an explicit review/follow-up date for this matter. |
| 3 | `sprk_project` | `sprk_projectdescription` (MULTILINE TEXT) | Memo (100000006) | true | Same as #1, for projects. |
| 4 | `sprk_project` | `sprk_nextreviewdate` (DATE ONLY) | DateTime (100000004) | true | Same as #2, for projects. |

**Deliberately NOT in the starter set** (too risky for auto-propose): `statuscode`/`statecode`,
`sprk_closeddate` (implies status change), financials (budget/spend/MONEY), owner/assignment lookups,
`sprk_accesspermission`/`sprk_issecure` (privilege), and the AI-generated `*summary` rollups
(`sprk_mattersummary`/`sprk_recordsummary`/`sprk_tasksummary`/etc. — email must NOT drive those).

**Acceptance (FR-D4)**: after seeding, Job B can propose at least these field updates in `spaarkedev1`.
Job B only PROPOSES (`sprk_emailreviewlog` Proposed rows) — a human confirms every write (task 031).

## Next steps (on your approval)
1. Confirm the direction + which fields you actually want (add/remove rows).
2. I verify each `sprk_targetfieldlogicalname` exists on its target entity (`dataverse-create-schema` / MCP read).
3. Seed the approved rows via Web API (gated deploy step — with your go-ahead).

**Recommendation**: keep the starter set to 2–3 genuinely-safe fields; expand later as UAT shows what
Job B proposes well. Job B always produces *human-confirmed proposals* (never auto-writes), so the
allow-list controls scope, not final authority.
