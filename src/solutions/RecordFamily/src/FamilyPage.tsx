/**
 * FamilyPage - the RecordFamily code page's view: the validated anchor in, `RecordFamilyModal` (embedded) or the
 * error bar out. Split from `main.tsx` (which only bootstraps auth and mounts) so the wiring that matters -
 * `readFamilyParams` gates the request, the modal is `embedded` inside the platform dialog, and the modal's actions
 * open the push panel - is unit-tested (`recordFamilyPage.test.tsx` in `Spaarke.UI.Components`).
 *
 * The push (field-mapping-server-write-path-r1 task 040; FR-35): the modal's `onAction` hands the ticked rows here, and
 * `RecordFamilyActionPanel` runs preview -> confirm -> apply for them against the record the ribbon was used on. Nothing
 * is pushed on open or by ticking a row; the panel writes only on its confirm button. The form is refreshed by the ribbon
 * when this dialog closes, so no refresh is needed here.
 */
import * as React from "react";
import { FluentProvider, MessageBar, MessageBarBody } from "@fluentui/react-components";
import type { IWizardPageBootstrap } from "@spaarke/ui-components/utils/useWizardPageBootstrap";
import {
  RecordFamilyActionPanel,
  RecordFamilyModal,
  type IFamilySelection,
  type RecordFamilyAction,
} from "@spaarke/ui-components/components/RecordFamilyModal";
import { readFamilyParams } from "./familyParams";

/** The slice of the shared page bootstrap this view reads. */
export type IFamilyPageBootstrap = Pick<
  IWizardPageBootstrap,
  "theme" | "params" | "isAuthReady" | "authenticatedFetch" | "closeDialog"
>;

/** The action the person chose and exactly the rows they ticked; the array identity stays stable until they choose again. */
interface IPendingAction {
  action: RecordFamilyAction;
  rows: IFamilySelection[];
}

export function FamilyPage({ b }: { b: IFamilyPageBootstrap }): React.JSX.Element {
  // Validated: a malformed table name or id never reaches a request URL (see familyParams.ts).
  const anchor = readFamilyParams(b.params);
  const [pending, setPending] = React.useState<IPendingAction | null>(null);
  const handleAction = React.useCallback(
    (action: RecordFamilyAction, rows: IFamilySelection[]) => setPending({ action, rows }),
    []
  );
  const closePanel = React.useCallback(() => setPending(null), []);

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
        onAction={handleAction}
      />
      {pending && (
        <RecordFamilyActionPanel
          open
          onClose={closePanel}
          action={pending.action}
          source={{ table: anchor.entityName, id: anchor.id }}
          rows={pending.rows}
          authenticatedFetch={b.authenticatedFetch}
        />
      )}
    </FluentProvider>
  );
}
