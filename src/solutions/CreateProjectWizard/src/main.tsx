import * as React from "react";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { mountWizardPage, WizardPageInitializing } from "@spaarke/ui-components/utils/mountWizardPage";
import { launchAssociationFromParams } from "@spaarke/ui-components/utils/launchAssociation";
import { CreateProjectWizard, mapProjectHandoffSeed } from "@spaarke/ui-components/components/CreateProjectWizard";
import { FluentProvider } from "@fluentui/react-components";

function App() {
  const b = useWizardPageBootstrap("CreateProjectWizard");

  // Assistant → surface hand-off pre-seed: map the drafted seed onto the wizard's initial form values.
  // `undefined` when opened directly (no `handoffId`) — the wizard opens empty.
  const initialFormValues = React.useMemo(() => mapProjectHandoffSeed(b.handoffSeed), [b.handoffSeed]);

  // The create-flow file leg: pass the hand-off's session file references so the wizard fetches the
  // drafted-from document(s) and attaches them to the new project. `undefined` when there is no session file.
  const initialFileRefs = React.useMemo(() => {
    const seed = b.handoffSeed;
    if (!seed || !seed.sessionId || seed.fileIds.length === 0) return undefined;
    return { sessionId: seed.sessionId, fileIds: seed.fileIds, fileNames: seed.fileNames };
  }, [b.handoffSeed]);

  // Task 195 (#2352): opened from a matter's form command or its "New" on the Project <-> Matter subgrid
  // (sprk_wizard_commands), the launch envelope names that matter — pre-select it and lock the Associate-To step.
  // Only the matter table is taken; any other launch record, or none, leaves the step as it was.
  const { initialAssociation, lockAssociation } = React.useMemo(
    () => launchAssociationFromParams(b.params, "sprk_matter"),
    [b.params]
  );

  if (!b.isAuthReady) return <WizardPageInitializing theme={b.theme} />;

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <CreateProjectWizard
        open={true}
        dataService={b.dataService}
        uploadService={b.uploadService}
        navigationService={b.navigationService}
        emailComposeHandlers={b.emailComposeHandlers}
        embedded={true}
        onClose={b.closeDialog}
        onComplete={b.completeHandoff}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        resolveSpeContainerId={b.resolveSpeContainerId}
        tenantId={b.tenantId}
        initialFormValues={initialFormValues}
        initialFileRefs={initialFileRefs}
        initialAssociation={initialAssociation}
        lockAssociation={lockAssociation}
      />
    </FluentProvider>
  );
}

mountWizardPage("CreateProjectWizard", App);
