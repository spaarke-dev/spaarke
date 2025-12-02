/**
 * useDatasetMode - Extract data from PCF dataset binding
 * Used in model-driven apps where Power Platform provides the dataset
 */

import { useState, useEffect, useMemo } from "react";
import { IDatasetRecord, IDatasetColumn } from "../types";
import { IDatasetResult } from "./types";

export interface IUseDatasetModeProps {
  dataset: ComponentFramework.PropertyTypes.DataSet;
}

export function useDatasetMode(props: IUseDatasetModeProps): IDatasetResult {
  const { dataset } = props;
  const [error, setError] = useState<string | null>(null);

  // Extract columns from dataset with field security
  const columns = useMemo((): IDatasetColumn[] => {
    if (!dataset.columns || dataset.columns.length === 0) {
      return [];
    }

    return dataset.columns.map((col) => {
      // Extract field security from column metadata
      const colSecurity = (col as any).security;

      return {
        name: col.name,
        displayName: col.displayName,
        dataType: col.dataType,
        isKey: false,
        isPrimary: col.name === dataset.columns[0]?.name,
        visualSizeFactor: col.visualSizeFactor,
        // Field-level security
        isSecured: colSecurity?.secured === true,
        canRead: colSecurity?.readable !== false,
        canUpdate: colSecurity?.editable !== false,
        canCreate: colSecurity?.editable !== false
      };
    });
  }, [dataset.columns]);

  // Extract records from dataset
  const records = useMemo((): IDatasetRecord[] => {
    if (!dataset.sortedRecordIds || dataset.sortedRecordIds.length === 0) {
      return [];
    }

    const extractedRecords: IDatasetRecord[] = [];

    dataset.sortedRecordIds.forEach((recordId) => {
      const record = dataset.records[recordId];
      if (!record) return;

      const dataRecord: IDatasetRecord = {
        id: recordId,
        entityName: dataset.getTargetEntityType()
      };

      // Extract all column values
      columns.forEach((col) => {
        const formattedValue = record.getFormattedValue(col.name);
        const rawValue = record.getValue(col.name);

        dataRecord[col.name] = formattedValue || rawValue;
        dataRecord[`${col.name}_raw`] = rawValue;
      });

      extractedRecords.push(dataRecord);
    });

    return extractedRecords;
  }, [dataset.sortedRecordIds, dataset.records, columns, dataset]);

  // Pagination state
  const paging = dataset.paging;
  const hasNextPage = paging?.hasNextPage ?? false;
  const hasPreviousPage = paging?.hasPreviousPage ?? false;

  // Pagination functions
  const loadNextPage = () => {
    if (hasNextPage && paging) {
      paging.loadNextPage();
    }
  };

  const loadPreviousPage = () => {
    if (hasPreviousPage && paging) {
      paging.loadPreviousPage();
    }
  };

  const refresh = () => {
    try {
      dataset.refresh();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to refresh dataset");
    }
  };

  // Monitor for errors
  useEffect(() => {
    if (dataset.error) {
      setError("Dataset error occurred");
    } else {
      setError(null);
    }
  }, [dataset.error]);

  return {
    records,
    columns,
    loading: dataset.loading,
    error,
    totalRecordCount: paging?.totalResultCount ?? records.length,
    hasNextPage,
    hasPreviousPage,
    loadNextPage,
    loadPreviousPage,
    refresh
  };
}
