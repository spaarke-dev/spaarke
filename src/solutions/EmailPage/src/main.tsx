/**
 * Email Code Page entry point (email-communication-solution-r5 task 042,
 * spec FR-02 / NFR-07, design Lens 2).
 *
 * Mirrors `src/solutions/DailyBriefing/src/main.tsx` exactly: `createRoot` +
 * host `FluentProvider` (theme via `resolveCodePageTheme` /
 * `setupCodePageThemeListener`) + `AppErrorBoundary` + an async, non-blocking
 * `bootstrapAuth()` (`resolveRuntimeConfig` → `setRuntimeConfig` →
 * `ensureAuthInitialized`). This is mount #2 of the dual-use Pattern D surface
 * — mount #1 is the SpaarkeAi `email` workspace widget (task 041). BOTH mounts
 * render the SAME shared `EmailWorkspace` from `@spaarke/communication-components`
 * unchanged (dual-mount parity, NFR-06); this file's only job is to bootstrap
 * auth/theme and supply the host-specific (Xrm-backed) adapters `EmailWorkspace`
 * expects as props (ADR-012 — the shared component stays Xrm-agnostic).
 *
 * Fail-closed (NFR-07): `dataverseClient` / `dataService` / `navigationService`
 * / `webApi` are backed by `Xrm.WebApi` — they work within the existing
 * authenticated MDA session and do not depend on this page's own auth
 * bootstrap. Only the BFF-backed `.eml` render + composer send calls require
 * `authenticatedFetch` (ADR-028). `EmailWorkspace` itself is gated on
 * `bootstrapAuth()` completing: while config/auth resolve, the pane shows a
 * loading state; if bootstrap throws (no resolvable auth context — e.g. no
 * signed-in account, unreachable BFF config), the pane shows an error/retry
 * state instead of mounting `EmailWorkspace` — no email content is ever
 * rendered without a resolved auth context.
 *
 * Web resource: sprk_emailpage (see package.json build script rename step).
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, Spinner, Text, Button, makeStyles, tokens } from '@fluentui/react-components';
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppErrorBoundary,
  AppInsightsService,
  useXrmEmailHostProps,
  type XrmEmailHostProps,
  getXrm,
  ensureNavigatorSidePane,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from '@spaarke/auth';
import {
  EmailWorkspace,
  useEmailComposeActions,
  type EmailWorkspaceWebApi,
} from '@spaarke/communication-components';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { resolveEmailLaunch, type ComposeLaunch } from './emailLaunch';

/**
 * Minimal structural shape of the cross-frame Xrm surface read for Pattern A (embedded host form) record resolution.
 * `getXrm()` is loosely typed and the ambient `XrmStatic` does not expose `getPageContext` / the legacy `Page`, so
 * the read casts to this local shape (all optional; every access is null-guarded).
 */
interface HostFormXrm {
  Utility?: {
    getPageContext?: () => { input?: { entityName?: string; entityId?: string } } | undefined;
  };
  Page?: { data?: { entity?: { getId?: () => string | undefined; getEntityName?: () => string | undefined } } };
}

const useStyles = makeStyles({
  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    boxSizing: 'border-box',
  },
});

/**
 * Bootstrap auth (config + MSAL + tenant ID). Non-blocking for the FluentProvider
 * shell — called from inside the component tree so the page paints immediately;
 * `EmailWorkspace` itself waits on this promise before mounting (see `Root`).
 */
async function bootstrapAuth(): Promise<void> {
  const config = await resolveRuntimeConfig();
  // Application Insights, so AppErrorBoundary.componentDidCatch routes errors to the
  // "Failures" pane via reportClientError(). #1537: the connection string is THIS
  // environment's, from the BFF at runtime (cached) — never a build-time key. Not
  // awaited and never rejects: a failed lookup means telemetry off, not a broken page.
  void AppInsightsService.initializeFromRuntime(() => getTelemetryConnectionString(config.bffBaseUrl));
  setRuntimeConfig(config);
  await ensureAuthInitialized();

  if (!config.tenantId) {
    const tenantId = await getAuthProvider().getTenantId();
    if (tenantId) {
      setRuntimeConfig({ ...config, tenantId });
    }
  }
}

/**
 * Builds the `EmailWorkspaceWebApi` bridge from `Xrm.WebApi` — lazily resolved
 * at call time (mirrors `createXrmDataService`/`XrmDataverseClient`) so
 * construction never throws even before Xrm is available; only individual
 * calls do, if Xrm truly never becomes available.
 */
function buildXrmWebApi(): EmailWorkspaceWebApi {
  function requireXrmWebApi() {
    const xrm = getXrm();
    if (!xrm?.WebApi) {
      throw new Error('Xrm.WebApi is not available. The Email code page requires a Dataverse-hosted context.');
    }
    return xrm.WebApi;
  }

  return {
    retrieveMultipleRecords: (entityLogicalName: string, query?: string, maxPageSize?: number) =>
      requireXrmWebApi().retrieveMultipleRecords(entityLogicalName, query, maxPageSize),
    retrieveRecord: (entityLogicalName: string, id: string, options?: string) =>
      requireXrmWebApi().retrieveRecord(entityLogicalName, id, options),
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) =>
      requireXrmWebApi().updateRecord(entityLogicalName, id, data),
  };
}

/** Loading state shown while `bootstrapAuth()` resolves config + MSAL init. */
const EmailPageLoading: React.FC = () => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="email-page-loading">
      <Spinner size="large" label="Loading Email…" />
    </div>
  );
};
EmailPageLoading.displayName = 'EmailPageLoading';

/**
 * Fail-closed error state (NFR-07). Rendered instead of `EmailWorkspace` when
 * `bootstrapAuth()` rejects — e.g. no resolvable auth context. No email
 * content is ever rendered without a resolved auth context.
 */
const EmailPageAuthError: React.FC<{ onRetry: () => void }> = ({ onRetry }) => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="email-page-auth-error">
      <Text weight="semibold" size={500}>
        Unable to load Email
      </Text>
      <Text>Sign-in could not be verified for this session. Retry, or reopen this page from Spaarke navigation.</Text>
      <Button appearance="primary" onClick={onRetry}>
        Retry
      </Button>
    </div>
  );
};
EmailPageAuthError.displayName = 'EmailPageAuthError';

interface EmailComposeStandaloneProps {
  compose: ComposeLaunch;
  bffBaseUrl: string;
  /** The shared Xrm-backed host wiring (adapters, compose handlers, From, org URL) — see `useXrmEmailHostProps`. */
  hostProps: XrmEmailHostProps;
}

/**
 * Standalone compose host for the `openEmailCompose` launcher. Mounts ONLY the
 * composer dialog (no reading surface), auto-triggers the requested action
 * (New / Reply / Reply All / Forward) once on mount — `useEmailComposeActions`
 * fetches the reply/forward source itself — and closes its own web-resource modal
 * window on Cancel or after a successful Send (via the `onClose` hook).
 */
const EmailComposeStandalone: React.FC<EmailComposeStandaloneProps> = ({
  compose,
  bffBaseUrl,
  hostProps,
}) => {
  const closedRef = React.useRef(false);
  const closeWindow = React.useCallback(() => {
    if (closedRef.current) return;
    closedRef.current = true;
    // Modal target=2 responds to window.close; postMessage is the host fallback
    // (mirrors NotepadShell's documented two-strategy close mechanism).
    try {
      window.close();
    } catch {
      /* ignore */
    }
    try {
      window.parent?.postMessage({ type: 'email-compose-close' }, '*');
    } catch {
      /* ignore */
    }
  }, []);

  const { actions, composerDialog, openComposer } = useEmailComposeActions({
    // Dedicated compose window (target=2 modal) — the composer IS the window.
    composerFullBleed: true,
    authenticatedFetch,
    bffBaseUrl,
    ...hostProps,
    onClose: closeWindow,
  });

  // Auto-open the requested composer ONCE on mount.
  const triggeredRef = React.useRef(false);
  React.useEffect(() => {
    if (triggeredRef.current) return;
    triggeredRef.current = true;
    // `useEmailComposeActions` always populates all four handlers; the toolbar-actions
    // type marks them optional, so optional-chain the calls (safe no-op if ever absent).
    const { mode, sourceId, regarding } = compose;
    if (mode === 'reply' && sourceId) actions.onReply?.(sourceId);
    else if (mode === 'replyAll' && sourceId) actions.onReplyAll?.(sourceId);
    else if (mode === 'forward' && sourceId) actions.onForward?.(sourceId);
    else if (regarding) openComposer('compose', undefined, { regarding });
    else actions.onNew?.();
  }, [actions, openComposer, compose]);

  // A subtle loading state shows behind the dialog during the reply/forward source
  // fetch; the composer dialog's own modal backdrop covers it once it opens.
  return (
    <>
      <EmailPageLoading />
      {composerDialog}
    </>
  );
};
EmailComposeStandalone.displayName = 'EmailComposeStandalone';

function Root() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const [bffBaseUrl, setBffBaseUrl] = React.useState<string | null>(null);
  const [bootstrapError, setBootstrapError] = React.useState<unknown>(null);
  const [attempt, setAttempt] = React.useState(0);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Global Navigator side pane (spaarke-side-pane-navigation-history-r1):
  // register the app-level Navigator pane on mount so it docks whenever a user
  // lands on this code page (modern UCI has no global app-load hook, so every
  // code-page host registers it). Idempotent + never throws — the shared
  // ensureNavigatorSidePane() no-ops if the pane already exists.
  React.useEffect(() => {
    ensureNavigatorSidePane();
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    setBootstrapError(null);
    bootstrapAuth()
      .then(() => {
        if (!cancelled) setBffBaseUrl(getBffBaseUrl());
      })
      .catch(err => {
        console.warn('[EmailPage] Auth bootstrap failed — Email workspace unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attempt]);

  // Host-specific (Xrm-backed) adapters `EmailWorkspace` expects as props (ADR-012 — the shared component stays
  // Xrm-agnostic). The Xrm-backed adapters, compose handlers (recipient lookup, record lookup, add-relationship, upload,
  // templates, AI draft, share links), the "From:" mailbox and the org URL: the same hook the SpaarkeAi widget and
  // the LegalWorkspace section use, so the mounts stay in parity (NFR-06, #2973). Its compose handlers are rebuilt
  // once `bffBaseUrl` resolves.
  const hostProps = useXrmEmailHostProps({ authenticatedFetch, bffBaseUrl });
  const webApi = React.useMemo(() => buildXrmWebApi(), []);

  // Resolve the incoming record + view mode ONCE from the launch context (URL
  // `data`/`id` param → embedded host form). Drives `initialSelectedId` (pre-select)
  // and `hideList` (single-record "form" mode). Absent → list mode (unchanged).
  const launch = React.useMemo(
    () =>
      resolveEmailLaunch(typeof window !== 'undefined' ? window.location.search : '', () => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = getXrm((x: any) => typeof x.Utility?.getPageContext === 'function' || !!x.Page?.data) as unknown as HostFormXrm | null;
        const entity = xrm?.Page?.data?.entity;
        return {
          pageInput: xrm?.Utility?.getPageContext?.()?.input,
          formEntityName: entity?.getEntityName?.(),
          formEntityId: entity?.getId?.(),
        };
      }),
    []
  );

  const handleRetry = React.useCallback(() => setAttempt(n => n + 1), []);

  let body: React.ReactNode;
  if (bootstrapError) {
    body = <EmailPageAuthError onRetry={handleRetry} />;
  } else if (bffBaseUrl === null) {
    body = <EmailPageLoading />;
  } else if (launch.compose) {
    // Compose mode (openEmailCompose) — mount ONLY the composer, not the workspace.
    body = (
      <EmailComposeStandalone
        compose={launch.compose}
        bffBaseUrl={bffBaseUrl}
        hostProps={hostProps}
      />
    );
  } else {
    body = (
      <EmailWorkspace
        {...hostProps}
        webApi={webApi}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        initialSelectedId={launch.initialSelectedId}
        hideList={launch.hideList}
      />
    );
  }

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <AppErrorBoundary surfaceName="Email">{body}</AppErrorBoundary>
    </FluentProvider>
  );
}

const rootElement = document.getElementById('root');
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <Root />
    </React.StrictMode>
  );
} else {
  // eslint-disable-next-line no-console
  console.error('[EmailPage] Root element not found');
}
