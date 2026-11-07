/**
 * FamilyPage - the RecordFamily code page's view: the validated anchor in, `RecordFamilyModal` (embedded) or the
 * error bar out. Split from `main.tsx` (which only bootstraps auth and mounts) so the wiring that matters -
 * `readFamilyParams` gates the request, and the modal is `embedded` inside the platform dialog - is unit-tested
 * (`recordFamilyPage.test.tsx` in `Spaarke.UI.Components`).
 */
import * as React from "react";
import { FluentProvider, MessageBar, MessageBarBody } from "@fluentui/react-components";
import type { IWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import { RecordFamilyModal } from "@spaarke/ui-components/components/RecordFamilyModal";
import { readFamilyParams } from "./familyParams";

/** The slice of the shared page bootstrap this view reads. */
export type IFamilyPageBootstrap = Pick<
  IWizardPageBootstrap,
  "theme" | "params" | "isAuthReady" | "authenticatedFetch" | "closeDialog"
>;

export function FamilyPage({ b }: { b: IFamilyPageBootstrap }): React.JSX.Element {
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
