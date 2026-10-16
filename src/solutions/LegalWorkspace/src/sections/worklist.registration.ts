/**
 * worklist.registration.ts — SectionRegistration for the Worklist workspace section.
 *
 * spaarke-ontology-platform-r1 task 156 (found at the 055 deploy): the `worklist` direct widget is
 * registered in `@spaarke/ai-widgets` (`register-workspace-widgets.ts`) but no Console path opened it,
 * because the workspace dropdown lists `sprk_workspacelayout` rows, not the widget registry (each row
 * mounts ONE LegalWorkspace section by id — see `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`
 * §3.1). This section shim is the missing half; the matching `sprk_workspacelayout` "Worklist" row
 * (sections:["worklist"]) is seeded by `scripts/seed-worklist-workspace-layout.ps1`. It copies how
 * `reconciliation.registration.ts` made "Reconciliation" reachable (Pattern D dual-use).
 *
 * Renders the SAME shared `WorklistWidget` (`@spaarke/ui-components`) as the direct widget
 * `WorklistWorkspaceWidget`, with a service from `createWorklistService({ fetch, baseUrl })` (D-15): the
 * Signal read route over LegalWorkspace's `authenticatedFetch` and `getBffBaseUrl()`. This shim reads no
 * `sprk_signal` / `sprk_decisionrecord` row through Xrm or Dataverse.
 *
 * Why the shim builds the service itself instead of reusing `WorklistWorkspaceWidget`: that widget reads
 * its fetch and base URL from `useAiSession()`, which THROWS outside an `AiSessionProvider`. The provider
 * is present in the Console (ThreePaneShell wraps the embedded `LegalWorkspaceApp`), but this section is
 * also in the default `SECTION_REGISTRY` the standalone LegalWorkspace code page uses — there is no
 * provider there, so reuse would crash that host. `authenticatedFetch` + `getBffBaseUrl()` work in both.
 *
 * Opening a line launches the decision wizard through `launchDecisionWizard` (D-26), the in-app host the
 * Console mounts once in `ThreePaneShell` — the same launcher `WorklistWidget` defaults to and the direct
 * widget uses. It is passed explicitly so the wiring is visible here. In the standalone code page (no
 * `DecisionWizardHost`) the launcher returns its own `{ opened: false, reason: 'no-host' }` result and the
 * widget shows its notice; no host is added here.
 *
 * The direct widget's optional slots (`lookup`, `aggregateRow`) are Xrm/pane-bus backed and are not wired
 * in this shim; the worklist lanes and the decision wizard launch are fully served by the BFF route.
 *
 * Declared `React.FC` with `React.createElement` per this directory's `.ts`-not-`.tsx` convention
 * (esbuild does not parse JSX in `.ts`).
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React-version-agnostic),
 *            ADR-028 (function-based auth contract), ADR-039/BFF §10 (surface identity in code).
 */

import * as React from "react";
import { TaskListSquareLtrRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import {
  WorklistWidget,
  createWorklistService,
  launchDecisionWizard,
} from "@spaarke/ui-components";
import { getBffBaseUrl } from "../config/runtimeConfig";
import { authenticatedFetch } from "../services/authInit";

// ---------------------------------------------------------------------------
// WorklistSectionMount — builds the BFF-route service and renders the shared
// widget. Scrolls as a whole (a document-like page, not a bounded grid), the
// same host styling the direct widget uses.
// ---------------------------------------------------------------------------
const WorklistSectionMount: React.FC = () => {
  const service = React.useMemo(
    () => createWorklistService({ fetch: authenticatedFetch, baseUrl: getBffBaseUrl() }),
    [],
  );

  return React.createElement(
    "div",
    {
      "data-testid": "worklist-section-host",
      style: {
        width: "100%",
        minWidth: 0,
        boxSizing: "border-box",
        overflowY: "auto",
      },
    },
    React.createElement(WorklistWidget, { service, launchWizard: launchDecisionWizard }),
  );
};

export const worklistRegistration: SectionRegistration = {
  id: "worklist",
  label: "Worklist",
  description: "Your Decide and Do lanes - open a line to decide or act on it",
  icon: TaskListSquareLtrRegular,
  category: "data",
  // Document-like, scrolling surface — same viewport floor as the other tall data sections.
  defaultHeight: "720px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "worklist",
      type: "content",
      title: "Worklist",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(WorklistSectionMount),
    };
  },
};

export default worklistRegistration;
