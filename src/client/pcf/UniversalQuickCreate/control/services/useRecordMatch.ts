/**
 * Record Match Hook (useRecordMatch)
 *
 * Manages record matching API calls to find and associate Dataverse records
 * with documents based on extracted entities.
 *
 * @version 1.0.0
 */

import { useState, useCallback } from 'react';
import { ExtractedEntities } from './useAiSummary';

/**
 * Record type options for filtering
 */
export type RecordTypeFilter = 'all' | 'sprk_matter' | 'sprk_project' | 'sprk_invoice';

/**
 * Record type display info
 */
export const RECORD_TYPE_OPTIONS: { value: RecordTypeFilter; label: string }[] = [
    { value: 'all', label: 'All Records' },
    { value: 'sprk_matter', label: 'Matters' },
    { value: 'sprk_project', label: 'Projects' },
    { value: 'sprk_invoice', label: 'Invoices' }
];

/**
 * A single record match suggestion from the API
 */
export interface RecordMatchSuggestion {
    /** The Dataverse record ID (GUID) */
    recordId: string;
    /** The record type (e.g., "sprk_matter", "sprk_project", "sprk_invoice") */
    recordType: string;
    /** The display name of the record */
    recordName: string;
    /** Confidence score (0.0 to 1.0) */
    confidenceScore: number;
    /** Human-readable reasons explaining why this record matched */
    matchReasons: string[];
    /** The Dataverse lookup field name to populate when associating this record */
    lookupFieldName: string;
}

/**
 * Response from match-records API
 */
export interface RecordMatchResponse {
    suggestions: RecordMatchSuggestion[];
    totalMatches: number;
}

/**
 * Response from associate-record API
 */
export interface AssociateRecordResponse {
    success: boolean;
    message: string;
}

/**
 * Hook options
 */
export interface UseRecordMatchOptions {
    /** Base URL for API endpoints */
    apiBaseUrl: string;
    /** Function to get authorization token */
    getToken?: () => Promise<string>;
    /** Maximum results to fetch (default: 5) */
    maxResults?: number;
}

/**
 * Hook return type
 */
export interface UseRecordMatchResult {
    /** Current suggestions */
    suggestions: RecordMatchSuggestion[];
    /** Whether matching is in progress */
    isMatching: boolean;
    /** Whether association is in progress */
    isAssociating: boolean;
    /** Error message if any */
    error: string | null;
    /** Association success message */
    successMessage: string | null;
    /** Find matching records for extracted entities */
    findMatches: (entities: ExtractedEntities, recordTypeFilter: RecordTypeFilter) => Promise<void>;
    /** Associate a document with a record */
    associateRecord: (documentId: string, suggestion: RecordMatchSuggestion) => Promise<boolean>;
    /** Clear current state */
    clear: () => void;
}

/**
 * useRecordMatch Hook
 *
 * Provides record matching and association functionality for documents.
 */
export const useRecordMatch = (options: UseRecordMatchOptions): UseRecordMatchResult => {
    const { apiBaseUrl, getToken, maxResults = 5 } = options;

    const [suggestions, setSuggestions] = useState<RecordMatchSuggestion[]>([]);
    const [isMatching, setIsMatching] = useState(false);
    const [isAssociating, setIsAssociating] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);

    /**
     * Find matching records based on extracted entities
     */
    const findMatches = useCallback(async (
        entities: ExtractedEntities,
        recordTypeFilter: RecordTypeFilter
    ) => {
        setIsMatching(true);
        setError(null);
        setSuggestions([]);
        setSuccessMessage(null);

        try {
            let token: string | undefined;
            if (getToken) {
                token = await getToken();
            }

            const response = await fetch(`${apiBaseUrl}/ai/document-intelligence/match-records`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify({
                    entities: {
                        organizations: entities.organizations || [],
                        people: entities.people || [],
                        references: entities.references || [],
                        keywords: [] // Could add keywords from document if available
                    },
                    recordTypeFilter,
                    maxResults
                })
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText || `HTTP ${response.status}`);
            }

            const result: RecordMatchResponse = await response.json();
            setSuggestions(result.suggestions);

        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to find matching records');
            setSuggestions([]);
        } finally {
            setIsMatching(false);
        }
    }, [apiBaseUrl, getToken, maxResults]);

    /**
     * Associate a document with a record
     */
    const associateRecord = useCallback(async (
        documentId: string,
        suggestion: RecordMatchSuggestion
    ): Promise<boolean> => {
        setIsAssociating(true);
        setError(null);
        setSuccessMessage(null);

        try {
            let token: string | undefined;
            if (getToken) {
                token = await getToken();
            }

            const response = await fetch(`${apiBaseUrl}/ai/document-intelligence/associate-record`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify({
                    documentId,
                    recordId: suggestion.recordId,
                    recordType: suggestion.recordType,
                    lookupFieldName: suggestion.lookupFieldName
                })
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText || `HTTP ${response.status}`);
            }

            const result: AssociateRecordResponse = await response.json();

            if (result.success) {
                setSuccessMessage(result.message || `Associated with ${suggestion.recordName}`);
                return true;
            } else {
                throw new Error(result.message || 'Association failed');
            }

        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to associate record');
            return false;
        } finally {
            setIsAssociating(false);
        }
    }, [apiBaseUrl, getToken]);

    /**
     * Clear current state
     */
    const clear = useCallback(() => {
        setSuggestions([]);
        setError(null);
        setSuccessMessage(null);
        setIsMatching(false);
        setIsAssociating(false);
    }, []);

    return {
        suggestions,
        isMatching,
        isAssociating,
        error,
        successMessage,
        findMatches,
        associateRecord,
        clear
    };
};

export default useRecordMatch;
