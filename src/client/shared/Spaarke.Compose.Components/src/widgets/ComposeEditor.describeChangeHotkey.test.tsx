/**
 * ComposeEditor.describeChangeHotkey.test.tsx — FR-04 (task 060, UC-5) render-level DoD coverage for
 * the Ctrl+Space / Ctrl+/ "Describe a change" hotkey, exercised against the REAL ComposeEditor (real
 * TipTap editor, real instruction Dialog) — not a mock harness.
 *
 * CI-ONLY suite group: needs @spaarke/auth + @spaarke/ai-widgets/events resolution (standalone-jest
 * cannot load them by design — see the project's monorepo test note). The IME-guard negative case is
 * proven WITHOUT a mount in the standalone `composeHotkeys.test.ts`; this file proves the end-to-end
 * "collapsed caret → dialog opens → dispatch carries the paragraph slots" behavior.
 *
 * Covers acceptance criteria:
 *  1. Collapsed cursor (no selection) + Ctrl+Space ⇒ "Describe a change" opens for the current
 *     caret/paragraph, and submitting dispatches the compose-rewrite-instruction Action scoped to
 *     the enclosing paragraph (routed to the document session).
 *  3. Ctrl+/ is wired as the fallback (also opens the dialog).
 */

import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor } from './ComposeEditor';
import {
  registerComposeAiToolbarAction,
  __resetComposeAiToolbarActionsForTests,
  type ComposeActionEnqueue,
} from './ComposeAiToolbar';
import type { Editor } from '@tiptap/react';

// ComposeAiToolbar's useAuth() throws outside a real initAuth() bootstrap — mirror the toolbar tests.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

/** Wire the compose-rewrite-instruction Action with a real bindingId so the caret runner dispatches. */
function wireDescribeChangeBinding(): void {
  registerComposeAiToolbarAction({
    id: 'compose-rewrite-instruction',
    label: 'Describe a change',
    tooltip: 'Describe a change in your own words.',
    bindingId: 'test-binding-rewrite',
    placement: 'primary',
    materializesInEditor: true,
    surfaces: ['selection', 'review-note'],
    inputPrompt: 'Describe the change you’d like to make to this clause.',
  });
}

function renderEditor(enqueue: ComposeActionEnqueue) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={null} sessionId="session-060" enqueueComposeAction={enqueue} />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

function getEditorInstance(container: HTMLElement): Editor {
  const dom = container.querySelector('.ProseMirror') as unknown as { editor?: Editor } | null;
  if (!dom?.editor) throw new Error('TipTap editor instance not found on ProseMirror DOM node');
  return dom.editor;
}

function getEditorDom(container: HTMLElement): HTMLElement {
  const dom = container.querySelector('.ProseMirror') as HTMLElement | null;
  if (!dom) throw new Error('ProseMirror editor DOM not found — editor did not mount');
  return dom;
}

/** Seed a paragraph and collapse the caret inside it (no selection). */
function seedCollapsedCaret(editor: Editor): void {
  act(() => {
    editor.commands.setContent('<p>Hello world here</p>');
    editor.commands.setTextSelection({ from: 3, to: 3 });
  });
  expect(editor.state.selection.from).toBe(editor.state.selection.to); // truly collapsed
}

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
});

describe('ComposeEditor — Ctrl+Space "Describe a change" at the caret (FR-04)', () => {
  it('opens the instruction dialog for a COLLAPSED caret and dispatches the paragraph-scoped Action on submit', async () => {
    wireDescribeChangeBinding();
    const enqueue = jest.fn(async () => ({})) as unknown as jest.MockedFunction<ComposeActionEnqueue>;
    const { container } = renderEditor(enqueue);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);
    const editorDom = getEditorDom(container);
    seedCollapsedCaret(editor);

    // No dialog yet.
    expect(screen.queryByTestId('compose-instruction-dialog')).not.toBeInTheDocument();

    // Ctrl+Space with a collapsed caret opens the shipped "Describe a change" dialog.
    fireEvent.keyDown(editorDom, { ctrlKey: true, code: 'Space', key: ' ' });
    expect(await screen.findByTestId('compose-instruction-dialog')).toBeInTheDocument();

    // Type an instruction + submit (Ctrl+Enter, per the dialog's own submit affordance).
    const input = screen.getByTestId('compose-instruction-input');
    fireEvent.change(input, { target: { value: 'make it mutual' } });
    fireEvent.keyDown(input, { ctrlKey: true, key: 'Enter' });

    // The Action dispatched, scoped to the enclosing paragraph ("Hello world here" = pos 1..17),
    // routed to the document session (inline redline), carrying the free-text instruction.
    await act(async () => {
      await Promise.resolve();
    });
    expect(enqueue).toHaveBeenCalledTimes(1);
    const request = enqueue.mock.calls[0][0];
    expect(request.bindingId).toBe('test-binding-rewrite');
    expect(request.documentSessionId).toBe('session-060');
    expect(request.args?.slots).toEqual(
      expect.objectContaining({
        selectionText: 'Hello world here',
        selectionAnchorStart: 1,
        selectionAnchorEnd: 17,
        instruction: 'make it mutual',
        sessionId: 'session-060',
      })
    );
  });

  it('Ctrl+/ (fallback binding) also opens the dialog for a collapsed caret', async () => {
    wireDescribeChangeBinding();
    const enqueue = jest.fn(async () => ({})) as unknown as jest.MockedFunction<ComposeActionEnqueue>;
    const { container } = renderEditor(enqueue);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);
    const editorDom = getEditorDom(container);
    seedCollapsedCaret(editor);

    fireEvent.keyDown(editorDom, { ctrlKey: true, key: '/', code: 'Slash' });
    expect(await screen.findByTestId('compose-instruction-dialog')).toBeInTheDocument();
  });

  it('cancelling the dialog dispatches nothing (abort path)', async () => {
    wireDescribeChangeBinding();
    const enqueue = jest.fn(async () => ({})) as unknown as jest.MockedFunction<ComposeActionEnqueue>;
    const { container } = renderEditor(enqueue);
    await screen.findByRole('textbox');
    const editor = getEditorInstance(container);
    const editorDom = getEditorDom(container);
    seedCollapsedCaret(editor);

    fireEvent.keyDown(editorDom, { ctrlKey: true, code: 'Space', key: ' ' });
    expect(await screen.findByTestId('compose-instruction-dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('compose-instruction-cancel'));

    await act(async () => {
      await Promise.resolve();
    });
    expect(enqueue).not.toHaveBeenCalled();
  });
});
