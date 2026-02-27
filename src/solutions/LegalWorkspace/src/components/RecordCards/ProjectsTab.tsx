/**
 * ProjectsTab — embedded tab wrapper for the Projects record list.
 *
 * Uses useProjectsList with contactId (broad filter) and renders RecordCardList
 * with RecordCard items.
 */

import * as React from "react";
import { TaskListSquareLtrRegular } from "@fluentui/react-icons";
import { DataverseService } from "../../services/DataverseService";
import { useProjectsList } from "../../hooks/useProjectsList";
import { RecordCard } from "./RecordCard";
import { RecordCardList } from "./RecordCardList";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IProjectsTabProps {
  service: DataverseService;
  userId: string;
  contactId: string | null;
  onCountChange?: (count: number) => void;
  onRefetchReady?: (refetch: () => void) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ProjectsTab: React.FC<IProjectsTabProps> = ({
  service,
  userId,
  contactId,
  onCountChange,
  onRefetchReady,
}) => {
  const { projects, isLoading, error, totalCount, refetch } = useProjectsList(
    service,
    userId,
    { top: 50, contactId }
  );

  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  React.useEffect(() => {
    onRefetchReady?.(refetch);
  }, [refetch, onRefetchReady]);

  return (
    <RecordCardList
      totalCount={totalCount}
      isLoading={isLoading}
      error={error}
      ariaLabel="Projects list"
    >
      {projects.map((project) => (
        <RecordCard
          key={project.sprk_projectid}
          icon={TaskListSquareLtrRegular}
          iconLabel="Project"
          entityName="sprk_project"
          entityId={project.sprk_projectid}
          typeBadge={project.projectTypeName || undefined}
          title={project.sprk_projectname ?? project.sprk_name}
          primaryFields={[
            project.sprk_projectnumber,
          ].filter(Boolean) as string[]}
          statusBadge={project.statuscodeName || undefined}
          description={project.sprk_projectdescription}
        />
      ))}
    </RecordCardList>
  );
};
