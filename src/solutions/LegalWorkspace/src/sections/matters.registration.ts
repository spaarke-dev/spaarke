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

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "matters",
      type: "content",
      title: "Matters",
      style: { overflow: "hidden" },
      // Height-chain fix v2 — see communications.registration.ts for rationale.
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
            data: { configId: MATTERS_CONFIG_ID },
            widgetType: "matters-list",
          }),
        ),
    };
  },
};

export default mattersRegistration;
