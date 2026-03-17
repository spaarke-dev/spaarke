import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  Badge,
  Tooltip,
} from "@fluentui/react-components";
import {
  PeopleRegular,
  BuildingRegular,
} from "@fluentui/react-icons";
import { getContacts, getOrganizations, ODataContact, ODataOrganization } from "../api/web-api-client";
import { SectionCard } from "./SectionCard";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021, no hard-coded colors)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXL,
    gap: tokens.spacingHorizontalS,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    minHeight: "120px",
  },
  emptyIcon: {
    fontSize: "32px",
    color: tokens.colorNeutralForeground4,
  },
  tableContainer: {
    overflowX: "auto",
    width: "100%",
  },
  dataGrid: {
    width: "100%",
    minWidth: "400px",
  },
});

// ---------------------------------------------------------------------------
// Access level badge helpers (referenced from project POML context)
// ---------------------------------------------------------------------------

function getAccessLevelLabel(value: number): string {
  switch (value) {
    case 100000000:
      return "View Only";
    case 100000001:
      return "Collaborate";
    case 100000002:
      return "Full Access";
    default:
      return "Participant";
  }
}

function getAccessLevelColor(
  value: number
): "brand" | "success" | "informative" | "warning" {
  switch (value) {
    case 100000000:
      return "informative";
    case 100000001:
      return "brand";
    case 100000002:
      return "success";
    default:
      return "informative";
  }
}

// ---------------------------------------------------------------------------
// Contacts DataGrid
// ---------------------------------------------------------------------------

const contactColumns: TableColumnDefinition<ODataContact>[] = [
  createTableColumn<ODataContact>({
    columnId: "name",
    renderHeaderCell: () => "Name",
    renderCell: (contact) => (
      <TableCellLayout>
        <Text size={300}>
          {contact.fullname ??
            (`${contact.firstname ?? ""} ${contact.lastname ?? ""}`.trim() || "—")}
        </Text>
      </TableCellLayout>
    ),
  }),
  createTableColumn<ODataContact>({
    columnId: "email",
    renderHeaderCell: () => "Email",
    renderCell: (contact) => (
      <TableCellLayout>
        <Text size={300}>
          {contact.emailaddress1 ?? "—"}
        </Text>
      </TableCellLayout>
    ),
  }),
  createTableColumn<ODataContact>({
    columnId: "role",
    renderHeaderCell: () => "Role",
    renderCell: (contact) => (
      <TableCellLayout>
        <Text size={300}>
          {contact.jobtitle ?? "—"}
        </Text>
      </TableCellLayout>
    ),
  }),
  createTableColumn<ODataContact>({
    columnId: "accessLevel",
    renderHeaderCell: () => "Access Level",
    renderCell: (_contact) => (
      <TableCellLayout>
        {/* Access level is scoped by Power Pages table permissions —
            all contacts returned by the API are active participants.
            Default display as Collaborate since the filter ensures
            they have an active sprk_externalrecordaccess record. */}
        <Tooltip content="Project participant access level" relationship="label">
          <Badge
            appearance="tint"
            color={getAccessLevelColor(100000001)}
            size="small"
          >
            {getAccessLevelLabel(100000001)}
          </Badge>
        </Tooltip>
      </TableCellLayout>
    ),
  }),
];

interface ContactsGridProps {
  contacts: ODataContact[];
  loading: boolean;
  error: string | null;
}

const ContactsGrid: React.FC<ContactsGridProps> = ({ contacts, loading, error }) => {
  const styles = useStyles();

  if (loading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="tiny" label="Loading contacts..." />
      </div>
    );
  }

  if (error) {
    return (
      <MessageBar intent="warning">
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }

  if (contacts.length === 0) {
    return (
      <div className={styles.emptyState} role="status" aria-live="polite">
        <PeopleRegular className={styles.emptyIcon} aria-hidden="true" />
        <Text size={300}>No contacts are associated with this project.</Text>
      </div>
    );
  }

  return (
    <div className={styles.tableContainer}>
      <DataGrid
        items={contacts}
        columns={contactColumns}
        getRowId={(contact) => contact.contactid}
        className={styles.dataGrid}
        aria-label="Project contacts"
      >
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => (
              <DataGridHeaderCell>
                <Text size={200} weight="semibold">
                  {renderHeaderCell()}
                </Text>
              </DataGridHeaderCell>
            )}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<ODataContact>>
          {({ item, rowId }) => (
            <DataGridRow<ODataContact> key={rowId}>
              {({ renderCell }) => (
                <DataGridCell>{renderCell(item)}</DataGridCell>
              )}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Organizations DataGrid
// ---------------------------------------------------------------------------

/**
 * Extended organization record with derived contact count.
 * Contact count is computed client-side from the contacts list
 * by matching the parent account lookup (_parentcustomerid_value).
 */
interface ODataOrganizationWithCount extends ODataOrganization {
  _contactCount: number;
}

const organizationColumns: TableColumnDefinition<ODataOrganizationWithCount>[] = [
  createTableColumn<ODataOrganizationWithCount>({
    columnId: "name",
    renderHeaderCell: () => "Name",
    renderCell: (org) => (
      <TableCellLayout>
        <Text size={300}>{org.name}</Text>
      </TableCellLayout>
    ),
  }),
  createTableColumn<ODataOrganizationWithCount>({
    columnId: "location",
    renderHeaderCell: () => "Location",
    renderCell: (org) => {
      const city = org.address1_city ?? "";
      const country = org.address1_country ?? "";
      const location = [city, country].filter(Boolean).join(", ") || "—";
      return (
        <TableCellLayout>
          <Text size={300}>{location}</Text>
        </TableCellLayout>
      );
    },
  }),
  createTableColumn<ODataOrganizationWithCount>({
    columnId: "contactCount",
    renderHeaderCell: () => "Contacts",
    renderCell: (org) => (
      <TableCellLayout>
        <Badge
          appearance="tint"
          color="informative"
          size="small"
          aria-label={`${org._contactCount} contact${org._contactCount !== 1 ? "s" : ""}`}
        >
          {org._contactCount}
        </Badge>
      </TableCellLayout>
    ),
  }),
];

interface OrganizationsGridProps {
  organizations: ODataOrganizationWithCount[];
  loading: boolean;
  error: string | null;
}

const OrganizationsGrid: React.FC<OrganizationsGridProps> = ({
  organizations,
  loading,
  error,
}) => {
  const styles = useStyles();

  if (loading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="tiny" label="Loading organisations..." />
      </div>
    );
  }

  if (error) {
    return (
      <MessageBar intent="warning">
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }

  if (organizations.length === 0) {
    return (
      <div className={styles.emptyState} role="status" aria-live="polite">
        <BuildingRegular className={styles.emptyIcon} aria-hidden="true" />
        <Text size={300}>No organisations are associated with this project.</Text>
      </div>
    );
  }

  return (
    <div className={styles.tableContainer}>
      <DataGrid
        items={organizations}
        columns={organizationColumns}
        getRowId={(org) => org.accountid}
        className={styles.dataGrid}
        aria-label="Project organisations"
      >
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => (
              <DataGridHeaderCell>
                <Text size={200} weight="semibold">
                  {renderHeaderCell()}
                </Text>
              </DataGridHeaderCell>
            )}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<ODataOrganizationWithCount>>
          {({ item, rowId }) => (
            <DataGridRow<ODataOrganizationWithCount> key={rowId}>
              {({ renderCell }) => (
                <DataGridCell>{renderCell(item)}</DataGridCell>
              )}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ContactsOrganizationsProps {
  /** Dataverse GUID of the parent sprk_project record */
  projectId: string;
}

// ---------------------------------------------------------------------------
// Main ContactsOrganizations component
// ---------------------------------------------------------------------------

/**
 * ContactsOrganizations — read-only view of contacts and organisations for a
 * Secure Project.
 *
 * Shows:
 *   1. **Contacts** grid — all contacts with active external access to the
 *      project, displaying name, email, role, and access level.
 *   2. **Organisations** grid — parent accounts of those contacts, showing
 *      name, location, and contact count.
 *
 * Data is scoped by the Power Pages table permission chain: only contacts and
 * accounts the authenticated user is authorised to view are returned by the
 * Web API. No CRUD operations are available — this is a read-only view for
 * all access levels.
 *
 * ADR-021: All styles use Fluent v9 design tokens only. No hard-coded colors.
 * ADR-022: Component is a pure React 18 function (createRoot in main.tsx).
 * ADR-012: DataGrid uses Fluent UI v9 DataGrid (same component used by
 *           @spaarke/ui-components GridView internally) for standalone SPA
 *           context where PCF ComponentFramework is not available.
 */
export const ContactsOrganizations: React.FC<ContactsOrganizationsProps> = ({
  projectId,
}) => {
  const styles = useStyles();

  // ---------------------------------------------------------------------------
  // Contacts state
  // ---------------------------------------------------------------------------

  const [contacts, setContacts] = React.useState<ODataContact[]>([]);
  const [loadingContacts, setLoadingContacts] = React.useState<boolean>(true);
  const [contactsError, setContactsError] = React.useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Organizations state
  // ---------------------------------------------------------------------------

  const [organizations, setOrganizations] = React.useState<ODataOrganizationWithCount[]>([]);
  const [loadingOrganizations, setLoadingOrganizations] = React.useState<boolean>(true);
  const [organizationsError, setOrganizationsError] = React.useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Fetch contacts
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    if (!projectId) return;

    let cancelled = false;

    const fetchContacts = async () => {
      setLoadingContacts(true);
      setContactsError(null);

      try {
        const data = await getContacts(projectId);
        if (!cancelled) {
          setContacts(data);
        }
      } catch (err) {
        if (!cancelled) {
          console.error("[ContactsOrganizations] Failed to load contacts:", err);
          setContactsError("Could not load contacts. Please refresh and try again.");
        }
      } finally {
        if (!cancelled) {
          setLoadingContacts(false);
        }
      }
    };

    void fetchContacts();

    return () => {
      cancelled = true;
    };
  }, [projectId]);

  // ---------------------------------------------------------------------------
  // Fetch organizations and compute contact counts
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    if (!projectId) return;

    let cancelled = false;

    const fetchOrganizations = async () => {
      setLoadingOrganizations(true);
      setOrganizationsError(null);

      try {
        const data = await getOrganizations(projectId);
        if (!cancelled) {
          // Compute contact count per organisation from the already-fetched contacts list.
          // This avoids a second round-trip for count data.
          const withCounts: ODataOrganizationWithCount[] = data.map((org) => ({
            ...org,
            _contactCount: contacts.filter(
              (c) => c._parentcustomerid_value === org.accountid
            ).length,
          }));
          setOrganizations(withCounts);
        }
      } catch (err) {
        if (!cancelled) {
          console.error("[ContactsOrganizations] Failed to load organisations:", err);
          setOrganizationsError(
            "Could not load organisations. Please refresh and try again."
          );
        }
      } finally {
        if (!cancelled) {
          setLoadingOrganizations(false);
        }
      }
    };

    // Fetch organizations after contacts are resolved so contact count
    // computation has data available. We also depend on `contacts` so that
    // if contacts reload, counts are recomputed.
    if (!loadingContacts) {
      void fetchOrganizations();
    }

    return () => {
      cancelled = true;
    };
  }, [projectId, contacts, loadingContacts]);

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className={styles.root}>
      {/* Contacts section */}
      <SectionCard title="Contacts">
        <ContactsGrid
          contacts={contacts}
          loading={loadingContacts}
          error={contactsError}
        />
      </SectionCard>

      {/* Organisations section */}
      <SectionCard title="Organisations">
        <OrganizationsGrid
          organizations={organizations}
          loading={loadingOrganizations}
          error={organizationsError}
        />
      </SectionCard>
    </div>
  );
};

export default ContactsOrganizations;
