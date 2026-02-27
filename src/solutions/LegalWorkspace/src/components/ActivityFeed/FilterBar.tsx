/**
 * FilterBar — 8 pill-style filter buttons for the Updates Feed (Block 3).
 *
 * Each pill displays:
 *   - Category icon
 *   - Category label
 *   - Dynamic count badge (derived from the full All-filter event list)
 *
 * The active pill is highlighted using Fluent UI v9 ToggleButton checked state.
 * Zero-count pills remain visible and clickable; the count badge renders "0".
 *
 * Accessibility:
 *   - role="radiogroup" on the container (single-select semantics)
 *   - role="radio" implied by ToggleButton's aria-pressed
 *   - aria-label describes each pill with its count
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  ToggleButton,
  Badge,
} from "@fluentui/react-components";
import {
  AppsListRegular,
  AlertRegular,
  ClockRegular,
  AlertOnRegular,
  MailRegular,
  DocumentRegular,
  ReceiptRegular,
  CheckboxCheckedRegular,
} from "@fluentui/react-icons";
import { EventFilterCategory } from "../../types/enums";
import { CategoryCounts } from "../../hooks/useActivityFeedFilters";

// ---------------------------------------------------------------------------
// Pill metadata
// ---------------------------------------------------------------------------

interface IFilterPillMeta {
  key: EventFilterCategory;
  label: string;
  icon: React.ReactElement;
  ariaDescription: string;
}

const FILTER_PILLS: IFilterPillMeta[] = [
  {
    key: EventFilterCategory.All,
    label: "All",
    icon: <AppsListRegular />,
    ariaDescription: "Show all updates",
  },
  {
    key: EventFilterCategory.HighPriority,
    label: "High Priority",
    icon: <AlertRegular />,
    ariaDescription: "Show high priority updates",
  },
  {
    key: EventFilterCategory.Overdue,
    label: "Overdue",
    icon: <ClockRegular />,
    ariaDescription: "Show overdue items",
  },
  {
    key: EventFilterCategory.Alerts,
    label: "Alerts",
    icon: <AlertOnRegular />,
    ariaDescription: "Show alerts and status changes",
  },
  {
    key: EventFilterCategory.Emails,
    label: "Emails",
    icon: <MailRegular />,
    ariaDescription: "Show email updates",
  },
  {
    key: EventFilterCategory.Documents,
    label: "Documents",
    icon: <DocumentRegular />,
    ariaDescription: "Show document updates",
  },
  {
    key: EventFilterCategory.Invoices,
    label: "Invoices",
    icon: <ReceiptRegular />,
    ariaDescription: "Show invoice updates",
  },
  {
    key: EventFilterCategory.Tasks,
    label: "Tasks",
    icon: <CheckboxCheckedRegular />,
    ariaDescription: "Show task updates",
  },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  filterBar: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    minHeight: "36px",
  },
  pill: {
    // Compact pill sizing
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    minWidth: "auto",
    gap: tokens.spacingHorizontalXS,
  },
  pillContent: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
  },
  countBadge: {
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFilterBarProps {
  /** Currently active filter pill */
  activeFilter: EventFilterCategory;
  /** Per-category item counts for badge display */
  categoryCounts: CategoryCounts;
  /** Called when the user clicks a filter pill */
  onFilterChange: (filter: EventFilterCategory) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FilterBar: React.FC<IFilterBarProps> = ({
  activeFilter,
  categoryCounts,
  onFilterChange,
}) => {
  const styles = useStyles();

  return (
    <div
      className={styles.filterBar}
      role="toolbar"
      aria-label="Filter updates by category"
    >
      {FILTER_PILLS.map((pill) => {
        const count = categoryCounts[pill.key];
        const isActive = activeFilter === pill.key;

        return (
          <ToggleButton
            key={pill.key}
            className={styles.pill}
            size="small"
            appearance="subtle"
            checked={isActive}
            icon={pill.icon}
            onClick={() => onFilterChange(pill.key)}
            aria-label={`${pill.ariaDescription} (${count})`}
            aria-pressed={isActive}
          >
            <span className={styles.pillContent}>
              {pill.label}
              <Badge
                className={styles.countBadge}
                size="small"
                appearance={isActive ? "filled" : "ghost"}
                color={isActive ? "brand" : "informative"}
                aria-hidden="true"
              >
                {count > 999 ? "999+" : count}
              </Badge>
            </span>
          </ToggleButton>
        );
      })}
    </div>
  );
};
