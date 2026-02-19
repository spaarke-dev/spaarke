import * as React from "react";
import {
  makeStyles,
  tokens,
  ToggleButton,
} from "@fluentui/react-components";
import {
  DocumentRegular,
  ReceiptRegular,
  InfoRegular,
  SparkleRegular,
} from "@fluentui/react-icons";
import { NotificationCategory } from "../../types";
import { NOTIFICATION_CATEGORIES } from "./notificationTypes";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  filtersBar: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  filterButton: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    minWidth: "auto",
  },
});

// ---------------------------------------------------------------------------
// Category icon resolver
// ---------------------------------------------------------------------------

function getCategoryIcon(category: NotificationCategory): React.ReactElement {
  switch (category) {
    case "Documents":
      return <DocumentRegular />;
    case "Invoices":
      return <ReceiptRegular />;
    case "Status":
      return <InfoRegular />;
    case "Analysis":
      return <SparkleRegular />;
    default:
      return <DocumentRegular />;
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface INotificationFiltersProps {
  /** Currently active filter categories. Empty set = show all. */
  activeFilters: Set<NotificationCategory>;
  /** Called when a filter toggle is clicked */
  onToggleFilter: (category: NotificationCategory) => void;
}

export const NotificationFilters: React.FC<INotificationFiltersProps> = ({
  activeFilters,
  onToggleFilter,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.filtersBar} role="group" aria-label="Filter notifications by category">
      {NOTIFICATION_CATEGORIES.map((cat) => {
        const isActive = activeFilters.has(cat.key);
        return (
          <ToggleButton
            key={cat.key}
            className={styles.filterButton}
            size="small"
            appearance="subtle"
            checked={isActive}
            icon={getCategoryIcon(cat.key)}
            onClick={() => onToggleFilter(cat.key)}
            aria-label={`${isActive ? "Remove" : "Add"} ${cat.label} filter`}
            aria-pressed={isActive}
          >
            {cat.label}
          </ToggleButton>
        );
      })}
    </div>
  );
};
