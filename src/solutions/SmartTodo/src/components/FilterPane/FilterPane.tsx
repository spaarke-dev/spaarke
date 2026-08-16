/**
 * FilterPane — SmartTodo Code Page filter pane (task 021, FR-06 / F-3).
 *
 * Opened by task 020's Filter pill (`isFilterPaneOpen` / `onToggleFilterPane`,
 * lifted in `SmartTodoApp.tsx`). Exposes 4 expandable categories:
 *   - Priority     — multi-select of `sprk_priority` choice values (reuses the
 *                    shared `TagFilter` chip filter per CLAUDE.md §11 — no new
 *                    multi-select control was built for this).
 *   - Status       — Open / In Progress / Completed (multi-select checkboxes).
 *   - Due date     — Today / Tomorrow / This week / Overdue (single-select,
 *                    with an "Any" option to clear the category).
 *   - Assigned To  — typeahead against `contact` via `Xrm.WebApi` (single-
 *                    select), replicating the exact pattern task 020 removed
 *                    from `Header.tsx` (250ms debounce, `contains(fullname,…)`,
 *                    statecode eq 0, $top=8, $orderby fullname asc) — per
 *                    docs/standards/DATA-ACCESS-DECISION-CRITERIA.md, this
 *                    uses the `IWebApi` handle already threaded through this
 *                    Code Page (obtained via `getWebApi()` in `xrmProvider.ts`,
 *                    which IS Xrm.WebApi under the hood) rather than reaching
 *                    into `globalThis.Xrm` directly — same host-context
 *                    contract, more testable (inject a mock `IWebApi`).
 *   - Clear all    — resets to `DEFAULT_TODO_FILTER` (Status {Open, In
 *                    Progress}, everything else unfiltered) — NOT an
 *                    all-clear/empty state.
 *
 * Fully controlled: this component holds NO filter-predicate state of its
 * own — `filterState`/`onChange` are owned by `SmartTodoApp.tsx` (NFR-03:
 * filter state lives above the Kanban, independent of the CSS-only
 * orientation swap). The pane itself stays MOUNTED across open/close
 * (visibility toggled via CSS `display: none`, not unmount) so the
 * Assigned-To typeahead's local query/results state survives a close/reopen.
 *
 * @see ADR-021 Fluent UI v9 design system (semantic tokens only)
 * @see ADR-012 Shared component library (TagFilter reuse)
 * @see docs/standards/DATA-ACCESS-DECISION-CRITERIA.md
 * @see projects/smart-todo-r5/spec.md FR-06 (F-3)
 */
import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Checkbox,
  Input,
  Radio,
  RadioGroup,
  Spinner,
  Text,
} from '@fluentui/react-components';
import type {
  CheckboxOnChangeData,
  InputOnChangeData,
  RadioGroupOnChangeData,
} from '@fluentui/react-components';
import { TagFilter } from '@spaarke/ui-components';
import type { TagFilterOption } from '@spaarke/ui-components';
import {
  DEFAULT_TODO_FILTER,
  TODO_PRIORITY_CHOICE_VALUES,
} from '../../services/queryHelpers';
import type {
  ITodoFilterState,
  TodoDueDateCategory,
  TodoStatusFilterValue,
} from '../../services/queryHelpers';
import type { IWebApi } from '../../types/xrm';
import { useFilterPaneStyles } from './FilterPane.styles';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Assigned-To typeahead tuning — replicates the exact values from the
 * pre-task-020 `Header.tsx` Assigned-To quick-add field (debounce 250ms,
 * min 2 chars, $top=8) so the UX is identical to what users had before.
 */
const ASSIGNED_TO_DEBOUNCE_MS = 250;
const ASSIGNED_TO_MIN_CHARS = 2;
const ASSIGNED_TO_MAX_RESULTS = 8;
/** Blur-close delay — long enough for a `mousedown` on a result row to register first. */
const ASSIGNED_TO_BLUR_CLOSE_DELAY_MS = 150;

interface IContactResult {
  id: string;
  name: string;
}

const STATUS_OPTIONS: ReadonlyArray<{ value: TodoStatusFilterValue; label: string }> = [
  { value: 'Open', label: 'Open' },
  { value: 'InProgress', label: 'In Progress' },
  { value: 'Completed', label: 'Completed' },
];

const DUE_DATE_OPTIONS: ReadonlyArray<{ value: TodoDueDateCategory; label: string }> = [
  { value: 'Today', label: 'Today' },
  { value: 'Tomorrow', label: 'Tomorrow' },
  { value: 'ThisWeek', label: 'This week' },
  { value: 'Overdue', label: 'Overdue' },
];

/** `sprk_priority` choice values converted to `TagFilter`'s string-keyed contract. */
const PRIORITY_TAG_OPTIONS: TagFilterOption[] = TODO_PRIORITY_CHOICE_VALUES.map((p) => ({
  value: String(p.value),
  label: p.label,
}));

/** Sentinel RadioGroup value for "no due-date filter" (clears `dueDateCategory`). */
const DUE_DATE_ANY_VALUE = 'any';

const ACCORDION_ITEMS = ['priority', 'status', 'dueDate', 'assignedTo'] as const;

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface FilterPaneProps {
  /** Controlled open state — driven by task 020's Filter pill (`isFilterPaneOpen`). */
  isOpen: boolean;
  /** Current filter predicate. Fully controlled — this component holds no filter state of its own. */
  filterState: ITodoFilterState;
  /** Invoked with the NEXT full filter state on every user interaction (never a delta). */
  onChange: (next: ITodoFilterState) => void;
  /** Xrm.WebApi reference (host-context — DATA-ACCESS-DECISION-CRITERIA) for the Assigned-To typeahead. */
  webApi: IWebApi;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FilterPane: React.FC<FilterPaneProps> = ({
  isOpen,
  filterState,
  onChange,
  webApi,
}) => {
  const styles = useFilterPaneStyles();

  // ── Priority (TagFilter multi-select — reuses the shared component) ──────
  const priorityStrings = React.useMemo(
    () => filterState.priorityValues.map(String),
    [filterState.priorityValues],
  );
  const handlePriorityChange = React.useCallback(
    (selected: string[]) => {
      onChange({ ...filterState, priorityValues: selected.map(Number) });
    },
    [filterState, onChange],
  );

  // ── Status (3 checkboxes) ─────────────────────────────────────────────────
  const handleStatusToggle = React.useCallback(
    (value: TodoStatusFilterValue, checked: boolean) => {
      const next = checked
        ? [...filterState.statusValues, value]
        : filterState.statusValues.filter((v) => v !== value);
      onChange({ ...filterState, statusValues: next });
    },
    [filterState, onChange],
  );

  // ── Due date (single-select radio, with an "Any" clear option) ───────────
  const handleDueDateChange = React.useCallback(
    (_ev: React.FormEvent<HTMLDivElement>, data: RadioGroupOnChangeData) => {
      onChange({
        ...filterState,
        dueDateCategory:
          data.value === DUE_DATE_ANY_VALUE ? undefined : (data.value as TodoDueDateCategory),
      });
    },
    [filterState, onChange],
  );

  // ── Assigned To (Xrm.WebApi contact typeahead — replicates the pattern
  //    removed from Header.tsx in task 020) ─────────────────────────────────
  const [assignedToQuery, setAssignedToQuery] = React.useState<string>('');
  const [assignedToResults, setAssignedToResults] = React.useState<IContactResult[]>([]);
  const [isSearchingContacts, setIsSearchingContacts] = React.useState<boolean>(false);
  const [showAssignedToResults, setShowAssignedToResults] = React.useState<boolean>(false);

  React.useEffect(() => {
    const q = assignedToQuery.trim();
    if (q.length < ASSIGNED_TO_MIN_CHARS) {
      setAssignedToResults([]);
      setShowAssignedToResults(false);
      setIsSearchingContacts(false);
      return;
    }
    if (!webApi?.retrieveMultipleRecords) {
      setShowAssignedToResults(false);
      setIsSearchingContacts(false);
      return;
    }

    let cancelled = false;
    setIsSearchingContacts(true);
    const handle = window.setTimeout(() => {
      const escaped = q.replace(/'/g, "''");
      const filter = `statecode eq 0 and contains(fullname,'${escaped}')`;
      const options = `?$select=contactid,fullname&$filter=${filter}&$top=${ASSIGNED_TO_MAX_RESULTS}&$orderby=fullname asc`;
      webApi
        .retrieveMultipleRecords('contact', options)
        .then((result) => {
          if (cancelled) return;
          const mapped = (result.entities ?? [])
            .filter((e) => !!e.contactid && !!e.fullname)
            .map((e) => ({ id: e.contactid as string, name: e.fullname as string }));
          setAssignedToResults(mapped);
          setShowAssignedToResults(true);
          setIsSearchingContacts(false);
        })
        .catch(() => {
          if (cancelled) return;
          setAssignedToResults([]);
          setShowAssignedToResults(false);
          setIsSearchingContacts(false);
        });
    }, ASSIGNED_TO_DEBOUNCE_MS);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [assignedToQuery, webApi]);

  const handleSelectAssignedTo = React.useCallback(
    (contact: IContactResult) => {
      setAssignedToQuery(contact.name);
      setShowAssignedToResults(false);
      onChange({ ...filterState, assignedToContactId: contact.id });
    },
    [filterState, onChange],
  );

  const handleAssignedToInputChange = React.useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: InputOnChangeData) => {
      setAssignedToQuery(data.value);
      // Typing invalidates a previously selected contact until the user picks
      // a new one from the results (mirrors the removed Header pattern).
      if (filterState.assignedToContactId) {
        onChange({ ...filterState, assignedToContactId: undefined });
      }
    },
    [filterState, onChange],
  );

  const handleAssignedToBlur = React.useCallback(() => {
    window.setTimeout(() => setShowAssignedToResults(false), ASSIGNED_TO_BLUR_CLOSE_DELAY_MS);
  }, []);

  const handleAssignedToFocus = React.useCallback(() => {
    if (assignedToResults.length > 0) setShowAssignedToResults(true);
  }, [assignedToResults.length]);

  // ── Clear all ──────────────────────────────────────────────────────────
  const handleClearAll = React.useCallback(() => {
    setAssignedToQuery('');
    setAssignedToResults([]);
    setShowAssignedToResults(false);
    onChange(DEFAULT_TODO_FILTER);
  }, [onChange]);

  // ── Render ───────────────────────────────────────────────────────────────
  return (
    <div
      className={isOpen ? styles.rootOpen : styles.rootClosed}
      data-testid="filter-pane"
      aria-hidden={!isOpen}
    >
      <div className={styles.headerRow}>
        <Text weight="semibold" size={300}>
          Filter
        </Text>
        <Button
          appearance="subtle"
          size="small"
          onClick={handleClearAll}
          data-testid="filter-pane-clear-all"
        >
          Clear all
        </Button>
      </div>

      <Accordion multiple collapsible defaultOpenItems={[...ACCORDION_ITEMS]}>
        <AccordionItem value="priority">
          <AccordionHeader>Priority</AccordionHeader>
          <AccordionPanel>
            <TagFilter
              label="Priority"
              options={PRIORITY_TAG_OPTIONS}
              selected={priorityStrings}
              onChange={handlePriorityChange}
            />
          </AccordionPanel>
        </AccordionItem>

        <AccordionItem value="status">
          <AccordionHeader>Status</AccordionHeader>
          <AccordionPanel>
            <div className={styles.checkboxGroup}>
              {STATUS_OPTIONS.map((opt) => (
                <Checkbox
                  key={opt.value}
                  label={opt.label}
                  checked={filterState.statusValues.includes(opt.value)}
                  onChange={(_ev, data: CheckboxOnChangeData) =>
                    handleStatusToggle(opt.value, !!data.checked)
                  }
                  data-testid={`filter-pane-status-${opt.value}`}
                />
              ))}
            </div>
          </AccordionPanel>
        </AccordionItem>

        <AccordionItem value="dueDate">
          <AccordionHeader>Due date</AccordionHeader>
          <AccordionPanel>
            <RadioGroup
              value={filterState.dueDateCategory ?? DUE_DATE_ANY_VALUE}
              onChange={handleDueDateChange}
              data-testid="filter-pane-due-date"
            >
              <Radio value={DUE_DATE_ANY_VALUE} label="Any" />
              {DUE_DATE_OPTIONS.map((opt) => (
                <Radio key={opt.value} value={opt.value} label={opt.label} />
              ))}
            </RadioGroup>
          </AccordionPanel>
        </AccordionItem>

        <AccordionItem value="assignedTo">
          <AccordionHeader>Assigned To</AccordionHeader>
          <AccordionPanel>
            <div className={styles.assignedToContainer}>
              <Input
                value={assignedToQuery}
                onChange={handleAssignedToInputChange}
                onFocus={handleAssignedToFocus}
                onBlur={handleAssignedToBlur}
                placeholder="Search by name…"
                contentAfter={isSearchingContacts ? <Spinner size="tiny" /> : undefined}
                data-testid="filter-pane-assigned-to-input"
              />
              {showAssignedToResults && assignedToResults.length > 0 ? (
                <ul
                  className={styles.assignedToResultsList}
                  role="listbox"
                  aria-label="Assigned To search results"
                  data-testid="filter-pane-assigned-to-results"
                >
                  {assignedToResults.map((c) => (
                    <li key={c.id} role="presentation">
                      <button
                        type="button"
                        role="option"
                        aria-selected={filterState.assignedToContactId === c.id}
                        className={styles.assignedToResultItem}
                        onClick={() => handleSelectAssignedTo(c)}
                        data-testid={`filter-pane-assigned-to-result-${c.id}`}
                      >
                        {c.name}
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          </AccordionPanel>
        </AccordionItem>
      </Accordion>
    </div>
  );
};

FilterPane.displayName = 'SmartTodoFilterPane';
