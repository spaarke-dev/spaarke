# 012 — `sprk_emailreviewlog` Audit Entity Verification (LIVE `spaarkedev1`)

> **Task**: 012 (P1, STANDARD, verify-or-create) · **Date**: 2026-07-29 · **Method**: Dataverse MCP `describe tables/sprk_emailreviewlog` + `read_query` (COUNT) against live `spaarkedev1`. **No schema created or altered — table already exists with the full FR-08 attribute set.**
> **Verdict**: ✅ **PRESENT.** All FR-08 columns confirmed live, matching `schema-to-create.md` §3 exactly (including the as-built "Overriden" single-d spelling and the as-built `sprk_action`/`sprk_actortype` option-set integers). Task 012 is **VERIFY-ONLY** for this run — no create/alter performed. Task 031 (Job B apply audit write) is **unblocked**.

---

## PRESENT / ABSENT

| Check | Result |
|---|---|
| Table `sprk_emailreviewlog` exists | ✅ PRESENT |
| Collection name | `sprk_emailreviewlogs` |
| Description (live) | "Track email review and triage activities" |
| Current row count | 0 (no rows written yet — expected; task 031 writes rows) |
| All FR-08 required columns present | ✅ YES (11/11 — see inventory below) |
| Any missing/mismatched column vs `schema-to-create.md` §3 | ❌ NONE — exact match |

---

## Verbatim column inventory (from live `describe`)

```
DESCRIBE TABLE sprk_emailreviewlog (
  createdby LOOKUP (GUID) ( Related table : systemuser),
  createdon DATETIME,
  createdonbehalfby LOOKUP (GUID) ( Related table : systemuser),
  importsequencenumber INT,
  modifiedby LOOKUP (GUID) ( Related table : systemuser),
  modifiedon DATETIME,
  modifiedonbehalfby LOOKUP (GUID) ( Related table : systemuser),
  overriddencreatedon DATE ONLY,
  ownerid OWNER,
  owningbusinessunit LOOKUP (GUID) ( Related table : businessunit),
  owningteam LOOKUP (GUID) ( Related table : team),
  owninguser LOOKUP (GUID) ( Related table : systemuser),
  sprk_action CHOICE (Options: Classified (100000000), Proposed (100000001), Approved (100000002), Overriden (100000003), Dismissed (100000004), Applied (100000005)),
  sprk_actor NVARCHAR(200),
  sprk_actortype CHOICE (Options: Machine (100000000), Human (100000001)),
  sprk_aisuggestion MULTILINE TEXT,
  sprk_communication LOOKUP (GUID) ( Related table : sprk_communication),
  sprk_confidence DECIMAL,
  sprk_emailreviewlogid GUID,
  sprk_name NVARCHAR(850) NOT NULL,
  sprk_sourceref NVARCHAR(1000),
  sprk_targetentity NVARCHAR(100),
  sprk_targetfield NVARCHAR(100),
  sprk_targetrecordid NVARCHAR(100),
  statecode STATE (INT) (Valid Options: Active (0), Inactive (1)),
  statuscode STATUS (INT) (Valid Options: Active (1), Inactive (2)),
  timezoneruleversionnumber INT,
  utcconversiontimezonecode INT,
  versionnumber BIGINT
);
```

(Platform/system columns — `createdby`, `createdon`, `modifiedby`, `modifiedon`, `ownerid`, `statecode`, `statuscode`, etc. — are standard Dataverse table scaffolding, not FR-08-specific; listed for completeness.)

---

## FR-08 attribute checklist — schema-to-create.md §3 vs live (VERBATIM MATCH)

| FR-08 / schema-doc column | Live logical name | Live type | Match? |
|---|---|---|---|
| `sprk_name` (primary; item label) | `sprk_name` | NVARCHAR(850) NOT NULL | ✅ exact name |
| `sprk_communication` (item lookup) | `sprk_communication` | LOOKUP (GUID) → `sprk_communication` | ✅ exact name + target |
| `sprk_actortype` (Choice: Machine/Human) | `sprk_actortype` | CHOICE — Machine=100000000, Human=100000001 | ✅ exact name + values |
| `sprk_actor` (actor id text) | `sprk_actor` | NVARCHAR(200) | ✅ exact name + length |
| `sprk_action` (Choice: 6-value closed set) | `sprk_action` | CHOICE — Classified=100000000, Proposed=100000001, Approved=100000002, **Overriden**=100000003, Dismissed=100000004, Applied=100000005 | ✅ exact name + values (note: label is "Overriden", single **d** — cosmetic, code keys on the integer not the label) |
| `sprk_aisuggestion` (JSON multiline) | `sprk_aisuggestion` | MULTILINE TEXT | ✅ exact name |
| `sprk_confidence` (decimal 0–1) | `sprk_confidence` | DECIMAL | ✅ exact name |
| `sprk_sourceref` (source locator) | `sprk_sourceref` | NVARCHAR(1000) (schema doc said 500 — live is 1000, a **superset**, no functional impact) | ✅ exact name (length delta: live is more generous) |
| `sprk_targetentity` (Job B target entity) | `sprk_targetentity` | NVARCHAR(100) | ✅ exact name |
| `sprk_targetrecordid` (Job B target record GUID-as-text) | `sprk_targetrecordid` | NVARCHAR(100) | ✅ exact name |
| `sprk_targetfield` (Job B target field) | `sprk_targetfield` | NVARCHAR(100) | ✅ exact name — **note this is a DIFFERENT table from `sprk_emailupdatefield`**; on `sprk_emailreviewlog` the live name IS `sprk_targetfield` (unlike `sprk_emailupdatefield.sprk_targetfieldlogicalname` per task 001's delta table — do not conflate the two tables' naming) |
| Timestamp | `createdon` (platform) | DATETIME | ✅ — no explicit `sprk_reviewedon` was added; platform `createdon` is sufficient per schema-doc's parenthetical note |

**No deltas from `schema-to-create.md` §3.** The only cosmetic note is `sprk_sourceref` being NVARCHAR(1000) live vs the doc's suggested 500 — a superset, not a mismatch, no action needed.

---

## Exact logical names task 031 (Job B apply) MUST bind to

| Purpose | Exact logical name | Type / target |
|---|---|---|
| Table | `sprk_emailreviewlog` | (collection: `sprk_emailreviewlogs`) |
| Reviewed item | `sprk_communication` | LOOKUP → `sprk_communication` |
| Actor type | `sprk_actortype` | CHOICE — Machine=100000000, Human=100000001 |
| Actor identity | `sprk_actor` | NVARCHAR(200) — free text (user id/name or rule/model id) |
| Action taken | `sprk_action` | CHOICE — Classified=100000000, Proposed=100000001, Approved=100000002, **Overriden**=100000003 (single-d spelling — code keys on int), Dismissed=100000004, Applied=100000005 |
| Prior AI suggestion (JSON) | `sprk_aisuggestion` | MULTILINE TEXT |
| Confidence at proposal time | `sprk_confidence` | DECIMAL |
| Source citation locator | `sprk_sourceref` | NVARCHAR(1000) |
| Job B target entity | `sprk_targetentity` | NVARCHAR(100) — **NOT the same field as `sprk_emailupdatefield.sprk_targetfieldlogicalname`; this is a plain text field on the LOG row, distinct table** |
| Job B target record | `sprk_targetrecordid` | NVARCHAR(100) — GUID as text |
| Job B target field | `sprk_targetfield` | NVARCHAR(100) |
| Timestamp | `createdon` | platform DATETIME (no explicit `sprk_reviewedon` exists — use `createdon`) |

For a Job B "Applied" row, 031 should write: `sprk_action = Applied (100000005)`, `sprk_actortype = Human (100000001)` (the confirming user) or `Machine (100000000)` if fully automated, `sprk_targetentity`/`sprk_targetrecordid`/`sprk_targetfield` populated, `sprk_aisuggestion` carrying the old→new JSON, `sprk_sourceref` citing the source text.

---

## Append-only semantics — NOT independently verifiable via MCP read tools

MCP `describe`/`read_query` cannot inspect security-role privilege configuration (create/read-only vs update/delete grants) or form customizations. The live column list shows standard `statecode`/`statuscode` (Active/Inactive) — no structural evidence of a custom "locked" mechanism, which is expected (append-only is typically enforced via security-role privilege removal, not schema shape). **This task did not check/alter security roles** — per the parent instruction to remain read-only and verify-only when the table already exists. If append-only enforcement (no update/delete privilege for normal actors) has not yet been configured on the security role(s), that is a follow-up for whoever finalizes the deploy (060) or for the operator — flagging as a **non-blocking note**, not a schema gap.

---

## Delta / blocker summary

- **Delta vs schema doc**: none functional. `sprk_sourceref` is NVARCHAR(1000) live vs 500 suggested (superset, fine). `sprk_action` label "Overriden" (single d) is cosmetic per task 001/schema-doc — already flagged as accepted-as-built.
- **Blocker**: NONE. Table exists with 100% of FR-08 required columns, matching option-set values exactly as recorded in `schema-to-create.md` §3.
- **Not verified (out of MCP read-tool scope)**: append-only security-role enforcement (no update/delete for normal actors). Flag for 060 (deploy) or operator confirmation — does not block 031.

---

## Downstream unblock status

| Task | Depends on | Status |
|---|---|---|
| 031 (Job B apply audit write) | `sprk_emailreviewlog` schema | ✅ **UNBLOCKED** — bind to exact logical names above |
| 060 (deploy) | append-only role config (unverified) | ⚠️ note: confirm security-role privileges before/at deploy; not a schema blocker |
