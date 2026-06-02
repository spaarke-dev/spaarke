/**
 * UniversalDatasetGridHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that previously lived imperatively in the PCF class:
 *  - @spaarke/auth initialization (in useEffect, NOT in PCF init)
 *  - Theme resolution + listener for dynamic theme changes
 *  - Loading state (spinner) while auth resolves
 *  - Error state (red panel) when auth fails
 *
 * Per ADR-022 + /pcf-deploy skill's "async init in ReactControl" rule:
 * For ComponentFramework.ReactControl, notifyOutputChanged() does NOT reliably
 * trigger updateView() for read-only dataset controls. Async init that needs
 * to flip the UI into a render state MUST live in useState/useEffect inside
 * React.
 *
 * The actual grid UI is delegated to the existing UniversalDatasetGridRoot
 * component (unchanged) — this host just decides which sub-state to render.
 */

import * as React from 'react';
import { useEffect, useState, useMemo } from 'react';
import { FluentProvider, Spinner } from '@fluentui/react-components';
import { IInputs } from './generated/ManifestTypes';
import { UniversalDatasetGridRoot } from './components/UniversalDatasetGridRoot';
import { ErrorBoundary } from './components/ErrorBoundary';
import { resolveTheme, setupThemeListener } from './providers/ThemeProvider';
import { DEFAULT_GRID_CONFIG, CalendarFilter, parseCalendarFilter } from './types';
import { logger } from './utils/logger';
import { initializeAuth } from './authInit';

export interface IUniversalDatasetGridHostProps {
  context: ComponentFramework.Context<IInputs>;
  notifyOutputChanged: () => void;
  /**
   * Row click handler — the PCF class wires this to capture the selected event
   * date for output write-back (Task 012). The handler mutates class state
   * (private _selectedEventDate) and calls notifyOutputChanged().
   */
  onRowClick: (date: string | null) => void;
}

export const UniversalDatasetGridHost: React.FC<IUniversalDatasetGridHostProps> = ({
  context,
  notifyOutputChanged,
  onRowClick,
}) => {
  const [authReady, setAuthReady] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [theme, setTheme] = useState(() => resolveTheme(context));

  // Auth init — runs once on mount. The PCF webAPI doesn't change between calls
  // so a single effect is sufficient.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        logger.info('UniversalDatasetGridHost', 'Initializing @spaarke/auth...');
        await initializeAuth(context.webAPI);
        if (!cancelled) {
          setAuthReady(true);
          logger.info('UniversalDatasetGridHost', '@spaarke/auth initialized');
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        logger.error('UniversalDatasetGridHost', '@spaarke/auth init failed', err);
        if (!cancelled) setAuthError(msg);
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  const calendarFilter: CalendarFilter | null = useMemo(
    () => parseCalendarFilter(context.parameters.calendarFilter?.raw),
    [context.parameters.calendarFilter?.raw]
  );

  const content = (() => {
    if (authError) {
      return (
        <div
          style={{
            padding: 20,
            margin: 10,
            border: '1px solid #d32f2f',
            backgroundColor: '#ffebee',
            color: '#c62828',
            borderRadius: 4,
            fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif",
            fontSize: 14,
          }}
          role="alert"
        >
          <strong>Authentication initialization failed</strong>
          <div style={{ marginTop: 8 }}>
            {authError}. Please refresh the page and try again. If the problem persists, contact your administrator.
          </div>
        </div>
      );
    }
    if (!authReady) {
      return (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: 80,
            padding: 16,
          }}
          role="status"
          aria-busy="true"
        >
          <Spinner size="small" label="Loading..." />
        </div>
      );
    }
    return (
      <ErrorBoundary>
        <UniversalDatasetGridRoot
          context={context}
          notifyOutputChanged={notifyOutputChanged}
          config={DEFAULT_GRID_CONFIG}
          calendarFilter={calendarFilter}
          onRowClick={onRowClick}
        />
      </ErrorBoundary>
    );
  })();

  return <FluentProvider theme={theme}>{content}</FluentProvider>;
};
