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
      style: { overflow: "hidden" },
      // ai-spaarke-ai-workspace-UI-r2 follow-up v2 (2026-07-01) height-chain fix:
      // Wrap the widget in a div with maxHeight + minHeight:0 INSIDE the content
      // slot. Setting maxHeight on the section.style (which applies to the
      // SectionPanel card) is defeated by SectionPanel's `content` div having
      // default `min-height: auto` — content refuses to shrink below intrinsic
      // minimum, forcing card to grow beyond its max-height. Wrapping HERE puts
      // the clamp inside the content flex-item slot where minHeight:0 propagates
      // correctly to the DataverseEntityViewWidget flex chain.
      renderContent: () =>
        React.createElement(
          "div",
          {
            style: {
              display: "flex",
              flexDirection: "column",
              flex: "1 1 auto",
              maxHeight: "80vh",
              minHeight: 0,
              overflow: "hidden",
            },
          },
          React.createElement(DataverseEntityViewWidget, {
            data: { configId: COMMUNICATIONS_CONFIG_ID },
            widgetType: "communications-list",
          }),
        ),
    };
  },
};

export default communicationsRegistration;
