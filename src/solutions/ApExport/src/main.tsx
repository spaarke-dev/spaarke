/**
 * AP Export Code Page entry point (ebilling-r1 task 151b, EB-D-181; FR-41, FR-42).
 *
 * A dialog page: the ribbon script `sprk_/scripts/apexport_ribbon.js` opens it with `Xrm.Navigation.navigateTo`
 * (target 2) from the sprk_invoice views and form, with `data = "mode=<export|markpaid>&ids=..."`. ADR-050: a hostless
 * ribbon script launches with navigateTo; the platform dialog carries the title and the close button.
 *
 * Mirrors `src/solutions/RuleReview/src/main.tsx`: `createRoot` + host `FluentProvider` (theme via
 * `resolveCodePageTheme` / `setupCodePageThemeListener`) + `AppErrorBoundary` + an async `bootstrapAuth()`
 * (`resolveRuntimeConfig` -> `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028). The page mounts only after that
 * resolves; if it fails the page shows a retry state (fail closed). Every export, payment and download goes through
 * `authenticatedFetch` to the BFF; the page never writes Dataverse rows itself (ADR-002). The only `Xrm.WebApi` use is
 * the read-only display of the selection (`services/invoices.ts`).
 *
 * The Navigator side pane is registered on mount (root CLAUDE.md section 17, docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md).
 *
 * Web resource: sprk_apexport (see the build rename in package.json).
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, Spinner, Text, Button, makeStyles, tokens } from '@fluentui/react-components';
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppErrorBoundary,
  AppInsightsService,
  ensureNavigatorSidePane,
  parseDataParams,
  createXrmNavigationService,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from '@spaarke/auth';
import { App } from './App';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createApExportApi } from './services/apExportApi';
import { parseLaunch } from './services/launch';

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
  // Application Insights at runtime from this environment's connection string (#1537); never awaited, never rejects.
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

/** Close the host navigateTo dialog; the ribbon script then refreshes the grid or form. */
function closeDialog(): void {
  try {
    createXrmNavigationService().closeDialog({ confirmed: true });
  } catch (err) {
    console.warn('[ApExport] The dialog could not be closed:', err);
  }
}

const Loading: React.FC = () => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="apexport-loading">
      <Spinner size="large" label="Loading…" />
    </div>
  );
};

const AuthError: React.FC<{ onRetry: () => void }> = ({ onRetry }) => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="apexport-auth-error">
      <Text weight="semibold" size={500}>
        Unable to load
      </Text>
      <Text>Sign-in could not be verified for this session. Retry, or close this dialog and try again.</Text>
      <Button appearance="primary" onClick={onRetry}>
        Retry
      </Button>
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

  // Global Navigator side pane: every code-page host registers it on mount. Idempotent and never throws.
  React.useEffect(() => {
    ensureNavigatorSidePane();
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    setBootstrapError(null);
    setReady(false);
    bootstrapAuth()
      .then(() => {
        if (!cancelled) setReady(true);
      })
      .catch(err => {
        console.warn('[ApExport] Auth bootstrap failed - page unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createApExportApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);

  let body: React.ReactNode;
  if (bootstrapError) {
    body = <AuthError onRetry={() => setAttempt(n => n + 1)} />;
  } else if (!api) {
    body = <Loading />;
  } else {
    body = <App api={api} launch={launch} onClose={closeDialog} />;
  }

  // The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div className={s.surface}>
        <AppErrorBoundary surfaceName="AP Export">{body}</AppErrorBoundary>
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
  console.error('[ApExport] Root element not found');
}
