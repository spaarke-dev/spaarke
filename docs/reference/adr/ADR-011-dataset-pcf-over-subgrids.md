# ADR-011: Dataset PCF Controls Over Native Subgrids

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-30 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |
| Sprint | Sprint 2 → Sprint 3 Transition |

---

## Context

During Sprint 2, we implemented JavaScript web resources for file management operations on Document forms. While functional, this approach revealed limitations for scenarios requiring list-based interactions (viewing related documents, bulk operations, filtering, etc.).

### Native Subgrid Limitations

| Limitation | Impact |
|------------|--------|
| Limited customization | Restricted UI/UX capabilities |
| No custom actions | Cannot add custom buttons or context menus easily |
| Poor reusability | Each subgrid must be configured separately |
| Limited interactivity | Difficult to implement drag-drop, inline editing |
| Performance constraints | Limited control over data loading and caching |
| Maintenance burden | Changes require form republishing across environments |

### Sprint 3 Requirements

- Display lists of related documents across multiple contexts (main forms, dashboards, custom pages)
- Support custom file operations (upload, download, replace, delete) from list view
- Enable bulk operations and advanced filtering
- Provide consistent UX across all touchpoints
- Maximize reusability to minimize development and maintenance effort

---

## Decision

**Build custom Dataset PCF (PowerApps Component Framework) controls instead of native Power Platform subgrids for list-based document management scenarios.**

### Scope

| Scenario | Technology |
|----------|------------|
| Related documents lists on entity main forms | ✅ Dataset PCF |
| Document search/browse interfaces in custom pages | ✅ Dataset PCF |
| Bulk document operations requiring selection | ✅ Dataset PCF |
| Advanced filtering and sorting | ✅ Dataset PCF |
| Custom visualizations (card view, tile view) | ✅ Dataset PCF |
| Simple, read-only reference lists (no custom actions) | Native subgrid OK |
| Admin/configuration scenarios | Native subgrid OK |

### Key Principles

| Principle | Implementation |
|-----------|----------------|
| **Reusability First** | Single Dataset PCF configurable for multiple scenarios via props |
| **Performance** | Virtual scrolling, client-side caching, lazy loading, optimistic UI |
| **Consistent UX** | Fluent UI React, responsive, accessible (WCAG 2.1 AA), dark mode |
| **Developer Experience** | TypeScript, Jest/RTL, Storybook, hot reload |

---

## Consequences

### Positive

| Benefit | Details |
|---------|---------|
| ✅ Reusability | Single PCF used across forms, dashboards, custom pages |
| ✅ Advanced Interactions | Drag-drop upload, inline editing, bulk selection, context menus |
| ✅ Performance | Virtual scrolling, caching, optimistic updates |
| ✅ Modern Development | TypeScript, React hooks, unit tests, component library |
| ✅ Flexibility | Multiple view modes, custom filtering, external integrations |

### Negative

| Challenge | Mitigation |
|-----------|------------|
| ❌ Initial development effort | Reusable PCF template, documentation, training |
| ❌ Deployment complexity | Automate in CI/CD, solution layers, thorough testing |
| ❌ Maintenance overhead | Subscribe to release notes, quarterly updates, test coverage |

---

## Alternatives Considered

| Alternative | Rejection Reason |
|-------------|------------------|
| Native subgrids + custom ribbon buttons | Complex/fragile ribbon customization, poor bulk UX |
| Canvas App embedded component | Performance overhead, inconsistent UX, poor data binding |
| Custom page with Dataverse API | Loses Power Platform security, duplicate auth logic |
| JavaScript web resources | Legacy (ADR-006), poor testability, limited UI |

---

## Implementation

### Phase 1: Core Dataset PCF Control (Sprint 3)

| Deliverable | Details |
|-------------|---------|
| Base scaffolding | Dataset PCF project structure |
| Virtual scrolling | react-window integration |
| Basic CRUD | View, upload, download, delete |
| Configuration | Props (entityType, viewConfig, actions) |
| Quality | Unit tests, Storybook |

**Effort:** 24-32 hours (Sprint 3 Task 3.2)

### Phase 2: Advanced Features (Sprint 4+)

- Bulk selection and batch operations
- Drag-and-drop file upload
- Inline metadata editing
- Multiple view modes (grid, cards, tiles)
- Advanced filtering and search
- File preview panel
- Version history integration

### Technical Stack

| Category | Technology |
|----------|------------|
| PCF Framework | @microsoft/pcf-tools |
| UI Framework | React ^18.x, Fluent UI React ^9.x |
| Language | TypeScript ^5.x |
| Virtual Scrolling | react-window ^1.x |
| Testing | Jest ^29.x, React Testing Library ^14.x |
| Documentation | Storybook ^7.x |

### PCF Configuration Interface

```typescript
interface DocumentListConfig {
  entityType: "sprk_document" | "sprk_container";
  relationshipName?: string;
  viewMode: "grid" | "cards" | "tiles";
  pageSize: number;
  enableVirtualScroll: boolean;
  enableUpload: boolean;
  enableDownload: boolean;
  enableDelete: boolean;
  enableBulkActions: boolean;
  customActions?: Action[];
  defaultFilter?: string;
  enableSearch: boolean;
  searchFields: string[];
  theme?: "light" | "dark" | "auto";
}
```

---

## Operationalization

### Development Standards

| Standard | Requirement |
|----------|-------------|
| List-based UI | Must use Dataset PCF (approved exceptions only) |
| Unit tests | Minimum 80% coverage |
| Documentation | Storybook stories required |
| Accessibility | Testing mandatory before production |

### CI/CD Integration

```yaml
- task: PCFBuild
- task: PCFTest (--coverage)
- task: SolutionPackager (includePCF: true)
```

---

## Success Metrics

| Category | Metric | Target |
|----------|--------|--------|
| **Development** | Reuse rate | > 80% (same PCF across 3+ contexts) |
| **Development** | Time to add new list feature | < 8 hours (vs. 24+ with subgrids) |
| **Performance** | Initial load (50 records) | < 2s |
| **Performance** | Scroll to 500th record | < 100ms |
| **Performance** | File upload action | < 500ms (excluding network) |
| **UX** | Task completion time | 30% faster than subgrids |
| **UX** | User satisfaction | > 4.5/5 |
| **Quality** | Build time | < 2 min |
| **Quality** | Unit test execution | < 30s |
| **Quality** | Runtime errors (first 3 months) | Zero |

---

## Exceptions

Native subgrids may be used for:

| Exception | Criteria |
|-----------|----------|
| Admin configuration lists | No custom actions needed |
| Simple lookup/reference | < 20 records |
| Read-only audit logs | Performance not critical |
| Temporary/prototype | Will be replaced with PCF in next sprint |

All exceptions require **explicit approval** from tech lead with documented rationale.

---

## Related ADRs

- [ADR-006: Prefer PCF Controls Over Web Resources](./ADR-006-prefer-pcf-over-webresources.md)
- [ADR-002: No Heavy Plugins](./ADR-002-no-heavy-plugins.md) (thin plugins + PCF for UI)
- [ADR-004: Async Job Contract](./ADR-004-async-job-contract.md) (background processing for bulk ops)

---

## Compliance

**Code review checklist:**
- [ ] List-based UI uses Dataset PCF (or has documented exception)
- [ ] PCF has 80%+ test coverage
- [ ] Storybook stories exist for new components
- [ ] Accessibility testing completed
- [ ] No new native subgrids without tech lead approval

## AI-Directed Coding Guidance

- Default for list-based document UX: implement/extend the Dataset PCF under `src/client/pcf/UniversalDatasetGrid/`.
- Reuse shared components from `src/client/shared/Spaarke.UI.Components/` rather than duplicating UI primitives.
- Avoid adding new native subgrids or bespoke JS webresources; if you must, document an explicit exception and plan the PCF replacement.

---

## References

- [Power Apps Component Framework Documentation](https://docs.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Dataset PCF Samples](https://github.com/microsoft/PowerApps-Samples/tree/master/component-framework)
- [Fluent UI React Components](https://react.fluentui.dev/)
- [React Window (Virtual Scrolling)](https://github.com/bvaughn/react-window)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-09-30 | 1.0 | Initial ADR creation post-Sprint 2 | Spaarke Engineering |
| 2025-12-04 | 1.1 | Format update for AI readability | Spaarke Engineering |
