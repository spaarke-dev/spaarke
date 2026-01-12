# Final Configuration State - Phase 5d Cutover

> **Date**: 2026-01-11
> **Task**: 071 - Update Azure configuration for final schema
> **Status**: Complete

---

## Configuration Summary

### Embedding Model

| Setting | Final Value | Notes |
|---------|-------------|-------|
| Model | `text-embedding-3-large` | Higher quality embeddings |
| Dimensions | `3072` | Native dimension for text-embedding-3-large |
| Index Fields | `contentVector3072`, `documentVector3072` | 3072-dimension vector fields |

### App Service Configuration

**DocumentIntelligence Settings**:
```json
{
  "EmbeddingModel": "text-embedding-3-large",
  "EmbeddingDimensions": 3072
}
```

**EmbeddingMigration Settings**:
```json
{
  "_comment": "DEPRECATED: Migration to 3072-dim vectors complete (Phase 5d)",
  "Enabled": false
}
```

---

## Index Schema State

### Deployed Schema: `spaarke-knowledge-index-v2`

The deployed index contains both 1536-dim and 3072-dim vector fields for backward compatibility:

| Field | Dimensions | Status | Used By |
|-------|------------|--------|---------|
| `contentVector` | 1536 | **DEPRECATED** | Not used |
| `documentVector` | 1536 | **DEPRECATED** | Not used |
| `contentVector3072` | 3072 | **ACTIVE** | RagService |
| `documentVector3072` | 3072 | **ACTIVE** | VisualizationService |

**Note**: Azure AI Search does not support removing fields from a live index. The 1536-dim fields will remain in the schema but are not used by the application.

### Field Constants in Code

**RagService.cs**:
```csharp
private const string VectorFieldName = "contentVector3072";
private const int VectorDimensions = 3072;
```

**VisualizationService.cs**:
```csharp
private const string DocumentVectorFieldName = "documentVector3072";
private const int VectorDimensions = 3072;
```

---

## Deprecated Components

### Removed from Configuration

1. **LegacyEmbeddingDimensions** - Removed from `DocumentIntelligenceOptions.cs`
2. **EmbeddingMigration detailed settings** - Simplified to just `Enabled: false`
3. **AI_EMBEDDING_DIMENSIONS placeholder** - Hardcoded to 3072 in appsettings.template.json

### Still Registered (Disabled)

1. **EmbeddingMigrationService** - Registered in Program.cs but disabled via config
2. **DocumentVectorBackfillService** - Registered in Program.cs but disabled via config

These services remain registered for potential future use but will not execute unless explicitly enabled.

---

## Index Schema Files

| File | Purpose | Status |
|------|---------|--------|
| `spaarke-knowledge-index.json` | Original 1536-dim schema | **Historical reference** |
| `spaarke-knowledge-index-v2.json` | Dual vector schema (1536 + 3072) | **Currently deployed** |
| `spaarke-knowledge-index-migration.json` | Migration testing schema | **Historical reference** |

---

## Verification Checklist

- [x] `EmbeddingDimensions` set to 3072 in config
- [x] `LegacyEmbeddingDimensions` removed from code
- [x] `EmbeddingMigration.Enabled` = false
- [x] RagService uses `contentVector3072`
- [x] VisualizationService uses `documentVector3072`
- [x] All AI tests passing (83 tests: 26 Visualization + 57 RAG)
- [x] Build succeeds

---

## Rollback Instructions (If Needed)

If issues arise with 3072-dim vectors:

1. **Cannot roll back index schema** - 1536-dim fields still exist
2. **Code rollback**:
   - Revert RagService.VectorFieldName to "contentVector"
   - Revert VisualizationService.DocumentVectorFieldName to "documentVector"
   - Restore LegacyEmbeddingDimensions to DocumentIntelligenceOptions.cs
3. **Config rollback**:
   - Set EmbeddingDimensions to 1536
   - Re-add LegacyEmbeddingDimensions: 1536

**Note**: Rollback would require re-indexing with 1536-dim embeddings, which is not recommended.

---

## Next Steps

1. **Task 072**: E2E regression testing
2. **Task 090**: Project wrap-up and cleanup
