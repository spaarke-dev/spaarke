/**
 * Wizard Step 1 — Template Selection
 *
 * Renders the 6 layout templates as visual thumbnail cards in a responsive grid.
 * Each card displays a CSS-based mini grid preview, the template name, and slot count.
 * The selected card is highlighted with a brand-colored border.
 *
 * @see ADR-021 - Fluent UI v9 Design System
 * @see ADR-012 - Shared component library
 */

import * as React from "react";
import {
  Badge,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import {
  LAYOUT_TEMPLATES,
  type LayoutTemplate,
  type LayoutTemplateId,
} from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface TemplateStepProps {
  /** Currently selected template ID, or null if none selected. */
  selectedTemplateId: LayoutTemplateId | null;
  /** Callback when the user selects a template card. */
  onSelect: (templateId: LayoutTemplateId) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: "12px",
    width: "100%",
    maxWidth: "840px",
    alignSelf: "center",
  },
  heading: {
    textAlign: "center",
  },
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(3, 1fr)",
    gap: "12px",
  },
  card: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    ...shorthands.padding("12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    border: `2px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "pointer",
    transitionProperty: "border-color, box-shadow",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      boxShadow: tokens.shadow4,
    },
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: "2px",
    },
  },
  cardSelected: {
    border: `2px solid ${tokens.colorBrandStroke1}`,
    boxShadow: tokens.shadow8,
  },
  thumbnail: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
    ...shorthands.padding("8px"),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    backgroundColor: tokens.colorNeutralBackground3,
    aspectRatio: "4 / 3",
    justifyContent: "center",
  },
  thumbnailRow: {
    display: "grid",
    gap: "4px",
    flex: 1,
    minHeight: 0,
  },
  thumbnailSlot: {
    ...shorthands.borderRadius("2px"),
    backgroundColor: tokens.colorNeutralStroke2,
    minHeight: "8px",
  },
  thumbnailSlotSelected: {
    backgroundColor: tokens.colorBrandBackground,
  },
  cardMeta: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: "8px",
  },
});

// ---------------------------------------------------------------------------
// Thumbnail renderer
// ---------------------------------------------------------------------------

/**
 * Renders a miniature CSS grid that visually represents the template's row/column
 * structure. Each slot is a colored rectangle; selected templates use brand color.
 */
const TemplateThumbnail: React.FC<{
  template: LayoutTemplate;
  selected: boolean;
}> = ({ template, selected }) => {
  const classes = useStyles();

  return (
    <div className={classes.thumbnail}>
      {template.rows.map((row) => (
        <div
          key={row.id}
          className={classes.thumbnailRow}
          style={{ gridTemplateColumns: row.gridTemplateColumns }}
        >
          {Array.from({ length: row.slotCount }, (_, i) => (
            <div
              key={`${row.id}-${i}`}
              className={mergeClasses(
                classes.thumbnailSlot,
                selected && classes.thumbnailSlotSelected,
              )}
            />
          ))}
        </div>
      ))}
    </div>
  );
};

// ---------------------------------------------------------------------------
// TemplateStep component
// ---------------------------------------------------------------------------

export const TemplateStep: React.FC<TemplateStepProps> = ({
  selectedTemplateId,
  onSelect,
}) => {
  const classes = useStyles();

  return (
    <div className={classes.root}>
      <Text
        className={classes.heading}
        size={400}
        weight="regular"
        as="p"
      >
        Choose a layout template to define the structure of your workspace.
      </Text>

      <div className={classes.grid} role="radiogroup" aria-label="Layout templates">
        {LAYOUT_TEMPLATES.map((template) => {
          const selected = template.id === selectedTemplateId;
          return (
            <div
              key={template.id}
              role="radio"
              aria-checked={selected}
              aria-label={`${template.name} — ${template.slotCount} slots`}
              tabIndex={0}
              className={mergeClasses(
                classes.card,
                selected && classes.cardSelected,
              )}
              onClick={() => onSelect(template.id)}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  onSelect(template.id);
                }
              }}
            >
              <TemplateThumbnail template={template} selected={selected} />

              <div className={classes.cardMeta}>
                <Text weight="semibold" size={300}>
                  {template.name}
                </Text>
                <Badge
                  appearance="filled"
                  color={selected ? "brand" : "informative"}
                  size="small"
                >
                  {template.slotCount} slots
                </Badge>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
