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
import { CalendarSection, CalendarFilterOutput, IEventDateInfo } from "./CalendarSection";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface CalendarDrawerProps {
  /** Whether the drawer is open */
  isOpen: boolean;
  /** Callback when drawer should close */
  onClose: () => void;
  /**
   * Event dates (with counts and optional overdue flags) to show indicators
   * on the calendar.
   *
   * Task 064 (R4 B-8, 2026-05-26): prop type upgraded from `string[]` to the
   * rich `IEventDateInfo[]` shape so CalendarSection can render event-count
   * badges (`count > 1`) and overdue indicators (`overdue === true`) per
   * FR-11. Previously this prop was typed as `string[]` and the value was
   * bridged via an `as unknown as IEventDateInfo[]` cast — that cast is now
   * removed because call sites supply the richer shape directly. Behavior
   * parity preserved for surfaces that only need the indicator: pass
   * `[{ date, count: 1 }]` to get the prior "dot only" rendering.
   */
  eventDates: IEventDateInfo[];
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
        {/*
          Task 064 (R4 B-8): bridging cast removed. The CalendarDrawerProps
          API now matches the CalendarSection IEventDateInfo[] contract, so
          eventDates flows through as-is. The pre-existing R3 task 114 note
          (recorded the drift) is preserved in the deploy notes archive at
          projects/spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md.
        */}
        <CalendarSection
          eventDates={eventDates}
          onFilterChange={onFilterChange}
        />
      </DrawerBody>
    </OverlayDrawer>
  );
};

export default CalendarDrawer;
