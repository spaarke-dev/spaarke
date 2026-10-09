/**
 * SpeDocumentViewerHost — top-level React wrapper for the PCF.
 *
 * Owns the lifecycle that previously lived imperatively in the PCF class:
 *  - Auth initialization via @spaarke/auth (in useEffect, NOT in PCF init)
 *  - Design-mode detection (renders a placeholder)
 *  - Loading state (spinner)
 *  - Error state (red message)
 *  - Theme resolution (light/dark)
 *
 * Per ADR-022 + the /pcf-deploy skill rule: For ComponentFramework.ReactControl
 * (virtual controls), notifyOutputChanged() does NOT reliably trigger updateView()
 * for read-only field-bound controls. Async init that needs to flip the UI into
 * a render state MUST live in useState/useEffect inside React.
 *
 * The actual document viewer UI is delegated to the existing DocumentViewerApp
 * component (unchanged) — this host just decides which sub-state to render.
 */

import * as React from 'react';
import { useEffect, useState, useCallback } from 'react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  tokens,
} from '@fluentui/react-components';
import { initializeAuth, resolveViewerAuth } from './authInit';
import { DocumentViewerApp } from './SpeDocumentViewer';
import { getApiBaseUrl } from '../../shared/utils/environmentVariables';

const THEME_STORAGE_KEY = 'spaarke-theme';
const THEME_CHANGE_EVENT = 'spaarke-theme-change';

type ThemePreference = 'light' | 'dark' | 'auto';

function getUserThemePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'auto') return stored;
  } catch {
    /* ignore */
  }
  return 'auto';
}

function getEffectiveDarkMode(context: ComponentFramework.Context<unknown>): boolean {
  const preference = getUserThemePreference();
  if (preference === 'dark') return true;
  if (preference === 'light') return false;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  if ((context as any)?.fluentDesignLanguage?.isDarkTheme !== undefined) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (context as any).fluentDesignLanguage.isDarkTheme;
  }

  const navbar = document.querySelector("[data-id='navbar-container']");
  if (navbar) {
    const bg = getComputedStyle(navbar).backgroundColor;
    if (bg === 'rgb(10, 10, 10)') return true;
    if (bg === 'rgb(240, 240, 240)') return false;
  }

  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
}

/**
 * Heuristics borrowed from the old PCF class. When the control is hosted inside
 * the form designer / app designer / preview iframe, we render a static
 * placeholder instead of attempting authentication.
 */
function isDesignMode(context: ComponentFramework.Context<unknown>): boolean {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const ctxAny = context as any;
  try {
    if (window.parent !== window) {
      const parentUrl = window.parent.location.href.toLowerCase();
      if (parentUrl.includes('/designer/') || parentUrl.includes('/formeditor/') || parentUrl.includes('appdesigner')) {
        return true;
      }
    }
  } catch {
    /* cross-origin parent — fall through */
  }
  if (ctxAny?.mode?.isAuthoringMode === true) return true;
  return false;
}

const GUID_REGEX = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

function extractDocumentId(context: ComponentFramework.Context<unknown>): string {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const ctxAny = context as any;
  const rawValue = ctxAny.parameters?.documentId?.raw;
  if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
    const trimmed = rawValue.trim();
    if (GUID_REGEX.test(trimmed)) return trimmed;
    throw new Error('Document ID must be a GUID format.');
  }
  const recordId = ctxAny.mode?.contextInfo?.entityId;
  if (recordId && typeof recordId === 'string') {
    if (GUID_REGEX.test(recordId)) return recordId;
    throw new Error('Form context did not provide a valid GUID.');
  }
  return '';
}

// ---------------------------------------------------------------------------
// Static UI fragments (replace the imperative innerHTML the PCF class used)
// ---------------------------------------------------------------------------

// Rendered inside the FluentProvider below, so semantic tokens follow the light/dark theme (ADR-021).
const DesignModePlaceholder: React.FC = () => {
  const bg = tokens.colorNeutralBackground2;
  const fg = tokens.colorNeutralForeground1;
  const border = tokens.colorNeutralStroke1;
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        minHeight: 200,
        backgroundColor: bg,
        border: `2px dashed ${border}`,
        borderRadius: 8,
        padding: 20,
        textAlign: 'center',
      }}
    >
      <div style={{ marginTop: 16, color: fg, fontWeight: 600, fontSize: 14 }}>SPE Document Viewer</div>
      <div style={{ marginTop: 8, color: fg, opacity: 0.7, fontSize: 12 }}>Document preview will appear at runtime</div>
    </div>
  );
};

const LoadingOverlay: React.FC = () => (
  <div className="spe-document-viewer-loading-overlay" role="status" aria-busy="true">
    <Spinner size="small" label="Loading document viewer..." />
  </div>
);

const ErrorPanel: React.FC<{ message: string; correlationId: string }> = ({ message, correlationId }) => (
  <MessageBar intent="error" style={{ margin: tokens.spacingVerticalM }}>
    <MessageBarBody>
      <MessageBarTitle>Document viewer unavailable</MessageBarTitle>
      {message}
      <div style={{ fontSize: tokens.fontSizeBase100 }}>Correlation ID: {correlationId}</div>
    </MessageBarBody>
  </MessageBar>
);

// ---------------------------------------------------------------------------
// Host component
// ---------------------------------------------------------------------------

export interface ISpeDocumentViewerHostProps {
  context: ComponentFramework.Context<unknown>;
  correlationId: string;
}

export const SpeDocumentViewerHost: React.FC<ISpeDocumentViewerHostProps> = ({ context, correlationId }) => {
  const [authReady, setAuthReady] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [bffApiUrl, setBffApiUrl] = useState<string>('');
  const [isDarkTheme, setIsDarkTheme] = useState(() => getEffectiveDarkMode(context));

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const ctxAny = context as any;
  // The form's tenantId / clientAppId / bffAppId properties are intentionally NOT read:
  // they hold dev values shipped in SpaarkeMaster (see authInit.ts). Kept in the manifest
  // only so existing form customizations stay valid.
  // bffApiUrl resolved from sprk_BffApiBaseUrl Dataverse env var (single source of truth).
  // Per-instance PCF property removed (task 024) — every environment must keep the env var current.
  const enableEdit = ctxAny.parameters?.enableEdit?.raw ?? true;
  const enableDelete = ctxAny.parameters?.enableDelete?.raw ?? false;
  const enableDownload = ctxAny.parameters?.enableDownload?.raw ?? true;
  const showToolbar = ctxAny.parameters?.showToolbar?.raw ?? false;
  // Virtual controls don't get container access in init() — the framework allocates
  // a host element whose height may not propagate to height:100% children. Read the
  // controlHeight parameter (default 600) and apply it explicitly to the outer
  // wrapper so the iframe + document viewer fill the configured area, matching the
  // old StandardControl behavior.
  const controlHeight: number = ctxAny.parameters?.controlHeight?.raw ?? 600;
  const designMode = isDesignMode(context);

  // Auth init — runs once on mount (skipped in design mode).
  // Resolves the BFF URL (sprk_BffApiBaseUrl) and the tenant / MSAL client / BFF app id
  // (environment variables, plus the Xrm organization tenant — see authInit.ts), then initializes
  // auth with a tenant-specific authority. Missing or non-GUID values fail closed.
  useEffect(() => {
    if (designMode) return;
    let cancelled = false;
    (async () => {
      try {
        const resolvedAuth = await resolveViewerAuth(ctxAny.webAPI);
        // Resolve BFF base URL from Dataverse env var (single source of truth across all clients)
        const resolvedBffUrl = await getApiBaseUrl(ctxAny.webAPI);
        if (!resolvedBffUrl) {
          throw new Error(
            'sprk_BffApiBaseUrl Dataverse environment variable is not set or empty. Configure it in the SpaarkeCore solution.'
          );
        }
        if (cancelled) return;
        setBffApiUrl(resolvedBffUrl);
        console.info(`[SpeDocumentViewer] Resolved BFF URL from sprk_BffApiBaseUrl: ${resolvedBffUrl}`);
        await initializeAuth(resolvedAuth, resolvedBffUrl);
        if (!cancelled) {
          setAuthReady(true);
          console.info('[SpeDocumentViewer] @spaarke/auth initialized');
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error('[SpeDocumentViewer] Auth init failed:', err);
        // A blocked / interaction-required sign-in is not fatal: render the viewer, whose first
        // authenticatedFetch retries token acquisition (and surfaces its own error if that fails).
        // Configuration errors from resolveViewerAuth never match these words and fail closed below.
        if (
          msg.toLowerCase().includes('popup') ||
          msg.toLowerCase().includes('blocked') ||
          msg.toLowerCase().includes('interaction_required')
        ) {
          if (!cancelled) setAuthReady(true);
          return;
        }
        if (!cancelled) setAuthError(msg);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [designMode, ctxAny.webAPI]);

  // Theme listener
  useEffect(() => {
    const handle = () => setIsDarkTheme(getEffectiveDarkMode(context));
    const onStorage = (ev: StorageEvent) => {
      if (ev.key === THEME_STORAGE_KEY) handle();
    };
    const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
    const onMq = (ev: MediaQueryListEvent) => {
      if (getUserThemePreference() === 'auto') setIsDarkTheme(ev.matches);
    };
    window.addEventListener('storage', onStorage);
    window.addEventListener(THEME_CHANGE_EVENT, handle);
    mq?.addEventListener('change', onMq);
    return () => {
      window.removeEventListener('storage', onStorage);
      window.removeEventListener(THEME_CHANGE_EVENT, handle);
      mq?.removeEventListener('change', onMq);
    };
  }, [context]);

  const theme = isDarkTheme ? webDarkTheme : webLightTheme;

  const handleRefresh = useCallback(() => {
    console.info('[SpeDocumentViewer] Refresh requested');
  }, []);
  const handleDeleted = useCallback(() => {
    console.info('[SpeDocumentViewer] Document deleted');
  }, []);

  let documentId = '';
  try {
    documentId = extractDocumentId(context);
  } catch {
    /* no-op */
  }

  const content = (() => {
    if (designMode) return <DesignModePlaceholder />;
    if (authError) return <ErrorPanel message={authError} correlationId={correlationId} />;
    if (!authReady) return <LoadingOverlay />;
    return (
      <DocumentViewerApp
        documentId={documentId}
        bffApiUrl={bffApiUrl}
        correlationId={correlationId}
        isDarkTheme={isDarkTheme}
        enableEdit={enableEdit}
        enableDelete={enableDelete}
        enableDownload={enableDownload}
        showToolbar={showToolbar}
        onRefresh={handleRefresh}
        onDeleted={handleDeleted}
      />
    );
  })();

  return (
    <FluentProvider
      theme={theme}
      style={{
        width: '100%',
        height: `${controlHeight}px`,
        minHeight: `${controlHeight}px`,
        maxHeight: `${controlHeight}px`,
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
      }}
    >
      {content}
    </FluentProvider>
  );
};
