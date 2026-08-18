/**
 * ComposeEditor.focusChatHotkey.test.tsx — FR-05 (task 061, UC-6) render-level coverage for the
 * Ctrl+Shift+Space "focus the Assistant chat" hotkey, exercised against the REAL ComposeEditor (real
 * TipTap editor) with a real PaneEventBus subscriber recording the cross-pane signal.
 *
 * CI-ONLY suite group: needs @spaarke/auth + @spaarke/ai-widgets/events resolution (standalone-jest
 * cannot load them by design). The IME-guard + Shift-disambiguation negatives are proven WITHOUT a
 * mount in the standalone `composeHotkeys.test.ts`; this file proves the end-to-end editor emit.
 *
 * Covers acceptance criteria:
 *  1. Ctrl+Shift+Space from the editor dispatches a `conversation.focus_chat_input` PaneEventBus event
 *     (the transport ConversationPane relays to SprkChat's focusInput()).
 *  3. The editor advertises the shortcut via `aria-keyshortcuts` (the discoverability hint).
 *  + Negative: plain Ctrl+Space (no Shift) does NOT emit focus_chat_input (disambiguation).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider, usePaneEvent } from '@spaarke/ai-widgets/events';
import { ComposeEditor } from './ComposeEditor';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

/** Records every `conversation`-channel event so the test can assert the focus signal. */
function ConversationRecorder({ sink }: { sink: Array<{ type: string; sessionId?: string }> }): null {
  usePaneEvent('conversation', event => {
    sink.push({ type: event.type, sessionId: event.sessionId });
  });
  return null;
}

function renderEditor(sink: Array<{ type: string; sessionId?: string }>) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ConversationRecorder sink={sink} />
        <ComposeEditor docxBytes={null} sessionId="session-061" />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

function getEditorDom(container: HTMLElement): HTMLElement {
  const dom = container.querySelector('.ProseMirror') as HTMLElement | null;
  if (!dom) throw new Error('ProseMirror editor DOM not found — editor did not mount');
  return dom;
}

describe('ComposeEditor — Ctrl+Shift+Space focuses the Assistant chat (FR-05)', () => {
  it('dispatches a conversation.focus_chat_input event carrying the sessionId', async () => {
    const events: Array<{ type: string; sessionId?: string }> = [];
    const { container } = renderEditor(events);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.keyDown(editorDom, { ctrlKey: true, shiftKey: true, code: 'Space', key: ' ' });

    const focusEvents = events.filter(e => e.type === 'focus_chat_input');
    expect(focusEvents).toHaveLength(1);
    expect(focusEvents[0].sessionId).toBe('session-061');
  });

  it('advertises the shortcut via aria-keyshortcuts on the editor textbox (discoverability hint)', async () => {
    const events: Array<{ type: string; sessionId?: string }> = [];
    const { container } = renderEditor(events);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    expect(editorDom.getAttribute('aria-keyshortcuts')).toBe('Control+Space Control+Shift+Space');
  });

  it('does NOT emit focus_chat_input for plain Ctrl+Space (Shift disambiguation)', async () => {
    const events: Array<{ type: string; sessionId?: string }> = [];
    const { container } = renderEditor(events);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.keyDown(editorDom, { ctrlKey: true, code: 'Space', key: ' ' });

    expect(events.filter(e => e.type === 'focus_chat_input')).toHaveLength(0);
  });

  it('does NOT emit focus_chat_input during an IME composition (isComposing guard)', async () => {
    const events: Array<{ type: string; sessionId?: string }> = [];
    const { container } = renderEditor(events);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.keyDown(editorDom, { ctrlKey: true, shiftKey: true, code: 'Space', key: ' ', isComposing: true });

    expect(events.filter(e => e.type === 'focus_chat_input')).toHaveLength(0);
  });
});
