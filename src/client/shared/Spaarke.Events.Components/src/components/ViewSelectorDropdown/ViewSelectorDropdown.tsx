/**
 * ViewSelectorDropdown Component
 *
 * A dropdown component for selecting saved views for the Event entity.
 * Displays available views and allows switching between them.
 *
 * Task 093: Add View Selector Dropdown with Saved Views
 * - Uses known view GUIDs from Events-View-GUIDS.md
 * - Default view is "Active Events"
 * - Selection persists in session storage
 * - Dark mode supported via Fluent UI tokens
 *
 * Future enhancement: Dynamically fetch savedquery records from Dataverse
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/093-add-view-selector-dropdown.poml
 * @see projects/events-workspace-apps-UX-r1/notes/Events-View-GUIDS.md
 */

import * as React from "react";
import {
  Dropdown,
  Option,
  makeStyles,
  tokens,
  shorthands,
} from "@fluentui/react-components";
import { ChevronDown20Regular } from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface SavedView {
  /** View GUID */
  id: string;
  /** Display name */
  name: string;
  /** Entity logical name this view belongs to */
  entityName: string;
  /** Optional FetchXML (for future use) */
  fetchXml?: string;
  /** Optional LayoutXML (for future use) */
  layoutXml?: string;
}

export interface ViewSelectorDropdownProps {
  /** Currently selected view ID */
  selectedViewId: string;
  /** Callback when view selection changes */
  onViewChange: (viewId: string, viewName: string) => void;
  /** Optional: Override available views (default uses EVENT_VIEWS) */
  views?: SavedView[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants - Known Event Views
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Known saved views for the Event (sprk_event) entity.
 * These GUIDs are from the Dataverse environment.
 * @see projects/events-workspace-apps-UX-r1/notes/Events-View-GUIDS.md
 */
export const EVENT_VIEWS: SavedView[] = [
  {
    id: "7690f9d7-9cb1-4837-ac76-d0705e9e1b75",
    name: "Active Events",
    entityName: "sprk_event",
  },
  {
    id: "b836398f-6900-f111-8407-7c1e520aa4df",
    name: "All Events",
    entityName: "sprk_event",
  },
  {
    id: "32c1041a-ba02-f111-8407-7c1e520aa4df",
    name: "All Tasks",
    entityName: "sprk_event",
  },
  {
    id: "e0d27d71-ba02-f111-8407-7c1e520aa4df",
    name: "All Tasks Open",
    entityName: "sprk_event",
  },
];

/** Default view ID - "Active Events" */
export const DEFAULT_VIEW_ID = "7690f9d7-9cb1-4837-ac76-d0705e9e1b75";

/** Session storage key for persisting view selection */
const VIEW_STORAGE_KEY = "eventsPage_selectedViewId";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  dropdown: {
    minWidth: "200px",
    maxWidth: "300px",
    // OOB-style larger font for view title
    fontSize: "18px",
    fontWeight: tokens.fontWeightSemibold,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Hooks
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook to manage view selection state with session storage persistence.
 * @returns [selectedViewId, setSelectedViewId, selectedViewName]
 */
export function useViewSelection(): [string, (id: string) => void, string] {
  const [selectedViewId, setSelectedViewIdState] = React.useState<string>(() => {
    // Try to restore from session storage
    const stored = sessionStorage.getItem(VIEW_STORAGE_KEY);
    return stored || DEFAULT_VIEW_ID;
  });

  const setSelectedViewId = React.useCallback((id: string) => {
    setSelectedViewIdState(id);
    sessionStorage.setItem(VIEW_STORAGE_KEY, id);
  }, []);

  const selectedViewName = React.useMemo(() => {
    const view = EVENT_VIEWS.find((v) => v.id === selectedViewId);
    return view?.name || "Active Events";
  }, [selectedViewId]);

  return [selectedViewId, setSelectedViewId, selectedViewName];
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ViewSelectorDropdown - Dropdown for selecting saved views.
 *
 * Displays available Event views and allows the user to switch between them.
 * Selection is persisted in session storage.
 */
export const ViewSelectorDropdown: React.FC<ViewSelectorDropdownProps> = ({
  selectedViewId,
  onViewChange,
  views = EVENT_VIEWS,
}) => {
  const styles = useStyles();

  const selectedView = views.find((v) => v.id === selectedViewId);
  const selectedValue = selectedView?.name || "Active Events";

  const handleChange = React.useCallback(
    (_: unknown, data: { optionValue?: string; optionText?: string }) => {
      if (data.optionValue) {
        const view = views.find((v) => v.id === data.optionValue);
        onViewChange(data.optionValue, view?.name || "");
        console.log("[ViewSelector] View changed to:", data.optionValue, view?.name);
      }
    },
    [views, onViewChange]
  );

  return (
    <Dropdown
      className={styles.dropdown}
      value={selectedValue}
      selectedOptions={[selectedViewId]}
      onOptionSelect={handleChange}
      expandIcon={<ChevronDown20Regular />}
      appearance="underline"
    >
      {views.map((view) => (
        <Option key={view.id} value={view.id} text={view.name}>
          {view.name}
        </Option>
      ))}
    </Dropdown>
  );
};

export default ViewSelectorDropdown;
