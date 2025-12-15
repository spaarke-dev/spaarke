# SDAP File Viewer Enhancements - Phase 2

> **Status**: Planned | **Priority**: Medium

## Summary

Address the unused `DocumentOperations.js` webresource and consolidate file operations into PCF controls.

## Key Finding

The `sprk_DocumentOperations` webresource (v1.1.0) contains Upload/Download/Replace/Delete functions that are **never called**. No ribbon buttons were configured to invoke these functions. PCF controls have their own TypeScript implementations.

## Recommendation

**Delete the unused webresource** - PCF controls already provide this functionality in a modern, ADR-006 compliant manner.

## Files

- [spec.md](spec.md) - Full analysis and recommendations

## Related

- PCF Services: `src/client/pcf/UniversalDatasetGrid/control/services/`
- Unused Code: `src/dataverse/webresources/spaarke_documents/DocumentOperations.js`
