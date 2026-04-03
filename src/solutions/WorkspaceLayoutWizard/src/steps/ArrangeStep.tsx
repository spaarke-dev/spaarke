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
  Checkbox,
  Input,
  Label,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import {
  ReOrderDotsVertical24Regular,
  Info16Regular,
} from "@fluentui/react-icons";
import type { FluentIcon } from "@fluentui/react-icons";
import {
  getLayoutTemplate,
  type LayoutTemplateId,
  type LayoutTemplateRow,
} from "@spaarke/ui-components";
import type { SectionCatalogItem } from "./SectionStep";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Map of slot key (e.g., "row-1:0") to section ID. */
export type SlotAssignments = Map<string, string>;

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
  /** Callback when slot assignments change. */
  onAssignmentsChange: (assignments: SlotAssignments) => void;
  /** Callback when workspace name changes. */
  onNameChange: (name: string) => void;
  /** Callback when default checkbox changes. */
  onDefaultChange: (isDefault: boolean) => void;
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
  gridRow: {
    display: "grid",
    gap: "8px",
  },
  slot: {
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
}> = ({ slotId, section, onDrop, onDragStart }) => {
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
        <DraggableSectionCard section={section} isHovered={isHovered} />
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
// ArrangeStep component
// ---------------------------------------------------------------------------

export const ArrangeStep: React.FC<ArrangeStepProps> = ({
  templateId,
  selectedSections,
  sectionAssignments,
  workspaceName,
  isDefault,
  onAssignmentsChange,
  onNameChange,
  onDefaultChange,
}) => {
  const classes = useStyles();
  const template = getLayoutTemplate(templateId);
  const dragSourceRef = React.useRef<{ sectionId: string; slotId: string } | null>(null);

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
    },
    [sectionAssignments, onAssignmentsChange],
  );

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

  if (!template) return null;

  // Identify overflow sections: selected but beyond slot capacity.
  // These are sections that can't fit in the template's slots.
  const totalSlots = template.slotCount;
  const hasOverflow = selectedSections.length > totalSlots;

  return (
    <div className={classes.root}>
      {/* Workspace name + Set default (inline row) */}
      <div style={{ display: "flex", flexDirection: "row", alignItems: "flex-end", gap: tokens.spacingHorizontalL }}>
        <div style={{ flex: "1 1 0", minWidth: 0, maxWidth: "320px" }}>
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
      </div>

      {/* Section heading */}
      <Text size={400} weight="semibold">
        Drag layout sections
      </Text>

      {/* Template grid */}
      <div className={classes.gridContainer}>
        {template.rows.map((row: LayoutTemplateRow) => (
          <div
            key={row.id}
            className={classes.gridRow}
            style={{ gridTemplateColumns: row.gridTemplateColumns }}
          >
            {Array.from({ length: row.slotCount }, (_, colIdx) => {
              const key = slotKey(row.id, colIdx);
              const sectionId = sectionAssignments.get(key);
              const section = sectionId ? sectionMap.get(sectionId) : undefined;

              return (
                <GridSlot
                  key={key}
                  slotId={key}
                  section={section}
                  onDrop={handleSlotDrop}
                  onDragStart={handleSlotDragStart}
                />
              );
            })}
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
    </div>
  );
};
