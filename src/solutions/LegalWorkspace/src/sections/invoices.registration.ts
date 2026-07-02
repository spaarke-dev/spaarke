/**
 * invoices.registration.ts — SectionRegistration for the Invoices workspace section.
 *
 * ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08): one of four shared
 * Dataverse-entity-view sections. Wraps the shared `<DataverseEntityViewWidget>`
 * with an operator-created `sprk_gridconfiguration` row.
 *
 * Pattern D (dual-use): also registered as Direct widget `invoices-list`.
 * Standards: ADR-012, ADR-021, ADR-022, ADR-028.
 */

import * as React from "react";
import { ReceiptRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DataverseEntityViewWidget } from "@spaarke/ai-widgets/widgets/workspace/DataverseEntityViewWidget";

/**
 * GUID of the `sprk_gridconfiguration` Dataverse row for the Invoices view.
 * **DEPLOYMENT REQUIREMENT**: replace before deploying. See
 * `projects/ai-spaarke-ai-workspace-UI-r1/notes/entity-view-widget-deployment.md`.
 */
// spaarkedev1 sprk_gridconfiguration: 'Invoice Matter Budget Performance' (pre-existing)
const INVOICES_CONFIG_ID = "d021827b-9b5e-f111-ab0c-7c1e521545d7";

export const invoicesRegistration: SectionRegistration = {
  id: "invoices",
  label: "Invoices",
  description: "Your invoices",
  icon: ReceiptRegular,
  category: "data",
  defaultHeight: "480px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "invoices",
      type: "content",
      title: "Invoices",
      // Height-chain fix (2026-07-01) — see communications.registration.ts for rationale.
      style: { overflow: "hidden", maxHeight: "480px", display: "flex" },
      renderContent: () =>
        React.createElement(DataverseEntityViewWidget, {
          data: { configId: INVOICES_CONFIG_ID },
          widgetType: "invoices-list",
        }),
    };
  },
};

export default invoicesRegistration;
