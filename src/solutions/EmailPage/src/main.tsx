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

import * as React from "react";
import { createRoot } from "react-dom/client";
import {
  FluentProvider,
  Spinner,
  Text,
  Button,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppErrorBoundary,
  AppInsightsService,
  createXrmDataService,
  createXrmNavigationService,
  XrmDataverseClient,
  getXrm,
} from "@spaarke/ui-components";
import { resolveRuntimeConfig, getAuthProvider } from "@spaarke/auth";
import { EmailWorkspace, type EmailWorkspaceWebApi } from "@spaarke/communication-components";
import { setRuntimeConfig, getBffBaseUrl } from "./config/runtimeConfig";
import { ensureAuthInitialized, authenticatedFetch } from "./services/authInit";

// ai-spaarke-ai-workspace-UI-r1 brittleness Phase D (2026-06-09) precedent:
// Initialize Application Insights so AppErrorBoundary.componentDidCatch can
// route errors to the "Failures" pane via reportClientError(). Key is sourced
// from a build-time Vite env var; absent in dev → no-op (boundary still logs
// to console). Override: VITE_APP_INSIGHTS_KEY=<key> npm run build
const _appInsightsKey: string = import.meta.env.VITE_APP_INSIGHTS_KEY ?? "";
if (_appInsightsKey) {
  AppInsightsService.initialize(_appInsightsKey);
}

const useStyles = makeStyles({
  centered: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    width: "100%",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    boxSizing: "border-box",
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
      throw new Error(
        "Xrm.WebApi is not available. The Email code page requires a Dataverse-hosted context."
      );
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
EmailPageLoading.displayName = "EmailPageLoading";

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
      <Text>
        Sign-in could not be verified for this session. Retry, or reopen this page from Spaarke navigation.
      </Text>
      <Button appearance="primary" onClick={onRetry}>
        Retry
      </Button>
    </div>
  );
};
EmailPageAuthError.displayName = "EmailPageAuthError";

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
      .catch((err) => {
        console.warn("[EmailPage] Auth bootstrap failed — Email workspace unavailable:", err);
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

  const handleRetry = React.useCallback(() => setAttempt((n) => n + 1), []);

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
      />
    );
  }

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <AppErrorBoundary surfaceName="Email">{body}</AppErrorBoundary>
    </FluentProvider>
  );
}

const rootElement = document.getElementById("root");
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <Root />
    </React.StrictMode>
  );
} else {
  // eslint-disable-next-line no-console
  console.error("[EmailPage] Root element not found");
}
