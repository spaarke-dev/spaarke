/**
 * ConversationView.scrollToMessage.test.tsx (task 023, FR-05)
 *
 * Coverage for the minimal + additive imperative handle exposed via
 * `React.forwardRef` + `useImperativeHandle`: `scrollToMessage(id)` scrolls the
 * matching bubble into view AND flashes a transient highlight that auto-clears.
 *
 * Per ADR-038/ADR-028 a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import) — same pattern as the
 * sibling `ConversationView.test.tsx`.
 */
import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ConversationView } from '../ConversationView';
import type { ConversationViewHandle, ConversationViewProps } from '../ConversationView.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_1 = '11111111-1111-1111-1111-111111111111';
const USER_2 = '22222222-2222-2222-2222-222222222222';

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000000, // PlainText
    // Message-type so the messages render as chat bubbles (role="article").
    // The scrollToMessage anchor wrapper is child-agnostic (it wraps email
    // blocks and bubbles identically, task 021 / FR-04) — this suite exercises
    // the imperative handle, for which the bubble is the simpler fixture.
    communicationType: 100000004, // Message
    from: 'someone@example.com',
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

function renderWithRef(messages: IThreadMessageDto[]) {
  const ref = React.createRef<ConversationViewHandle>();
  const props: ConversationViewProps = {
    threadId: 't1',
    currentUserSystemUserId: USER_1,
    authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
  };
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <ConversationView ref={ref} {...props} />
    </FluentProvider>
  );
  return { ref, ...utils };
}

describe('ConversationView.scrollToMessage (FR-05 open→pin)', () => {
  it('scrolls the matching bubble into view and applies a transient highlight that clears', async () => {
    const scrollSpy = jest.spyOn(Element.prototype, 'scrollIntoView').mockImplementation(function () {
      /* jsdom noop */
    });

    const { ref, container } = renderWithRef([
      dto('a', { sentBy: USER_1 }),
      dto('target', { sentBy: USER_2, sentByName: 'Colleague' }),
    ]);

    // Wait for both bubbles (and their anchors) to render.
    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2));
    const targetAnchor = container.querySelector('[data-message-id="target"]') as HTMLElement;
    const otherAnchor = container.querySelector('[data-message-id="a"]') as HTMLElement;
    expect(targetAnchor).toBeTruthy();

    scrollSpy.mockClear();

    // Drive the imperative handle.
    act(() => {
      ref.current!.scrollToMessage('target');
    });

    // Scrolled the RIGHT anchor into view.
    expect(scrollSpy).toHaveBeenCalledTimes(1);
    expect(scrollSpy.mock.instances[0]).toBe(targetAnchor);

    // Highlight applied to the target only.
    expect(targetAnchor).toHaveAttribute('data-highlighted', 'true');
    expect(otherAnchor).not.toHaveAttribute('data-highlighted');

    // Highlight auto-clears after PIN_HIGHLIGHT_MS (~1.5s).
    await waitFor(() => expect(targetAnchor).not.toHaveAttribute('data-highlighted'), { timeout: 2500 });

    scrollSpy.mockRestore();
  });

  it('is a no-op (never throws, no scroll, no stray highlight) when the id is not currently rendered', async () => {
    const scrollSpy = jest.spyOn(Element.prototype, 'scrollIntoView').mockImplementation(function () {
      /* jsdom noop */
    });

    const { ref, container } = renderWithRef([dto('a', { sentBy: USER_1 })]);
    await screen.findByRole('article');

    scrollSpy.mockClear();
    expect(() => act(() => ref.current!.scrollToMessage('does-not-exist'))).not.toThrow();

    // A miss must not scroll anything and must not highlight the wrong bubble.
    expect(scrollSpy).not.toHaveBeenCalled();
    expect(container.querySelector('[data-highlighted="true"]')).toBeNull();

    scrollSpy.mockRestore();
  });
});
