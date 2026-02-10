/**
 * RelatedEventSection - Collapsible section for linking to other events
 *
 * Displays a Related Event lookup field with:
 * - Lookup control for selecting an event (sprk_event)
 * - Display of selected event name
 * - Clear button to remove selection
 *
 * Uses Fluent UI v9 Accordion for collapsible behavior per ADR-021.
 * Section visibility controlled by Event Type configuration.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/036-create-relatedevent-section.poml
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Text,
  Spinner,
  Link,
} from "@fluentui/react-components";
import {
  LinkRegular,
  DismissCircleRegular,
  CalendarMonthRegular,
  SearchRegular,
} from "@fluentui/react-icons";

/**
 * Section collapse state options (inline type to avoid external dependency)
 * Matches ISectionDefaults from EventTypeConfig
 */
type SectionCollapseState = "expanded" | "collapsed";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Related event information for display
 */
export interface IRelatedEventInfo {
  /** Event GUID */
  id: string;
  /** Event display name */
  name: string;
}

/**
 * Props for the RelatedEventSection component
 */
export interface IRelatedEventSectionProps {
  /** Currently selected related event (null if none) */
  relatedEvent: IRelatedEventInfo | null;
  /** Callback when user wants to open the lookup dialog */
  onLookup: () => void;
  /** Callback when user clears the related event selection */
  onClear: () => void;
  /** Callback when user clicks the related event link to navigate */
  onNavigate?: (eventId: string) => void;
  /** Whether the field is disabled (read-only mode) */
  disabled?: boolean;
  /** Loading state */
  isLoading?: boolean;
  /** Default collapse state for the section */
  defaultCollapseState?: SectionCollapseState;
  /** Whether the section should be visible (based on Event Type config) */
  visible?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  accordion: {
    // Remove default accordion background
    backgroundColor: "transparent",
  },
  accordionItem: {
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
  },
  accordionHeader: {
    // Custom header styling
  },
  headerContent: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
  },
  headerIcon: {
    color: tokens.colorNeutralForeground3,
    fontSize: "16px",
  },
  headerText: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  panel: {
    ...shorthands.padding("12px", "20px", "16px", "20px"),
  },
  lookupContainer: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
  },
  selectedEvent: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("10px", "12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
  },
  eventInfo: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    minWidth: 0, // Allow text truncation
    flexGrow: 1,
  },
  eventIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: "16px",
    flexShrink: 0,
  },
  eventLink: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    cursor: "pointer",
    textDecoration: "none",
    ":hover": {
      textDecoration: "underline",
    },
  },
  eventName: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  clearButton: {
    flexShrink: 0,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("12px"),
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  lookupButton: {
    width: "100%",
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("16px"),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * RelatedEventSection component for displaying and selecting a related event.
 *
 * Uses Fluent UI v9 Accordion for collapsible behavior. The section allows
 * users to:
 * - View the currently linked event (with navigation link)
 * - Open a lookup dialog to select a different event
 * - Clear the current selection
 *
 * @example
 * ```tsx
 * const [relatedEvent, setRelatedEvent] = useState<IRelatedEventInfo | null>(null);
 *
 * <RelatedEventSection
 *   relatedEvent={relatedEvent}
 *   onLookup={handleOpenLookup}
 *   onClear={() => setRelatedEvent(null)}
 *   onNavigate={handleNavigateToEvent}
 *   disabled={!canEdit}
 *   defaultCollapseState="collapsed"
 * />
 * ```
 */
export const RelatedEventSection: React.FC<IRelatedEventSectionProps> = ({
  relatedEvent,
  onLookup,
  onClear,
  onNavigate,
  disabled = false,
  isLoading = false,
  defaultCollapseState = "collapsed",
  visible = true,
}) => {
  const styles = useStyles();

  // Track which accordion items are open
  const [openItems, setOpenItems] = React.useState<string[]>(
    defaultCollapseState === "expanded" ? ["relatedEvent"] : []
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Event Handlers
  // ─────────────────────────────────────────────────────────────────────────

  /**
   * Handle accordion toggle
   */
  const handleToggle = React.useCallback(
    (_event: unknown, data: { openItems: string[] }) => {
      setOpenItems(data.openItems);
    },
    []
  );

  /**
   * Handle clear button click
   */
  const handleClear = React.useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation(); // Prevent accordion toggle
      onClear();
    },
    [onClear]
  );

  /**
   * Handle event link click (navigate to the related event)
   */
  const handleNavigate = React.useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      if (relatedEvent && onNavigate) {
        onNavigate(relatedEvent.id);
      }
    },
    [relatedEvent, onNavigate]
  );

  /**
   * Handle lookup button click
   */
  const handleLookup = React.useCallback(() => {
    onLookup();
  }, [onLookup]);

  // ─────────────────────────────────────────────────────────────────────────
  // Don't render if section is hidden by Event Type config
  // ─────────────────────────────────────────────────────────────────────────

  if (!visible) {
    return null;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Accordion
      className={styles.accordion}
      collapsible
      openItems={openItems}
      onToggle={handleToggle}
    >
      <AccordionItem className={styles.accordionItem} value="relatedEvent">
        <AccordionHeader className={styles.accordionHeader}>
          <div className={styles.headerContent}>
            <LinkRegular className={styles.headerIcon} />
            <Text className={styles.headerText}>Related Event</Text>
          </div>
        </AccordionHeader>
        <AccordionPanel className={styles.panel}>
          {/* Loading State */}
          {isLoading && (
            <div className={styles.loadingContainer}>
              <Spinner size="small" label="Loading..." />
            </div>
          )}

          {/* Content */}
          {!isLoading && (
            <div className={styles.lookupContainer}>
              {/* Selected Event Display */}
              {relatedEvent ? (
                <div className={styles.selectedEvent}>
                  <div className={styles.eventInfo}>
                    <CalendarMonthRegular className={styles.eventIcon} />
                    {onNavigate ? (
                      <Link
                        className={styles.eventLink}
                        onClick={handleNavigate}
                        title={relatedEvent.name}
                        aria-label={`Navigate to ${relatedEvent.name}`}
                      >
                        {relatedEvent.name}
                      </Link>
                    ) : (
                      <Text className={styles.eventName} title={relatedEvent.name}>
                        {relatedEvent.name}
                      </Text>
                    )}
                  </div>
                  {!disabled && (
                    <Button
                      className={styles.clearButton}
                      appearance="subtle"
                      icon={<DismissCircleRegular />}
                      onClick={handleClear}
                      aria-label="Clear related event"
                      title="Clear selection"
                    />
                  )}
                </div>
              ) : (
                /* Empty State with Lookup Button */
                <div className={styles.emptyState}>
                  <Text className={styles.emptyText}>
                    No related event selected
                  </Text>
                  {!disabled && (
                    <Button
                      className={styles.lookupButton}
                      appearance="secondary"
                      icon={<SearchRegular />}
                      onClick={handleLookup}
                      aria-label="Select related event"
                    >
                      Select Event
                    </Button>
                  )}
                </div>
              )}

              {/* Change Selection Button (when event is selected) */}
              {relatedEvent && !disabled && (
                <Button
                  appearance="subtle"
                  icon={<SearchRegular />}
                  onClick={handleLookup}
                  aria-label="Change related event"
                  size="small"
                >
                  Change Selection
                </Button>
              )}
            </div>
          )}
        </AccordionPanel>
      </AccordionItem>
    </Accordion>
  );
};

/**
 * Extract related event info from an event record
 *
 * @param record - Event record from Dataverse WebAPI
 * @returns Related event info or null if no related event
 */
export function extractRelatedEventInfo(
  record: Record<string, unknown>
): IRelatedEventInfo | null {
  const id = record["_sprk_relatedevent_value"] as string | undefined;
  const name = record[
    "_sprk_relatedevent_value@OData.Community.Display.V1.FormattedValue"
  ] as string | undefined;

  if (!id) {
    return null;
  }

  return {
    id,
    name: name ?? "Unknown Event",
  };
}

export default RelatedEventSection;
