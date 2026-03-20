import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components/utils/codePageTheme";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { CreateProjectWizard } from "@spaarke/ui-components/components/CreateProjectWizard";

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
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <CreateProjectWizard
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

const rootElement = document.getElementById("root");
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}
