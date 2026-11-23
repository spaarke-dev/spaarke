/**
 * Invoice Files Code Page entry point (ebilling-r1 task 151e).
 *
 * Embedded in the `sprk_invoice` main form (Files section) as a web resource control with "Pass record object-type code and unique
 * identifier as parameters", so the URL carries `id` and `typename` (see `services/launch.ts`). Lists the invoice's servable files from
 * `GET /api/finance/invoices/{invoiceId}/files`; Open and Download go through the content route. Every call goes through
 * `authenticatedFetch` to the BFF; the page never reads or writes Dataverse rows itself (ADR-002) - the file list is the server's
 * decision, and there is deliberately no `sprk_storedfile` subgrid (no role grants Read on it).
 *
 * Mirrors `src/solutions/ApExport/src/main.tsx`: `createRoot` + host `FluentProvider` (theme via `resolveCodePageTheme` /
 * `setupCodePageThemeListener`, dark mode included) + `AppErrorBoundary` + an async `bootstrapAuth()` (`resolveRuntimeConfig` ->
 * `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028). The page mounts only after that resolves; if it fails the page shows a retry
 * state (fail closed). The Navigator side pane is the form's own concern, so it is not registered here (this is an embedded section, not a
 * page the user navigates to).
 *
 * Web resource: sprk_invoicefiles (see the build rename in package.json).
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
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from '@spaarke/auth';
import { InvoiceFilesPanel } from './components/InvoiceFilesPanel';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createInvoiceFilesApi } from './services/invoiceFilesApi';
import { browserDeps } from './services/fileActions';
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
    backgroundColor: tokens.colorNeutralBackground1,
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

/** Save a blob as a file (an object URL and a temporary link); the page is same-origin with the app, so the download is the browser's own. */
function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Open the source email Document's form in the host (Xrm.Navigation.openForm through the parent frame). */
async function openDocument(documentId: string): Promise<void> {
  try {
    await createXrmNavigationService().openRecord('sprk_document', documentId);
  } catch (err) {
    console.warn('[InvoiceFiles] The source email could not be opened:', err);
  }
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

  React.useEffect(() => {
    // An unsaved or foreign record needs no auth: the page only says so.
    if (!launch.ok) return undefined;
    let cancelled = false;
    setBootstrapError(null);
    setReady(false);
    bootstrapAuth()
      .then(() => {
        if (!cancelled) setReady(true);
      })
      .catch(err => {
        console.warn('[InvoiceFiles] Auth bootstrap failed - page unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt, launch]);

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createInvoiceFilesApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const actionDeps = React.useMemo(() => (api ? browserDeps(api, saveBlob) : null), [api]);

  let body: React.ReactNode;
  if (!launch.ok) {
    body = <Message testId="invoicefiles-launch" title="Files" body={launch.message} />;
  } else if (bootstrapError) {
    body = (
      <Message
        testId="invoicefiles-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or reload the form."
        onRetry={() => setAttempt(n => n + 1)}
      />
    );
  } else if (!api || !actionDeps) {
    body = (
      <div className={s.centered} data-testid="invoicefiles-bootstrapping">
        <Spinner size="small" label="Loading files…" />
      </div>
    );
  } else {
    body = <InvoiceFilesPanel invoiceId={launch.invoiceId} api={api} actionDeps={actionDeps} openDocument={openDocument} />;
  }

  // The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div className={s.surface}>
        <AppErrorBoundary surfaceName="Invoice Files">{body}</AppErrorBoundary>
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
  console.error('[InvoiceFiles] Root element not found');
}
