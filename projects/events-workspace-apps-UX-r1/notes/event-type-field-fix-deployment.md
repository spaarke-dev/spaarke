# Event Type Field Name Fix - Deployment Notes

**Date**: 2026-02-08
**Issue**: Incorrect Event Type field name across codebase
**Correct Field**: `sprk_eventtype_ref` (OData: `_sprk_eventtype_ref_value`)

---

## Summary

The Event entity's Event Type lookup field was incorrectly referenced in multiple locations:
- `_sprk_eventtype_value` (wrong field name entirely)

**Correct OData field name**: `_sprk_eventtype_ref_value`

---

## Files Changed

### EventDetailSidePane (React Custom Page)
- `src/solutions/EventDetailSidePane/src/types/EventRecord.ts` - Interface and select field arrays
- `src/solutions/EventDetailSidePane/src/App.tsx` - Event Type ID extraction
- `src/solutions/EventDetailSidePane/src/components/HeaderSection.tsx` - Event Type name display
- `src/solutions/EventDetailSidePane/src/hooks/useEventTypeConfig.ts` - Documentation example

### EventsPage (React Custom Page)
- `src/solutions/EventsPage/src/components/GridSection.tsx` - Interface, filters, mappings, mock data
- `src/solutions/EventsPage/src/services/FetchXmlService.ts` - Formatted value field name

### DueDatesWidget (PCF Control)
- `src/client/pcf/DueDatesWidget/control/services/eventFilterService.ts`
- `src/client/pcf/DueDatesWidget/control/__tests__/setupTests.ts`

### Backend (.NET API)
- `src/server/shared/Spaarke.Dataverse/Models.cs` - Comment documentation
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` - OData queries and entity mapping

### Documentation
- `projects/events-workspace-apps-UX-r1/notes/design/universal-datagrid-enhancement.md` - Design doc example

---

## Deployment Checklist

### 1. EventsPage Web Resource
- [ ] Build: `cd src/solutions/EventsPage && npm run build`
- [ ] Output: `src/solutions/EventsPage/dist/`
- [ ] Deploy: Update `sprk_/eventspage/bundle.js` web resource in Dataverse
- [ ] Publish customizations

### 2. EventDetailSidePane Web Resource
- [ ] Build: `cd src/solutions/EventDetailSidePane && npm run build`
- [ ] Output: `src/solutions/EventDetailSidePane/dist/`
- [ ] Deploy: Update `sprk_/eventdetailsidepane/bundle.js` web resource in Dataverse
- [ ] Publish customizations

### 3. DueDatesWidget PCF Control
- [ ] Build and pack: v1.0.7 with correct field name
- [ ] Solution: `src/client/pcf/DueDatesWidget/Solution/bin/SpaarkeDueDatesWidget_v1.0.7.zip`
- [ ] Import: `pac solution import --path <path-to-zip> --publish-changes`

### 4. Backend API (.NET)
- [ ] Build: `dotnet build src/server/api/Sprk.Bff.Api/`
- [ ] Output: `src/server/api/Sprk.Bff.Api/bin/Release/net8.0/publish/`
- [ ] Deploy: Upload to Azure App Service via Kudu (spe-api-dev-67e2xz)

---

## Verification Steps

After deployment, verify:
1. EventsPage grid shows Event Type column correctly
2. EventDetailSidePane header shows Event Type badge
3. DueDatesWidget shows Event Type on cards
4. Backend API returns Event Type in event queries

---

## Rollback

If issues occur, revert the field name changes back to the original patterns in each component.
