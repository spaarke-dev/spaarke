/**
 * ActivityNotesSection sub-list smoke test — R2.1 hotfix (2026-06-19), Fix C.
 *
 * Locks in Fix B (the consumer wiring in `ActivityNotesSection.tsx` that
 * passes `items` + per-item callbacks to `NarrativeBullet`) so it cannot
 * silently regress.
 *
 * Pre-Fix B, NarrativeBullet's optional `items` prop was never supplied by
 * the parent, so the sub-list rendering path (FR-11/12/13/14, built by
 * Wave 9 tasks 020-023) never activated. This test mounts ActivityNotesSection
 * with an aggregated bullet (itemIds.length === 2) and asserts the rendered
 * DOM contains 2 sub-row `listitem` elements + their per-item links — proving
 * the wiring is in place.
 *
 * If this test fails after a refactor, check that ActivityNotesSection still
 * passes `items` + `onAddToTodoItem` + `onDismissItem` to NarrativeBullet.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ActivityNotesSection } from '../src/components/ActivityNotesSection';
import type { ChannelFetchResult, NotificationItem } from '../src/types/notifications';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

function makeItem(overrides: Partial<NotificationItem>): NotificationItem {
  return {
    id: 'item-default',
    title: 'Default title',
    body: 'Default body',
    category: 'new-documents',
    priority: 'normal',
    actionUrl: '',
    regardingName: 'Default Matter',
    regardingEntityType: 'sprk_matter',
    regardingId: '00000000-0000-0000-0000-000000000001',
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    ...overrides,
  } as NotificationItem;
}

const ITEM_A = makeItem({
  id: 'item-a',
  title: 'Document A',
  regardingId: '00000000-0000-0000-0000-00000000000a',
  regardingName: 'Matter Alpha',
});

const ITEM_B = makeItem({
  id: 'item-b',
  title: 'Document B',
  regardingId: '00000000-0000-0000-0000-00000000000b',
  regardingName: 'Matter Bravo',
});

const AGGREGATED_CHANNEL_NARRATIVES = [
  {
    category: 'new-documents',
    bullets: [
      {
        narrative: 'Multiple documents await your review across two matters.',
        // itemIds.length > 1 → sub-list rendering path activates IFF items
        // is supplied by the parent (Fix B).
        itemIds: [ITEM_A.id, ITEM_B.id],
        primaryEntityType: 'sprk_matter',
        primaryEntityId: ITEM_A.regardingId,
        primaryEntityName: ITEM_A.regardingName,
      },
    ],
  },
];

const CHANNELS: ChannelFetchResult[] = [
  {
    status: 'success',
    group: {
      meta: {
        category: 'new-documents',
        label: 'New Documents',
        iconName: 'DocumentRegular',
        order: 1,
      },
      items: [ITEM_A, ITEM_B],
      unreadCount: 2,
    },
  },
];

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

function renderActivityNotes(): void {
  const noop = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <ActivityNotesSection
        channelNarratives={AGGREGATED_CHANNEL_NARRATIVES}
        channels={CHANNELS}
        onAddToTodo={noop}
        onDismiss={noop}
        isTodoCreated={() => false}
        isTodoPending={() => false}
        getTodoError={() => undefined}
        isLoading={false}
      />
    </FluentProvider>
  );
}

describe('ActivityNotesSection sub-list wiring (Fix B)', () => {
  test('aggregated bullet (itemIds.length > 1) renders N sub-row listitems', () => {
    renderActivityNotes();

    // SubRow.tsx wraps each row in role=listitem; SubRowContainer uses role=list.
    // FR-11: itemIds.length > 1 AND items supplied → N sub-rows rendered.
    const listItems = screen.getAllByRole('listitem');
    expect(listItems.length).toBeGreaterThanOrEqual(2);
  });

  test('each sub-row references the underlying NotificationItem title', () => {
    renderActivityNotes();

    // SubRowLink renders item.title || item.regardingName as the link text
    // when regardingEntityType + regardingId are present (FR-12).
    expect(screen.getByText('Document A')).toBeInTheDocument();
    expect(screen.getByText('Document B')).toBeInTheDocument();
  });

  test('aggregated narrative line still renders above the sub-list', () => {
    renderActivityNotes();

    // The narrative text from the AI-generated bullet stays the primary
    // content; the sub-list is supplementary per FR-11.
    expect(screen.getByText(/Multiple documents await your review across two matters/i)).toBeInTheDocument();
  });
});
