/**
 * documents.registration.ts — SectionRegistration for the My Documents section.
 *
 * Migrates the My Documents (DocumentsTab content) section from the monolithic
 * workspaceConfig to the dynamic Section Registry pattern (WKSP-001). Moves
 * toolbar construction and handler wiring into the factory.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9)
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import {
  DocumentRegular,
  ArrowClockwiseRegular,
  AddRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DocumentsTab } from "../components/RecordCards/DocumentsTab";
import type { DataverseService } from "../services/DataverseService";

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

export const documentsRegistration: SectionRegistration = {
  id: "documents",
  label: "My Documents",
  description: "Recent documents with quick actions",
  icon: DocumentRegular,
  category: "data",
  defaultHeight: "300px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    // Refetch handle — captured by the refresh button's onClick closure
    // and registered via DocumentsTab's onRefetchReady callback.
    let refetchFn: (() => void) | undefined;

    const toolbar = React.createElement(
      React.Fragment,
      null,
      // Refresh button (left-aligned via marginRight: auto)
      React.createElement(Button, {
        appearance: "subtle",
        size: "small",
        icon: React.createElement(ArrowClockwiseRegular),
        onClick: () => refetchFn?.(),
        "aria-label": "Refresh documents",
        style: { marginRight: "auto" },
      }),
      // Divider
      React.createElement(ToolbarDivider),
      // Right button group: add + open (15px gap)
      React.createElement(
        "div",
        {
          style: {
            display: "flex",
            flexDirection: "row",
            alignItems: "center",
            gap: "15px",
          },
        },
        // Add document button — opens DocumentUploadWizard in standalone mode
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(AddRegular),
          onClick: () =>
            context.onOpenWizard("sprk_documentuploadwizard"),
          "aria-label": "Add document",
        }),
        // Open all documents button
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(OpenRegular),
          onClick: () =>
            context.onOpenWizard("sprk_alldocuments", undefined, {
              width: { value: 85, unit: "%" },
              height: { value: 85, unit: "%" },
            }),
          "aria-label": "Open all documents",
        }),
      ),
    );

    return {
      id: "documents",
      type: "content",
      title: "My Documents",
      toolbar,
      style: { overflow: "visible" },
      renderContent: () =>
        React.createElement(
          "div",
          {
            style: {
              display: "flex",
              flexDirection: "column",
              flex: "1 1 0",
              overflow: "visible",
            },
          },
          React.createElement(DocumentsTab, {
            service: context.service as DataverseService,
            userId: context.userId,
            maxVisible: 6,
            onCountChange: context.onBadgeCountChange,
            onRefetchReady: (refetch: () => void) => {
              refetchFn = refetch;
              context.onRefetchReady(refetch);
            },
          }),
        ),
    };
  },
};

export default documentsRegistration;
