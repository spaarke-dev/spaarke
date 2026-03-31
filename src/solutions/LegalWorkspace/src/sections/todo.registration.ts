/**
 * todo.registration.ts — SectionRegistration for the My To Do List section.
 *
 * Migrates the SmartToDo (embedded) section from the monolithic workspaceConfig
 * to the dynamic Section Registry pattern (WKSP-001). SmartToDo is rendered in
 * embedded mode with disableSidePane=true for workspace glance mode.
 *
 * Toolbar layout: refresh (left) | divider | add + open (right, 15px gap)
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9)
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import {
  CheckmarkCircleRegular,
  ArrowClockwiseRegular,
  AddRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { SmartToDo } from "../components/SmartToDo/SmartToDo";

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

export const todoRegistration: SectionRegistration = {
  id: "todo",
  label: "My To Do List",
  description: "Embedded smart to-do list with flag sync",
  icon: CheckmarkCircleRegular,
  category: "productivity",
  defaultHeight: "560px",

  factory(ctx: SectionFactoryContext): ContentSectionConfig {
    // -------------------------------------------------------------------
    // Refetch holder — captured by toolbar closure, written by SmartToDo
    // via onRefetchReady during its first render cycle.
    // -------------------------------------------------------------------

    let refetchFn: (() => void) | undefined;

    // -------------------------------------------------------------------
    // Toolbar: refresh (left) | divider | add + open (right, 15px gap)
    // -------------------------------------------------------------------

    const toolbar = React.createElement(
      React.Fragment,
      null,
      // Refresh button — left-aligned via marginRight: auto
      React.createElement(Button, {
        appearance: "subtle",
        size: "small",
        icon: React.createElement(ArrowClockwiseRegular),
        onClick: () => {
          // Trigger refetch via the registered refetch callback
          if (refetchFn) refetchFn();
        },
        "aria-label": "Refresh To Do list",
        style: { marginRight: "auto" },
      }),
      // Divider
      React.createElement(ToolbarDivider),
      // Add + Open button group (right-aligned, 15px gap)
      React.createElement(
        "div",
        {
          style: {
            display: "flex",
            flexDirection: "row" as const,
            alignItems: "center",
            gap: "15px",
          },
        },
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(AddRegular),
          onClick: () => ctx.onOpenWizard("sprk_createtodowizard"),
          "aria-label": "Create new to do",
        }),
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(OpenRegular),
          onClick: () =>
            ctx.onOpenWizard("sprk_smarttodo", undefined, {
              width: { value: 85, unit: "%" },
              height: { value: 85, unit: "%" },
            }),
          "aria-label": "Open full To Do list",
        }),
      ),
    );

    return {
      id: "todo",
      type: "content",
      title: "My To Do List",
      toolbar,
      style: {},
      renderContent: () =>
        React.createElement(SmartToDo, {
          embedded: true,
          webApi: ctx.webApi as any,
          userId: ctx.userId,
          disableSidePane: true,
          onCountChange: ctx.onBadgeCountChange,
          onRefetchReady: (refetch: () => void) => {
            refetchFn = refetch;
            ctx.onRefetchReady(refetch);
          },
        }),
    };
  },
};

export default todoRegistration;
