/**
 * EmailProcessingMonitorHost — top-level React wrapper for the virtual PCF.
 *
 * Owns the lifecycle that previously lived imperatively in the PCF class:
 *  - Configuration validation (clientAppId / bffAppId / bffApiUrl)
 *  - @spaarke/auth initialization (useEffect, NOT PCF init)
 *  - Theme resolution via shared themeStorage
 *  - Loading / Ready / Error state — as React conditional renders, not innerHTML
 *
 * Per ADR-022 + /pcf-deploy skill: ReactControl's notifyOutputChanged() does
 * NOT reliably trigger updateView() for controls without two-way bound fields,
 * so async auth init MUST live in useState/useEffect.
 *
 * Dashboard rendering is delegated to the existing EmailProcessingDashboard
 * (which owns its own FluentProvider) — this host just decides which state
 * to render.
 */

import * as React from 'react';
import { useEffect, useState } from 'react';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth } from './authInit';
import { EmailProcessingDashboard } from './EmailProcessingDashboard';
import { getApiBaseUrl } from '../../shared/utils/environmentVariables';
import { getEffectiveDarkMode, setupThemeListener } from '@spaarke/ui-components/dist/utils/themeStorage';

export interface IEmailProcessingMonitorHostProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const EmailProcessingMonitorHost: React.FC<IEmailProcessingMonitorHostProps> = ({ context, version }) => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const ctxAny = context as any;
  const clientAppId: string = ctxAny.parameters?.clientAppId?.raw || '';
  const bffAppId: string = ctxAny.parameters?.bffAppId?.raw || '';
  // bffApiUrl resolved from sprk_BffApiBaseUrl Dataverse env var (single source of truth).
  // Per-instance PCF property removed (task 024) -- every environment must keep the env var current.
  const refreshIntervalSeconds: number = ctxAny.parameters?.refreshIntervalSeconds?.raw ?? 30;
  const controlHeight: number = ctxAny.parameters?.controlHeight?.raw ?? 400;

  const [authReady, setAuthReady] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [bffApiUrl, setBffApiUrl] = useState<string>('');
  const [isDarkTheme, setIsDarkTheme] = useState(() => getEffectiveDarkMode(context));

  // Auth init -- runs once on mount.
  // Resolves BFF URL from sprk_BffApiBaseUrl Dataverse env var FIRST, then initializes auth.
  // This replaces the per-instance bffApiUrl PCF property (removed in task 024).
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        if (!clientAppId || !bffAppId) {
          throw new Error('Missing required configuration: clientAppId and bffAppId must be provided');
        }
        // Resolve BFF base URL from Dataverse env var (single source of truth across all clients)
        const resolvedBffUrl = await getApiBaseUrl(ctxAny.webAPI);
        if (!resolvedBffUrl) {
          throw new Error(
            'sprk_BffApiBaseUrl Dataverse environment variable is not set or empty. Configure it in the SpaarkeCore solution.'
          );
        }
        if (cancelled) return;
        setBffApiUrl(resolvedBffUrl);
        console.log(`[EmailProcessingMonitorHost] Resolved BFF URL: ${resolvedBffUrl}`);
        await initializeAuth(clientAppId, bffAppId, resolvedBffUrl);
        if (!cancelled) setAuthReady(true);
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error('[EmailProcessingMonitorHost] Auth init failed:', err);
        if (!cancelled) setAuthError(msg);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [clientAppId, bffAppId, ctxAny.webAPI]);

  useEffect(() => {
    const cleanup = setupThemeListener(setIsDarkTheme, context);
    return cleanup;
  }, [context]);

  // Re-evaluate theme whenever PCF context changes
  useEffect(() => {
    setIsDarkTheme(getEffectiveDarkMode(context));
  }, [context]);

  // Host applies the outer height so the dashboard fills the configured area.
  // (Standard controls set container.style.height directly; virtual controls
  // don't get container access in init(), so this happens in JSX.)
  const wrapperStyle: React.CSSProperties = {
    width: '100%',
    minHeight: `${controlHeight}px`,
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
  };

  if (authError) {
    return (
      <div
        style={{
          ...wrapperStyle,
          padding: 20,
          border: '2px solid #d32f2f',
          backgroundColor: '#ffebee',
          color: '#c62828',
          borderRadius: 4,
        }}
        role="alert"
      >
        <strong>Email Processing Monitor Error</strong>
        <p>{authError}</p>
        <p>
          <small>Version: {version}</small>
        </p>
      </div>
    );
  }

  if (!authReady) {
    return (
      <div
        className="email-monitor-loading-overlay"
        role="status"
        aria-busy="true"
        aria-label="Loading email processing statistics"
        style={wrapperStyle}
      >
        <div className="email-monitor-loading-spinner" />
        <span className="email-monitor-loading-text">Loading statistics...</span>
      </div>
    );
  }

  return (
    <div style={wrapperStyle}>
      <EmailProcessingDashboard
        bffApiUrl={bffApiUrl}
        isDarkTheme={isDarkTheme}
        refreshIntervalSeconds={refreshIntervalSeconds}
        version={version}
        onError={err => {
          console.error('[EmailProcessingMonitorHost] Dashboard error:', err);
        }}
      />
    </div>
  );
};
