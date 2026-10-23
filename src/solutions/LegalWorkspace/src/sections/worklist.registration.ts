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
 * Signal read route over LegalWorkspace's `authenticatedFetch` and `getBffBaseUrl()`. The lanes come from
 * that route and nowhere else: this shim never reads a `sprk_signal` or `sprk_decisionrecord` row, through
 * Xrm or Dataverse.
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
 * The widget's two optional slots are filled as the direct widget fills them (`WorklistWorkspaceWidget`):
 *   - `lookup` - `createWorklistLookup(Xrm.WebApi)`, the decision wizard's Person / Lookup search. The
 *     `DecisionWizardHost` has no lookup of its own, so without it Revise budget, Reassign, Add To Do and
 *     Assign Work (required Lookup / Person fields) cannot be completed. It queries contacts, `sprk_budget`
 *     and `sprk_organization` (allowed by D-15, which bars only Signal / Decision Record reads). Undefined
 *     when there is no Xrm (the wizard's searches then find nothing).
 *   - `openLookup` / `openRecord` - task 158 (D-182): `createWorklistOpenLookup` / `createWorklistOpenRecord` over
 *     `createXrmNavigationService()`. Every Person / Lookup field in the wizard then opens the platform side-pane
 *     lookup (scoped as `lookup` is), and the subject in *What was found* opens in a modal above the wizard.
 *     Undefined when there is no Xrm (the wizard keeps its inline type-ahead and shows the subject as text).
 *   - `aggregateRow` - task 054's `ReconciliationAggregateCard`; "Open Email Review" dispatches `widget_load`
 *     for the reconciliation tab on the PaneEventBus the Console mounts above the embedded workspace
 *     (a no-op where there is no bus, e.g. the standalone page, via `useOptionalDispatchPaneEvent`).
 *
 * Declared `React.FC` with `React.createElement` per this directory's `.ts`-not-`.tsx` convention
 * (esbuild does not parse JSX in `.ts`).
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React-version-agnostic),
 *            ADR-028 (function-based auth contract), ADR-039/BFF §10 (surface identity in code).
 */

import * as React from "react";
import { tokens } from "@fluentui/react-components";
import { TaskListSquareLtrRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import {
  WorklistWidget,
  XrmDataverseClient,
  createWorklistService,
  createXrmNavigationService,
  getXrm,
  launchDecisionWizard,
} from "@spaarke/ui-components";
import {
  createWorklistLookup,
  createWorklistOpenLookup,
  createWorklistOpenRecord,
  useOptionalDispatchPaneEvent,
  type WorklistLookupDataSource,
} from "@spaarke/ai-widgets";
import { ReconciliationAggregateCard } from "@spaarke/communication-components";
import { getBffBaseUrl } from "../config/runtimeConfig";
import { authenticatedFetch } from "../services/authInit";

// The registered type string of the Email Review (reconciliation) tab the aggregate card opens - the value of
// `RECONCILIATION_WIDGET_TYPE` in `@spaarke/ai-widgets` `WorklistWorkspaceWidget.tsx` (not exported from the package index).
const RECONCILIATION_WIDGET_TYPE = "communications-reconciliation";

// ---------------------------------------------------------------------------
// WorklistSectionMount - builds the BFF-route service, fills the lookup and
// aggregate-row slots as the direct widget does, and renders the shared widget.
// ---------------------------------------------------------------------------
const WorklistSectionMount: React.FC = () => {
  const service = React.useMemo(
    () => createWorklistService({ fetch: authenticatedFetch, baseUrl: getBffBaseUrl() }),
    [],
  );
  const dispatch = useOptionalDispatchPaneEvent();

  // Without Xrm (a non-MDA dev shell) the wizard's searches find nothing, and the aggregate card reports its count as Missing.
  const webApi = React.useMemo(() => getXrm()?.WebApi as WorklistLookupDataSource | undefined, []);
  const lookup = React.useMemo(() => (webApi ? createWorklistLookup(webApi) : undefined), [webApi]);
  const dataverseClient = React.useMemo(() => (webApi ? new XrmDataverseClient() : undefined), [webApi]);
  // D-182: the wizard's Person / Lookup fields open the platform side-pane lookup and its subject opens in a modal; without Xrm
  // the wizard keeps its inline type-ahead and shows the subject as text.
  const navigation = React.useMemo(() => (webApi ? createXrmNavigationService() : undefined), [webApi]);
  const openLookup = React.useMemo(() => (navigation ? createWorklistOpenLookup(navigation) : undefined), [navigation]);
  const openRecord = React.useMemo(() => (navigation ? createWorklistOpenRecord(navigation) : undefined), [navigation]);

  const openEmailReview = React.useCallback(() => {
    dispatch("workspace", { type: "widget_load", widgetType: RECONCILIATION_WIDGET_TYPE });
  }, [dispatch]);

  const aggregateRow = React.useMemo(
    () => React.createElement(ReconciliationAggregateCard, { dataverseClient, onOpen: openEmailReview }),
    [dataverseClient, openEmailReview],
  );

  // The Console's workspace tab scrolls as a whole; the worklist is a document-like page, so the host only pads it
  // (the same padding the direct widget uses).
  return React.createElement(
    "div",
    {
      "data-testid": "worklist-section-host",
      style: {
        width: "100%",
        minWidth: 0,
        boxSizing: "border-box",
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
      },
    },
    React.createElement(WorklistWidget, {
      service,
      lookup,
      openLookup,
      openRecord,
      aggregateRow,
      launchWizard: launchDecisionWizard,
    }),
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
