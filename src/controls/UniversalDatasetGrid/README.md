# Universal Dataset Grid - PCF Control

A modern, accessible dataset grid control for Power Apps built with React 18 and Fluent UI v9.

## Overview

The Universal Dataset Grid is a Power Apps Component Framework (PCF) control that provides a feature-rich, accessible grid for displaying and managing dataset records. Built with modern web technologies and Microsoft's design system.

## Features

- ✅ **React 18** - Single root architecture with modern React patterns
- ✅ **Fluent UI v9** - Microsoft's latest design system
- ✅ **Responsive Design** - Adapts to container size with horizontal scrolling
- ✅ **Theming** - Automatic light/dark mode detection from Power Apps
- ✅ **Accessibility** - ARIA-compliant, keyboard navigation
- ✅ **Multi-select** - Row selection with Power Apps synchronization
- ✅ **Sorting** - Column-based sorting
- ✅ **Error Handling** - React Error Boundary with graceful fallbacks
- ✅ **Structured Logging** - Centralized logger with configurable log levels
- ✅ **File Operations** - Add, Remove, Update, Download file commands
- ✅ **Production Ready** - 470 KB optimized bundle

## Technical Stack

### Core Technologies
- **React**: 18.2.0
- **Fluent UI**: v9.54.0
- **TypeScript**: Latest
- **PCF Framework**: Latest

### Build Tools
- **Webpack**: 5.x
- **Babel**: Modern JavaScript transpilation
- **ESLint**: Comprehensive linting with React and TypeScript rules

## Architecture

### Single React Root Pattern
The control uses React 18's `createRoot()` API for optimal performance:

```typescript
// One React root created in init()
this.root = ReactDOM.createRoot(container);

// Updates via props, not re-mounting
this.root.render(<App context={context} />);
```

### Component Hierarchy
```
UniversalDatasetGrid (PCF Control)
└── ErrorBoundary
    └── FluentProvider (Theme)
        └── UniversalDatasetGridRoot
            ├── CommandBar (Toolbar)
            └── DatasetGrid (Main Grid)
```

### Key Components

#### 1. **index.ts** - PCF Control Entry Point
- Implements PCF lifecycle (init, updateView, destroy)
- Manages single React root
- Error handling with try-catch blocks
- Structured logging

#### 2. **UniversalDatasetGridRoot.tsx** - Main React Component
- Manages dataset state
- Coordinates child components
- Debounced `notifyOutputChanged` (300ms)
- Selection state management

#### 3. **DatasetGrid.tsx** - Grid Component
- Standard Fluent UI DataGrid (non-virtualized)
- Multi-select with row selection
- Column sorting
- Responsive layout

#### 4. **CommandBar.tsx** - Toolbar Component
- File operation buttons (Add, Remove, Update, Download)
- Refresh button
- Selection counter
- Fluent UI Toolbar components

#### 5. **ErrorBoundary.tsx** - Error Handler
- Catches React errors
- Displays user-friendly error UI
- Logs errors for debugging

#### 6. **ThemeProvider.ts** - Theme Resolution
- Detects light/dark mode from Power Apps context
- Luminance-based color analysis
- Graceful fallbacks

#### 7. **logger.ts** - Centralized Logging
- Log levels: DEBUG, INFO, WARN, ERROR
- Component-tagged messages
- Configurable output

## Installation & Deployment

### Prerequisites
- Node.js 16+
- npm 8+
- Power Platform CLI (`pac`)

### Build

```bash
# Install dependencies
npm install

# Development build
npm run build:dev

# Production build (recommended)
npm run build:prod
```

### Deploy to Power Apps

```bash
# Push to environment (uses build-wrapper for production mode)
pac pcf push --publisher-prefix <your-prefix>
```

**Note**: The `build-wrapper.js` automatically forces production builds during `pac pcf push` to ensure optimized bundles.

## Configuration

### Manifest Settings
Located in `ControlManifest.Input.xml`:

```xml
<control namespace="Spaarke.UI.Components"
         constructor="UniversalDatasetGrid"
         version="2.0.7"
         display-name-key="Universal Dataset Grid"
         description-key="Document management grid with SDAP integration and Fluent UI v9"
         control-type="standard">
```

### Grid Configuration
Default configuration in `types/index.ts`:

```typescript
export const DEFAULT_GRID_CONFIG: GridConfiguration = {
    enablePaging: false,
    pageSize: 5000,
    enableSorting: true,
    enableFiltering: false
};
```

## Usage in Power Apps

1. **Add Control to Form/View**
   - Open form/view designer
   - Add field or section
   - Select "Universal Dataset Grid" from control list

2. **Configure Dataset**
   - Bind to dataset property
   - Configure columns in view
   - Set selection behavior

3. **Handle Events**
   - Selection changes sync automatically
   - Use Power Apps formulas to respond to selections

## Development

### Project Structure
```
UniversalDatasetGrid/
├── UniversalDatasetGrid/           # Source code
│   ├── components/                 # React components
│   │   ├── CommandBar.tsx
│   │   ├── DatasetGrid.tsx
│   │   ├── ErrorBoundary.tsx
│   │   └── UniversalDatasetGridRoot.tsx
│   ├── providers/                  # Utilities
│   │   └── ThemeProvider.ts
│   ├── utils/                      # Utilities
│   │   └── logger.ts
│   ├── types/                      # TypeScript types
│   │   └── index.ts
│   ├── generated/                  # PCF generated files
│   └── index.ts                    # Control entry point
├── build-wrapper.js                # Production build wrapper
├── eslint.config.mjs              # ESLint configuration
├── package.json                    # Dependencies
└── tsconfig.json                   # TypeScript config
```

### ESLint Configuration
The project uses comprehensive ESLint rules:
- TypeScript recommended + stylistic
- React best practices
- React Hooks rules
- Power Apps PCF checker
- Promise best practices

Run linting:
```bash
npx eslint .
```

### Logging

Enable debug logging:
```typescript
import { logger, LogLevel } from './utils/logger';

// In development, set debug level
logger.setLogLevel(LogLevel.DEBUG);
```

Log levels:
- `DEBUG`: Detailed development info
- `INFO`: General information (default)
- `WARN`: Warnings
- `ERROR`: Errors only

## Known Issues & Limitations

### Virtualization Not Implemented
**Status**: Attempted but reverted to standard DataGrid

**Issue**: The virtualized DataGrid (`@fluentui-contrib/react-data-grid-react-window-grid`) had critical alignment issues:
- Column headers misaligned with body cells
- Selection column width not calculated correctly
- Horizontal scrolling not syncing between header and body
- Required precise dimension matching that conflicted with responsive design

**Attempted Solutions**:
1. ✗ Fixed column widths with selection column offset
2. ✗ `noNativeElements` prop for div-based layout
3. ✗ Dynamic container sizing with ResizeObserver
4. ✗ Shared width calculations between header and body

**Root Cause**:
- Virtualization requires exact pixel-perfect dimensions
- Header uses `VariableSizeList`, body uses `VariableSizeGrid`
- Scroll synchronization requires refs and manual reset calls
- Incompatible with fluid/responsive container sizing

**Workaround**:
- Reverted to standard Fluent UI DataGrid (non-virtualized)
- Works well for datasets up to ~1000 records
- Proper column alignment maintained
- Native scrolling works correctly

**Future Sprint Recommendation**:
For large datasets (5000+ records), consider:
1. Server-side paging with load-more pattern
2. Alternative virtualization library (e.g., `@tanstack/react-virtual`)
3. Native browser virtualization with `content-visibility: auto`
4. Hybrid approach: virtualize only when record count exceeds threshold

**Reference**:
- Package: `@fluentui-contrib/react-data-grid-react-window-grid` v2.4.1
- Example: https://github.com/microsoft/fluentui-contrib/tree/main/packages/react-data-grid-react-window-grid
- Storybook: https://microsoft.github.io/fluentui-contrib/react-data-grid-react-window-grid/

### Bundle Size
Current: **470 KB** (production)
- Under 5 MB Dataverse limit ✅
- Could be optimized further with code splitting

### Performance
- Non-virtualized grid handles 1000s of records adequately
- Debounced output notifications (300ms) prevent excessive PCF calls
- React 18 concurrent features improve responsiveness

## Troubleshooting

### Control Not Loading
1. Check browser console for errors
2. Verify bundle.js loaded successfully
3. Check ErrorBoundary didn't catch startup error
4. Review structured logs with `[UniversalDatasetGrid]` prefix

### Deployment Issues
1. **Bundle > 5 MB**: Ensure production build (`npm run build:prod`)
2. **CPM Errors**: Temporarily disable with `move Directory.Packages.props Directory.Packages.props.disabled`
3. **MSBuild Issues**: Check `build-wrapper.js` is forcing production mode

### Theme Not Applied
1. Check Power Apps provides `fluentDesignLanguage.tokenTheme`
2. Review theme detection logs in console
3. Verify color luminance calculation for dark mode

## Version History

### v2.0.7 (Current)
- ✅ Complete Fluent UI v9 migration
- ✅ React 18 single root architecture
- ✅ Error boundary and structured logging
- ✅ Enhanced ESLint configuration
- ✅ Standard DataGrid (virtualization deferred)
- ✅ Comprehensive documentation

### v2.0.3
- React 18 migration (partial)
- Fluent UI v9 components

### v1.x
- Legacy implementation

## Contributing

### Code Standards
1. Follow TypeScript strict mode
2. Use Fluent UI design tokens (no hardcoded colors)
3. All components must be functional (no class components)
4. Use React hooks for state management
5. Add structured logging for key operations
6. Wrap risky operations in try-catch
7. Pass ESLint with zero warnings

### Pull Request Checklist
- [ ] Code passes ESLint (`npx eslint .`)
- [ ] TypeScript compiles without errors
- [ ] Production build succeeds
- [ ] Bundle size < 5 MB
- [ ] Tested in Power Apps environment
- [ ] Logs use centralized logger
- [ ] Error handling implemented
- [ ] Documentation updated

## License

Proprietary - Spaarke Internal Use

## Support

For issues or questions:
- Review logs in browser console
- Check this documentation
- Consult PCF framework docs: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/

---

**Built with ❤️ using React, Fluent UI, and Power Apps PCF Framework**
