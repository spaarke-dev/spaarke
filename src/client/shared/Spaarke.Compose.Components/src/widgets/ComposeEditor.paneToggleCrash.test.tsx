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
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle } from './ComposeEditor';
import type { ComposeServerProjection } from '../types/compose-contracts';

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

// Regression guard only (task 013): production no longer imports `docxToTipTapHtml` or
// `stampParaIds`. The editor mounts via the `projection` prop below, not this bridge.
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

// Task 013 (F-2 "one reader"): the client-side mammoth reader is DELETED — the editor now
// requires a server `projection` to mount the editable surface at all.
const PANE_TOGGLE_PROJECTION: ComposeServerProjection = {
  status: 'success',
  canEdit: true,
  html: '<p data-paraid="AB12CD34">Loaded document body for the pane-toggle crash guard.</p>',
  warnings: [],
  schemaVersion: 'compose-html-v1',
};

const FINDING = {
  sectionRef: 'Paragraph 1 (p. 1)',
  quotedText: 'Loaded document body for the pane-toggle crash guard.',
  riskLevel: 'High',
  explanation: 'A flagged clause.',
};

function renderEditor(summaryOpen: boolean) {
  const ref = React.createRef<ComposeEditorHandle>();
  const reviewSummary = {
    open: summaryOpen,
    hasFindings: true,
    onToggle: jest.fn(),
    findings: [FINDING],
    placementFailureCount: 0,
  };
  const view = render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture(8)}
          projection={PANE_TOGGLE_PROJECTION}
          // Transient draft, no imports — the crash is import-independent.
          documentRef={{ speDriveItemId: '', fileName: 'Pane-toggle guard.docx' }}
          sessionId="session-pane-toggle"
          reviewSummary={reviewSummary}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  const rerenderWith = (open: boolean): void =>
    view.rerender(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider>
          <ComposeEditor
            ref={ref}
            docxBytes={docxBytesFixture(8)}
            projection={PANE_TOGGLE_PROJECTION}
            documentRef={{ speDriveItemId: '', fileName: 'Pane-toggle guard.docx' }}
            sessionId="session-pane-toggle"
            reviewSummary={{ ...reviewSummary, open }}
          />
        </PaneEventBusProvider>
      </FluentProvider>
    );
  return { ref, rerenderWith };
}

// UAT round-6 #3b: the Comments FAB (the original trigger) was REMOVED. This guard now exercises the
// SAME null↔<div> sibling toggle via the Review Summary panel (round-5 #1 — it renders in the editor's
// top region, conditional on `reviewSummary.open`), which is exactly the kind of conditional-sibling
// change the BubbleMenu-last-child fix protects.
describe('ComposeEditor — P1: toggling an editor pane with a live BubbleMenu does not crash', () => {
  it('opens the Review Summary pane without an insertBefore DOM error', async () => {
    renderEditor(true);
    await screen.findByRole('textbox'); // editor + BubbleMenu mounted
    await waitFor(() => expect(screen.getByTestId('nda-review-summary-panel')).toBeInTheDocument());
    // The editor is still mounted and healthy (no WidgetErrorBoundary swap-out).
    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });

  it('toggles the Review Summary pane open/closed repeatedly without crashing', async () => {
    const { rerenderWith } = renderEditor(true);
    await screen.findByRole('textbox');
    await screen.findByTestId('nda-review-summary-panel');

    // Each toggle is a null↔<div> sibling change that previously could resolve its anchor to the
    // detached BubbleMenu node.
    rerenderWith(false);
    await waitFor(() => expect(screen.queryByTestId('nda-review-summary-panel')).not.toBeInTheDocument());
    rerenderWith(true);
    await screen.findByTestId('nda-review-summary-panel');

    // The editor is still mounted and healthy (no WidgetErrorBoundary swap-out).
    expect(screen.getByRole('textbox')).toBeInTheDocument();
  });
});
