import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import { getXrm } from "@spaarke/ui-components/utils/xrmContext";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { mountWizardPage, WizardPageInitializing } from "@spaarke/ui-components/utils/mountWizardPage";
import { WorkAssignmentWizardDialog } from "@spaarke/ui-components/components/CreateWorkAssignmentWizard";

function App() {
  const b = useWizardPageBootstrap("CreateWorkAssignmentWizard");

  // UAT R5-8 (the create-flow file leg): when launched from the Assistant "Assign Work" Quick Start
  // card via the surface-launch envelope, pass the hand-off's session file refs so the wizard fetches +
  // pre-attaches them. `undefined` on a direct open (no handoffId on the URL).
  const initialFileRefs = React.useMemo(() => {
    const seed = b.handoffSeed;
    if (!seed || !seed.sessionId || seed.fileIds.length === 0) return undefined;
    return { sessionId: seed.sessionId, fileIds: seed.fileIds, fileNames: seed.fileNames };
  }, [b.handoffSeed]);

  // Resolve SPE container ID from the user's business unit. Kept per page (not the bootstrap's): this
  // wizard fails loudly when the business unit has no container, where the shared one returns ''.
  const resolveSpeContainerId = React.useCallback(async (): Promise<string> => {
    // Shared cross-frame walker (task 081 / C-8).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = getXrm(['webApi', 'utility']);
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    const user = await xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=_businessunitid_value");
    const buId = user["_businessunitid_value"] as string;
    if (!buId) throw new Error("Could not resolve business unit");
    const bu = await xrm.WebApi.retrieveRecord("businessunit", buId, "?$select=sprk_containerid");
    const containerId = bu["sprk_containerid"] as string;
    if (!containerId) throw new Error("Business unit has no SPE container configured");
    return containerId;
  }, []);

  if (!b.isAuthReady) return <WizardPageInitializing theme={b.theme} />;

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <WorkAssignmentWizardDialog
        dataService={b.dataService}
        navigationService={b.navigationService}
        emailComposeHandlers={b.emailComposeHandlers}
        embedded={true}
        open={true}
        onClose={b.closeDialog}
        // #1420 honest-ack: on a SUCCESSFUL create, write the committed hand-off result, then close.
        onComplete={b.completeHandoff}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        resolveSpeContainerId={resolveSpeContainerId}
        tenantId={b.tenantId}
        initialFileRefs={initialFileRefs}
      />
    </FluentProvider>
  );
}

mountWizardPage("CreateWorkAssignmentWizard", App);
