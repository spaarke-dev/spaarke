/**
 * Hook return types for dataset and headless modes
 */

import { IDatasetRecord, IDatasetColumn } from "../types";

export interface IDatasetResult {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  loading: boolean;
  error: string | null;
  totalRecordCount: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  loadNextPage: () => void;
  loadPreviousPage: () => void;
  refresh: () => void;
}
