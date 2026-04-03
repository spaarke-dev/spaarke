/**
 * Wizard Step 2 — Section Selection
 *
 * Renders the available workspace sections grouped by category (overview, data,
 * ai, productivity). Each section shows a checkbox, icon, label, and description.
 * A counter displays "Selected: N sections | Layout slots: M" with color feedback.
 *
 * @see ADR-021 - Fluent UI v9 Design System
 * @see ADR-012 - Shared component library
 */

import * as React from "react";
import {
  Checkbox,
  Text,
  Radio,
  RadioGroup,
  Label,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import type { FluentIcon } from "@fluentui/react-icons";
import type { SectionCategory } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Section catalog item — flat shape used by the wizard (no factory). */
export interface SectionCatalogItem {
  id: string;
  label: string;
  description: string;
  category: SectionCategory;
  icon: FluentIcon;
  defaultHeight?: string;
}

export type WorkspaceScope = "my" | "all";

export interface SectionStepProps {
  /** Available sections to display. */
  sections: SectionCatalogItem[];
  /** Currently selected section IDs. */
  selectedIds: Set<string>;
  /** Number of layout slots from the selected template. */
  slotCount: number;
  /** Toggle a section's selection state. */
  onToggle: (sectionId: string) => void;
  /** Record scope setting. */
  scope: WorkspaceScope;
  /** Called when scope changes. */
  onScopeChange: (scope: WorkspaceScope) => void;
}

// ---------------------------------------------------------------------------
// Category metadata
// ---------------------------------------------------------------------------

const CATEGORY_ORDER: SectionCategory[] = [
  "overview",
  "data",
  "ai",
  "productivity",
];

const CATEGORY_LABELS: Record<SectionCategory, string> = {
  overview: "Overview",
  data: "Data",
  ai: "AI & Intelligence",
  productivity: "Productivity",
};

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: "20px",
    width: "100%",
    maxWidth: "640px",
    alignSelf: "center",
  },
  heading: {
    textAlign: "center",
  },
  counter: {
    textAlign: "center",
    paddingTop: "4px",
    paddingBottom: "4px",
  },
  counterWarning: {
    color: tokens.colorPaletteYellowForeground1,
  },
  counterError: {
    color: tokens.colorPaletteRedForeground1,
  },
  counterOk: {
    color: tokens.colorNeutralForeground2,
  },
  categoryGroup: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  categoryHeading: {
    paddingBottom: "4px",
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    marginBottom: "4px",
  },
  sectionItem: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
    paddingTop: "8px",
    paddingBottom: "8px",
    paddingLeft: "4px",
    paddingRight: "4px",
    borderRadius: tokens.borderRadiusMedium,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
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
  sectionText: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    minWidth: 0,
    flex: 1,
  },
});

// ---------------------------------------------------------------------------
// SectionCheckItem — single section row
// ---------------------------------------------------------------------------

const SectionCheckItem: React.FC<{
  section: SectionCatalogItem;
  checked: boolean;
  onToggle: () => void;
}> = ({ section, checked, onToggle }) => {
  const classes = useStyles();
  const IconComponent = section.icon;

  return (
    <div className={classes.sectionItem}>
      <Checkbox
        checked={checked}
        onChange={onToggle}
        aria-label={`${checked ? "Deselect" : "Select"} ${section.label}`}
      />
      <div className={classes.sectionIcon}>
        <IconComponent />
      </div>
      <div className={classes.sectionText}>
        <Text weight="semibold" size={300}>
          {section.label}
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          {section.description}
        </Text>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// SectionStep component
// ---------------------------------------------------------------------------

export const SectionStep: React.FC<SectionStepProps> = ({
  sections,
  selectedIds,
  slotCount,
  onToggle,
  scope,
  onScopeChange,
}) => {
  const classes = useStyles();
  const selectedCount = selectedIds.size;

  // Group sections by category
  const grouped = React.useMemo(() => {
    const map = new Map<SectionCategory, SectionCatalogItem[]>();
    for (const section of sections) {
      const list = map.get(section.category) ?? [];
      list.push(section);
      map.set(section.category, list);
    }
    return map;
  }, [sections]);

  // Determine counter style
  const counterClass =
    selectedCount === 0
      ? classes.counterError
      : selectedCount > slotCount
        ? classes.counterWarning
        : classes.counterOk;

  return (
    <div className={classes.root}>
      {/* Scope setting */}
      <div style={{ display: "flex", flexDirection: "column", gap: tokens.spacingVerticalXS }}>
        <Label weight="semibold">Scope</Label>
        <RadioGroup
          value={scope}
          onChange={(_e, data) => onScopeChange(data.value as WorkspaceScope)}
          layout="horizontal"
        >
          <Radio value="my" label="Show only my records" />
          <Radio value="all" label="Show all records" />
        </RadioGroup>
      </div>

      <Text className={classes.heading} size={400} weight="regular" as="p">
        Select the sections you want in your workspace layout.
      </Text>

      {/* Counter */}
      <Text
        className={mergeClasses(classes.counter, counterClass)}
        size={300}
        weight="semibold"
      >
        Selected: {selectedCount} sections | Layout slots: {slotCount}
      </Text>

      {/* Category groups */}
      {CATEGORY_ORDER.map((category) => {
        const items = grouped.get(category);
        if (!items || items.length === 0) return null;

        return (
          <div key={category} className={classes.categoryGroup}>
            <Text
              className={classes.categoryHeading}
              size={300}
              weight="semibold"
              style={{ color: tokens.colorNeutralForeground2 }}
            >
              {CATEGORY_LABELS[category]}
            </Text>
            {items.map((section) => (
              <SectionCheckItem
                key={section.id}
                section={section}
                checked={selectedIds.has(section.id)}
                onToggle={() => onToggle(section.id)}
              />
            ))}
          </div>
        );
      })}

    </div>
  );
};
