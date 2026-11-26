import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { mountWizardPage, WizardPageInitializing } from "@spaarke/ui-components/utils/mountWizardPage";
import { withBffChildWrites } from "@spaarke/ui-components/utils/adapters/bffChildWriteAdapter";
import {
  CreateTodoWizard,
  withTodoCreatedBroadcast,
  resolveCurrentUserContact,
} from "@spaarke/ui-components/components/CreateTodoWizard";

// R4 task 100 (W-2) post-create refetch broadcast and the smart-todo-r5 default assignee now live in
// `@spaarke/ui-components` CreateTodoWizard/todoWizardHostSupport.ts, shared with the in-app
// InAppWizardHost (spaarke-ontology-platform-r1 task 112) so the two hosts cannot drift.

function App() {
  const b = useWizardPageBootstrap("CreateTodoWizard");

  // R4 task 100 (W-2) — wrap the dataService so successful sprk_todo creates
  // broadcast on the shared BroadcastChannel for cross-iframe refetch wiring.
  //
  // UAC-r2 task 147 r1c (owner round 28 item 1): the to-do — and every other CHILD record the wizard creates — goes
  // through the BFF (POST /api/v1/child-records/{table}, G5), which decides its owner. The broadcast wraps the BFF-routed
  // service, so it fires after the BFF create. With no base URL (auth init failed) the service is "not connected": a
  // child create is refused with a message, never sent to Xrm.WebApi.
  const { dataService: baseDataService, bffBaseUrl } = b;
  const dataService = React.useMemo(
    () =>
      withTodoCreatedBroadcast(
        withBffChildWrites(baseDataService, b.authenticatedFetch, bffBaseUrl || undefined),
      ),
    [baseDataService, b.authenticatedFetch, bffBaseUrl],
  );

  // UAC-r2 task 147 r1c: a launch from a record (the parent-form "Create To Do" ribbon, and the secure-record subgrid's
  // "New To Do" that replaces the platform "+ New" under a secure record — owner round 28 item 2) passes the record as
  // entityType / entityId / recordName. The To Do opens filed under it (the launch contract's "launch record triple",
  // TodoWizardDialog `initialRegarding`); the user may still change it. Absent → no pre-fill, as before.
  const initialRegarding = React.useMemo(() => {
    const entityType = (b.params.entityType ?? "").trim();
    const entityId = (b.params.entityId ?? "").replace(/[{}]/g, "").toLowerCase();
    return entityType && entityId
      ? { entityType, recordId: entityId, recordName: b.params.recordName ?? "" }
      : undefined;
  }, [b.params]);

  // smart-todo-r5 UAT 2026-08-17 (item #1) — default "Assigned To" to the
  // current user's CONTACT. `sprk_todo.sprk_assignedto` targets the OOB
  // `contact` table, resolved from systemuser via the custom `contact
  // .sprk_systemuser` lookup (same query as `useCurrentContactId`, inlined here
  // because this Code Page intentionally does NOT depend on
  // `@spaarke/smart-todo-components`). Defensive: any failure leaves the assignee blank.
  const [defaultAssignedTo, setDefaultAssignedTo] = React.useState<
    { contactId: string; contactName?: string } | undefined
  >(undefined);
  React.useEffect(() => {
    let cancelled = false;
    void resolveCurrentUserContact(dataService).then((contact) => {
      if (!cancelled && contact) setDefaultAssignedTo(contact);
    });
    return () => {
      cancelled = true;
    };
  }, [dataService]);

  if (!b.isAuthReady) return <WizardPageInitializing theme={b.theme} />;

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <CreateTodoWizard
        open={true}
        dataService={dataService}
        navigationService={b.navigationService}
        emailComposeHandlers={b.emailComposeHandlers}
        initialRegarding={initialRegarding}
        embedded={true}
        onClose={b.closeDialog}
        authenticatedFetch={b.authenticatedFetch}
        bffBaseUrl={b.bffBaseUrl}
        resolveSpeContainerId={b.resolveSpeContainerId}
        defaultAssignedTo={defaultAssignedTo}
      />
    </FluentProvider>
  );
}

mountWizardPage("CreateTodoWizard", App);
