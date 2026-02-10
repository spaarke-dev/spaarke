/**
 * CalendarDrawer Component
 *
 * A Fluent UI Drawer/OverlayDrawer component that wraps the EventCalendarFilter.
 * Used to show calendar filtering in an overlay panel that doesn't take space from the grid.
 *
 * Task 091: Move Calendar to Side Pane
 * - Uses Fluent UI v9 OverlayDrawer for proper overlay behavior
 * - Grid takes full width when drawer is closed
 * - Drawer overlays from the right side
 * - Persists open/closed state in session storage
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/091-move-calendar-to-sidepane.poml
 */

import * as React from "react";
import {
  OverlayDrawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  makeStyles,
  tokens,
  shorthands,
} from "@fluentui/react-components";
import { Dismiss24Regular, Calendar24Regular } from "@fluentui/react-icons";
import { CalendarSection, CalendarFilterOutput } from "./CalendarSection";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface CalendarDrawerProps {
  /** Whether the drawer is open */
  isOpen: boolean;
  /** Callback when drawer should close */
  onClose: () => void;
  /** Event dates to show indicators on calendar */
  eventDates: string[];
  /** Callback when calendar filter changes */
  onFilterChange: (filter: CalendarFilterOutput | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  drawer: {
    // Drawer width to accommodate calendar
    width: "340px",
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
  },
  headerTitle: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  body: {
    ...shorthands.padding("0"),
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * CalendarDrawer - Overlay drawer containing the calendar filter.
 *
 * Renders the EventCalendarFilter inside a Fluent UI OverlayDrawer that
 * slides in from the right side of the screen. The grid underneath
 * remains at full width.
 */
export const CalendarDrawer: React.FC<CalendarDrawerProps> = ({
  isOpen,
  onClose,
  eventDates,
  onFilterChange,
}) => {
  const styles = useStyles();

  return (
    <OverlayDrawer
      open={isOpen}
      onOpenChange={(_, data) => {
        if (!data.open) {
          onClose();
        }
      }}
      position="end"
      className={styles.drawer}
    >
      <DrawerHeader>
        <div className={styles.header}>
          <DrawerHeaderTitle>
            <span className={styles.headerTitle}>
              <Calendar24Regular />
              Calendar Filter
            </span>
          </DrawerHeaderTitle>
          <Button
            appearance="subtle"
            aria-label="Close calendar"
            icon={<Dismiss24Regular />}
            onClick={onClose}
          />
        </div>
      </DrawerHeader>
      <DrawerBody className={styles.body}>
        <CalendarSection
          eventDates={eventDates}
          onFilterChange={onFilterChange}
        />
      </DrawerBody>
    </OverlayDrawer>
  );
};

export default CalendarDrawer;
