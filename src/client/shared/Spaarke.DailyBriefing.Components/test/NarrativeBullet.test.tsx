/**
 * NarrativeBullet unit tests — R2 task 024 / P2a coverage (NFR-05 + ADR-021).
 *
 * Covers FR-11..FR-14a + dark-mode parity for the P2a hybrid-aggregation UX
 * introduced in Wave 8 (SubRow skeleton) + Wave 9 (SubRowLink / SubRowTodo /
 * SubRowDismiss slots).
 *
 * Cases:
 *   1. itemIds.length === 1 — no sub-list rendered (existing single-bullet UX).
 *   2. itemIds.length > 1 (with `items`) — sub-list with N rows + ARIA
 *      role=list/listitem rendered.
 *   3. Sub-row link click invokes Xrm.Navigation.navigateTo with the item's
 *      regardingEntityType + regardingId (FR-12). Window.Xrm is mocked.
 *   4. Sub-row Add-to-To-Do click invokes the onAddToTodoItem callback with
 *      item.id (FR-13).
 *   5. Sub-row Dismiss click invokes onDismissItem(item.id) for that single
 *      id (FR-14).
 *   6. Aggregated Dismiss (top-level button) invokes the parent onDismiss with
 *      the FULL itemIds[] array (FR-14a — verifies the prop contract that
 *      DailyBriefingApp.handleDismiss relies on for cascade).
 *   7. Dark-mode rendering — asserts semantic-token usage (Fluent v9
 *      tokens.colorNeutralForeground1 etc. resolve via FluentProvider's
 *      webDarkTheme), per ADR-021.
 *
 * Mocking strategy:
 *   - window.Xrm is installed per-test (case 3) and uninstalled after.
 *   - Callbacks (onAddToTodo, onDismiss, onAddToTodoItem, onDismissItem) are
 *     jest.fn() so each test is fully independent (no module-level state).
 *   - @spaarke/ui-components MicrosoftToDoIcon is routed via the existing
 *     jest.config moduleNameMapper to a no-op SVG stub.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
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
// Tests
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
  //         item's id (FR-13).
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

    // SubRowTodo renders a Button with aria-label="Add to To Do" (default).
    // The aggregated top-level button on NarrativeBullet ALSO has the same
    // aria-label. To uniquely identify the per-item buttons (FR-13), we
    // filter for buttons whose closest [role=listitem] ancestor exists —
    // i.e., buttons that live inside a sub-row. This is the canonical way
    // to assert sub-row-only behavior without DOM-order coupling.
    const allTodoButtons = screen.getAllByRole('button', { name: /Add to To Do/i });
    const subRowTodoButtons = allTodoButtons.filter(btn => btn.closest('[role="listitem"]') !== null);
    expect(subRowTodoButtons).toHaveLength(2);

    // Click each sub-row To-Do button and assert it fires with the matching
    // item.id. The aggregated button does NOT call onAddToTodoItem; it
    // calls onAddToTodo (a separate aggregated prop).
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
    // onDismissItem is awaited inside SubRowDismiss; return true to simulate
    // a successful markAsRead.
    const onDismissItem = jest.fn().mockResolvedValue(true);
    const items = [
      makeItem({ id: 'n-1', title: 'First dismissable' }),
      makeItem({ id: 'n-2', title: 'Second dismissable' }),
    ];
    const props = baseProps({
      itemIds: ['n-1', 'n-2'],
      items,
      // NarrativeBullet.SubRowProps expects `onDismissItem?: (itemId) => void`;
      // SubRowDismiss accepts (itemId) => Promise<boolean> | boolean | void.
      // The actual prop signature is widened in SubRowDismissProps, so
      // returning a Promise is fine. The NarrativeBullet prop type is
      // declared as `(itemId: string) => void` to keep the parent contract
      // simple; the resolved value is consumed by SubRowDismiss only.
      onDismissItem: onDismissItem as unknown as (itemId: string) => void,
    });
    renderWith(props);

    // SubRowDismiss uses aria-label="Dismiss". Filter for buttons inside a
    // sub-row (role=listitem) so we don't accidentally click the aggregated
    // Dismiss button — same canonical pattern as Case 4.
    const allDismissButtons = screen.getAllByRole('button', { name: /^Dismiss$/i });
    const subRowDismissButtons = allDismissButtons.filter(btn => btn.closest('[role="listitem"]') !== null);
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
  // Case 6: aggregated Dismiss invokes the parent onDismiss with the FULL
  //         itemIds[] array (FR-14a cascade prop contract).
  // -------------------------------------------------------------------------

  it('Case 6: aggregated (top-level) Dismiss invokes onDismiss with the full itemIds[] array (FR-14a contract)', () => {
    const onDismiss = jest.fn();
    const items = [makeItem({ id: 'n-1' }), makeItem({ id: 'n-2' }), makeItem({ id: 'n-3' })];
    const props = baseProps({
      itemIds: ['n-1', 'n-2', 'n-3'],
      items,
      onDismiss,
    });
    renderWith(props);

    // The aggregated Dismiss button is the first "Dismiss"-labeled button in
    // DOM order (rendered in the right-side `actions` cluster of the parent
    // NarrativeBullet, but appears BEFORE the sub-row dismiss buttons in the
    // accessibility tree because… actually, the sub-list is rendered inside
    // `content` which precedes `actions`. To avoid order coupling, we filter
    // for the button NOT inside a role=listitem (i.e., the aggregated one).
    const allDismiss = screen.getAllByRole('button', { name: /^Dismiss$/i });
    const aggregated = allDismiss.find(btn => btn.closest('[role="listitem"]') === null);
    expect(aggregated).toBeDefined();
    fireEvent.click(aggregated!);

    expect(onDismiss).toHaveBeenCalledTimes(1);
    // FR-14a: cascade contract — parent receives the FULL itemIds[] array.
    // DailyBriefingApp.handleDismiss iterates and markAsRead's each id.
    expect(onDismiss).toHaveBeenCalledWith(['n-1', 'n-2', 'n-3']);
  });

  // -------------------------------------------------------------------------
  // Case 7: dark-mode rendering — semantic tokens (ADR-021).
  // -------------------------------------------------------------------------

  it('Case 7: dark-mode renders via semantic tokens (no hard-coded colors in component source) (ADR-021)', () => {
    const items = [
      makeItem({ id: 'n-1', title: 'Dark mode row 1' }),
      makeItem({ id: 'n-2', title: 'Dark mode row 2' }),
    ];
    const props = baseProps({
      itemIds: ['n-1', 'n-2'],
      items,
    });
    const { container } = renderWith(props, webDarkTheme);

    // Sanity: the sub-list rendered under webDarkTheme.
    expect(screen.getByRole('list')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByText('Dark mode row 1')).toBeInTheDocument();
    expect(screen.getByText('Dark mode row 2')).toBeInTheDocument();
    expect(container.firstChild).not.toBeNull();

    // ADR-021 semantic-token parity check, two-pronged.
    //
    // 1) Static source check: scan NarrativeBullet, SubRow, SubRowLink,
    //    SubRowTodo, SubRowDismiss source for ANY hard-coded hex color
    //    literal (e.g. `#fff`, `#0078d4`). All color values MUST flow
    //    through `tokens.color*` so dark mode swaps them automatically.
    //
    //    This catches regressions at the authoring layer — the strongest
    //    JSDOM-friendly proof of dark-mode parity per the task notes
    //    ("JSDOM-friendly token lookup OR a Playwright snapshot — either
    //    works").
    //
    // 2) FluentProvider sanity: at least one <style> tag was injected by
    //    Griffel (proving the dark theme was applied via the v9 token
    //    system, not by-passed).
    const fs = require('fs') as typeof import('fs');
    const path = require('path') as typeof import('path');
    const componentsDir = path.resolve(__dirname, '../src/components');
    const filesToScan = ['NarrativeBullet.tsx', 'SubRow.tsx', 'SubRowLink.tsx', 'SubRowTodo.tsx', 'SubRowDismiss.tsx'];

    // Hex color literal: # followed by 3, 4, 6, or 8 hex digits, where the
    // PRECEDING character is a quote / colon / whitespace / open-paren.
    // This avoids false positives on URL fragments / hash IDs.
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
    // If any component has a hard-coded hex color, the test fails with the
    // offender list so the regression is immediately obvious.
    expect(offenders).toEqual([]);

    // Sanity: FluentProvider injected styles into the JSDOM document.
    const styleTags = document.querySelectorAll('style');
    expect(styleTags.length).toBeGreaterThan(0);
  });
});
