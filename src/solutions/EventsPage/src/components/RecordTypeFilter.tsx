/**
 * RecordTypeFilter Component
 *
 * Multi-select dropdown filter for filtering events by Event Type (sprk_eventtype).
 * Uses Fluent UI v9 Combobox with multi-select capability.
 *
 * Features:
 * - Fetches event types from Dataverse sprk_eventtype entity
 * - Multi-select with chips display
 * - Search/filter capability
 * - Dark mode support via design tokens
 *
 * Note: "Record Type" in the UI refers to the Event Type lookup field (sprk_eventtypeid).
 * This allows users to filter events by categories like Hearing, Filing, Regulatory, etc.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/064-events-page-regarding-column.poml
 * @see .claude/adr/ADR-021-fluent-design-system.md
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Combobox,
  Option,
  Spinner,
  Text,
} from "@fluentui/react-components";
import { Tag20Regular } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Xrm Type Declaration
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Event Type record from Dataverse sprk_eventtype entity
 */
export interface IEventTypeOption {
  id: string;
  name: string;
  /** Optional description for tooltip/secondary text */
  description?: string;
  /** Color code for visual indicator (if defined in entity) */
  colorCode?: string;
}

/**
 * Props for RecordTypeFilter component
 */
export interface RecordTypeFilterProps {
  /** Currently selected event type IDs */
  selectedTypeIds: string[];
  /** Callback when selection changes */
  onSelectionChange: (typeIds: string[]) => void;
  /** Placeholder text when no selection */
  placeholder?: string;
  /** Disable the filter */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
  },
  label: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  combobox: {
    minWidth: "180px",
    maxWidth: "280px",
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    ...shorthands.padding("8px"),
  },
  errorText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorPaletteRedForeground1,
  },
  optionContent: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
  },
  optionIcon: {
    color: tokens.colorBrandForeground1,
  },
  optionText: {
    fontSize: tokens.fontSizeBase200,
  },
  optionDescription: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    marginLeft: "auto",
  },
});

// ---------------------------------------------------------------------------
// Helper Functions
// ---------------------------------------------------------------------------

/**
 * Check if Xrm WebApi is available
 */
function isXrmAvailable(): boolean {
  return !!(typeof Xrm !== "undefined" && Xrm.WebApi);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RecordTypeFilter: React.FC<RecordTypeFilterProps> = ({
  selectedTypeIds,
  onSelectionChange,
  placeholder = "Event type...",
  disabled = false,
}) => {
  const styles = useStyles();

  // State
  const [eventTypes, setEventTypes] = React.useState<IEventTypeOption[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [searchText, setSearchText] = React.useState("");

  /**
   * Fetch event types from Dataverse or return mock data
   */
  const fetchEventTypes = React.useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      if (!isXrmAvailable()) {
        // Mock data for development/testing outside Dataverse
        console.warn(
          "[RecordTypeFilter] Xrm.WebApi not available. Using mock data."
        );
        const mockTypes = getMockEventTypes();
        setEventTypes(mockTypes);
        setLoading(false);
        return;
      }

      // Fetch active event types from sprk_eventtype
      // Order by name for consistent display
      const result = await Xrm.WebApi.retrieveMultipleRecords(
        "sprk_eventtype",
        "?$select=sprk_eventtypeid,sprk_name,sprk_description" +
          "&$filter=statecode eq 0" + // Active only
          "&$orderby=sprk_name asc" +
          "&$top=100"
      );

      const fetchedTypes: IEventTypeOption[] = (result.entities || []).map(
        (type: any) => ({
          id: type.sprk_eventtypeid,
          name: type.sprk_name || "Unnamed Type",
          description: type.sprk_description,
        })
      );

      setEventTypes(fetchedTypes);
    } catch (err) {
      console.error("[RecordTypeFilter] Error fetching event types:", err);
      setError(
        err instanceof Error ? err.message : "Failed to load event types"
      );
    } finally {
      setLoading(false);
    }
  }, []);

  // Fetch event types on mount
  React.useEffect(() => {
    fetchEventTypes();
  }, [fetchEventTypes]);

  /**
   * Handle combobox selection change
   */
  const handleOptionSelect = React.useCallback(
    (
      _event: any,
      data: { optionValue?: string; selectedOptions: string[] }
    ) => {
      onSelectionChange(data.selectedOptions);
    },
    [onSelectionChange]
  );

  /**
   * Handle search text change
   */
  const handleSearchChange = React.useCallback(
    (_event: any, data: { value: string }) => {
      setSearchText(data.value);
    },
    []
  );

  // Filter event types based on search text
  const filteredTypes = React.useMemo(() => {
    if (!searchText) return eventTypes;
    const search = searchText.toLowerCase();
    return eventTypes.filter(
      (type) =>
        type.name.toLowerCase().includes(search) ||
        (type.description?.toLowerCase().includes(search) ?? false)
    );
  }, [eventTypes, searchText]);

  // Get display value for selected types
  const selectedValue = React.useMemo(() => {
    if (selectedTypeIds.length === 0) return "";
    const selectedTypes = eventTypes.filter((t) =>
      selectedTypeIds.includes(t.id)
    );
    if (selectedTypes.length === 1) {
      return selectedTypes[0].name;
    }
    return `${selectedTypes.length} types selected`;
  }, [selectedTypeIds, eventTypes]);

  // Render loading state
  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loadingContainer}>
          <Spinner size="tiny" />
          <Text size={200}>Loading types...</Text>
        </div>
      </div>
    );
  }

  // Render error state
  if (error) {
    return (
      <div className={styles.container}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <Combobox
        className={styles.combobox}
        placeholder={placeholder}
        multiselect
        selectedOptions={selectedTypeIds}
        onOptionSelect={handleOptionSelect}
        value={selectedValue}
        onInput={handleSearchChange}
        disabled={disabled}
        aria-label="Filter by event type"
      >
        {filteredTypes.map((type) => (
          <Option key={type.id} value={type.id} text={type.name}>
            <div className={styles.optionContent}>
              <Tag20Regular className={styles.optionIcon} />
              <Text className={styles.optionText}>{type.name}</Text>
              {type.description && (
                <Text className={styles.optionDescription}>
                  {type.description}
                </Text>
              )}
            </div>
          </Option>
        ))}
        {filteredTypes.length === 0 && (
          <Option value="" text="" disabled>
            No event types found
          </Option>
        )}
      </Combobox>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Mock Data (for development outside Dataverse)
// ---------------------------------------------------------------------------

function getMockEventTypes(): IEventTypeOption[] {
  return [
    {
      id: "type-1",
      name: "Filing Deadline",
      description: "Court filing deadlines",
    },
    {
      id: "type-2",
      name: "Meeting",
      description: "Client and internal meetings",
    },
    {
      id: "type-3",
      name: "Task",
      description: "General tasks and to-dos",
    },
    {
      id: "type-4",
      name: "Hearing",
      description: "Court hearings and appearances",
    },
    {
      id: "type-5",
      name: "Regulatory",
      description: "Regulatory compliance events",
    },
    {
      id: "type-6",
      name: "Reminder",
      description: "General reminders",
    },
  ];
}

export default RecordTypeFilter;
