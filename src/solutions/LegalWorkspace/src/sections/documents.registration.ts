/**
 * documents.registration.ts — SectionRegistration for the My Documents section.
 *
 * Migrates the My Documents (DocumentsTab content) section from the monolithic
 * workspaceConfig to the dynamic Section Registry pattern (WKSP-001). Moves
 * toolbar construction and handler wiring into the factory.
 *
 * The section title area renders a plain-text Menu-based view picker (instead of
 * a bordered Dropdown) so users can switch between available sprk_document views.
 * Selecting a view updates both the card list AND which view opens in the dialog.
 *
 * Architecture: DocumentsViewMenuTitle (title) and DocumentsSectionContent
 * (body) are siblings in SectionPanel. They share state via a mutable controller
 * ref — the title writes a setter into the ref on mount, and the body calls it
 * when the view changes. This avoids lifting state into the workspace root.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9)
 */

import * as React from "react";
import {
  Button,
  Text,
  Menu,
  MenuTrigger,
  MenuButton,
  MenuPopover,
  MenuList,
  MenuItem,
  tokens,
} from "@fluentui/react-components";
import {
  DocumentRegular,
  ArrowClockwiseRegular,
  AddRegular,
  OpenRegular,
  CheckmarkRegular,
} from "@fluentui/react-icons";
import { ViewService, getXrm } from "@spaarke/ui-components";
import type { IViewDefinition } from "@spaarke/ui-components";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DocumentsTab } from "../components/RecordCards/DocumentsTab";
import type { DataverseService } from "../services/DataverseService";

// ---------------------------------------------------------------------------
// Toolbar divider — thin vertical separator between toolbar button groups.
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
// DocumentsViewMenuTitle — renders a plain-text Menu-based view picker in the
// section title area. Looks like "My Documents ∨" with no border or background,
// matching the section title font (size 400, semibold).
//
// Exposes view changes to the factory via onViewChange callback.
// ---------------------------------------------------------------------------

interface IDocumentsViewMenuTitleProps {
  onViewChange: (view: IViewDefinition) => void;
}

const DocumentsViewMenuTitle: React.FC<IDocumentsViewMenuTitleProps> = ({
  onViewChange,
}) => {
  const [views, setViews] = React.useState<IViewDefinition[]>([]);
  const [selectedView, setSelectedView] = React.useState<IViewDefinition | undefined>();
  const xrm = getXrm();

  React.useEffect(() => {
    if (!xrm) return;
    let mounted = true;
    const service = new ViewService(xrm);
    service
      .getViews("sprk_document", { includePersonal: true })
      .then((fetchedViews) => {
        if (!mounted) return;
        setViews(fetchedViews);
        // Auto-select the default view but don't call onViewChange —
        // the default is already what the tab shows on initial load.
        const defaultView = fetchedViews.find((v) => v.isDefault) ?? fetchedViews[0];
        if (defaultView) setSelectedView(defaultView);
      })
      .catch(() => {
        // View fetch failed — graceful fallback to static title
      });
    return () => {
      mounted = false;
    };
  }, [xrm]);

  const displayName = selectedView?.name ?? "My Documents";

  // No Xrm or no views yet — render static title
  if (!xrm || views.length === 0) {
    return React.createElement(
      Text,
      { size: 400, weight: "semibold" as const },
      displayName,
    );
  }

  return React.createElement(
    Menu,
    null,
    React.createElement(
      MenuTrigger,
      { disableButtonEnhancement: true },
      React.createElement(
        MenuButton,
        {
          appearance: "transparent" as const,
          size: "small" as const,
          style: {
            fontSize: tokens.fontSizeBase400,
            fontWeight: tokens.fontWeightSemibold,
            color: tokens.colorNeutralForeground1,
            padding: "0 2px",
            minWidth: "unset",
            height: "auto",
            lineHeight: "normal",
          },
        },
        displayName,
      ),
    ),
    React.createElement(
      MenuPopover,
      null,
      React.createElement(
        MenuList,
        null,
        ...views.map((view) =>
          React.createElement(
            MenuItem,
            {
              key: view.id,
              icon:
                selectedView?.id === view.id
                  ? React.createElement(CheckmarkRegular)
                  : React.createElement("span", { style: { width: "16px", display: "inline-block" } }),
              onClick: () => {
                setSelectedView(view);
                onViewChange(view);
              },
            },
            view.name,
          ),
        ),
      ),
    ),
  );
};

// ---------------------------------------------------------------------------
// DocumentsSectionContent — wrapper that holds selectedViewId React state.
//
// This component is the "body" sibling of DocumentsViewMenuTitle in SectionPanel.
// Since they cannot share React state directly, the factory creates a controller
// ref. This component writes its setSelectedViewId into that ref via
// onControllerReady; the title calls it when the user picks a different view.
// ---------------------------------------------------------------------------

interface IDocumentsSectionContentProps {
  service: DataverseService;
  userId: string;
  onCountChange?: (count: number) => void;
  onRefetchReady?: (refetch: () => void) => void;
  onControllerReady?: (setView: (viewId: string | undefined) => void) => void;
  maxVisible?: number;
}

const DocumentsSectionContent: React.FC<IDocumentsSectionContentProps> = ({
  service,
  userId,
  onCountChange,
  onRefetchReady,
  onControllerReady,
  maxVisible,
}) => {
  const [selectedViewId, setSelectedViewId] = React.useState<string | undefined>();

  // Register the setter so the title component can trigger view changes.
  const stableOnControllerReady = React.useRef(onControllerReady);
  React.useEffect(() => {
    stableOnControllerReady.current?.(setSelectedViewId);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return React.createElement(DocumentsTab, {
    service,
    userId,
    maxVisible: maxVisible ?? 6,
    selectedViewId,
    onCountChange,
    onRefetchReady,
  });
};

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
    // selectedViewIdRef — the currently selected view ID.
    // Written by DocumentsViewMenuTitle, read by the Open button onClick.
    const selectedViewIdRef: { current: string | undefined } = { current: undefined };

    // contentControllerRef — holds the setter exposed by DocumentsSectionContent.
    // Written by DocumentsSectionContent on mount, called by DocumentsViewMenuTitle.
    const contentControllerRef: {
      current: ((viewId: string | undefined) => void) | undefined;
    } = { current: undefined };

    // Refetch handle — captured by the refresh button's onClick closure.
    let refetchFn: (() => void) | undefined;

    // Title: plain-text Menu-based view picker
    const titleContent = React.createElement(DocumentsViewMenuTitle, {
      onViewChange: (view: IViewDefinition) => {
        selectedViewIdRef.current = view.id;
        contentControllerRef.current?.(view.id);
      },
    });

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
        // Add document button
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(AddRegular),
          onClick: () => context.onOpenWizard("sprk_documentuploadwizard"),
          "aria-label": "Add document",
        }),
        // Open documents list button — opens the selected view in Dataverse
        React.createElement(Button, {
          appearance: "subtle",
          size: "small",
          icon: React.createElement(OpenRegular),
          onClick: () =>
            context.onOpenDocumentsDialog?.(selectedViewIdRef.current),
          "aria-label": "Open documents list",
        }),
      ),
    );

    return {
      id: "documents",
      type: "content",
      title: "My Documents",
      titleContent,
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
          React.createElement(DocumentsSectionContent, {
            service: context.service as DataverseService,
            userId: context.userId,
            maxVisible: 6,
            onCountChange: context.onBadgeCountChange,
            onRefetchReady: (refetch: () => void) => {
              refetchFn = refetch;
              context.onRefetchReady(refetch);
            },
            onControllerReady: (setView) => {
              contentControllerRef.current = setView;
            },
          }),
        ),
    };
  },
};

export default documentsRegistration;
