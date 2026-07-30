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
  createXrmDataService,
  createXrmNavigationService,
  createXrmEmailComposeHandlers,
  resolveCurrentUserEmail,
  searchUsersAndContacts,
  XrmDataverseClient,
  getXrm,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider } from '@spaarke/auth';
import { EmailWorkspace, type EmailWorkspaceWebApi } from '@spaarke/communication-components';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';

// ai-spaarke-ai-workspace-UI-r1 brittleness Phase D (2026-06-09) precedent:
// Initialize Application Insights so AppErrorBoundary.componentDidCatch can
// route errors to the "Failures" pane via reportClientError(). Key is sourced
// from a build-time Vite env var; absent in dev → no-op (boundary still logs
// to console). Override: VITE_APP_INSIGHTS_KEY=<key> npm run build
const _appInsightsKey: string = import.meta.env.VITE_APP_INSIGHTS_KEY ?? '';
if (_appInsightsKey) {
  AppInsightsService.initialize(_appInsightsKey);
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

const COMMUNICATION_ENTITY = 'sprk_communication';

/**
 * Minimal structural shape of the cross-frame Xrm surface this file reads for
 * Pattern A (embedded host form) record resolution. `getXrm()` is loosely typed
 * and the ambient `@types/xrm` `XrmStatic` doesn't expose `getPageContext` /
 * the legacy `Page`, so we cast to this local shape (all optional — every access
 * is null-guarded).
 */
interface HostFormXrm {
  Utility?: {
    getPageContext?: () => { input?: { entityName?: string; entityId?: string } } | undefined;
  };
  Page?: { data?: { entity?: { getId?: () => string | undefined } } };
}

/**
 * Resolve the incoming `sprk_communication` record id + view mode from the
 * launch context, in precedence order:
 *   (a) `data` query param — Pattern B (`Xrm.Navigation.navigateTo({ data })`,
 *       e.g. the launcher `openEmailRecord`). Carries the bare communication id,
 *       optionally with a folded `&single=1` (or `view=record`) flag.
 *   (b) `id` query param — direct-link / dev fallback.
 *   (c) the embedded host form's record — Pattern A (Email page embedded on a
 *       `sprk_communication` form), read via the cross-frame Xrm.
 *
 * Single-record ("form") mode is derived from a `single=1` / `view=record` flag,
 * which may arrive as its own top-level param OR folded into the `data` value.
 * `hideList` is only meaningful when an id resolves. When nothing resolves →
 * list mode (id `undefined`, `hideList` false) — i.e. behaves exactly as before.
 */
function resolveEmailLaunch(): { initialSelectedId?: string; hideList: boolean } {
  const normalizeId = (raw: string | null | undefined): string | undefined => {
    const v = (raw ?? '').replace(/[{}]/g, '').trim().toLowerCase();
    return v.length > 0 ? v : undefined;
  };
  const isSingleFlag = (params: URLSearchParams): boolean =>
    params.get('single') === '1' || params.get('view') === 'record';

  let id: string | undefined;
  let single = false;

  try {
    const search = typeof window !== 'undefined' ? window.location.search : '';
    const top = new URLSearchParams(search);

    // single / view may arrive as their own top-level param.
    single = isSingleFlag(top);

    // (a) data param (Pattern B). The value is either `<guid>` or, when the flag
    //     was folded in by the launcher, `<guid>&single=1` — split the id from
    //     any trailing flags (Dataverse surfaces the whole `data` string).
    const rawData = top.get('data');
    if (rawData) {
      const [idPart, ...rest] = rawData.split('&');
      id = normalizeId(idPart);
      if (rest.length > 0 && isSingleFlag(new URLSearchParams(rest.join('&')))) single = true;
    }

    // (b) id fallback param.
    if (!id) id = normalizeId(top.get('id'));
  } catch {
    /* URL parse failure → fall through to host-form / list mode */
  }

  // (c) embedded host form (Pattern A) — only when no param supplied an id.
  // `getXrm()` is loosely typed; the cross-frame `getPageContext()` and the
  // legacy `Page` surfaces aren't on the ambient `@types/xrm` `XrmStatic`, so
  // cast to a minimal local shape (repo `as unknown as` idiom) rather than any.
  if (!id) {
    try {
      const xrm = getXrm() as unknown as HostFormXrm | null;
      const input = xrm?.Utility?.getPageContext?.()?.input;
      if (input?.entityId && (!input.entityName || input.entityName === COMMUNICATION_ENTITY)) {
        id = normalizeId(input.entityId);
      }
      if (!id) {
        const pageId = xrm?.Page?.data?.entity?.getId?.();
        id = normalizeId(pageId);
      }
    } catch {
      /* not embedded on a form — list mode */
    }
  }

  return { initialSelectedId: id, hideList: Boolean(id) && single };
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

function Root() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const [bffBaseUrl, setBffBaseUrl] = React.useState<string | null>(null);
  const [bootstrapError, setBootstrapError] = React.useState<unknown>(null);
  const [attempt, setAttempt] = React.useState(0);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
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

  // Host-specific (Xrm-backed) adapters `EmailWorkspace` expects as props
  // (ADR-012 — the shared component stays Xrm-agnostic; this mount resolves
  // the concrete implementations).
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);
  const webApi = React.useMemo(() => buildXrmWebApi(), []);

  // Composer parity wiring (compose-wiring fixes #1/#2/#3/#5): recipient
  // typeahead + the Xrm-backed advanced-lookup handlers (people picker, record
  // lookup, add-relationship) + the Dataverse URL used for attachment
  // deep-links. Same handlers the CommunicationActions PCF builds — lifted into
  // the shared `createXrmEmailComposeHandlers` factory so both mounts stay in
  // parity (NFR-06).
  const handleSearchRecipients = React.useCallback(
    (query: string) => searchUsersAndContacts(dataService, query),
    [dataService]
  );
  // Pass auth + BFF URL so the factory also builds `onUploadLocalAttachment` (item 9b):
  // new-file attachments upload to the deployment SPE container (resolved from the
  // user's BU) and become governed `sprk_document`s. Re-memoized when bffBaseUrl resolves.
  const composeHandlers = React.useMemo(
    () =>
      createXrmEmailComposeHandlers({
        authenticatedFetch,
        bffBaseUrl: bffBaseUrl ?? undefined,
      }),
    [authenticatedFetch, bffBaseUrl]
  );
  const dataverseUrl = React.useMemo(() => getXrm()?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '', []);

  // Signed-in user's mailbox for the compose "From:" row (item 3) — the email surface
  // defaults From to send-as this user (switchable to the Spaarke shared mailbox).
  const [fromMailbox, setFromMailbox] = React.useState<string | undefined>();
  React.useEffect(() => {
    let cancelled = false;
    void resolveCurrentUserEmail().then(email => {
      if (!cancelled) setFromMailbox(email);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Resolve the incoming record + view mode ONCE from the launch context (URL
  // `data`/`id` param → embedded host form). Drives `initialSelectedId` (pre-select)
  // and `hideList` (single-record "form" mode). Absent → list mode (unchanged).
  const launch = React.useMemo(resolveEmailLaunch, []);

  const handleRetry = React.useCallback(() => setAttempt(n => n + 1), []);

  let body: React.ReactNode;
  if (bootstrapError) {
    body = <EmailPageAuthError onRetry={handleRetry} />;
  } else if (bffBaseUrl === null) {
    body = <EmailPageLoading />;
  } else {
    body = (
      <EmailWorkspace
        dataverseClient={dataverseClient}
        dataService={dataService}
        navigationService={navigationService}
        webApi={webApi}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onSearchRecipients={handleSearchRecipients}
        onLookupRecipients={composeHandlers.onLookupRecipients}
        recordLookupCatalog={composeHandlers.recordLookupCatalog}
        onLookupRecord={composeHandlers.onLookupRecord}
        onAddRelationship={composeHandlers.onAddRelationship}
        onUploadLocalAttachment={composeHandlers.onUploadLocalAttachment}
        fromMailbox={fromMailbox}
        dataverseUrl={dataverseUrl}
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
