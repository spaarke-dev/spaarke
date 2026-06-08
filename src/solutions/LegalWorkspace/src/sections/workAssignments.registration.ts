/**
 * workAssignments.registration.ts — SectionRegistration for the Work Assignments section.
 *
 * ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08): one of four shared
 * Dataverse-entity-view sections. Wraps the shared `<DataverseEntityViewWidget>`
 * with an operator-created `sprk_gridconfiguration` row for the
 * `sprk_workassignment` entity's default view.
 *
 * Pattern D (dual-use): also registered as Direct widget `work-assignments-list`.
 * Standards: ADR-012, ADR-021, ADR-022, ADR-028.
 */

import * as React from "react";
import { BriefcaseRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DataverseEntityViewWidget } from "@spaarke/ai-widgets/widgets/workspace/DataverseEntityViewWidget";

/**
 * GUID of the `sprk_gridconfiguration` Dataverse row for the Work Assignments view.
 * **DEPLOYMENT REQUIREMENT**: replace before deploying. See
 * `projects/ai-spaarke-ai-workspace-UI-r1/notes/entity-view-widget-deployment.md`.
 */
const WORK_ASSIGNMENTS_CONFIG_ID = "REPLACE-ME-WORK-ASSIGNMENTS-CONFIG-ID";

export const workAssignmentsRegistration: SectionRegistration = {
  id: "work-assignments",
  label: "Work Assignments",
  description: "Work assignments routed to you",
  icon: BriefcaseRegular,
  category: "data",
  defaultHeight: "480px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "work-assignments",
      type: "content",
      title: "Work Assignments",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: { configId: WORK_ASSIGNMENTS_CONFIG_ID },
          widgetType: "work-assignments-list",
        }),
    };
  },
};

export default workAssignmentsRegistration;
