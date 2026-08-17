/**
 * SearchFilter — unit tests (smart-todo-r5 UAT 2026-08-17 — expanding text
 * search, replacing task 021's structured Filter pane; layout revised same
 * day in UAT pass 2 to render inline in `Header.tsx`, left of the Filter
 * pill, with the "Search" caption label removed).
 *
 * Covers the closed acceptance set from the UAT decision (see
 * `projects/smart-todo-r5/notes/uat-filter-text-search.md`):
 *   - `isOpen=false` renders the box hidden (`aria-hidden="true"`, CSS
 *     `display: none`) — clicking the Header's Filter pill (task 020,
 *     unchanged) expands it.
 *   - `isOpen=true` renders the box visible (`aria-hidden="false"`) with the
 *     current `value` reflected in the input.
 *   - No "Search" caption/label is rendered (pass 2 — placeholder text
 *     carries the affordance instead).
 *   - The placeholder reads exactly "Filter by name, description, assigned
 *     to..." (pass 2 wording).
 *   - Typing in the box invokes `onChange` with the raw next value (no
 *     internal debounce — the actual name/description/assigned-to matching
 *     lives in `SmartToDo.tsx`'s `displayItems` memo, exercised separately).
 *   - The component holds no filter-predicate state of its own — `value`/
 *     `onChange` are fully controlled by the parent (mirrors the old
 *     FilterPane's NFR-03 contract: toggling `isOpen` never mutates `value`).
 *
 * Test harness note: mirrors the retired `FilterPane.test.tsx`'s harness —
 * this package's `node_modules` has `@testing-library/jest-dom` but NOT
 * `@testing-library/react` (see `Header.test.tsx`'s header comment for the
 * full rationale). No `@spaarke/ui-components` import exists in
 * `SearchFilter.tsx` (unlike the retired FilterPane, which needed the shared
 * `TagFilter`), so no mocking is required here.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SearchFilter, type SearchFilterProps } from '../SearchFilter';

// React 19 requires this flag for `act()` to recognize the jsdom environment
// as a test environment — see Header.test.tsx for the same note.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

// ---------------------------------------------------------------------------
// Minimal render harness (no @testing-library/react available — see header).
// ---------------------------------------------------------------------------

function findByTestId(root: ParentNode, testid: string): HTMLElement {
  const el = root.querySelector(`[data-testid="${testid}"]`);
  if (!el) throw new Error(`No element with data-testid=${testid}`);
  return el as HTMLElement;
}

/** Resolves the native `<input>` under the SearchBox's `data-testid` wrapper. */
function findInputByTestId(root: ParentNode, testid: string): HTMLInputElement {
  const el = findByTestId(root, testid);
  if (el.tagName === 'INPUT') return el as HTMLInputElement;
  const nested = el.querySelector('input');
  if (!nested) throw new Error(`No <input> found under data-testid=${testid}`);
  return nested as HTMLInputElement;
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

function renderSearchFilter(overrides: Partial<SearchFilterProps> = {}): Harness {
  const onChange = jest.fn();

  const props: SearchFilterProps = {
    isOpen: true,
    value: '',
    onChange,
    ...overrides,
  };

  const container = document.createElement('div');
  document.body.appendChild(container);
  let root: Root;
  act(() => {
    root = createRoot(container);
    root.render(
      <FluentProvider theme={webLightTheme}>
        <SearchFilter {...props} />
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
});

describe('SearchFilter — visibility (Filter pill toggle, task 020 unchanged)', () => {
  it('render_IsOpenFalse_MarksAriaHiddenTrue', () => {
    const h = (activeHarness = renderSearchFilter({ isOpen: false }));
    expect(findByTestId(h.container, 'search-filter')).toHaveAttribute('aria-hidden', 'true');
  });

  it('render_IsOpenTrue_MarksAriaHiddenFalse', () => {
    const h = (activeHarness = renderSearchFilter({ isOpen: true }));
    expect(findByTestId(h.container, 'search-filter')).toHaveAttribute('aria-hidden', 'false');
  });
});

describe('SearchFilter — UAT pass 2 layout (2026-08-17): no label, revised placeholder', () => {
  it('render_NeverShowsASearchCaptionLabel', () => {
    const h = (activeHarness = renderSearchFilter());
    expect(h.container.textContent).not.toMatch(/^Search$/);
    expect(Array.from(h.container.querySelectorAll('*')).some((el) => el.textContent === 'Search')).toBe(false);
  });

  it('render_InputPlaceholder_MatchesExactUatWording', () => {
    const h = (activeHarness = renderSearchFilter());
    expect(findInputByTestId(h.container, 'search-filter-input').placeholder).toBe(
      'Filter by name, description, assigned to...',
    );
  });
});

describe('SearchFilter — controlled value', () => {
  it('render_WithValue_ReflectsItInTheInput', () => {
    const h = (activeHarness = renderSearchFilter({ value: 'Smith v Jones' }));
    expect(findInputByTestId(h.container, 'search-filter-input').value).toBe('Smith v Jones');
  });

  it('type_IntoTheBox_InvokesOnChangeWithTheRawNextValue', () => {
    const h = (activeHarness = renderSearchFilter());
    setInputValue(findInputByTestId(h.container, 'search-filter-input'), 'jordan');
    expect(h.onChange).toHaveBeenCalledWith('jordan');
  });

  it('type_EmptyString_InvokesOnChangeWithEmptyString', () => {
    const h = (activeHarness = renderSearchFilter({ value: 'jordan' }));
    setInputValue(findInputByTestId(h.container, 'search-filter-input'), '');
    expect(h.onChange).toHaveBeenCalledWith('');
  });
});

describe('SearchFilter — NFR-03 proxy (search text survives unrelated re-renders)', () => {
  it('toggle_IsOpen_NeverInvokesOnChange', () => {
    const onChange = jest.fn();
    const container = document.createElement('div');
    document.body.appendChild(container);
    let root: Root;
    act(() => {
      root = createRoot(container);
      root.render(
        <FluentProvider theme={webLightTheme}>
          <SearchFilter isOpen={true} value="Smith v Jones" onChange={onChange} />
        </FluentProvider>,
      );
    });

    // Simulate the bar closing then reopening (the only visibility toggle
    // this component's props expose) — `value` is NOT owned by this
    // component, so nothing here can mutate it.
    act(() => {
      root.render(
        <FluentProvider theme={webLightTheme}>
          <SearchFilter isOpen={false} value="Smith v Jones" onChange={onChange} />
        </FluentProvider>,
      );
    });
    act(() => {
      root.render(
        <FluentProvider theme={webLightTheme}>
          <SearchFilter isOpen={true} value="Smith v Jones" onChange={onChange} />
        </FluentProvider>,
      );
    });

    expect(onChange).not.toHaveBeenCalled();
    expect(findInputByTestId(container, 'search-filter-input').value).toBe('Smith v Jones');

    act(() => {
      root.unmount();
    });
    container.remove();
  });
});
