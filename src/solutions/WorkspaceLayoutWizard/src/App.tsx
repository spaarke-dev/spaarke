/**
 * Workspace Layout Wizard - Root Application Component
 *
 * Standalone wizard dialog for creating, editing, or cloning workspace section layouts.
 * Opened via Xrm.Navigation.navigateTo as a webresource dialog.
 *
 * Wizard Steps:
 *   Step 1: Choose Layout - Select template or start blank
 *   Step 2: Configure Sections - Add/remove/reorder sections
 *   Step 3: Review & Save - Preview layout and confirm
 *
 * @see ADR-026 - Full-Page Custom Page Standard
 * @see ADR-021 - Fluent UI v9 Design System
 */

import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowLeft24Regular,
  ArrowRight24Regular,
  Checkmark24Regular,
} from "@fluentui/react-icons";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { TemplateStep, SectionStep, ArrangeStep, buildInitialAssignments } from "./steps";
import type { SectionCatalogItem, SlotAssignments } from "./steps";
import type { LayoutTemplateId } from "@spaarke/ui-components";
import { getLayoutTemplate } from "@spaarke/ui-components";
import type { WizardMode } from "./main";
import {
  RocketRegular,
  DataBarVerticalRegular,
  ClockRegular,
  CheckmarkCircleRegular,
  DocumentRegular,
} from "@fluentui/react-icons";

/** Parsed LayoutJson row — mirrors the BFF LayoutJsonRow shape for sectionsJson parsing. */
interface LayoutJsonRow {
  id: string;
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

/** Wizard step definition */
interface WizardStep {
  key: string;
  title: string;
  description: string;
}

const WIZARD_STEPS: WizardStep[] = [
  {
    key: "choose-layout",
    title: "Choose Layout",
    description: "Select a layout template or start from a blank canvas.",
  },
  {
    key: "configure-sections",
    title: "Configure Sections",
    description: "Add, remove, and reorder sections in your workspace layout.",
  },
  {
    key: "review-save",
    title: "Review & Save",
    description: "Preview the final layout and save your configuration.",
  },
];

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

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    alignItems: "center",
    padding: "16px 24px",
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    gap: "16px",
  },
  stepIndicator: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  stepDot: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorNeutralStroke1,
  },
  stepDotActive: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
  },
  stepDotCompleted: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
  },
  content: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    padding: "24px",
    gap: "16px",
    overflow: "auto",
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: "16px 24px",
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
  },
});

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
    return { id: row.id, sections };
  });

  return JSON.stringify({ schemaVersion: 1, rows } satisfies LayoutJson);
}

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
  const [currentStep, setCurrentStep] = React.useState(0);
  const [selectedTemplateId, setSelectedTemplateId] =
    React.useState<LayoutTemplateId | null>(saveAsData?.templateId ?? null);
  const [selectedSectionIds, setSelectedSectionIds] = React.useState<
    Set<string>
  >(() => saveAsData?.sectionIds ?? new Set(DEFAULT_SECTION_IDS));
  const [workspaceName, setWorkspaceName] = React.useState(saveAsData?.name ?? "");
  const [isDefault, setIsDefault] = React.useState(false);
  const [sectionAssignments, setSectionAssignments] =
    React.useState<SlotAssignments>(saveAsData?.assignments ?? new Map());
  const [isSaving, setIsSaving] = React.useState(false);
  const [saveError, setSaveError] = React.useState<string | null>(null);

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

  const step = WIZARD_STEPS[currentStep];
  const isFirstStep = currentStep === 0;
  const isLastStep = currentStep === WIZARD_STEPS.length - 1;

  /** Derive the selected section catalog items in stable order. */
  const selectedSections = React.useMemo(
    () => SECTION_CATALOG.filter((s) => selectedSectionIds.has(s.id)),
    [selectedSectionIds],
  );

  /** Next button is disabled on step 0 until a template is chosen,
   *  and on step 1 until at least one section is selected. */
  const isNextDisabled =
    (currentStep === 0 && selectedTemplateId === null) ||
    (currentStep === 1 && selectedSectionIds.size === 0);

  /** Save button is disabled when workspace name is empty. */
  const isSaveDisabled = workspaceName.trim().length === 0;

  /**
   * Advance to the next step. When entering the Arrange step (step 2),
   * auto-initialize slot assignments if they are empty.
   */
  const handleNext = React.useCallback(() => {
    setCurrentStep((prev) => {
      const next = prev + 1;
      if (next === 2 && selectedTemplateId) {
        // Auto-assign sections to slots if no assignments exist yet.
        setSectionAssignments((prevAssignments) => {
          if (prevAssignments.size === 0) {
            return buildInitialAssignments(selectedTemplateId, selectedSections);
          }
          return prevAssignments;
        });
      }
      return next;
    });
  }, [selectedTemplateId, selectedSections]);

  /**
   * Save the wizard state to the BFF API.
   * - Create / SaveAs -> POST /api/workspace/layouts
   * - Edit -> PUT /api/workspace/layouts/{layoutId}
   */
  const handleSave = React.useCallback(async () => {
    if (!selectedTemplateId) return;

    setIsSaving(true);
    setSaveError(null);

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

      // Close dialog and return result to parent
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).__dialogResult = { confirmed: true, layoutId: savedId };
      window.close();
    } catch (err: unknown) {
      // Handle typed errors from authenticatedFetch (ApiError with status)
      const apiErr = err as { status?: number; message?: string };
      if (apiErr.status === 409) {
        setSaveError("Maximum 10 workspaces reached. Please delete an existing workspace before creating a new one.");
      } else if (apiErr.status === 400) {
        setSaveError(apiErr.message ?? "Validation error. Please check your inputs and try again.");
      } else if (apiErr.status === 403) {
        setSaveError("This layout cannot be modified.");
      } else if (apiErr.status === 404) {
        setSaveError("The layout was not found. It may have been deleted.");
      } else {
        setSaveError(
          apiErr.message ?? "Failed to save workspace layout. Please try again."
        );
      }
    } finally {
      setIsSaving(false);
    }
  }, [mode, layoutId, selectedTemplateId, sectionAssignments, workspaceName, isDefault, authenticatedFetch]);

  const headerTitle =
    mode === "edit"
      ? "Edit Workspace Layout"
      : mode === "saveAs"
        ? "Save Layout As..."
        : "Create Workspace Layout";

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <div className={useStyles().root}>
        {/* Header with step indicator */}
        <div className={useStyles().header}>
          <Text weight="semibold" size={500}>
            {headerTitle}
          </Text>
          <div className={useStyles().stepIndicator}>
            {WIZARD_STEPS.map((s, i) => (
              <div
                key={s.key}
                className={
                  i === currentStep
                    ? useStyles().stepDotActive
                    : i < currentStep
                      ? useStyles().stepDotCompleted
                      : useStyles().stepDot
                }
              />
            ))}
          </div>
        </div>

        {/* Step content */}
        <div className={useStyles().content}>
          {currentStep === 0 ? (
            <TemplateStep
              selectedTemplateId={selectedTemplateId}
              onSelect={setSelectedTemplateId}
            />
          ) : currentStep === 1 ? (
            <SectionStep
              sections={SECTION_CATALOG}
              selectedIds={selectedSectionIds}
              slotCount={slotCount}
              onToggle={handleSectionToggle}
            />
          ) : currentStep === 2 && selectedTemplateId ? (
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
          ) : null}
        </div>

        {/* Inline error display for save failures */}
        {saveError && (
          <div style={{ padding: "0 24px" }}>
            <MessageBar intent="error">
              <MessageBarBody>{saveError}</MessageBarBody>
            </MessageBar>
          </div>
        )}

        {/* Footer with navigation buttons */}
        <div className={useStyles().footer}>
          <Button
            appearance="subtle"
            icon={<ArrowLeft24Regular />}
            disabled={isFirstStep}
            onClick={() => setCurrentStep((s) => s - 1)}
          >
            Back
          </Button>
          {isLastStep ? (
            <Button
              appearance="primary"
              icon={isSaving ? <Spinner size="tiny" /> : <Checkmark24Regular />}
              iconPosition="after"
              disabled={isSaveDisabled || isSaving}
              onClick={handleSave}
            >
              {isSaving ? "Saving..." : "Save Layout"}
            </Button>
          ) : (
            <Button
              appearance="primary"
              icon={<ArrowRight24Regular />}
              iconPosition="after"
              disabled={isNextDisabled}
              onClick={handleNext}
            >
              Next
            </Button>
          )}
        </div>
      </div>
    </FluentProvider>
  );
};
