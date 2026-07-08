/**
 * projects.registration.ts — SectionRegistration for the Projects workspace section.
 *
 * ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08): one of four shared
 * Dataverse-entity-view sections (documents / projects / invoices /
 * work-assignments). Each wraps the shared `<DataverseEntityViewWidget>` with
 * an operator-created `sprk_gridconfiguration` row.
 *
 * Pattern D (dual-use): also registered as Direct widget `projects-list`.
 * Standards: ADR-012, ADR-021, ADR-022, ADR-028.
 */

import * as React from "react";
import { FolderRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DataverseEntityViewWidget } from "@spaarke/ai-widgets/widgets/workspace/DataverseEntityViewWidget";

/**
 * GUID of the `sprk_gridconfiguration` Dataverse row for the Projects view.
 * **DEPLOYMENT REQUIREMENT**: replace before deploying. See
 * `projects/ai-spaarke-ai-workspace-UI-r1/notes/entity-view-widget-deployment.md`.
 */
// spaarkedev1 sprk_gridconfiguration: 'Active Projects (Workspace)' (created 2026-06-08)
const PROJECTS_CONFIG_ID = "97ee98e7-7a63-f111-ab0c-70a8a53ec687";

export const projectsRegistration: SectionRegistration = {
  id: "projects",
  label: "Projects",
  description: "Your projects",
  icon: FolderRegular,
  category: "data",
  defaultHeight: "480px",
  // spaarke-dataset-grid-framework-r2 FR-08 (task 005, 2026-07-02): replaces the
  // prior tactical 80vh clamp wrapper. See communications.registration.ts.
  contentSizing: "clamped",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    // spaarke-dataset-grid-framework-r2 DEF-005 / DEF-005b+c (2026-07-02, FR-03
    // end-to-end wiring): honor per-instance overrides from the LayoutJsonRow
    // SectionInstance. Bare-string entries + omitted overrides fall through to
    // the baked-in default configId / config-record / framework defaults.
    // Widget forwards to `<DataGrid />` which delegates precedence to
    // `resolveEffectivePageSize` + `resolveEffectiveAvailableViews`.
    const effectiveConfigId =
      context.sectionInstance?.configIdOverride ?? PROJECTS_CONFIG_ID;
    const instanceOverrides = context.sectionInstance?.overrides;
    return {
      id: "projects",
      type: "content",
      title: "Projects",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: {
            configId: effectiveConfigId,
            pageSize: instanceOverrides?.pageSize,
            availableViews: instanceOverrides?.availableViews,
          },
          widgetType: "projects-list",
        }),
    };
  },
};

export default projectsRegistration;
