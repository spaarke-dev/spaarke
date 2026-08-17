/**
 * SearchFilter — SmartTodo Code Page expanding text search (smart-todo-r5 UAT
 * decision, 2026-08-17).
 *
 * REPLACES the structured Filter pane (task 021, FR-06 / F-3 — Priority /
 * Status / Due-date / Assigned-To categories) per the operator's UAT
 * decision: a single free-text search box, expanded by task 020's Filter
 * pill (`isFilterPaneOpen` / `onToggleFilterPane`, unchanged), that matches
 * the To Do's NAME + DESCRIPTION + ASSIGNED-TO display name (case-insensitive
 * substring) as the user types.
 *
 * Fully controlled: this component holds NO filter-predicate state of its
 * own — `value`/`onChange` are owned by `SmartTodoApp.tsx` and threaded to
 * `<SmartToDo searchQuery={value}>`'s existing client-side substring filter
 * (which this task extends to also match assigned-to — see
 * `SmartToDo.tsx`'s `displayItems` memo). The bar itself stays MOUNTED
 * across open/close (visibility toggled via CSS `display: none`, not
 * unmount) so the search text survives a close/reopen — mirrors the
 * structured FilterPane's precedent for the same reason (state continuity,
 * NFR-03).
 *
 * Regression note (documented for operator sign-off, NOT a bug to fix here):
 * removing the structured Status filter also removes the "Show Completed"
 * toggle that lived in the Filter pane's Status checkboxes (FR-07 / task
 * 022). The operator explicitly chose "replace" over "keep Completed" — see
 * `projects/smart-todo-r5/notes/uat-filter-text-search.md`.
 *
 * @see ADR-021 Fluent UI v9 design system (semantic tokens only)
 * @see ADR-012 Shared component library (reuses Fluent's SearchBox — no new
 *      text-input control was built for this)
 * @see projects/smart-todo-r5/notes/uat-filter-text-search.md
 */
import * as React from 'react';
import { SearchBox, Text } from '@fluentui/react-components';
import type { InputOnChangeData, SearchBoxChangeEvent } from '@fluentui/react-components';
import { useSearchFilterStyles } from './SearchFilter.styles';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SearchFilterProps {
  /** Controlled open state — driven by task 020's Filter pill (`isFilterPaneOpen`). */
  isOpen: boolean;
  /** Current search text. Fully controlled — this component holds no state of its own. */
  value: string;
  /** Invoked with the next search text on every keystroke (never debounced internally). */
  onChange: (next: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SearchFilter: React.FC<SearchFilterProps> = ({ isOpen, value, onChange }) => {
  const styles = useSearchFilterStyles();
  const inputRef = React.useRef<HTMLInputElement>(null);

  // Auto-focus the box each time it expands — the user just clicked "Filter"
  // specifically to start typing a search.
  React.useEffect(() => {
    if (isOpen) {
      inputRef.current?.focus();
    }
  }, [isOpen]);

  const handleChange = React.useCallback(
    (_ev: SearchBoxChangeEvent, data: InputOnChangeData) => {
      onChange(data.value);
    },
    [onChange],
  );

  return (
    <div
      className={isOpen ? styles.rootOpen : styles.rootClosed}
      data-testid="search-filter"
      aria-hidden={!isOpen}
    >
      <Text weight="semibold" size={200} className={styles.label}>
        Search
      </Text>
      <SearchBox
        ref={inputRef}
        className={styles.searchBox}
        value={value}
        onChange={handleChange}
        placeholder="Search by name, description, or assignee…"
        aria-label="Search to-do items by name, description, or assignee"
        data-testid="search-filter-input"
      />
    </div>
  );
};

SearchFilter.displayName = 'SmartTodoSearchFilter';
