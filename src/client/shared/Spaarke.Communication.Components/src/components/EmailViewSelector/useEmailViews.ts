/**
 * useEmailViews.ts
 *
 * Host-wiring hook for the Email surface's view picker
 * (email-communication-solution-r5 task 031, FR-04; design Lens 2/Lens 4).
 *
 * Fetches the `sprk_communication` saved-view list via the injected
 * `IDataverseClient.retrieveSavedQueriesForEntity` (ADR-028 — no bespoke auth
 * surface, works under both the Xrm host-context adapter and the BFF
 * `authenticatedFetch` adapter), defaults the selection to the "Email —
 * Inbox" saved view (falling back to the entity's default view when absent),
 * and re-runs the selected view's FetchXML whenever the selection changes —
 * preserving the maker's filter/sort/columns verbatim but INJECTING the
 * association columns the left-list status dot depends on (via
 * `ensureAssociationColumns`), so the dot always resolves regardless of the
 * maker's column set.
 *
 * The Email filter lives entirely in each maker-authored view's FetchXML
 * (`sprk_communicationtype = Email`) — this hook does NOT add its own Email
 * predicate on top of the view (spec Lens 5 saved-view source policy). The
 * rows returned here are raw Dataverse records (shape governed by the
 * columns each view's FetchXML selects); the host that ultimately renders
 * `<EmailCardList />` (task 032, ReadingPaneShell) owns mapping those raw
 * records into the `EmailCardItem` shape — mirroring the "host owns mapping"
 * boundary already documented on `EmailCardList.types.ts`.
 *
 * Errors from either the view-list load or a per-view FetchXML run are
 * surfaced via `error` (FR-19 error banner) rather than thrown — a rejected
 * promise here must never crash the surface.
 *
 * Escalation note (task POML `<escalation>`): if NO saved views resolve for
 * `sprk_communication` at all (neither "Email — Inbox" nor any default view),
 * that is a maker/config prerequisite gap (spec Lens 5), not something this
 * hook hardcodes a FetchXML for — it surfaces a descriptive `error` instead
 * of crashing, so the gap is visible and actionable rather than silent.
 */
import * as React from 'react';
import type { IDataverseClient, SavedView } from '@spaarke/ui-components';
import { ensureAssociationColumns } from './ensureAssociationColumns';

/** Entity this surface's view picker is scoped to. */
const COMMUNICATION_ENTITY = 'sprk_communication';

/** The default saved-view name the Email surface opens to (spec Lens 2). */
export const EMAIL_INBOX_VIEW_NAME = 'Email — Inbox';

/** Result shape returned by {@link useEmailViews}. */
export interface UseEmailViewsResult<T = Record<string, unknown>> {
  /** All `sprk_communication` saved views, mapped to the `ViewSelector` shape. */
  views: ReadonlyArray<SavedView>;
  /** The currently active view id (undefined only before the initial load resolves). */
  selectedViewId: string | undefined;
  /** Change the active view — triggers a re-run of that view's FetchXML. */
  setSelectedViewId: (viewId: string) => void;
  /** Raw rows returned by the active view's FetchXML (already Email-filtered by the view itself). */
  rows: ReadonlyArray<T>;
  /** True while the view list OR the active view's rows are being (re)loaded. */
  isLoading: boolean;
  /** Non-null when the view list or the FetchXML run rejected. Never thrown. */
  error: Error | null;
  /** Re-runs the active view's FetchXML (owner UAT 2026-08-03 Item 2 — the left-list Refresh button). No-op until a view has resolved. */
  refetch: () => void;
}

function toError(err: unknown): Error {
  return err instanceof Error ? err : new Error(String(err));
}

function resolveDefaultView(views: ReadonlyArray<SavedView>): SavedView | undefined {
  const inbox = views.find(v => v.name.trim().toLowerCase() === EMAIL_INBOX_VIEW_NAME.toLowerCase());
  if (inbox) return inbox;
  // Fall back to the entity's default view — never crash on a missing named view.
  return views.find(v => v.isDefault) ?? views[0];
}

/**
 * Host-wiring hook: fetches `sprk_communication` saved views, resolves the
 * default selection, and re-runs the selected view's FetchXML on change.
 *
 * @param client Injected Dataverse access adapter (Xrm or BFF) — ADR-028.
 */
export function useEmailViews<T = Record<string, unknown>>(client: IDataverseClient): UseEmailViewsResult<T> {
  const [views, setViews] = React.useState<ReadonlyArray<SavedView>>([]);
  const [selectedViewId, setSelectedViewId] = React.useState<string | undefined>(undefined);
  const [rows, setRows] = React.useState<ReadonlyArray<T>>([]);
  const [isLoadingViews, setIsLoadingViews] = React.useState<boolean>(true);
  const [isLoadingRows, setIsLoadingRows] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);
  // Monotonic key bumped by `refetch()` to force a re-run of the rows effect
  // below WITHOUT changing the selected view (owner UAT 2026-08-03 Item 2).
  const [reloadKey, setReloadKey] = React.useState<number>(0);
  const refetch = React.useCallback(() => setReloadKey(k => k + 1), []);

  // Load the saved-view list once per client instance, then resolve the
  // default selection ("Email — Inbox", falling back to the entity default).
  React.useEffect(() => {
    let cancelled = false;
    setIsLoadingViews(true);
    setError(null);

    client
      .retrieveSavedQueriesForEntity(COMMUNICATION_ENTITY)
      .then(summaries => {
        if (cancelled) return;
        const mapped: SavedView[] = summaries.map(s => ({ id: s.id, name: s.name, isDefault: s.isDefault }));
        setViews(mapped);

        const initial = resolveDefaultView(mapped);
        if (!initial) {
          // No saved views at all for sprk_communication — a maker/config
          // prerequisite gap (spec Lens 5). Surface, do not crash.
          setError(
            new Error(
              `No saved views found for entity "${COMMUNICATION_ENTITY}" — expected at least an ` +
                `"${EMAIL_INBOX_VIEW_NAME}" view or an entity default view.`
            )
          );
          setIsLoadingViews(false);
          return;
        }
        setSelectedViewId(initial.id);
        setIsLoadingViews(false);
      })
      .catch(err => {
        if (cancelled) return;
        setError(toError(err));
        setIsLoadingViews(false);
      });

    return () => {
      cancelled = true;
    };
  }, [client]);

  // Re-run the selected view's FetchXML whenever the selection changes (initial
  // resolution included). The view's own Email filter/sort/columns are preserved
  // verbatim — we only INJECT the association columns the left-list status dot
  // depends on (via `ensureAssociationColumns`) so the dot always resolves
  // regardless of the maker's column set. A maker inbox view commonly omits the
  // association status/provenance/typed-lookup columns, which previously left the
  // dot undefined; augmenting here restores it without forcing makers to edit
  // every view.
  React.useEffect(() => {
    if (!selectedViewId) return;
    let cancelled = false;
    setIsLoadingRows(true);
    setError(null);

    client
      .retrieveSavedQuery(selectedViewId)
      .then(view =>
        client.retrieveMultipleRecords<T>(
          view.entityName || COMMUNICATION_ENTITY,
          ensureAssociationColumns(view.fetchXml)
        )
      )
      .then(result => {
        if (cancelled) return;
        setRows(result.entities);
        setIsLoadingRows(false);
      })
      .catch(err => {
        if (cancelled) return;
        setError(toError(err));
        setIsLoadingRows(false);
      });

    return () => {
      cancelled = true;
    };
    // `reloadKey` is a dependency so `refetch()` re-runs this exact effect (same
    // selected view) — the left-list Refresh button (owner UAT 2026-08-03 Item 2).
  }, [client, selectedViewId, reloadKey]);

  return {
    views,
    selectedViewId,
    setSelectedViewId,
    rows,
    isLoading: isLoadingViews || isLoadingRows,
    error,
    refetch,
  };
}
