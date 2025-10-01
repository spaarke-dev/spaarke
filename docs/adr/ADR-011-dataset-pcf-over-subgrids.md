# ADR-011: Dataset PCF Controls Over Native Subgrids

**Status:** Accepted
**Date:** 2025-09-30
**Authors:** Spaarke Engineering
**Sprint:** Sprint 2 → Sprint 3 Transition

---

## Context

During Sprint 2, we implemented JavaScript web resources for file management operations on Document forms. While functional, this approach revealed limitations for scenarios requiring list-based interactions (viewing related documents, bulk operations, filtering, etc.).

Native Power Platform subgrids provide basic list functionality but have significant limitations:
- **Limited customization** - Restricted UI/UX capabilities
- **No custom actions** - Cannot add custom buttons or context menus easily
- **Poor reusability** - Each subgrid must be configured separately
- **Limited interactivity** - Difficult to implement drag-drop, inline editing, etc.
- **Performance constraints** - Limited control over data loading and caching
- **Maintenance burden** - Changes require form republishing across environments

**Sprint 3 Requirements:**
- Display lists of related documents across multiple contexts (main forms, dashboards, custom pages)
- Support custom file operations (upload, download, replace, delete) from list view
- Enable bulk operations and advanced filtering
- Provide consistent UX across all touchpoints
- Maximize reusability to minimize development and maintenance effort

---

## Decision

**We will build custom Dataset PCF (PowerApps Component Framework) controls instead of using native Power Platform subgrids for list-based document management scenarios.**

### Scope

**Use Dataset PCF Controls for:**
1. **Related documents lists** on entity main forms
2. **Document search/browse** interfaces in custom pages
3. **Bulk document operations** requiring selection and batch actions
4. **Advanced filtering** and sorting scenarios
5. **Custom visualizations** (card view, tile view, etc.)

**Continue using native subgrids for:**
- Simple, read-only reference lists with no custom actions
- Admin/configuration scenarios where custom UI is not needed
- Scenarios where standard column filtering is sufficient

### Key Principles

1. **Reusability First**
   - Single Dataset PCF control configurable for multiple scenarios
   - Props-based configuration (e.g., `entityType`, `filterPreset`, `actions`)
   - Shared component library for common UI patterns

2. **Performance Optimization**
   - Virtual scrolling for large datasets
   - Client-side caching with invalidation strategies
   - Lazy loading of file metadata and thumbnails
   - Optimistic UI updates

3. **Consistent UX**
   - Fluent UI React components for Microsoft design consistency
   - Responsive design (desktop, tablet, mobile)
   - Accessibility (WCAG 2.1 AA compliance)
   - Dark mode support

4. **Developer Experience**
   - TypeScript for type safety
   - Unit testing with Jest/React Testing Library
   - Storybook for component documentation
   - Hot reload for rapid development

---

## Consequences

### Positive

✅ **Reusability**
- Single PCF control used across forms, dashboards, and custom pages
- Reduced development time for new list-based features
- Consistent behavior and UX across all contexts

✅ **Advanced Interactions**
- Drag-and-drop file upload directly in list view
- Inline editing of document metadata
- Bulk selection and batch operations
- Custom context menus and command bars

✅ **Performance**
- Control over data fetching and rendering strategies
- Virtual scrolling prevents DOM bloat
- Client-side caching reduces server calls
- Optimistic updates improve perceived performance

✅ **Modern Development Practices**
- TypeScript type safety catches errors at compile time
- React hooks for cleaner state management
- Unit tests ensure reliability
- Component library enables rapid feature development

✅ **Flexibility**
- Easy to add new features without form redesign
- Support for multiple view modes (grid, cards, tiles)
- Custom filtering and sorting logic
- Integration with external services

### Negative

❌ **Initial Development Effort**
- Higher upfront cost compared to configuring native subgrids
- Requires PCF setup, build pipeline, and testing infrastructure
- Learning curve for developers not familiar with PCF

❌ **Deployment Complexity**
- PCF controls require packaging and solution deployment
- Version management across environments
- Potential compatibility issues with Power Platform updates

❌ **Maintenance Overhead**
- Must keep PCF framework dependencies up to date
- Breaking changes in Power Platform PCF API require updates
- Need to monitor performance across different browsers

### Mitigation Strategies

**For Initial Development Effort:**
- Create reusable PCF template with build pipeline pre-configured
- Document best practices and patterns
- Provide training for development team

**For Deployment Complexity:**
- Automate PCF packaging in CI/CD pipeline
- Use solution layers for environment-specific configurations
- Implement thorough testing before production deployment

**For Maintenance Overhead:**
- Subscribe to Power Platform release notes
- Establish regular update cadence (quarterly)
- Maintain comprehensive test coverage

---

## Alternatives Considered

### 1. **Native Subgrids with Custom Ribbon Buttons**
**Approach:** Use standard subgrids, add custom ribbon buttons for file operations

**Rejected Because:**
- Ribbon customization is complex and fragile
- Limited to command bar actions (no inline interactions)
- Poor UX for bulk operations
- Still requires separate configuration per form

### 2. **Canvas App Embedded Component**
**Approach:** Embed Canvas App in model-driven forms for list display

**Rejected Because:**
- Performance overhead of embedded canvas apps
- Difficult to maintain consistency with model-driven app UX
- Limited data binding capabilities
- Poor integration with form context

### 3. **Custom Page with Dataverse API**
**Approach:** Build entirely custom pages outside Power Platform

**Rejected Because:**
- Loses Power Platform security and role-based access
- Requires duplicate authentication/authorization logic
- No integration with model-driven app navigation
- Higher maintenance burden

### 4. **Continue with JavaScript Web Resources**
**Approach:** Extend Sprint 2 JavaScript approach to handle lists

**Rejected Because:**
- JavaScript web resources are legacy technology (see ADR-006)
- Poor testability and maintainability
- Limited UI capabilities (no virtual scrolling, etc.)
- Difficult to implement advanced interactions

---

## Implementation

### Phase 1: Core Dataset PCF Control (Sprint 3)

**Deliverables:**
- Base Dataset PCF control scaffolding
- Virtual scrolling with react-window
- Basic CRUD operations (view, upload, download, delete)
- Configuration props (entityType, viewConfig, actions)
- Unit tests and Storybook documentation

**Effort:** 24-32 hours (Sprint 3 Task 3.2)

### Phase 2: Advanced Features (Sprint 4+)

**Potential Enhancements:**
- Bulk selection and batch operations
- Drag-and-drop file upload
- Inline metadata editing
- Multiple view modes (grid, cards, tiles)
- Advanced filtering and search
- File preview panel
- Version history integration

### Technical Stack

```typescript
// Core Dependencies
- PCF Framework: @microsoft/pcf-tools
- React: ^18.x
- TypeScript: ^5.x
- Fluent UI React: ^9.x
- React Window: ^1.x (virtual scrolling)

// Testing & Dev Tools
- Jest: ^29.x
- React Testing Library: ^14.x
- Storybook: ^7.x
- ESLint + Prettier
```

### PCF Control Configuration

```tsx
// Example: Document List Dataset PCF
interface DocumentListConfig {
  // Entity configuration
  entityType: "sprk_document" | "sprk_container";
  relationshipName?: string; // For related records

  // View configuration
  viewMode: "grid" | "cards" | "tiles";
  pageSize: number; // Default: 50
  enableVirtualScroll: boolean; // Default: true

  // Actions configuration
  enableUpload: boolean;
  enableDownload: boolean;
  enableDelete: boolean;
  enableBulkActions: boolean;
  customActions?: Action[];

  // Filtering
  defaultFilter?: string; // FetchXML filter
  enableSearch: boolean;
  searchFields: string[];

  // Styling
  theme?: "light" | "dark" | "auto";
  customStyles?: CSSProperties;
}
```

### Reusability Patterns

**Pattern 1: Different Entities, Same Control**
```xml
<!-- Document list on Account form -->
<control id="relatedDocuments"
         classid="{dataset-pcf-guid}"
         entityType="sprk_document"
         relationshipName="account_documents"
         viewMode="grid" />

<!-- Container list on custom page -->
<control id="allContainers"
         classid="{dataset-pcf-guid}"
         entityType="sprk_container"
         viewMode="cards" />
```

**Pattern 2: Contextual Actions**
```typescript
// Account-specific document actions
const accountDocActions = [
  { id: "email", label: "Email to Contact", handler: emailDocument },
  { id: "attach", label: "Attach to Case", handler: attachToCase }
];

// Project-specific document actions
const projectDocActions = [
  { id: "share", label: "Share with Team", handler: shareDocument },
  { id: "archive", label: "Archive to SharePoint", handler: archiveDocument }
];
```

---

## Operationalization

### Development Standards

1. **All list-based UI must use Dataset PCF controls** (approved exceptions only)
2. **PCF controls must be unit tested** (minimum 80% coverage)
3. **Storybook stories required** for all PCF components
4. **Accessibility testing mandatory** before production deployment

### CI/CD Integration

```yaml
# PCF Build Pipeline
- task: PCFBuild
  inputs:
    solution: power-platform/pcf/DocumentList
    outputPath: solutions/

- task: PCFTest
  inputs:
    testCommand: npm test -- --coverage

- task: SolutionPackager
  inputs:
    includePCF: true
```

### Success Metrics

**Development Efficiency:**
- Reuse rate: > 80% (same PCF control across 3+ contexts)
- Time to add new list feature: < 8 hours (vs. 24+ hours with subgrids)

**Performance:**
- Initial load: < 2s for 50 records
- Scroll to 500th record: < 100ms
- File upload action: < 500ms (excluding network time)

**User Experience:**
- Task completion time: 30% faster than subgrids
- User satisfaction: > 4.5/5 (vs. 3.2/5 for native subgrids)

**Maintainability:**
- Build time: < 2 min
- Unit test execution: < 30s
- Zero runtime errors in production (first 3 months)

---

## Exceptions

**Native subgrids may be used for:**
1. **Admin configuration lists** where no custom actions are needed
2. **Simple lookup/reference scenarios** with < 20 records
3. **Read-only audit logs** where performance is not critical
4. **Temporary/prototype features** that will be replaced with PCF in next sprint

All exceptions require **explicit approval** from tech lead with documented rationale.

---

## Related ADRs

- [ADR-006: Prefer PCF Controls Over Web Resources](./ADR-006-prefer-pcf-over-webresources.md)
- [ADR-002: No Heavy Plugins](./ADR-002-no-heavy-plugins.md) (thin plugins + PCF for UI)
- [ADR-004: Async Job Contract](./ADR-004-async-job-contract.md) (background processing for bulk ops)

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

---

**Next Actions:**
1. ✅ Document ADR-011 (this document)
2. 🔄 Create Sprint 3 Task 3.2: Dataset PCF Control Implementation
3. 🔄 Set up PCF project template and build pipeline
4. 🔄 Design component API and props interface
5. 🔄 Create Storybook stories for design review
