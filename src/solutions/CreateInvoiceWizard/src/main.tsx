import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { CreateInvoiceWizard } from "@spaarke/ui-components/components/CreateInvoiceWizard";

function App() {
  const b = useWizardPageBootstrap("CreateInvoiceWizard");

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
      <CreateInvoiceWizard
        open={true}
        dataService={b.dataService}
        navigationService={b.navigationService}
        embedded={true}
        onClose={b.closeDialog}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        resolveSpeContainerId={b.resolveSpeContainerId}
        tenantId={b.tenantId}
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
  console.error("[CreateInvoiceWizard] Root element not found");
}
