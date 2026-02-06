/**
 * AssignedToFilter Component
 *
 * Multi-select dropdown filter for filtering events by assigned user (owner).
 * Uses Fluent UI v9 Combobox with multi-select capability.
 *
 * Features:
 * - Fetches users from Dataverse systemuser entity
 * - Multi-select with chips display
 * - Current user selected by default
 * - Search/filter capability
 * - Dark mode support via design tokens
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/063-events-page-integrate-sidepane.poml
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
  Persona,
} from "@fluentui/react-components";
import { Person20Regular } from "@fluentui/react-icons";

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
 * User record from Dataverse systemuser entity
 */
export interface IUserOption {
  id: string;
  fullname: string;
  internalemailaddress?: string;
  /** True if this is the current logged-in user */
  isCurrentUser?: boolean;
}

/**
 * Props for AssignedToFilter component
 */
export interface AssignedToFilterProps {
  /** Currently selected user IDs */
  selectedUserIds: string[];
  /** Callback when selection changes */
  onSelectionChange: (userIds: string[]) => void;
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
    minWidth: "200px",
    maxWidth: "300px",
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
  currentUserBadge: {
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
  return !!(typeof Xrm !== "undefined" && Xrm.WebApi && Xrm.Utility);
}

/**
 * Get current user ID from Xrm context
 */
function getCurrentUserId(): string | null {
  if (typeof Xrm === "undefined" || !Xrm.Utility) {
    return null;
  }
  try {
    const globalContext = Xrm.Utility.getGlobalContext();
    return globalContext?.userSettings?.userId?.replace(/[{}]/g, "") || null;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AssignedToFilter: React.FC<AssignedToFilterProps> = ({
  selectedUserIds,
  onSelectionChange,
  placeholder = "Assigned to...",
  disabled = false,
}) => {
  const styles = useStyles();

  // State
  const [users, setUsers] = React.useState<IUserOption[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [searchText, setSearchText] = React.useState("");

  /**
   * Fetch users from Dataverse or return mock data
   */
  const fetchUsers = React.useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      if (!isXrmAvailable()) {
        // Mock data for development/testing outside Dataverse
        console.warn(
          "[AssignedToFilter] Xrm.WebApi not available. Using mock data."
        );
        const mockUsers = getMockUsers();
        setUsers(mockUsers);

        // Auto-select current user (mock)
        if (selectedUserIds.length === 0) {
          const currentUser = mockUsers.find((u) => u.isCurrentUser);
          if (currentUser) {
            onSelectionChange([currentUser.id]);
          }
        }
        setLoading(false);
        return;
      }

      // Get current user ID
      const currentUserId = getCurrentUserId();

      // Fetch active users from systemuser
      // Filter: Only active users, exclude system accounts
      const result = await Xrm.WebApi.retrieveMultipleRecords(
        "systemuser",
        "?$select=systemuserid,fullname,internalemailaddress" +
          "&$filter=isdisabled eq false and accessmode ne 4" + // accessmode 4 = System
          "&$orderby=fullname asc" +
          "&$top=100"
      );

      const fetchedUsers: IUserOption[] = (result.entities || []).map(
        (user: any) => ({
          id: user.systemuserid,
          fullname: user.fullname || "Unknown User",
          internalemailaddress: user.internalemailaddress,
          isCurrentUser: user.systemuserid === currentUserId,
        })
      );

      setUsers(fetchedUsers);

      // Auto-select current user if no selection
      if (selectedUserIds.length === 0 && currentUserId) {
        const currentUser = fetchedUsers.find((u) => u.id === currentUserId);
        if (currentUser) {
          onSelectionChange([currentUser.id]);
        }
      }
    } catch (err) {
      console.error("[AssignedToFilter] Error fetching users:", err);
      setError(err instanceof Error ? err.message : "Failed to load users");
    } finally {
      setLoading(false);
    }
  }, [selectedUserIds.length, onSelectionChange]);

  // Fetch users on mount
  React.useEffect(() => {
    fetchUsers();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  // Filter users based on search text
  const filteredUsers = React.useMemo(() => {
    if (!searchText) return users;
    const search = searchText.toLowerCase();
    return users.filter(
      (user) =>
        user.fullname.toLowerCase().includes(search) ||
        (user.internalemailaddress?.toLowerCase().includes(search) ?? false)
    );
  }, [users, searchText]);

  // Get display value for selected users
  const selectedValue = React.useMemo(() => {
    if (selectedUserIds.length === 0) return "";
    const selectedUsers = users.filter((u) => selectedUserIds.includes(u.id));
    if (selectedUsers.length === 1) {
      return selectedUsers[0].fullname;
    }
    return `${selectedUsers.length} users selected`;
  }, [selectedUserIds, users]);

  // Render loading state
  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loadingContainer}>
          <Spinner size="tiny" />
          <Text size={200}>Loading users...</Text>
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
        selectedOptions={selectedUserIds}
        onOptionSelect={handleOptionSelect}
        value={selectedValue}
        onInput={handleSearchChange}
        disabled={disabled}
        aria-label="Filter by assigned user"
      >
        {filteredUsers.map((user) => (
          <Option
            key={user.id}
            value={user.id}
            text={user.fullname}
          >
            <div className={styles.optionContent}>
              <Persona
                name={user.fullname}
                secondaryText={user.internalemailaddress}
                avatar={{ icon: <Person20Regular /> }}
                size="small"
              />
              {user.isCurrentUser && (
                <Text className={styles.currentUserBadge}>(me)</Text>
              )}
            </div>
          </Option>
        ))}
        {filteredUsers.length === 0 && (
          <Option value="" text="" disabled>
            No users found
          </Option>
        )}
      </Combobox>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Mock Data (for development outside Dataverse)
// ---------------------------------------------------------------------------

function getMockUsers(): IUserOption[] {
  // Mock user GUIDs for development/testing (valid GUID format)
  return [
    {
      id: "00000000-0000-0000-0000-000000000001",
      fullname: "Current User",
      internalemailaddress: "current.user@example.com",
      isCurrentUser: true,
    },
    {
      id: "00000000-0000-0000-0000-000000000003",
      fullname: "John Smith",
      internalemailaddress: "john.smith@example.com",
      isCurrentUser: false,
    },
    {
      id: "00000000-0000-0000-0000-000000000002",
      fullname: "Jane Doe",
      internalemailaddress: "jane.doe@example.com",
      isCurrentUser: false,
    },
    {
      id: "00000000-0000-0000-0000-000000000004",
      fullname: "Bob Johnson",
      internalemailaddress: "bob.johnson@example.com",
      isCurrentUser: false,
    },
    {
      id: "00000000-0000-0000-0000-000000000005",
      fullname: "Alice Williams",
      internalemailaddress: "alice.williams@example.com",
      isCurrentUser: false,
    },
  ];
}

export default AssignedToFilter;
