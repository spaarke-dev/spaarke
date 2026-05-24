/**
 * quickSummary.registration.ts — SectionRegistration for the Quick Summary section.
 *
 * Migrates the Quick Summary (content) section from the monolithic workspaceConfig
 * to the dynamic Section Registry pattern (WKSP-001). QuickSummaryRow is already
 * self-contained — it manages its own data fetching internally.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9)
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import {
  DataBarVerticalRegular,
  ArrowClockwiseRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { QuickSummaryRow } from "../components/QuickSummary/QuickSummaryRow";

// ---------------------------------------------------------------------------
// Toolbar divider — thin vertical separator between toolbar button groups.
// Matches the original WorkspaceGrid layout using an inline span.
// ---------------------------------------------------------------------------

const ToolbarDivider: React.FC = () =>
  React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: "1px",
      height: "20px",
      backgroundColor: "var(--colorNeutralStroke2)",
      marginLeft: "2px",
      marginRight: "2px",
      flexShrink: 0,
      display: "inline-block",
    },
  });

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

export const quickSummaryRegistration: SectionRegistration = {
  id: "quick-summary",
  label: "Quick Summary",
  description: "Key metrics at a glance",
  icon: DataBarVerticalRegular,
  category: "overview",
  // Round 8 Wave 3 (task 110, 2026-05-22): bumped from "180px" to "440px"
  // to fit the 6-card 2x3 grid (each card ~120px tall + row gaps + section
  // padding). The 180px height was sized for the 4-card single-row layout
  // that shipped in Round 6.
  defaultHeight: "440px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    const toolbar = React.createElement(
      React.Fragment,
      null,
      React.createElement(Button, {
        appearance: "subtle",
        size: "small",
        icon: React.createElement(ArrowClockwiseRegular),
        "aria-label": "Refresh Quick Summary",
        style: { marginRight: "auto" },
      }),
      React.createElement(ToolbarDivider),
      React.createElement(Button, {
        appearance: "subtle",
        size: "small",
        icon: React.createElement(OpenRegular),
        onClick: () =>
          context.onOpenWizard("sprk_quicksummarydashboard", undefined, {
            width: { value: 85, unit: "%" },
            height: { value: 85, unit: "%" },
          }),
        "aria-label": "Open Quick Summary Dashboard",
      }),
    );

    return {
      id: "quick-summary",
      type: "content",
      title: "Quick Summary",
      toolbar,
      style: {},
      renderContent: () =>
        React.createElement(
          "div",
          { style: { padding: "8px 12px 12px 12px" } },
          React.createElement(QuickSummaryRow, {
            webApi: context.webApi as any,
            userId: context.userId,
            scope: context.scope,
            businessUnitId: context.businessUnitId,
          }),
        ),
    };
  },
};

export default quickSummaryRegistration;
