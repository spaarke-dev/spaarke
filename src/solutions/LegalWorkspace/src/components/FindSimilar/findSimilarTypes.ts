/**
 * findSimilarTypes.ts
 * Type definitions for the Find Similar wizard.
 * Mirrors the Semantic Search code page types for API compatibility.
 */

// ---------------------------------------------------------------------------
// Search result types (subset of Semantic Search types)
// ---------------------------------------------------------------------------

/** Result category tab in the results grid. */
export type FindSimilarDomain = 'documents' | 'matters' | 'projects';

/** A single document search result from POST /api/ai/search. */
export interface IDocumentResult {
  documentId: string;
  name: string;
  documentType?: string;
  fileType?: string;
  combinedScore: number;
  highlights?: string[];
  parentEntityType?: string;
  parentEntityName?: string;
  parentEntityId?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** A single record search result from POST /api/ai/search/records. */
export interface IRecordResult {
  recordId: string;
  recordType: string;
  recordName: string;
  recordDescription?: string;
  confidenceScore: number;
  matchReasons?: string[];
  organizations?: string[];
  people?: string[];
  createdAt?: string;
  modifiedAt?: string;
}

/** Combined response from both search endpoints. */
export interface IFindSimilarResults {
  documents: IDocumentResult[];
  documentsTotalCount: number;
  matters: IRecordResult[];
  mattersTotalCount: number;
  projects: IRecordResult[];
  projectsTotalCount: number;
}

/** Lifecycle status of the find-similar search. */
export type FindSimilarStatus = 'idle' | 'loading' | 'success' | 'error';

// ---------------------------------------------------------------------------
// Grid adapter types (matches SearchResultsGrid pattern)
// ---------------------------------------------------------------------------

/** Minimal dataset record for grid rendering. */
export interface IGridRecord {
  id: string;
  entityName: string;
  [key: string]: unknown;
}

/** Column definition for the results grid. */
export interface IGridColumn {
  name: string;
  displayName: string;
  dataType: string;
  visualSizeFactor?: number;
}
