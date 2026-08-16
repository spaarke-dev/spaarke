/**
 * Header — unit tests (task 020, FR-05 / U-3 top-bar redesign, 2026-08-16).
 *
 * Covers the closed acceptance set from
 * `projects/smart-todo-r5/tasks/020-codepage-top-bar-redesign.poml`:
 *   - Default (no selection) cluster renders exactly Filter · + New Task · ⋮
 *     overflow, in that order, and NOTHING else inline (no QuickAdd, no
 *     inline SearchBox).
 *   - Filter pill toggles `isFilterPaneOpen` via `onToggleFilterPane`.
 *   - "+ New Task" invokes `onNewTask` when provided; is hidden when omitted
 *     (matches the existing optional-prop convention).
 *   - The ⋮ overflow menu contains exactly 3 items — Settings, Layout,
 *     Refresh — in that order, each invoking its pre-existing handler
 *     unchanged.
 *   - Layout menu item flips orientation horizontal ↔ vertical.
 *   - Selection-aware branch (selectedCount > 0) suppresses + New Task and
 *     the overflow menu, keeping only the Filter pill alongside
 *     `<SelectionAwareToolbar>` (pre-existing precedent — QuickAdd/+Wizard
 *     were likewise suppressed during selection).
 *
 * Test harness note: this package's node_modules (verified 2026-08-16, task
 * 020) has `@testing-library/jest-dom` but NOT `@testing-library/react` or
 * `@testing-library/user-event` — unlike most other Spaarke solutions. Per
 * task 020's concurrency constraint ("node_modules ... ALREADY installed —
 * do NOT run npm install"), and since 2 sibling agents are mid-edit in this
 * same worktree (install-race risk), this file uses a minimal manual harness
 * (`react-dom/client` + `act` from `react` + native DOM event dispatch —
 * React 19 dropped `ReactTestUtils.Simulate`) instead of RTL. Fluent's
 * `<Menu>` popover renders via a React Portal to `document.body`, so
 * overflow-menu queries search `document.body`, not the local container.
 *
 * `@spaarke/ui-components` is mocked below: this worktree's
 * `Spaarke.SdapClient/dist` was never built (no local `node_modules`, `tsc`
 * unavailable without `npm install`), and `@spaarke/ui-components`'s barrel
 * (`services/index.js` → `EntityCreationService.js`) unconditionally
 * requires `@spaarke/sdap-client` — a pre-existing, task-020-unrelated
 * environment gap that would block ANY test importing that barrel (this is
 * the first component test ever written for this solution's `src/`, so
 * nobody had hit it yet). Mocking keeps this test hermetic to Header's own
 * logic regardless. Deviation documented in
 * `projects/smart-todo-r5/notes/task-020-topbar.md`.
 */

jest.mock('@spaarke/ui-components', () => {
  const ReactActual = require('react') as typeof import('react');
  return {
    MicrosoftToDoIcon: () => ReactActual.createElement('span', { 'data-testid': 'todo-icon' }),
    SelectionAwareToolbar: (props: { selectedCount: number }) =>
      ReactActual.createElement(
        'div',
        { 'data-testid': 'selection-toolbar' },
        `${props.selectedCount} selected`,
      ),
  };
});

import '@testing-library/jest-dom';
import * as React from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { Header, type HeaderProps } from '../Header';

// React 19 requires this flag for `act()` to recognize the jsdom environment
// as a test environment (jest.setup.cjs — outside this task's scope — does
// not set it; scoping it locally here is sufficient and non-invasive).
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

function fireKeyDown(el: Element, key: string): void {
  act(() => {
    el.dispatchEvent(
      new KeyboardEvent('keydown', { key, code: key, bubbles: true, cancelable: true }),
    );
  });
}

function findButtonByText(root: ParentNode, text: RegExp): HTMLButtonElement {
  const buttons = Array.from(root.querySelectorAll('button'));
  const match = buttons.find((b) => text.test(b.textContent ?? ''));
  if (!match) {
    throw new Error(`No <button> found matching ${text} among ${buttons.length} buttons`);
  }
  return match;
}

function queryButtonByText(root: ParentNode, text: RegExp): HTMLButtonElement | null {
  const buttons = Array.from(root.querySelectorAll('button'));
  return buttons.find((b) => text.test(b.textContent ?? '')) ?? null;
}

function findButtonByAriaLabel(root: ParentNode, label: RegExp): HTMLButtonElement {
  const buttons = Array.from(root.querySelectorAll('button'));
  const match = buttons.find((b) => label.test(b.getAttribute('aria-label') ?? ''));
  if (!match) {
    throw new Error(`No <button aria-label> found matching ${label}`);
  }
  return match;
}

function queryButtonByAriaLabel(root: ParentNode, label: RegExp): HTMLButtonElement | null {
  const buttons = Array.from(root.querySelectorAll('button'));
  return buttons.find((b) => label.test(b.getAttribute('aria-label') ?? '')) ?? null;
}

interface Harness {
  container: HTMLDivElement;
  unmount: () => void;
  onToggleFilterPane: jest.Mock;
  onNewTask: jest.Mock;
  onOpenSettings: jest.Mock;
  onOrientationChange: jest.Mock;
  onRefresh: jest.Mock;
}

function renderHeader(overrides: Partial<HeaderProps> = {}): Harness {
  const onToggleFilterPane = jest.fn();
  const onNewTask = jest.fn();
  const onOpenSettings = jest.fn();
  const onOrientationChange = jest.fn();
  const onRefresh = jest.fn();

  const props: HeaderProps = {
    selectedCount: 0,
    isFilterPaneOpen: false,
    onToggleFilterPane,
    onNewTask,
    onOpenSettings,
    onOrientationChange,
    onRefresh,
    orientation: 'horizontal',
    ...overrides,
  };

  const container = document.createElement('div');
  document.body.appendChild(container);
  let root: Root;
  act(() => {
    root = createRoot(container);
    root.render(
      <FluentProvider theme={webLightTheme}>
        <Header {...props} />
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
    onToggleFilterPane,
    onNewTask,
    onOpenSettings,
    onOrientationChange,
    onRefresh,
  };
}

/** Opens the "⋮" overflow menu and returns its (portal-rendered, document.body) root. */
function openOverflowMenu(h: Harness): HTMLElement {
  const trigger = findButtonByAriaLabel(h.container, /more options/i);
  fireClick(trigger);
  const menu = document.body.querySelector('[role="menu"]');
  if (!menu) throw new Error('Overflow menu did not open');
  return menu as HTMLElement;
}

let activeHarness: Harness | undefined;

afterEach(() => {
  activeHarness?.unmount();
  activeHarness = undefined;
});

describe('Header — mockup layout parity (FR-05)', () => {
  it('render_DefaultNoSelection_ShowsFilterNewTaskOverflowInOrderOnly', () => {
    const h = (activeHarness = renderHeader());

    // Title cluster.
    expect(h.container.textContent).toContain('Smart To Do');

    // Right cluster: Filter, + New Task, ⋮ — nothing else inline.
    const filterButton = findButtonByText(h.container, /^Filter$/);
    const newTaskButton = findButtonByText(h.container, /new task/i);
    const overflowTrigger = findButtonByAriaLabel(h.container, /more options/i);

    expect(filterButton).toBeInTheDocument();
    expect(newTaskButton).toBeInTheDocument();
    expect(overflowTrigger).toBeInTheDocument();

    // No QuickAdd remnants, no inline SearchBox (the removed toggle pattern).
    expect(h.container.querySelector('input')).toBeNull();
    expect(h.container.querySelector('[role="searchbox"]')).toBeNull();

    // DOM order matches mockup order: Filter → + New Task → ⋮.
    const allButtons = Array.from(h.container.querySelectorAll('button'));
    expect(allButtons.indexOf(filterButton)).toBeLessThan(allButtons.indexOf(newTaskButton));
    expect(allButtons.indexOf(newTaskButton)).toBeLessThan(allButtons.indexOf(overflowTrigger));
  });

  it('click_FilterPill_TogglesIsFilterPaneOpenViaCallback', () => {
    const h = (activeHarness = renderHeader({ isFilterPaneOpen: false }));

    fireClick(findButtonByText(h.container, /^Filter$/));

    expect(h.onToggleFilterPane).toHaveBeenCalledTimes(1);
  });

  it('render_FilterPillAriaExpanded_ReflectsIsFilterPaneOpen', () => {
    const h = (activeHarness = renderHeader({ isFilterPaneOpen: true }));
    expect(findButtonByText(h.container, /^Filter$/)).toHaveAttribute('aria-expanded', 'true');
  });

  it('click_NewTaskButton_InvokesOnNewTaskStub', () => {
    const h = (activeHarness = renderHeader());

    fireClick(findButtonByText(h.container, /new task/i));

    expect(h.onNewTask).toHaveBeenCalledTimes(1);
  });

  it('render_OnNewTaskOmitted_HidesNewTaskButtonWithoutThrowing', () => {
    const h = (activeHarness = renderHeader({ onNewTask: undefined }));
    expect(queryButtonByText(h.container, /new task/i)).toBeNull();
  });
});

describe('Header — overflow menu (Settings, Layout, Refresh) — FR-05 / U-3', () => {
  it('open_OverflowMenu_ContainsExactlyThreeItemsInOrder', () => {
    const h = (activeHarness = renderHeader());

    const menu = openOverflowMenu(h);
    const items = Array.from(menu.querySelectorAll('[role="menuitem"]'));

    expect(items).toHaveLength(3);
    expect(items[0].textContent).toContain('Settings');
    expect(items[1].textContent).toContain('Layout');
    expect(items[2].textContent).toContain('Refresh');
  });

  it('click_SettingsMenuItem_InvokesOnOpenSettingsUnchanged', () => {
    const h = (activeHarness = renderHeader());
    const menu = openOverflowMenu(h);
    const settingsItem = Array.from(menu.querySelectorAll('[role="menuitem"]')).find((el) =>
      /settings/i.test(el.textContent ?? ''),
    )!;

    fireClick(settingsItem);

    expect(h.onOpenSettings).toHaveBeenCalledTimes(1);
  });

  it('click_LayoutMenuItem_FlipsOrientationHorizontalToVertical', () => {
    const h = (activeHarness = renderHeader({ orientation: 'horizontal' }));
    const menu = openOverflowMenu(h);
    const layoutItem = Array.from(menu.querySelectorAll('[role="menuitem"]')).find((el) =>
      /layout/i.test(el.textContent ?? ''),
    )!;

    fireClick(layoutItem);

    expect(h.onOrientationChange).toHaveBeenCalledWith('vertical');
  });

  it('click_LayoutMenuItem_FlipsOrientationVerticalToHorizontal', () => {
    const h = (activeHarness = renderHeader({ orientation: 'vertical' }));
    const menu = openOverflowMenu(h);
    const layoutItem = Array.from(menu.querySelectorAll('[role="menuitem"]')).find((el) =>
      /layout/i.test(el.textContent ?? ''),
    )!;

    fireClick(layoutItem);

    expect(h.onOrientationChange).toHaveBeenCalledWith('horizontal');
  });

  it('click_RefreshMenuItem_InvokesOnRefreshUnchanged', () => {
    const h = (activeHarness = renderHeader());
    const menu = openOverflowMenu(h);
    const refreshItem = Array.from(menu.querySelectorAll('[role="menuitem"]')).find((el) =>
      /refresh/i.test(el.textContent ?? ''),
    )!;

    fireClick(refreshItem);

    expect(h.onRefresh).toHaveBeenCalledTimes(1);
  });

  it('render_OrientationOrHandlerOmitted_HidesLayoutMenuItem', () => {
    const h = (activeHarness = renderHeader({ orientation: undefined, onOrientationChange: undefined }));
    const menu = openOverflowMenu(h);
    const items = Array.from(menu.querySelectorAll('[role="menuitem"]'));

    expect(items.some((el) => /layout/i.test(el.textContent ?? ''))).toBe(false);
    expect(items).toHaveLength(2); // Settings + Refresh remain.
  });

  it('keyboard_EscapeClosesMenu', () => {
    const h = (activeHarness = renderHeader());
    openOverflowMenu(h);

    fireKeyDown(document.body.querySelector('[role="menu"]') as HTMLElement, 'Escape');

    expect(document.body.querySelector('[role="menu"]')).toBeNull();
  });
});

describe('Header — selection-aware branch (selectedCount > 0)', () => {
  it('render_WithSelection_HidesNewTaskAndOverflowKeepsFilter', () => {
    const h = (activeHarness = renderHeader({ selectedCount: 2 }));

    expect(queryButtonByText(h.container, /^Filter$/)).not.toBeNull();
    expect(queryButtonByText(h.container, /new task/i)).toBeNull();
    expect(queryButtonByAriaLabel(h.container, /more options/i)).toBeNull();
  });
});
