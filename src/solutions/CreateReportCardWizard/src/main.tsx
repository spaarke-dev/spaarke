import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { CreateReportCardWizard } from "@spaarke/ui-components/components/CreateReportCardWizard";

function App() {
  const b = useWizardPageBootstrap("CreateReportCardWizard");

  if (!b.isAuthReady) {
    return (
      <FluentProvider theme={b.theme} style={{ height: "100%" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
          <span>Initializing...</span>
        </div>
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <CreateReportCardWizard
        open={true}
        onClose={b.closeDialog}
        dataService={b.dataService}
        navigationService={b.navigationService}
        embedded={true}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        initialAssociation={b.initialAssociation}
        lockAssociation={b.lockAssociation}
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
  console.error("[CreateReportCardWizard] Root element not found");
}
