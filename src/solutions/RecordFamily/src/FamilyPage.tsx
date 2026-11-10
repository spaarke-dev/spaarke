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
  /** One per choice: the panel's `key`, so each choice opens a fresh panel and never shows the previous result. */
  seq: number;
}

export function FamilyPage({ b }: { b: IFamilyPageBootstrap }): React.JSX.Element {
  // Validated: a malformed table name or id never reaches a request URL (see familyParams.ts).
  const anchor = readFamilyParams(b.params);
  // The last action chosen. It stays set after the panel closes: the panel is closed with `open={false}`, never by
  // unmounting it while open. A Fluent Dialog unmounted while open leaves tabster's aria-hidden on the rest of the page,
  // so the whole Family would stay hidden from assistive technology after any push (#1906).
  const [pending, setPending] = React.useState<IPendingAction | null>(null);
  const [panelOpen, setPanelOpen] = React.useState(false);
  const closePanel = React.useCallback(() => setPanelOpen(false), []);

  // Escape closes the platform dialog only while focus is inside the embedded modal (its handler sits on the region
  // root). When the panel's Dialog closes, focus could be left on <body>; so remember what had focus when the action
  // was chosen (the action button) and hand focus back when the panel closes, falling back to the region root.
  const providerRef = React.useRef<HTMLDivElement>(null);
  const opener = React.useRef<HTMLElement | null>(null);
  const handleAction = React.useCallback((action: RecordFamilyAction, rows: IFamilySelection[]) => {
    const active = document.activeElement;
    opener.current = active instanceof HTMLElement && active !== document.body ? active : null;
    setPending(prev => ({ action, rows, seq: (prev?.seq ?? 0) + 1 }));
    setPanelOpen(true);
  }, []);
  const wasPanelOpen = React.useRef(false);
  React.useEffect(() => {
    if (panelOpen) {
      wasPanelOpen.current = true;
      return;
    }
    if (!wasPanelOpen.current) return;
    wasPanelOpen.current = false;
    const target = opener.current;
    opener.current = null;
    const root = providerRef.current?.querySelector<HTMLElement>('[role="region"]');
    if (target && target.isConnected && !target.hasAttribute("disabled")) target.focus();
    else root?.focus();
  }, [panelOpen]);

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
    <FluentProvider ref={providerRef} theme={b.theme} style={{ height: "100%" }}>
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
          key={pending.seq}
          open={panelOpen}
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
