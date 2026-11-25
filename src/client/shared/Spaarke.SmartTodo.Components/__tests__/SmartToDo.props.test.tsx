/**
 * SmartToDo — the optional props added when the SmartTodo code page's fork was folded into the shared board
 * (#2907). Each test fails if its prop stops working, and the "default" tests pin LegalWorkspace's behaviour
 * (record cards, own header, Future collapsed, no column-order persistence).
 *
 * `@spaarke/ui-components` is mocked at the module boundary (as in Header.test.tsx): its barrel needs dist packages
 * this worktree does not build. The `KanbanBoard` stand-in renders each column's cards through the board's real
 * `renderCard`, hides collapsed columns' cards, reports the orientation, and exposes a same-column reorder so the
 * column-order persistence path can be driven without a drag library.
 */

jest.mock('@spaarke/ui-components', () => {
  const ReactActual = require('react') as typeof import('react');
  interface MockColumn {
    id: string;
    title: string;
    items: Array<{ sprk_todoid: string }>;
  }
  interface MockBoardProps {
    columns: MockColumn[];
    renderCard: (item: unknown, index: number, columnId: string) => React.ReactNode;
    onDragEnd: (result: unknown) => void;
    collapsedColumns?: ReadonlySet<string>;
    orientation?: string;
  }
  return {
    // The real shared date primitives (column bucketing, due labels) — the whole module, so a newly used export
    // cannot silently arrive as undefined.
    ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
    KanbanBoard: (props: MockBoardProps) =>
      ReactActual.createElement(
        'div',
        { 'data-testid': 'board', 'data-orientation': props.orientation ?? 'default' },
        props.columns.map(col =>
          ReactActual.createElement(
            'section',
            {
              key: col.id,
              'data-testid': `column-${col.id}`,
              'data-collapsed': String(props.collapsedColumns?.has(col.id) ?? false),
            },
            props.collapsedColumns?.has(col.id)
              ? null
              : col.items.map((item, i) =>
                  ReactActual.createElement(
                    'div',
                    { key: item.sprk_todoid, 'data-testid': `card-${item.sprk_todoid}` },
                    props.renderCard(item, i, col.id)
                  )
                ),
            ReactActual.createElement(
              'button',
              {
                'data-testid': `reorder-${col.id}`,
                onClick: () =>
                  props.onDragEnd({
                    draggableId: col.items[0]?.sprk_todoid,
                    source: { droppableId: col.id, index: 0 },
                    destination: { droppableId: col.id, index: 1 },
                  }),
              },
              'reorder'
            )
          )
        )
      ),
    EmptyState: () => ReactActual.createElement('div', { 'data-testid': 'empty' }),
    OrientationToggle: (props: { orientation: string; onChange: (next: string) => void }) =>
      ReactActual.createElement(
        'button',
        {
          'data-testid': 'orientation-toggle',
          onClick: () => props.onChange(props.orientation === 'horizontal' ? 'vertical' : 'horizontal'),
        },
        props.orientation
      ),
    NULL_DEFAULT_PRIORITY_SCORE: 50,
    NULL_DEFAULT_EFFORT_SCORE: 50,
    RecordCardShell: (props: { title?: React.ReactNode; onClick?: () => void }) =>
      ReactActual.createElement('div', { 'data-testid': 'record-card', onClick: props.onClick }, props.title),
    CardIcon: () => null,
    MicrosoftToDoIcon: () => ReactActual.createElement('span', { 'data-testid': 'todo-icon' }),
  };
});

import '@testing-library/jest-dom';
import * as React from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SmartToDo, type ISmartToDoProps } from '../src/components/SmartToDo';
import type { ITodo, ITodoKanbanPreferences } from '../src/types/entities';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// Undated → every item lands in the Future column.
const todo = (id: string, name: string, extra: Partial<ITodo> = {}): ITodo =>
  ({
    sprk_todoid: id,
    sprk_name: name,
    statecode: 0,
    statuscode: 1,
    sprk_priorityscore: 50,
    sprk_effortscore: 50,
    createdon: '2026-11-01T00:00:00Z',
    modifiedon: '2026-11-01T00:00:00Z',
    ...extra,
  }) as ITodo;

const ITEMS: ITodo[] = [
  todo('a', 'Review the merger draft'),
  todo('b', 'Call opposing counsel', { sprk_regardingrecordname: 'Acme v. Widget' }),
];

const PREFS: ITodoKanbanPreferences = { todayThreshold: 60, tomorrowThreshold: 30 };

function baseProps(over: Partial<ISmartToDoProps> = {}): ISmartToDoProps {
  return {
    items: ITEMS,
    isLoading: false,
    error: null,
    onRefetch: jest.fn(),
    preferences: PREFS,
    onUpdatePreferences: jest.fn(),
    onCreateTodo: jest.fn(async () => ({ success: true as const })),
    onDismissTodo: jest.fn(async () => ({ success: true as const })),
    onRestoreTodo: jest.fn(async () => ({ success: true as const })),
    ...over,
  };
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

function render(props: ISmartToDoProps): void {
  act(() => {
    root.render(
      <FluentProvider theme={webLightTheme}>
        <SmartToDo {...props} />
      </FluentProvider>
    );
  });
}

const q = (sel: string): HTMLElement | null => container.querySelector(sel);
const byLabel = (label: string): HTMLElement | null => container.querySelector(`[aria-label='${label}']`);
/** The compact card's root element inside the board stand-in's wrapper. */
const compactCard = (id: string): HTMLElement => q(`[data-testid="card-${id}"]`)!.firstElementChild as HTMLElement;
const cardIdsIn = (columnId: string): Array<string | null> =>
  Array.from(container.querySelectorAll(`[data-testid="column-${columnId}"] [data-testid^="card-"]`)).map(el =>
    el.getAttribute('data-testid')
  );

// Every column expanded, so cards render.
const EXPANDED: Partial<ISmartToDoProps> = { defaultCollapsedColumns: [] };

describe('SmartToDo defaults (LegalWorkspace behaviour unchanged)', () => {
  it('renders its own header, record cards, and collapses Future', () => {
    render(baseProps());
    expect(container.textContent).toContain('Smart To Do');
    expect(byLabel('Threshold settings')).not.toBeNull();
    expect(q('[data-testid="column-Future"]')).toHaveAttribute('data-collapsed', 'true');
  });

  it('uses record cards (no selection checkbox) when cardAppearance is omitted', () => {
    render(baseProps(EXPANDED));
    expect(container.querySelectorAll('[data-testid="record-card"]').length).toBe(2);
    expect(byLabel('Select "Review the merger draft"')).toBeNull();
  });

  it('does not persist column order unless persistColumnOrder is set', async () => {
    const onUpdatePreferences = jest.fn();
    render(baseProps({ ...EXPANDED, onUpdatePreferences }));
    await act(async () => {
      q('[data-testid="reorder-Future"]')!.click();
      await Promise.resolve();
    });
    expect(onUpdatePreferences).not.toHaveBeenCalledWith(expect.objectContaining({ columnOrders: expect.anything() }));
  });
});

describe('SmartToDo optional props', () => {
  it('searchQuery filters the cards by title / regarding record', () => {
    render(baseProps({ ...EXPANDED, searchQuery: 'acme' }));
    expect(q('[data-testid="card-b"]')).not.toBeNull();
    expect(q('[data-testid="card-a"]')).toBeNull();
  });

  it('a blank searchQuery shows every card', () => {
    render(baseProps({ ...EXPANDED, searchQuery: '   ' }));
    expect(q('[data-testid="card-a"]')).not.toBeNull();
    expect(q('[data-testid="card-b"]')).not.toBeNull();
  });

  it('defaultCollapsedColumns=[] starts every column expanded', () => {
    render(baseProps(EXPANDED));
    expect(q('[data-testid="column-Future"]')).toHaveAttribute('data-collapsed', 'false');
  });

  it('cardAppearance="compact" renders the compact card instead of the record card', () => {
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact' }));
    expect(container.querySelectorAll('[data-testid="record-card"]').length).toBe(0);
    // The compact card's score circle (the record card mock has none).
    expect(q('[data-testid="card-a"] [title^="To Do Score:"]')).not.toBeNull();
    // No checkbox until the host plumbs onToggleSelect.
    expect(byLabel('Select "Review the merger draft"')).toBeNull();
  });

  it('selectedIds marks a card selected and onToggleSelect receives its id', () => {
    const onToggleSelect = jest.fn();
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact', selectedIds: new Set(['a']), onToggleSelect }));
    expect(byLabel('Deselect "Review the merger draft"')).not.toBeNull();
    act(() => byLabel('Select "Call opposing counsel"')!.click());
    expect(onToggleSelect).toHaveBeenCalledWith('b');
  });

  it('onOpenTodo fires from the compact card Open icon', () => {
    const onOpenTodo = jest.fn();
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact', onOpenTodo }));
    act(() => byLabel('Open "Call opposing counsel" in modal')!.click());
    expect(onOpenTodo).toHaveBeenCalledWith('b');
  });

  it('onSelectTodo fires on a compact card click', () => {
    const onSelectTodo = jest.fn();
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact', onSelectTodo }));
    act(() => compactCard('b').click());
    expect(onSelectTodo).toHaveBeenCalledWith('b');
  });

  it('selectedTodoId highlights only that compact card', () => {
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact', onSelectTodo: jest.fn(), selectedTodoId: 'a' }));
    expect(compactCard('a')).toHaveAttribute('aria-selected', 'true');
    expect(compactCard('b')).toHaveAttribute('aria-selected', 'false');
  });

  it('onOpenTodo fires on a compact card double-click', () => {
    const onOpenTodo = jest.fn();
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact', onOpenTodo }));
    act(() => {
      compactCard('a').dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    });
    expect(onOpenTodo).toHaveBeenCalledWith('a');
  });

  it('disableSidePane makes compact cards inert (no select, no open)', () => {
    const onSelectTodo = jest.fn();
    const onOpenTodo = jest.fn();
    render(baseProps({ ...EXPANDED, cardAppearance: 'compact', disableSidePane: true, onSelectTodo, onOpenTodo }));
    act(() => compactCard('a').click());
    act(() => {
      compactCard('a').dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    });
    expect(onSelectTodo).not.toHaveBeenCalled();
    expect(onOpenTodo).not.toHaveBeenCalled();
    expect(byLabel('Open "Review the merger draft" in modal')).toBeNull();
  });

  it('record cards open the todo on click (LegalWorkspace)', () => {
    const onOpenTodo = jest.fn();
    render(baseProps({ ...EXPANDED, onOpenTodo }));
    act(() => (q('[data-testid="card-b"] [data-testid="record-card"]') as HTMLElement).click());
    expect(onOpenTodo).toHaveBeenCalledWith('b');
  });

  it('hideHeader removes the board header but keeps the settings popover reachable', () => {
    let openSettings: (() => void) | undefined;
    render(baseProps({ hideHeader: true, onSettingsOpenerReady: open => (openSettings = open) }));
    expect(byLabel('Threshold settings')).toBeNull();
    expect(openSettings).toBeInstanceOf(Function);
    act(() => openSettings!());
    expect(document.body.querySelector("[aria-label='Today threshold score']")).not.toBeNull();
  });

  it('orientation is passed to the board', () => {
    render(baseProps({ orientation: 'vertical' }));
    expect(q('[data-testid="board"]')).toHaveAttribute('data-orientation', 'vertical');
  });

  it('persistColumnOrder saves a same-column reorder as columnOrders', async () => {
    const onUpdatePreferences = jest.fn();
    render(baseProps({ ...EXPANDED, persistColumnOrder: true, onUpdatePreferences }));
    await act(async () => {
      q('[data-testid="reorder-Future"]')!.click();
      await Promise.resolve();
    });
    expect(onUpdatePreferences).toHaveBeenCalledWith({ columnOrders: { Future: ['b', 'a'] } });
  });

  it('persistColumnOrder seeds the column order from preferences.columnOrders', () => {
    render(
      baseProps({
        ...EXPANDED,
        persistColumnOrder: true,
        preferences: { ...PREFS, columnOrders: { Future: ['b', 'a'] } },
      })
    );
    expect(cardIdsIn('Future')).toEqual(['card-b', 'card-a']);
  });

  it('without persistColumnOrder a stored column order is ignored (LegalWorkspace keeps the score order)', () => {
    render(baseProps({ ...EXPANDED, preferences: { ...PREFS, columnOrders: { Future: ['b', 'a'] } } }));
    expect(cardIdsIn('Future')).toEqual(['card-a', 'card-b']);
  });

  // A class-name check only: Griffel styles are not computed in jsdom, so the 100% height itself is a DEV hand check.
  it('fillContainer changes the root layout class', () => {
    render(baseProps());
    const plain = (q('[role="region"]') as HTMLElement).className;
    render(baseProps({ fillContainer: true }));
    const filled = (q('[role="region"]') as HTMLElement).className;
    expect(filled).not.toBe(plain);
  });
});
