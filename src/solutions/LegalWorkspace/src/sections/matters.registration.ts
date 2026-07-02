/**
 * matters.registration.ts — SectionRegistration for the Matters workspace section.
 *
 * ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08, iteration 2): added per operator
 * testing feedback — Matters joins Documents/Projects/Invoices/WorkAssignments
 * as a Dataverse-entity-view section. Same shared widget as the others, just
 * pointed at a different `sprk_gridconfiguration` GUID.
 *
 * Pattern D (dual-use): also registered as Direct widget `matters-list`.
 * Standards: ADR-012, ADR-021, ADR-022, ADR-028.
 */

import * as React from "react";
import { BriefcaseSearchRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DataverseEntityViewWidget } from "@spaarke/ai-widgets/widgets/workspace/DataverseEntityViewWidget";

// spaarkedev1 sprk_gridconfiguration: 'Active Matters (Workspace)' (created 2026-06-08)
const MATTERS_CONFIG_ID = "113ad380-9e63-f111-ab0c-70a8a53ec687";

export const mattersRegistration: SectionRegistration = {
  id: "matters",
  label: "Matters",
  description: "Your matters",
  icon: BriefcaseSearchRegular,
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
      context.sectionInstance?.configIdOverride ?? MATTERS_CONFIG_ID;
    const instanceOverrides = context.sectionInstance?.overrides;
    return {
      id: "matters",
      type: "content",
      title: "Matters",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: {
            configId: effectiveConfigId,
            pageSize: instanceOverrides?.pageSize,
            availableViews: instanceOverrides?.availableViews,
          },
          widgetType: "matters-list",
        }),
    };
  },
};

export default mattersRegistration;
