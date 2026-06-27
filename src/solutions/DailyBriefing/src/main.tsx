/**
 * DailyBriefing Code Page entry point.
 *
 * Renders immediately — App component polls for Xrm availability reactively.
 * Auth bootstrap runs async inside Root — non-blocking for initial render.
 * This ensures instant display on MDA welcome screen and left nav.
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppErrorBoundary,
  AppInsightsService,
} from "@spaarke/ui-components";

// ai-spaarke-ai-workspace-UI-r1 brittleness Phase D (2026-06-09):
// Initialize Application Insights so AppErrorBoundary.componentDidCatch can
// route errors to the "Failures" pane via reportClientError(). Key is sourced
// from a build-time Vite env var; absent in dev → no-op (boundary still logs
// to console). Override: VITE_APP_INSIGHTS_KEY=<key> npm run build
const _appInsightsKey: string = import.meta.env.VITE_APP_INSIGHTS_KEY ?? "";
if (_appInsightsKey) {
  AppInsightsService.initialize(_appInsightsKey);
}
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { resolveRuntimeConfig, getAuthProvider } from "@spaarke/auth";
import { DailyBriefingApp } from "@spaarke/daily-briefing-components/components";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";

/**
 * Bootstrap auth (config + MSAL + tenant ID). Non-blocking — called from
 * inside the component tree so the UI renders immediately.
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

function Root() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Bootstrap auth async — non-blocking, digest works without it (no AI briefing)
  React.useEffect(() => {
    bootstrapAuth().catch((err) =>
      console.warn("[DailyBriefing] Auth bootstrap failed — AI briefing unavailable:", err)
    );
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <AppErrorBoundary surfaceName="Daily Briefing">
        <DailyBriefingApp params={params} />
      </AppErrorBoundary>
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
}
