# Wave 0 Discovery Report — SRFR-001

> **Task**: SRFR-001 · **Executed**: 2026-07-02 · **Env**: spaarkedev1 (via MCP Dataverse)
> **Purpose**: Close Q-06 + Q-07 residuals and reconfirm two pre-checks (ENTITY_LOOKUP_CONFIGS callers, presave enumeration) before Wave 1 starts.

---

## 🚨 CRITICAL: Spec/reality divergences (unplanned findings)

Wave 0 investigation surfaced **7 material divergences** between `spec.md` (especially Appendix A) and the actual Dataverse schema / catalog data. These are **NOT** in the original Q-06/Q-07 scope but WILL invalidate downstream Wave tasks if not resolved. Escalation to owner required.

### D-1: `sprk_fieldmappingprofile` schema — spec §A.2.1 is wrong

**Spec §A.2.1 claims**:
| Field | Type |
|---|---|
| `sprk_sourceentity` | Text (logical name) |
| `sprk_targetentity` | Text (logical name) |
| `sprk_syncmode` | OptionSet (`OneTime` \| `ManualRefresh`) |

**Real schema** (MCP describe 2026-07-02):
| Field | Type |
|---|---|
| `sprk_sourcerecordtype` | **LOOKUP to `sprk_recordtype_ref`** |
| `sprk_targetrecordtype` | **LOOKUP to `sprk_recordtype_ref`** |
| `sprk_compatibilitymode` | Choice: `Strict (0)` \| `Resolve (1)` |
| `sprk_defaultvalue` | NVARCHAR(1000) |
| `sprk_description` | Multiline text |
| *(no `sprk_syncmode` at all)* | — |

**Impact**:
- **Wave 6 task 060** (MDA form) — spec's proposed columns are WRONG. Form must expose the actual lookups + `sprk_compatibilitymode`.
- **Wave 6 task 061** (push webresource) — profile query must use `_sprk_sourcerecordtype_value eq {guid-of-source-recordtype-ref}`, NOT `sprk_sourceentity eq 'X'`. Requires two-step resolution: entity name → `sprk_recordtype_ref` GUID → profiles.
- **Wave 6 task 062** (ribbon `hasSourceProfile`) — same query rewrite.
- Spec Appendix A §A.2, §A.3, §A.5 all need substantial rewrites.

### D-2: `sprk_fieldmappingrule` schema — spec §A.2.2 is wrong (table name too)

**Table name**: `sprk_fieldmappingrule` (SINGULAR). Spec §A.2.2 heading is `sprk_fieldmappingrules` (plural — collection name; table is singular).

**Spec §A.2.2 claims a `sprk_mappingtype` field**: `Copy` \| `Default` \| `Concat` \| `Template`. **This field does NOT exist**.

**Real schema**:
| Field | Type | Purpose |
|---|---|---|
| `sprk_fieldmappingprofile` | LOOKUP → sprk_fieldmappingprofile | Parent link |
| `sprk_sourcefield` | NVARCHAR(100) | ✅ matches spec |
| `sprk_targetfield` | NVARCHAR(100) | ✅ matches spec |
| `sprk_executionorder` | INT | ✅ matches spec |
| `sprk_defaultvalue` | NVARCHAR(100) | ✅ matches spec |
| `sprk_sourcefieldtype` | Choice: Text/Lookup/OptionSet/Number/DateTime/Boolean/Memo | NEW — schematic type |
| `sprk_targetfieldtype` | Choice: Text/Lookup/OptionSet/Number/DateTime/Boolean/Memo | NEW — schematic type |
| `sprk_syncmode` | Choice: **`One-time (0)` \| `Manual Refresh (1)`** — **per-rule, NOT per-profile** | See D-3 |
| `sprk_compatibilitymode` | Choice: Strict/Resolve — also per-rule | NEW |
| `sprk_mappingdirection` | Choice: Parent-to-Child (0) \| Child-to-Parent (1) \| Bidirectional (2) | NEW |
| `sprk_iscascadingsource` | Bit | NEW — per-rule cascade flag |
| `sprk_isrequired` | Bit | NEW |

**Impact**: Wave 6 task 060 (MDA form subgrid columns) must be rewritten to match. Wave 6 task 061 push-updates iteration must respect per-rule syncmode + mappingdirection + iscascadingsource + isrequired semantics — none of which are in the spec.

### D-3: Sync-mode semantic divergence — per-RULE vs per-PROFILE

Spec §A.3 says "One profile per source→target entity pair" with `sprk_syncmode` at profile level. Reality: syncmode is per-RULE. Different rules within one profile can have different sync modes. **This is a semantic feature difference**, not just a naming diff.

**Impact**: Wave 6 task 061's `pushUpdates()` logic — spec assumes "if profile.syncmode = ManualRefresh, push all rules". Reality: must iterate rules and honor each rule's own syncmode. The `One-time` rules would be skipped on the second push.

### D-4: Data-quality typos in `sprk_recordtype_ref.sprk_regardingfield`

Three catalog rows have typo values that will cause runtime failures for the data-driven resolver:

| `sprk_recordlogicalname` | `sprk_regardingfield` (catalog value) | Correct value |
|---|---|---|
| `sprk_project` | `sprk_egardingproject` ⚠️ (missing 'r') | `sprk_regardingproject` |
| `sprk_budget` | `sprk_regarrdingbudget` ⚠️ (extra 'r') | `sprk_regardingbudget` |
| `sprk_billinganalysis` | `sprk_regardingbillinganaysis` ⚠️ (missing 'l') | `sprk_regardingbillinganalysis` |

**Impact**: `PolymorphicResolverService.applyResolverFields()` reads `sprk_regardingfield` and uses it as the target lookup name on the host. For Project, Budget, Billing Analysis this value doesn't match any real column → Web API PATCH will return 404 or "unknown attribute" errors.

**Recommendation**: **Add data-fix step to Wave 0 task 002** (previously only populate `sprk_regardingrecordnumberfield`; now also fix these 3 typos). OR run a one-time fixup as part of SRFR-001's follow-up.

### D-5: `sprk_regardingrecordnumberfield` — EMPTY for all rows including Matter

Owner clarification on 2026-07-02 stated "Matter is populated; create for other entities". Reality (MCP query `WHERE sprk_regardingrecordnumberfield IS NOT NULL` returned zero rows):

**`sprk_regardingrecordnumberfield` is null for ALL 13 catalog rows including Matter.**

**Impact**: Task 002 (FR-A4-02) previously scoped to "10 non-Matter entities". Correct scope is **ALL 13 rows** (or 12 if Matter's expected value differs from spec's expectation of `sprk_matternumber`). Task 002 estimate remains 2h but now includes Matter.

### D-6: Contact entity ambiguity — catalog claims `sprk_contact` (nonexistent)

Catalog row for "Person" has `sprk_recordlogicalname = "sprk_contact"`, but `sprk_contact` **does not exist as a Dataverse table**. Simultaneously, the AssociationResolver's hardcoded `ENTITY_LOOKUP_CONFIGS` uses `contact` (OOB) as the logical name for Contact/Person.

Two possibilities:
1. **Catalog error** — the value should be `contact` (OOB). Fix in Wave 0 task 002.
2. **Planned custom entity** — a `sprk_contact` Spaarke entity is planned but not yet created. If so, schema-creation belongs in scope OR needs its own follow-on project.

**Recommendation**: Ask owner. Default assumption based on existing AssociationResolver code = OOB `contact`. Fix catalog to `contact` in task 002.

### D-7: 13 record types, not 11 — extra `To Do` + `Billing Analysis`

Spec claims 11 target entities. Catalog has 13 active rows:

| # | recordtype name | logical name | Notes |
|---|---|---|---|
| 1 | To Do | sprk_todo | **HOST entity, not target** — do these have `sprk_regardingrecordnumber` needs? |
| 2 | Matter | sprk_matter | ✅ target |
| 3 | Project | sprk_project | ✅ target |
| 4 | Analysis | sprk_analysis | ✅ target |
| 5 | Document | sprk_document | ✅ target |
| 6 | Organization | sprk_organization | ✅ target |
| 7 | Person | sprk_contact (does not exist — see D-6) | ✅ target |
| 8 | Event | sprk_event | ✅ target |
| 9 | Invoice | sprk_invoice | ✅ target |
| 10 | Budget | sprk_budget | ✅ target |
| 11 | Billing Analysis | sprk_billinganalysis | **New entry not in spec** — needs `sprk_regardingrecordnumber` column too? |
| 12 | Account | account | ✅ target |
| 13 | Work Assignment | sprk_workassignment | ✅ target |

**Impact**:
- **Wave 1 task 010** (schema add) scope: 10 non-Matter → **actually 11 non-Matter** if Billing Analysis is a target (12 total including Matter which spec says has it — see D-5 which contradicts). Owner clarification needed.
- **Wave 6 task 060** MDA form + `sprk_todo` inclusion: since To Do is a HOST (not a regarding target), does it belong in this catalog? Might explain why spec said 11 (excluded To Do).

---

## Deliverable 1 — Q-06: Contact + Account target-field recommendation

### Account (OOB entity)
**Recommendation**: **`accountnumber`** (OOB, NVARCHAR 20).

**Rationale**: OOB `account` table has a purpose-built `accountnumber` text field for exactly this use case. It matches the "record number" semantic of `sprk_matternumber`, `sprk_projectnumber`, etc. No new column needed on the OOB entity (CLAUDE.md §11 compliant — reuse existing).

### Contact (per catalog: `sprk_contact` — but see D-6 above)
**Recommendation**: Depends on D-6 resolution.

**Scenario A** — catalog corrected to OOB `contact`:
- OOB `contact` has NO natural "contact number" text field.
- Candidates:
  - `employeeid` NVARCHAR(50) — works for employees but semantic mismatch for general contacts
  - `externaluseridentifier` NVARCHAR(50) — semantic match but rarely populated
  - `governmentid` — PII; **avoid**
  - `fullname` — display, not identifier
- **Preferred**: **graceful-blank** per NFR-06 (metadata-null case = warn + skip). No column addition; layout renders number cell blank for Contact records. CLAUDE.md §11 compliance — no new OOB column added.
- **Alternative if owner wants a value**: `employeeid` for employee-type contacts; graceful-blank otherwise.

**Scenario B** — custom `sprk_contact` entity is planned:
- Blocked until owner confirms whether creating this entity is in scope. If yes, add `sprk_contactnumber` text field alongside the entity creation. If no, out-of-scope for this project.

**Recommendation**: Escalate D-6 to owner FIRST; graceful-blank is the safe default until resolved.

---

## Deliverable 2 — Q-07: Ribbon scope inventory

MCP query on 2026-07-02: `WHERE statecode = 0` on `sprk_fieldmappingprofile`.

**2 active profiles**:

| # | sprk_name | Source recordtype ref | Target recordtype ref | Compatibility Mode |
|---|---|---|---|---|
| 1 | Matter to Event | `e8547bb4-...` → **Matter** (`sprk_matter`) | `5e9b37ea-...` → Event (`sprk_event`) | — (default) |
| 2 | Project to Event | `ca68b3bb-...` → **Project** (`sprk_project`) | `5e9b37ea-...` → Event (`sprk_event`) | — (default) |

**Ribbon deploy scope for Wave 6 task 062**: **BOTH Matter AND Project main forms**.

Neither profile currently has any `sprk_fieldmappingrule` records queried yet — recommend Wave 6 verification that rules exist BEFORE UAT (task 084 UAT scenario 1 depends on this).

---

## Deliverable 3 — ENTITY_LOOKUP_CONFIGS caller audit

### External-consumer grep (looking for anything outside `src/client/pcf/AssociationResolver/`)

Full-tree grep for `ENTITY_LOOKUP_CONFIGS` returns **zero external consumers**. All 7 matches are inside `src/client/pcf/AssociationResolver/handlers/RecordSelectionHandler.ts`:

| Line | Match | Purpose |
|---|---|---|
| 46 | `const ENTITY_LOOKUP_CONFIGS: EntityLookupConfig[] = [` | Definition |
| 189 | `// This replaces the hardcoded ENTITY_LOOKUP_CONFIGS` | Comment |
| 226 | `dynamicEntityConfigs = [...ENTITY_LOOKUP_CONFIGS];` | Fallback assignment (catch branch) |
| 231 | `dynamicEntityConfigs = [...ENTITY_LOOKUP_CONFIGS];` | Fallback assignment (empty-response branch) |
| 245 | `return dynamicEntityConfigs \|\| ENTITY_LOOKUP_CONFIGS;` | `getEntityConfigs()` — ALREADY dynamic-first |
| 528 | `return ENTITY_LOOKUP_CONFIGS.find(c => c.logicalName === logicalName);` | `getEntityConfig()` — direct, needs transition |
| 535 | `return [...ENTITY_LOOKUP_CONFIGS];` | `getAllEntityConfigs()` — direct, needs transition |

### `EntityLookupConfig` INTERFACE grep

11 matches, of which 3 are outside `RecordSelectionHandler.ts`:

| File | Line | Purpose |
|---|---|---|
| `AssociationResolverApp.tsx` | 44 | Import |
| `AssociationResolverApp.tsx` | 57 | Comment |
| `AssociationResolverApp.tsx` | 58 | `type EntityConfig = EntityLookupConfig;` alias |

**Confirmation**: The interface IS externally consumed (by `AssociationResolverApp.tsx`). It MUST stay exported. Only the CONST is retired.

### Correction to spec FR-B4-01

Spec says "two internal call sites need transition (`getEntityConfig` L527, `getAllEntityConfigs` L534)". Actual line numbers: **L528 and L535** (off-by-one).

Additionally, spec doesn't explicitly mention the FALLBACK assignments at L226 and L231. Those two are the ACCEPTABLE fallback path (catch/empty branch) and can EITHER (a) be removed together with the const (would need to change `loadEntityConfigs` catch/empty branches to throw or return null) OR (b) preserved as a safety net for offline/error scenarios.

**Recommendation for task 050**:
- Transition L528 (`getEntityConfig`) and L535 (`getAllEntityConfigs`) to dynamic-first.
- Preserve the L226/L231 fallback branches (safety net for catch/empty webApi responses).
- Delete `ENTITY_LOOKUP_CONFIGS` const at L46 **only** if the fallback branches are also rewritten to another safe mechanism (throw + user-visible error, or return `[]`). Otherwise the fallback remains functional.

---

## Deliverable 4 — Presave enumeration confirmation

Read of `src/client/webresources/js/sprk_todo_regarding_presave.js` on 2026-07-02:

| Item | FR-A5-04 claim | Reality | Match? |
|---|---|---|---|
| `TEXT_FIELDS` array | 3 entries: `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl` | Line 103: exactly those 3 entries | ✅ |
| `textKeyForField()` switch | Maps field logical names → payload keys | Line 258: switch statement with 3 cases (recordId, recordName, recordUrl) | ✅ |
| Pending payload docstring | Lines 37-46 | Confirmed lines 37-46 with expected shape | ✅ |
| `VERSION` constant | `1.1.0` | Line 97: `ns.VERSION = "1.1.0";` | ✅ |

**Result**: FR-A5-04's 5-step targeted-update plan for task 040 remains **fully accurate**. No corrections needed.

---

## Consolidated action items

### Immediate — before Waves 1+ begin
1. **Escalate D-1 through D-7 to owner** — all 7 divergences require owner input before Wave 6 (and D-4/D-5/D-6/D-7 also affect Wave 0 task 002 scope + Wave 1 task 010 scope).
2. **Update spec.md**:
   - Rewrite Appendix A §A.2.1, §A.2.2 to match real schema (D-1, D-2)
   - Rewrite Appendix A §A.3 for per-rule syncmode semantics (D-3)
   - Update ADR-024 note if the 5-field write set is affected (unlikely — write set is on host entity, not on field-mapping schema)
3. **Wave 0 task 002 scope expansion**:
   - Original scope: populate `sprk_regardingrecordnumberfield` for 10 non-Matter entities
   - New scope: populate for **all 12+ target rows including Matter** (D-5)
   - ADD: fix `sprk_regardingfield` typos for Project, Budget, Billing Analysis (D-4)
   - ADD: resolve Contact catalog value (`sprk_contact` → `contact` OR create custom entity) (D-6)
   - Effort adjustment: 2h → ~3h to accommodate data-fixes + Contact investigation
4. **Wave 1 task 010 scope adjustment**:
   - Original scope: add `sprk_regardingrecordnumber` to 10 entities
   - New scope: verify Matter status; add to remaining including Billing Analysis (**11 entities** if Billing Analysis is a target, or 10 if BA is out-of-scope)
   - Effort: unchanged (4h)
5. **Wave 6 task 060 scope substantial rewrite**:
   - MDA form must expose actual `sprk_fieldmappingprofile` fields (lookups + compatibilitymode + description), not spec's incorrect text-field + syncmode set
   - Subgrid columns must match real `sprk_fieldmappingrule` fields (10 fields, not 5)
6. **Wave 6 task 061 substantial rewrite**:
   - Profile query rewrite for lookup-based filter
   - Per-rule syncmode iteration logic
   - Consider `sprk_mappingdirection`, `sprk_iscascadingsource`, `sprk_isrequired` semantics in push logic
7. **Wave 6 task 062 minor rewrite**:
   - `hasSourceProfile` query uses lookup filter instead of text-field filter
   - Deploy to Project form in addition to Matter (Q-07 finding)

### Task 001 completion
All four original Q-06/Q-07/const-audit/presave-reconfirm deliverables satisfied above. Ready to complete SRFR-001.

### Recommended follow-up sequencing
1. Present divergence findings D-1..D-7 to owner (this report).
2. Owner decisions:
   - a. Approve spec.md rewrite pass (D-1..D-3)?
   - b. Contact resolution (D-6)?
   - c. Billing Analysis in scope (D-7)?
   - d. Data-fix authorization for D-4 typos?
3. Post-owner-approval: proceed to Wave 0 task 002 with expanded scope.

---

## Evidence appendix

### MCP query results (verbatim)

**Q-07 active profiles**:
```sql
SELECT sprk_fieldmappingprofileid, sprk_name, sprk_sourcerecordtype, sprk_targetrecordtype, sprk_compatibilitymode, statecode
FROM sprk_fieldmappingprofile WHERE statecode = 0;
```
Returns 2 rows: `Matter to Event` and `Project to Event`.

**D-5 catalog population check**:
```sql
SELECT sprk_recordtypename, sprk_recordlogicalname, sprk_regardingrecordnumberfield
FROM sprk_recordtype_ref WHERE sprk_regardingrecordnumberfield IS NOT NULL;
```
Returns **0 rows**.

### File-inspection evidence

**`sprk_todo_regarding_presave.js`**:
- L97: `ns.VERSION = "1.1.0";`
- L103: `var TEXT_FIELDS = ["sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl"];`
- L258: `function textKeyForField(fieldName) {`

**`RecordSelectionHandler.ts`** (grep of `ENTITY_LOOKUP_CONFIGS`): 7 matches in a single file; 0 external consumers.

**`AssociationResolverApp.tsx`**:
- L44: `EntityLookupConfig,` (import)
- L58: `type EntityConfig = EntityLookupConfig;`
