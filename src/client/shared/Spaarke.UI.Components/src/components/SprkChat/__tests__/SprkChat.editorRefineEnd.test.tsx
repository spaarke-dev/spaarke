/**
 * SprkChat - editor refine always ends the editor's stream (#1906 review, F5)
 *
 * The Analysis Workspace's diff review stays in its streaming state until it
 * receives document_stream_end. handleEditorRefine emitted it only on a `done`
 * line or in its catch, so a refine stream that closed without `done` left the
 * editor waiting forever while the chat said "Revision sent to editor".
 */

import * as React from 'react';
import { screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SprkChat } from '../SprkChat';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

import { TextEncoder, TextDecoder } from 'util';
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

class MockBridge {
  private handlers = new Map<string, Array<(payload: unknown) => void>>();
  readonly emitted: Array<{ event: string; payload: any }> = [];
  isDisconnected = false;

  subscribe(event: string, handler: (payload: unknown) => void): () => void {
    const list = this.handlers.get(event) ?? [];
    list.push(handler);
    this.handlers.set(event, list);
    return () => {
      const current = this.handlers.get(event) ?? [];
      const idx = current.indexOf(handler);
      if (idx >= 0) current.splice(idx, 1);
    };
  }

  emit(event: string, payload: unknown): void {
    this.emitted.push({ event, payload });
    for (const handler of this.handlers.get(event) ?? []) handler(payload);
  }

  disconnect(): void {
    this.isDisconnected = true;
    this.handlers.clear();
  }
}

function jsonResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    json: jest.fn().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

/** A refine stream that sends one token and closes WITHOUT a `done` event. */
function refineResponseWithoutDone(): Response {
  const chunk = new TextEncoder().encode(`data: ${JSON.stringify({ type: 'token', content: 'Simpler.' })}\n\n`);
  let delivered = false;
  return {
    ok: true,
    status: 200,
    body: {
      getReader: () => ({
        read: async () => {
          if (delivered) return { done: true, value: undefined };
          delivered = true;
          return { done: false, value: chunk };
        },
        releaseLock: () => {},
      }),
    },
    headers: new Headers(),
  } as unknown as Response;
}

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

describe('SprkChat - editor refine stream end', () => {
  beforeEach(() => {
    mockFetch.mockReset();
    mockFetch.mockImplementation((url: string) => {
      if (url.includes('/refine')) return Promise.resolve(refineResponseWithoutDone());
      if (url.includes('/playbooks')) return Promise.resolve(jsonResponse({ playbooks: [] }));
      if (url.includes('/sessions')) {
        return Promise.resolve(jsonResponse({ sessionId: 'session-1', createdAt: '2026-10-15T00:00:00Z' }));
      }
      return Promise.resolve(jsonResponse({ commands: [] }));
    });
  });

  it('refineStreamClosesWithoutDone_EmitsDocumentStreamEndOnce', async () => {
    const bridge = new MockBridge();
    const user = userEvent.setup();
    const authenticatedFetch = (url: string, init?: RequestInit) => mockFetch(url, init);

    await act(async () => {
      renderWithProviders(
        <SprkChat
          playbookId="pb-1"
          apiBaseUrl="https://api.example.com"
          authenticatedFetch={authenticatedFetch}
          getAccessToken={() => Promise.resolve('header.e30.sig')}
          bridge={bridge as any}
        />
      );
    });
    await waitFor(() => {
      const textarea = screen.getByTestId('chat-input-textarea');
      expect(textarea.querySelector('textarea') || textarea).not.toBeDisabled();
    });

    act(() => {
      bridge.emit('selection_changed', {
        text: 'Complex text to simplify',
        startOffset: 0,
        endOffset: 24,
        context: JSON.stringify({ selectedHtml: '<p>Complex text to simplify</p>', source: 'analysis-editor' }),
      });
    });
    await user.click(await screen.findByTestId('quick-action-simplify'));

    await waitFor(() => expect(screen.getByText('Revision sent to editor for review.')).toBeInTheDocument());
    const ends = bridge.emitted.filter(e => e.event === 'document_stream_end');
    expect(ends).toHaveLength(1);
    expect(ends[0].payload).toMatchObject({ cancelled: false, totalTokens: 1 });
  });
});
