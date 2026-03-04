/**
 * DocumentsTab — embedded tab wrapper for the Documents record list.
 *
 * Uses useDocumentsTabList (broad filter) and renders RecordCardList
 * with DocumentCard items. Follows the MattersTab embedded pattern with
 * onCountChange and onRefetchReady callbacks.
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import { DataverseService } from "../../services/DataverseService";
import { useDocumentsTabList } from "../../hooks/useDocumentsTabList";
import { DocumentCard } from "./DocumentCard";
import { RecordCardList } from "./RecordCardList";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentsTabProps {
  service: DataverseService;
  userId: string;
  onCountChange?: (count: number) => void;
  onRefetchReady?: (refetch: () => void) => void;
  /** Maximum rows to display. */
  maxVisible?: number;
  /** Called when "Show more" is clicked. */
  onShowMore?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DocumentsTab: React.FC<IDocumentsTabProps> = ({
  service,
  userId,
  onCountChange,
  onRefetchReady,
  maxVisible,
  onShowMore,
}) => {
  const { documents, isLoading, error, totalCount, refetch } =
    useDocumentsTabList(service, userId, { top: 50 });

  // Report count to parent
  React.useEffect(() => {
    onCountChange?.(totalCount);
  }, [totalCount, onCountChange]);

  // Expose refetch to parent
  React.useEffect(() => {
    onRefetchReady?.(refetch);
  }, [refetch, onRefetchReady]);

  const visibleDocs = maxVisible ? documents.slice(0, maxVisible) : documents;

  return (
    <>
      <RecordCardList
        totalCount={totalCount}
        isLoading={isLoading}
        error={error}
        ariaLabel="Documents list"
      >
        {visibleDocs.map((doc) => (
          <DocumentCard key={doc.sprk_documentid} document={doc} />
        ))}
      </RecordCardList>
      {onShowMore && documents.length > (maxVisible ?? Infinity) && (
        <div style={{ display: "flex", justifyContent: "center", padding: "8px" }}>
          <Button appearance="subtle" size="small" onClick={onShowMore}>
            Show more ({documents.length - (maxVisible ?? 0)} more)
          </Button>
        </div>
      )}
    </>
  );
};
