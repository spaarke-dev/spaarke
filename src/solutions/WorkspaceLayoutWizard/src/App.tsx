/**
 * Workspace Layout Wizard - Root Application Component
 *
 * Standalone wizard dialog for creating, editing, or cloning workspace section layouts.
 * Opened via Xrm.Navigation.navigateTo as a webresource dialog.
 *
 * Uses the shared WizardShell component (ADR-012) in embedded mode for the
 * sidebar stepper, footer navigation, and finish/error/success flow.
 *
 * Wizard Steps:
 *   Step 1: Choose Layout - Select template or start blank
 *   Step 2: Configure Sections - Add/remove/reorder sections
 *   Step 3: Review & Save - Preview layout and confirm
 *
 * @see ADR-026 - Full-Page Custom Page Standard
 * @see ADR-021 - Fluent UI v9 Design System
 * @see ADR-012 - Shared component library (WizardShell)
 */

import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import { tokens, Text, Button } from "@fluentui/react-components";
import {
  CheckmarkCircle24Regular,
  CheckmarkCircleRegular,
  RocketRegular,
  DataBarVerticalRegular,
  ClockRegular,
  DocumentRegular,
} from "@fluentui/react-icons";
import { WizardShell, getLayoutTemplate } from "@spaarke/ui-components";
import type {
  IWizardStepConfig,
  IWizardShellHandle,
  IWizardSuccessConfig,
  LayoutTemplateId,
} from "@spaarke/ui-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { TemplateStep, SectionStep, ArrangeStep, buildInitialAssignments } from "./steps";
import type { SectionCatalogItem, SlotAssignments } from "./steps";
import type { WizardMode } from "./main";

/** Parsed LayoutJson row — mirrors the BFF LayoutJsonRow shape for sectionsJson parsing. */
interface LayoutJsonRow {
  id: string;
  columns: string;
  columnsSmall?: string;
  sections: string[];
}

/** Parsed LayoutJson — mirrors the BFF LayoutJson shape for sectionsJson parsing. */
interface LayoutJson {
  schemaVersion: number;
  rows: LayoutJsonRow[];
}

/** Props passed from main.tsx based on URL data parameters */
interface AppProps {
  mode: WizardMode;
  layoutId: string | null;
  /** Template ID from the source layout (saveAs mode) */
  layoutTemplateId: string | null;
  /** JSON-encoded sections from the source layout (saveAs mode) */
  sectionsJson: string | null;
  /** Display name of the source layout (saveAs mode) */
  sourceName: string | null;
  /** Authenticated fetch function from @spaarke/auth for BFF API calls. */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
}

// ---------------------------------------------------------------------------
// Section catalog — inline constant matching the 5 registered sections.
// In a production build this would be fetched from GET /api/workspace/sections.
// ---------------------------------------------------------------------------

const SECTION_CATALOG: SectionCatalogItem[] = [
  {
    id: "get-started",
    label: "Get Started",
    description: "Quick-action cards for common workflows",
    category: "overview",
    icon: RocketRegular,
  },
  {
    id: "quick-summary",
    label: "Quick Summary",
    description: "Key metrics at a glance",
    category: "overview",
    icon: DataBarVerticalRegular,
  },
  {
    id: "latest-updates",
    label: "Latest Updates",
    description: "Recent activity feed with flagging",
    category: "data",
    icon: ClockRegular,
    defaultHeight: "325px",
  },
  {
    id: "todo",
    label: "My To Do List",
    description: "Embedded smart to-do list with flag sync",
    category: "productivity",
    icon: CheckmarkCircleRegular,
    defaultHeight: "560px",
  },
  {
    id: "documents",
    label: "My Documents",
    description: "Recent documents with quick actions",
    category: "data",
    icon: DocumentRegular,
  },
];

/** Default section IDs — all 5 sections selected by default. */
const DEFAULT_SECTION_IDS = new Set(SECTION_CATALOG.map((s) => s.id));

// ---------------------------------------------------------------------------
// Step IDs (stable string keys for WizardShell)
// ---------------------------------------------------------------------------

const STEP_CHOOSE_LAYOUT = "choose-layout";
const STEP_CONFIGURE_SECTIONS = "configure-sections";
const STEP_REVIEW_SAVE = "review-save";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Parse sectionsJson from the source layout into:
 * - a Set of selected section IDs
 * - a SlotAssignments Map (slot key -> section ID)
 */
function parseSectionsJson(
  json: string,
): { sectionIds: Set<string>; assignments: SlotAssignments } {
  const sectionIds = new Set<string>();
  const assignments: SlotAssignments = new Map();
  try {
    const parsed = JSON.parse(json) as LayoutJson;
    for (const row of parsed.rows) {
      for (let col = 0; col < row.sections.length; col++) {
        const sectionId = row.sections[col];
        if (sectionId) {
          sectionIds.add(sectionId);
          assignments.set(`${row.id}:${col}`, sectionId);
        }
      }
    }
  } catch (err) {
    console.warn("[WorkspaceLayoutWizard] Failed to parse sectionsJson:", err);
  }
  return { sectionIds, assignments };
}

/**
 * Build the sectionsJson string from current wizard state.
 * Transforms slot assignments (Map<slotKey, sectionId>) into the LayoutJson
 * format expected by the BFF API: { schemaVersion: 1, rows: [{ id, sections }] }
 */
function buildSectionsJson(
  templateId: LayoutTemplateId,
  assignments: SlotAssignments,
): string {
  const template = getLayoutTemplate(templateId);
  if (!template) return JSON.stringify({ schemaVersion: 1, rows: [] });

  const rows: LayoutJsonRow[] = template.rows.map((row) => {
    const sections: string[] = [];
    for (let col = 0; col < row.slotCount; col++) {
      const sectionId = assignments.get(`${row.id}:${col}`);
      sections.push(sectionId ?? "");
    }
    return {
      id: row.id,
      columns: row.gridTemplateColumns,
      columnsSmall: row.gridTemplateColumnsSmall,
      sections,
    };
  });

  return JSON.stringify({ schemaVersion: 1, rows } satisfies LayoutJson);
}

// ---------------------------------------------------------------------------
// App Component
// ---------------------------------------------------------------------------

export const App: React.FC<AppProps> = ({ mode, layoutId, layoutTemplateId, sectionsJson, sourceName, authenticatedFetch }) => {
  // ---------------------------------------------------------------------------
  // SaveAs pre-population: parse source layout data once at mount time
  // ---------------------------------------------------------------------------
  const saveAsData = React.useMemo(() => {
    if (mode !== "saveAs" || !sectionsJson) return null;
    const { sectionIds, assignments } = parseSectionsJson(sectionsJson);
    return {
      templateId: (layoutTemplateId ?? null) as LayoutTemplateId | null,
      sectionIds,
      assignments,
      name: sourceName ? `${sourceName} (copy)` : "",
    };
  }, [mode, layoutTemplateId, sectionsJson, sourceName]);

  const [theme, setTheme] = React.useState(resolveTheme);
  const [selectedTemplateId, setSelectedTemplateId] =
    React.useState<LayoutTemplateId | null>(saveAsData?.templateId ?? null);
  const [selectedSectionIds, setSelectedSectionIds] = React.useState<
    Set<string>
  >(() => saveAsData?.sectionIds ?? new Set(DEFAULT_SECTION_IDS));
  const [workspaceName, setWorkspaceName] = React.useState(saveAsData?.name ?? "");
  const [isDefault, setIsDefault] = React.useState(false);
  const [sectionAssignments, setSectionAssignments] =
    React.useState<SlotAssignments>(saveAsData?.assignments ?? new Map());

  const wizardRef = React.useRef<IWizardShellHandle>(null);

  /** Derive slot count from the selected template. */
  const slotCount = React.useMemo(() => {
    if (!selectedTemplateId) return 0;
    return getLayoutTemplate(selectedTemplateId)?.slotCount ?? 0;
  }, [selectedTemplateId]);

  /** Toggle a section in the selection set (immutable update). */
  const handleSectionToggle = React.useCallback((sectionId: string) => {
    setSelectedSectionIds((prev) => {
      const next = new Set(prev);
      if (next.has(sectionId)) {
        next.delete(sectionId);
      } else {
        next.add(sectionId);
      }
      return next;
    });
  }, []);

  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  /** Derive the selected section catalog items in stable order. */
  const selectedSections = React.useMemo(
    () => SECTION_CATALOG.filter((s) => selectedSectionIds.has(s.id)),
    [selectedSectionIds],
  );

  /**
   * Auto-initialize slot assignments when entering the Review & Save step.
   * Watches the wizard shell state to detect when we arrive at step index 2
   * with an empty assignments map, then populates default assignments.
   */
  const prevStepRef = React.useRef<number>(-1);
  React.useEffect(() => {
    const currentIndex = wizardRef.current?.state.currentStepIndex ?? -1;
    const wasOnDifferentStep = prevStepRef.current !== currentIndex;
    prevStepRef.current = currentIndex;

    if (wasOnDifferentStep && currentIndex === 2 && selectedTemplateId && sectionAssignments.size === 0) {
      setSectionAssignments(buildInitialAssignments(selectedTemplateId, selectedSections));
    }
  });

  // ---------------------------------------------------------------------------
  // onFinish: Save the wizard state to the BFF API.
  // - Create / SaveAs -> POST /api/workspace/layouts
  // - Edit -> PUT /api/workspace/layouts/{layoutId}
  // ---------------------------------------------------------------------------

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    if (!selectedTemplateId) {
      throw new Error("No layout template selected.");
    }

    const body = {
      name: workspaceName.trim(),
      layoutTemplateId: selectedTemplateId,
      sectionsJson: buildSectionsJson(selectedTemplateId, sectionAssignments),
      isDefault,
    };

    const isEdit = mode === "edit" && layoutId;
    const url = isEdit
      ? `/api/workspace/layouts/${layoutId}`
      : "/api/workspace/layouts";
    const method = isEdit ? "PUT" : "POST";

    try {
      const response = await authenticatedFetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      // Parse the created/updated layout to extract the ID
      const savedLayout = await response.json();
      const savedId = savedLayout?.id ?? savedLayout?.Id ?? layoutId;

      // Set dialog result for parent to read
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).__dialogResult = { confirmed: true, layoutId: savedId };

      // Return success config — WizardShell displays the success screen
      return {
        icon: (
          <CheckmarkCircle24Regular
            style={{ fontSize: "48px", color: tokens.colorStatusSuccessForeground1 }}
          />
        ),
        title: "Workspace Saved",
        body: (
          <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
            Your workspace layout <strong>{workspaceName.trim()}</strong> has been saved successfully.
          </Text>
        ),
        actions: (
          <Button appearance="primary" onClick={() => window.close()}>
            Close
          </Button>
        ),
      };
    } catch (err: unknown) {
      // Handle typed errors from authenticatedFetch (ApiError with status)
      const apiErr = err as { status?: number; message?: string };
      if (apiErr.status === 409) {
        throw new Error("Maximum 10 workspaces reached. Please delete an existing workspace before creating a new one.");
      } else if (apiErr.status === 400) {
        throw new Error(apiErr.message ?? "Validation error. Please check your inputs and try again.");
      } else if (apiErr.status === 403) {
        throw new Error("This layout cannot be modified.");
      } else if (apiErr.status === 404) {
        throw new Error("The layout was not found. It may have been deleted.");
      } else {
        throw new Error(apiErr.message ?? "Failed to save workspace layout. Please try again.");
      }
    }
  }, [mode, layoutId, selectedTemplateId, sectionAssignments, workspaceName, isDefault, authenticatedFetch]);

  // ---------------------------------------------------------------------------
  // WizardShell step configurations
  // ---------------------------------------------------------------------------

  const wizardTitle =
    mode === "edit"
      ? "Edit Workspace Layout"
      : mode === "saveAs"
        ? "Save Layout As..."
        : "Create Workspace Layout";

  const steps: IWizardStepConfig[] = React.useMemo(
    () => [
      {
        id: STEP_CHOOSE_LAYOUT,
        label: "Choose Layout",
        canAdvance: () => selectedTemplateId !== null,
        renderContent: () => (
          <TemplateStep
            selectedTemplateId={selectedTemplateId}
            onSelect={setSelectedTemplateId}
          />
        ),
      },
      {
        id: STEP_CONFIGURE_SECTIONS,
        label: "Configure Sections",
        canAdvance: () => selectedSectionIds.size > 0,
        renderContent: () => (
          <SectionStep
            sections={SECTION_CATALOG}
            selectedIds={selectedSectionIds}
            slotCount={slotCount}
            onToggle={handleSectionToggle}
          />
        ),
      },
      {
        id: STEP_REVIEW_SAVE,
        label: "Review & Save",
        canAdvance: () => workspaceName.trim().length > 0,
        renderContent: () =>
          selectedTemplateId ? (
            <ArrangeStep
              templateId={selectedTemplateId}
              selectedSections={selectedSections}
              sectionAssignments={sectionAssignments}
              workspaceName={workspaceName}
              isDefault={isDefault}
              onAssignmentsChange={setSectionAssignments}
              onNameChange={setWorkspaceName}
              onDefaultChange={setIsDefault}
            />
          ) : null,
      },
    ],
    [
      selectedTemplateId,
      selectedSectionIds,
      slotCount,
      handleSectionToggle,
      selectedSections,
      sectionAssignments,
      workspaceName,
      isDefault,
    ],
  );

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <WizardShell
        ref={wizardRef}
        open={true}
        embedded={true}
        hideTitle={true}
        ariaLabel={wizardTitle}
        steps={steps}
        onClose={() => {
          // Close the navigateTo dialog — try parent Xrm first, then window.close
          try {
            const xrm =
              (window as any).Xrm ??
              (window.parent as any)?.Xrm ??
              (window.top as any)?.Xrm;
            if (xrm?.Navigation?.navigateTo) {
              // Set cancelled result and close
              (window as any).__dialogResult = { confirmed: false };
              window.close();
            } else {
              window.close();
            }
          } catch {
            window.close();
          }
        }}
        onFinish={handleFinish}
        finishLabel="Save Layout"
        finishingLabel="Saving..."
      />
    </FluentProvider>
  );
};
