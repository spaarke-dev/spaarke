# Phase 4 Deployment Readiness - Event Form Controls

> **Date**: 2026-02-01
> **Task**: 036
> **Phase**: Phase 4 - Event Form Controls

---

## Summary

Phase 4 PCF controls are **READY FOR DEPLOYMENT**. All three Phase 4 controls build successfully and meet deployment criteria.

---

## Deployment Readiness Checklist

### EventFormController PCF

- [x] Builds without errors
- [x] Version: 1.0.0
- [x] Bundle size: **1.44 MB** (within 5MB limit)
- [x] Dark mode support via Fluent UI v9 tokens
- [x] Handlers exported correctly (FieldVisibilityHandler, SaveValidationHandler)
- [x] Uses React 16 APIs (ReactDOM.render)
- [x] Platform-provided libraries declared (React 16.14.0, Fluent 9.46.2)

**Location**: `src/client/pcf/EventFormController`

**Purpose**: Controls Event form field visibility based on Event Type configuration. Reads required/hidden field configuration from sprk_eventtype and shows/hides form fields accordingly.

### RegardingLink PCF

- [x] Builds without errors
- [x] Version: 1.0.0
- [x] Bundle size: **760 KB** (within 5MB limit)
- [x] Dark mode support via Fluent UI v9 tokens
- [x] Uses React 16 APIs (ReactDOM.render)
- [x] Platform-provided libraries declared (React 16.14.0, Fluent 9.46.2)

**Location**: `src/client/pcf/RegardingLink`

**Purpose**: Displays a clickable navigation link to the regarding (parent) record. Reads regardingRecordType and regardingRecordId to render a link to the parent entity.

### UpdateRelatedButton PCF

- [x] Builds without errors
- [x] Version: 1.0.0
- [x] Bundle size: **1.06 MB** (within 5MB limit)
- [x] Dark mode support via Fluent UI v9 tokens
- [x] Uses React 16 APIs (ReactDOM.render)
- [x] Platform-provided libraries declared (React 16.14.0, Fluent 9.46.2)

**Location**: `src/client/pcf/UpdateRelatedButton`

**Purpose**: Triggers update of related records based on configured field mappings. Calls the Field Mapping API push endpoint to propagate changes to related entities.

---

## Bundle Size Summary

| Control | Bundle Size | Within Limit |
|---------|-------------|--------------|
| EventFormController | 1.44 MB | Yes (< 5MB) |
| RegardingLink | 760 KB | Yes (< 5MB) |
| UpdateRelatedButton | 1.06 MB | Yes (< 5MB) |

**Total Phase 4 bundle size**: ~3.26 MB

---

## Version Numbers

All Phase 4 controls are at version **1.0.0**:

| Control | Manifest Version |
|---------|-----------------|
| EventFormController | 1.0.0 |
| RegardingLink | 1.0.0 |
| UpdateRelatedButton | 1.0.0 |

---

## Technical Details

### Platform Libraries (ADR-022 Compliant)

All controls declare platform-provided libraries in ControlManifest.Input.xml:

```xml
<platform-library name="React" version="16.14.0" />
<platform-library name="Fluent" version="9.46.2" />
```

### Dark Mode Support (ADR-021 Compliant)

All controls use Fluent UI v9 theme resolution:

```typescript
function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
    if (context?.fluentDesignLanguage?.isDarkTheme) return webDarkTheme;
    // Fallback to localStorage, media query, or light theme
}
```

### React 16 APIs (ADR-022 Compliant)

All controls use React 16 pattern:

```typescript
import * as ReactDOM from "react-dom"; // NOT react-dom/client

// In init/updateView:
ReactDOM.render(element, container);

// In destroy:
ReactDOM.unmountComponentAtNode(container);
```

---

## Known Issues

### AssociationResolver (Phase 3 Control)

The AssociationResolver control from Phase 3 has build errors related to missing `@spaarke/ui-components` module. This does NOT block Phase 4 deployment as:

1. AssociationResolver was deployed in Phase 3 (Task 025)
2. It's a Phase 3 control, not a Phase 4 control
3. The existing deployed version remains functional

**Root Cause**: The FieldMappingHandler.ts imports from `@spaarke/ui-components` which is not configured in the control's package.json. This may need attention in a future task.

---

## Deployment Instructions

### Pre-Deployment

1. Verify dev environment is configured: `https://spaarkedev1.crm.dynamics.com`
2. Ensure PAC authentication is valid: `pac auth list`
3. Review Event form configuration (see `notes/event-form-configuration.md`)

### Deployment Steps

For each control, use the full solution workflow (NOT `pac pcf push`):

1. **Build**:
   ```bash
   cd src/client/pcf/[ControlName]
   npm run build
   ```

2. **Update versions** in all 4 locations:
   - Source manifest: `ControlManifest.Input.xml`
   - UI footer: Component `.tsx`
   - Solution manifest: `solution.xml`
   - Solution control manifest: `Controls/.../ControlManifest.xml`

3. **Pack and import solution**:
   ```bash
   pac solution pack --zipfile SpaarkeEventsR1_v1.0.0.zip --folder SpaarkeEventsR1_extracted
   pac solution import --path SpaarkeEventsR1_v1.0.0.zip --force-overwrite --publish-changes
   ```

4. **Verify deployment**:
   - Check form loads without errors
   - Verify controls render correctly
   - Test dark mode toggle

---

## Post-Deployment Verification

1. **EventFormController**:
   - Open Event form
   - Select an Event Type
   - Verify fields show/hide based on configuration
   - Test save validation for required fields

2. **RegardingLink**:
   - Open Event form with regarding record set
   - Verify link displays correct parent entity name
   - Test link navigation opens parent record

3. **UpdateRelatedButton**:
   - Configure a field mapping profile with push mode
   - Trigger update via button
   - Verify related records are updated

---

## Approval

| Role | Name | Approved | Date |
|------|------|----------|------|
| Developer | Claude Code | Yes | 2026-02-01 |
| Reviewer | - | Pending | - |

---

## Related Documentation

- [Phase 2 Deployment Readiness](phase2-deployment-readiness.md)
- [Phase 3 Deployment Readiness](phase3-deployment-readiness.md)
- [Phase 5 Deployment Readiness](phase5-deployment-readiness.md)
- [Event Form Configuration](event-form-configuration.md)
