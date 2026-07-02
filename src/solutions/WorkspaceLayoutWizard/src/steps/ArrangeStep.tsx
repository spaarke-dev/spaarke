/**
 * Wizard Step 3 — Arrange Sections & Name Workspace
 *
 * Renders the selected template's grid layout with drag-and-drop section cards.
 * Users drag section cards between grid slots and an unassigned area to arrange
 * their workspace. Includes workspace name input and "Set as default" checkbox.
 *
 * Uses HTML5 Drag and Drop API — no external library required for slot swapping.
 *
 * @see ADR-021 - Fluent UI v9 Design System
 * @see ADR-012 - Shared component library
 */

import * as React from "react";
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Checkbox,
  Combobox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Dropdown,
  Input,
  Label,
  Option,
  SpinButton,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import type { SpinButtonChangeEvent, SpinButtonOnChangeData } from "@fluentui/react-components";
import {
  ReOrderDotsVertical24Regular,
  Info16Regular,
  DismissRegular,
  PinRegular,
  QuestionCircle16Regular,
  Settings16Regular,
  Warning16Regular,
} from "@fluentui/react-icons";
import {
  getLayoutTemplate,
  SECTION_METADATA_CATALOG,
  type LayoutTemplateId,
  type LayoutTemplateRow,
} from "@spaarke/ui-components";
import type { SectionCatalogItem } from "./SectionStep";

// ---------------------------------------------------------------------------
// FR-04 (task 015) — widthPreference lookup
//
// `SECTION_METADATA_CATALOG` is the single source of truth for widthPreference
// (added to `SectionMetadata` by task 014). App.tsx's derived `SECTION_CATALOG`
// intentionally drops widthPreference to keep the wizard's local
// `SectionCatalogItem` shape minimal, so we re-lookup from the catalog here.
//
// Absent widthPreference → treated as `'any'` (no wizard warnings). Matches
// SectionMetadata's documented default.
// ---------------------------------------------------------------------------
function lookupWidthPreference(
  sectionId: string,
): "full" | "half" | "any" {
  const meta = SECTION_METADATA_CATALOG.find((m) => m.id === sectionId);
  return meta?.widthPreference ?? "any";
}

// ---------------------------------------------------------------------------
// DEF-002 / DEF-003 (spaarke-dataset-grid-framework-r2) — entity-name lookup
//
// `SectionMetadata.entityName` is the logical name of the Dataverse entity a
// section renders (populated on the 6 entity-list sections: documents, matters,
// projects, invoices, work-assignments, communications). Used by the Advanced
// panel to hydrate the configId picker + availableViews picker via BFF.
//
// Non-entity-list sections (dailyBriefing, quickSummary, getStarted, etc.)
// return `undefined` and the pickers gracefully collapse to their no-data
// state instead of triggering a network round-trip.
// ---------------------------------------------------------------------------
function lookupEntityName(sectionId: string): string | undefined {
  const meta = SECTION_METADATA_CATALOG.find((m) => m.id === sectionId);
  return meta?.entityName;
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Map of slot key (e.g., "row-1:0") to section ID. */
export type SlotAssignments = Map<string, string>;

/**
 * Per-instance override shape for a section placed in a layout row.
 *
 * Wizard-local mirror of `SectionInstance` from
 * `@spaarke/ui-components/components/WorkspaceShell/buildDynamicWorkspaceConfig`
 * (task 012 schema half). Lives here (rather than in App.tsx) to break a
 * potential circular import between App.tsx and this file. Both `App.tsx`
 * (for parseSectionsJson + buildSectionsJson state) and `ArrangeStep.tsx`
 * (for the Advanced accordion prop shape) reference this type.
 *
 * Spec: FR-03 (spaarke-dataset-grid-framework-r2, task 013 UI half).
 */
export interface SectionInstance {
  /** Registration ID (matches the bare-string form). */
  id: string;
  /**
   * Optional. Override the section's baked-in `configId` for this placement
   * only — enables one registration to render different `sprk_gridconfiguration`
   * records per placement.
   */
  configIdOverride?: string;
  /**
   * Optional. Override the section header title in this layout only.
   * Underlying `SectionMetadata.label` is unchanged.
   */
  label?: string;
  /**
   * Per-instance DataGrid behavior overrides.
   */
  overrides?: {
    /** Effective `pageSize` — takes precedence over config record's `behavior.pageSize`. */
    pageSize?: number;
    /**
     * Effective savedquery allowlist — REPLACES config-level
     * `SourceSavedQuery.availableViews` (FR-05) when both are set.
     */
    availableViews?: string[];
  };
}

export interface ArrangeStepProps {
  /** Selected template ID from Step 1. */
  templateId: LayoutTemplateId;
  /** Sections selected in Step 2, with catalog metadata. */
  selectedSections: SectionCatalogItem[];
  /** Current slot assignments: slot key -> section ID. */
  sectionAssignments: SlotAssignments;
  /** Workspace name. */
  workspaceName: string;
  /** Whether this workspace is set as default. */
  isDefault: boolean;
  /**
   * Whether this workspace should be pinned to auto-open on next SpaarkeAi load.
   * Task 092 / round 5 — operator-driven UX. Persistence wiring lives in App.tsx
   * (multi-pin `localStorage` list `spaarke:workspace:pinned-list`, shared with
   * SpaarkeAi's `services/pinnedWorkspaces.ts`; BFF user-preferences endpoint
   * remains a follow-on once such an endpoint exists).
   */
  pinToStart: boolean;
  /** Callback when slot assignments change. */
  onAssignmentsChange: (assignments: SlotAssignments) => void;
  /** Callback when workspace name changes. */
  onNameChange: (name: string) => void;
  /** Callback when default checkbox changes. */
  onDefaultChange: (isDefault: boolean) => void;
  /** Callback when pin-to-start checkbox changes. */
  onPinToStartChange: (pinToStart: boolean) => void;
  /**
   * FR-02 (task 011) — Per-row optional height ceiling, keyed by row.id
   * (matching the template row IDs from `LAYOUT_TEMPLATES`). Empty map means
   * every row uses the framework default (grow to fit sections). Rendered as
   * a Fluent v9 `Dropdown` with 5 presets + "Custom…" per row.
   */
  rowHeights: Map<string, string>;
  /** FR-02 (task 011) — Callback when the row-height map changes. */
  onRowHeightsChange: (rowHeights: Map<string, string>) => void;
  /**
   * FR-03 (task 013) — Per-slot SectionInstance overrides, keyed by slotKey
   * (`${row.id}:${col}`). Empty map means every placed section serializes as a
   * bare string (back-compat). Rendered as a Fluent v9 `Accordion` "Advanced"
   * panel under each filled slot with 4 controls: configId, label, pageSize,
   * availableViews. See {@link AdvancedSectionControl}.
   */
  sectionInstances: Map<string, SectionInstance>;
  /** FR-03 (task 013) — Callback when the section-instances map changes. */
  onSectionInstancesChange: (sectionInstances: Map<string, SectionInstance>) => void;
  /**
   * DEF-002 / DEF-003 (spaarke-dataset-grid-framework-r2) — authenticated fetch
   * used by the Advanced panel to hydrate the configId picker + availableViews
   * picker via BFF. Passed through from App.tsx (which receives it from
   * @spaarke/auth). When a section has no `entityName` metadata, the pickers
   * short-circuit without a network call.
   */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
}

// ---------------------------------------------------------------------------
// FR-02 (task 011) — Row-height presets
//
// Values chosen from the design's common cases (spec.md § FR-02):
//   - `""` (empty)          → "Auto (default)" — DOES NOT emit `rowHeight` to JSON
//   - `"40vh"`               → small
//   - `"60vh"`               → medium
//   - `"80vh"`               → large
//   - `"100vh"`              → full-viewport
//   - `"__custom__"` sentinel → reveal an `Input` for arbitrary CSS length
//
// The dropdown value key is separate from the emitted CSS value so that
// switching to "Custom…" doesn't accidentally emit the sentinel — the map
// only stores the CSS value (or nothing, for "Auto").
// ---------------------------------------------------------------------------

const ROW_HEIGHT_AUTO_KEY = "__auto__" as const;
const ROW_HEIGHT_CUSTOM_KEY = "__custom__" as const;

interface RowHeightPreset {
  key: string;
  label: string;
  /** CSS length to emit; `undefined` for "Auto" (omit field entirely). */
  value: string | undefined;
}

const ROW_HEIGHT_PRESETS: readonly RowHeightPreset[] = [
  { key: ROW_HEIGHT_AUTO_KEY, label: "Auto (default)", value: undefined },
  { key: "40vh", label: "40vh (small)", value: "40vh" },
  { key: "60vh", label: "60vh (medium)", value: "60vh" },
  { key: "80vh", label: "80vh (large)", value: "80vh" },
  { key: "100vh", label: "100vh (full-viewport)", value: "100vh" },
  { key: ROW_HEIGHT_CUSTOM_KEY, label: "Custom…", value: undefined },
];

/**
 * Map a stored CSS value (from the row-heights map) to the dropdown key + label
 * that should be displayed. Any value NOT in the preset list is treated as
 * "Custom…" and the custom input is revealed.
 */
function resolveRowHeightPreset(
  storedValue: string | undefined,
): { key: string; label: string; isCustom: boolean } {
  if (!storedValue || storedValue.length === 0) {
    return { key: ROW_HEIGHT_AUTO_KEY, label: "Auto (default)", isCustom: false };
  }
  const preset = ROW_HEIGHT_PRESETS.find((p) => p.value === storedValue);
  if (preset) {
    return { key: preset.key, label: preset.label, isCustom: false };
  }
  return { key: ROW_HEIGHT_CUSTOM_KEY, label: "Custom…", isCustom: true };
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a slot key from row ID and column index. */
function slotKey(rowId: string, colIndex: number): string {
  return `${rowId}:${colIndex}`;
}

/**
 * Build the initial auto-assignment: fill template slots sequentially
 * with the selected sections.
 */
export function buildInitialAssignments(
  templateId: LayoutTemplateId,
  selectedSections: SectionCatalogItem[],
): SlotAssignments {
  const template = getLayoutTemplate(templateId);
  if (!template) return new Map();

  const assignments: SlotAssignments = new Map();
  let sectionIndex = 0;

  for (const row of template.rows) {
    for (let col = 0; col < row.slotCount; col++) {
      if (sectionIndex < selectedSections.length) {
        assignments.set(slotKey(row.id, col), selectedSections[sectionIndex].id);
        sectionIndex++;
      }
    }
  }

  return assignments;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    width: "100%",
    maxWidth: "780px",
    alignSelf: "center",
  },
  formRow: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  checkboxRow: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    paddingTop: "4px",
  },
  gridContainer: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
  },
  // FR-02 (task 011) — wraps each grid row + its per-row settings header.
  rowGroup: {
    display: "flex",
    flexDirection: "column",
    gap: "6px",
  },
  // FR-02 (task 011) — per-row settings header (row-height dropdown + tooltip).
  rowSettingsHeader: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    flexWrap: "wrap",
    // Semantic token — adapts to light/dark automatically per ADR-021.
    color: tokens.colorNeutralForeground2,
  },
  rowSettingsLabel: {
    // Semantic token for row label — adapts to dark mode.
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  rowHeightDropdown: {
    minWidth: "200px",
  },
  rowHeightCustomInput: {
    minWidth: "140px",
  },
  helpIcon: {
    // Semantic token — adapts to dark mode; visually secondary.
    color: tokens.colorNeutralForeground3,
    display: "flex",
    alignItems: "center",
    cursor: "help",
  },
  gridRow: {
    display: "grid",
    gap: "8px",
  },
  slot: {
    position: "relative",
    minHeight: "72px",
    ...shorthands.padding("12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    border: `2px dashed ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    transitionProperty: "border-color, background-color",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  slotRemoveButton: {
    position: "absolute",
    top: "4px",
    right: "4px",
    width: "20px",
    height: "20px",
    minWidth: "20px",
    minHeight: "20px",
    ...shorthands.padding("0"),
    ...shorthands.borderRadius(tokens.borderRadiusCircular),
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    border: "none",
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    cursor: "pointer",
    opacity: 0,
    transitionProperty: "opacity, background-color",
    transitionDuration: tokens.durationFast,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground3Hover,
      color: tokens.colorNeutralForeground1,
    },
    ":focus": {
      opacity: 1,
      outline: `2px solid ${tokens.colorStrokeFocus2}`,
    },
  },
  slotRemoveButtonVisible: {
    opacity: 1,
  },
  slotDragOver: {
    border: `2px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
  },
  slotFilled: {
    border: `2px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "grab",
  },
  slotPlaceholder: {
    color: tokens.colorNeutralForeground4,
  },
  sectionCard: {
    display: "flex",
    alignItems: "center",
    gap: "10px",
    width: "100%",
    userSelect: "none",
  },
  dragHandle: {
    display: "flex",
    alignItems: "center",
    color: tokens.colorNeutralForeground4,
    cursor: "grab",
    flexShrink: 0,
    opacity: 0,
    transitionProperty: "opacity",
    transitionDuration: tokens.durationFast,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  dragHandleVisible: {
    opacity: 1,
  },
  sectionIcon: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "24px",
    height: "24px",
    flexShrink: 0,
    color: tokens.colorNeutralForeground2,
  },
  unassignedArea: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    ...shorthands.padding("12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    border: `2px dashed ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground3,
    minHeight: "48px",
  },
  unassignedAreaDragOver: {
    border: `2px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
  },
  unassignedList: {
    display: "flex",
    flexWrap: "wrap",
    gap: "8px",
  },
  unassignedCard: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "grab",
    userSelect: "none",
    transitionProperty: "box-shadow",
    transitionDuration: tokens.durationFast,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      boxShadow: tokens.shadow4,
    },
  },
  overflowNote: {
    display: "flex",
    alignItems: "center",
    gap: "6px",
    paddingTop: "4px",
    color: tokens.colorNeutralForeground3,
  },
  // FR-03 (task 013) — Advanced accordion wrapper below each filled slot.
  // Sits inside the row's grid column so accordions align with their slots.
  advancedAccordion: {
    marginTop: "6px",
    // Semantic token for subtle background so the accordion visually groups
    // with the slot without competing with the drag-drop card above it.
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  advancedHeader: {
    // Accordion header padding + typography — Fluent v9 defaults are fine;
    // just ensure the icon aligns cleanly.
    color: tokens.colorNeutralForeground2,
  },
  advancedPanel: {
    display: "flex",
    flexDirection: "column",
    gap: "12px",
    // Panel padding — Fluent v9 AccordionPanel has minimal built-in padding.
    ...shorthands.padding("12px"),
  },
  advancedField: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  advancedFieldLabel: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  advancedFieldHelp: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  advancedHelpBlock: {
    // Semantic token — adapts to dark mode automatically.
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    paddingTop: "4px",
  },
  // FR-04 (task 015) — Warning icon overlay on a section chip when the
  // section's widthPreference conflicts with its row slot count. The icon
  // sits absolutely-positioned in the slot's top-left so it doesn't compete
  // with the remove button (top-right). Fluent v9 palette + Tooltip wrap.
  widthWarningIcon: {
    position: "absolute",
    top: "4px",
    left: "4px",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    color: tokens.colorPaletteYellowForeground1,
    cursor: "help",
    // Keep the icon interactive for tooltip hover but don't interfere with
    // the slot's drag behavior.
    pointerEvents: "auto",
  },
});

// ---------------------------------------------------------------------------
// DraggableSectionCard — compact card with icon + label (inline in slot)
// ---------------------------------------------------------------------------

const DraggableSectionCard: React.FC<{
  section: SectionCatalogItem;
  isHovered: boolean;
}> = ({ section, isHovered }) => {
  const classes = useStyles();
  const IconComponent = section.icon;

  return (
    <div className={classes.sectionCard}>
      <div
        className={mergeClasses(
          classes.dragHandle,
          isHovered && classes.dragHandleVisible,
        )}
      >
        <ReOrderDotsVertical24Regular />
      </div>
      <div className={classes.sectionIcon}>
        <IconComponent />
      </div>
      <Text weight="semibold" size={300}>
        {section.label}
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// GridSlot — single drop target in the template grid
// ---------------------------------------------------------------------------

const GridSlot: React.FC<{
  slotId: string;
  section: SectionCatalogItem | undefined;
  onDrop: (slotId: string, sectionId: string) => void;
  onDragStart: (sectionId: string, sourceSlotId: string) => void;
  /**
   * Remove the section from this slot (Fix 2 / task 091).
   * Returns the section to the unassigned pool. The slot becomes empty;
   * the save flow tolerates empty slots — fix 1 / task 091.
   */
  onRemove: (slotId: string) => void;
  /**
   * FR-04 (task 015) — Optional widthPreference warning to render as a small
   * Fluent v9 Warning icon in the slot's top-left, wrapped in a Tooltip
   * explaining the mismatch. `undefined` means no warning.
   *
   * Rendered only for FILLED slots (empty slots can't have a preference mismatch).
   */
  widthWarning?: string;
}> = ({ slotId, section, onDrop, onDragStart, onRemove, widthWarning }) => {
  const classes = useStyles();
  const [isDragOver, setIsDragOver] = React.useState(false);
  const [isHovered, setIsHovered] = React.useState(false);

  const handleDragOver = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.dataTransfer.dropEffect = "move";
      if (!isDragOver) setIsDragOver(true);
    },
    [isDragOver],
  );

  const handleDragLeave = React.useCallback(() => {
    setIsDragOver(false);
  }, []);

  const handleDrop = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);
      const sectionId = e.dataTransfer.getData("text/plain");
      if (sectionId) {
        onDrop(slotId, sectionId);
      }
    },
    [slotId, onDrop],
  );

  const handleDragStart = React.useCallback(
    (e: React.DragEvent) => {
      if (!section) return;
      e.dataTransfer.setData("text/plain", section.id);
      e.dataTransfer.setData("application/x-source-slot", slotId);
      e.dataTransfer.effectAllowed = "move";
      // Notify parent of drag source for swap logic
      onDragStart(section.id, slotId);
    },
    [section, slotId, onDragStart],
  );

  const handleRemoveClick = React.useCallback(
    (e: React.MouseEvent) => {
      // Stop propagation so the click doesn't initiate a drag or focus the slot.
      e.stopPropagation();
      e.preventDefault();
      onRemove(slotId);
    },
    [slotId, onRemove],
  );

  // Prevent the remove button from initiating an HTML5 drag on the parent slot.
  const handleRemoveMouseDown = React.useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
  }, []);

  return (
    <div
      className={mergeClasses(
        classes.slot,
        isDragOver && classes.slotDragOver,
        section != null && classes.slotFilled,
      )}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      draggable={section != null}
      onDragStart={handleDragStart}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      {section ? (
        <>
          <DraggableSectionCard section={section} isHovered={isHovered} />
          {widthWarning && (
            <Tooltip
              content={widthWarning}
              relationship="description"
              withArrow
            >
              <span
                className={classes.widthWarningIcon}
                role="img"
                aria-label={widthWarning}
                data-testid={`slot-width-warning-${slotId}`}
              >
                <Warning16Regular />
              </span>
            </Tooltip>
          )}
          <button
            type="button"
            className={mergeClasses(
              classes.slotRemoveButton,
              isHovered && classes.slotRemoveButtonVisible,
            )}
            onClick={handleRemoveClick}
            onMouseDown={handleRemoveMouseDown}
            aria-label={`Remove ${section.label} from this slot`}
            title={`Remove ${section.label}`}
            data-testid={`slot-remove-${slotId}`}
            draggable={false}
          >
            <DismissRegular fontSize={14} />
          </button>
        </>
      ) : (
        <Text className={classes.slotPlaceholder} size={200}>
          Drop section here
        </Text>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// UnassignedSectionCard — compact chip for the unassigned area
// ---------------------------------------------------------------------------

const UnassignedSectionCard: React.FC<{
  section: SectionCatalogItem;
}> = ({ section }) => {
  const classes = useStyles();
  const IconComponent = section.icon;

  const handleDragStart = React.useCallback(
    (e: React.DragEvent) => {
      e.dataTransfer.setData("text/plain", section.id);
      e.dataTransfer.setData("application/x-source-slot", "unassigned");
      e.dataTransfer.effectAllowed = "move";
    },
    [section.id],
  );

  return (
    <div className={classes.unassignedCard} draggable onDragStart={handleDragStart}>
      <div className={classes.sectionIcon}>
        <IconComponent />
      </div>
      <Text weight="semibold" size={200}>
        {section.label}
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// RowHeightControl — per-row settings header (FR-02 / task 011)
//
// Fluent v9 Dropdown with 5 presets + "Custom…" + tooltip. When "Custom…" is
// selected, a Fluent v9 Input for arbitrary CSS length is revealed. All
// colors use semantic tokens (ADR-021) for dark-mode compliance.
// ---------------------------------------------------------------------------

const RowHeightControl: React.FC<{
  rowIndex: number;
  rowId: string;
  currentValue: string | undefined;
  onChange: (rowId: string, newValue: string | undefined) => void;
}> = ({ rowIndex, rowId, currentValue, onChange }) => {
  const classes = useStyles();
  const preset = resolveRowHeightPreset(currentValue);

  // The custom-input reveal is derived from the resolved preset. When the
  // stored value is a preset value, `isCustom` is false and the input hides.
  // When the operator selects "Custom…" explicitly, we transition by setting
  // the map value to "" (blank string sentinel), which resolves to Custom but
  // still emits nothing (blank rowHeight is filtered out in buildSectionsJson).
  const [showCustomInput, setShowCustomInput] = React.useState<boolean>(
    preset.isCustom,
  );

  // Keep local reveal state in sync when the parent updates the stored value
  // (e.g., saveAs prefill of a custom-height row).
  React.useEffect(() => {
    if (preset.isCustom) setShowCustomInput(true);
  }, [preset.isCustom]);

  const handleDropdownChange = React.useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      const key = data.optionValue;
      if (!key) return;
      if (key === ROW_HEIGHT_AUTO_KEY) {
        // Clear the entry — buildSectionsJson will omit the field entirely.
        onChange(rowId, undefined);
        setShowCustomInput(false);
        return;
      }
      if (key === ROW_HEIGHT_CUSTOM_KEY) {
        // Reveal the custom input but do NOT yet commit a value; the user
        // will type one. Until they type, map stays at previous non-preset
        // custom value (if any) OR blank.
        setShowCustomInput(true);
        // Preserve any existing custom value; otherwise reset to blank.
        if (!preset.isCustom) onChange(rowId, "");
        return;
      }
      // Preset selection: commit the CSS value.
      const chosen = ROW_HEIGHT_PRESETS.find((p) => p.key === key);
      if (chosen?.value) {
        onChange(rowId, chosen.value);
        setShowCustomInput(false);
      }
    },
    [rowId, onChange, preset.isCustom],
  );

  const handleCustomInputChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      // Empty string stays in the map as "" — buildSectionsJson trims + skips.
      onChange(rowId, data.value);
    },
    [rowId, onChange],
  );

  return (
    <div className={classes.rowSettingsHeader}>
      <Text size={200} className={classes.rowSettingsLabel}>
        Row {rowIndex + 1}
      </Text>
      <Label htmlFor={`row-height-${rowId}`} size="small">
        Row height
      </Label>
      <Dropdown
        id={`row-height-${rowId}`}
        className={classes.rowHeightDropdown}
        size="small"
        value={preset.label}
        selectedOptions={[preset.key]}
        onOptionSelect={handleDropdownChange}
        aria-label={`Row ${rowIndex + 1} height`}
        data-testid={`row-height-dropdown-${rowId}`}
      >
        {ROW_HEIGHT_PRESETS.map((p) => (
          <Option key={p.key} value={p.key}>
            {p.label}
          </Option>
        ))}
      </Dropdown>
      {showCustomInput && (
        <Input
          className={classes.rowHeightCustomInput}
          size="small"
          appearance="outline"
          value={preset.isCustom ? currentValue ?? "" : ""}
          onChange={handleCustomInputChange}
          placeholder="e.g., 640px or 70vh"
          aria-label={`Row ${rowIndex + 1} custom height`}
          data-testid={`row-height-custom-input-${rowId}`}
        />
      )}
      <Tooltip
        content="Sets the row's max-height. Sections inside respect this ceiling regardless of their own contentSizing. Leave as Auto to let sections decide their own height."
        relationship="description"
        withArrow
      >
        <span
          className={classes.helpIcon}
          role="img"
          aria-label="Row height help"
          data-testid={`row-height-help-${rowId}`}
        >
          <QuestionCircle16Regular />
        </span>
      </Tooltip>
    </div>
  );
};

// ---------------------------------------------------------------------------
// FR-03 (task 013) — Advanced accordion per placed section
//
// One expandable Fluent v9 Accordion under each filled slot. Contains 4
// controls that write into the wizard's per-slot `SectionInstance` state map:
//
//   (a) configId picker (Dropdown)             — SectionInstance.configIdOverride
//   (b) label override    (Input)              — SectionInstance.label
//   (c) pageSize override (SpinButton)         — SectionInstance.overrides.pageSize
//   (d) availableViews    (Input, comma-list)  — SectionInstance.overrides.availableViews
//
// Empty/default fields = "no override" — buildSectionsJson (App.tsx) emits the
// section as a bare string (back-compat with every pre-R2 published record).
// Any single field set = SectionInstance object emitted.
//
// DEF-002 / DEF-003 (spaarke-dataset-grid-framework-r2) — RESOLVED. The
// configId picker + availableViews field now query Dataverse via BFF:
//   - configId picker → GET /api/dataverse/gridconfigurations/{entity}
//     (new endpoint added by DEF-002)
//   - availableViews  → GET /api/dataverse/savedqueries/{entity}
//     (existing endpoint from spaarke-datagrid-framework-r1 FR-BFF-02)
//
// Both endpoints resolve `entity` from `SectionMetadata.entityName` (populated
// on the 6 entity-list sections). Non-entity-list sections short-circuit both
// pickers and fall back to helpful "no override applicable" copy.
//
// The pickers use `useEntityPickerData` (below) which caches results per-entity
// for the lifetime of the wizard step, avoiding refetches when the operator
// expands multiple Advanced accordions.
// ---------------------------------------------------------------------------

/** Configuration picker option — mirrors GridConfigurationSummaryDto from BFF. */
interface ConfigIdOption {
  key: string; // "" = None (clears the override)
  label: string;
  isDefault?: boolean;
}

/** Saved-query picker option — mirrors SavedQuerySummaryDto from BFF. */
interface SavedQueryOption {
  id: string;
  name: string;
  isDefault: boolean;
}

const CONFIG_ID_NONE_KEY = "";
const CONFIG_ID_NONE_OPTION: ConfigIdOption = {
  key: CONFIG_ID_NONE_KEY,
  label: "None (use default)",
};

/**
 * BFF response shape for GET /api/dataverse/gridconfigurations/{entity}.
 * Mirrors GridConfigurationSummaryDto — payload names are camelCase per the
 * BFF's PropertyNamingPolicy.CamelCase serializer.
 */
interface GridConfigurationSummaryPayload {
  id: string;
  name: string;
  entityLogicalName: string;
  isDefault: boolean;
  sortOrder: number;
}

/**
 * BFF response shape for GET /api/dataverse/savedqueries/{entity}.
 * Mirrors SavedQuerySummaryDto (existing endpoint from framework R1).
 */
interface SavedQuerySummaryPayload {
  id: string;
  name: string;
  isDefault: boolean;
  queryType: number;
}

/**
 * Loader state shared by both pickers.
 */
type PickerLoadState<T> =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; data: T }
  | { status: "error"; message: string };

/**
 * Cached picker data keyed by entityLogicalName. Lives at the ArrangeStep
 * component level and is threaded through AdvancedSectionControl so multiple
 * expanded accordions for the same entity share one fetch.
 */
interface EntityPickerCache {
  configs: Map<string, PickerLoadState<ConfigIdOption[]>>;
  savedQueries: Map<string, PickerLoadState<SavedQueryOption[]>>;
}

/**
 * Fetch grid configurations for an entity via BFF. Returns [None + fetched].
 * Errors surface as a picker with only "None (use default)" available so
 * makers can still edit the layout — DEF-002 explicitly requires graceful
 * degradation, not a hard block.
 */
async function fetchGridConfigurations(
  entityLogicalName: string,
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>,
  signal: AbortSignal,
): Promise<ConfigIdOption[]> {
  const url = `/api/dataverse/gridconfigurations/${encodeURIComponent(entityLogicalName)}`;
  const res = await authenticatedFetch(url, { signal });
  if (!res.ok) {
    throw new Error(
      `Failed to load grid configurations for ${entityLogicalName} (HTTP ${res.status})`,
    );
  }
  const payload = (await res.json()) as GridConfigurationSummaryPayload[];
  // Prepend "None" so it's always available. Backend already sorts by
  // sortOrder then name; preserve that ordering.
  const options: ConfigIdOption[] = [
    CONFIG_ID_NONE_OPTION,
    ...payload.map((c) => ({
      key: c.id,
      label: c.isDefault ? `${c.name} (default)` : c.name,
      isDefault: c.isDefault,
    })),
  ];
  return options;
}

/**
 * Fetch savedqueries for an entity via BFF. Returns the raw list; the picker
 * ignores queryType (both user-owned = 0 and main app view = 64 are
 * user-selectable in the availableViews allowlist).
 */
async function fetchSavedQueries(
  entityLogicalName: string,
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>,
  signal: AbortSignal,
): Promise<SavedQueryOption[]> {
  const url = `/api/dataverse/savedqueries/${encodeURIComponent(entityLogicalName)}`;
  const res = await authenticatedFetch(url, { signal });
  if (!res.ok) {
    throw new Error(
      `Failed to load saved queries for ${entityLogicalName} (HTTP ${res.status})`,
    );
  }
  const payload = (await res.json()) as SavedQuerySummaryPayload[];
  return payload.map((s) => ({
    id: s.id,
    name: s.name,
    isDefault: s.isDefault,
  }));
}

/**
 * Read the SectionInstance for a slot from the wizard's map, or return an
 * empty stub `{ id: sectionId }` for the "no override yet" state. Used by
 * the Advanced accordion controls as their controlled-value source.
 */
function readInstanceForSlot(
  sectionInstances: Map<string, SectionInstance>,
  slotKey: string,
  sectionId: string,
): SectionInstance {
  const existing = sectionInstances.get(slotKey);
  if (existing) return existing;
  return { id: sectionId };
}

/**
 * Detect whether a SectionInstance has any override set. Mirrors the emit-time
 * logic in App.tsx's `hasAnyOverride` — used here to decide whether the map
 * needs to store the instance or delete it (empty instance = bare string
 * back-compat).
 */
function sectionInstanceHasAnyOverride(instance: SectionInstance): boolean {
  if (instance.configIdOverride && instance.configIdOverride.length > 0) return true;
  if (instance.label && instance.label.length > 0) return true;
  const o = instance.overrides;
  if (o) {
    if (typeof o.pageSize === "number" && Number.isFinite(o.pageSize)) return true;
    if (Array.isArray(o.availableViews) && o.availableViews.length > 0) return true;
  }
  return false;
}

const AdvancedSectionControl: React.FC<{
  slotKey: string;
  sectionId: string;
  sectionLabel: string;
  sectionInstances: Map<string, SectionInstance>;
  onSectionInstancesChange: (next: Map<string, SectionInstance>) => void;
  /** DEF-002 / DEF-003 — pickers hydrate against BFF via this fetch. */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /** DEF-002 / DEF-003 — shared entity-picker cache across all accordions. */
  pickerCache: EntityPickerCache;
  /** Notify parent when a fetch completes so state re-renders + shared cache updates. */
  onPickerCacheChange: (cache: EntityPickerCache) => void;
}> = ({
  slotKey,
  sectionId,
  sectionLabel,
  sectionInstances,
  onSectionInstancesChange,
  authenticatedFetch,
  pickerCache,
  onPickerCacheChange,
}) => {
  const classes = useStyles();
  const instance = readInstanceForSlot(sectionInstances, slotKey, sectionId);

  // DEF-002 / DEF-003 — resolve entityName + kick off BFF fetch on mount if we
  // haven't fetched for this entity yet. Both pickers share the same cache
  // keyed by entity so parallel expansions of Advanced accordions for the
  // same section don't re-request.
  const entityName = React.useMemo(() => lookupEntityName(sectionId), [sectionId]);

  const configState = entityName
    ? pickerCache.configs.get(entityName) ?? { status: "idle" as const }
    : ({ status: "idle" as const });
  const savedQueriesState = entityName
    ? pickerCache.savedQueries.get(entityName) ?? { status: "idle" as const }
    : ({ status: "idle" as const });

  React.useEffect(() => {
    if (!entityName) return;
    const controller = new AbortController();

    // Fetch grid configurations if not yet started.
    if (configState.status === "idle") {
      // Mark loading in the cache immediately to prevent duplicate parallel fetches.
      const next: EntityPickerCache = {
        configs: new Map(pickerCache.configs),
        savedQueries: new Map(pickerCache.savedQueries),
      };
      next.configs.set(entityName, { status: "loading" });
      onPickerCacheChange(next);

      void (async () => {
        try {
          const options = await fetchGridConfigurations(entityName, authenticatedFetch, controller.signal);
          if (controller.signal.aborted) return;
          const updated: EntityPickerCache = {
            configs: new Map(next.configs),
            savedQueries: new Map(next.savedQueries),
          };
          updated.configs.set(entityName, { status: "ready", data: options });
          onPickerCacheChange(updated);
        } catch (err) {
          if (controller.signal.aborted) return;
          const updated: EntityPickerCache = {
            configs: new Map(next.configs),
            savedQueries: new Map(next.savedQueries),
          };
          updated.configs.set(entityName, {
            status: "error",
            message: err instanceof Error ? err.message : String(err),
          });
          onPickerCacheChange(updated);
        }
      })();
    }

    // Fetch savedqueries if not yet started.
    if (savedQueriesState.status === "idle") {
      const next: EntityPickerCache = {
        configs: new Map(pickerCache.configs),
        savedQueries: new Map(pickerCache.savedQueries),
      };
      next.savedQueries.set(entityName, { status: "loading" });
      onPickerCacheChange(next);

      void (async () => {
        try {
          const options = await fetchSavedQueries(entityName, authenticatedFetch, controller.signal);
          if (controller.signal.aborted) return;
          const updated: EntityPickerCache = {
            configs: new Map(next.configs),
            savedQueries: new Map(next.savedQueries),
          };
          updated.savedQueries.set(entityName, { status: "ready", data: options });
          onPickerCacheChange(updated);
        } catch (err) {
          if (controller.signal.aborted) return;
          const updated: EntityPickerCache = {
            configs: new Map(next.configs),
            savedQueries: new Map(next.savedQueries),
          };
          updated.savedQueries.set(entityName, {
            status: "error",
            message: err instanceof Error ? err.message : String(err),
          });
          onPickerCacheChange(updated);
        }
      })();
    }

    return () => controller.abort();
    // NOTE: intentionally NOT depending on `pickerCache` here — the effect
    // reads it via closure and enqueues a state update. Depending on it would
    // re-fire on every cache update and produce a loop. `entityName` +
    // status fields are the load-bearing triggers.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityName, configState.status, savedQueriesState.status, authenticatedFetch]);

  // Central update helper — applies a mutation to the current instance,
  // then normalizes: if the resulting instance has NO override set, DELETE it
  // from the map (back-compat "empty → bare string" invariant). Otherwise
  // upsert.
  const updateInstance = React.useCallback(
    (mutator: (draft: SectionInstance) => void) => {
      const draft: SectionInstance = {
        id: instance.id,
        configIdOverride: instance.configIdOverride,
        label: instance.label,
        overrides: instance.overrides ? { ...instance.overrides } : undefined,
      };
      mutator(draft);
      const next = new Map(sectionInstances);
      if (sectionInstanceHasAnyOverride(draft)) {
        next.set(slotKey, draft);
      } else {
        next.delete(slotKey);
      }
      onSectionInstancesChange(next);
    },
    [instance, sectionInstances, onSectionInstancesChange, slotKey],
  );

  const handleConfigIdSelect = React.useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      const key = data.optionValue;
      updateInstance((d) => {
        if (!key || key === CONFIG_ID_NONE_KEY) {
          d.configIdOverride = undefined;
        } else {
          d.configIdOverride = key;
        }
      });
    },
    [updateInstance],
  );

  const handleLabelChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      updateInstance((d) => {
        d.label = data.value.length > 0 ? data.value : undefined;
      });
    },
    [updateInstance],
  );

  const handlePageSizeChange = React.useCallback(
    (_ev: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
      const raw = data.value ?? (data.displayValue ? Number(data.displayValue) : null);
      updateInstance((d) => {
        const next = typeof raw === "number" && Number.isFinite(raw) && raw > 0 ? raw : undefined;
        if (next === undefined) {
          if (d.overrides) {
            d.overrides = { ...d.overrides, pageSize: undefined };
          }
        } else {
          d.overrides = { ...(d.overrides ?? {}), pageSize: next };
        }
      });
    },
    [updateInstance],
  );

  // DEF-003 — Combobox multi-select handler. Fluent v9 Combobox emits the
  // FULL set of selected option values on each change (not deltas), so we can
  // write them straight into the instance override.
  const handleAvailableViewsSelect = React.useCallback(
    (_ev: unknown, data: { selectedOptions: string[] }) => {
      const tokens = data.selectedOptions.filter((s) => s.length > 0);
      updateInstance((d) => {
        if (tokens.length === 0) {
          if (d.overrides) {
            d.overrides = { ...d.overrides, availableViews: undefined };
          }
        } else {
          d.overrides = { ...(d.overrides ?? {}), availableViews: tokens };
        }
      });
    },
    [updateInstance],
  );

  // Resolve controlled-value strings.
  const currentConfigIdKey = instance.configIdOverride ?? CONFIG_ID_NONE_KEY;
  // Build the effective configId option list: real data if ready, else fall
  // back to "None" only (matches graceful-degradation contract).
  const configOptions: readonly ConfigIdOption[] =
    configState.status === "ready" ? configState.data : [CONFIG_ID_NONE_OPTION];
  const currentConfigIdOption =
    configOptions.find((o) => o.key === currentConfigIdKey) ??
    configOptions[0] ??
    CONFIG_ID_NONE_OPTION;
  const currentLabelValue = instance.label ?? "";
  const currentPageSize = instance.overrides?.pageSize;
  const currentAvailableViewIds = instance.overrides?.availableViews ?? [];
  const savedQueryOptions: readonly SavedQueryOption[] =
    savedQueriesState.status === "ready" ? savedQueriesState.data : [];
  // Combobox displayValue: prefer resolved names, fall back to raw IDs when
  // the list hasn't loaded yet (avoids GUIDs disappearing during hydration).
  const currentAvailableViewsDisplay = currentAvailableViewIds
    .map((id) => savedQueryOptions.find((sq) => sq.id === id)?.name ?? id)
    .join(", ");

  return (
    <Accordion
      collapsible
      multiple
      className={classes.advancedAccordion}
      data-testid={`advanced-accordion-${slotKey}`}
    >
      <AccordionItem value={`${slotKey}-advanced`}>
        <AccordionHeader
          className={classes.advancedHeader}
          expandIconPosition="end"
          icon={<Settings16Regular />}
        >
          <Text size={200} weight="semibold">
            Advanced
          </Text>
        </AccordionHeader>
        <AccordionPanel className={classes.advancedPanel}>
          {/* (a) configId picker — DEF-002 real Dataverse query */}
          <div className={classes.advancedField}>
            <Label
              htmlFor={`advanced-configid-${slotKey}`}
              size="small"
              className={classes.advancedFieldLabel}
            >
              Grid configuration
              {configState.status === "loading" && (
                <Spinner
                  size="tiny"
                  aria-label="Loading grid configurations"
                  data-testid={`advanced-configid-spinner-${slotKey}`}
                  style={{ marginLeft: "6px", display: "inline-block" }}
                />
              )}
            </Label>
            <Dropdown
              id={`advanced-configid-${slotKey}`}
              size="small"
              value={currentConfigIdOption.label}
              selectedOptions={[currentConfigIdOption.key]}
              onOptionSelect={handleConfigIdSelect}
              aria-label={`Grid configuration override for ${sectionLabel}`}
              data-testid={`advanced-configid-dropdown-${slotKey}`}
              disabled={!entityName}
            >
              {configOptions.map((o) => (
                <Option key={o.key || "none"} value={o.key}>
                  {o.label}
                </Option>
              ))}
            </Dropdown>
            {configState.status === "error" && (
              <Text
                className={classes.advancedFieldHelp}
                style={{ color: tokens.colorPaletteRedForeground1 }}
                data-testid={`advanced-configid-error-${slotKey}`}
              >
                Could not load configurations: {configState.message}. Only
                &quot;None (use default)&quot; is available.
              </Text>
            )}
            {!entityName && (
              <Text className={classes.advancedFieldHelp}>
                This section has no Dataverse entity — configId override is not
                applicable.
              </Text>
            )}
            {entityName && configState.status !== "error" && (
              <Text className={classes.advancedFieldHelp}>
                Point this section at a different sprk_gridconfiguration record.
                Leave as &quot;None&quot; to use the registration&#39;s default.
              </Text>
            )}
          </div>

          {/* (b) label override */}
          <div className={classes.advancedField}>
            <Label
              htmlFor={`advanced-label-${slotKey}`}
              size="small"
              className={classes.advancedFieldLabel}
            >
              Label override
            </Label>
            <Input
              id={`advanced-label-${slotKey}`}
              size="small"
              appearance="outline"
              value={currentLabelValue}
              onChange={handleLabelChange}
              placeholder={sectionLabel}
              aria-label={`Label override for ${sectionLabel}`}
              data-testid={`advanced-label-input-${slotKey}`}
            />
            <Text className={classes.advancedFieldHelp}>
              Rename this section in this layout only. Empty = use default label.
            </Text>
          </div>

          {/* (c) pageSize override */}
          <div className={classes.advancedField}>
            <Label
              htmlFor={`advanced-pagesize-${slotKey}`}
              size="small"
              className={classes.advancedFieldLabel}
            >
              Page size
            </Label>
            <SpinButton
              id={`advanced-pagesize-${slotKey}`}
              size="small"
              appearance="outline"
              min={1}
              max={500}
              step={25}
              value={currentPageSize ?? null}
              displayValue={currentPageSize === undefined ? "" : String(currentPageSize)}
              onChange={handlePageSizeChange}
              placeholder="e.g., 100"
              aria-label={`Page size override for ${sectionLabel}`}
              data-testid={`advanced-pagesize-spinbutton-${slotKey}`}
            />
            <Text className={classes.advancedFieldHelp}>
              Rows per page. Empty = use config record&#39;s default (framework
              default is 25).
            </Text>
          </div>

          {/* (d) availableViews override — DEF-003 real savedquery multi-select */}
          <div className={classes.advancedField}>
            <Label
              htmlFor={`advanced-views-${slotKey}`}
              size="small"
              className={classes.advancedFieldLabel}
            >
              Available views
              {savedQueriesState.status === "loading" && (
                <Spinner
                  size="tiny"
                  aria-label="Loading saved queries"
                  data-testid={`advanced-views-spinner-${slotKey}`}
                  style={{ marginLeft: "6px", display: "inline-block" }}
                />
              )}
            </Label>
            <Combobox
              id={`advanced-views-${slotKey}`}
              size="small"
              appearance="outline"
              multiselect
              value={currentAvailableViewsDisplay}
              selectedOptions={currentAvailableViewIds}
              onOptionSelect={handleAvailableViewsSelect}
              placeholder={
                savedQueryOptions.length === 0
                  ? entityName
                    ? savedQueriesState.status === "loading"
                      ? "Loading views…"
                      : "No saved queries available"
                    : "N/A — no Dataverse entity"
                  : "Select one or more views…"
              }
              aria-label={`Available views override for ${sectionLabel}`}
              data-testid={`advanced-views-combobox-${slotKey}`}
              disabled={!entityName || savedQueryOptions.length === 0}
            >
              {savedQueryOptions.map((sq) => (
                <Option key={sq.id} value={sq.id}>
                  {sq.isDefault ? `${sq.name} (default)` : sq.name}
                </Option>
              ))}
            </Combobox>
            {savedQueriesState.status === "error" && (
              <Text
                className={classes.advancedFieldHelp}
                style={{ color: tokens.colorPaletteRedForeground1 }}
                data-testid={`advanced-views-error-${slotKey}`}
              >
                Could not load saved queries: {savedQueriesState.message}.
              </Text>
            )}
            {!entityName && (
              <Text className={classes.advancedFieldHelp}>
                This section has no Dataverse entity — availableViews override
                is not applicable.
              </Text>
            )}
            {entityName && savedQueriesState.status !== "error" && (
              <Text className={classes.advancedFieldHelp}>
                Select one or more saved views this section should offer. Leave
                empty to use the config-level allowlist.
              </Text>
            )}
          </div>

          {/* Help block covering all four fields */}
          <Text className={classes.advancedHelpBlock}>
            Overrides apply only to this layout instance. Set configId to point
            to a different sprk_gridconfiguration record. Set label to rename
            this section only in this layout.
          </Text>
        </AccordionPanel>
      </AccordionItem>
    </Accordion>
  );
};

// ---------------------------------------------------------------------------
// ArrangeStep component
// ---------------------------------------------------------------------------

export const ArrangeStep: React.FC<ArrangeStepProps> = ({
  templateId,
  selectedSections,
  sectionAssignments,
  workspaceName,
  isDefault,
  pinToStart,
  rowHeights,
  sectionInstances,
  onAssignmentsChange,
  onNameChange,
  onDefaultChange,
  onPinToStartChange,
  onRowHeightsChange,
  onSectionInstancesChange,
  authenticatedFetch,
}) => {
  const classes = useStyles();
  const template = getLayoutTemplate(templateId);
  const dragSourceRef = React.useRef<{ sectionId: string; slotId: string } | null>(null);

  // DEF-002 / DEF-003 — shared picker cache across all Advanced accordions in
  // this step. Keyed by entityLogicalName so opening Documents advanced +
  // Matters advanced fires 2 fetches total (not N per accordion).
  const [pickerCache, setPickerCache] = React.useState<EntityPickerCache>(() => ({
    configs: new Map(),
    savedQueries: new Map(),
  }));

  // FR-04 (task 015) — widthPreference placement dialog state. When the user
  // drops a `widthPreference: 'full'` section into a multi-slot row, we open a
  // Fluent v9 Dialog offering to clear the other slots in that row. Declining
  // (Cancel) leaves the row intact — the section will still render with a
  // persistent warning icon via `slotWidthWarnings` below.
  //
  // The dialog is NOT a hard block: the operator can also force a mismatch by
  // editing JSON directly (per spec). The runtime dev-guard in
  // `sectionRegistry.ts` still fires `console.warn` when the resulting layout
  // renders in that state — covering the "bypassed the wizard warning" path.
  const [widthPrefDialog, setWidthPrefDialog] = React.useState<{
    open: boolean;
    slotId: string;
    sectionId: string;
    sectionLabel: string;
    rowId: string;
  } | null>(null);

  // Build a section lookup map for quick access by ID.
  const sectionMap = React.useMemo(() => {
    const map = new Map<string, SectionCatalogItem>();
    for (const s of selectedSections) {
      map.set(s.id, s);
    }
    return map;
  }, [selectedSections]);

  // Determine which sections are assigned to grid slots.
  const assignedSectionIds = React.useMemo(() => {
    return new Set(sectionAssignments.values());
  }, [sectionAssignments]);

  // Sections not assigned to any grid slot.
  const unassignedSections = React.useMemo(() => {
    return selectedSections.filter((s) => !assignedSectionIds.has(s.id));
  }, [selectedSections, assignedSectionIds]);

  // Track drag source for slot-to-slot swaps.
  const handleSlotDragStart = React.useCallback(
    (sectionId: string, sourceSlotId: string) => {
      dragSourceRef.current = { sectionId, slotId: sourceSlotId };
    },
    [],
  );

  // Handle a drop onto a grid slot: supports unassigned->slot, slot->slot swap.
  const handleSlotDrop = React.useCallback(
    (targetSlotId: string, droppedSectionId: string) => {
      const next = new Map(sectionAssignments);
      const source = dragSourceRef.current;
      const sourceSlotId = source?.slotId ?? "unassigned";

      // What section currently lives in the target slot?
      const existingTargetSectionId = next.get(targetSlotId);

      // Place the dragged section in the target slot.
      next.set(targetSlotId, droppedSectionId);

      if (sourceSlotId !== "unassigned") {
        // Slot-to-slot: swap — put the displaced section in the source slot.
        if (existingTargetSectionId) {
          next.set(sourceSlotId, existingTargetSectionId);
        } else {
          // Source slot becomes empty.
          next.delete(sourceSlotId);
        }
      } else if (existingTargetSectionId) {
        // Dragged from unassigned into an occupied slot: displaced section goes unassigned.
        // Remove the displaced section from the slot (it will appear in unassigned automatically).
        // But the dragged section now occupies the target, so just check if the displaced
        // section is somewhere else — it's not, so no extra action needed.
      }

      dragSourceRef.current = null;
      onAssignmentsChange(next);

      // FR-04 (task 015) — Placement check. If the dropped section prefers
      // full-width AND the target row has >1 slot, prompt the operator to
      // clear the row's other slots. The check runs AFTER the assignment
      // update so the dialog sees the post-drop layout. Cancel leaves the
      // multi-slot layout intact; the section chip will still surface the
      // widthWarning icon via `slotWidthWarnings` below.
      const widthPref = lookupWidthPreference(droppedSectionId);
      if (widthPref !== "full" || !template) return;
      // Parse the target slot key back into rowId + col.
      const colonIdx = targetSlotId.lastIndexOf(":");
      if (colonIdx <= 0) return;
      const targetRowId = targetSlotId.slice(0, colonIdx);
      const targetRow = template.rows.find((r) => r.id === targetRowId);
      if (!targetRow || targetRow.slotCount <= 1) return;
      // Only prompt when the row actually has other occupied slots — an
      // empty multi-slot row doesn't need cleanup.
      let otherOccupied = 0;
      for (let c = 0; c < targetRow.slotCount; c++) {
        const key = slotKey(targetRow.id, c);
        if (key !== targetSlotId && next.has(key)) otherOccupied++;
      }
      const sectionCatalogEntry = sectionMap.get(droppedSectionId);
      const sectionLabel = sectionCatalogEntry?.label ?? droppedSectionId;
      // If the row is only occupied by the dropped section, no cleanup needed;
      // but the warning icon still surfaces because the row template retains
      // its multi-column layout. We only open the dialog when there IS a
      // cleanup to offer (otherOccupied > 0). Icon-only case is covered by
      // the passive warning surface.
      if (otherOccupied > 0) {
        setWidthPrefDialog({
          open: true,
          slotId: targetSlotId,
          sectionId: droppedSectionId,
          sectionLabel,
          rowId: targetRow.id,
        });
      }
    },
    [sectionAssignments, onAssignmentsChange, template, sectionMap],
  );

  // FR-04 (task 015) — "Yes, convert" handler. Clears every other slot in the
  // dialog's target row so the full-width section is the sole occupant.
  // Downstream buildSectionsJson still emits the row's original `columns`
  // (e.g., "1fr 1fr") — the framework's buildDynamicWorkspaceConfig then
  // produces an overflow-safe row with the single section stretched across
  // via the empty-slot handling. Semantics: matches "convert row to single-
  // column" from the operator's perspective without requiring wizard-side
  // template mutation (which would break existing tests + preview flow).
  const handleWidthPrefConfirm = React.useCallback(() => {
    if (!widthPrefDialog || !template) {
      setWidthPrefDialog(null);
      return;
    }
    const row = template.rows.find((r) => r.id === widthPrefDialog.rowId);
    if (!row) {
      setWidthPrefDialog(null);
      return;
    }
    const next = new Map(sectionAssignments);
    for (let c = 0; c < row.slotCount; c++) {
      const key = slotKey(row.id, c);
      if (key !== widthPrefDialog.slotId) next.delete(key);
    }
    onAssignmentsChange(next);
    setWidthPrefDialog(null);
  }, [widthPrefDialog, template, sectionAssignments, onAssignmentsChange]);

  const handleWidthPrefCancel = React.useCallback(() => {
    setWidthPrefDialog(null);
  }, []);

  // FR-04 (task 015) — Compute a per-slot widthPreference warning message.
  // Returns a Map keyed by slotKey; only slots with a live mismatch appear.
  // Used by GridSlot's `widthWarning` prop.
  //
  // Categories:
  //   'full' section in multi-slot row  → "Full-width preferred; row has {N} columns"
  //   'half' section in single-slot row → "Half-width preferred; single-column row may leave whitespace"
  //   'any' → no warning (default)
  const slotWidthWarnings = React.useMemo<Map<string, string>>(() => {
    const warnings = new Map<string, string>();
    if (!template) return warnings;
    for (const row of template.rows) {
      for (let c = 0; c < row.slotCount; c++) {
        const key = slotKey(row.id, c);
        const sectionId = sectionAssignments.get(key);
        if (!sectionId) continue;
        const pref = lookupWidthPreference(sectionId);
        if (pref === "full" && row.slotCount > 1) {
          warnings.set(
            key,
            `This widget is designed for full-width rows; the current row has ${row.slotCount} columns and may truncate content.`,
          );
        } else if (pref === "half" && row.slotCount === 1) {
          warnings.set(
            key,
            "This widget is designed for half-width rows; a single-column row may leave excess whitespace.",
          );
        }
      }
    }
    return warnings;
  }, [template, sectionAssignments]);

  // Handle drop onto unassigned area — remove section from its slot.
  const [isUnassignedDragOver, setIsUnassignedDragOver] = React.useState(false);

  const handleUnassignedDragOver = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.dataTransfer.dropEffect = "move";
      if (!isUnassignedDragOver) setIsUnassignedDragOver(true);
    },
    [isUnassignedDragOver],
  );

  const handleUnassignedDragLeave = React.useCallback(() => {
    setIsUnassignedDragOver(false);
  }, []);

  const handleUnassignedDrop = React.useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsUnassignedDragOver(false);
      const sectionId = e.dataTransfer.getData("text/plain");
      if (!sectionId) return;

      // Find the slot that contains this section and remove it.
      const next = new Map(sectionAssignments);
      for (const [key, val] of next) {
        if (val === sectionId) {
          next.delete(key);
          break;
        }
      }
      dragSourceRef.current = null;
      onAssignmentsChange(next);
    },
    [sectionAssignments, onAssignmentsChange],
  );

  // Remove a section from a slot via the slot's X button (Fix 2 / task 091).
  // The section returns to the unassigned pool automatically via the
  // `unassignedSections` derived state.
  const handleSlotRemove = React.useCallback(
    (slotId: string) => {
      if (!sectionAssignments.has(slotId)) return;
      const next = new Map(sectionAssignments);
      next.delete(slotId);
      dragSourceRef.current = null;
      onAssignmentsChange(next);
    },
    [sectionAssignments, onAssignmentsChange],
  );

  // FR-02 (task 011) — Per-row height change handler. Delegates to the parent
  // via onRowHeightsChange with an immutable Map update. Undefined value
  // clears the entry (dropdown returned to "Auto (default)").
  const handleRowHeightChange = React.useCallback(
    (rowId: string, newValue: string | undefined) => {
      const next = new Map(rowHeights);
      if (newValue === undefined) {
        next.delete(rowId);
      } else {
        next.set(rowId, newValue);
      }
      onRowHeightsChange(next);
    },
    [rowHeights, onRowHeightsChange],
  );

  if (!template) return null;

  // Identify overflow sections: selected but beyond slot capacity.
  // These are sections that can't fit in the template's slots.
  const totalSlots = template.slotCount;
  const hasOverflow = selectedSections.length > totalSlots;

  return (
    <div className={classes.root}>
      {/* Workspace name + Set default + Pin to Start (inline row) */}
      <div style={{ display: "flex", flexDirection: "row", alignItems: "flex-end", gap: tokens.spacingHorizontalL, flexWrap: "wrap" }}>
        <div style={{ flex: "1 1 0", minWidth: 0, maxWidth: "480px" }}>
          <Label htmlFor="workspace-name" required weight="semibold">
            Workspace name
          </Label>
          <Input
            id="workspace-name"
            value={workspaceName}
            onChange={(_e, data) => onNameChange(data.value)}
            placeholder="e.g., My Dashboard"
            appearance="outline"
          />
        </div>
        <Checkbox
          checked={isDefault}
          onChange={(_e, data) => onDefaultChange(data.checked === true)}
          label="Set default"
          style={{ paddingBottom: "2px" }}
        />
        {/*
          Pin to Start — task 092 / round 5 operator UX. When checked, this
          workspace's ID + name is added to the multi-pin localStorage list
          `spaarke:workspace:pinned-list` (see App.tsx). SpaarkeAi's
          WorkspacePane reads the list on cold load and auto-opens each
          pinned workspace as a tab. Multiple pins coexist; persists across
          browser sessions on the same device.
        */}
        <Checkbox
          checked={pinToStart}
          onChange={(_e, data) => onPinToStartChange(data.checked === true)}
          label={
            <span style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}>
              <PinRegular fontSize={14} aria-hidden="true" />
              <span>Pin to Start</span>
            </span>
          }
          aria-label="Pin to Start — auto-open this workspace on SpaarkeAi load"
          title="Auto-open this workspace on SpaarkeAi load"
          data-testid="pin-to-start-checkbox"
          style={{ paddingBottom: "2px" }}
        />
      </div>

      {/* Section heading */}
      <Text size={400} weight="semibold">
        Drag layout sections
      </Text>

      {/* Template grid */}
      <div className={classes.gridContainer}>
        {template.rows.map((row: LayoutTemplateRow, rowIdx: number) => (
          <div key={row.id} className={classes.rowGroup}>
            {/*
              FR-02 (task 011) — Per-row settings header. Fluent v9 Dropdown
              for `rowHeight` (Auto/40vh/60vh/80vh/100vh/Custom…) + tooltip.
              Selected value wires through to `LayoutJsonRow.rowHeight` in the
              wizard's JSON output via App.tsx.
            */}
            <RowHeightControl
              rowIndex={rowIdx}
              rowId={row.id}
              currentValue={rowHeights.get(row.id)}
              onChange={handleRowHeightChange}
            />
            <div
              className={classes.gridRow}
              style={{ gridTemplateColumns: row.gridTemplateColumns }}
            >
              {Array.from({ length: row.slotCount }, (_, colIdx) => {
                const key = slotKey(row.id, colIdx);
                const sectionId = sectionAssignments.get(key);
                const section = sectionId ? sectionMap.get(sectionId) : undefined;

                // FR-03 (task 013): render slot + Advanced accordion stacked in
                // a per-column wrapper. Accordion only appears for FILLED slots
                // (empty slot has no SectionInstance to render overrides for).
                //
                // Placement hook for FR-04 (task 015): task 015 will add a
                // `widthPreference` warning banner between the slot and the
                // accordion when a `widthPreference: 'full'` section is placed
                // in a multi-slot row. Leaving the vertical stack layout as-is
                // makes that insertion point trivial to add.
                return (
                  <div
                    key={key}
                    style={{ display: "flex", flexDirection: "column" }}
                  >
                    <GridSlot
                      slotId={key}
                      section={section}
                      onDrop={handleSlotDrop}
                      onDragStart={handleSlotDragStart}
                      onRemove={handleSlotRemove}
                      widthWarning={slotWidthWarnings.get(key)}
                    />
                    {section && (
                      <AdvancedSectionControl
                        slotKey={key}
                        sectionId={section.id}
                        sectionLabel={section.label}
                        sectionInstances={sectionInstances}
                        onSectionInstancesChange={onSectionInstancesChange}
                        authenticatedFetch={authenticatedFetch}
                        pickerCache={pickerCache}
                        onPickerCacheChange={setPickerCache}
                      />
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        ))}
      </div>

      {/* Unassigned sections area */}
      {(unassignedSections.length > 0 || true) && (
        <>
          <Text size={300} weight="semibold" style={{ color: tokens.colorNeutralForeground2 }}>
            Unassigned sections
          </Text>
          <div
            className={mergeClasses(
              classes.unassignedArea,
              isUnassignedDragOver && classes.unassignedAreaDragOver,
            )}
            onDragOver={handleUnassignedDragOver}
            onDragLeave={handleUnassignedDragLeave}
            onDrop={handleUnassignedDrop}
          >
            {unassignedSections.length > 0 ? (
              <div className={classes.unassignedList}>
                {unassignedSections.map((section) => (
                  <UnassignedSectionCard key={section.id} section={section} />
                ))}
              </div>
            ) : (
              <Text
                size={200}
                style={{ color: tokens.colorNeutralForeground4, textAlign: "center" }}
              >
                All sections assigned. Drag a section here to unassign it.
              </Text>
            )}
          </div>
        </>
      )}

      {/* Overflow note when more sections than slots */}
      {hasOverflow && (
        <div className={classes.overflowNote}>
          <Info16Regular />
          <Text size={200}>
            You have more sections than layout slots. Extra unassigned sections will be
            added as full-width rows below the grid.
          </Text>
        </div>
      )}

      {/*
        FR-04 (task 015) — Placement dialog. Opens when the operator drops a
        `widthPreference: 'full'` section into a multi-slot row that has other
        occupied slots. "Yes, convert" clears the other slots in that row;
        "Cancel" leaves the row intact + the section chip retains a warning icon.

        Fluent v9 Dialog per ADR-021 — surface uses semantic tokens (dark-mode
        safe via FluentProvider). `onOpenChange` handles backdrop / Esc close.
      */}
      <Dialog
        open={widthPrefDialog?.open === true}
        onOpenChange={(_ev, data) => {
          if (!data.open) handleWidthPrefCancel();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Full-width widget</DialogTitle>
            <DialogContent>
              <Text>
                {widthPrefDialog
                  ? `"${widthPrefDialog.sectionLabel}" renders best at full row width. Convert this row to a single column?`
                  : "This widget renders best at full row width. Convert this row to a single column?"}
              </Text>
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button
                  appearance="secondary"
                  onClick={handleWidthPrefCancel}
                  data-testid="width-pref-dialog-cancel"
                >
                  Cancel (keep row)
                </Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                onClick={handleWidthPrefConfirm}
                data-testid="width-pref-dialog-confirm"
              >
                Yes, convert
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
