/**
 * useQuickSummaryCounts — React hook that fires parallel webApi count
 * queries on mount and exposes the results as a Record<cardId, count>.
 *
 * Also fetches badge counts (New / Overdue) for each card that has a
 * badgeFilter configured.
 *
 * Usage:
 *   const { counts, badgeCounts, isLoading, refetch } = useQuickSummaryCounts(webApi, userId);
 *   const matterCount = counts["my-matters"]; // number | undefined
 *   const newMatters = badgeCounts["my-matters"]; // number | undefined
 */

import * as React from "react";
import type { IWebApi } from "../types/xrm";
import { QUICK_SUMMARY_CARDS } from "../components/QuickSummary/quickSummaryConfig";
import type { IFilterContext } from "../components/QuickSummary/quickSummaryConfig";

export interface IQuickSummaryCountsResult {
  /** Map of card id to count. undefined means the individual query failed. */
  counts: Record<string, number | undefined>;
  /** Map of card id to badge count (New or Overdue). undefined if not applicable or failed. */
  badgeCounts: Record<string, number | undefined>;
  /** True while any query is still in flight. */
  isLoading: boolean;
  /** Non-null if the entire batch failed. */
  error: string | null;
  /** Re-run all count queries. */
  refetch: () => void;
}

export function useQuickSummaryCounts(
  webApi: IWebApi,
  userId: string,
  scope?: "my" | "all",
  businessUnitId?: string,
): IQuickSummaryCountsResult {
  const [counts, setCounts] = React.useState<Record<string, number | undefined>>({});
  const [badgeCounts, setBadgeCounts] = React.useState<Record<string, number | undefined>>({});
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  const fetchCounts = React.useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const filterCtx: IFilterContext = { userId, scope, businessUnitId };

      // Build all queries: main counts + badge counts
      const mainQueries = QUICK_SUMMARY_CARDS.map(async (card) => {
        try {
          const query = `?$select=${card.primaryKey}&$filter=${card.countFilter(filterCtx)}&$top=500`;
          const result = await webApi.retrieveMultipleRecords(card.entityName, query, 500);
          return { id: card.id, count: result.entities.length };
        } catch {
          return { id: card.id, count: undefined };
        }
      });

      const badgeQueries = QUICK_SUMMARY_CARDS.map(async (card) => {
        if (!card.badgeFilter) return { id: card.id, count: undefined };
        try {
          const filter = card.badgeFilter(filterCtx);
          const query = `?$select=${card.primaryKey}&$filter=${filter}&$top=100`;
          const result = await webApi.retrieveMultipleRecords(card.entityName, query, 100);
          return { id: card.id, count: result.entities.length };
        } catch {
          return { id: card.id, count: undefined };
        }
      });

      const [mainResults, badgeResults] = await Promise.all([
        Promise.all(mainQueries),
        Promise.all(badgeQueries),
      ]);

      const newCounts: Record<string, number | undefined> = {};
      for (const r of mainResults) newCounts[r.id] = r.count;
      setCounts(newCounts);

      const newBadgeCounts: Record<string, number | undefined> = {};
      for (const r of badgeResults) newBadgeCounts[r.id] = r.count;
      setBadgeCounts(newBadgeCounts);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load counts");
    } finally {
      setIsLoading(false);
    }
  }, [webApi, userId, scope, businessUnitId]);

  React.useEffect(() => {
    fetchCounts();
  }, [fetchCounts]);

  return { counts, badgeCounts, isLoading, error, refetch: fetchCounts };
}
