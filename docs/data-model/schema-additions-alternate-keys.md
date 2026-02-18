# Schema Additions: Alternate Keys for SaaS Portability

**Date**: 2026-02-11
**Purpose**: Add alternate key fields to support multi-environment deployments without hardcoded GUIDs

---

## Problem Statement

Primary key GUIDs (`sprk_playbookid`, `sprk_invoiceid`, etc.) change when solutions deploy to new environments (DEV → QA → PROD). This breaks hardcoded GUID references in code.

**Example**:
- DEV: Playbook "Invoice Analysis" has GUID `1e657651-9308-f111-8407-7c1e520aa4df`
- QA: Same playbook has GUID `9a8b7c6d-1234-5678-abcd-ef0123456789`
- PROD: Same playbook has GUID `4f5e6d7c-8901-2345-bcde-123456789abc`

Code using hardcoded GUID only works in one environment.

---

## Solution: Alternate Keys

Add **alternate key fields** (string) that remain stable across all environments. These provide logical, portable identifiers.

---

## Schema Changes

### 1. sprk_analysisplaybook Entity

**New Field**: `sprk_playbookcode`

| Property | Value |
|----------|-------|
| **Display Name** | Playbook Code |
| **Schema Name** | sprk_playbookcode |
| **Type** | Single Line of Text |
| **Max Length** | 50 |
| **Required** | Business Required |
| **Unique** | Yes (Alternate Key) |
| **Format** | `PB-{NNN}` (e.g., `PB-013`) |
| **Description** | Unique code for playbook - stable across all environments |

**Alternate Key Definition**:
- **Name**: `sprk_playbookcode_key`
- **Fields**: `sprk_playbookcode`
- **Purpose**: Enable lookup by code instead of GUID

**Migration**:
- Backfill existing playbooks with codes based on their order/purpose
- Example codes:
  - `PB-001`: Document Classification
  - `PB-002`: Document Profile
  - `PB-013`: Invoice Analysis (Finance)

---

### 2. Future Entities (Not MVP, but recommended pattern)

#### sprk_invoice
**New Field**: `sprk_invoicecode`
- Format: `INV-{YYYYMMDD}-{SEQUENCE}` (e.g., `INV-20260211-001`)
- Use case: Cross-reference invoices across environments for testing

#### sprk_matter
**New Field**: `sprk_mattercode`
- Format: Client-provided matter number (e.g., `M-2024-1234`)
- Use case: Lookup matters by business identifier instead of GUID

---

## Implementation Steps

### Step 1: Add Field to Dataverse (Manual)

1. Open [Power Apps Maker Portal](https://make.powerapps.com)
2. Navigate to **Solutions** → **Spaarke**
3. Open **sprk_analysisplaybook** entity
4. Add new field:
   - Display Name: `Playbook Code`
   - Schema Name: `sprk_playbookcode`
   - Type: Single Line of Text
   - Max Length: 50
   - Required: Business Required
5. **Create Alternate Key**:
   - Go to **Keys** tab
   - Click **+ New Key**
   - Name: `sprk_playbookcode_key`
   - Select field: `sprk_playbookcode`
   - Save
6. **Publish customizations**

### Step 2: Backfill Existing Records

Run this in **Power Apps Tools** or via Web API:

```javascript
// Backfill playbook codes
const playbooks = [
  { id: "1e657651-9308-f111-8407-7c1e520aa4df", code: "PB-013" } // Invoice Analysis
  // Add other playbooks as needed
];

for (const playbook of playbooks) {
  await Xrm.WebApi.updateRecord("sprk_analysisplaybook", playbook.id, {
    sprk_playbookcode: playbook.code
  });
}
```

### Step 3: Update Code (This Implementation)

- Add `RetrieveByAlternateKeyAsync` to IDataverseService
- Create `IPlaybookLookupService` with caching
- Update `InvoiceExtractionJobHandler` to use lookup service

---

## Alternate Key Lookup Performance

**Without caching**:
- Each lookup: 1 Dataverse query (~50-100ms)
- 1000 invoices/hour = 1000 queries = significant load

**With IMemoryCache (1-hour TTL)**:
- First lookup: 1 Dataverse query + cache write
- Subsequent lookups: In-memory (< 1ms)
- Cache expires after 1 hour, refreshes on next access
- Memory usage: ~1KB per cached playbook (negligible)

**Recommendation**: Always use caching for alternate key lookups in high-volume scenarios.

---

## Multi-Environment Deployment Flow

### DEV Environment (Initial Setup)
1. Create playbook "Invoice Analysis" → Gets GUID `1e657651-9308-...`
2. Set `sprk_playbookcode = "PB-013"`
3. Deploy code using `GetByCodeAsync("PB-013")`

### QA Environment (Solution Import)
1. Import solution → Playbook gets NEW GUID `9a8b7c6d-...`
2. Solution import preserves `sprk_playbookcode = "PB-013"`
3. **Same code works without changes** ✅

### PROD Environment (Solution Import)
1. Import solution → Playbook gets NEW GUID `4f5e6d7c-...`
2. Solution import preserves `sprk_playbookcode = "PB-013"`
3. **Same code works without changes** ✅

**Result**: One codebase works in all environments without config changes.

---

## Comparison with Other Approaches

| Approach | Portability | Performance | Maintenance | Verdict |
|----------|-------------|-------------|-------------|---------|
| **Hardcoded GUID** | ❌ Breaks in new environments | ✅ Direct | ❌ Config per environment | ❌ Not SaaS-ready |
| **Config file** | ⚠️ Requires config per environment | ✅ Direct | ❌ Manual mapping | ⚠️ Fragile |
| **Lookup by name** | ✅ Portable | ❌ Slow (no index) | ⚠️ Name changes break code | ⚠️ Risky |
| **Alternate keys** | ✅ Portable | ✅ Indexed | ✅ Self-documenting | ✅ **RECOMMENDED** |
| **Configuration entity** | ✅ Portable | ⚠️ Extra query | ❌ Additional complexity | ⚠️ Overkill for MVP |

---

## Testing Plan

### Unit Tests
- Verify `RetrieveByAlternateKeyAsync` calls correct Web API endpoint
- Verify cache hit/miss behavior
- Verify cache expiration after TTL

### Integration Tests
1. Create test playbook with code `PB-TEST-001`
2. Lookup by code, verify correct record returned
3. Delete and recreate with same code, verify lookup still works
4. Verify cache invalidation works correctly

### Multi-Environment Test
1. Export solution from DEV (playbook GUID = `AAA...`)
2. Import to QA (playbook GUID = `BBB...`)
3. Run code in QA using `GetByCodeAsync("PB-013")`
4. Verify correct playbook is retrieved despite different GUID

---

## Security Considerations

**Alternate keys are visible in URLs** when using Web API:
```
GET /api/data/v9.2/sprk_analysisplaybooks(sprk_playbookcode='PB-013')
```

**Recommendation**: Don't encode sensitive information in alternate key values. Use business identifiers only.

---

## Future Enhancements (Post-MVP)

1. **Auto-generate playbook codes** on creation (plugin or Power Automate)
2. **Add alternate keys to other entities** (invoice, matter, document)
3. **Build admin UI** for managing playbook codes
4. **Audit trail** for code changes (prevent breaking references)

---

*This schema change is foundational for multi-tenant SaaS deployments.*
