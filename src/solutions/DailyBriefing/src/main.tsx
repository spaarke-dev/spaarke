import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider, Spinner } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { resolveRuntimeConfig, initAuth } from "@spaarke/auth";
import { App } from "./App";

/**
 * DailyBriefing Code Page entry point.
 *
 * Deployed as sprk_dailyupdate web resource. Opened via:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_dailyupdate", data: encodedParams },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   );
 *
 * Supports light/dark/high-contrast themes via resolveCodePageTheme().
 * Bootstraps @spaarke/auth for BFF API calls (AI briefing endpoint).
 */
function Root() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);

  // Listen for theme changes (cross-tab + custom event)
  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Bootstrap @spaarke/auth for BFF API calls (AI briefing)
  React.useEffect(() => {
    let cancelled = false;
    async function initialize(): Promise<void> {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
          tenantId: config.tenantId || undefined,
          proactiveRefresh: false, // Short-lived dialog, no proactive refresh needed
        });
        if (!cancelled) setIsAuthReady(true);
      } catch (err) {
        console.warn("[DailyBriefing] Auth initialization failed — AI briefing will be unavailable:", err);
        // Still render the digest without AI briefing
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  if (!isAuthReady) {
    return (
      <FluentProvider theme={theme} style={{ height: "100%" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
          <Spinner label="Loading daily briefing..." />
        </div>
      </FluentProvider>
    );
  }

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
} else {
  console.error("[DailyBriefing] Root element not found");
}
