import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import { getXrm } from "@spaarke/ui-components/utils/xrmContext";
import { launchAssociationFromParams } from "@spaarke/ui-components/utils/launchAssociation";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { mountWizardPage, WizardPageInitializing } from "@spaarke/ui-components/utils/mountWizardPage";
import { CreateMatterWizard, mapMatterHandoffSeed } from "@spaarke/ui-components/components/CreateMatterWizard";
import { EntityCreationService, type IUserBuCascadeDefaults } from "@spaarke/ui-components/services/EntityCreationService";

function App() {
  const b = useWizardPageBootstrap("CreateMatterWizard");

  // Assistant → surface hand-off pre-seed (spaarkeai-assistant-enhancements-r1 task 013): map the drafted
  // seed onto the wizard's initial form values. `undefined` when opened directly — the wizard opens empty.
  const initialFormValues = React.useMemo(() => mapMatterHandoffSeed(b.handoffSeed), [b.handoffSeed]);

  // UAT W-2/W-5 (the create-flow file leg): pass the hand-off's session file references so the wizard
  // fetches the drafted-from document(s) and attaches them to the new matter. `undefined` when there is none.
  const initialFileRefs = React.useMemo(() => {
    const seed = b.handoffSeed;
    if (!seed || !seed.sessionId || seed.fileIds.length === 0) return undefined;
    return { sessionId: seed.sessionId, fileIds: seed.fileIds, fileNames: seed.fileNames };
  }, [b.handoffSeed]);

  // Task 195 (#2352): opened from a project's form command or its "New" on the Project <-> Matter subgrid
  // (sprk_wizard_commands), the launch envelope names that project — pre-select it and lock the Associate-To step.
  // Only the project table is taken; any other launch record, or none, leaves the step as it was.
  const { initialAssociation, lockAssociation } = React.useMemo(
    () => launchAssociationFromParams(b.params, "sprk_project"),
    [b.params]
  );

  // FR-WIZ-01 (spaarke-multi-container-multi-index-r1): resolve BU cascade defaults
  // (sprk_containerid + sprk_searchindexname) from the current user's owning BU. Since task 021 the wizard passes
  // them to MatterService but nothing reads them (the matter's index and container columns are the server's,
  // FR-38/FR-40); the callback stays as the host's contract.
  const resolveUserBuDefaults = React.useCallback(async (): Promise<IUserBuCascadeDefaults> => {
    // Shared cross-frame walker (task 081 / C-8).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = getXrm(['webApi', 'utility']);
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    return await EntityCreationService.resolveUserBuDefaults(xrm.WebApi, userId);
  }, []);

  if (!b.isAuthReady) return <WizardPageInitializing theme={b.theme} />;

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <CreateMatterWizard
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
        resolveUserBuDefaults={resolveUserBuDefaults}
        tenantId={b.tenantId}
        initialFormValues={initialFormValues}
        initialFileRefs={initialFileRefs}
        initialAssociation={initialAssociation}
        lockAssociation={lockAssociation}
      />
    </FluentProvider>
  );
}

mountWizardPage("CreateMatterWizard", App);
