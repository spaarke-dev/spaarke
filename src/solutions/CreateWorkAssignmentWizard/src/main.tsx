import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { WorkAssignmentWizardDialog } from "@spaarke/ui-components/components/CreateWorkAssignmentWizard";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>(params.bffBaseUrl || "");

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

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
          proactiveRefresh: true,
        });
        if (!cancelled) {
          setResolvedBffBaseUrl(config.bffBaseUrl);
          setIsAuthReady(true);
        }
      } catch (err) {
        console.error("[CreateWorkAssignmentWizard] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  // Resolve SPE container ID from the user's business unit
  const resolveSpeContainerId = React.useCallback(async (): Promise<string> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    const user = await xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=_businessunitid_value");
    const buId = user["_businessunitid_value"] as string;
    if (!buId) throw new Error("Could not resolve business unit");
    const bu = await xrm.WebApi.retrieveRecord("businessunit", buId, "?$select=sprk_containerid");
    const containerId = bu["sprk_containerid"] as string;
    if (!containerId) throw new Error("Business unit has no SPE container configured");
    return containerId;
  }, []);

  const handleClose = React.useCallback(() => {
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  if (!isAuthReady) {
    return (
      <FluentProvider theme={theme} style={{ height: "100%" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
          <span>Initializing...</span>
        </div>
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <WorkAssignmentWizardDialog
        dataService={dataService}
        navigationService={navigationService}
        embedded={true}
        open={true}
        onClose={handleClose}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={resolvedBffBaseUrl}
        resolveSpeContainerId={resolveSpeContainerId}
      />
    </FluentProvider>
  );
}

const rootElement = document.getElementById("root");
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[CreateWorkAssignmentWizard] Root element not found");
}
