import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { mountWizardPage, WizardPageInitializing } from "@spaarke/ui-components/utils/mountWizardPage";
import { CreateReportCardWizard } from "@spaarke/ui-components/components/CreateReportCardWizard";

function App() {
  const b = useWizardPageBootstrap("CreateReportCardWizard");

  if (!b.isAuthReady) return <WizardPageInitializing theme={b.theme} />;

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <CreateReportCardWizard
        open={true}
        onClose={b.closeDialog}
        dataService={b.dataService}
        navigationService={b.navigationService}
        emailComposeHandlers={b.emailComposeHandlers}
        embedded={true}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        initialAssociation={b.initialAssociation}
        lockAssociation={b.lockAssociation}
      />
    </FluentProvider>
  );
}

mountWizardPage("CreateReportCardWizard", App);
