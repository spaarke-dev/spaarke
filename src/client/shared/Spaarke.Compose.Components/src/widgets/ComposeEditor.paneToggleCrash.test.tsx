/**
 * ComposeEditor.paneToggleCrash.test.tsx — UAT 2026-07-19 P1 regression guard.
 *
 * Proves that toggling the Styles ("A") and Comments FABs on the REAL ComposeEditor
 * (with the REAL TipTap <BubbleMenu> mounted) no longer crashes the widget.
 *
 * THE BUG (import-independent, latent since tasks 043/044): TipTap's BubbleMenu plugin
 * calls `this.element.remove()` on mount, detaching its wrapper <div> from the DOM while
 * React's fiber still records it as a live child. The <BubbleMenu> used to render as a
 * sibling BETWEEN the toggleable Comments/Styles panes (which `return null` when closed)
 * and the always-mounted editor scroll region. Clicking a FAB toggled a pane null→<div>;
 * React's getHostSibling resolved the insert-anchor to the DETACHED BubbleMenu node and
 * called `container.insertBefore(paneDiv, detachedBubbleDiv)` — which throws
 * "Failed to execute 'insertBefore' … not a child of this node" (jsdom words it
 * "NotFoundError: The child can not be found in the parent."), tripping the
 * WidgetErrorBoundary ("Compose failed to load").
 *
 * THE FIX (ComposeEditor.tsx): the <BubbleMenu> was relocated to be the LAST child of the
 * editor container, so every conditional sibling anchors on the always-mounted
 * `editorScrollWrap` instead of the tippy-detached node.
 *
 * This scenario was previously UNTESTED — the panes were only exercised in isolation
 * (ComposeStylesPane.test.tsx / ComposeCommentThread.test.tsx), never through the real
 * ComposeEditor with a live BubbleMenu, which is why the crash reached UAT. The crash
 * reproduces with NO imports and NO docx, proving it is BubbleMenu/layout-driven, not
 * import-driven — so this test deliberately mounts a transient draft with zero imports.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle } from './ComposeEditor';

// ComposeAiToolbar (rendered inside the BubbleMenu) calls useAuth(); stub it — this test
// never dispatches an action. Mirrors ComposeEditor.dirtyOnMount.test.tsx.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Resolve the mammoth import synchronously (jsdom has no mammoth WASM path).
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({
    html: '<p>Loaded document body for the pane-toggle crash guard.</p>',
    messages: [],
  })),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

// Valid DOCX byte signature (PK\x03\x04) so the editor mounts the editable surface
// rather than the reference-only state (Wave 6 DEF-G gate).
function docxBytesFixture(totalLen = 8): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0);
  return buf.buffer;
}

function renderEditor() {
  const ref = React.createRef<ComposeEditorHandle>();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture(8)}
          // Transient draft, no imports — the crash is import-independent.
          documentRef={{ speDriveItemId: '', fileName: 'Pane-toggle guard.docx' }}
          sessionId="session-pane-toggle"
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return ref;
}

describe('ComposeEditor — P1: toggling Styles/Comments FABs with a live BubbleMenu does not crash', () => {
  it('opens the Styles pane without an insertBefore DOM error', async () => {
    renderEditor();
    await screen.findByRole('textbox'); // editor + BubbleMenu mounted

    // Before the fix, this click threw during React commit
    // ("NotFoundError: The child can not be found in the parent.").
    fireEvent.click(screen.getByTestId('compose-styles-toggle'));

    await waitFor(() => expect(screen.getByTestId('compose-styles-pane')).toBeInTheDocument());
  });

  it('opens the Comments pane without an insertBefore DOM error', async () => {
    renderEditor();
    await screen.findByRole('textbox');

    fireEvent.click(screen.getByTestId('compose-comments-toggle'));

    await waitFor(() => expect(screen.getByTestId('compose-comment-thread-panel')).toBeInTheDocument());
  });

  it('toggles both panes open/closed repeatedly without crashing', async () => {
    renderEditor();
    await screen.findByRole('textbox');

    const styles = screen.getByTestId('compose-styles-toggle');
    const comments = screen.getByTestId('compose-comments-toggle');

    // Open styles, open comments, close styles, close comments — each toggle is a
    // null↔<div> sibling change that previously could resolve its anchor to the
    // detached BubbleMenu node.
    fireEvent.click(styles);
    await screen.findByTestId('compose-styles-pane');
    fireEvent.click(comments);
    await screen.findByTestId('compose-comment-thread-panel');
    fireEvent.click(styles);
    await waitFor(() => expect(screen.queryByTestId('compose-styles-pane')).not.toBeInTheDocument());
    fireEvent.click(comments);
    await waitFor(() => expect(screen.queryByTestId('compose-comment-thread-panel')).not.toBeInTheDocument());

    // The editor is still mounted and healthy (no WidgetErrorBoundary swap-out).
    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });
});
