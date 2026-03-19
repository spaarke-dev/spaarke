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
  TabList,
  Tab,
  TabValue,
  Button,
  Tooltip,
  Field,
  Input,
  Textarea,
} from "@fluentui/react-components";
import {
  CalendarRegular,
  CheckmarkCircleRegular,
  PeopleRegular,
  PersonAddRegular,
  MailRegular,
  AlertRegular,
  Sparkle20Regular,
} from "@fluentui/react-icons";
import { getProjectById, ODataProject } from "../api/web-api-client";
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
  // Page header: reference number + project name + badges + icon row
  pageHeader: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalM,
  },
  pageHeaderTitleRow: {
    display: "flex",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
  },
  pageHeaderLeft: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    flex: "1",
  },
  pageHeaderIcons: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
    paddingTop: tokens.spacingVerticalXS,
  },
  projectSubtitle: {
    color: tokens.colorNeutralForeground2,
  },
  badgeRow: {
    display: "flex",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalXXS,
  },
  // Tab content
  tabContentArea: {
    paddingTop: tokens.spacingVerticalM,
    minHeight: "320px",
  },
  // Overview tab
  overviewTab: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  infoGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    "@media (max-width: 600px)": {
      gridTemplateColumns: "1fr",
    },
  },
  fullWidth: {
    gridColumn: "1 / -1",
  },
  // Documents section
  documentsSection: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  // Calendar tab
  calendarTab: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  // Contacts tab
  contactsHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: tokens.spacingVerticalS,
  },
  // Loading
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "400px",
    gap: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type ProjectTab = "overview" | "calendar" | "contacts";

interface ProjectPageParams {
  id: string;
  [key: string]: string | undefined;
}

// ---------------------------------------------------------------------------
// Status helpers
// ---------------------------------------------------------------------------

function getStatusLabel(status: number | null | undefined): string {
  switch (status) {
    case 1: return "Active";
    case 2: return "Closed";
    case 3: return "Archived";
    default: return "Active";
  }
}

function getStatusColor(
  status: number | null | undefined
): "brand" | "success" | "warning" | "danger" | "informative" | "important" | "severe" | undefined {
  switch (status) {
    case 1: return "success";
    case 2: return "warning";
    case 3: return "informative";
    default: return "success";
  }
}

function getAccessLevelLabel(value: number): string {
  switch (value) {
    case 100000000: return "View Only";
    case 100000001: return "Collaborate";
    case 100000002: return "Full Access";
    default: return "Unknown";
  }
}

function getAccessLevelColor(
  value: number
): "brand" | "success" | "warning" | "danger" | "informative" | "important" | "severe" | undefined {
  switch (value) {
    case 100000000: return "informative";
    case 100000001: return "brand";
    case 100000002: return "success";
    default: return "informative";
  }
}

// ---------------------------------------------------------------------------
// Overview tab — Project Information + Documents
// ---------------------------------------------------------------------------

interface OverviewTabContentProps {
  project: ODataProject;
  projectId: string;
  accessLevel: AccessLevel;
}

const OverviewTabContent: React.FC<OverviewTabContentProps> = ({
  project,
  projectId,
  accessLevel,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.overviewTab}>
      {/* PROJECT INFORMATION */}
      <SectionCard title="Project Information">
        <div className={styles.infoGrid}>
          <Field label="Project Number">
            <Input
              value={project.sprk_referencenumber ?? ""}
              readOnly
              appearance="outline"
            />
          </Field>
          <Field label="Project Name">
            <Input
              value={project.sprk_name ?? ""}
              readOnly
              appearance="outline"
            />
          </Field>
          <Field label="Project Type">
            <Input
              value={"—"}
              readOnly
              appearance="outline"
            />
          </Field>
          <Field label="Matter Type">
            <Input
              value={"—"}
              readOnly
              appearance="outline"
            />
          </Field>
          <Field label="Practice Area">
            <Input
              value={"—"}
              readOnly
              appearance="outline"
            />
          </Field>
          <div className={styles.fullWidth}>
            <Field label="Project Description">
              <Textarea
                value={project.sprk_description ?? ""}
                readOnly
                appearance="outline"
                resize="vertical"
              />
            </Field>
          </div>
        </div>
      </SectionCard>

      {/* DOCUMENTS */}
      <SectionCard title="Documents">
        <div className={styles.documentsSection}>
          {/* AI Toolbar — hidden for ViewOnly users (enforced inside AiToolbar) */}
          <AiToolbar
            projectId={projectId}
            accessLevel={accessLevel}
          />

          {/* Semantic Search */}
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
      </SectionCard>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Calendar tab — EventsCalendar + SmartTodo
// ---------------------------------------------------------------------------

interface CalendarTabContentProps {
  projectId: string;
  accessLevel: AccessLevel;
}

const CalendarTabContent: React.FC<CalendarTabContentProps> = ({
  projectId,
  accessLevel,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.calendarTab}>
      <EventsCalendar
        projectId={projectId}
        accessLevel={accessLevel}
      />
      <SmartTodo
        projectId={projectId}
        accessLevel={accessLevel}
      />
    </div>
  );
};

// ---------------------------------------------------------------------------
// Contacts tab — ContactsOrganizations + InviteUserDialog
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
 * ProjectPage — external user's view of a single Secure Project.
 *
 * Layout (matches mock-up):
 * ┌──────────────────────────────────────────────────────────────┐
 * │ ← My Workspace                                               │
 * │ PRJT.10001.01                  [Mail] [✓] [Bell] [Sparkle]   │
 * │ Project Name                                                  │
 * │ [Status badge] [Access badge]                                │
 * ├──────────────────────────────────────────────────────────────┤
 * │ OVERVIEW   CALENDAR   CONTACTS                               │
 * ├──────────────────────────────────────────────────────────────┤
 * │ Overview: Project Information (form) + Documents section     │
 * │ Calendar: EventsCalendar + SmartTodo                         │
 * │ Contacts: ContactsOrganizations + InviteUserDialog           │
 * └──────────────────────────────────────────────────────────────┘
 *
 * Access level enforcement:
 *   - ViewOnly    — read-only across all tabs; no upload, create, AI, or invite
 *   - Collaborate — read + write + AI; no invite
 *   - FullAccess  — all Collaborate capabilities + Invite User button in Contacts tab
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

  const { accessLevel } = useAccessLevel(id);

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  const [project, setProject] = React.useState<ODataProject | null>(null);
  const [loadingProject, setLoadingProject] = React.useState<boolean>(true);
  const [projectError, setProjectError] = React.useState<{ status: number; message: string } | null>(null);
  const [activeTab, setActiveTab] = React.useState<TabValue>("overview");

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
              { label: "My Workspace", href: "#/" },
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
              { label: "My Workspace", href: "#/" },
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
            { label: "My Workspace", href: "#/" },
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
  // Render
  // ---------------------------------------------------------------------------

  return (
    <PageContainer>
      {/* Breadcrumb */}
      <NavigationBar
        items={[
          { label: "My Workspace", href: "#/" },
          { label: project.sprk_referencenumber ?? project.sprk_name },
        ]}
      />

      {/* Page header: reference number as title + page-level icon row */}
      <div className={styles.pageHeader}>
        <div className={styles.pageHeaderTitleRow}>
          <div className={styles.pageHeaderLeft}>
            <Text size={700} weight="semibold" as="h1">
              {project.sprk_referencenumber ?? project.sprk_name}
            </Text>
            {project.sprk_referencenumber && (
              <Text size={400} className={styles.projectSubtitle}>
                {project.sprk_name}
              </Text>
            )}
            <div className={styles.badgeRow}>
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
          </div>

          {/* Page-level action icons */}
          <div className={styles.pageHeaderIcons}>
            <Tooltip content="Send email" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<MailRegular />}
                aria-label="Send email"
              />
            </Tooltip>
            <Tooltip content="Tasks" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<CheckmarkCircleRegular />}
                aria-label="Tasks"
              />
            </Tooltip>
            <Tooltip content="Notifications" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<AlertRegular />}
                aria-label="Notifications"
              />
            </Tooltip>
            <Tooltip content="AI Assistant" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<Sparkle20Regular />}
                aria-label="AI Assistant"
              />
            </Tooltip>
          </div>
        </div>
      </div>

      {/* Tabbed content */}
      <TabList
        selectedValue={activeTab}
        onTabSelect={(_ev, data) => setActiveTab(data.value)}
        appearance="subtle"
        size="medium"
      >
        <Tab value="overview">
          Overview
        </Tab>
        <Tab value="calendar" icon={<CalendarRegular />}>
          Calendar
        </Tab>
        <Tab value="contacts" icon={<PeopleRegular />}>
          Contacts
        </Tab>
      </TabList>

      <div className={styles.tabContentArea}>
        {activeTab === "overview" && (
          <OverviewTabContent
            project={project}
            projectId={id}
            accessLevel={accessLevel}
          />
        )}
        {activeTab === "calendar" && (
          <CalendarTabContent
            projectId={id}
            accessLevel={accessLevel}
          />
        )}
        {activeTab === "contacts" && (
          <ContactsTabContent
            projectId={id}
            accessLevel={accessLevel}
          />
        )}
      </div>
    </PageContainer>
  );
};

export default ProjectPage;
