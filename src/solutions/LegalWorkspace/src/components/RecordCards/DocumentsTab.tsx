/**
 * DocumentsTab — embedded tab wrapper for the Documents record list.
 *
 * Uses useDocumentsTabList (broad filter) and renders RecordCardList
 * with DocumentCard items. Follows the MattersTab embedded pattern with
 * onCountChange and onRefetchReady callbacks.
 */

import * as React from "react";
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
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DocumentsTab: React.FC<IDocumentsTabProps> = ({
  service,
  userId,
  onCountChange,
  onRefetchReady,
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

  return (
    <RecordCardList
      totalCount={totalCount}
      isLoading={isLoading}
      error={error}
      ariaLabel="Documents list"
    >
      {documents.map((doc) => (
        <DocumentCard key={doc.sprk_documentid} document={doc} />
      ))}
    </RecordCardList>
  );
};
