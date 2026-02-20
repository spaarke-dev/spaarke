/**
 * useRelatedRecord - Generic hook for fetching/creating a single related record
 *
 * Used for Memo and ToDo sections — both follow the same pattern:
 * - Query for a related record by parent lookup
 * - If found, return for display/edit
 * - If not found, allow creation
 * - Save independently from the parent record
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { getXrm } from "../utils/xrmAccess";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseRelatedRecordOptions {
  /** Entity logical name (e.g., "sprk_memo", "sprk_eventtodo") */
  entityName: string;
  /** Lookup field name referencing the parent (e.g., "sprk_regardingevent") */
  parentLookupField: string;
  /** Parent record ID (Event GUID) */
  parentId: string | null;
  /** OData $select fields to retrieve */
  selectFields: string;
  /** OData $orderby (default: "createdon desc") */
  orderBy?: string;
}

export interface UseRelatedRecordResult {
  /** Whether the record is loading */
  isLoading: boolean;
  /** Error message if any */
  error: string | null;
  /** The related record data (null if none found) */
  record: Record<string, unknown> | null;
  /** The record ID (null if none found) */
  recordId: string | null;
  /** Create a new related record with initial data */
  createRecord: (data: Record<string, unknown>) => Promise<string | null>;
  /** Update the existing related record */
  updateRecord: (data: Record<string, unknown>) => Promise<boolean>;
  /** Refresh the record from server */
  refresh: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

export function useRelatedRecord(
  options: UseRelatedRecordOptions
): UseRelatedRecordResult {
  const { entityName, parentLookupField, parentId, selectFields, orderBy } = options;

  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [record, setRecord] = React.useState<Record<string, unknown> | null>(null);
  const [recordId, setRecordId] = React.useState<string | null>(null);
  const [refreshCounter, setRefreshCounter] = React.useState(0);

  // Fetch the related record
  React.useEffect(() => {
    if (!parentId) {
      setRecord(null);
      setRecordId(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;

    async function fetchRelated() {
      setIsLoading(true);
      setError(null);

      const xrm = getXrm();
      if (!xrm?.WebApi) {
        setError("Xrm.WebApi not available");
        setIsLoading(false);
        return;
      }

      try {
        const normalizedParentId = parentId!.replace(/[{}]/g, "").toLowerCase();
        const filter = `_${parentLookupField}_value eq ${normalizedParentId}`;
        const order = orderBy ?? "createdon desc";
        const query = `?$select=${selectFields}&$filter=${filter}&$orderby=${order}&$top=1`;

        const result = await xrm.WebApi.retrieveMultipleRecords(entityName, query);

        if (cancelled) return;

        if (result.entities.length > 0) {
          const entity = result.entities[0];
          const idField = `${entityName}id`;
          const id = (entity[idField] as string) ?? null;
          setRecord(entity);
          setRecordId(id);
        } else {
          setRecord(null);
          setRecordId(null);
        }
      } catch (err) {
        if (cancelled) return;
        const msg = err instanceof Error ? err.message : String(err);
        console.error(`[useRelatedRecord] Failed to fetch ${entityName}:`, msg);
        setError(msg);
        setRecord(null);
        setRecordId(null);
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    fetchRelated();

    return () => {
      cancelled = true;
    };
  }, [entityName, parentLookupField, parentId, selectFields, orderBy, refreshCounter]);

  // Create a new related record
  const createRelatedRecord = React.useCallback(
    async (data: Record<string, unknown>): Promise<string | null> => {
      if (!parentId) return null;

      const xrm = getXrm();
      if (!xrm?.WebApi) {
        setError("Xrm.WebApi not available");
        return null;
      }

      try {
        const normalizedParentId = parentId.replace(/[{}]/g, "").toLowerCase();

        // Set the parent lookup using @odata.bind
        const createData = {
          ...data,
          [`${parentLookupField}@odata.bind`]: `/sprk_events(${normalizedParentId})`,
        };

        const result = await xrm.WebApi.createRecord(entityName, createData);
        const newId = (result as Record<string, unknown>).id as string;

        if (newId) {
          // Refresh to get the full record
          setRefreshCounter((c) => c + 1);
          return newId.replace(/[{}]/g, "").toLowerCase();
        }
        return null;
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error(`[useRelatedRecord] Failed to create ${entityName}:`, msg);
        setError(msg);
        return null;
      }
    },
    [entityName, parentLookupField, parentId]
  );

  // Update the existing related record
  const updateRelatedRecord = React.useCallback(
    async (data: Record<string, unknown>): Promise<boolean> => {
      if (!recordId) return false;

      const xrm = getXrm();
      if (!xrm?.WebApi) {
        setError("Xrm.WebApi not available");
        return false;
      }

      try {
        await xrm.WebApi.updateRecord(entityName, recordId, data);

        // Update local state optimistically
        setRecord((prev) => (prev ? { ...prev, ...data } : prev));
        return true;
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error(`[useRelatedRecord] Failed to update ${entityName}:`, msg);
        setError(msg);
        return false;
      }
    },
    [entityName, recordId]
  );

  const refresh = React.useCallback(() => {
    setRefreshCounter((c) => c + 1);
  }, []);

  return {
    isLoading,
    error,
    record,
    recordId,
    createRecord: createRelatedRecord,
    updateRecord: updateRelatedRecord,
    refresh,
  };
}
