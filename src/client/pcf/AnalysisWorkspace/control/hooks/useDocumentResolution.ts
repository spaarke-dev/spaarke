/**
 * useDocumentResolution Hook
 *
 * Manages document ID resolution state for the AnalysisWorkspace control.
 * Resolves the actual document ID, container ID, file ID, and document name
 * from the Analysis record's linked Document record in Dataverse.
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { logInfo, logError } from '../utils/logger';

/**
 * Options for the useDocumentResolution hook
 */
export interface UseDocumentResolutionOptions {
  /** Initial document ID from PCF props (may be empty — resolved from Analysis record) */
  documentId: string;

  /** Initial container ID from PCF props */
  containerId: string;

  /** Initial file ID from PCF props */
  fileId: string;

  /** Dataverse WebAPI reference for querying document records */
  webApi: ComponentFramework.WebApi;
}

/**
 * Result returned by the useDocumentResolution hook
 */
export interface UseDocumentResolutionResult {
  /** Resolved document ID (from Analysis record's document lookup) */
  documentId: string;

  /** Resolved container/drive ID (from Document record's sprk_graphdriveid) */
  containerId: string;

  /** Resolved file/item ID (from Document record's sprk_graphitemid) */
  fileId: string;

  /** Resolved document name (from Document record's sprk_documentname) */
  documentName: string;

  /** Playbook ID associated with the analysis (from Analysis record) */
  playbookId: string | null;

  /**
   * Resolve document details from a Dataverse document ID.
   * Queries the sprk_document entity for SPE integration fields.
   */
  resolveFromDocumentId: (docId: string) => Promise<void>;

  /** Set the playbook ID (from analysis record's _sprk_playbook_value) */
  setPlaybookId: (id: string | null) => void;

  /** Set the document ID directly (when resolved from analysis record) */
  setDocumentId: (id: string) => void;
}

/**
 * useDocumentResolution Hook
 *
 * Encapsulates document ID resolution state and the Dataverse query
 * to resolve container/file IDs from a document record.
 *
 * @example
 * ```tsx
 * const {
 *   documentId, containerId, fileId, documentName, playbookId,
 *   resolveFromDocumentId, setPlaybookId, setDocumentId,
 * } = useDocumentResolution({
 *   documentId: props.documentId,
 *   containerId: props.containerId,
 *   fileId: props.fileId,
 *   webApi: props.webApi,
 * });
 *
 * // In loadAnalysis, after fetching analysis record:
 * if (result._sprk_documentid_value) {
 *   setDocumentId(result._sprk_documentid_value);
 *   await resolveFromDocumentId(result._sprk_documentid_value);
 * }
 * ```
 */
export const useDocumentResolution = (
  options: UseDocumentResolutionOptions
): UseDocumentResolutionResult => {
  const { documentId: initialDocumentId, containerId: initialContainerId, fileId: initialFileId, webApi } = options;

  // Resolved document fields from expanded relationship
  // Note: documentId prop may be empty — we resolve the actual document ID from the Analysis record
  const [resolvedDocumentId, setResolvedDocumentId] = React.useState(initialDocumentId);
  const [resolvedContainerId, setResolvedContainerId] = React.useState(initialContainerId);
  const [resolvedFileId, setResolvedFileId] = React.useState(initialFileId);
  const [resolvedDocumentName, setResolvedDocumentName] = React.useState('');

  // Playbook info (loaded from analysis record)
  const [playbookId, setPlaybookId] = React.useState<string | null>(null);

  /**
   * Resolve document details from a Dataverse document ID.
   * Queries sprk_document for SPE integration fields (drive ID, item ID, name).
   */
  const resolveFromDocumentId = React.useCallback(
    async (docId: string): Promise<void> => {
      try {
        // Query document for SPE integration fields
        // Document entity field names: sprk_graphdriveid, sprk_graphitemid, sprk_documentname
        const docResult = await webApi.retrieveRecord(
          'sprk_document',
          docId,
          '?$select=sprk_documentname,sprk_graphdriveid,sprk_graphitemid'
        );
        if (docResult.sprk_graphdriveid) {
          setResolvedContainerId(docResult.sprk_graphdriveid);
        }
        if (docResult.sprk_graphitemid) {
          setResolvedFileId(docResult.sprk_graphitemid);
        }
        if (docResult.sprk_documentname) {
          setResolvedDocumentName(docResult.sprk_documentname);
        }
        logInfo(
          'useDocumentResolution',
          `Document fields resolved: docId=${docId}, container=${docResult.sprk_graphdriveid}, file=${docResult.sprk_graphitemid}`
        );
      } catch (docErr) {
        logError('useDocumentResolution', 'Failed to load document details', docErr);
      }
    },
    [webApi]
  );

  return {
    documentId: resolvedDocumentId,
    containerId: resolvedContainerId,
    fileId: resolvedFileId,
    documentName: resolvedDocumentName,
    playbookId,
    resolveFromDocumentId,
    setPlaybookId,
    setDocumentId: setResolvedDocumentId,
  };
};

export default useDocumentResolution;
