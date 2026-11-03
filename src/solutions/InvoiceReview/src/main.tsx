/**
 * Invoice Review Code Page entry point (ebilling-r1 task 110; FR-31, FR-32, D-58).
 *
 * Opened with an invoice id (`data=invoiceId%3D{guid}`, `?invoiceId=`, or `id` + `typename=sprk_invoice`; see `services/launch.ts`). Every
 * call goes through `authenticatedFetch` to the BFF; the page never reads or writes Dataverse rows itself (ADR-002): the review, the
 * decisions, the corrections and the PDF bytes are all the server's.
 *
 * Mirrors `src/solutions/InvoiceFiles/src/main.tsx` (and `CommunicationReconciliation/src/main.tsx` for the Navigator): `createRoot` + host
 * `FluentProvider` (theme via `resolveCodePageTheme` / `setupCodePageThemeListener`, dark mode included) + `AppErrorBoundary` + an async
 * `bootstrapAuth()` (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028) that the page waits on before it
 * mounts; a failed bootstrap shows a retry state (fail closed). The Navigator side pane is registered on mount
 * (`ensureNavigatorSidePane()`, docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md).
 *
 * Web resource: sprk_invoicereviewpage (see the build rename in package.json). Its navigation entry is task 112's.
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, Spinner, Text, Button, makeStyles, tokens } from '@fluentui/react-components';
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppErrorBoundary,
  AppInsightsService,
  parseDataParams,
  createXrmNavigationService,
  ensureNavigatorSidePane,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from '@spaarke/auth';
import { InvoiceReviewWithDecisions } from './components/InvoiceReviewWithDecisions';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createInvoiceReviewApi } from './services/invoiceReviewApi';
import { createInvoiceDecisionApi } from './services/decisionApi';
import { parseLaunch } from './services/launch';

const useStyles = makeStyles({
  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalL,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  surface: {
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
  },
});

/** Bootstrap auth (config + MSAL + tenant ID); the page waits on this before it mounts. */
async function bootstrapAuth(): Promise<void> {
  const config = await resolveRuntimeConfig();
  // Application Insights at runtime from this environment's connection string (issue 1537); never awaited, never rejects.
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

/** Open the invoice's matter, project or work assignment in the host (Xrm.Navigation through the parent frame). */
function openRecord(entityName: string, id: string): void {
  createXrmNavigationService()
    .openRecord(entityName, id)
    .catch(err => console.warn('[InvoiceReview] The record could not be opened:', err));
}

const Message: React.FC<{ testId: string; title: string; body: string; onRetry?: () => void }> = ({ testId, title, body, onRetry }) => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid={testId}>
      <Text weight="semibold">{title}</Text>
      <Text>{body}</Text>
      {onRetry && (
        <Button appearance="primary" size="small" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
};

function Root() {
  const s = useStyles();
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const [ready, setReady] = React.useState(false);
  const [bootstrapError, setBootstrapError] = React.useState<unknown>(null);
  const [attempt, setAttempt] = React.useState(0);
  const launch = React.useMemo(() => parseLaunch(parseDataParams()), []);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Global Navigator side pane: every code-page host registers it on mount (idempotent, never throws).
  React.useEffect(() => {
    ensureNavigatorSidePane();
  }, []);

  React.useEffect(() => {
    if (!launch.ok) return undefined;
    let cancelled = false;
    setBootstrapError(null);
    setReady(false);
    bootstrapAuth()
      .then(() => {
        if (!cancelled) setReady(true);
      })
      .catch(err => {
        console.warn('[InvoiceReview] Auth bootstrap failed - page unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt, launch]);

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createInvoiceReviewApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const decisionApi = React.useMemo(() => (ready ? createInvoiceDecisionApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);

  let body: React.ReactNode;
  if (!launch.ok) {
    body = <Message testId="invoicereview-launch" title="Invoice Review" body={launch.message} />;
  } else if (bootstrapError) {
    body = (
      <Message
        testId="invoicereview-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or reload the page."
        onRetry={() => setAttempt(n => n + 1)}
      />
    );
  } else if (!api || !decisionApi) {
    body = (
      <div className={s.centered} data-testid="invoicereview-bootstrapping">
        <Spinner size="small" label="Loading…" />
      </div>
    );
  } else {
    body = <InvoiceReviewWithDecisions invoiceId={launch.invoiceId} api={api} decisionApi={decisionApi}openRecord={openRecord} />;
  }

  // The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div className={s.surface}>
        <AppErrorBoundary surfaceName="Invoice Review">{body}</AppErrorBoundary>
      </div>
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
  console.error('[InvoiceReview] Root element not found');
}
