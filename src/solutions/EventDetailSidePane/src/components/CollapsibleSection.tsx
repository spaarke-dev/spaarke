/**
 * CollapsibleSection - Reusable collapsible/accordion section wrapper
 *
 * Provides expand/collapse functionality for side pane sections.
 * Used by DatesSection, RelatedEventSection, DescriptionSection, and HistorySection.
 *
 * Uses Fluent UI v9 components with proper theming support.
 *
 * @see projects/events-workspace-apps-UX-r1/design.md - Conditional Sections spec
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  mergeClasses,
} from "@fluentui/react-components";
import {
  ChevronDownRegular,
  ChevronRightRegular,
} from "@fluentui/react-icons";

// -----------------------------------------------------------------------------
// Types
// -----------------------------------------------------------------------------

/**
 * Props for the CollapsibleSection component
 */
export interface CollapsibleSectionProps {
  /** Section title displayed in the header */
  title: string;
  /** Optional icon to display before the title */
  icon?: React.ReactNode;
  /** Whether the section starts expanded (default: false) */
  defaultExpanded?: boolean;
  /** Controlled expanded state (if provided, component becomes controlled) */
  expanded?: boolean;
  /** Callback when expanded state changes */
  onExpandedChange?: (expanded: boolean) => void;
  /** Whether the section is disabled (cannot be toggled) */
  disabled?: boolean;
  /** Section content (children) */
  children: React.ReactNode;
  /** Optional badge/count to display in header */
  badge?: React.ReactNode;
  /** Optional CSS class name for the container */
  className?: string;
  /** Accessibility label for the toggle button */
  ariaLabel?: string;
}

// -----------------------------------------------------------------------------
// Styles
// -----------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    ...shorthands.padding("12px", "16px"),
    cursor: "pointer",
    userSelect: "none",
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ":focus-visible": {
      outlineWidth: "2px",
      outlineStyle: "solid",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },
  headerExpanded: {
    ...shorthands.borderRadius(
      tokens.borderRadiusMedium,
      tokens.borderRadiusMedium,
      "0",
      "0"
    ),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  headerDisabled: {
    cursor: "not-allowed",
    opacity: 0.6,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2,
    },
  },
  chevron: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    fontSize: "16px",
    color: tokens.colorNeutralForeground2,
    transitionProperty: "transform",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },
  chevronExpanded: {
    transform: "rotate(0deg)",
  },
  chevronCollapsed: {
    transform: "rotate(0deg)",
  },
  titleContainer: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    flexGrow: 1,
    minWidth: 0,
  },
  icon: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    fontSize: "16px",
    color: tokens.colorNeutralForeground2,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  badge: {
    flexShrink: 0,
    marginLeft: "auto",
  },
  content: {
    display: "none",
    ...shorthands.padding("16px"),
    ...shorthands.borderRadius(
      "0",
      "0",
      tokens.borderRadiusMedium,
      tokens.borderRadiusMedium
    ),
  },
  contentExpanded: {
    display: "block",
  },
});

// -----------------------------------------------------------------------------
// Component
// -----------------------------------------------------------------------------

/**
 * CollapsibleSection component for expand/collapse functionality.
 *
 * Can be used as controlled or uncontrolled component:
 * - Uncontrolled: Use defaultExpanded, component manages its own state
 * - Controlled: Use expanded + onExpandedChange, parent manages state
 *
 * Supports light, dark, and high-contrast themes via Fluent UI tokens.
 *
 * @example Uncontrolled usage
 * ```tsx
 * <CollapsibleSection title="Dates" icon={<CalendarRegular />} defaultExpanded>
 *   <DateFields />
 * </CollapsibleSection>
 * ```
 *
 * @example Controlled usage
 * ```tsx
 * const [expanded, setExpanded] = useState(false);
 *
 * <CollapsibleSection
 *   title="History"
 *   expanded={expanded}
 *   onExpandedChange={setExpanded}
 * >
 *   <HistoryList />
 * </CollapsibleSection>
 * ```
 */
export const CollapsibleSection: React.FC<CollapsibleSectionProps> = ({
  title,
  icon,
  defaultExpanded = false,
  expanded: controlledExpanded,
  onExpandedChange,
  disabled = false,
  children,
  badge,
  className,
  ariaLabel,
}) => {
  const styles = useStyles();

  // Determine if controlled or uncontrolled
  const isControlled = controlledExpanded !== undefined;

  // Internal state for uncontrolled mode
  const [internalExpanded, setInternalExpanded] = React.useState(defaultExpanded);

  // Effective expanded state
  const expanded = isControlled ? controlledExpanded : internalExpanded;

  // Generate unique ID for accessibility
  const sectionId = React.useId();
  const headerId = `${sectionId}-header`;
  const contentId = `${sectionId}-content`;

  /**
   * Handle toggle click
   */
  const handleToggle = React.useCallback(() => {
    if (disabled) return;

    const newExpanded = !expanded;

    if (!isControlled) {
      setInternalExpanded(newExpanded);
    }

    onExpandedChange?.(newExpanded);
  }, [disabled, expanded, isControlled, onExpandedChange]);

  /**
   * Handle keyboard navigation
   */
  const handleKeyDown = React.useCallback(
    (event: React.KeyboardEvent) => {
      if (disabled) return;

      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        handleToggle();
      }
    },
    [disabled, handleToggle]
  );

  // Compute header class names
  const headerClassName = mergeClasses(
    styles.header,
    expanded && styles.headerExpanded,
    disabled && styles.headerDisabled
  );

  // Compute content class names
  const contentClassName = mergeClasses(
    styles.content,
    expanded && styles.contentExpanded
  );

  return (
    <div className={mergeClasses(styles.container, className)}>
      {/* Header (clickable) */}
      <div
        id={headerId}
        className={headerClassName}
        role="button"
        tabIndex={disabled ? -1 : 0}
        aria-expanded={expanded}
        aria-controls={contentId}
        aria-label={ariaLabel || `${expanded ? "Collapse" : "Expand"} ${title} section`}
        aria-disabled={disabled}
        onClick={handleToggle}
        onKeyDown={handleKeyDown}
      >
        {/* Chevron indicator */}
        <span className={styles.chevron}>
          {expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
        </span>

        {/* Title container with optional icon */}
        <div className={styles.titleContainer}>
          {icon && <span className={styles.icon}>{icon}</span>}
          <Text className={styles.title}>{title}</Text>
        </div>

        {/* Optional badge */}
        {badge && <span className={styles.badge}>{badge}</span>}
      </div>

      {/* Content (collapsible) */}
      <div
        id={contentId}
        className={contentClassName}
        role="region"
        aria-labelledby={headerId}
        hidden={!expanded}
      >
        {children}
      </div>
    </div>
  );
};

export default CollapsibleSection;
