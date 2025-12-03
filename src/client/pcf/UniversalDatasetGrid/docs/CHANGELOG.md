# Changelog

All notable changes to the Universal Dataset Grid PCF control will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.7] - 2025-10-05

### Added - Sprint 5B: Fluent UI v9 & Code Quality
- **Phase A: Architecture Refactor**
  - Single React Root architecture using React 18 `createRoot()` API
  - Simplified component hierarchy (no multiple roots)
  - Build wrapper (`build-wrapper.js`) for production deployments

- **Phase B: Theming & Design Tokens**
  - Dynamic theme resolution from Power Apps context
  - Dark mode detection via luminance calculation
  - 100% design token compliance (no hardcoded colors)

- **Phase D: Code Quality & Standards**
  - Centralized logging utility (`utils/logger.ts`) with configurable log levels
  - React ErrorBoundary component for graceful error handling
  - Enhanced ESLint configuration with React and React Hooks rules
  - Comprehensive documentation (README, API docs, virtualization investigation)

### Changed
- Migrated from multiple `ReactDOM.render()` calls to single `createRoot()`
- Replaced `console.log` with structured logger throughout codebase
- Updated ThemeProvider to use token-based color analysis (removed accessibility API dependency)
- Enhanced CommandBar with layout shift prevention (always-rendered selection counter)
- Updated all components to use Fluent UI design tokens exclusively

### Fixed
- React 18 deprecation warnings (`ReactDOM.render` is no longer supported)
- Container `appendChild` errors from multiple React roots
- Intermittent control loading issues
- Grid layout shift when selecting/deselecting rows
- Theme detection errors when Power Apps context unavailable
- Bundle size optimization (470 KB production build)

### Removed
- Virtualized DataGrid implementation (deferred to future sprint due to alignment issues)
- Unused `teamsHighContrastTheme` import
- Legacy console.log statements

### Technical Details
- **Bundle Size**: 470 KB (production), 7.4 MB (development)
- **React Version**: 18.2.0
- **Fluent UI Version**: 9.54.0
- **Build Mode**: Production enforced via build-wrapper during `pac pcf push`

### Known Issues
- Virtualization deferred: Standard DataGrid used, suitable for <1000 records
- File operations (Add/Remove/Update/Download) are placeholder implementations
- No server-side paging implemented (dataset loads all records)

---

## [2.0.6] - 2025-10-05

### Attempted - Phase C: Virtualization (Not Released)
- Attempted implementation of virtualized DataGrid using `@fluentui-contrib/react-data-grid-react-window-grid`
- Encountered critical column alignment issues with responsive containers
- Multiple implementation attempts with various approaches
- Reverted to standard DataGrid for stability

**See**: [VIRTUALIZATION_INVESTIGATION.md](./VIRTUALIZATION_INVESTIGATION.md) for detailed analysis

---

## [2.0.5] - 2025-10-04

### Added - Sprint 5B: Phase A Tasks
- Task A.1: Single React Root Architecture
  - Converted to React 18 `createRoot()` pattern
  - Removed legacy `ReactDOM.render()` calls
  - Simplified `index.ts` from 21 KiB to 10 KiB

- Task A.2: Fluent UI DataGrid Integration
  - Implemented `@fluentui/react-table` DataGrid component
  - Multi-select functionality with Power Apps synchronization
  - Column sorting support

- Task A.3: Fluent UI Toolbar
  - Migrated CommandBar to Fluent UI v9 Toolbar components
  - Added file operation buttons (Add, Remove, Update, Download)
  - Added Refresh button with ArrowClockwise icon

### Changed
- Updated manifest to version 2.0.5
- Simplified component props (removed unnecessary state)

### Fixed
- Development bundle size: 7.35 MB → 3.7 MB (after initial optimizations)
- Production bundle size: Optimized to 456 KB

---

## [2.0.4] - 2025-09-30

### Added - Sprint 5A: Initial Fluent UI v9 Migration
- Partial React 18 migration
- Initial Fluent UI v9 component integration
- Updated package dependencies

### Issues
- React 18 deprecation warnings present
- Container appendChild errors occurring
- Control loading intermittently

---

## [2.0.3] - 2025-09-15

### Added - Sprint 4: SDAP Integration Preparation
- SDAP service interfaces (ISpeService, IOboSpeService) - Later removed in Sprint 4 Task 4.4
- Document attachment handling infrastructure
- Entity metadata for Matters, Documents, Attachments

### Changed
- Enhanced dataset handling for document operations
- Updated TypeScript configurations

---

## [2.0.0] - 2025-08-01

### Added - Major Architecture Update
- TypeScript implementation
- PCF framework integration
- Dataset binding support
- Multi-select functionality

### Changed
- Migrated from JavaScript to TypeScript
- Updated to modern PCF patterns

---

## [1.5.0] - 2025-06-15

### Added
- Initial grid implementation with basic dataset display
- Column sorting
- Row selection

### Changed
- Legacy React implementation
- Class-based components

---

## [1.0.0] - 2025-05-01

### Added
- Initial release
- Basic grid functionality
- Power Apps integration

---

## Future Roadmap

### Planned for Next Sprint
- **Virtualization** (Phase C continuation):
  - Evaluate `@tanstack/react-virtual` as alternative to Fluent UI contrib
  - Implement hybrid approach: standard grid <500 records, virtualized for larger datasets
  - Performance testing with 5000+ record datasets

- **File Operations** (Complete implementations):
  - Add File: SharePoint document upload
  - Remove File: Document deletion with confirmation
  - Update File: Version control and file replacement
  - Download: Multi-file zip support

- **Advanced Features**:
  - Column resize and reorder
  - Advanced filtering UI
  - Bulk operations
  - Export to Excel

- **Performance**:
  - Code splitting for reduced initial bundle
  - Lazy loading for large datasets
  - Server-side paging option

### Under Consideration
- Column customization UI
- Saved view presets
- Inline editing
- Drag-and-drop file upload
- Real-time collaboration features

---

## Migration Guide

### Upgrading from 2.0.3 to 2.0.7

1. **No Breaking Changes** - Drop-in replacement
2. **Improved Performance** - Automatic with React 18 architecture
3. **Enhanced Logging** - Check console for new structured log format
4. **Theme Detection** - Automatic dark mode support

### Upgrading from 1.x to 2.x

1. **Major Breaking Changes** - Complete rewrite
2. **TypeScript Required** - All components now TypeScript
3. **PCF Framework** - Must use Power Apps PCF
4. **Dataset Binding** - Different property binding approach

---

## Support & Contributions

### Reporting Issues
- Check [Known Issues](#known-issues) first
- Review browser console logs (use `[UniversalDatasetGrid]` filter)
- Include PCF context version and Power Apps environment

### Development Setup
```bash
# Clone repository
git clone <repo-url>

# Install dependencies
npm install

# Build
npm run build:prod

# Deploy
pac pcf push --publisher-prefix <prefix>
```

### Code Standards
- TypeScript strict mode
- ESLint zero warnings
- Fluent UI design tokens only
- React functional components
- Comprehensive error handling
- Structured logging

---

**Maintained by**: Spaarke Development Team
**License**: Proprietary
**Documentation**: See [README.md](./README.md)
