/**
 * RecordFamily code page — hosts RecordFamilyModal as a Dataverse `navigateTo` dialog.
 *
 * Launched by the Matter / Project "Push Updates" ribbon button
 * (`Spaarke.FieldMapping.Push.pushUpdates` in `sprk_fieldmapping_push.js`) with the dialog data
 * `entityName=<logical name>&id=<record id>` (field-mapping-server-write-path-r1 task 075; FR-35).
 * A ribbon script has no React host, so `navigateTo` is the sanctioned launch (ADR-050); this page
 * is the minimal host that gives the modal its auth and theme.
 *
 * The modal is rendered `embedded`: the platform dialog already owns the title bar and the close button, so a
 * second Fluent Dialog inside it would be a modal in a modal (ADR-050). `onClose` closes the platform dialog the
 * way the sibling dialog pages do (`useWizardPageBootstrap().closeDialog`).
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
import { readFamilyParams } from "./familyParams";

function App() {
  const b = useWizardPageBootstrap("RecordFamily");

  // Validated: a malformed table name or id never reaches a request URL (see familyParams.ts).
  const anchor = readFamilyParams(b.params);

  if (!b.isAuthReady) {
    return (
      <FluentProvider theme={b.theme} style={{ height: "100%" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
          <span>Initializing...</span>
        </div>
      </FluentProvider>
    );
  }

  if (!anchor) {
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
        table={anchor.entityName}
        recordId={anchor.id}
        authenticatedFetch={b.authenticatedFetch}
        embedded
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
