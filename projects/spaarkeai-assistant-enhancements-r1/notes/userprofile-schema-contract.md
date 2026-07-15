# `sprk_userprofile` Schema Contract (verified 2026-07-15)

> **Task**: 001 (FR-E1 verification). **Environment**: spaarkedev1. **Method**: Dataverse MCP `describe` + `search`.
> **Authoritative record** for tasks **030** (stated-profile producer — read path) and **042** (My Assistant questionnaire — write path). Use these EXACT logical names; do not guess.
> **Verification tooling note**: MCP `describe` surfaces **columns only** — it does NOT expose `EntityKeyMetadata` (alternate keys) or `RelationshipMetadata`. Alt-key + N:N were verified by other means (search for the intersect entity); the alt-key **name** could not be read and is flagged below.

## `sprk_userprofile` (collection `sprk_userprofiles`, PK `sprk_userprofileid`)

| Logical name | Type | Required | Notes | Verified |
|---|---|---|---|---|
| `sprk_name` | NVARCHAR(850) | ✅ NOT NULL | Primary name column | ✅ |
| `sprk_primaryrole` | CHOICE | — | 14 options (see below) | ✅ present ⚠️ see finding F-1 |
| `sprk_focusareas` | MULTILINE TEXT | — | User-authored free text (prompt-injection surface — task 052) | ✅ |
| `sprk_officelocation` | NVARCHAR(100) | — | | ✅ |
| `sprk_assistantpreferences` | MULTILINE TEXT | — | Canonicalize as deterministic JSON when rendered (NFR-02, task 032); prompt-injection surface (052) | ✅ |
| `sprk_profilecompletedon` | DATETIME | — | Cold-start gate key (task 042 FR-F3) | ✅ |
| `sprk_profileversion` | INT | — | | ✅ |
| `sprk_systemuser` | LOOKUP → `systemuser` | — | Relationship to user (Option B); the keyed-retrieve/upsert key | ✅ |

### `sprk_primaryrole` option set (label → value)

| Value | Label | | Value | Label |
|---|---|---|---|---|
| 100000000 | Senior Partner | | 100000007 | Practice Support |
| 100000001 | Partner | | 100000008 | Administrator |
| 100000002 | Senior Associate | | 100000009 | Legal Operations |
| 100000003 | Associate | | 100000010 | Senior Counsel |
| 100000004 | Paralegal | | 100000011 | General Counsel |
| 100000005 | Specialist | | 100000012 | Counsel |
| 100000006 | Other | | 100000013 | Associate General Counsel |

The producer (030) renders the **label** for the stored value. Whatever the global/local binding (F-1), this label↔value map is the contract.

## `sprk_practicearea_ref` (collection `sprk_practicearea_refs`, PK `sprk_practicearea_refid`)

| Logical name | Type | Required | Notes | Verified |
|---|---|---|---|---|
| `sprk_practiceareaname` | NVARCHAR(100) | ✅ NOT NULL | The resolver's (010) lookup match target + the name the producer (030) renders | ✅ |
| `sprk_practiceareacode` | NVARCHAR(10) | — | | ✅ |

## N:N relationship — `sprk_userprofile` ↔ `sprk_practicearea_ref`

**Intersect entity: `sprk_userprofile_sprk_practicearea_ref`** ✅ (found via search + describe). Intersect columns:

| Column | Role |
|---|---|
| `sprk_userprofileid` | FK → `sprk_userprofile` |
| `sprk_practicearea_refid` | FK → `sprk_practicearea_ref` |
| `sprk_userprofile_sprk_practicearea_refid` | Intersect PK |

- **Task 042** writes practice-area selections as **N:N associates** into this intersect.
- **Task 030** reads related `sprk_practicearea_ref.sprk_practiceareaname` values via this relationship.
- The relationship schema name presented to the Web API `Associate`/`Disassociate` calls is `sprk_userprofile_sprk_practicearea_ref` (confirm the exact `@odata.bind` navigation-property casing at task 042 against `$metadata`).

## Findings / carried-forward confirmations (non-blocking)

| # | Finding | Impact | Owner of follow-up |
|---|---|---|---|
| **F-1** | `sprk_primaryrole` describes as a CHOICE with **14 inline options** — the shape of a **local** option set. Owner intent (FR-E1) was to bind to the **existing GLOBAL** role choice set (reusable beyond AI). `describe` cannot distinguish global-vs-local. | If local, the "reusable global set" goal is unmet — but the label↔value map above still serves the producer. Not blocking 030. | Owner to confirm global-vs-local in the maker portal; re-bind to global if needed (schema change — separate from R1 code). |
| **F-2** | **Alternate key** on `sprk_systemuser` (Option B keyed upsert) could **not be read** — MCP `describe` does not expose `EntityKeyMetadata`. Not confirmed present OR absent. | Task 042's keyed upsert + task 030's keyed retrieve depend on it. | Confirm the alt-key logical name via maker portal / `$metadata` `EntityKeyMetadata` at **task 042** (which already carries an escalation trigger for a missing/misconfigured alt-key). |

## Escalation decision

**No escalation fired.** The task's escalation trigger is for a **confirmed-absent** column / alt-key / N:N. All FR-E1 columns, the `sprk_systemuser` lookup, `sprk_practicearea_ref`, and the N:N intersect are **verified present**. F-1 (choice binding) and F-2 (alt-key name) are **tooling-visibility gaps**, not confirmed absences — carried forward as concrete task-time confirmations (both already anticipated in `current-task.md` prerequisites + task 042's escalation trigger). Proceeding does not risk building on un-promoted columns.
