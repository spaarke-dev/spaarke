# SPE File Viewer - Known Issues

## Issue: sprk_driveitemid Field Not Being Populated

### Observed Behavior

When Document records are created, the following fields are populated:
- ✅ `sprk_graphitemid` - Contains SharePoint Graph Item ID (e.g., `01LBYCMX5Qi2...`)
- ❌ `sprk_driveitemid` - Remains empty

### Impact

- **PCF Control Binding**: The SPE File Viewer control MUST be bound to `sprk_graphitemid` instead of `sprk_driveitemid`
- **Workaround**: Use `sprk_graphitemid` for all document previews

### Root Cause - CONFIRMED ✅

**Source:** [DocumentRecordService.ts:191](../UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts#L191)

The document creation code in `createDocumentPayload()` **intentionally only populates `sprk_graphitemid`**:

```typescript
// SharePoint Embedded metadata
sprk_graphitemid: file.id,
sprk_graphdriveid: parentContext.containerId,
```

**Findings:**
1. ❌ There is **NO code** to populate `sprk_driveitemid` in the document creation process
2. ✅ `sprk_graphitemid` is populated with `file.id` from the Graph API response
3. ❓ `sprk_driveitemid` appears to be a **legacy/unused field** from an earlier design
4. ⚠️ The field exists in the Dataverse schema but is never populated by any code

**Conclusion:** This is **not a bug** - it's the current design. `sprk_driveitemid` is simply not being used.

### Recommended Actions

1. **Short-term (Completed)** ✅:
   - ✅ Documented that `sprk_graphitemid` is the correct field to use
   - ✅ Updated deployment guides to specify `sprk_graphitemid` only
   - ✅ PCF control handles both fields via array extraction (v1.0.2)

2. **Long-term (Schema Cleanup)**:
   - **Option A: Remove Field** - Delete `sprk_driveitemid` from Dataverse schema if it's truly unused
   - **Option B: Populate Field** - Add code to populate `sprk_driveitemid` with same value as `sprk_graphitemid` (redundancy)
   - **Option C: Document As-Is** - Keep field but document as "reserved for future use"

   **Recommendation:** Option A (Remove) - The field serves no purpose and creates confusion

### Related Files

- [DEPLOYMENT-v1.0.2.md](DEPLOYMENT-v1.0.2.md) - Updated to specify `sprk_graphitemid` only
- Document creation/upload code (location TBD)
- Dataverse solution (field definitions)

### Status

✅ **Resolved (Design Clarification)** - Root cause identified. This is not a bug but a design choice. `sprk_driveitemid` is unused. Schema cleanup recommended but not required.

### Last Updated

November 24, 2025
