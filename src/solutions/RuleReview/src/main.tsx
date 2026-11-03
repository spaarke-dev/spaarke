/**
 * Rule Review Code Page entry point (ebilling-r1 task 077, FR-28).
 *
 * Mirrors `src/solutions/CommunicationReconciliation/src/main.tsx` (the task's skeleton): `createRoot` + host `FluentProvider` (theme via
 * `resolveCodePageTheme` / `setupCodePageThemeListener`) + `AppErrorBoundary` + an async `bootstrapAuth()` (`resolveRuntimeConfig` ->
 * `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028). The page mounts only after that resolves; if it fails the page shows a retry
 * state (fail closed). Every rule set call goes through `authenticatedFetch` to the BFF; the page never writes Dataverse rows itself
 * (ADR-002). The only `Xrm.WebApi` use is the read-only firm lookup (`services/firmLookup.ts`).
 *
 * The Navigator side pane is registered on mount (root CLAUDE.md section 17, docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md).
 *
 * Web resource: sprk_rulereviewpage (see the build rename in package.json). Deployment is task 079's.
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
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from '@spaarke/auth';
import { App } from './App';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createRuleSetApi } from './services/ruleSetApi';
import { createFirmLookup } from './services/firmLookup';

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

/** Bootstrap auth (config + MSAL + tenant ID); the page waits on this before it mounts (NFR-07). */
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

const RuleReviewLoading: React.FC = () => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="rulereview-loading">
      <Spinner size="large" label="Loading Rule Review..." />
    </div>
  );
};

const RuleReviewAuthError: React.FC<{ onRetry: () => void }> = ({ onRetry }) => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="rulereview-auth-error">
      <Text weight="semibold" size={500}>
        Unable to load Rule Review
      </Text>
      <Text>Sign-in could not be verified for this session. Retry, or reopen this page from Spaarke navigation.</Text>
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
        console.warn('[RuleReview] Auth bootstrap failed - page unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const services = React.useMemo(
    () => (ready ? { api: createRuleSetApi(authenticatedFetch, getBffBaseUrl()), firmLookup: createFirmLookup() } : null),
    [ready]
  );

  let body: React.ReactNode;
  if (bootstrapError) {
    body = <RuleReviewAuthError onRetry={() => setAttempt(n => n + 1)} />;
  } else if (!services) {
    body = <RuleReviewLoading />;
  } else {
    body = <App api={services.api} firmLookup={services.firmLookup} />;
  }

  // The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div className={s.surface}>
        <AppErrorBoundary surfaceName="Rule Review">{body}</AppErrorBoundary>
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
  console.error('[RuleReview] Root element not found');
}
