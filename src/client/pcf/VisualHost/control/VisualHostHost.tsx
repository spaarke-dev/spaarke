/**
 * VisualHostHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that previously lived imperatively in the PCF class:
 *  - Theme resolution + listener for dynamic theme changes
 *  - FluentProvider + ErrorBoundary + VisualHostRoot composition
 *
 * Per ADR-022: virtual controls return React elements from updateView()
 * rather than rendering imperatively into a container. The theme listener
 * lives in useEffect so it can react to localStorage / custom-event changes
 * without needing the PCF class to re-render manually.
 */

import * as React from 'react';
import { useEffect, useState } from 'react';
import { FluentProvider } from '@fluentui/react-components';
import { IInputs } from './generated/ManifestTypes';
import { VisualHostRoot } from './components/VisualHostRoot';
import { ErrorBoundary } from './components/ErrorBoundary';
import { resolveTheme, setupThemeListener } from './providers/ThemeProvider';

export interface IVisualHostHostProps {
  context: ComponentFramework.Context<IInputs>;
  notifyOutputChanged: () => void;
}

export const VisualHostHost: React.FC<IVisualHostHostProps> = ({ context, notifyOutputChanged }) => {
  const [theme, setTheme] = useState(() => resolveTheme(context));

  // Theme listener — fires on localStorage events + spaarke-theme-change custom events
  useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme(context));
    }, context);
    return cleanup;
  }, [context]);

  // Re-evaluate theme whenever Power Apps context changes
  useEffect(() => {
    setTheme(resolveTheme(context));
  }, [context]);

  return (
    <FluentProvider theme={theme} style={{ width: '100%' }}>
      <ErrorBoundary>
        <VisualHostRoot context={context} notifyOutputChanged={notifyOutputChanged} />
      </ErrorBoundary>
    </FluentProvider>
  );
};
