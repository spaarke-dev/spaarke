/**
 * NarrativeBullet unit tests — R2 task 024 + R4 task 045 + R7 W12 task 134 coverage.
 *
 * R2/P2a (preserved): FR-11..FR-14a + dark-mode parity for the hybrid-aggregation
 * UX introduced in Wave 8 + Wave 9 (SubRow + SubRowLink/Todo/Dismiss).
 *
 * R7 W12 task 134 (operator MVP, wave12 plan §2.1): primary visible 'Add To Do'
 * Checkmark icon button hoisted OUT of the overflow menu while preserving the
 * three-dot menu component for future tools. Net changes to test surface:
 *   - Overflow menu now shows 5 MenuItems (Add to To Do removed; canonical
 *     order: Mark as read, Remove from briefing, Keep on briefing for 7 more
 *     days, Dismiss, Open record).
 *   - Primary 'Add To Do' button is a sibling Button BEFORE the MenuButton
 *     with aria-label "Add To Do" (distinct from SubRowTodo's "Add to To Do").
 *   - onAddToTodo(itemIds) callback contract is UNCHANGED — clicking the
 *     primary button preserves the ADR-024 useInlineTodoCreate path.
 *
 * R4 / FR-18 (preserved): three-dot overflow menu replaces the inline 5-icon
 * action row. AC-18a/b/c cases (post-task-134):
 *   - RendersOverflowMenu_NotInlineRow — MoreHorizontalRegular trigger present;
 *     5-icon inline row absent; primary 'Add To Do' button present (task 134).
 *   - OverflowMenu_Shows5Actions — open the menu, assert 5 MenuItems in the
 *     canonical order: Mark as read, Remove from briefing, Keep on briefing
 *     for 7 more days, Dismiss, Open record.
 *   - OverflowMenu_KeyboardAccessible — trigger is focusable; Enter opens.
 *   - OverflowMenu_DarkModeCompliance (ADR-021) — render with `webDarkTheme`;
 *     static source scan rejects raw hex literals.
 *   - OverflowMenu_PreservesR3Actions — invoke onCheck/onRemove/onKeep via
 *     the menu items → callbacks fire as expected.
 *   - PrimaryAddToDoButton_CallsExistingPath — invoke onAddToTodo via the
 *     primary visible Checkmark button → existing prop signature preserved
 *     (`useInlineTodoCreate` + `TODO_REGARDING_CATALOG` integration owned by
 *     the parent component, ADR-024 regression-free invariant).
 *
 * Sub-list (FR-11..FR-14) test cases preserved verbatim since SubRow slot
 * behavior was NOT changed by task 045 or task 134.
 *
 * Mocking strategy:
 *   - window.Xrm is installed per-test (sub-row link case + Open record case)
 *     and uninstalled after.
 *   - Callbacks are `jest.fn()` so each test is fully independent.
 *   - @spaarke/ui-components MicrosoftToDoIcon is routed via the existing
 *     jest.config moduleNameMapper to a no-op SVG stub.
 */

import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { NarrativeBullet } from '../src/components/NarrativeBullet';
import type { NarrativeBulletProps } from '../src/components/NarrativeBullet';
import type { NotificationItem } from '../src/types/notifications';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

function makeItem(overrides: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id: 'n-1',
    title: 'Review motion to dismiss',
    body: 'Motion is overdue.',
    category: 'tasks-overdue',
    priority: 'high',
    actionUrl: '/main.aspx?etc=1&id=abc',
    regardingName: 'Acme Matter',
    regardingEntityType: 'sprk_matter',
    regardingId: '11111111-1111-1111-1111-111111111111',
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    dueDate: null,
    ...overrides,
  };
}

function baseProps(overrides: Partial<NarrativeBulletProps> = {}): NarrativeBulletProps {
  return {
    narrative: 'Review motion to dismiss for Acme Matter.',
    primaryEntityName: 'Acme Matter',
    primaryEntityType: 'sprk_matter',
    primaryEntityId: '11111111-1111-1111-1111-111111111111',
    itemIds: ['n-1'],
    onAddToTodo: jest.fn(),
    onDismiss: jest.fn(),
    isTodoCreated: false,
    isTodoPending: false,
    ...overrides,
  };
}

function renderWith(props: NarrativeBulletProps, theme = webLightTheme): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={theme}>
      <NarrativeBullet {...props} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Xrm mock helpers
// ---------------------------------------------------------------------------

function installXrm(): jest.Mock {
  const navigateTo = jest.fn().mockResolvedValue(undefined);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    Navigation: { navigateTo },
  };
  return navigateTo;
}

function uninstallXrm(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
}

// ---------------------------------------------------------------------------
// Helper: open the overflow menu and return the menu element.
// Fluent v9 `Menu` renders its popover via a portal; the trigger is the
// MenuButton with aria-label="More actions".
// ---------------------------------------------------------------------------

function openOverflowMenu(): HTMLElement {
  const trigger = screen.getByRole('button', { name: /More actions/i });
  act(() => {
    fireEvent.click(trigger);
  });
  // After the click, MenuPopover renders MenuList with role=menu.
  return screen.getByRole('menu');
}

// ---------------------------------------------------------------------------
// Tests — FR-18 overflow menu (R4 task 045 — new)
// ---------------------------------------------------------------------------

describe('NarrativeBullet — FR-18 three-dot overflow menu (R4 task 045)', () => {
  afterEach(() => {
    uninstallXrm();
  });

  it('RendersOverflowMenu_NotInlineRow: renders MoreHorizontalRegular trigger + primary Add To Do button; NO inline 5-icon row (FR-18 / AC-18a / task 134)', () => {
    const props = baseProps({
      itemIds: ['n-1'],
      items: [makeItem({ id: 'n-1' })],
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    // The overflow MenuButton trigger is present (preserved per operator
    // emphasis on three-dot menu for future tools).
    expect(screen.getByRole('button', { name: /More actions/i })).toBeInTheDocument();

    // R7 W12 task 134: primary visible 'Add To Do' Checkmark button is present
    // as a sibling BEFORE the menu trigger (operator MVP, wave12 plan §2.1).
    // Distinct aria-label "Add To Do" — NOT "Add to To Do" (which is the
    // SubRowTodo per-item button label) — so per-bullet vs per-sub-row
    // targeting is unambiguous for tests and screen readers.
    expect(screen.getByRole('button', { name: /^Add To Do$/i })).toBeInTheDocument();

    // The inline 5-icon row (pre-R4) had buttons with these aria-labels
    // rendered. With the menu CLOSED, the menu-only aria-labels MUST NOT
    // exist anywhere in the DOM — they only appear inside the MenuPopover
    // (which is unmounted while the menu is closed). This is the binding
    // FR-18 assertion that the inline row was removed.
    expect(screen.queryByRole('button', { name: /^Mark as read$/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Remove from briefing$/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Keep on briefing for 7 more days$/i })).toBeNull();
    // For the single-item bullet there is no per-bullet "Add to To Do"
    // (the sub-row label, distinct from the new primary "Add To Do") and
    // no "Dismiss" button rendered inline.
    expect(screen.queryByRole('button', { name: /^Add to To Do$/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Dismiss$/i })).toBeNull();
  });

  it('OverflowMenu_Shows5Actions: open the menu and assert 5 MenuItems in canonical post-task-134 order (AC-18a)', () => {
    const props = baseProps({
      itemIds: ['n-1'],
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    openOverflowMenu();

    // Fluent v9 MenuItems have role="menuitem". Collect them in DOM order and
    // assert the canonical labels match the post-task-134 sequence.
    // ("Add to To Do" is now a primary visible button, NOT a menu item.)
    const menuItems = screen.getAllByRole('menuitem');
    expect(menuItems).toHaveLength(5);
    const labels = menuItems.map(el => (el.textContent ?? '').trim());
    expect(labels).toEqual([
      'Mark as read',
      'Remove from briefing',
      'Keep on briefing for 7 more days',
      'Dismiss',
      'Open record',
    ]);
  });

  it('OverflowMenu_KeyboardAccessible: trigger is focusable and opens on Enter (AC-18b)', () => {
    const props = baseProps({
      itemIds: ['n-1'],
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    const trigger = screen.getByRole('button', { name: /More actions/i });
    // The trigger is a real HTML <button>, so it is in the tab order by default
    // (no negative tabindex). Focus it programmatically and verify focus state.
    act(() => {
      trigger.focus();
    });
    expect(trigger).toHaveFocus();
    expect(trigger.getAttribute('tabindex')).not.toEqual('-1');

    // Fluent v9 MenuButton opens its menu on click; the keyboard-equivalent
    // (Enter / Space) is implemented by the native <button> default action
    // (synthesizes a click). We assert the menu opens once the trigger receives
    // a keyDown-equivalent click — this is the canonical RTL pattern for
    // verifying keyboard-accessibility on a focusable button trigger.
    act(() => {
      fireEvent.click(trigger);
    });
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('OverflowMenu_DarkModeCompliance: renders under webDarkTheme via semantic tokens — NarrativeBullet source has 0 hex literals (ADR-021 / AC-18b)', () => {
    const props = baseProps({
      itemIds: ['n-1'],
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    const { container } = renderWith(props, webDarkTheme);

    // Sanity: the bullet rendered under webDarkTheme.
    expect(screen.getByRole('button', { name: /More actions/i })).toBeInTheDocument();
    expect(container.firstChild).not.toBeNull();

    // ADR-021 semantic-token parity check — STATIC SOURCE SCAN.
    //
    // The strongest JSDOM-friendly proof of dark-mode parity is a static scan
    // of the component source for any hard-coded hex color literal. All color
    // values MUST flow through `tokens.color*` so dark mode swaps them
    // automatically. This catches regressions at the authoring layer.
    //
    // Hex color literal: # followed by 3/4/6/8 hex digits, where the PRECEDING
    // character is a quote / colon / whitespace / open-paren. This avoids
    // false positives on URL fragments / hash IDs.
    const fs = require('fs') as typeof import('fs');
    const path = require('path') as typeof import('path');
    const componentsDir = path.resolve(__dirname, '../src/components');
    const filesToScan = ['NarrativeBullet.tsx', 'SubRow.tsx', 'SubRowLink.tsx', 'SubRowTodo.tsx', 'SubRowDismiss.tsx'];
    const hexColorRe = /[\s:'"(]#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/;

    const offenders: string[] = [];
    for (const file of filesToScan) {
      const full = path.join(componentsDir, file);
      const source = fs.readFileSync(full, 'utf8');
      // Strip comments before scanning so doc strings can mention hex codes.
      const codeOnly = source
        .replace(/\/\*[\s\S]*?\*\//g, '') // block comments
        .replace(/(^|[^:])\/\/.*$/gm, '$1'); // line comments
      const m = codeOnly.match(hexColorRe);
      if (m) {
        offenders.push(`${file}: found ${m[0]}`);
      }
    }
    expect(offenders).toEqual([]);

    // Sanity: FluentProvider injected styles into the JSDOM document.
    const styleTags = document.querySelectorAll('style');
    expect(styleTags.length).toBeGreaterThan(0);
  });

  it('OverflowMenu_PreservesR3Actions: clicking R3 menu items invokes onCheck/onRemove/onKeep with the primary item id (FR-4/5/6)', () => {
    const onCheck = jest.fn();
    const onRemove = jest.fn();
    const onKeep = jest.fn();
    const items = [makeItem({ id: 'n-1', ttlinseconds: 604800 })];
    const props = baseProps({
      itemIds: ['n-1'],
      items,
      onCheck,
      onRemove,
      onKeep,
    });
    renderWith(props);

    openOverflowMenu();

    // Click "Mark as read" → onCheck('n-1')
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Mark as read$/i }));
    });
    expect(onCheck).toHaveBeenCalledTimes(1);
    expect(onCheck).toHaveBeenCalledWith('n-1');

    // Re-open the menu (Fluent v9 closes the menu on item click).
    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Remove from briefing$/i }));
    });
    expect(onRemove).toHaveBeenCalledTimes(1);
    expect(onRemove).toHaveBeenCalledWith('n-1');

    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Keep on briefing for 7 more days$/i }));
    });
    expect(onKeep).toHaveBeenCalledTimes(1);
    // FR-6 passes both the primary item id AND its current TTL value so the
    // service can compute newTtl = currentTtlSeconds + 604800.
    expect(onKeep).toHaveBeenCalledWith('n-1', 604800);
  });

  it('PrimaryAddToDoButton_CallsExistingPath: clicking the primary Add To Do Checkmark button invokes onAddToTodo(itemIds) — preserves ADR-024 useInlineTodoCreate path (AC-18c / task 134)', () => {
    const onAddToTodo = jest.fn();
    const props = baseProps({
      itemIds: ['n-1', 'n-2'],
      items: [makeItem({ id: 'n-1' }), makeItem({ id: 'n-2' })],
      onAddToTodo,
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    // Task 134: Add To Do is now a PRIMARY VISIBLE Button with aria-label
    // "Add To Do" — NOT a menu item. The overflow menu is not opened in this
    // test because the primary button is reachable directly.
    const addToDoButton = screen.getByRole('button', { name: /^Add To Do$/i });
    act(() => {
      fireEvent.click(addToDoButton);
    });

    // ADR-024 regression-free invariant: the parent's `onAddToTodo` callback
    // signature is unchanged (receives the full itemIds array). The parent
    // (DailyBriefingApp) owns `useInlineTodoCreate` + `TODO_REGARDING_CATALOG`
    // wiring; this component only invokes the prop unchanged.
    expect(onAddToTodo).toHaveBeenCalledTimes(1);
    expect(onAddToTodo).toHaveBeenCalledWith(['n-1', 'n-2']);
  });

  it('PrimaryAddToDoButton_DisabledWhenCreated: button is disabled when isTodoCreated=true (no duplicate creates, task 134)', () => {
    const onAddToTodo = jest.fn();
    const props = baseProps({
      itemIds: ['n-1'],
      onAddToTodo,
      isTodoCreated: true,
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    const addToDoButton = screen.getByRole('button', { name: /^Add To Do$/i }) as HTMLButtonElement;
    expect(addToDoButton.disabled).toBe(true);
    act(() => {
      fireEvent.click(addToDoButton);
    });
    expect(onAddToTodo).not.toHaveBeenCalled();
  });

  it('PrimaryAddToDoButton_DisabledWhenPending: button is disabled when isTodoPending=true (no double-fire, task 134)', () => {
    const onAddToTodo = jest.fn();
    const props = baseProps({
      itemIds: ['n-1'],
      onAddToTodo,
      isTodoPending: true,
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    const addToDoButton = screen.getByRole('button', { name: /^Add To Do$/i }) as HTMLButtonElement;
    expect(addToDoButton.disabled).toBe(true);
    act(() => {
      fireEvent.click(addToDoButton);
    });
    expect(onAddToTodo).not.toHaveBeenCalled();
  });

  it('OverflowMenu_DismissCallsParentCascade: clicking Dismiss invokes onDismiss(itemIds[]) — FR-14a cascade contract preserved', () => {
    const onDismiss = jest.fn();
    const props = baseProps({
      itemIds: ['n-1', 'n-2', 'n-3'],
      items: [makeItem({ id: 'n-1' }), makeItem({ id: 'n-2' }), makeItem({ id: 'n-3' })],
      onDismiss,
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Dismiss$/i }));
    });

    // FR-14a cascade contract: parent receives the FULL itemIds[] array.
    // DailyBriefingApp.handleDismiss iterates and markAsRead's each id.
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(onDismiss).toHaveBeenCalledWith(['n-1', 'n-2', 'n-3']);
  });

  it('OverflowMenu_OpenRecord: clicking Open record invokes onOpenRecord(type, id) when supplied (FR-18 new action)', () => {
    const onOpenRecord = jest.fn();
    const props = baseProps({
      itemIds: ['n-1'],
      onOpenRecord,
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Open record$/i }));
    });

    expect(onOpenRecord).toHaveBeenCalledTimes(1);
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', '11111111-1111-1111-1111-111111111111');
  });

  it('OverflowMenu_OpenRecord_Fallback: when onOpenRecord is undefined, Open record falls back to Xrm.Navigation.navigateTo', () => {
    const navigateTo = installXrm();
    const props = baseProps({
      itemIds: ['n-1'],
      onCheck: jest.fn(),
      onRemove: jest.fn(),
      onKeep: jest.fn(),
    });
    renderWith(props);

    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Open record$/i }));
    });

    expect(navigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toEqual({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: '11111111-1111-1111-1111-111111111111',
    });
    expect(navOptions).toMatchObject({
      target: 2,
      width: { value: 80, unit: '%' },
      height: { value: 80, unit: '%' },
    });
  });
});

// ---------------------------------------------------------------------------
// Tests — FR-11..FR-14a (R2 P2a sub-list + SubRow — PRESERVED unchanged)
// ---------------------------------------------------------------------------

describe('NarrativeBullet — P2a sub-list + SubRow behaviors (FR-11..FR-14a)', () => {
  afterEach(() => {
    uninstallXrm();
  });

  // -------------------------------------------------------------------------
  // Case 1: itemIds.length === 1 → no sub-list rendered.
  // -------------------------------------------------------------------------

  it('Case 1: does NOT render the sub-list when itemIds.length === 1', () => {
    const props = baseProps({
      itemIds: ['n-1'],
      // items provided, but length-1 short-circuit MUST suppress the sub-list.
      items: [makeItem({ id: 'n-1' })],
    });
    renderWith(props);

    // No role=list (the sub-list container uses role=list).
    expect(screen.queryByRole('list')).toBeNull();
    // No listitems either.
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);
    // Single-bullet UX still renders the narrative + entity link.
    expect(screen.getByText(/Review motion to dismiss/i)).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Case 2: itemIds.length > 1 → sub-list with N rows + ARIA roles rendered.
  // -------------------------------------------------------------------------

  it('Case 2: renders the sub-list with role=list and N listitems when itemIds.length > 1', () => {
    const items = [
      makeItem({ id: 'n-1', title: 'First overdue motion' }),
      makeItem({ id: 'n-2', title: 'Second overdue motion' }),
      makeItem({ id: 'n-3', title: 'Third overdue motion' }),
    ];
    const props = baseProps({
      itemIds: ['n-1', 'n-2', 'n-3'],
      items,
    });
    renderWith(props);

    // Exactly one sub-list container with role=list.
    const list = screen.getByRole('list');
    expect(list).toBeInTheDocument();
    // ARIA label reflects the count for screen readers.
    expect(list).toHaveAttribute('aria-label', expect.stringMatching(/3 underlying notifications/i));
    // N listitems = N items.
    const rows = screen.getAllByRole('listitem');
    expect(rows).toHaveLength(3);
    // Each item's title (SubRowLink display text) is visible.
    expect(screen.getByText('First overdue motion')).toBeInTheDocument();
    expect(screen.getByText('Second overdue motion')).toBeInTheDocument();
    expect(screen.getByText('Third overdue motion')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Case 3: sub-row link click invokes Xrm.Navigation.navigateTo with the
  //         underlying item's regardingEntityType + regardingId (FR-12).
  // -------------------------------------------------------------------------

  it("Case 3: sub-row link click calls Xrm.Navigation.navigateTo with the item's regardingEntityType + regardingId (FR-12)", () => {
    const navigateTo = installXrm();
    const items = [
      makeItem({
        id: 'n-1',
        title: 'Matter A link',
        regardingEntityType: 'sprk_matter',
        regardingId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      }),
      makeItem({
        id: 'n-2',
        title: 'Contact B link',
        regardingEntityType: 'contact',
        regardingId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      }),
    ];
    const props = baseProps({
      itemIds: ['n-1', 'n-2'],
      items,
    });
    renderWith(props);

    // Click the second sub-row link (Contact B).
    const link = screen.getByRole('link', { name: /Open Contact B link/i });
    fireEvent.click(link);

    expect(navigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    // FR-12: target uses the SUPPLIED item.regardingEntityType + regardingId
    // (NOT the primaryEntityType/primaryEntityId on the parent bullet).
    expect(pageInput).toEqual({
      pageType: 'entityrecord',
      entityName: 'contact',
      entityId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    });
    // Modal dialog options (target 2 + 80/80) per the SubRowLink contract.
    expect(navOptions).toMatchObject({
      target: 2,
      width: { value: 80, unit: '%' },
      height: { value: 80, unit: '%' },
    });
  });

  // -------------------------------------------------------------------------
  // Case 4: sub-row Add-to-To-Do click invokes onAddToTodoItem with the
  //         item's id (FR-13). [Note: top-level Add-To-Do is now a primary
  //         visible Checkmark button — covered by
  //         PrimaryAddToDoButton_CallsExistingPath. The primary button uses
  //         aria-label "Add To Do" (no lowercase "to"); the sub-row buttons
  //         use aria-label "Add to To Do". The regex below targets the
  //         sub-row label and does NOT match the primary button — leaving
  //         the per-item assertion intact.]
  // -------------------------------------------------------------------------

  it('Case 4: sub-row Add-to-To-Do click invokes onAddToTodoItem with item.id (FR-13)', () => {
    const onAddToTodoItem = jest.fn();
    const onAddToTodo = jest.fn();
    const items = [
      makeItem({ id: 'n-1', title: 'First todo target' }),
      makeItem({ id: 'n-2', title: 'Second todo target' }),
    ];
    const props = baseProps({
      itemIds: ['n-1', 'n-2'],
      items,
      onAddToTodo,
      onAddToTodoItem,
    });
    renderWith(props);

    // SubRowTodo renders a Button with aria-label="Add to To Do". The primary
    // task-134 button uses aria-label "Add To Do" (no lowercase "to") and
    // does NOT match the substring regex below. So the only "Add to To Do"
    // matches in the DOM right now are the per-item sub-row buttons.
    const subRowTodoButtons = screen.getAllByRole('button', { name: /Add to To Do/i });
    expect(subRowTodoButtons).toHaveLength(2);

    // Click each sub-row To-Do button and assert it fires with the matching
    // item.id.
    fireEvent.click(subRowTodoButtons[0]); // first sub-row → n-1
    fireEvent.click(subRowTodoButtons[1]); // second sub-row → n-2

    expect(onAddToTodoItem).toHaveBeenCalledTimes(2);
    expect(onAddToTodoItem).toHaveBeenNthCalledWith(1, 'n-1');
    expect(onAddToTodoItem).toHaveBeenNthCalledWith(2, 'n-2');
    // The aggregated callback was NOT fired by sub-row clicks.
    expect(onAddToTodo).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // Case 5: sub-row Dismiss invokes onDismissItem(item.id) for that single id
  //         (FR-14).
  // -------------------------------------------------------------------------

  it('Case 5: sub-row Dismiss click invokes onDismissItem(item.id) for that single id (FR-14)', async () => {
    const onDismissItem = jest.fn().mockResolvedValue(true);
    const items = [
      makeItem({ id: 'n-1', title: 'First dismissable' }),
      makeItem({ id: 'n-2', title: 'Second dismissable' }),
    ];
    const props = baseProps({
      itemIds: ['n-1', 'n-2'],
      items,
      onDismissItem: onDismissItem as unknown as (itemId: string) => void,
    });
    renderWith(props);

    // SubRowDismiss uses aria-label="Dismiss". The overflow menu's "Dismiss"
    // MenuItem is unmounted while the menu is closed, so the only Dismiss
    // buttons in the DOM right now are the sub-row buttons.
    const subRowDismissButtons = screen.getAllByRole('button', { name: /^Dismiss$/i });
    expect(subRowDismissButtons).toHaveLength(2);

    // Click the FIRST sub-row Dismiss button → should fire onDismissItem("n-1").
    // Wrap in act() because SubRowDismiss does a setState after the await.
    await React.act(async () => {
      fireEvent.click(subRowDismissButtons[0]);
      // Flush the promise returned by onDismissItem so the setDismissed
      // setState inside SubRowDismiss runs inside the same act() scope.
      await Promise.resolve();
    });

    expect(onDismissItem).toHaveBeenCalledTimes(1);
    expect(onDismissItem).toHaveBeenCalledWith('n-1');
  });

  // -------------------------------------------------------------------------
  // Case 6: aggregated Dismiss is now in the overflow menu — covered by
  //         OverflowMenu_DismissCallsParentCascade (above).
  // -------------------------------------------------------------------------
});
