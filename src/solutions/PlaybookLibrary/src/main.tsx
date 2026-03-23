/**
 * Playbook Library - Code Page Entry Point
 *
 * Thin wrapper that mounts the PlaybookLibraryShell component inside a
 * Dataverse modal dialog. Parses URL parameters (entityType, entityId,
 * intent), resolves theme, and creates Xrm service adapters.
 *
 * Opened via Xrm.Navigation.navigateTo with pageType: "webresource".
 * Web resource name: sprk_playbooklibrary
 * Display name: Playbook Library
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
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components/utils/codePageTheme";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { PlaybookLibraryShell } from "@spaarke/ui-components/components/PlaybookLibraryShell";
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
        console.error("[PlaybookLibrary] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const handleClose = React.useCallback(() => {
    // Close the Dataverse modal dialog, signaling confirmation
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  // Determine mode and intent from URL params
  const intent = params.intent;
  const mode = intent ? "intent" as const : undefined;

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
      <PlaybookLibraryShell
        dataService={dataService}
        navigationService={navigationService}
        entityType={params.entityType || ""}
        entityId={params.entityId || ""}
        embedded={true}
        onClose={handleClose}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={resolvedBffBaseUrl}
        {...(mode ? { mode, intent } : {})}
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
  console.error("[PlaybookLibrary] Root element not found");
}
