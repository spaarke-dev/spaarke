/**
 * Create Matter Wizard - Code Page Entry Point
 *
 * Thin wrapper that mounts the CreateMatterWizard component inside a
 * Dataverse modal dialog. Parses URL parameters, resolves theme, and
 * creates Xrm service adapters.
 *
 * Opened via Xrm.Navigation.navigateTo with pageType: "webresource".
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
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { CreateMatterWizard } from "@spaarke/ui-components/components/CreateMatterWizard";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const uploadService = React.useMemo(() => createXrmUploadService(params.bffBaseUrl || ""), [params.bffBaseUrl]);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const handleClose = React.useCallback(() => {
    // Close the Dataverse modal dialog, signaling confirmation
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <CreateMatterWizard
        open={true}
        dataService={dataService}
        uploadService={uploadService}
        navigationService={navigationService}
        embedded={true}
        onClose={handleClose}
        authenticatedFetch={fetch.bind(window)}
        bffBaseUrl={params.bffBaseUrl || ""}
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
  console.error("[CreateMatterWizard] Root element not found");
}
