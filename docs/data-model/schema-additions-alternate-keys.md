# Alternate Keys for SaaS Portability

> **Last Updated**: April 5, 2026
> **Original Date**: 2026-02-11
> **Purpose**: Reference document for alternate key definitions, their usage in code, and the multi-environment deployment pattern they enable.

---

## Problem Statement

Primary key GUIDs (`sprk_analysisplaybookid`, `sprk_invoiceid`, etc.) change when solutions deploy to new environments (DEV -> QA -> PROD). This breaks hardcoded GUID references in code.

**Example**:
- DEV: Playbook "Invoice Analysis" -> GUID `1e657651-9308-f111-8407-7c1e520aa4df`
- QA: Same playbook -> GUID `9a8b7c6d-1234-5678-abcd-ef0123456789`
- PROD: Same playbook -> GUID `4f5e6d7c-8901-2345-bcde-123456789abc`

**Solution**: Add **alternate key fields** (string) that remain stable across all environments, providing logical, portable identifiers.

---

## Alternate Keys Catalog

### Reference / Configuration Entities

These alternate keys enable code to reference configuration records without hardcoded GUIDs.

| Entity | Alternate Key Field(s) | Key Name | Format | Purpose |
|---|---|---|---|---|
| `sprk_analysisplaybook` | `sprk_playbookcode` | `sprk_playbookcode_key` | `PB-{NNN}` (e.g., `PB-013`) | Cross-environment playbook lookup |
| `sprk_analysisaction` | `sprk_actioncode` | `sprk_actioncode_key` | (TBD) | Cross-environment action lookup |
| `sprk_analysisskill` | `sprk_skillcode` | `sprk_skillcode_key` | (TBD) | Cross-environment skill lookup |
| `sprk_analysistool` | `sprk_toolcode` | `sprk_toolcode_key` | (TBD) | Cross-environment tool lookup |

### Idempotency / Upsert Entities

These alternate keys are composite keys used for idempotent upsert operations. Re-running a job should not create duplicates.

| Entity | Alternate Key Fields | Purpose |
|---|---|---|
| `sprk_billingevent` | `sprk_invoice` + `sprk_linesequence` | Idempotent invoice line re-extraction |
| `sprk_spendsnapshot` | `sprk_matter` + `sprk_periodtype` + `sprk_periodkey` + `sprk_bucketkey` + `sprk_visibilityfilter` (matter-scoped) | Idempotent snapshot regeneration |
| `sprk_spendsnapshot` | `sprk_project` + `sprk_periodtype` + `sprk_periodkey` + `sprk_bucketkey` + `sprk_visibilityfilter` (project-scoped) | Idempotent snapshot regeneration |
| Processing Job entity | `sprk_idempotencykey` | Idempotency key for background jobs |

---

## Service Usage Map

Which BFF services use each alternate key:

| Alternate Key | Service / File | Method | Call Site |
|---|---|---|---|
| `sprk_playbookcode` | `PlaybookLookupService` | `GetByCodeAsync(string)` | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookLookupService.cs` |
| `sprk_playbookcode` | `InvoiceExtractionJobHandler` | `_playbookLookup.GetByCodeAsync("PB-013")` | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs:310` |
| `sprk_actioncode` | `ActionLookupService` | `GetByCodeAsync(string)` | `src/server/api/Sprk.Bff.Api/Services/Ai/ActionLookupService.cs` |
| `sprk_skillcode` | `SkillLookupService` | `GetByCodeAsync(string)` | `src/server/api/Sprk.Bff.Api/Services/Ai/SkillLookupService.cs` |
| `sprk_toolcode` | `ToolLookupService` | `GetByCodeAsync(string)` | `src/server/api/Sprk.Bff.Api/Services/Ai/ToolLookupService.cs` |
| SpendSnapshot 5-field | `SpendSnapshotService` | `CreateSnapshotEntityForMatter()`, `CreateSnapshotEntityForProject()` | `src/server/api/Sprk.Bff.Api/Services/Finance/SpendSnapshotService.cs` |
| SpendSnapshot 5-field | `SpendSnapshotGenerationJobHandler` | Upserts via `SpendSnapshotService` | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/SpendSnapshotGenerationJobHandler.cs` |
| `sprk_idempotencykey` | `IProcessingJobService` | `GetProcessingJobByIdempotencyKeyAsync(string)` | `src/server/shared/Spaarke.Dataverse/IProcessingJobService.cs` |

---

## Core Infrastructure

All alternate key lookups go through a single interface in `Spaarke.Dataverse`:

```csharp
// IGenericEntityService.cs
Task<Entity> RetrieveByAlternateKeyAsync(
    string entityLogicalName,
    KeyAttributeCollection alternateKeyValues,
    string[]? columns = null,
    CancellationToken ct = default);
```

**Implementation**:
- `DataverseServiceClientImpl.RetrieveByAlternateKeyAsync()` (line 1805) -- **primary implementation**, uses `RetrieveRequest` with `KeyAttributes` via ServiceClient
- `DataverseWebApiService.RetrieveByAlternateKeyAsync()` (line 2121) -- **not implemented**, throws `NotImplementedException` directing callers to the ServiceClient implementation

**Caveat**: Alternate key lookups require ServiceClient mode. The Web API path is not currently supported for this operation.

---

## Canonical Usage Pattern: Playbook Lookup

```csharp
// 1. Inject the lookup service
public class InvoiceExtractionJobHandler
{
    private readonly IPlaybookLookupService _playbookLookup;

    public InvoiceExtractionJobHandler(IPlaybookLookupService playbookLookup)
    {
        _playbookLookup = playbookLookup;
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        // 2. Lookup by stable code, not GUID
        var playbook = await _playbookLookup.GetByCodeAsync("PB-013", ct);

        // 3. Use the resolved playbook
        // ... orchestration logic
    }
}
```

**Under the hood** (`PlaybookLookupService.GetByCodeAsync`):

```csharp
var alternateKeyValues = new KeyAttributeCollection
{
    { "sprk_playbookcode", playbookCode }
};

var entity = await _genericEntityService.RetrieveByAlternateKeyAsync(
    "sprk_analysisplaybook",
    alternateKeyValues,
    columns: new[] { "sprk_analysisplaybookid", "sprk_name", "sprk_playbookcode", /* ... */ },
    ct);
```

---

## Canonical Usage Pattern: SpendSnapshot Upsert

`SpendSnapshotService` creates snapshot entities with a 5-field alternate key for idempotent regeneration:

```csharp
// Matter-scoped snapshot
var keyAttributes = new KeyAttributeCollection
{
    { "sprk_matter", matterId },
    { "sprk_periodtype", periodType },     // OptionSet: Month | ToDate
    { "sprk_periodkey", periodKey },       // e.g. "2026-02"
    { "sprk_bucketkey", DefaultBucketKey }, // "TOTAL" for MVP
    { "sprk_visibilityfilter", DefaultVisibilityFilter }
};

var snapshot = new Entity("sprk_spendsnapshot", keyAttributes);
// ... populate fields ...
await _genericEntityService.UpsertAsync(snapshot, ct);
```

**Note**: The actual field names in code differ from the originally-documented names in earlier versions of this doc. The authoritative field names are:
- `sprk_periodtype` (Choice: Month / ToDate) -- NOT `sprk_snapshotperiod`
- `sprk_periodkey` (Text: e.g. `2026-02`) -- NOT `sprk_periodvalue`
- `sprk_bucketkey` (Text: default `TOTAL`)
- `sprk_visibilityfilter` (Text)

---

## Caching Strategy

Lookups via alternate key benefit significantly from caching since configuration records change rarely:

**Without caching**:
- Each lookup: 1 Dataverse query (~50-100ms)
- High-volume scenarios (1000 invoices/hour = 1000 queries)

**With `IMemoryCache` (1-hour TTL)**:
- First lookup: 1 Dataverse query + cache write
- Subsequent lookups: In-memory (< 1ms)
- Memory footprint: ~1KB per cached playbook (negligible)

**Recommendation**: Always use caching for configuration alternate key lookups (playbook, action, skill, tool). Do NOT cache idempotency-style lookups (they must hit Dataverse to detect concurrent upserts).

`PlaybookLookupService`, `ActionLookupService`, `SkillLookupService`, and `ToolLookupService` all implement memory-cached reads per `FinanceModule.cs` DI registration comments.

---

## Multi-Environment Deployment Flow

### DEV Environment (Initial Setup)
1. Create playbook "Invoice Analysis" -> Gets GUID `1e657651-9308-...`
2. Set `sprk_playbookcode = "PB-013"`
3. Deploy code using `GetByCodeAsync("PB-013")`

### QA Environment (Solution Import)
1. Import solution -> Playbook gets NEW GUID `9a8b7c6d-...`
2. Solution import preserves `sprk_playbookcode = "PB-013"`
3. **Same code works without changes**

### PROD Environment (Solution Import)
1. Import solution -> Playbook gets NEW GUID `4f5e6d7c-...`
2. Solution import preserves `sprk_playbookcode = "PB-013"`
3. **Same code works without changes**

---

## Comparison with Other Approaches

| Approach | Portability | Performance | Maintenance | Verdict |
|----------|-------------|-------------|-------------|---------|
| Hardcoded GUID | Breaks in new environments | Direct | Config per environment | Not SaaS-ready |
| Config file | Requires config per environment | Direct | Manual mapping | Fragile |
| Lookup by name | Portable | Slow (no index) | Name changes break code | Risky |
| **Alternate keys** | **Portable** | **Indexed** | **Self-documenting** | **RECOMMENDED** |
| Configuration entity | Portable | Extra query | Additional complexity | Overkill for MVP |

---

## Entities Currently Without Alternate Keys

These entities do NOT have alternate keys defined (consider adding where deployment portability is needed):

- `sprk_invoice` (future: `sprk_invoicecode` format `INV-{YYYYMMDD}-{SEQUENCE}`)
- `sprk_matter` (future: `sprk_mattercode` = client-provided matter number)
- `sprk_budget` / `sprk_budgetbucket`
- `sprk_spendsignal`
- `sprk_kpiassessment`
- `sprk_communication`
- `sprk_workassignment`
- `sprk_document` (uses `sprk_driveitemid` as natural uniqueness, but not a formal alternate key)

---

## Testing

### Unit Tests
- Verify `RetrieveByAlternateKeyAsync` calls correct Dataverse API with proper `KeyAttributeCollection`
- Verify cache hit/miss behavior for cached lookup services
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

## Related Documentation

| Document | Path |
|---|---|
| Field Mapping Reference | `docs/data-model/field-mapping-reference.md` |
| Schema Corrections | `docs/data-model/schema-corrections.md` |
| Entity Relationship Model | `docs/data-model/entity-relationship-model.md` |
| JSON Field Schemas | `docs/data-model/json-field-schemas.md` |

---

*This schema pattern is foundational for multi-tenant SaaS deployments.*
