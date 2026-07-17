import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { CreateEventWizard, mapEventHandoffSeed } from "@spaarke/ui-components/components/CreateEventWizard";

function App() {
  const b = useWizardPageBootstrap("CreateEventWizard");

  // Assistant → surface hand-off pre-seed (spaarkeai-assistant-enhancements-r1
  // task 013): map the bootstrap's hand-off seed onto the wizard's initial form
  // values so a `create-task` launch opens PRE-FILLED. `undefined` when opened
  // directly (no hand-off) — the wizard opens empty as before.
  const initialFormValues = React.useMemo(() => mapEventHandoffSeed(b.handoffSeed), [b.handoffSeed]);

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
      <CreateEventWizard
        open={true}
        dataService={b.dataService}
        uploadService={b.uploadService}
        navigationService={b.navigationService}
        embedded={true}
        onClose={b.closeDialog}
        onComplete={b.completeHandoff}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        resolveSpeContainerId={b.resolveSpeContainerId}
        tenantId={b.tenantId}
        initialAssociation={b.initialAssociation}
        lockAssociation={b.lockAssociation}
        initialFormValues={initialFormValues}
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
  console.error("[CreateEventWizard] Root element not found");
}
