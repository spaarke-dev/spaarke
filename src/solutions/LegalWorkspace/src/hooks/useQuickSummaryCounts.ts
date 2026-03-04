/**
 * useQuickSummaryCounts — React hook that fires 4 parallel webApi count
 * queries on mount and exposes the results as a Record<cardId, count>.
 *
 * Each card query selects only the primary key with a $top=500 cap so that
 * retrieveMultipleRecords returns a lightweight entity array whose length
 * is the count.
 *
 * Usage:
 *   const { counts, isLoading, error, refetch } = useQuickSummaryCounts(webApi, userId);
 *   const matterCount = counts["my-matters"]; // number | undefined
 */

import * as React from "react";
import type { IWebApi } from "../types/xrm";
import { QUICK_SUMMARY_CARDS } from "../components/QuickSummary/quickSummaryConfig";

export interface IQuickSummaryCountsResult {
  /** Map of card id to count. undefined means the individual query failed. */
  counts: Record<string, number | undefined>;
  /** True while any query is still in flight. */
  isLoading: boolean;
  /** Non-null if the entire batch failed. */
  error: string | null;
  /** Re-run all count queries. */
  refetch: () => void;
}

export function useQuickSummaryCounts(
  webApi: IWebApi,
  userId: string
): IQuickSummaryCountsResult {
  const [counts, setCounts] = React.useState<Record<string, number | undefined>>({});
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  const fetchCounts = React.useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const results = await Promise.all(
        QUICK_SUMMARY_CARDS.map(async (card) => {
          try {
            const query = `?$select=${card.primaryKey}&$filter=${card.countFilter(userId)}&$top=500`;
            const result = await webApi.retrieveMultipleRecords(card.entityName, query, 500);
            return { id: card.id, count: result.entities.length };
          } catch {
            return { id: card.id, count: undefined };
          }
        })
      );
      const newCounts: Record<string, number | undefined> = {};
      for (const r of results) {
        newCounts[r.id] = r.count;
      }
      setCounts(newCounts);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load counts");
    } finally {
      setIsLoading(false);
    }
  }, [webApi, userId]);

  React.useEffect(() => {
    fetchCounts();
  }, [fetchCounts]);

  return { counts, isLoading, error, refetch: fetchCounts };
}
