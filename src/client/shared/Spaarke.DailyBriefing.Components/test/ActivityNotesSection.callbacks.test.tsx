/**
 * ActivityNotesSection callback-wiring tests — R4 tasks 046 + 047.
 *
 * Locks in the FR-18 + FR-19 callback contract: ActivityNotesSection must
 * propagate ALL SIX overflow-menu / link callbacks down to NarrativeBullet:
 *
 *   1. onCheck         (R3 FR-4 — Mark as read)
 *   2. onRemove        (R3 FR-5 — Remove from briefing)
 *   3. onKeep          (R3 FR-6 — Keep on briefing for 7 more days)
 *   4. onAddToTodo     (ADR-024 / R2 — Add to To Do)
 *   5. onDismiss       (FR-14a — Dismiss)
 *   6. onOpenRecord    (FR-18 #6 + FR-19 — Open record / link click)
 *
 * Each test mounts ActivityNotesSection with a single-item bullet, opens the
 * NarrativeBullet overflow menu, invokes the relevant MenuItem, and asserts
 * the corresponding callback fired with the canonical args.
 *
 * If this file fails after a refactor, check that ActivityNotesSection still
 * passes the full six-callback set straight through to NarrativeBullet
 * (regression test for the wiring layer added by R4 task 046).
 */

import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ActivityNotesSection } from '../src/components/ActivityNotesSection';
import type { ChannelFetchResult, NotificationItem } from '../src/types/notifications';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const ITEM_ID = '11111111-1111-1111-1111-111111111111';

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
    regardingId: ITEM_ID,
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    dueDate: null,
    ttlinseconds: 604800,
    ...overrides,
  };
}

const CHANNELS: ChannelFetchResult[] = [
  {
    status: 'success',
    group: {
      meta: {
        category: 'tasks-overdue',
        label: 'Overdue Tasks',
        iconName: 'Warning',
        order: 1,
      },
      items: [makeItem({ id: 'n-1' })],
      unreadCount: 1,
    },
  },
];

const CHANNEL_NARRATIVES = [
  {
    category: 'tasks-overdue',
    bullets: [
      {
        narrative: 'Review motion to dismiss for Acme Matter.',
        itemIds: ['n-1'],
        primaryEntityType: 'sprk_matter',
        primaryEntityId: ITEM_ID,
        primaryEntityName: 'Acme Matter',
      },
    ],
  },
];

interface RenderOpts {
  onAddToTodo?: jest.Mock;
  onDismiss?: jest.Mock;
  onCheck?: jest.Mock;
  onRemove?: jest.Mock;
  onKeep?: jest.Mock;
  onOpenRecord?: jest.Mock;
}

function renderSection(opts: RenderOpts = {}): {
  callbacks: Required<RenderOpts>;
} {
  const callbacks: Required<RenderOpts> = {
    onAddToTodo: opts.onAddToTodo ?? jest.fn(),
    onDismiss: opts.onDismiss ?? jest.fn(),
    onCheck: opts.onCheck ?? jest.fn(),
    onRemove: opts.onRemove ?? jest.fn(),
    onKeep: opts.onKeep ?? jest.fn(),
    onOpenRecord: opts.onOpenRecord ?? jest.fn(),
  };
  render(
    <FluentProvider theme={webLightTheme}>
      <ActivityNotesSection
        channelNarratives={CHANNEL_NARRATIVES}
        channels={CHANNELS}
        onAddToTodo={callbacks.onAddToTodo}
        onDismiss={callbacks.onDismiss}
        isTodoCreated={() => false}
        isTodoPending={() => false}
        getTodoError={() => undefined}
        isLoading={false}
        onCheck={callbacks.onCheck}
        onRemove={callbacks.onRemove}
        onKeep={callbacks.onKeep}
        onOpenRecord={callbacks.onOpenRecord}
      />
    </FluentProvider>
  );
  return { callbacks };
}

function openOverflowMenu(): HTMLElement {
  const trigger = screen.getByRole('button', { name: /More actions/i });
  act(() => {
    fireEvent.click(trigger);
  });
  return screen.getByRole('menu');
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ActivityNotesSection — FR-18 + FR-19 callback wiring (R4 tasks 046+047)', () => {
  it('ActivityNotesSection_PropagatesAllSixCallbacks: 6 MenuItems render → all 6 callback props reach NarrativeBullet', () => {
    renderSection();
    openOverflowMenu();

    const labels = screen
      .getAllByRole('menuitem')
      .map(el => (el.textContent ?? '').trim());
    // Canonical FR-18 order: 6 actions — proves all 6 callback props were
    // received by NarrativeBullet (because NarrativeBullet hides items 1/2/3
    // when their callback prop is undefined, and hides item 6 when
    // primaryEntityType/Id are missing).
    expect(labels).toEqual([
      'Mark as read',
      'Remove from briefing',
      'Keep on briefing for 7 more days',
      'Add to To Do',
      'Dismiss',
      'Open record',
    ]);
  });

  it('ActivityNotesSection_OnMarkAsRead_TriggersR3CheckHandler: Mark as read → onCheck(itemId) preserved (AC-18c regression coverage)', () => {
    const { callbacks } = renderSection();
    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Mark as read$/i }));
    });
    // R3 FR-4 contract: onCheck(itemId). Wiring proves the section passed the
    // R3 handler unchanged so existing optimistic-UI + toast logic still fires.
    expect(callbacks.onCheck).toHaveBeenCalledTimes(1);
    expect(callbacks.onCheck).toHaveBeenCalledWith('n-1');
  });

  it('ActivityNotesSection_OnRemove_TriggersR3RemoveHandler: Remove → onRemove(itemId) preserved (AC-18c regression coverage)', () => {
    const { callbacks } = renderSection();
    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Remove from briefing$/i }));
    });
    expect(callbacks.onRemove).toHaveBeenCalledTimes(1);
    expect(callbacks.onRemove).toHaveBeenCalledWith('n-1');
  });

  it('ActivityNotesSection_OnKeep_TriggersR3KeepHandler: Keep +7d → onKeep(itemId, ttl) preserved (AC-18c regression coverage)', () => {
    const { callbacks } = renderSection();
    openOverflowMenu();
    act(() => {
      fireEvent.click(
        screen.getByRole('menuitem', { name: /^Keep on briefing for 7 more days$/i })
      );
    });
    expect(callbacks.onKeep).toHaveBeenCalledTimes(1);
    // ttl flows through from NotificationItem.ttlinseconds = 604800 (fixture).
    expect(callbacks.onKeep).toHaveBeenCalledWith('n-1', 604800);
  });

  it('ActivityNotesSection_OnAddToTodo_InvokesUseInlineTodoCreatePath: ADR-024 preserved — onAddToTodo(itemIds[]) flows up', () => {
    const { callbacks } = renderSection();
    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Add to To Do$/i }));
    });
    // ADR-024 regression-free invariant: parent's onAddToTodo receives the
    // full itemIds[]; the parent (DailyBriefingApp) owns useInlineTodoCreate
    // + TODO_REGARDING_CATALOG. This test asserts the section passed the
    // callback through unchanged.
    expect(callbacks.onAddToTodo).toHaveBeenCalledTimes(1);
    expect(callbacks.onAddToTodo).toHaveBeenCalledWith(['n-1']);
  });

  it('ActivityNotesSection_OnDismiss_TriggersDismissHandler: Dismiss → onDismiss(itemIds[]) cascade preserved', () => {
    const { callbacks } = renderSection();
    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Dismiss$/i }));
    });
    expect(callbacks.onDismiss).toHaveBeenCalledTimes(1);
    expect(callbacks.onDismiss).toHaveBeenCalledWith(['n-1']);
  });

  it('ActivityNotesSection_OnOpenRecord_FiresFromOverflowMenu: Open record → onOpenRecord(type, id)', () => {
    const { callbacks } = renderSection();
    openOverflowMenu();
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Open record$/i }));
    });
    expect(callbacks.onOpenRecord).toHaveBeenCalledTimes(1);
    expect(callbacks.onOpenRecord).toHaveBeenCalledWith('sprk_matter', ITEM_ID);
  });

  it('ActivityNotesSection_OnOpenRecord_FiresFromRegardingNameLink: FR-19 link click → onOpenRecord(type, id)', () => {
    const { callbacks } = renderSection();
    // The regarding-name link in NarrativeBullet renders with role="link" and
    // text "Acme Matter ↗" (the up-right arrow glyph). Click invokes
    // handleLinkClick → onOpenRecord(primaryEntityType, primaryEntityId).
    const link = screen.getByRole('link', { name: /Acme Matter/i });
    act(() => {
      fireEvent.click(link);
    });
    expect(callbacks.onOpenRecord).toHaveBeenCalledTimes(1);
    expect(callbacks.onOpenRecord).toHaveBeenCalledWith('sprk_matter', ITEM_ID);
  });
});
