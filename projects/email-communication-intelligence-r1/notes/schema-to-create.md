# Schema to Create — email-communication-intelligence-r1 (Phase 1)

> **For**: operator to create in `spaarkedev1` (in parallel with task-gen). Task **001** verifies these; schema tasks **011/012/013** are written verify-or-create, so if you build these they become verify-only (no collision).
> **Convention note**: Two Options = Boolean; Choice = option set (integer values auto-assign from 100000000; labels below). Add all to the project's managed solution.

---

## ✅ AS-BUILT option-set values (confirmed by operator 2026-07-29) — AUTHORITATIVE for implementation

Tables created in `spaarkedev1`. Implementation code + `TRIAGE-EMAIL` output mapping MUST use these exact values.

**`sprk_fieldtype`** (on `sprk_emailupdatefield`) — coercion hint:
| Label | Value | Coerces as (`CoerceFieldValue`) |
|---|---|---|
| Text | 100000000 | string |
| Lookup | 100000001 | entity reference |
| Option Set | 100000002 | choice (optionset int) |
| Number | 100000003 | whole **or** decimal — resolve from target-field metadata at write time |
| Date Time | 100000004 | datetime |
| Boolean | 100000005 | two-options |
| Memo | 100000006 | multiline string |
| Currency | 100000007 | money |

**`sprk_triagepriority`** (on `sprk_communication`): Urgent=100000000 · High=100000001 · Medium=100000002 · Low=100000003
**`sprk_reviewoutcome`** (on `sprk_communication`): File=100000000 · Update=100000001 · Route=100000002 · Dismiss=100000003 · Pending=100000004
**`sprk_actortype`** (on `sprk_emailreviewlog`): Machine=100000000 · Human=100000001
**`sprk_action`** (on `sprk_emailreviewlog`): Classified=100000000 · Proposed=100000001 · Approved=100000002 · Overriden=100000003 · Dismissed=100000004 · Applied=100000005

> **Deltas from the original spec (accepted as-built):** `sprk_fieldtype` uses a single **Number** (not separate WholeNumber/Decimal — resolve from field metadata) + adds **Memo**; `sprk_triagepriority` uses **Urgent/High/Medium/Low** (not Critical/High/Normal/Low). `sprk_action` label **"Overriden"** has one `d` (cosmetic; code keys on the integer value, not the label — no impact, but fixable if desired).
> **To confirm (task 001 verifies)**: the `sprk_triagecategory` **config table** (#4) + the `sprk_communication.sprk_triagecategory` **lookup**; and the two lookups `sprk_emailupdatefield.sprk_targetentity → sprk_recordtype_ref` and `sprk_communication.sprk_triagecategory`.

---

## 1. `sprk_emailupdatefield` — Job B allow-list (NEW TABLE)  ·  gates FR-09/FR-11
**Purpose**: the safety boundary — Job B may only propose an update to a field that has an **enabled** row here. Also the per-field kill-switch.
**Ownership**: Organization-owned (reference/config data). Primary name: `sprk_name`.

| Column (logical) | Display | Type | Req | Default | Notes |
|---|---|---|---|---|---|
| `sprk_name` | Name | Single Line Text (200) | Y | — | e.g. `Matter · Closing Date` |
| `sprk_targetentity` | Target Entity | **Lookup → `sprk_recordtype_ref`** | Y | — | which entity; reuses the catalog. *(Text logical-name also acceptable if simpler.)* |
| `sprk_targetfield` | Target Field | Single Line Text (100) | Y | — | field logical name, e.g. `sprk_closingdate` |
| `sprk_enabled` | Enabled | Two Options | Y | Yes | disabled rows never proposable |
| `sprk_fieldtype` | Field Type | Choice | N | — | `Text` · `Choice` · `Boolean` · `WholeNumber` · `Decimal` · `Money` · `DateTime` · `Lookup` |
| `sprk_requireconfirm` | Require Confirm | Two Options | Y | Yes | P1 always Yes (never silent) |
| `sprk_extractionguidance` | Extraction Guidance | Multiple Lines (2000) | N | — | optional per-field AI hint, e.g. *"explicit calendar dates only"* |

**Starter seed** (a few per core entity to prove the loop): Matter → `sprk_closingdate` (DateTime), status; Invoice → amount (Money), due date (DateTime), status; Project → status, key dates.

---

## 2. Triage fields on `sprk_communication` (ADD FIELDS to existing entity)  ·  FR-07

| Column (logical) | Display | Type | Req | Notes |
|---|---|---|---|---|
| `sprk_triagecategory` | Triage Category | **Lookup → `sprk_triagecategory`** (table #4) | N | human-facing category |
| `sprk_triagepriority` | Triage Priority | Choice | N | `Critical` · `High` · `Normal` · `Low` |
| `sprk_triagesummary` | Triage Summary | Multiple Lines (2000) | N | 2-line AI summary |
| `sprk_triageobligations` | Triage Obligations | Multiple Lines (4000) | N | **lean JSON** array (D-06) |
| `sprk_riconfidence` | RI Confidence | Decimal (precision 2, 0–1) | N | the RI-confidence score |
| `sprk_reviewoutcome` | Review Outcome | Choice | N | `File` · `Update` · `Route` · `Dismiss` · `Pending` |

---

## 3. `sprk_emailreviewlog` — per-email audit (NEW TABLE, append-only)  ·  FR-08
**Purpose**: defensible machine + human review record. **Append-only** (no edit/delete via UI). Query per matter via the linked communication's regarding.
**Ownership**: Organization-owned. Primary name: `sprk_name` (autonumber recommended, e.g. `ERL-{SEQNUM:00000}`).

| Column (logical) | Display | Type | Req | Notes |
|---|---|---|---|---|
| `sprk_name` | Name | Autonumber or Text | Y | |
| `sprk_communication` | Communication | Lookup → `sprk_communication` | Y | the email reviewed |
| `sprk_actortype` | Actor Type | Choice | Y | `Machine` · `Human` |
| `sprk_actor` | Actor | Single Line Text (200) | N | user id/name OR rule/model id |
| `sprk_action` | Action | Choice | Y | `Classified` · `Proposed` · `Approved` · `Overridden` · `Dismissed` · `Applied` |
| `sprk_aisuggestion` | AI Suggestion | Multiple Lines (4000) | N | prior AI suggestion (JSON) |
| `sprk_confidence` | Confidence | Decimal (precision 2, 0–1) | N | |
| `sprk_sourceref` | Source Ref | Single Line Text (500) | N | cited source / attachment locator (e.g. `OA_908068.pdf p.1`) |
| `sprk_targetentity` | Target Entity | Single Line Text (100) | N | for Job B applied updates |
| `sprk_targetrecordid` | Target Record Id | Single Line Text (100) | N | GUID as text |
| `sprk_targetfield` | Target Field | Single Line Text (100) | N | field changed |

*(Timestamp = system `createdon`; add `sprk_reviewedon` DateTime only if you want an explicit event time distinct from row creation.)*

---

## 4. `sprk_triagecategory` — taxonomy config (NEW TABLE)  ·  FR-16 (D-03)
**Purpose**: makes category taxonomy + priority weight **tuneable as data** (add/reweight without code).
**Ownership**: Organization-owned. Primary name: `sprk_name`.

| Column (logical) | Display | Type | Req | Notes |
|---|---|---|---|---|
| `sprk_name` | Name | Single Line Text (100) | Y | category label |
| `sprk_priorityweight` | Priority Weight | Whole Number | N | higher = more urgent (feeds priority) |
| `sprk_enabled` | Enabled | Two Options | Y (default Yes) | |

**Starter seed**: `Client instruction` · `Court / Filing` · `Invoice / Billing` · `Scheduling` · `Opposing counsel` · `Administrative` · `Marketing / Noise`.

*(Simpler alternative: make `sprk_communication.sprk_triagecategory` a global Choice instead of a lookup, and skip this table — but you lose the priority-weight-as-data and easy add-without-metadata-change. Recommend the table.)*

---

## Already added by operator (task 001 will verify)
- `sprk_communication.sprk_regardingreportcard` (Lookup → `sprk_reportcard`) ✅
- `sprk_recordtype_ref` RPTC row (code `RPTC`, `sprk_regardingrecordnumberfield = sprk_reportcardnumber`) ✅
- `sprk_reportcard.sprk_reportcardnumber` — confirm exists ✅
- **Hygiene (optional, task 001 flags)**: `sprk_recordtype_ref` `sprk_regardingfield` typos (`sprk_regarrdingbudget`, `sprk_egardingproject`) + the `contact`-row `sprk_recordlogicalname = sprk_contact` anomaly — the identifier rung reads these defensively, so cleanup is optional.

---

## Summary — 3 new tables + 6 fields
1. **`sprk_emailupdatefield`** (7 cols) — Job B allow-list
2. **6 triage fields** on `sprk_communication`
3. **`sprk_emailreviewlog`** (11 cols) — audit
4. **`sprk_triagecategory`** (3 cols) — taxonomy config

Deploy all to the project managed solution. `sprk_emailupdatefield` is the only hard gate for a *later* wave (Phase 3 / Job B); the rest support Phases 1–2.
