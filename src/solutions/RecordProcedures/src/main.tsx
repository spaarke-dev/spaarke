/**
 * Record Procedures Code Page entry point (ebilling-r1 task 084a, EB-D-217).
 *
 * Embedded in the Billing tab of the `sprk_matter`, `sprk_project` and `sprk_workassignment` main forms as a web resource control with "Pass
 * record object-type code and unique identifier as parameters", so the URL carries `id` and `typename` (see `services/launch.ts`). Shows the
 * Procedures the record is subject to and the rules in force, for one firm and one work date, from task 084's read routes
 * (`GET /api/finance/{matters|projects|workassignments}/{recordId}/procedures` and `/rules-in-force`). Task 084b: a row opens the Procedure or
 * Rule wizard, whose writes go through task 089's routes (`services/overridesApi.ts`) and nowhere else.
 *
 * Every call to the BFF goes through `authenticatedFetch`; the page never reads or writes the Procedure, override or rule tables itself
 * (ADR-002). The one `Xrm.WebApi` read is the picker's list of firms that have invoiced the record (`services/firmChoices.ts`).
 *
 * Mirrors `src/solutions/InvoiceFiles/src/main.tsx`: `createRoot` + host `FluentProvider` (theme via `resolveCodePageTheme` /
 * `setupCodePageThemeListener`, dark mode included) + `AppErrorBoundary` + an async `bootstrapAuth()` (`resolveRuntimeConfig` ->
 * `setRuntimeConfig` -> `ensureAuthInitialized`, ADR-028). The page mounts only after that resolves; if it fails the page shows a retry state
 * (fail closed). The Navigator side pane is the host form's own concern (the Matter and Project ribbons carry the trigger), so
 * `ensureNavigatorSidePane()` is not called here: this is an embedded section, not a page the user navigates to.
 *
 * Web resource: sprk_recordprocedures (see the build rename in package.json).
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
  getXrm,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from '@spaarke/auth';
import { ProceduresPanel } from './components/ProceduresPanel';
import { setRuntimeConfig, getBffBaseUrl } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';
import { createRecordProceduresApi } from './services/recordProceduresApi';
import { createOverridesApi } from './services/overridesApi';
import { createDryRunApi } from './services/dryRunApi';
import { createFirmChoices, type WebApiLike } from './services/firmChoices';
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

/** `Xrm.WebApi` as the signed-in user, resolved at call time; read methods only. Undefined outside a Dataverse-hosted page. */
function hostWebApi(): WebApiLike | undefined {
  const api = getXrm()?.WebApi;
  if (!api) return undefined;
  return {
    retrieveMultipleRecords: async (entity, options) => {
      const result = await api.retrieveMultipleRecords(entity, options);
      return { entities: result.entities as Record<string, unknown>[] };
    },
  };
}

const firmChoices = createFirmChoices(hostWebApi);

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
        console.warn('[RecordProcedures] Auth bootstrap failed - page unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt, launch]);

  // Built once the runtime config is set (getBffBaseUrl throws before that).
  const api = React.useMemo(() => (ready ? createRecordProceduresApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const overrides = React.useMemo(() => (ready ? createOverridesApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);
  const dryRun = React.useMemo(() => (ready ? createDryRunApi(authenticatedFetch, getBffBaseUrl()) : null), [ready]);

  let body: React.ReactNode;
  if (!launch.ok) {
    body = <Message testId="recordprocedures-launch" title="Procedures" body={launch.message} />;
  } else if (bootstrapError) {
    body = (
      <Message
        testId="recordprocedures-auth-error"
        title="Unable to load"
        body="Sign-in could not be verified for this session. Retry, or reload the form."
        onRetry={() => setAttempt(n => n + 1)}
      />
    );
  } else if (!api || !overrides || !dryRun) {
    body = (
      <div className={s.centered} data-testid="recordprocedures-bootstrapping">
        <Spinner size="small" label="Loading procedures…" />
      </div>
    );
  } else {
    body = <ProceduresPanel core={launch.core} table={launch.table} recordId={launch.recordId} api={api} overrides={overrides} dryRun={dryRun} firmChoices={firmChoices} />;
  }

  // The FluentProvider carries only the theme; the surface colours sit on a wrapper div (project gotcha F-10).
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div className={s.surface}>
        <AppErrorBoundary surfaceName="Record Procedures">{body}</AppErrorBoundary>
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
  console.error('[RecordProcedures] Root element not found');
}
