# PCF Error Handling Pattern

> **Domain**: PCF / Error Boundaries & User Experience
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-006, ADR-012

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalDatasetGrid/control/components/ErrorBoundary.tsx` | React error boundary |
| `src/client/pcf/SpeFileViewer/control/index.ts` | State machine error handling |

---

## Error Boundary Component

```typescript
import React from "react";
import { tokens } from "@fluentui/react-components";

interface ErrorBoundaryState {
    hasError: boolean;
    error: Error | null;
}

export class ErrorBoundary extends React.Component<
    { children: React.ReactNode },
    ErrorBoundaryState
> {
    constructor(props: { children: React.ReactNode }) {
        super(props);
        this.state = { hasError: false, error: null };
    }

    static getDerivedStateFromError(error: Error): ErrorBoundaryState {
        return { hasError: true, error };
    }

    componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
        console.error('ErrorBoundary caught error:', error, errorInfo);
        // Log to Application Insights if available
    }

    render(): React.ReactNode {
        if (this.state.hasError) {
            return (
                <div style={{
                    padding: tokens.spacingVerticalL,
                    backgroundColor: tokens.colorNeutralBackground1,
                    border: `1px solid ${tokens.colorPaletteRedBorder2}`,
                    borderRadius: tokens.borderRadiusMedium
                }}>
                    <h3 style={{ color: tokens.colorPaletteRedForeground1 }}>
                        Something went wrong
                    </h3>
                    <p>The component encountered an error and cannot display.</p>
                    <details>
                        <summary>Error details</summary>
                        <pre style={{ whiteSpace: 'pre-wrap' }}>
                            {this.state.error?.toString()}
                        </pre>
                    </details>
                </div>
            );
        }

        return this.props.children;
    }
}
```

---

## Usage in PCF

```typescript
private renderComponent(): void {
    this._root?.render(
        React.createElement(
            FluentProvider,
            { theme: this._theme },
            React.createElement(
                ErrorBoundary,
                null,
                React.createElement(RootComponent, { /* props */ })
            )
        )
    );
}
```

---

## State Machine Error Pattern

For controls with multiple states:

```typescript
enum ControlState {
    Loading = "Loading",
    Ready = "Ready",
    Error = "Error"
}

class MyControl {
    private _state: ControlState = ControlState.Loading;
    private _errorMessage: string = "";

    private transitionTo(newState: ControlState, error?: string): void {
        this._state = newState;
        this._errorMessage = error || "";
        this._notifyOutputChanged?.();
    }

    private async loadDataAsync(): Promise<void> {
        try {
            this.transitionTo(ControlState.Loading);
            const data = await fetchData();
            this.transitionTo(ControlState.Ready);
        } catch (error) {
            this.transitionTo(ControlState.Error, error.message);
        }
    }

    private render(): void {
        switch (this._state) {
            case ControlState.Loading:
                this.renderLoading();
                break;
            case ControlState.Ready:
                this.renderControl();
                break;
            case ControlState.Error:
                this.renderError(this._errorMessage);
                break;
        }
    }
}
```

---

## User-Friendly Error Messages

```typescript
function getErrorMessage(error: unknown): string {
    if (error instanceof Error) {
        // Map known error types to friendly messages
        if (error.message.includes('401')) {
            return 'Your session has expired. Please refresh the page.';
        }
        if (error.message.includes('403')) {
            return 'You do not have permission to perform this action.';
        }
        if (error.message.includes('404')) {
            return 'The requested item was not found.';
        }
        if (error.message.includes('Network')) {
            return 'Unable to connect. Please check your network connection.';
        }
    }
    return 'An unexpected error occurred. Please try again.';
}
```

---

## Error Display Component

```tsx
import { MessageBar, MessageBarBody, MessageBarTitle } from "@fluentui/react-components";

interface ErrorDisplayProps {
    error: Error | null;
    onDismiss?: () => void;
}

export const ErrorDisplay: React.FC<ErrorDisplayProps> = ({ error, onDismiss }) => {
    if (!error) return null;

    return (
        <MessageBar intent="error" onDismiss={onDismiss}>
            <MessageBarBody>
                <MessageBarTitle>Error</MessageBarTitle>
                {getErrorMessage(error)}
            </MessageBarBody>
        </MessageBar>
    );
};
```

---

## Key Principles

1. **Never show stack traces to users** - Use friendly messages
2. **Always log full errors** - Console or Application Insights
3. **Provide recovery options** - Refresh, retry, or contact support
4. **Use Fluent UI tokens** - Consistent error styling

---

## Related Patterns

- [Control Initialization](control-initialization.md) - Error boundary integration
- [PCF Constraints](../../constraints/pcf.md) - Error handling requirements

---

**Lines**: ~115
