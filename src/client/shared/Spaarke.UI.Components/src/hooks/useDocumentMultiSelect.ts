/**
 * useDocumentMultiSelect.ts
 *
 * Generic Set-based multi-selection state hook intended for document grids
 * (SemanticSearchControl results, DocumentRelationshipViewer, etc.) but
 * usable for any string-id selection scenario.
 *
 * The hook owns a `Set<string>` of selected ids and exposes ergonomic
 * operations (toggle, select, deselect, clear, selectAll, isSelected, count).
 * All callbacks are stable across renders so the hook can be safely passed
 * through props and React.memo barriers.
 *
 * @example
 * ```tsx
 * const sel = useDocumentMultiSelect();
 * <Checkbox checked={sel.isSelected(doc.documentId)}
 *           onChange={() => sel.toggle(doc.documentId)} />
 * <Button disabled={sel.count === 0} onClick={openEmailWizard}>
 *   Email ({sel.count})
 * </Button>
 * ```
 */
import * as React from 'react';

// ---------------------------------------------------------------------------
// Result shape
// ---------------------------------------------------------------------------

/**
 * Return shape of {@link useDocumentMultiSelect}.
 *
 * Note: `selected` is a `Set<string>` — when you pass it as a React prop
 * downstream, the reference changes on every state update so React's default
 * shallow equality will see "different" sets. Consumers that need stable
 * referential equality should derive their own memoized snapshot.
 */
export interface UseDocumentMultiSelectResult {
  /** Current selected ids (e.g. sprk_document GUIDs). */
  readonly selected: Set<string>;
  /** Returns `true` iff `id` is in the current selection. */
  isSelected(id: string): boolean;
  /** Adds the id if missing, removes it if present. */
  toggle(id: string): void;
  /** Adds the id (no-op if already selected). */
  select(id: string): void;
  /** Removes the id (no-op if not selected). */
  deselect(id: string): void;
  /** Clears the selection. */
  clear(): void;
  /** Replaces the selection with the given ids (de-duplicated). */
  selectAll(ids: string[]): void;
  /** Number of selected ids. */
  readonly count: number;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Hook that manages a `Set<string>` selection model.
 *
 * @returns A stable result object containing the current selection and
 *   ergonomic mutator callbacks. All callbacks have stable identity across
 *   renders (safe to put in dependency arrays).
 */
export function useDocumentMultiSelect(): UseDocumentMultiSelectResult {
  const [selected, setSelected] = React.useState<Set<string>>(() => new Set<string>());

  const isSelected = React.useCallback(
    (id: string): boolean => {
      return selected.has(id);
    },
    [selected]
  );

  const toggle = React.useCallback((id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const select = React.useCallback((id: string) => {
    setSelected(prev => {
      if (prev.has(id)) return prev;
      const next = new Set(prev);
      next.add(id);
      return next;
    });
  }, []);

  const deselect = React.useCallback((id: string) => {
    setSelected(prev => {
      if (!prev.has(id)) return prev;
      const next = new Set(prev);
      next.delete(id);
      return next;
    });
  }, []);

  const clear = React.useCallback(() => {
    setSelected(prev => (prev.size === 0 ? prev : new Set<string>()));
  }, []);

  const selectAll = React.useCallback((ids: string[]) => {
    setSelected(() => new Set(ids));
  }, []);

  return React.useMemo<UseDocumentMultiSelectResult>(
    () => ({
      selected,
      isSelected,
      toggle,
      select,
      deselect,
      clear,
      selectAll,
      count: selected.size,
    }),
    [selected, isSelected, toggle, select, deselect, clear, selectAll]
  );
}
