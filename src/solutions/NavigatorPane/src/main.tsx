/**
 * NavigatorPane - React Entry Point
 *
 * Mounts the React application for the webresource pane (ADR-006 Path C —
 * a Vite code page hosted as a `pageType:'webresource'` pane, mirroring
 * CalendarSidePane/EventDetailSidePane; NOT a classic HTML web resource,
 * NOT a per-form handler).
 *
 * This bundle's root component is `SprkSidePaneHost` (task 011) — the
 * reusable, multi-contributor docked side-pane host that owns the right-rail
 * icon strip and theme + `--sprk-ui-scale` resolution for its own
 * FluentProvider. `NavigatorBody` (this bundle's actual UI) is registered as a
 * `sidePaneRegistry` contributor BEFORE render so `SprkSidePaneHost` renders
 * it behind exactly one rail icon (spec FR-01 + FR-13 framework-proof — one
 * entry = one rail icon; NavigatorPane is the framework's first tenant).
 *
 * EMBEDDED (`managePane={false}`): the docked Navigator pane is created by the
 * Path B app-startup bootstrap (`sprk_SidePaneManager`, task 086), which
 * `createPane`s pane id `sprk-navigator` and `navigate`s it to THIS bundle's
 * webresource. So this host must NOT also create a pane — it renders as the
 * pane's CONTENT. `paneId="sprk-navigator"` matches the bootstrap so the
 * contributor receives the correct pane id. (Mounting the host in its default
 * self-hosting mode here spawned a rival empty pane and caused a load/unload
 * loop — task 086 UAT fix.)
 *
 * Note: this is a standalone web resource (not a PCF control), so it uses
 * React 19's `createRoot` (ADR-006 / ADR-021 Code Page rules).
 *
 * @see NavigatorBody.tsx — the registered contributor's content
 * @see src/client/shared/Spaarke.UI.Components/src/components/SidePane/SprkSidePaneHost.tsx
 * @see src/client/shared/Spaarke.UI.Components/src/components/SidePane/sidePaneRegistry.ts
 * @see projects/spaarke-side-pane-navigation-history-r1/tasks/040-navigatorpane-codepage.poml
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { History24Regular } from "@fluentui/react-icons";
import { registerSidePaneContributor, SprkSidePaneHost } from "@spaarke/ui-components";
import { NavigatorBody } from "./NavigatorBody";

// Register the Navigator body as the sole SprkSidePaneHost contributor this
// bundle provides. Registration happens at module-load time (before render),
// matching the sidePaneRegistry doc convention and the "one entry = one rail
// icon" contract (spec FR-01).
registerSidePaneContributor({
  id: "navigator",
  icon: <History24Regular />,
  title: "Navigator",
  order: 0,
  component: async () => ({ default: NavigatorBody }),
});

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  // React 19 createRoot API
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <SprkSidePaneHost managePane={false} paneId="sprk-navigator" paneTitle="Navigator" />
    </React.StrictMode>
  );
} else {
  console.error("[NavigatorPane] Root element not found");
}
