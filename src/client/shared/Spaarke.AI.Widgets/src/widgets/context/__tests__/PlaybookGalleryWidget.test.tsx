/**
 * PlaybookGalleryWidget — unit tests
 *
 * Covers:
 * (a) Playbooks render as cards with correct name, description, and capability badges.
 * (b) Clicking a card dispatches a playbook_change event to the 'conversation' channel.
 * (c) An empty playbook list renders the EmptyState (never a blank pane).
 * (d) isLoading=true renders Skeleton placeholders, not card content.
 * (e) error prop renders the error message, not card content.
 * (f) The selected card receives aria-pressed="true" and brand styling class.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import PlaybookGalleryWidget, { type PlaybookGalleryData } from '../PlaybookGalleryWidget';
import type { ConversationPaneEvent } from '../../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWidget(
  data: PlaybookGalleryData,
  options: { isLoading?: boolean; error?: string; bus?: PaneEventBus } = {}
) {
  const bus = options.bus ?? new PaneEventBus();

  const { unmount } = render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <PlaybookGalleryWidget
          data={data}
          widgetType="playbook-gallery"
          isLoading={options.isLoading}
          error={options.error}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );

  return { bus, unmount };
}

const PLAYBOOKS: PlaybookGalleryData = {
  playbooks: [
    {
      id: 'pb-legal-review',
      name: 'Legal Review',
      description: 'Analyse contracts for risk and compliance issues.',
      capabilityBadges: ['Contract Analysis', 'Risk Flags'],
    },
    {
      id: 'pb-document-compare',
      name: 'Document Compare',
      description: 'Compare two versions of a document and highlight differences.',
      capabilityBadges: ['Compare', 'Redline'],
    },
    {
      id: 'pb-summary',
      name: 'Document Summary',
      description: 'Generate a concise summary of any uploaded document.',
      capabilityBadges: [],
    },
  ],
};

// ---------------------------------------------------------------------------
// (a) Card rendering
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — card rendering', () => {
  it('renders a card for every playbook in the data payload', () => {
    renderWidget(PLAYBOOKS);

    expect(screen.getByRole('button', { name: /Legal Review/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Document Compare/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Document Summary/ })).toBeInTheDocument();
  });

  it('displays the playbook name as the card title', () => {
    renderWidget(PLAYBOOKS);

    expect(screen.getByText('Legal Review')).toBeInTheDocument();
    expect(screen.getByText('Document Compare')).toBeInTheDocument();
  });

  it('displays the playbook description', () => {
    renderWidget(PLAYBOOKS);

    expect(screen.getByText('Analyse contracts for risk and compliance issues.')).toBeInTheDocument();
    expect(screen.getByText('Compare two versions of a document and highlight differences.')).toBeInTheDocument();
  });

  it('renders capability badges for each playbook that has them', () => {
    renderWidget(PLAYBOOKS);

    // Legal Review badges
    expect(screen.getByText('Contract Analysis')).toBeInTheDocument();
    expect(screen.getByText('Risk Flags')).toBeInTheDocument();

    // Document Compare badges
    expect(screen.getByText('Compare')).toBeInTheDocument();
    expect(screen.getByText('Redline')).toBeInTheDocument();
  });

  it('renders no badges for playbooks with an empty capabilityBadges array', () => {
    renderWidget({
      playbooks: [
        {
          id: 'pb-no-badges',
          name: 'No Badges',
          description: 'A playbook with no capability badges.',
          capabilityBadges: [],
        },
      ],
    });

    // Confirm the card is there but no badge elements
    expect(screen.getByRole('button', { name: /No Badges/ })).toBeInTheDocument();
    expect(screen.queryByRole('mark')).not.toBeInTheDocument();
  });

  it('renders cards inside a list region with correct accessible label', () => {
    renderWidget(PLAYBOOKS);

    const list = screen.getByRole('list', { name: /available playbooks/i });
    expect(list).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (b) Selection dispatch
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — playbook selection dispatch', () => {
  it('dispatches playbook_change to the conversation channel when a card is clicked', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const received: ConversationPaneEvent[] = [];
    bus.subscribe('conversation', e => received.push(e));

    renderWidget(PLAYBOOKS, { bus });

    const card = screen.getByRole('button', { name: /Legal Review/ });
    await user.click(card);

    expect(received).toHaveLength(1);
    expect(received[0]).toMatchObject({
      type: 'playbook_change',
      playbookId: 'pb-legal-review',
      playbookName: 'Legal Review',
    });
  });

  it('dispatches to conversation channel — not workspace, context, or safety', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();

    const workspaceEvents: unknown[] = [];
    const contextEvents: unknown[] = [];
    const safetyEvents: unknown[] = [];
    const conversationEvents: unknown[] = [];

    bus.subscribe('workspace', e => workspaceEvents.push(e));
    bus.subscribe('context', e => contextEvents.push(e));
    bus.subscribe('safety', e => safetyEvents.push(e));
    bus.subscribe('conversation', e => conversationEvents.push(e));

    renderWidget(PLAYBOOKS, { bus });

    await user.click(screen.getByRole('button', { name: /Document Compare/ }));

    expect(conversationEvents).toHaveLength(1);
    expect(workspaceEvents).toHaveLength(0);
    expect(contextEvents).toHaveLength(0);
    expect(safetyEvents).toHaveLength(0);
  });

  it('dispatches correct id and name for each distinct playbook selection', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const received: ConversationPaneEvent[] = [];
    bus.subscribe('conversation', e => received.push(e));

    renderWidget(PLAYBOOKS, { bus });

    await user.click(screen.getByRole('button', { name: /Document Compare/ }));
    await user.click(screen.getByRole('button', { name: /Document Summary/ }));

    expect(received[0]).toMatchObject({
      type: 'playbook_change',
      playbookId: 'pb-document-compare',
      playbookName: 'Document Compare',
    });
    expect(received[1]).toMatchObject({
      type: 'playbook_change',
      playbookId: 'pb-summary',
      playbookName: 'Document Summary',
    });
  });

  it('supports keyboard selection via Enter key', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const received: ConversationPaneEvent[] = [];
    bus.subscribe('conversation', e => received.push(e));

    renderWidget(PLAYBOOKS, { bus });

    const card = screen.getByRole('button', { name: /Legal Review/ });
    card.focus();
    await user.keyboard('{Enter}');

    expect(received).toHaveLength(1);
    expect(received[0].type).toBe('playbook_change');
  });

  it('supports keyboard selection via Space key', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const received: ConversationPaneEvent[] = [];
    bus.subscribe('conversation', e => received.push(e));

    renderWidget(PLAYBOOKS, { bus });

    const card = screen.getByRole('button', { name: /Legal Review/ });
    card.focus();
    await user.keyboard('{ }');

    expect(received).toHaveLength(1);
    expect(received[0].type).toBe('playbook_change');
  });
});

// ---------------------------------------------------------------------------
// (c) Selected card visual state
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — selected card state', () => {
  it('sets aria-pressed="true" on the selected card and "false" on others', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();

    renderWidget(PLAYBOOKS, { bus });

    const legalCard = screen.getByRole('button', { name: /Legal Review/ });
    const compareCard = screen.getByRole('button', { name: /Document Compare/ });

    // Nothing selected initially
    expect(legalCard).toHaveAttribute('aria-pressed', 'false');
    expect(compareCard).toHaveAttribute('aria-pressed', 'false');

    await user.click(legalCard);

    expect(legalCard).toHaveAttribute('aria-pressed', 'true');
    expect(compareCard).toHaveAttribute('aria-pressed', 'false');
  });

  it('moves selection to the newly clicked card', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();

    renderWidget(PLAYBOOKS, { bus });

    const legalCard = screen.getByRole('button', { name: /Legal Review/ });
    const compareCard = screen.getByRole('button', { name: /Document Compare/ });

    await user.click(legalCard);
    expect(legalCard).toHaveAttribute('aria-pressed', 'true');

    await user.click(compareCard);
    expect(legalCard).toHaveAttribute('aria-pressed', 'false');
    expect(compareCard).toHaveAttribute('aria-pressed', 'true');
  });

  it('updates aria-label to include ", selected" for the chosen card', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();

    renderWidget(PLAYBOOKS, { bus });

    await user.click(screen.getByRole('button', { name: /Legal Review/ }));

    expect(screen.getByRole('button', { name: /Legal Review, selected/ })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (d) Loading state
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — loading state', () => {
  it('renders skeleton placeholders when isLoading is true', () => {
    renderWidget({ playbooks: [] }, { isLoading: true });

    const busy = screen.getByRole('region', { hidden: false });
    expect(busy.querySelector('[aria-busy="true"]')).toBeInTheDocument();
  });

  it('does not render playbook cards while loading', () => {
    renderWidget(PLAYBOOKS, { isLoading: true });

    // No card buttons visible during load
    expect(screen.queryByRole('button', { name: /Legal Review/ })).not.toBeInTheDocument();
  });

  it('does not render the empty state while loading', () => {
    renderWidget({ playbooks: [] }, { isLoading: true });

    expect(screen.queryByText(/No playbooks available/)).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (c) Empty state
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — empty state', () => {
  it('renders the empty state when the playbook list is empty', () => {
    renderWidget({ playbooks: [] });

    expect(screen.getByText('No playbooks available')).toBeInTheDocument();
  });

  it('renders the empty state body text describing next steps', () => {
    renderWidget({ playbooks: [] });

    expect(screen.getByText(/No AI playbooks have been configured/i)).toBeInTheDocument();
  });

  it('does not render any playbook cards in the empty state', () => {
    renderWidget({ playbooks: [] });

    expect(screen.queryByRole('list', { name: /available playbooks/i })).not.toBeInTheDocument();
  });

  it('renders a status region for screen readers in the empty state', () => {
    renderWidget({ playbooks: [] });

    expect(screen.getByRole('status')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (e) Error state
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — error state', () => {
  it('renders the error message when error prop is provided', () => {
    renderWidget(PLAYBOOKS, { error: 'Failed to load playbooks. Please try again.' });

    expect(screen.getByText('Failed to load playbooks. Please try again.')).toBeInTheDocument();
  });

  it('error message is in an alert role for screen readers', () => {
    renderWidget({ playbooks: [] }, { error: 'Network error' });

    expect(screen.getByRole('alert')).toHaveTextContent('Network error');
  });
});

// ---------------------------------------------------------------------------
// (f) Data edge cases
// ---------------------------------------------------------------------------

describe('PlaybookGalleryWidget — data edge cases', () => {
  it('handles undefined data gracefully (treats as empty list)', () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider bus={new PaneEventBus()}>
          <PlaybookGalleryWidget data={undefined as unknown as PlaybookGalleryData} widgetType="playbook-gallery" />
        </PaneEventBusProvider>
      </FluentProvider>
    );

    expect(screen.getByText('No playbooks available')).toBeInTheDocument();
  });

  it('renders the section header text always (even when loading)', () => {
    renderWidget({ playbooks: [] }, { isLoading: true });

    expect(screen.getByText('Choose a Playbook')).toBeInTheDocument();
  });

  it('renders a playbook with a single capability badge correctly', () => {
    renderWidget({
      playbooks: [
        {
          id: 'pb-single-badge',
          name: 'Single Badge Playbook',
          description: 'Has one badge.',
          capabilityBadges: ['Summarise'],
        },
      ],
    });

    expect(screen.getByText('Summarise')).toBeInTheDocument();
  });
});
