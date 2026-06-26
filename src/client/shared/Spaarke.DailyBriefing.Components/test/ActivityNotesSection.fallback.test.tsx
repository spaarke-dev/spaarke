/**
 * ActivityNotesSection empty-narrative fallback tests — R4 task 034 (FR-16 / AC-16).
 *
 * Verifies the defense-in-depth fallback rendering branch:
 *
 *   - When `channelNarratives` is empty AND raw notifications exist, the
 *     component renders an "AI summary unavailable" MessageBar above
 *     per-channel raw notification cards (FR-16).
 *   - When `channelNarratives` has content, the normal AI-narrated rendering
 *     path is unchanged (no fallback banner).
 *   - When BOTH `channelNarratives` and raw notifications are empty, the
 *     component returns null (parent owns the all-caught-up state).
 *   - Dark mode compliance: the component renders without raw hex colors and
 *     respects the FluentProvider theme (semantic tokens — ADR-021).
 *
 * Companion to `ActivityNotesSection.subList.test.tsx` which locks in the
 * R2.1 Fix B sub-list wiring on the normal-rendering path.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
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
    createdOn: '2026-06-26T10:00:00Z',
    dueDate: null,
    ...overrides,
  } as NotificationItem;
}

const RAW_ITEM_DOC = makeItem({
  id: 'raw-item-doc',
  title: 'New document uploaded',
  body: 'Contract_v2.pdf added to Matter Alpha',
  regardingId: '00000000-0000-0000-0000-00000000000a',
  regardingName: 'Matter Alpha',
  category: 'new-documents',
});

const RAW_ITEM_EMAIL = makeItem({
  id: 'raw-item-email',
  title: 'Incoming email',
  body: 'From: counsel@example.com — Re: filing deadline',
  regardingId: '00000000-0000-0000-0000-00000000000b',
  regardingName: 'Matter Bravo',
  category: 'new-emails',
});

const CHANNELS_WITH_ITEMS: ChannelFetchResult[] = [
  {
    status: 'success',
    group: {
      meta: {
        category: 'new-documents',
        label: 'New Documents',
        iconName: 'DocumentRegular',
        order: 1,
      },
      items: [RAW_ITEM_DOC],
      unreadCount: 1,
    },
  },
  {
    status: 'success',
    group: {
      meta: {
        category: 'new-emails',
        label: 'New Emails',
        iconName: 'MailRegular',
        order: 2,
      },
      items: [RAW_ITEM_EMAIL],
      unreadCount: 1,
    },
  },
];

const CHANNELS_EMPTY: ChannelFetchResult[] = [
  {
    status: 'success',
    group: {
      meta: {
        category: 'new-documents',
        label: 'New Documents',
        iconName: 'DocumentRegular',
        order: 1,
      },
      items: [],
      unreadCount: 0,
    },
  },
];

const NORMAL_CHANNEL_NARRATIVES = [
  {
    category: 'new-documents',
    bullets: [
      {
        narrative: 'A document was uploaded to Matter Alpha.',
        itemIds: [RAW_ITEM_DOC.id],
        primaryEntityType: 'sprk_matter',
        primaryEntityId: RAW_ITEM_DOC.regardingId,
        primaryEntityName: RAW_ITEM_DOC.regardingName,
      },
    ],
  },
];

// ---------------------------------------------------------------------------
// Render helpers
// ---------------------------------------------------------------------------

interface RenderOpts {
  channelNarratives: Array<{
    category: string;
    bullets: Array<{
      narrative: string;
      itemIds: string[];
      primaryEntityType: string;
      primaryEntityId: string;
      primaryEntityName: string;
    }>;
  }>;
  channels: ChannelFetchResult[];
  theme?: typeof webLightTheme;
}

function renderActivityNotes({
  channelNarratives,
  channels,
  theme = webLightTheme,
}: RenderOpts): ReturnType<typeof render> {
  const noop = jest.fn();
  return render(
    <FluentProvider theme={theme}>
      <ActivityNotesSection
        channelNarratives={channelNarratives}
        channels={channels}
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

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ActivityNotesSection — FR-16 empty-narrative fallback', () => {
  test('RendersFallbackWhenNarrativesEmpty: banner + raw cards render when channelNarratives is empty', () => {
    renderActivityNotes({
      channelNarratives: [],
      channels: CHANNELS_WITH_ITEMS,
    });

    // Banner — "AI summary unavailable." + descriptive copy.
    expect(screen.getByText(/AI summary unavailable\./i)).toBeInTheDocument();
    expect(screen.getByText(/Showing raw notifications below/i)).toBeInTheDocument();

    // Per-channel grouping — both channels have headings.
    expect(screen.getByText('New Documents')).toBeInTheDocument();
    expect(screen.getByText('New Emails')).toBeInTheDocument();

    // Raw notification cards — titles + bodies + regarding link names appear.
    expect(screen.getByText('New document uploaded')).toBeInTheDocument();
    expect(screen.getByText(/Contract_v2\.pdf added to Matter Alpha/)).toBeInTheDocument();
    expect(screen.getByText('Incoming email')).toBeInTheDocument();
  });

  test('RendersNarrativesWhenAvailable: no fallback banner when channelNarratives has content', () => {
    renderActivityNotes({
      channelNarratives: NORMAL_CHANNEL_NARRATIVES,
      channels: CHANNELS_WITH_ITEMS,
    });

    // Banner copy MUST be absent — normal AI-narrated rendering path.
    expect(screen.queryByText(/AI summary unavailable/i)).not.toBeInTheDocument();

    // Narrative bullet rendered normally.
    expect(screen.getByText(/A document was uploaded to Matter Alpha/)).toBeInTheDocument();
  });

  test('HidesEverythingWhenZeroNotifications: returns null when both narratives and raw items are empty', () => {
    const { container } = renderActivityNotes({
      channelNarratives: [],
      channels: CHANNELS_EMPTY,
    });

    // Preserve historical behavior — parent owns all-caught-up state.
    // FluentProvider always wraps with a <div>; we assert the
    // ActivityNotesSection contributed no rendered "Activity Notes" heading.
    expect(screen.queryByText('Activity Notes')).not.toBeInTheDocument();
    expect(screen.queryByText(/AI summary unavailable/i)).not.toBeInTheDocument();
    // Sanity: rendered container has no notification-card text.
    expect(container.textContent ?? '').not.toMatch(/Contract_v2/);
  });

  test('SkipsErroredAndSystemChannels: only successful non-system channels with items render in fallback', () => {
    const channelsMixed: ChannelFetchResult[] = [
      ...CHANNELS_WITH_ITEMS,
      {
        status: 'error',
        category: 'new-events',
        error: 'fetch failed',
      },
      {
        status: 'success',
        group: {
          meta: {
            category: 'system',
            label: 'System',
            iconName: 'InfoRegular',
            order: 99,
          },
          items: [
            makeItem({
              id: 'sys-item',
              title: 'System notice',
              body: 'system body',
              category: 'system',
            }),
          ],
          unreadCount: 1,
        },
      },
    ];

    renderActivityNotes({
      channelNarratives: [],
      channels: channelsMixed,
    });

    // Banner present.
    expect(screen.getByText(/AI summary unavailable/i)).toBeInTheDocument();

    // Successful non-system channels render.
    expect(screen.getByText('New Documents')).toBeInTheDocument();
    expect(screen.getByText('New Emails')).toBeInTheDocument();

    // System channel suppressed — matches the narrative-path system filter.
    expect(screen.queryByText('System notice')).not.toBeInTheDocument();
  });

  test('DarkModeCompliance: fallback renders under dark theme without raw hex literal styles', () => {
    const { container } = renderActivityNotes({
      channelNarratives: [],
      channels: CHANNELS_WITH_ITEMS,
      theme: webDarkTheme,
    });

    // Banner + raw cards render under dark theme.
    expect(screen.getByText(/AI summary unavailable/i)).toBeInTheDocument();
    expect(screen.getByText('New document uploaded')).toBeInTheDocument();

    // ADR-021: no raw hex color literals in inline styles. Fluent v9 emits
    // CSS variables (e.g., `var(--colorNeutralForeground1)`) via makeStyles;
    // any inline `style="color: #fff"` would be a violation.
    const inlineStyleElements = container.querySelectorAll('[style]');
    inlineStyleElements.forEach(el => {
      const styleAttr = el.getAttribute('style') ?? '';
      // Specifically forbid hex literals; semantic tokens compile to var(...).
      expect(styleAttr).not.toMatch(/#[0-9a-fA-F]{3,8}/);
    });
  });
});
