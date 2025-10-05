import * as React from 'react';
import { tokens } from '@fluentui/react-components';
import { logger } from '../utils/logger';

interface ErrorBoundaryProps {
    children: React.ReactNode;
}

interface ErrorBoundaryState {
    hasError: boolean;
    error: Error | null;
}

/**
 * Error boundary component to catch React errors and display fallback UI
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
        logger.error('ErrorBoundary', 'React error caught', error, errorInfo);
    }

    render(): React.ReactNode {
        if (this.state.hasError) {
            return (
                <div
                    style={{
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'center',
                        height: '100%',
                        padding: tokens.spacingVerticalXL,
                        background: tokens.colorNeutralBackground1,
                        color: tokens.colorNeutralForeground1
                    }}
                >
                    <h3 style={{ color: tokens.colorPaletteRedForeground1, marginBottom: tokens.spacingVerticalM }}>
                        Something went wrong
                    </h3>
                    <p style={{ color: tokens.colorNeutralForeground2, marginBottom: tokens.spacingVerticalS }}>
                        The grid encountered an error and cannot display.
                    </p>
                    {this.state.error && (
                        <details style={{ marginTop: tokens.spacingVerticalM, maxWidth: '600px' }}>
                            <summary style={{ cursor: 'pointer', color: tokens.colorNeutralForeground2 }}>
                                Error details
                            </summary>
                            <pre
                                style={{
                                    marginTop: tokens.spacingVerticalS,
                                    padding: tokens.spacingVerticalS,
                                    background: tokens.colorNeutralBackground3,
                                    borderRadius: tokens.borderRadiusMedium,
                                    fontSize: '12px',
                                    overflow: 'auto'
                                }}
                            >
                                {this.state.error.toString()}
                            </pre>
                        </details>
                    )}
                </div>
            );
        }

        return this.props.children;
    }
}
