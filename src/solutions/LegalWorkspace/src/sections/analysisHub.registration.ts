/**
 * analysisHub.registration.ts — SectionRegistration for the "Analysis" workspace section.
 *
 * ai-advanced-capabilities-analysis-hub-r1 (front-door wiring, 2026-07-29): the
 * Analysis platform home/launcher surface. Mirrors the Pattern D dual-use shape of
 * `communications.registration.ts` / `email.registration.ts` — the SAME shared
 * `AnalysisHubWidget` (`@spaarke/ai-widgets`) that is registered as the Direct
 * workspace widget `analysis-hub` in
 * `@spaarke/ai-widgets/widgets/workspace/register-workspace-widgets.ts` is rendered
 * here as a `ContentSectionConfig` section. This is what surfaces "Analysis" as a
 * system workspace in Manage Workspaces (the dropdown lists `sprk_workspacelayout`
 * rows → selection dispatches `widgetType:'workspace'` → embedded `LegalWorkspaceApp`
 * → this section factory).
 *
 * Provider coupling note (why this is safe):
 *   Unlike the canonical Pattern D widgets (Calendar/Email — host-agnostic, Xrm.WebApi
 *   only), `AnalysisHubWidget` consumes the SpaarkeAi PaneEventBus + AI-session
 *   contexts. When this section mounts inside the SpaarkeAi shell, the embedded
 *   `LegalWorkspaceApp` renders BELOW `PaneEventBusProvider` + `AiSessionProvider`
 *   (see `SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — `centerPane={<WorkspacePane/>}`
 *   is nested inside both providers), so the widget is fully functional: cards launch
 *   the create wizard, the grid lists `sprk_analysis`, and row-reopen switches the
 *   Assistant session. In a bus-less host (standalone LegalWorkspace / MDA / dev) the
 *   widget reads both contexts OPTIONALLY (see `AnalysisHubWidget`'s
 *   `useOptionalDispatchPaneEvent` + `useContext(AiSessionContext)`) and degrades
 *   gracefully instead of throwing — mirroring `DataverseEntityViewWidget`.
 *
 * ADR-039 / BFF §10 (Path C): surface identity stays in CODE — this file introduces
 * no server-side surface-identity endpoint or record. Section id `analysis` is
 * distinct from the `analysis-hub` Direct-widget type string.
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React 19),
 *            ADR-028 (Spaarke Auth v2 — the widget's BFF calls use the session's
 *            `authenticatedFetch`; no token snapshots here).
 */

import * as React from "react";
import { DocumentSearchRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { AnalysisHubWidget } from "@spaarke/ai-widgets";

export const analysisHubRegistration: SectionRegistration = {
  id: "analysis",
  label: "Analysis",
  description: "Create and reopen AI analyses — Agreement Review, Legal Research, Patent Application",
  icon: DocumentSearchRegular,
  category: "ai",
  // GROW pattern (matches communications/calendar/compose): a `defaultHeight` with no
  // `contentSizing` becomes a `min-height` FLOOR on the SectionPanel card, letting the
  // hub (cards row + grid) expand to fill the tab. The hub's own root is a bounded flex
  // column (see AnalysisHubWidget styles.root), so it fills whatever height it is given.
  defaultHeight: "640px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "analysis",
      type: "content",
      title: "Analysis",
      style: { overflow: "hidden" },
      // Pattern D thin shim: the widget is self-contained (reads its session/bus from
      // context, not from SectionFactoryContext props), so no host props are forwarded —
      // the same discriminator that distinguishes Pattern D from Pattern A sections.
      renderContent: () =>
        React.createElement(AnalysisHubWidget, {
          data: {},
          widgetType: "analysis-hub",
        }),
    };
  },
};

export default analysisHubRegistration;
