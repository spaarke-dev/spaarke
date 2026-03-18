import * as React from "react";
import { useParams } from "react-router-dom";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  Badge,
  Divider,
  TabList,
  Tab,
  TabValue,
  Persona,
  Tooltip,
  Button,
} from "@fluentui/react-components";
import {
  FolderRegular,
  CalendarRegular,
  CheckmarkCircleRegular,
  PeopleRegular,
  PersonRegular,
  PersonAddRegular,
} from "@fluentui/react-icons";
import { getProjectById, getContacts, ODataProject, ODataContact } from "../api/web-api-client";
import { ApiError } from "../types";
import { AccessLevel } from "../types";
import {
  PageContainer,
  NavigationBar,
  SectionCard,
  ContactsOrganizations,
  DocumentLibrary,
  EventsCalendar,
  SmartTodo,
  AiToolbar,
  SemanticSearch,
  InviteUserDialog,
} from "../components";
import { useAccessLevel } from "../hooks/useAccessLevel";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  headerRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  headerMeta: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalM,
    alignItems: "center",
    flexWrap: "wrap",
  },
  referenceNumber: {
    fontFamily: tokens.fontFamilyMonospace,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  description: {
    color: tokens.colorNeutralForeground2,
    maxWidth: "800px",
  },
  participantsList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  participantRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  participantInfo: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    flex: "1",
  },
  participantEmail: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  tabContentArea: {
    paddingTop: tokens.spacingVerticalM,
    minHeight: "320px",
  },
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "400px",
    gap: tokens.spacingVerticalM,
  },
  emptyParticipants: {
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  statusBadgeActive: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground2,
  },
  divider: {
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
  },
  // Documents tab layout
  documentsTab: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  // Contacts tab header with Invite button
  contactsHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type ProjectTab = "documents" | "events" | "tasks" | "contacts";

interface ProjectPageParams {
  id: string;
  [key: string]: string | undefined;
}

// ---------------------------------------------------------------------------
// Status label helper
// ---------------------------------------------------------------------------

function getStatusLabel(status: number | null | undefined): string {
  switch (status) {
    case 1:
      return "Active";
    case 2:
      return "Closed";
    case 3:
      return "Archived";
    default:
      return "Active";
  }
}

function getStatusColor(
  status: number | null | undefined
): "brand" | "success" | "warning" | "danger" | "informative" | "important" | "severe" | undefined {
  switch (status) {
    case 1:
      return "success";
    case 2:
      return "warning";
    case 3:
      return "informative";
    default:
      return "success";
  }
}

// ---------------------------------------------------------------------------
// Access level label helper
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
      return "Unknown";
  }
}

function getAccessLevelColor(
  value: number
): "brand" | "success" | "warning" | "danger" | "informative" | "important" | "severe" | undefined {
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
// Participants section
// ---------------------------------------------------------------------------

interface ParticipantsSectionProps {
  contacts: ODataContact[];
  loading: boolean;
}

const ParticipantsSection: React.FC<ParticipantsSectionProps> = ({ contacts, loading }) => {
  const styles = useStyles();

  if (loading) {
    return (
      <SectionCard title="Participants">
        <Spinner size="tiny" label="Loading participants..." />
      </SectionCard>
    );
  }

  return (
    <SectionCard title="Participants">
      {contacts.length === 0 ? (
        <Text size={300} className={styles.emptyParticipants}>
          No participants found for this project.
        </Text>
      ) : (
        <div className={styles.participantsList}>
          {contacts.map((contact, index) => (
            <React.Fragment key={contact.contactid}>
              {index > 0 && <Divider className={styles.divider} />}
              <div className={styles.participantRow}>
                <Persona
                  name={contact.fullname ?? `${contact.firstname ?? ""} ${contact.lastname ?? ""}`.trim()}
                  secondaryText={contact.jobtitle ?? undefined}
                  avatar={{ icon: <PersonRegular /> }}
                  size="small"
                />
                <div className={styles.participantInfo}>
                  {contact.emailaddress1 && (
                    <Text size={200} className={styles.participantEmail}>
                      {contact.emailaddress1}
                    </Text>
                  )}
                </div>
                <Tooltip
                  content={getAccessLevelLabel(100000001)}
                  relationship="label"
                >
                  <Badge
                    appearance="tint"
                    color={getAccessLevelColor(100000001)}
                    size="small"
                  >
                    {getAccessLevelLabel(100000001)}
                  </Badge>
                </Tooltip>
              </div>
            </React.Fragment>
          ))}
        </div>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// Project metadata header
// ---------------------------------------------------------------------------

interface ProjectHeaderProps {
  project: ODataProject;
  accessLevel: AccessLevel;
}

const ProjectHeader: React.FC<ProjectHeaderProps> = ({ project, accessLevel }) => {
  const styles = useStyles();

  return (
    <div className={styles.headerRow}>
      <Text size={700} weight="semibold" as="h1">
        {project.sprk_name}
      </Text>

      <div className={styles.headerMeta}>
        <Text size={200} className={styles.referenceNumber}>
          {project.sprk_referencenumber}
        </Text>

        <Badge
          appearance="tint"
          color={getStatusColor(project.sprk_status)}
          size="medium"
        >
          {getStatusLabel(project.sprk_status)}
        </Badge>

        {project.sprk_issecure && (
          <Badge appearance="tint" color="warning" size="medium">
            Secure Project
          </Badge>
        )}

        {/* Show the current user's access level for this project */}
        <Tooltip
          content={`Your access level for this project: ${getAccessLevelLabel(accessLevel)}`}
          relationship="label"
        >
          <Badge
            appearance="outline"
            color={getAccessLevelColor(accessLevel)}
            size="medium"
          >
            {getAccessLevelLabel(accessLevel)}
          </Badge>
        </Tooltip>
      </div>

      {project.sprk_description && (
        <Text size={300} className={styles.description}>
          {project.sprk_description}
        </Text>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Documents tab content
// ---------------------------------------------------------------------------

interface DocumentsTabContentProps {
  projectId: string;
  accessLevel: AccessLevel;
}

const DocumentsTabContent: React.FC<DocumentsTabContentProps> = ({
  projectId,
  accessLevel,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.documentsTab}>
      {/* AI Toolbar — hidden for ViewOnly users (enforced inside AiToolbar) */}
      <AiToolbar
        projectId={projectId}
        accessLevel={accessLevel}
      />

      {/* Semantic Search — available for all access levels */}
      <SemanticSearch
        projectId={projectId}
        accessLevel={accessLevel}
      />

      {/* Document Library — upload/download hidden for ViewOnly */}
      <DocumentLibrary
        projectId={projectId}
        accessLevel={accessLevel}
      />
    </div>
  );
};

// ---------------------------------------------------------------------------
// Contacts tab content
// ---------------------------------------------------------------------------

interface ContactsTabContentProps {
  projectId: string;
  accessLevel: AccessLevel;
}

const ContactsTabContent: React.FC<ContactsTabContentProps> = ({
  projectId,
  accessLevel,
}) => {
  const styles = useStyles();
  const [inviteDialogOpen, setInviteDialogOpen] = React.useState<boolean>(false);
  const canUserInvite = accessLevel === AccessLevel.FullAccess;

  return (
    <div>
      {/* Invite button — only for FullAccess users */}
      {canUserInvite && (
        <div className={styles.contactsHeader}>
          <Text size={300}>
            Manage external users who have access to this project.
          </Text>
          <Button
            appearance="primary"
            icon={<PersonAddRegular />}
            onClick={() => setInviteDialogOpen(true)}
          >
            Invite User
          </Button>
        </div>
      )}

      {/* Contacts and Organizations read-only view */}
      <ContactsOrganizations projectId={projectId} />

      {/* Invite User Dialog — only rendered/usable for FullAccess users */}
      {canUserInvite && (
        <InviteUserDialog
          projectId={projectId}
          accessLevel={accessLevel}
          isOpen={inviteDialogOpen}
          onClose={() => setInviteDialogOpen(false)}
        />
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Main ProjectPage component
// ---------------------------------------------------------------------------

/**
 * ProjectPage — central hub page for a Secure Project (external user view).
 *
 * Reads the project `:id` from URL params via HashRouter route: #/project/:id.
 * Fetches project metadata and participants list from the Power Pages Web API.
 * Resolves the authenticated user's access level for this project via
 * useAccessLevel — which reads the user context from useExternalContext.
 *
 * Layout:
 *   1. Breadcrumb navigation (Home > Project name)
 *   2. Project metadata header (name, reference number, description, status, access level badge)
 *   3. Participants section (contacts with access levels)
 *   4. Tabbed content area:
 *      - Documents: AiToolbar (Collaborate+) + SemanticSearch (all) + DocumentLibrary (upload/download gated)
 *      - Events: EventsCalendar (create gated to Collaborate+)
 *      - Tasks: SmartTodo (create/edit gated to Collaborate+)
 *      - Contacts: ContactsOrganizations + InviteUserDialog button (FullAccess only)
 *
 * Access level enforcement:
 *   - ViewOnly    — read-only across all tabs; no upload, download, create, AI, or invite
 *   - Collaborate — read + write + AI; no invite
 *   - FullAccess  — all Collaborate capabilities + Invite User button in Contacts tab
 *
 * Note: Client-side enforcement is UX only. Server-side endpoint filters are
 * the security boundary (ADR-008, auth constraint).
 *
 * ADR-021: All styles use Fluent v9 design tokens. No hard-coded colors.
 * ADR-022: React 18 (createRoot used in main.tsx — this component is a pure function).
 */
export const ProjectPage: React.FC = () => {
  const styles = useStyles();
  const { id } = useParams<ProjectPageParams>();

  // ---------------------------------------------------------------------------
  // Access level resolution
  // ---------------------------------------------------------------------------

  // Resolve the authenticated user's access level for this project.
  // Falls back to ViewOnly (least privilege) while loading or if not found.
  const { accessLevel } = useAccessLevel(id);

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  const [project, setProject] = React.useState<ODataProject | null>(null);
  const [contacts, setContacts] = React.useState<ODataContact[]>([]);
  const [loadingProject, setLoadingProject] = React.useState<boolean>(true);
  const [loadingContacts, setLoadingContacts] = React.useState<boolean>(true);
  const [projectError, setProjectError] = React.useState<{ status: number; message: string } | null>(null);
  const [activeTab, setActiveTab] = React.useState<TabValue>("documents");

  // ---------------------------------------------------------------------------
  // Data fetching
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    if (!id) return;

    let cancelled = false;

    const fetchProject = async () => {
      setLoadingProject(true);
      setProjectError(null);

      try {
        const data = await getProjectById(id);
        if (!cancelled) {
          setProject(data);
        }
      } catch (err) {
        if (!cancelled) {
          if (err instanceof ApiError) {
            setProjectError({ status: err.statusCode, message: err.message });
          } else {
            setProjectError({ status: 0, message: "An unexpected error occurred loading the project." });
          }
        }
      } finally {
        if (!cancelled) {
          setLoadingProject(false);
        }
      }
    };

    void fetchProject();

    return () => {
      cancelled = true;
    };
  }, [id]);

  React.useEffect(() => {
    if (!id || !project) return;

    let cancelled = false;

    const fetchContacts = async () => {
      setLoadingContacts(true);

      try {
        const data = await getContacts(id);
        if (!cancelled) {
          setContacts(data);
        }
      } catch (err) {
        // Non-fatal: participants load failure should not block the page.
        console.error("[ProjectPage] Failed to load contacts:", err);
        if (!cancelled) {
          setContacts([]);
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
  }, [id, project]);

  // ---------------------------------------------------------------------------
  // Guard: missing URL param
  // ---------------------------------------------------------------------------

  if (!id) {
    return (
      <PageContainer>
        <MessageBar intent="error">
          <MessageBarBody>No project ID provided in the URL. Please navigate from the home page.</MessageBarBody>
        </MessageBar>
      </PageContainer>
    );
  }

  // ---------------------------------------------------------------------------
  // Loading state
  // ---------------------------------------------------------------------------

  if (loadingProject) {
    return (
      <PageContainer>
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Loading project..." />
        </div>
      </PageContainer>
    );
  }

  // ---------------------------------------------------------------------------
  // Error states
  // ---------------------------------------------------------------------------

  if (projectError) {
    if (projectError.status === 403) {
      return (
        <PageContainer>
          <NavigationBar
            items={[
              { label: "My Projects", href: "#/" },
              { label: "Access Denied" },
            ]}
          />
          <MessageBar intent="warning">
            <MessageBarBody>
              You do not have permission to view this project. If you believe this is an error,
              please contact the project team.
            </MessageBarBody>
          </MessageBar>
        </PageContainer>
      );
    }

    if (projectError.status === 404) {
      return (
        <PageContainer>
          <NavigationBar
            items={[
              { label: "My Projects", href: "#/" },
              { label: "Project Not Found" },
            ]}
          />
          <MessageBar intent="error">
            <MessageBarBody>
              The project could not be found. It may have been closed or the link may be incorrect.
            </MessageBarBody>
          </MessageBar>
        </PageContainer>
      );
    }

    return (
      <PageContainer>
        <NavigationBar
          items={[
            { label: "My Projects", href: "#/" },
            { label: "Error" },
          ]}
        />
        <MessageBar intent="error">
          <MessageBarBody>
            Failed to load project: {projectError.message}
          </MessageBarBody>
        </MessageBar>
      </PageContainer>
    );
  }

  // ---------------------------------------------------------------------------
  // Guard: project is null after successful load (unexpected)
  // ---------------------------------------------------------------------------

  if (!project) {
    return (
      <PageContainer>
        <MessageBar intent="error">
          <MessageBarBody>Project data could not be loaded. Please try again.</MessageBarBody>
        </MessageBar>
      </PageContainer>
    );
  }

  // ---------------------------------------------------------------------------
  // Tab content renderer
  // ---------------------------------------------------------------------------

  const renderTabContent = () => {
    switch (activeTab) {
      case "documents":
        return (
          <DocumentsTabContent
            projectId={id}
            accessLevel={accessLevel}
          />
        );
      case "events":
        return (
          <EventsCalendar
            projectId={id}
            accessLevel={accessLevel}
          />
        );
      case "tasks":
        return (
          <SmartTodo
            projectId={id}
            accessLevel={accessLevel}
          />
        );
      case "contacts":
        return (
          <ContactsTabContent
            projectId={id}
            accessLevel={accessLevel}
          />
        );
      default:
        return (
          <DocumentsTabContent
            projectId={id}
            accessLevel={accessLevel}
          />
        );
    }
  };

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <PageContainer>
      {/* Breadcrumb navigation */}
      <NavigationBar
        items={[
          { label: "My Projects", href: "#/" },
          { label: project.sprk_name },
        ]}
      />

      {/* Project metadata header — includes access level badge */}
      <ProjectHeader project={project} accessLevel={accessLevel} />

      {/* Participants section */}
      <ParticipantsSection
        contacts={contacts}
        loading={loadingContacts}
      />

      {/* Tabbed content area */}
      <SectionCard title="Project Content">
        <TabList
          selectedValue={activeTab}
          onTabSelect={(_ev, data) => setActiveTab(data.value)}
          appearance="subtle"
          size="medium"
        >
          <Tab value="documents" icon={<FolderRegular />}>
            Documents
          </Tab>
          <Tab value="events" icon={<CalendarRegular />}>
            Events
          </Tab>
          <Tab value="tasks" icon={<CheckmarkCircleRegular />}>
            Tasks
          </Tab>
          <Tab value="contacts" icon={<PeopleRegular />}>
            Contacts
          </Tab>
        </TabList>

        <div className={styles.tabContentArea}>{renderTabContent()}</div>
      </SectionCard>
    </PageContainer>
  );
};

export default ProjectPage;
