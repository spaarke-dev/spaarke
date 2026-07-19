import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components/utils/themeStorage";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { CreateProjectWizard, mapProjectHandoffSeed } from "@spaarke/ui-components/components/CreateProjectWizard";
import { EntityCreationService, type IUserBuCascadeDefaults } from "@spaarke/ui-components/services";
import { readHandoffFromUrl, handoffSeed as computeHandoffSeed, completeHandoff as writeCompleteHandoff } from "@spaarke/ui-components/services/surfaceHandoff";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>(params.bffBaseUrl || "");
  const [resolvedTenantId, setResolvedTenantId] = React.useState<string>("");

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
          setResolvedTenantId(config.tenantId ?? "");
          setIsAuthReady(true);
        }
      } catch (err) {
        console.error("[CreateProjectWizard] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const uploadService = React.useMemo(() => createXrmUploadService(resolvedBffBaseUrl), [resolvedBffBaseUrl]);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  // Assistant → surface hand-off pre-seed (spaarkeai-assistant-enhancements-r1 UAT #1):
  // when this wizard was launched from the Assistant's surface_launch create flow
  // (`launchSurface`), read this page's own hand-off envelope from the launch URL and
  // map the drafted seed onto the wizard's initial form values. `undefined` when opened
  // directly (no `handoffId`) — the wizard opens empty. Read once (URL static per mount).
  const handoff = React.useMemo(() => readHandoffFromUrl(), []);
  const initialFormValues = React.useMemo(
    () => mapProjectHandoffSeed(computeHandoffSeed(handoff)),
    [handoff]
  );

  // UAT #1 (the create-flow file leg): pass the hand-off's session file references so
  // the wizard fetches the drafted-from document(s) and attaches them to the new
  // project. `undefined` when there's no session file to carry — the files step stays empty.
  const initialFileRefs = React.useMemo(() => {
    const seed = computeHandoffSeed(handoff);
    if (!seed || !seed.sessionId || seed.fileIds.length === 0) return undefined;
    return { sessionId: seed.sessionId, fileIds: seed.fileIds, fileNames: seed.fileNames };
  }, [handoff]);

  const handleClose = React.useCallback(() => {
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  // UAT #1 honest-ack: on a SUCCESSFUL create, write the committed SurfaceHandoffResult
  // (record id) for the active hand-off, then close. No-op (just closes) when opened
  // directly. Cancellation is INFERRED from the absence of a written result, so the
  // cancel path (handleClose) intentionally writes nothing.
  const handleComplete = React.useCallback((recordId?: string) => {
    if (handoff?.handoffId) writeCompleteHandoff(handoff.handoffId, recordId);
    navigationService.closeDialog({ confirmed: true });
  }, [handoff, navigationService]);

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

  const resolveUserBuDefaults = React.useCallback(async (): Promise<IUserBuCascadeDefaults> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    return await EntityCreationService.resolveUserBuDefaults(xrm.WebApi, userId);
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
      <CreateProjectWizard
        open={true}
        dataService={dataService}
        uploadService={uploadService}
        navigationService={navigationService}
        embedded={true}
        onClose={handleClose}
        onComplete={handleComplete}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={resolvedBffBaseUrl}
        resolveSpeContainerId={resolveSpeContainerId}
        resolveUserBuDefaults={resolveUserBuDefaults}
        tenantId={resolvedTenantId}
        initialFormValues={initialFormValues}
        initialFileRefs={initialFileRefs}
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
  console.error("[CreateProjectWizard] Root element not found");
}
