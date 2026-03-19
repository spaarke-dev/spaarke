/**
 * Find Similar Documents Dialog - Code Page Entry Point
 *
 * Thin wrapper that mounts the FindSimilarDialog component inside a
 * Dataverse modal dialog. Parses URL parameters (documentId, containerId),
 * resolves theme, and creates Xrm service adapters.
 *
 * Opened via Xrm.Navigation.navigateTo with pageType: "webresource".
 * Web resource name: sprk_findsimilar
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
import { FindSimilarDialog } from "@spaarke/ui-components/components/FindSimilarDialog";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const handleClose = React.useCallback(() => {
    // Close the Dataverse modal dialog
    navigationService.closeDialog({ confirmed: false });
  }, [navigationService]);

  // Build the iframe URL from params passed via Xrm.Navigation.navigateTo data string
  const iframeUrl = React.useMemo(() => {
    const documentId = params.documentId;
    const containerId = params.containerId;
    if (!documentId || !containerId) return null;

    const urlParams = new URLSearchParams();
    urlParams.set("documentId", documentId);
    urlParams.set("containerId", containerId);
    if (params.bffBaseUrl) urlParams.set("bffBaseUrl", params.bffBaseUrl);

    // The iframe loads the DocumentRelationshipViewer web resource
    return `sprk_documentrelationshipviewer?data=${encodeURIComponent(urlParams.toString())}`;
  }, [params.documentId, params.containerId, params.bffBaseUrl]);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <FindSimilarDialog
        open={true}
        onClose={handleClose}
        url={iframeUrl}
        embedded={true}
        dataService={dataService}
        bffBaseUrl={params.bffBaseUrl || ""}
        authenticatedFetch={fetch.bind(window)}
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
  console.error("[FindSimilarDialog] Root element not found");
}
