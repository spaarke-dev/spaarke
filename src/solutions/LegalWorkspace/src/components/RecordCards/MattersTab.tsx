/**
 * MattersTab — embedded tab wrapper for the Matters record list.
 *
 * Uses useMattersList with contactId (broad filter) and renders RecordCardList
 * with RecordCard items. Follows the ActivityFeed embedded pattern with
 * onCountChange and onRefetchReady callbacks.
 */

import * as React from "react";
import { GavelRegular } from "@fluentui/react-icons";
import { DataverseService } from "../../services/DataverseService";
import { useMattersList } from "../../hooks/useMattersList";
import { RecordCard } from "./RecordCard";
import { RecordCardList } from "./RecordCardList";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IMattersTabProps {
  service: DataverseService;
  userId: string;
  contactId: string | null;
  onCountChange?: (count: number) => void;
  onRefetchReady?: (refetch: () => void) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const MattersTab: React.FC<IMattersTabProps> = ({
  service,
  userId,
  contactId,
  onCountChange,
  onRefetchReady,
}) => {
  const { matters, isLoading, error, totalCount, refetch } = useMattersList(
    service,
    userId,
    { top: 50, contactId }
  );

  // Report count to parent
  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  // Expose refetch to parent
  React.useEffect(() => {
    onRefetchReady?.(refetch);
  }, [refetch, onRefetchReady]);

  return (
    <RecordCardList
      totalCount={totalCount}
      isLoading={isLoading}
      error={error}
      ariaLabel="Matters list"
    >
      {matters.map((matter) => (
        <RecordCard
          key={matter.sprk_matterid}
          icon={GavelRegular}
          iconLabel="Matter"
          entityName="sprk_matter"
          entityId={matter.sprk_matterid}
          typeBadge={matter.matterTypeName || undefined}
          title={matter.sprk_mattername ?? ''}
          primaryFields={[
            matter.sprk_matternumber,
            matter.practiceAreaName,
          ].filter(Boolean) as string[]}
          statusBadge={matter.statuscodeName || undefined}
          description={matter.sprk_matterdescription}
        />
      ))}
    </RecordCardList>
  );
};
