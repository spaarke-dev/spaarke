/**
 * useHeadlessMode - Fetch data via Web API using FetchXML
 * Used in custom pages where no dataset binding exists
 */

import { useState, useEffect, useCallback } from "react";
import { IDatasetRecord, IDatasetColumn } from "../types";
import { IDatasetResult } from "./types";

export interface IUseHeadlessModeProps {
  webAPI: ComponentFramework.WebApi;
  entityName: string;
  fetchXml?: string;
  pageSize: number;
  autoLoad?: boolean;
}

interface IPagingInfo {
  pageNumber: number;
  pagingCookie?: string;
}

export function useHeadlessMode(props: IUseHeadlessModeProps): IDatasetResult {
  const { webAPI, entityName, fetchXml, pageSize, autoLoad = true } = props;

  const [records, setRecords] = useState<IDatasetRecord[]>([]);
  const [columns, setColumns] = useState<IDatasetColumn[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [pagingInfo, setPagingInfo] = useState<IPagingInfo>({ pageNumber: 1 });
  const [totalRecordCount, setTotalRecordCount] = useState<number>(0);
  const [hasMore, setHasMore] = useState<boolean>(false);

  // Build FetchXML query with paging
  const buildFetchXml = useCallback((page: number, cookie?: string): string => {
    if (fetchXml) {
      // User-provided FetchXML - inject paging attributes
      const fetchDoc = new DOMParser().parseFromString(fetchXml, "text/xml");
      const fetchNode = fetchDoc.querySelector("fetch");

      if (fetchNode) {
        fetchNode.setAttribute("page", page.toString());
        fetchNode.setAttribute("count", pageSize.toString());
        if (cookie) {
          fetchNode.setAttribute("paging-cookie", cookie);
        }
      }

      return new XMLSerializer().serializeToString(fetchDoc);
    } else {
      // Default FetchXML - retrieve all columns
      return `
        <fetch page="${page}" count="${pageSize}" ${cookie ? `paging-cookie="${cookie}"` : ""}>
          <entity name="${entityName}">
            <all-attributes />
          </entity>
        </fetch>
      `.trim();
    }
  }, [fetchXml, entityName, pageSize]);

  // Fetch data from Web API
  const fetchData = useCallback(async (page: number, cookie?: string) => {
    setLoading(true);
    setError(null);

    try {
      const query = buildFetchXml(page, cookie);

      // Execute FetchXML query
      const response = await webAPI.retrieveMultipleRecords(
        entityName,
        `?fetchXml=${encodeURIComponent(query)}`
      );

      // Extract records
      const fetchedRecords: IDatasetRecord[] = response.entities.map((entity: any) => {
        const record: IDatasetRecord = {
          id: entity[`${entityName}id`] || entity.id,
          entityName
        };

        // Copy all entity attributes
        Object.keys(entity).forEach((key) => {
          if (key !== `${entityName}id` && !key.startsWith("@")) {
            record[key] = entity[key];
          }
        });

        return record;
      });

      setRecords(fetchedRecords);

      // Extract columns from first record (if not already set)
      if (columns.length === 0 && fetchedRecords.length > 0) {
        const firstRecord = fetchedRecords[0];
        const extractedColumns: IDatasetColumn[] = Object.keys(firstRecord)
          .filter((key) => key !== "id" && key !== "entityName")
          .map((key) => ({
            name: key,
            displayName: key.replace(/_/g, " ").replace(/\b\w/g, (l) => l.toUpperCase()),
            dataType: typeof firstRecord[key] === "number" ? "number" : "string",
            isKey: key === `${entityName}id`,
            isPrimary: false
          }));

        setColumns(extractedColumns);
      }

      // Pagination info
      setHasMore(response.entities.length === pageSize);
      setTotalRecordCount(response.entities.length); // Note: FetchXML doesn't return total count

      // Extract paging cookie from response
      const nextCookie = (response as any)["@Microsoft.Dynamics.CRM.fetchxmlpagingcookie"];
      setPagingInfo({ pageNumber: page, pagingCookie: nextCookie });

      setLoading(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch data");
      setRecords([]);
      setLoading(false);
    }
  }, [buildFetchXml, entityName, webAPI, columns.length]);

  // Load next page
  const loadNextPage = useCallback(() => {
    if (hasMore && !loading) {
      fetchData(pagingInfo.pageNumber + 1, pagingInfo.pagingCookie);
    }
  }, [hasMore, loading, pagingInfo, fetchData]);

  // Load previous page
  const loadPreviousPage = useCallback(() => {
    if (pagingInfo.pageNumber > 1 && !loading) {
      fetchData(pagingInfo.pageNumber - 1);
    }
  }, [pagingInfo.pageNumber, loading, fetchData]);

  // Refresh data
  const refresh = useCallback(() => {
    fetchData(1); // Reset to page 1
  }, [fetchData]);

  // Auto-load on mount
  useEffect(() => {
    if (autoLoad) {
      fetchData(1);
    }
  }, [autoLoad, fetchData]);

  return {
    records,
    columns,
    loading,
    error,
    totalRecordCount,
    hasNextPage: hasMore,
    hasPreviousPage: pagingInfo.pageNumber > 1,
    loadNextPage,
    loadPreviousPage,
    refresh
  };
}
