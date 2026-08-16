/**
 * FilterPane — unit tests (task 021, FR-06 / F-3 filter pane).
 *
 * Covers the closed acceptance set from
 * `projects/smart-todo-r5/tasks/021-filter-pane.poml` step 7:
 *   - Default state renders Status {Open, In Progress} checked, everything
 *     else unfiltered (Priority none selected, Due-date "Any", Assigned-To
 *     empty).
 *   - Each category invokes `onChange` with the correctly narrowed next
 *     filter state (Priority / Status / Due-date / Assigned-To).
 *   - Assigned-To typeahead uses the injected `IWebApi` (Xrm.WebApi), NOT
 *     `fetch`/BFF — per docs/standards/DATA-ACCESS-DECISION-CRITERIA.md.
 *   - "Clear all" resets to exactly `DEFAULT_TODO_FILTER`.
 *   - NFR-03 proxy: toggling `isOpen` (the pane's own visibility — a stand-in
 *     for the orientation flip, which lives entirely outside this
 *     component's props) never invokes `onChange` — filterState survives
 *     unrelated re-renders because this component holds no filter state of
 *     its own.
 *
 * Test harness note: mirrors `Header.test.tsx`'s harness — this package's
 * node_modules has `@testing-library/jest-dom` but NOT
 * `@testing-library/react` (see that file's header comment for the full
 * rationale). `@spaarke/ui-components` is mocked because its barrel
 * (`services/index.js`) unconditionally requires `@spaarke/sdap-client`,
 * which isn't built in this worktree — mocking keeps this test hermetic to
 * FilterPane's own logic. The mock `TagFilter` renders one checkbox per
 * option so tests can exercise the same controlled `selected`/`onChange`
 * contract the real component exposes.
 */

jest.mock('@spaarke/ui-components', () => {
  const ReactActual = require('react') as typeof import('react');
  interface MockTagFilterProps {
    options: Array<{ value: string; label: string }>;
    selected: string[];
    onChange: (next: string[]) => void;
    label?: string;
  }
  return {
    TagFilter: ({ options, selected, onChange, label }: MockTagFilterProps) =>
      ReactActual.createElement(
        'div',
        { 'data-testid': 'mock-tag-filter', 'aria-label': label },
        options.map((opt) =>
          ReactActual.createElement(
            'label',
            { key: opt.value },
            ReactActual.createElement('input', {
              type: 'checkbox',
              'data-testid': `mock-tag-filter-option-${opt.value}`,
              checked: selected.includes(opt.value),
              onChange: () => {
                const next = selected.includes(opt.value)
                  ? selected.filter((v) => v !== opt.value)
                  : [...selected, opt.value];
                onChange(next);
              },
            }),
            opt.label,
          ),
        ),
      ),
  };
});

import '@testing-library/jest-dom';
import * as React from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FilterPane, type FilterPaneProps } from '../FilterPane';
import { DEFAULT_TODO_FILTER, type ITodoFilterState } from '../../../services/queryHelpers';
import type { IWebApi, RetrieveMultipleResult } from '../../../types/xrm';

// React 19 requires this flag for `act()` to recognize the jsdom environment
// as a test environment — see Header.test.tsx for the same note.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

// ---------------------------------------------------------------------------
// Minimal render harness (no @testing-library/react available — see header).
// ---------------------------------------------------------------------------

function fireClick(el: Element): void {
  act(() => {
    (el as HTMLElement).click();
  });
}

/**
 * Resolves the underlying native `<input>` for a `data-testid`, whether
 * Fluent forwards the attribute to the input itself (Input) or to a
 * wrapping element (Checkbox/Radio's `<label>` wrapper).
 */
function findInputByTestId(root: ParentNode, testid: string): HTMLInputElement {
  const el = root.querySelector(`[data-testid="${testid}"]`);
  if (!el) throw new Error(`No element with data-testid=${testid}`);
  if (el.tagName === 'INPUT') return el as HTMLInputElement;
  const nested = el.querySelector('input');
  if (!nested) throw new Error(`No <input> found under data-testid=${testid}`);
  return nested as HTMLInputElement;
}

function findByTestId(root: ParentNode, testid: string): HTMLElement {
  const el = root.querySelector(`[data-testid="${testid}"]`);
  if (!el) throw new Error(`No element with data-testid=${testid}`);
  return el as HTMLElement;
}

function queryByTestId(root: ParentNode, testid: string): HTMLElement | null {
  return root.querySelector(`[data-testid="${testid}"]`) as HTMLElement | null;
}

function setInputValue(input: HTMLInputElement, value: string): void {
  const nativeSetter = Object.getOwnPropertyDescriptor(
    window.HTMLInputElement.prototype,
    'value',
  )?.set;
  act(() => {
    nativeSetter?.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

interface Harness {
  container: HTMLDivElement;
  unmount: () => void;
  onChange: jest.Mock;
}

function makeWebApiMock(
  impl?: (entity: string, options?: string) => Promise<RetrieveMultipleResult>,
): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(
      impl ?? (async () => ({ entities: [] })),
    ) as unknown as IWebApi['retrieveMultipleRecords'],
    retrieveRecord: jest.fn(async () => ({})) as unknown as IWebApi['retrieveRecord'],
    createRecord: jest.fn(async () => ({ id: 'x' })) as unknown as IWebApi['createRecord'],
    updateRecord: jest.fn(async () => ({ id: 'x' })) as unknown as IWebApi['updateRecord'],
    deleteRecord: jest.fn(async () => ({ id: 'x' })) as unknown as IWebApi['deleteRecord'],
  };
}

function renderFilterPane(overrides: Partial<FilterPaneProps> = {}): Harness {
  const onChange = jest.fn();

  const props: FilterPaneProps = {
    isOpen: true,
    filterState: DEFAULT_TODO_FILTER,
    onChange,
    webApi: makeWebApiMock(),
    ...overrides,
  };

  const container = document.createElement('div');
  document.body.appendChild(container);
  let root: Root;
  act(() => {
    root = createRoot(container);
    root.render(
      <FluentProvider theme={webLightTheme}>
        <FilterPane {...props} />
      </FluentProvider>,
    );
  });

  return {
    container,
    unmount: () => {
      act(() => {
        root.unmount();
      });
      container.remove();
    },
    onChange,
  };
}

let activeHarness: Harness | undefined;

afterEach(() => {
  activeHarness?.unmount();
  activeHarness = undefined;
  jest.useRealTimers();
});

describe('FilterPane — default state (FR-06 acceptance)', () => {
  it('render_DefaultTodoFilter_StatusOpenAndInProgressCheckedOnly', () => {
    const h = (activeHarness = renderFilterPane());

    expect(findInputByTestId(h.container, 'filter-pane-status-Open').checked).toBe(true);
    expect(findInputByTestId(h.container, 'filter-pane-status-InProgress').checked).toBe(true);
    expect(findInputByTestId(h.container, 'filter-pane-status-Completed').checked).toBe(false);
  });

  it('render_DefaultTodoFilter_NoPriorityChipsSelected', () => {
    const h = (activeHarness = renderFilterPane());
    expect(findInputByTestId(h.container, 'mock-tag-filter-option-100000000').checked).toBe(
      false,
    );
    expect(findInputByTestId(h.container, 'mock-tag-filter-option-100000003').checked).toBe(
      false,
    );
  });

  it('render_DefaultTodoFilter_DueDateAnySelected', () => {
    const h = (activeHarness = renderFilterPane());
    const anyRadio = h.container.querySelector('input[type="radio"][value="any"]');
    expect(anyRadio).toHaveProperty('checked', true);
  });

  it('render_DefaultTodoFilter_AssignedToInputEmpty', () => {
    const h = (activeHarness = renderFilterPane());
    expect(findInputByTestId(h.container, 'filter-pane-assigned-to-input').value).toBe('');
  });

  it('render_IsOpenFalse_MarksAriaHiddenTrue', () => {
    const h = (activeHarness = renderFilterPane({ isOpen: false }));
    expect(findByTestId(h.container, 'filter-pane')).toHaveAttribute('aria-hidden', 'true');
  });

  it('render_IsOpenTrue_MarksAriaHiddenFalse', () => {
    const h = (activeHarness = renderFilterPane({ isOpen: true }));
    expect(findByTestId(h.container, 'filter-pane')).toHaveAttribute('aria-hidden', 'false');
  });
});

describe('FilterPane — Priority category', () => {
  it('click_UrgentChip_InvokesOnChangeWithUrgentInPriorityValues', () => {
    const h = (activeHarness = renderFilterPane());
    fireClick(findInputByTestId(h.container, 'mock-tag-filter-option-100000000'));
    expect(h.onChange).toHaveBeenCalledWith({
      ...DEFAULT_TODO_FILTER,
      priorityValues: [100000000],
    });
  });

  it('click_SelectedChipAgain_RemovesItFromPriorityValues', () => {
    const filterState: ITodoFilterState = {
      ...DEFAULT_TODO_FILTER,
      priorityValues: [100000000],
    };
    const h = (activeHarness = renderFilterPane({ filterState }));
    fireClick(findInputByTestId(h.container, 'mock-tag-filter-option-100000000'));
    expect(h.onChange).toHaveBeenCalledWith({ ...filterState, priorityValues: [] });
  });
});

describe('FilterPane — Status category', () => {
  it('check_Completed_InvokesOnChangeWithCompletedAddedToStatusValues', () => {
    const h = (activeHarness = renderFilterPane());
    fireClick(findInputByTestId(h.container, 'filter-pane-status-Completed'));
    expect(h.onChange).toHaveBeenCalledWith({
      ...DEFAULT_TODO_FILTER,
      statusValues: ['Open', 'InProgress', 'Completed'],
    });
  });

  it('uncheck_Open_InvokesOnChangeWithOpenRemovedFromStatusValues', () => {
    const h = (activeHarness = renderFilterPane());
    fireClick(findInputByTestId(h.container, 'filter-pane-status-Open'));
    expect(h.onChange).toHaveBeenCalledWith({
      ...DEFAULT_TODO_FILTER,
      statusValues: ['InProgress'],
    });
  });
});

describe('FilterPane — Due date category', () => {
  it('select_Today_InvokesOnChangeWithDueDateCategoryToday', () => {
    const h = (activeHarness = renderFilterPane());
    const todayRadio = h.container.querySelector(
      'input[type="radio"][value="Today"]',
    ) as HTMLInputElement;
    fireClick(todayRadio);
    expect(h.onChange).toHaveBeenCalledWith({
      ...DEFAULT_TODO_FILTER,
      dueDateCategory: 'Today',
    });
  });

  it('select_Any_FromANonDefaultCategory_ClearsDueDateCategory', () => {
    const filterState: ITodoFilterState = { ...DEFAULT_TODO_FILTER, dueDateCategory: 'Overdue' };
    const h = (activeHarness = renderFilterPane({ filterState }));
    const anyRadio = h.container.querySelector(
      'input[type="radio"][value="any"]',
    ) as HTMLInputElement;
    fireClick(anyRadio);
    expect(h.onChange).toHaveBeenCalledWith({ ...filterState, dueDateCategory: undefined });
  });
});

describe('FilterPane — Assigned To category (Xrm.WebApi typeahead)', () => {
  it('type_TwoOrMoreChars_DebouncesThenQueriesContactViaWebApiNotFetch', async () => {
    jest.useFakeTimers();
    const retrieveMultipleRecords = jest.fn(async (_entity: string, _options?: string) => ({
      entities: [{ contactid: 'c-1', fullname: 'Jordan Rivera' }],
    }));
    const webApi = makeWebApiMock(retrieveMultipleRecords);
    // Spy on global fetch (if present) to assert it's never invoked — the
    // typeahead MUST use Xrm.WebApi, never a BFF fetch call.
    const fetchSpy = jest.fn();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (globalThis as any).fetch = fetchSpy;

    const h = (activeHarness = renderFilterPane({ webApi }));
    const input = findInputByTestId(h.container, 'filter-pane-assigned-to-input');

    setInputValue(input, 'jo');

    await act(async () => {
      jest.advanceTimersByTime(300);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    const [entity, options] = retrieveMultipleRecords.mock.calls[0];
    expect(entity).toBe('contact');
    expect(options).toContain("contains(fullname,'jo')");
    expect(options).toContain('statecode eq 0');
    expect(options).toContain('$top=8');
    expect(fetchSpy).not.toHaveBeenCalled();

    const result = findByTestId(h.container, 'filter-pane-assigned-to-result-c-1');
    expect(result.textContent).toBe('Jordan Rivera');

    fireClick(result);
    expect(h.onChange).toHaveBeenCalledWith({
      ...DEFAULT_TODO_FILTER,
      assignedToContactId: 'c-1',
    });
  });

  it('type_OneChar_DoesNotQuery_BelowMinCharsThreshold', async () => {
    jest.useFakeTimers();
    const retrieveMultipleRecords = jest.fn(async () => ({ entities: [] }));
    const webApi = makeWebApiMock(retrieveMultipleRecords);
    const h = (activeHarness = renderFilterPane({ webApi }));

    setInputValue(findInputByTestId(h.container, 'filter-pane-assigned-to-input'), 'j');

    await act(async () => {
      jest.advanceTimersByTime(300);
      await Promise.resolve();
    });

    expect(retrieveMultipleRecords).not.toHaveBeenCalled();
  });
});

describe('FilterPane — Clear all', () => {
  it('click_ClearAll_InvokesOnChangeWithExactlyDefaultTodoFilter', () => {
    const filterState: ITodoFilterState = {
      priorityValues: [100000001],
      statusValues: ['Completed'],
      dueDateCategory: 'ThisWeek',
      assignedToContactId: 'some-contact',
    };
    const h = (activeHarness = renderFilterPane({ filterState }));

    fireClick(findByTestId(h.container, 'filter-pane-clear-all'));

    expect(h.onChange).toHaveBeenCalledWith(DEFAULT_TODO_FILTER);
  });
});

describe('FilterPane — NFR-03 (filter state survives unrelated re-renders)', () => {
  it('toggle_IsOpen_NeverInvokesOnChange', () => {
    const nonDefaultFilter: ITodoFilterState = {
      priorityValues: [100000002],
      statusValues: ['Open'],
      dueDateCategory: 'Tomorrow',
      assignedToContactId: 'c-9',
    };
    const onChange = jest.fn();
    const webApi = makeWebApiMock();

    const container = document.createElement('div');
    document.body.appendChild(container);
    let root: Root;
    act(() => {
      root = createRoot(container);
      root.render(
        <FluentProvider theme={webLightTheme}>
          <FilterPane
            isOpen={true}
            filterState={nonDefaultFilter}
            onChange={onChange}
            webApi={webApi}
          />
        </FluentProvider>,
      );
    });

    // Simulate the pane closing then reopening (the only visibility toggle
    // this component's props expose) — a stand-in for "some unrelated part
    // of SmartTodoApp's state changed" (e.g. orientation). filterState is
    // NOT owned by this component, so nothing here can mutate it.
    act(() => {
      root.render(
        <FluentProvider theme={webLightTheme}>
          <FilterPane
            isOpen={false}
            filterState={nonDefaultFilter}
            onChange={onChange}
            webApi={webApi}
          />
        </FluentProvider>,
      );
    });
    act(() => {
      root.render(
        <FluentProvider theme={webLightTheme}>
          <FilterPane
            isOpen={true}
            filterState={nonDefaultFilter}
            onChange={onChange}
            webApi={webApi}
          />
        </FluentProvider>,
      );
    });

    expect(onChange).not.toHaveBeenCalled();
    expect(
      findInputByTestId(container, 'filter-pane-status-Open').checked,
    ).toBe(true);

    act(() => {
      root.unmount();
    });
    container.remove();
  });
});
