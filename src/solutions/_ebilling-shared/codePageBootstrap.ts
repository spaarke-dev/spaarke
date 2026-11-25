/**
 * The auth + theme bootstrap the e-billing code pages share (#2965): theme, the optional Navigator side pane, and the fail-closed auth
 * bootstrap with its retry.
 *
 * Auth stays the page's own contract (ADR-028): the page passes ITS `setRuntimeConfig` (`config/runtimeConfig.ts`) and ITS
 * `ensureAuthInitialized` (`services/authInit.ts`, a `createCodePageAuthInitializer`), so the singleton, the `proactiveRefresh` choice and
 * the `logLabel` remain per page. This module only sequences them, as every page did: resolve the runtime config -> start Application
 * Insights -> `setRuntimeConfig` -> `ensureAuthInitialized` (`initAuth`) -> fall back to the signed-in user's tenant id. The page mounts its
 * content only after `ready`; when the bootstrap fails the page shows its retry state (fail closed), never an unauthenticated page.
 *
 * Why not `useWizardPageBootstrap`: it builds the wizard's Xrm data / upload / compose services and DEGRADES to "ready" when auth fails
 * (the wizard mounts and its calls 401). These pages must not mount without auth, and they need none of those services.
 */

import * as React from 'react';
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppInsightsService,
  ensureNavigatorSidePane,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString, type IRuntimeConfig } from '@spaarke/auth';

/** The two page-owned auth functions the bootstrap sequences. */
export interface CodePageAuthDeps {
  setRuntimeConfig: (config: IRuntimeConfig) => void;
  ensureAuthInitialized: () => Promise<void>;
}

/** Bootstrap auth (config + MSAL + tenant ID); the page waits on this before it mounts. */
export async function bootstrapAuth({ setRuntimeConfig, ensureAuthInitialized }: CodePageAuthDeps): Promise<void> {
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

export interface CodePageBootstrapOptions extends CodePageAuthDeps {
  /** The page name in console diagnostics, e.g. `"InvoiceFiles"`. */
  label: string;
  /**
   * False when the page needs no auth (an unsaved or foreign record: the page only says so). The bootstrap then does not start and `ready`
   * stays false. Default true.
   */
  enabled?: boolean;
  /** Register the global Navigator side pane on mount (a page the user navigates to). Not for a section embedded in a form. Default false. */
  registerNavigator?: boolean;
}

export interface CodePageBootstrap {
  /** The resolved Fluent theme (light/dark) for the page's FluentProvider; follows the host. */
  theme: ReturnType<typeof resolveCodePageTheme>;
  /** True once auth is initialised; build the page's API clients then (the BFF base URL is not set before). */
  ready: boolean;
  /** The reason the bootstrap failed, or null. Show the retry state while it is non-null. */
  bootstrapError: unknown;
  /** Run the bootstrap again (the retry button). */
  retry: () => void;
}

export function useCodePageBootstrap({
  label,
  enabled = true,
  registerNavigator = false,
  setRuntimeConfig,
  ensureAuthInitialized,
}: CodePageBootstrapOptions): CodePageBootstrap {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const [ready, setReady] = React.useState(false);
  const [bootstrapError, setBootstrapError] = React.useState<unknown>(null);
  const [attempt, setAttempt] = React.useState(0);

  // The page's two auth functions are read through a ref (latest value), so a caller that passes inline lambdas does not re-run the
  // bootstrap on every render: it re-runs only for a retry, or when `enabled` or `label` change.
  const authDeps = React.useRef<CodePageAuthDeps>({ setRuntimeConfig, ensureAuthInitialized });
  authDeps.current = { setRuntimeConfig, ensureAuthInitialized };

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Global Navigator side pane: a page the user navigates to registers it on mount. Idempotent and never throws.
  React.useEffect(() => {
    if (registerNavigator) ensureNavigatorSidePane();
  }, [registerNavigator]);

  React.useEffect(() => {
    if (!enabled) return undefined;
    let cancelled = false;
    setBootstrapError(null);
    setReady(false);
    bootstrapAuth({
      setRuntimeConfig: config => authDeps.current.setRuntimeConfig(config),
      ensureAuthInitialized: () => authDeps.current.ensureAuthInitialized(),
    })
      .then(() => {
        if (!cancelled) setReady(true);
      })
      .catch(err => {
        console.warn(`[${label}] Auth bootstrap failed - page unavailable:`, err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt, enabled, label]);

  const retry = React.useCallback(() => setAttempt(n => n + 1), []);

  return { theme, ready, bootstrapError, retry };
}
