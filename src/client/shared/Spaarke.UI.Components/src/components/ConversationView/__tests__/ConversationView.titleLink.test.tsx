/**
 * ConversationView.titleLink.test.tsx (task 025, FR-12)
 *
 * Coverage for the conversation title header: the title links to the thread's
 * associated record ONLY when the thread has a `regarding` AND the host wired
 * `onOpenRecord`; clicking delegates the open to the host (which uses the
 * sanctioned OOB record-scoped modal per MODAL-DECISION-CRITERIA — the shared
 * component imports no `Xrm` and embeds no iframe, ADR-012). Record-less
 * threads (or hosts with no `onOpenRecord`) render a plain, non-interactive
 * title. No `title` → no header at all (existing callers unaffected).
 *
 * Per ADR-038/ADR-028 a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import) — same pattern as the
 * sibling `ConversationView.test.tsx` / `ConversationView.scrollToMessage.test.tsx`.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { ConversationView } from '../ConversationView';
import type { ConversationViewProps, IConversationViewRegarding } from '../ConversationView.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_1 = '11111111-1111-1111-1111-111111111111';
const USER_2 = '22222222-2222-2222-2222-222222222222';

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000000, // PlainText
    communicationType: 100000004, // Message → chat bubble
    from: 'colleague@example.com',
    direction: null,
    sentBy: USER_2,
    sentByName: 'Colleague',
    sentAt: '2026-07-20T10:00:00Z',
    createdOn: '2026-07-20T10:00:00Z',
    inReplyTo: null,
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

function jsonResponse(body: unknown): Response {
  return { ok: true, json: async () => body } as unknown as Response;
}

function buildFetch(messages: IThreadMessageDto[]): jest.Mock {
  return jest.fn(async (url: string) => {
    if (url.includes('/unread-count')) {
      return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
    }
    return jsonResponse({ threadId: 't1', name: null, messages, count: messages.length });
  });
}

function renderView(overrides: Partial<ConversationViewProps>, theme = webLightTheme) {
  const props: ConversationViewProps = {
    threadId: 't1',
    currentUserSystemUserId: USER_1,
    authenticatedFetch: buildFetch([dto('m1')]) as unknown as ConversationViewProps['authenticatedFetch'],
    ...overrides,
  };
  return render(
    <FluentProvider theme={theme}>
      <ConversationView {...props} />
    </FluentProvider>
  );
}

const MATTER: IConversationViewRegarding = { entityType: 'sprk_matter', id: 'M1', name: 'Acme v. Beta' };

describe('ConversationView title header (FR-12)', () => {
  it('renders the title as a link and invokes onOpenRecord with the regarding entityType + id on click', async () => {
    const onOpenRecord = jest.fn();
    renderView({ title: 'Acme v. Beta', regarding: MATTER, onOpenRecord });

    const link = await screen.findByRole('button', { name: /acme v\. beta, open associated record/i });
    expect(link).toBeInTheDocument();

    await userEvent.click(link);
    expect(onOpenRecord).toHaveBeenCalledTimes(1);
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', 'M1');
  });

  it('renders a plain (non-interactive) title when the thread has no regarding record', async () => {
    renderView({ title: 'Direct chat with Colleague', onOpenRecord: jest.fn() });

    expect(await screen.findByText('Direct chat with Colleague')).toBeInTheDocument();
    // No record → not a link/button.
    expect(screen.queryByRole('button', { name: /open associated record/i })).toBeNull();
  });

  it('renders a plain title when a regarding exists but no onOpenRecord is wired', async () => {
    renderView({ title: 'Acme v. Beta', regarding: MATTER });

    expect(await screen.findByText('Acme v. Beta')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /open associated record/i })).toBeNull();
  });

  it('renders no title text when no title is supplied, but still offers the open-record TOOL when a record is wired (round-8.4 item 3)', async () => {
    renderView({ regarding: MATTER, onOpenRecord: jest.fn() });

    // Let the initial load settle so the assertion is about the header, not timing.
    await waitFor(() => expect(screen.getByRole('log', { name: /conversation messages/i })).toBeInTheDocument());
    // No title text/link (the title header itself stays absent — existing callers unaffected there).
    expect(screen.queryByText('Acme v. Beta')).toBeNull();
    // But the dedicated toolbar "open associated record" affordance now appears whenever a regarding record +
    // onOpenRecord are wired — it is a record-scoped TOOL, not part of the title. (In practice a record-anchored
    // thread has a name too, so this nameless case is rare; the tool must still be reachable.)
    expect(screen.getByRole('button', { name: /^open associated record$/i })).toBeInTheDocument();
  });

  it('title link is keyboard-operable (Enter activates onOpenRecord) and ARIA-labeled', async () => {
    const onOpenRecord = jest.fn();
    renderView({ title: 'Acme v. Beta', regarding: MATTER, onOpenRecord });

    const link = await screen.findByRole('button', { name: /acme v\. beta, open associated record/i });
    link.focus();
    expect(link).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', 'M1');
  });

  it('renders the linked title under the dark theme (ADR-021 — semantic tokens, no crash)', async () => {
    const onOpenRecord = jest.fn();
    renderView({ title: 'Acme v. Beta', regarding: MATTER, onOpenRecord }, webDarkTheme);

    expect(await screen.findByRole('button', { name: /acme v\. beta, open associated record/i })).toBeInTheDocument();
  });

  it('exposes the title as a heading for screen-reader navigation (NFR-05), for both linked and plain variants', async () => {
    const { rerender } = renderView({ title: 'Acme v. Beta', regarding: MATTER, onOpenRecord: jest.fn() });
    expect(await screen.findByRole('heading', { name: /acme v\. beta/i })).toBeInTheDocument();

    // Plain (record-less) title is still a heading.
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ConversationView
          threadId="t1"
          currentUserSystemUserId={USER_1}
          title="Direct chat"
          authenticatedFetch={buildFetch([dto('m1')]) as unknown as ConversationViewProps['authenticatedFetch']}
        />
      </FluentProvider>
    );
    expect(await screen.findByRole('heading', { name: /direct chat/i })).toBeInTheDocument();
  });

  it('the title link is a non-submit button (type="button") so it never submits a host form', async () => {
    renderView({ title: 'Acme v. Beta', regarding: MATTER, onOpenRecord: jest.fn() });
    const link = await screen.findByRole('button', { name: /acme v\. beta, open associated record/i });
    expect(link).toHaveAttribute('type', 'button');
  });
});
