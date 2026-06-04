/**
 * useSidePaneFilter — React hook bridging a side pane's filter messages to
 * the DataGrid's `hostFilters` prop.
 *
 * Subscribes to the {@link SidePaneFilterChannel} for a given `paneId`, runs
 * the provided translator on each message, and returns the resulting
 * `HostFilterCondition[]` ready to pass straight into `<DataGrid hostFilters />`.
 *
 * Stable across translator-prop identity changes (translator captured via ref)
 * so callers can use inline arrow functions without thrashing the subscription.
 *
 * **Usage**:
 * ```tsx
 * const hostFilters = useSidePaneFilter('date-filter', (payload: DatePanePayload) => {
 *   if (!payload?.from || !payload?.to) return [];
 *   return [{
 *     attribute: payload.dateField ?? 'sprk_duedate',
 *     operator: 'between',
 *     value: [payload.from, payload.to],
 *   }];
 * });
 *
 * return <DataGrid configId="…" hostFilters={hostFilters} />;
 * ```
 *
 * @see docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §5
 */

import * as React from 'react';
import { subscribeSidePaneFilter } from './SidePaneFilterChannel';
import type { HostFilterCondition } from '../fetchXmlOverlay';

/**
 * Hook signature for the translator parameter — converts a pane's payload
 * into framework `HostFilterCondition[]`.
 */
export type SidePaneFilterTranslator<TPayload> = (
  payload: TPayload
) => ReadonlyArray<HostFilterCondition>;

/**
 * Subscribe to a side pane's filter messages and translate them into
 * `HostFilterCondition[]` for the DataGrid.
 *
 * @param paneId Unique pane identifier (must match what the side pane sends via `sendSidePaneFilter`).
 * @param translator Function converting payload → conditions. Captured by ref so identity changes don't tear down the subscription.
 * @returns The latest translated condition array. Empty array on mount; updates on each message.
 */
export function useSidePaneFilter<TPayload = unknown>(
  paneId: string,
  translator: SidePaneFilterTranslator<TPayload>
): ReadonlyArray<HostFilterCondition> {
  const [conditions, setConditions] = React.useState<ReadonlyArray<HostFilterCondition>>([]);

  // Capture translator in a ref so callers can pass inline functions without
  // re-subscribing on every render.
  const translatorRef = React.useRef(translator);
  React.useEffect(() => {
    translatorRef.current = translator;
  }, [translator]);

  React.useEffect(() => {
    const unsubscribe = subscribeSidePaneFilter<TPayload>(paneId, payload => {
      try {
        const next = translatorRef.current(payload);
        setConditions(Array.isArray(next) ? next : []);
      } catch (err) {
        // eslint-disable-next-line no-console
        console.warn(`[useSidePaneFilter:${paneId}] translator threw, dropping message`, err);
      }
    });
    return unsubscribe;
  }, [paneId]);

  return conditions;
}
