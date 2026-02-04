/**
 * HeaderSection - Side Pane Header with Event Name, Type, and Parent Link
 *
 * Displays the header section of the Event Detail Side Pane with:
 * - Editable Event Name (inline input)
 * - Event Type badge (read-only)
 * - Parent record link (clickable)
 * - Close button (X)
 *
 * @see design.md - Header Layout Structure
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Input,
  Badge,
  Link,
  Button,
  Spinner,
  Text,
  mergeClasses,
} from "@fluentui/react-components";
import {
  DismissRegular,
  OpenRegular,
  CalendarMonthRegular,
} from "@fluentui/react-icons";
import { IEventRecord } from "../types/EventRecord";
import { loadEventHeader, updateEventName } from "../services/eventService";
import { closeSidePane, navigateToParentRecord } from "../services/sidePaneService";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (Fluent UI v9 makeStyles with semantic tokens)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  header: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("12px"),
    ...shorthands.padding("16px", "20px"),
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground2,
  },
  topRow: {
    display: "flex",
    alignItems: "flex-start",
    justifyContent: "space-between",
    ...shorthands.gap("12px"),
  },
  titleArea: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
    flexGrow: 1,
    minWidth: 0, // Allow text truncation
  },
  nameInput: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    // Remove input styling for inline edit appearance
    "& input": {
      fontWeight: tokens.fontWeightSemibold,
      fontSize: tokens.fontSizeBase400,
    },
  },
  nameInputEditing: {
    // Show border when focused/editing
  },
  metaRow: {
    display: "flex",
    alignItems: "center",
    flexWrap: "wrap",
    ...shorthands.gap("8px"),
  },
  typeBadge: {
    // Badge styling uses Fluent defaults
  },
  parentLink: {
    display: "inline-flex",
    alignItems: "center",
    ...shorthands.gap("4px"),
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorBrandForeground1,
    textDecoration: "none",
    cursor: "pointer",
    ":hover": {
      textDecoration: "underline",
    },
  },
  parentIcon: {
    fontSize: "12px",
  },
  closeButton: {
    // Keep close button at fixed size
    flexShrink: 0,
  },
  loadingState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
  },
  errorState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap("8px"),
    ...shorthands.padding("16px"),
    color: tokens.colorPaletteRedForeground1,
  },
  separator: {
    color: tokens.colorNeutralForeground4,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props Interface
// ─────────────────────────────────────────────────────────────────────────────

export interface IHeaderSectionProps {
  /** Event ID to load (GUID) */
  eventId: string | null;
  /** Callback when event data is loaded */
  onEventLoaded?: (event: IEventRecord) => void;
  /** Callback when event name is updated */
  onNameUpdated?: (newName: string) => void;
  /** Callback when close is requested (for unsaved changes prompt) */
  onCloseRequest?: () => void;
  /** Whether the form is in read-only mode (disables name editing) */
  isReadOnly?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const HeaderSection: React.FC<IHeaderSectionProps> = ({
  eventId,
  onEventLoaded,
  onNameUpdated,
  onCloseRequest,
  isReadOnly = false,
}) => {
  const styles = useStyles();

  // ─────────────────────────────────────────────────────────────────────────
  // State
  // ─────────────────────────────────────────────────────────────────────────

  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [event, setEvent] = React.useState<IEventRecord | null>(null);

  // Editable name state
  const [editingName, setEditingName] = React.useState<string>("");
  const [isSavingName, setIsSavingName] = React.useState(false);

  // ─────────────────────────────────────────────────────────────────────────
  // Load Event Data
  // ─────────────────────────────────────────────────────────────────────────

  React.useEffect(() => {
    if (!eventId) {
      setIsLoading(false);
      setError("No event ID provided");
      return;
    }

    let cancelled = false;

    const loadData = async () => {
      setIsLoading(true);
      setError(null);

      const result = await loadEventHeader(eventId);

      if (cancelled) return;

      if (result.success && result.event) {
        setEvent(result.event);
        setEditingName(result.event.sprk_eventname || "");
        onEventLoaded?.(result.event);
      } else {
        setError(result.error || "Failed to load event");
      }

      setIsLoading(false);
    };

    loadData();

    return () => {
      cancelled = true;
    };
  }, [eventId, onEventLoaded]);

  // ─────────────────────────────────────────────────────────────────────────
  // Event Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle close button click - delegate to parent for unsaved changes check
   * Falls back to direct close if no handler provided
   */
  const handleClose = React.useCallback(() => {
    if (onCloseRequest) {
      onCloseRequest();
    } else {
      closeSidePane();
    }
  }, [onCloseRequest]);

  /**
   * Handle parent link click - navigate to parent record
   */
  const handleParentClick = React.useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      if (event?.sprk_regardingrecordurl) {
        navigateToParentRecord(event.sprk_regardingrecordurl);
      }
    },
    [event]
  );

  /**
   * Handle name input change
   */
  const handleNameChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setEditingName(e.target.value);
    },
    []
  );

  /**
   * Handle name input blur - save if changed
   */
  const handleNameBlur = React.useCallback(async () => {
    if (!eventId || !event) return;

    const trimmedName = editingName.trim();
    if (trimmedName === event.sprk_eventname) {
      // No change
      return;
    }

    if (!trimmedName) {
      // Revert to original if empty
      setEditingName(event.sprk_eventname || "");
      return;
    }

    // Save the new name
    setIsSavingName(true);
    const result = await updateEventName(eventId, trimmedName);
    setIsSavingName(false);

    if (result.success) {
      // Update local state
      setEvent((prev) =>
        prev ? { ...prev, sprk_eventname: trimmedName } : null
      );
      onNameUpdated?.(trimmedName);
    } else {
      // Revert on error
      setEditingName(event.sprk_eventname || "");
      console.error("[HeaderSection] Failed to save name:", result.error);
    }
  }, [eventId, event, editingName, onNameUpdated]);

  /**
   * Handle Enter key in name input - blur to save
   */
  const handleNameKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") {
        e.currentTarget.blur();
      } else if (e.key === "Escape") {
        // Revert on Escape
        setEditingName(event?.sprk_eventname || "");
        e.currentTarget.blur();
      }
    },
    [event]
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Render Loading State
  // ─────────────────────────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <header className={styles.header}>
        <div className={styles.loadingState}>
          <Spinner size="small" label="Loading event..." />
        </div>
      </header>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Render Error State
  // ─────────────────────────────────────────────────────────────────────────

  if (error || !event) {
    return (
      <header className={styles.header}>
        <div className={styles.topRow}>
          <div className={styles.errorState}>
            <CalendarMonthRegular style={{ fontSize: "24px" }} />
            <Text>{error || "Event not found"}</Text>
          </div>
          <Button
            className={styles.closeButton}
            appearance="subtle"
            icon={<DismissRegular />}
            onClick={handleClose}
            aria-label="Close"
          />
        </div>
      </header>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Extract display values
  // ─────────────────────────────────────────────────────────────────────────

  const eventTypeName =
    event["_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue"] ||
    "Unknown Type";
  const parentName = event.sprk_regardingrecordname;
  const parentUrl = event.sprk_regardingrecordurl;

  // ─────────────────────────────────────────────────────────────────────────
  // Render Header
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <header className={styles.header}>
      {/* Top Row: Name + Close Button */}
      <div className={styles.topRow}>
        <div className={styles.titleArea}>
          {/* Editable Event Name (disabled in read-only mode) */}
          <Input
            className={mergeClasses(styles.nameInput)}
            appearance="underline"
            value={editingName}
            onChange={handleNameChange}
            onBlur={handleNameBlur}
            onKeyDown={handleNameKeyDown}
            disabled={isSavingName || isReadOnly}
            aria-label="Event name"
            placeholder="Event name"
            readOnly={isReadOnly}
          />

          {/* Meta Row: Type Badge + Parent Link */}
          <div className={styles.metaRow}>
            {/* Event Type Badge */}
            <Badge
              className={styles.typeBadge}
              appearance="filled"
              color="brand"
              size="small"
            >
              {eventTypeName}
            </Badge>

            {/* Parent Record Link (if available) */}
            {parentName && parentUrl && (
              <>
                <Text className={styles.separator}>|</Text>
                <Link
                  className={styles.parentLink}
                  onClick={handleParentClick}
                  aria-label={`Navigate to ${parentName}`}
                >
                  <OpenRegular className={styles.parentIcon} />
                  {parentName}
                </Link>
              </>
            )}
          </div>
        </div>

        {/* Close Button */}
        <Button
          className={styles.closeButton}
          appearance="subtle"
          icon={<DismissRegular />}
          onClick={handleClose}
          aria-label="Close side pane"
        />
      </div>
    </header>
  );
};
