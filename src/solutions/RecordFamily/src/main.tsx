/**
 * RecordFamily code page — hosts RecordFamilyModal as a Dataverse `navigateTo` dialog.
 *
 * Launched by the Matter / Project "Push Updates" ribbon button
 * (`Spaarke.FieldMapping.Push.pushUpdates` in `sprk_fieldmapping_push.js`) with the dialog data
 * `entityName=<logical name>&id=<record id>` (field-mapping-server-write-path-r1 task 075; FR-35).
 * A ribbon script has no React host, so `navigateTo` is the sanctioned launch (ADR-050); this page
 * is the minimal host that gives the modal its auth and theme.
 *
 * No `onAction` is passed yet: the modal then renders both actions disabled ("Available after the
 * push update") and is a read-only Family view. Task 040 wires the actions.
 *
 * Bootstrap reuses the shared `useWizardPageBootstrap` (ADR-028: `@spaarke/auth` →
 * `authenticatedFetch`; no token handling here). Reference page: `src/solutions/CreateReportCardWizard`.
 *
 * Not a Navigator host: `ensureNavigatorSidePane()` is deliberately NOT called — see the task notes.
 */
import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider, MessageBar, MessageBarBody } from "@fluentui/react-components";
import { useWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { RecordFamilyModal } from "@spaarke/ui-components/components/RecordFamilyModal";

function App() {
  const b = useWizardPageBootstrap("RecordFamily");

  const entityName = b.params.entityName ?? "";
  const id = b.params.id ?? "";

  if (!b.isAuthReady) {
    return (
      <FluentProvider theme={b.theme} style={{ height: "100%" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
          <span>Initializing...</span>
        </div>
      </FluentProvider>
    );
  }

  if (!entityName || !id) {
    return (
      <FluentProvider theme={b.theme} style={{ height: "100%" }}>
        <MessageBar intent="error">
          <MessageBarBody>The family could not be opened. Close this window and try again.</MessageBarBody>
        </MessageBar>
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={b.theme} style={{ height: "100%" }}>
      <RecordFamilyModal
        open={true}
        onClose={b.closeDialog}
        table={entityName}
        recordId={id}
        authenticatedFetch={b.authenticatedFetch}
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
  console.error("[RecordFamily] Root element not found");
}
