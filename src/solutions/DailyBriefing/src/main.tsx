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
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { resolveRuntimeConfig, getAuthProvider } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";
import { App } from "./App";

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
      <App params={params} />
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
