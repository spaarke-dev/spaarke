import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { CreateMatterWizard } from "@spaarke/ui-components/components/CreateMatterWizard";
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
        console.error("[CreateMatterWizard] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const uploadService = React.useMemo(() => createXrmUploadService(resolvedBffBaseUrl), [resolvedBffBaseUrl]);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const handleClose = React.useCallback(() => {
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  const resolveSpeContainerId = React.useCallback(async (): Promise<string> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    const user = await xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=_businessunitid_value");
    const buId = user["_businessunitid_value"] as string;
    if (!buId) throw new Error("Could not resolve business unit");
    const bu = await xrm.WebApi.retrieveRecord("businessunit", buId, "?$select=sprk_containerid");
    return (bu["sprk_containerid"] as string) || "";
  }, []);

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
      <CreateMatterWizard
        open={true}
        dataService={dataService}
        uploadService={uploadService}
        navigationService={navigationService}
        embedded={true}
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
  console.error("[CreateMatterWizard] Root element not found");
}
