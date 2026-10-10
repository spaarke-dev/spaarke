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
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { resolveRuntimeConfig, getAuthProvider, getTelemetryConnectionString } from "@spaarke/auth";
import { DailyBriefingApp } from "@spaarke/daily-briefing-components/components";
import { browsePlaybooks } from "./browsePlaybooks";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";

/**
 * Bootstrap auth (config + MSAL + tenant ID). Non-blocking — called from
 * inside the component tree so the UI renders immediately.
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

  // "Browse Playbooks": see ./browsePlaybooks.ts (R7 task 095 / FR-18).
  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <AppErrorBoundary surfaceName="Daily Briefing">
        <DailyBriefingApp params={params} onBrowsePlaybooks={browsePlaybooks} />
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
