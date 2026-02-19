/**
 * MyPortfolioWidget — tabbed sidebar widget for Block 5 of the Legal Operations Workspace.
 *
 * Renders three tabs: Matters, Projects, Documents, each with a count badge.
 * All three tabs are fully implemented with data-driven content:
 *   - Matters tab:   MatterItem rows with status, grades, overdue indicator
 *   - Projects tab:  ProjectItem rows with status badge (Active/Planning)
 *   - Documents tab: DocumentItem rows with file type icon, type badge, timestamp
 *
 * Tab layout uses Fluent UI v9 TabList/Tab components.
 * All colours and spacing come from Fluent semantic tokens (makeStyles / Griffel).
 * Footer navigation posts a postMessage to the parent MDA frame for each tab.
 *
 * Props:
 *   service  — DataverseService instance (from useDataverseService hook)
 *   userId   — Current user GUID from PCF context (context.userSettings.userId)
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  TabList,
  Tab,
  Text,
  Badge,
  Button,
  MessageBar,
  MessageBarBody,
  Skeleton,
  SkeletonItem,
  SelectTabData,
  SelectTabEvent,
} from '@fluentui/react-components';
import {
  BriefcaseRegular,
  FolderRegular,
  DocumentRegular,
  ArrowRightRegular,
  ArrowClockwiseRegular,
} from '@fluentui/react-icons';
import { DataverseService } from '../../services/DataverseService';
import { useMattersList } from '../../hooks/useMattersList';
import { useProjectsList } from '../../hooks/useProjectsList';
import { useDocumentsList } from '../../hooks/useDocumentsList';
import { navigateToEntity } from '../../utils/navigation';
import { PortfolioTab } from '../../types/enums';
import { MatterItem } from './MatterItem';
import { ProjectItem } from './ProjectItem';
import { DocumentItem } from './DocumentItem';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  widget: {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    height: '100%',
    minHeight: '280px',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: '0px',
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalXS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    gap: tokens.spacingHorizontalS,
    flexShrink: '0',
  },
  title: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    marginLeft: 'auto',
    paddingBottom: tokens.spacingVerticalXS,
  },
  tabList: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: '0',
  },
  tabLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  tabContent: {
    flex: '1 1 auto',
    overflowY: 'auto',
    overflowX: 'hidden',
    display: 'flex',
    flexDirection: 'column',
  },
  matterList: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
  },
  projectList: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
  },
  documentList: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  skeletonRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  errorContainer: {
    margin: tokens.spacingVerticalS,
    marginLeft: tokens.spacingHorizontalM,
    marginRight: tokens.spacingHorizontalM,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
    flex: '1 1 auto',
  },
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: '32px',
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  footer: {
    flexShrink: '0',
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  footerButton: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightRegular,
  },
});

// ---------------------------------------------------------------------------
// Skeleton placeholder for loading state
// ---------------------------------------------------------------------------

const LoadingSkeleton: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.loadingContainer} aria-busy="true" aria-label="Loading items">
      {[1, 2, 3].map((i) => (
        <div key={i} className={styles.skeletonRow}>
          <Skeleton>
            <SkeletonItem size={16} style={{ width: '70%' }} />
            <SkeletonItem size={12} style={{ width: '45%', marginTop: tokens.spacingVerticalXXS }} />
          </Skeleton>
        </div>
      ))}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Tab label with count badge
// ---------------------------------------------------------------------------

interface ITabLabelProps {
  label: string;
  icon: React.ReactElement;
  count?: number;
  isLoading?: boolean;
}

const TabLabel: React.FC<ITabLabelProps> = ({ label, icon, count, isLoading }) => {
  const styles = useStyles();
  return (
    <span className={styles.tabLabel}>
      <span aria-hidden="true">{icon}</span>
      {label}
      {!isLoading && count !== undefined && count > 0 && (
        <Badge
          size="small"
          color="brand"
          appearance="filled"
          aria-label={`${count} ${label.toLowerCase()}`}
        >
          {count}
        </Badge>
      )}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IMyPortfolioWidgetProps {
  /**
   * DataverseService instance. Obtain via useDataverseService(context.webAPI).
   * Passed as a prop so the parent can provide a stable, memoized instance.
   */
  service: DataverseService;
  /**
   * Current user GUID from PCF context.userSettings.userId.
   * The widget waits for a non-empty value before fetching data.
   */
  userId: string;
  /**
   * Optional: initial tab to display. Defaults to 'matters'.
   */
  initialTab?: PortfolioTab;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * MyPortfolioWidget renders a tabbed sidebar widget with Matters, Projects,
 * and Documents tabs. All three tabs have full data-driven content with
 * max 5 items and a "View All" footer that navigates to the entity view in MDA.
 */
export const MyPortfolioWidget: React.FC<IMyPortfolioWidgetProps> = ({
  service,
  userId,
  initialTab = 'matters',
}) => {
  const styles = useStyles();
  const [activeTab, setActiveTab] = React.useState<PortfolioTab>(initialTab);

  // Data fetching for all three tabs
  const { matters, isLoading: mattersLoading, error: mattersError, totalCount: mattersTotalCount, refetch: refetchMatters } = useMattersList(
    service,
    userId,
    { top: 5 }
  );
  const { projects, isLoading: projectsLoading, error: projectsError, totalCount: projectsTotalCount, refetch: refetchProjects } = useProjectsList(
    service,
    userId,
    { top: 5 }
  );
  const { documents, isLoading: documentsLoading, error: documentsError, totalCount: documentsTotalCount, refetch: refetchDocuments } = useDocumentsList(
    service,
    userId,
    { top: 5 }
  );

  // ---------------------------------------------------------------------------
  // Handlers
  // ---------------------------------------------------------------------------

  const handleTabSelect = React.useCallback(
    (_event: SelectTabEvent, data: SelectTabData) => {
      setActiveTab(data.value as PortfolioTab);
    },
    []
  );

  const handleViewAllMatters = React.useCallback(() => {
    navigateToEntity({
      action: 'openView',
      entityName: 'sprk_matter',
    });
  }, []);

  const handleViewAllProjects = React.useCallback(() => {
    navigateToEntity({
      action: 'openView',
      entityName: 'sprk_project',
    });
  }, []);

  const handleViewAllDocuments = React.useCallback(() => {
    navigateToEntity({
      action: 'openView',
      entityName: 'sprk_document',
    });
  }, []);

  /** Refresh the currently visible tab's data */
  const handleRefresh = React.useCallback(() => {
    switch (activeTab) {
      case 'matters':
        refetchMatters();
        break;
      case 'projects':
        refetchProjects();
        break;
      case 'documents':
        refetchDocuments();
        break;
    }
  }, [activeTab, refetchMatters, refetchProjects, refetchDocuments]);

  // ---------------------------------------------------------------------------
  // Footer visibility — show on all tabs; label and handler vary by tab
  // ---------------------------------------------------------------------------
  const footerConfig = React.useMemo(() => {
    switch (activeTab) {
      case 'matters':
        return { label: 'View All Matters', handler: handleViewAllMatters };
      case 'projects':
        return { label: 'View All Projects', handler: handleViewAllProjects };
      case 'documents':
        return { label: 'View All Documents', handler: handleViewAllDocuments };
    }
  }, [activeTab, handleViewAllMatters, handleViewAllProjects, handleViewAllDocuments]);

  // ---------------------------------------------------------------------------
  // Tab content rendering
  // ---------------------------------------------------------------------------

  const renderMattersContent = () => {
    if (mattersLoading) {
      return <LoadingSkeleton />;
    }

    if (mattersError) {
      return (
        <div className={styles.errorContainer}>
          <MessageBar intent="error">
            <MessageBarBody>
              {mattersError}
            </MessageBarBody>
          </MessageBar>
        </div>
      );
    }

    if (matters.length === 0) {
      return (
        <div className={styles.emptyState} role="status">
          <span className={styles.emptyIcon} aria-hidden="true">
            <BriefcaseRegular />
          </span>
          <Text size={200} className={styles.emptyText}>
            No active matters found.
          </Text>
        </div>
      );
    }

    return (
      <div
        className={styles.matterList}
        role="list"
        aria-label="My matters"
        aria-live="polite"
      >
        {matters.map((matter) => (
          <MatterItem key={matter.sprk_matterid} matter={matter} />
        ))}
      </div>
    );
  };

  const renderProjectsContent = () => {
    if (projectsLoading) {
      return <LoadingSkeleton />;
    }

    if (projectsError) {
      return (
        <div className={styles.errorContainer}>
          <MessageBar intent="error">
            <MessageBarBody>
              {projectsError}
            </MessageBarBody>
          </MessageBar>
        </div>
      );
    }

    if (projects.length === 0) {
      return (
        <div className={styles.emptyState} role="status">
          <span className={styles.emptyIcon} aria-hidden="true">
            <FolderRegular />
          </span>
          <Text size={200} className={styles.emptyText}>
            No projects found.
          </Text>
        </div>
      );
    }

    return (
      <div
        className={styles.projectList}
        role="list"
        aria-label="My projects"
        aria-live="polite"
      >
        {projects.map((project) => (
          <ProjectItem key={project.sprk_projectid} project={project} />
        ))}
      </div>
    );
  };

  const renderDocumentsContent = () => {
    if (documentsLoading) {
      return <LoadingSkeleton />;
    }

    if (documentsError) {
      return (
        <div className={styles.errorContainer}>
          <MessageBar intent="error">
            <MessageBarBody>
              {documentsError}
            </MessageBarBody>
          </MessageBar>
        </div>
      );
    }

    if (documents.length === 0) {
      return (
        <div className={styles.emptyState} role="status">
          <span className={styles.emptyIcon} aria-hidden="true">
            <DocumentRegular />
          </span>
          <Text size={200} className={styles.emptyText}>
            No documents found.
          </Text>
        </div>
      );
    }

    return (
      <div
        className={styles.documentList}
        role="list"
        aria-label="My documents"
        aria-live="polite"
      >
        {documents.map((doc) => (
          <DocumentItem key={doc.sprk_documentid} document={doc} />
        ))}
      </div>
    );
  };

  const renderContent = () => {
    switch (activeTab) {
      case 'matters':
        return renderMattersContent();
      case 'projects':
        return renderProjectsContent();
      case 'documents':
        return renderDocumentsContent();
    }
  };

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div
      className={styles.widget}
      aria-label="My Portfolio"
      role="region"
    >
      {/* Header */}
      <div className={styles.header}>
        <Text size={300} className={styles.title}>
          My Portfolio
        </Text>
        <div className={styles.headerActions}>
          <Button
            appearance="subtle"
            size="small"
            icon={<ArrowClockwiseRegular />}
            onClick={handleRefresh}
            aria-label="Refresh portfolio data"
            title="Refresh"
          />
        </div>
      </div>

      {/* Tab navigation */}
      <div className={styles.tabList}>
        <TabList
          selectedValue={activeTab}
          onTabSelect={handleTabSelect}
          size="small"
          aria-label="Portfolio sections"
        >
          <Tab
            value="matters"
            aria-label={`Matters${!mattersLoading && mattersTotalCount > 0 ? `, ${mattersTotalCount} items` : ''}`}
          >
            <TabLabel
              label="Matters"
              icon={<BriefcaseRegular />}
              count={mattersLoading ? undefined : mattersTotalCount}
              isLoading={mattersLoading}
            />
          </Tab>

          <Tab
            value="projects"
            aria-label={`Projects${!projectsLoading && projectsTotalCount > 0 ? `, ${projectsTotalCount} items` : ''}`}
          >
            <TabLabel
              label="Projects"
              icon={<FolderRegular />}
              count={projectsLoading ? undefined : projectsTotalCount}
              isLoading={projectsLoading}
            />
          </Tab>

          <Tab
            value="documents"
            aria-label={`Documents${!documentsLoading && documentsTotalCount > 0 ? `, ${documentsTotalCount} items` : ''}`}
          >
            <TabLabel
              label="Documents"
              icon={<DocumentRegular />}
              count={documentsLoading ? undefined : documentsTotalCount}
              isLoading={documentsLoading}
            />
          </Tab>
        </TabList>
      </div>

      {/* Tab content */}
      <div className={styles.tabContent}>
        {renderContent()}
      </div>

      {/* Footer — "View All" navigation for the active tab */}
      <div className={styles.footer}>
        <Button
          appearance="transparent"
          size="small"
          className={styles.footerButton}
          iconPosition="after"
          icon={<ArrowRightRegular />}
          onClick={footerConfig.handler}
          aria-label={`${footerConfig.label} in full list`}
        >
          {footerConfig.label}
        </Button>
      </div>
    </div>
  );
};
