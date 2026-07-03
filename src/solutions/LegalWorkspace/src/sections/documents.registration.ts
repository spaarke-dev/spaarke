/**
 * documents.registration.ts — SectionRegistration for the My Documents workspace section.
 *
 * ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08):
 *   Retired the card-view `DocumentsTab` implementation in favour of the shared
 *   `<DataverseEntityViewWidget>` from `@spaarke/ai-widgets`. The section now
 *   embeds a Spaarke DataGrid driven by an operator-created
 *   `sprk_gridconfiguration` row (constant DOCUMENTS_CONFIG_ID below — must be
 *   replaced before deployment). The grid framework owns the view picker,
 *   filter chips, command bar, and lazy paging that the previous card view
 *   approximated by hand.
 *
 * Pattern D (dual-use):
 *   Same widget is registered as a Direct widget (`documents-list` widgetType)
 *   in `@spaarke/ai-widgets/widgets/workspace/register-workspace-widgets.ts`.
 *   The component is the single source of truth; this file is the LW-side shim.
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React 19),
 *            ADR-028 (Xrm.WebApi — no token snapshots).
 */

import * as React from "react";
import { DocumentRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DataverseEntityViewWidget } from "@spaarke/ai-widgets/widgets/workspace/DataverseEntityViewWidget";

/**
 * GUID of the `sprk_gridconfiguration` Dataverse row for the My Documents view.
 *
 * **DEPLOYMENT REQUIREMENT**: Operator MUST replace this placeholder with the
 * real GUID before deployment. See
 * `projects/ai-spaarke-ai-workspace-UI-r1/notes/entity-view-widget-deployment.md`
 * for the operator setup. The DataGrid framework renders a clear empty state
 * when this id resolves to an unknown record.
 */
// spaarkedev1 sprk_gridconfiguration: 'Active Documents (Workspace)'
// (created 2026-06-09; replaces the legacy 'Semantic Search Documents View'
// row d99a4352-… which had the SemanticSearchControl PCF config shape
// instead of the DataGrid framework `source` shape).
const DOCUMENTS_CONFIG_ID = "1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6";

export const documentsRegistration: SectionRegistration = {
  id: "documents",
  label: "My Documents",
  description: "Your documents",
  icon: DocumentRegular,
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
      context.sectionInstance?.configIdOverride ?? DOCUMENTS_CONFIG_ID;
    const instanceOverrides = context.sectionInstance?.overrides;
    return {
      id: "documents",
      type: "content",
      title: "My Documents",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: {
            configId: effectiveConfigId,
            pageSize: instanceOverrides?.pageSize,
            availableViews: instanceOverrides?.availableViews,
          },
          widgetType: "documents-list",
        }),
    };
  },
};

export default documentsRegistration;
