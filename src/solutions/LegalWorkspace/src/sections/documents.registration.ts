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

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "documents",
      type: "content",
      title: "My Documents",
      // Height-chain fix (2026-07-01): `defaultHeight` is applied as `min-height`
      // by buildDynamicWorkspaceConfig — floor without ceiling → section grows to
      // fit content → DataGrid scroll surface never overflows. `maxHeight` here
      // clamps the section so the DataGrid scrolls internally + lazy-load works.
      style: { overflow: "hidden", maxHeight: "480px", display: "flex" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: { configId: DOCUMENTS_CONFIG_ID },
          widgetType: "documents-list",
        }),
    };
  },
};

export default documentsRegistration;
