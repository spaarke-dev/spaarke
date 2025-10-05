# Phase D: Code Quality & Standards

**Sprint:** 5B - Universal Dataset Grid Compliance
**Phase:** D - Code Quality & Standards
**Priority:** LOW (Can be done in parallel with SDAP)
**Estimated Effort:** 1 day
**Status:** 🔴 Not Started
**Depends On:** Phase A completion

---

## Objective

Clean up code quality, add proper error handling, enforce linting rules, and update documentation to maintain high standards and prevent future ADR violations.

---

## Current Issues

**From Compliance Assessment (Section 2.1 & 5):**
> "Remove excessive console logging; add error boundaries or telemetry integration."

**Problems:**
1. Excessive console.log statements (production code)
2. No error boundaries for React errors
3. No ESLint rules to prevent ADR violations
4. Missing/outdated documentation
5. No automated testing

---

## Tasks

### Task D.1: Remove Debug Logging & Add Error Handling

**Files:** All `.ts` and `.tsx` files

**Objective:** Clean up logging and add production-ready error handling

**AI Coding Instructions:**

```typescript
/**
 * Task D.1.1: Remove/Replace Debug Logging
 *
 * REMOVE all development console.log statements:
 * - console.log('[UniversalDatasetGrid] Starting init...')
 * - console.log('[ThemeProvider] initialize() called')
 * - console.log('[CommandBar] Rendering...')
 *
 * KEEP only:
 * - console.error() for errors
 * - console.warn() for warnings
 * - Critical initialization logs (can be removed in production build)
 */

// Instead of console.log everywhere, create a logger utility:

// File: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/utils/logger.ts
const IS_DEV = process.env.NODE_ENV === 'development';

export const logger = {
    debug: (message: string, ...args: any[]) => {
        if (IS_DEV) {
            console.log(`[UniversalDatasetGrid] ${message}`, ...args);
        }
    },
    info: (message: string, ...args: any[]) => {
        console.info(`[UniversalDatasetGrid] ${message}`, ...args);
    },
    warn: (message: string, ...args: any[]) => {
        console.warn(`[UniversalDatasetGrid] ${message}`, ...args);
    },
    error: (message: string, error?: Error | unknown, ...args: any[]) => {
        console.error(`[UniversalDatasetGrid] ${message}`, error, ...args);
    }
};

// Then replace all console.log with logger.debug:
// BEFORE:
console.log('[UniversalDatasetGrid] Init complete');

// AFTER:
logger.debug('Init complete');
```

**Task D.1.2: Add Error Boundaries**

```typescript
/**
 * Create Error Boundary component for React errors
 *
 * File: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/ErrorBoundary.tsx
 */

import * as React from 'react';
import { tokens } from '@fluentui/react-components';

interface ErrorBoundaryProps {
    children: React.ReactNode;
}

interface ErrorBoundaryState {
    hasError: boolean;
    error: Error | null;
}

/**
 * Error Boundary for catching React rendering errors.
 *
 * Prevents entire control from crashing due to component errors.
 */
export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
    constructor(props: ErrorBoundaryProps) {
        super(props);
        this.state = { hasError: false, error: null };
    }

    static getDerivedStateFromError(error: Error): ErrorBoundaryState {
        return { hasError: true, error };
    }

    componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
        console.error('[ErrorBoundary] Caught error:', error, errorInfo);

        // TODO: Send to telemetry service (Application Insights, etc.)
        // trackException({ exception: error, properties: errorInfo });
    }

    render(): React.ReactNode {
        if (this.state.hasError) {
            return (
                <div
                    style={{
                        padding: tokens.spacingVerticalXXL,
                        textAlign: 'center',
                        color: tokens.colorPaletteRedForeground1
                    }}
                >
                    <h2>Something went wrong</h2>
                    <p>The Universal Dataset Grid encountered an error.</p>
                    <details style={{ marginTop: tokens.spacingVerticalM, textAlign: 'left' }}>
                        <summary>Error Details</summary>
                        <pre style={{ fontSize: tokens.fontSizeBase200 }}>
                            {this.state.error?.message}
                            {'\n\n'}
                            {this.state.error?.stack}
                        </pre>
                    </details>
                    <button
                        onClick={() => this.setState({ hasError: false, error: null })}
                        style={{
                            marginTop: tokens.spacingVerticalL,
                            padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`
                        }}
                    >
                        Try Again
                    </button>
                </div>
            );
        }

        return this.props.children;
    }
}

// Wrap UniversalDatasetGridRoot in ErrorBoundary:
// File: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts

this.root.render(
    React.createElement(
        FluentProvider,
        { theme },
        React.createElement(
            ErrorBoundary,
            null,
            React.createElement(UniversalDatasetGridRoot, {
                context,
                notifyOutputChanged: this.notifyOutputChanged,
                config: this.config
            })
        )
    )
);
```

---

### Task D.2: Add ESLint Rules

**File:** `src/controls/UniversalDatasetGrid/eslint.config.mjs`

**Objective:** Prevent future ADR violations with automated linting

**AI Coding Instructions:**

```javascript
/**
 * Add ESLint rules to enforce Fluent UI v9 compliance
 *
 * File: eslint.config.mjs
 */

import tseslint from 'typescript-eslint';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import powerApps from '@microsoft/eslint-plugin-power-apps';

export default [
    ...tseslint.configs.recommended,
    {
        files: ['**/*.ts', '**/*.tsx'],
        plugins: {
            react,
            'react-hooks': reactHooks,
            'power-apps': powerApps
        },
        rules: {
            // Existing rules
            '@typescript-eslint/no-unused-vars': 'off',
            '@typescript-eslint/no-explicit-any': 'warn',

            // Prevent raw HTML elements (Fluent UI compliance)
            'no-restricted-syntax': [
                'error',
                {
                    selector: "JSXOpeningElement[name.name='table']",
                    message: 'Use Fluent UI DataGrid instead of raw <table>'
                },
                {
                    selector: "JSXOpeningElement[name.name='button'][name.name!='Button']",
                    message: 'Use Fluent UI Button component instead of raw <button>'
                }
            ],

            // Prevent inline styles
            'react/forbid-dom-props': [
                'warn',
                {
                    forbid: [
                        {
                            propName: 'style',
                            message: 'Use Fluent UI makeStyles or design tokens instead of inline styles'
                        }
                    ]
                }
            ],

            // React hooks rules
            'react-hooks/rules-of-hooks': 'error',
            'react-hooks/exhaustive-deps': 'warn',

            // Power Apps specific rules
            'power-apps/avoid-inline-styles': 'warn',
            'power-apps/use-platform-api': 'warn'
        }
    }
];
```

---

### Task D.3: Update Documentation

**Files to Create/Update:**
- `README.md` - Architecture overview
- `CONTRIBUTING.md` - Development guidelines
- `ARCHITECTURE.md` - Component structure

**Objective:** Document new architecture and standards

**AI Coding Instructions:**

```markdown
/**
 * Create comprehensive README.md
 *
 * File: src/controls/UniversalDatasetGrid/README.md
 */

# Universal Dataset Grid PCF Control

Fluent UI v9 compliant dataset grid control for Power Platform with SDAP integration.

## Features

- ✅ **Fluent UI v9**: Full compliance with Microsoft Fluent Design System
- ✅ **React 18**: Modern React architecture with single root pattern
- ✅ **Theming**: Dynamic light/dark/high-contrast theme support
- ✅ **Performance**: Dataset paging and optimized rendering for large datasets
- ✅ **Accessibility**: WCAG 2.1 AA compliant
- ✅ **SDAP Integration**: Secure file operations through SDAP service

## Architecture

```
PCF Container (index.ts)
  └─> React Root
      └─> FluentProvider (theme wrapper)
          └─> ErrorBoundary
              └─> UniversalDatasetGridRoot
                  ├─> CommandBar (Fluent Toolbar)
                  ├─> DatasetGrid (Fluent DataGrid)
                  └─> Future: Upload/Download dialogs
```

## Development

### Prerequisites
- Node.js 18+
- Power Platform CLI
- Power Apps environment

### Build
```bash
npm install
npm run build
```

### Deploy
```bash
pac pcf push --publisher-prefix sprk
```

### Lint
```bash
npm run lint
```

## Component Structure

- `index.ts` - PCF lifecycle management
- `components/`
  - `UniversalDatasetGridRoot.tsx` - Main React component
  - `CommandBar.tsx` - Fluent UI Toolbar with file operations
  - `DatasetGrid.tsx` - Fluent UI DataGrid wrapper
  - `ErrorBoundary.tsx` - Error handling
- `providers/`
  - `ThemeProvider.ts` - Theme resolution
- `types/` - TypeScript type definitions
- `utils/` - Helper functions

## Standards

- **Fluent UI v9 Only**: No raw HTML elements (table, button, div with inline styles)
- **Design Tokens**: Use `tokens.*` for all styling
- **React 18**: Use hooks, no class components
- **TypeScript**: Strict mode enabled
- **Accessibility**: All components keyboard navigable

## Testing

See `TESTING.md` for comprehensive test scenarios.

## References

- [Fluent UI v9 Docs](https://react.fluentui.dev/)
- [PCF Documentation](https://learn.microsoft.com/power-apps/developer/component-framework/)
- [Sprint 5B Compliance Tasks](../../../dev/projects/dataset_pcf_component/Sprint%205B/)
```

---

### Task D.4: Optional - Add Automated Tests

**Objective:** Add basic tests for critical components

**AI Coding Instructions:**

```typescript
/**
 * OPTIONAL: Add React Testing Library tests
 *
 * This is optional but recommended for production code.
 *
 * Install:
 * npm install --save-dev @testing-library/react @testing-library/jest-dom jest
 */

// File: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/__tests__/CommandBar.test.tsx

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CommandBar } from '../CommandBar';

describe('CommandBar', () => {
    const mockConfig = {
        fieldMappings: {
            hasFile: 'hasFile'
        }
    };

    const mockExecute = jest.fn();

    it('renders all buttons', () => {
        render(
            <FluentProvider theme={webLightTheme}>
                <CommandBar
                    config={mockConfig}
                    selectedRecordIds={[]}
                    selectedRecords={[]}
                    onCommandExecute={mockExecute}
                    onRefresh={jest.fn()}
                />
            </FluentProvider>
        );

        expect(screen.getByText('Add File')).toBeInTheDocument();
        expect(screen.getByText('Remove File')).toBeInTheDocument();
        expect(screen.getByText('Update File')).toBeInTheDocument();
        expect(screen.getByText('Download')).toBeInTheDocument();
    });

    it('disables Add File when no selection', () => {
        render(
            <FluentProvider theme={webLightTheme}>
                <CommandBar
                    config={mockConfig}
                    selectedRecordIds={[]}
                    selectedRecords={[]}
                    onCommandExecute={mockExecute}
                    onRefresh={jest.fn()}
                />
            </FluentProvider>
        );

        const addButton = screen.getByText('Add File').closest('button');
        expect(addButton).toBeDisabled();
    });

    // Add more tests...
});
```

---

## Testing Checklist

### Code Quality
- [ ] All console.log replaced with logger.debug
- [ ] logger.debug statements won't appear in production build
- [ ] Error boundaries catch and display errors gracefully
- [ ] No TypeScript errors or warnings
- [ ] ESLint passes with no violations

### ESLint Rules
- [ ] ESLint catches raw `<table>` usage
- [ ] ESLint warns about inline styles
- [ ] React hooks rules enforced
- [ ] Custom rules work correctly

### Documentation
- [ ] README.md up to date
- [ ] Architecture documented
- [ ] Development instructions clear
- [ ] Examples provided

---

## Validation Criteria

### Success Criteria:
1. ✅ No debug console.log in production code
2. ✅ Error boundaries prevent crashes
3. ✅ ESLint rules enforce Fluent UI compliance
4. ✅ Documentation complete and accurate
5. ✅ Tests pass (if implemented)

### Code Quality:
- ✅ Clean, maintainable code
- ✅ Proper error handling
- ✅ Consistent formatting
- ✅ No linting violations

---

## References

- **Compliance Assessment:** Section 2.1 "Accessibility & telemetry"
- **Compliance Assessment:** Section 4.4 "ESLint Rule to Enforce Fluent Components"
- **Compliance Assessment:** Section 5 "Implementation Checklist"
- **React Error Boundaries:** https://react.dev/reference/react/Component#catching-rendering-errors-with-an-error-boundary

---

## Completion Criteria

Phase D is complete when:
1. All tasks completed (D.1-D.4)
2. Code quality standards met
3. ESLint rules working
4. Documentation updated
5. All validation criteria met

---

_Document Version: 1.0_
_Created: 2025-10-05_
_Status: Ready for Implementation_
_Can be done in parallel with Phases B/C or SDAP work_
