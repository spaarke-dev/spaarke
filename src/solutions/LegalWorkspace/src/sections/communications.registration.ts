/**
 * communications.registration.ts — SectionRegistration for the Communications workspace section.
 *
 * ai-spaarke-ai-workspace-UI-r2 FR-09 (2026-07-01): fifth Dataverse-entity-view
 * section sharing `<DataverseEntityViewWidget>` with documents/matters/projects/
 * invoices/workAssignments. Wraps a Spaarke DataGrid pointed at an operator-
 * created `sprk_gridconfiguration` row for the `sprk_communication` entity.
 *
 * Pattern D (dual-use): also registered as Direct widget `communications-list`
 * in `@spaarke/ai-widgets/widgets/workspace/register-workspace-widgets.ts`.
 *
 * Row-click behavior: Layout 1 (OOB modal via `Xrm.Navigation.navigateTo` at
 * 85% × 85%) via the DataGrid framework's `defaultRecordOpen` — unified in
 * Phase 1 (task 002) per FR-03/FR-20. `configjson.rowOpen.formId` is intentionally
 * omitted so the user's default `sprk_communication` main form opens (per FR-11).
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React 19),
 *            ADR-028 (Xrm.WebApi — no token snapshots).
 */

import * as React from "react";
import { MailRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DataverseEntityViewWidget } from "@spaarke/ai-widgets/widgets/workspace/DataverseEntityViewWidget";

/**
 * GUID of the `sprk_gridconfiguration` Dataverse row for the Communications view.
 * Created 2026-07-01 by ai-spaarke-ai-workspace-UI-r2 task 010; see
 * `projects/ai-spaarke-ai-workspace-UI-r2/notes/communications-config-record.md`.
 */
// spaarkedev1 sprk_gridconfiguration: 'Active Communications (Workspace)' (created 2026-07-01)
const COMMUNICATIONS_CONFIG_ID = "e1826c4c-9575-f111-ab0e-7ced8ddc4a05";

export const communicationsRegistration: SectionRegistration = {
  id: "communications",
  label: "Communications",
  description: "Email, Teams, SMS, and notifications related to your work",
  icon: MailRegular,
  category: "data",
  defaultHeight: "480px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "communications",
      type: "content",
      title: "Communications",
      // ai-spaarke-ai-workspace-UI-r2 follow-up (2026-07-01) height-chain fix:
      // The framework's `buildDynamicWorkspaceConfig` applies SectionMetadata's
      // `defaultHeight` as `min-height` (a floor), leaving the ceiling unbounded.
      // Without a `max-height` ceiling, the section grows to fit all N rows and
      // the DataGrid's internal scroll surface never overflows → no scrollbar,
      // no lazy-load-on-scroll trigger. Setting `maxHeight` here clamps the
      // section so the DataGrid scrolls internally and lazy-load works.
      style: { overflow: "hidden", maxHeight: "480px", display: "flex" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: { configId: COMMUNICATIONS_CONFIG_ID },
          widgetType: "communications-list",
        }),
    };
  },
};

export default communicationsRegistration;
