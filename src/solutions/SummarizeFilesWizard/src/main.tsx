/**
 * Summarize Files Wizard - Code Page Entry Point
 *
 * Thin wrapper that mounts the SummarizeFilesDialog component inside a
 * Dataverse modal dialog. Parses URL parameters, resolves theme, and
 * creates Xrm service adapters.
 *
 * Opened via Xrm.Navigation.navigateTo with pageType: "webresource".
 * Web resource name: sprk_summarizefileswizard
 *
 * Note: This is a standalone web resource (not a PCF control), so it uses
 * React 18 which includes native useId() support required by Fluent UI v9.
 *
 * Uses deep imports from @spaarke/ui-components to avoid pulling in
 * Lexical/RichTextEditor and other heavy dependencies.
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { SummarizeFilesDialog } from "@spaarke/ui-components/components/SummarizeFilesWizard";
import { readHandoffFromUrl, handoffSeed as computeHandoffSeed } from "@spaarke/ui-components/services/surfaceHandoff";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>(params.bffBaseUrl || "");

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Initialize auth from Dataverse environment variables
  React.useEffect(() => {
    let cancelled = false;

    async function initializeAuth(): Promise<void> {
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
        console.error("[SummarizeFilesWizard] Failed to initialize auth:", err);
        // Still render with URL param bffBaseUrl as fallback, but auth won't work
        if (!cancelled) {
          setIsAuthReady(true);
        }
      }
    }

    void initializeAuth();
    return () => { cancelled = true; };
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  // UAT R5-8 (the create-flow file leg): when launched from the Assistant "Summarize Files" Quick
  // Start card via the surface-launch envelope, read the hand-off and pass the session file refs so
  // the dialog fetches + pre-seeds them. `undefined` on a direct open (no handoffId on the URL).
  const initialFileRefs = React.useMemo(() => {
    const seed = computeHandoffSeed(readHandoffFromUrl());
    if (!seed || !seed.sessionId || seed.fileIds.length === 0) return undefined;
    return { sessionId: seed.sessionId, fileIds: seed.fileIds, fileNames: seed.fileNames };
  }, []);

  const handleClose = React.useCallback(() => {
    // Close the Dataverse modal dialog, signaling confirmation
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
      <SummarizeFilesDialog
        open={true}
        onClose={handleClose}
        dataService={dataService}
        navigationService={navigationService}
        embedded={true}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={resolvedBffBaseUrl}
        initialFileRefs={initialFileRefs}
      />
    </FluentProvider>
  );
}

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  // React 18 createRoot API
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[SummarizeFilesWizard] Root element not found");
}
