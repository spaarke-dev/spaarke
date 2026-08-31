/**
 * ComposeEditor.blankPageEditable.test.tsx — FR-08 (R6 D8 / task 070) regression guard.
 *
 * D8 (R6 defer): "Compose tab → Blank page mounts NON-editable; Open template editable." Both use the
 * same `mountBornInEditor` → `mountDraftHtml` door; only the seed differs (`'<p></p>'` vs a heading+para
 * template). The R6 report suspected an empty-seed-specific reference-only fall-through.
 *
 * FINDING (task 070): the defect does NOT reproduce in the current codebase. The DEF-08 born-in-editor
 * rework routes ALL born-in-editor mounts through `mountDraftHtml`, which sets `docxBytes: null` +
 * `seedHtml: <html>` (see ComposeWorkspace.types.ts) — so the editor's docx-mount branch (the ONLY path
 * that can set the reference-only state) is never reached. With `docxBytes === null`, ComposeEditor's
 * mount effect takes the `initialHtml && initialHtml.length > 0` editable branch for ANY non-empty seed
 * — and `'<p></p>'` (blank) has length 7, exactly like the template. This test LOCKS that: blank mounts
 * editable (parity with the template), and the reference-only routing for genuinely non-editable
 * (non-docx) content is preserved (no regression). No production change was needed — see
 * notes/task-070-notes.md.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorDocumentRef } from './ComposeEditor';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Regression guard (task 013): production no longer imports a client-side docx reader; the mock's
// presence lets a spurious call be observed. Mirrors ComposeEditor.referenceOnly.test.tsx.
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({ html: '<p>x</p>', messages: [] })),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

/** Born-in-editor mount: docxBytes null (per the mountDraftHtml reducer), content from initialHtml. */
function renderBornInEditor(initialHtml: string) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={null} initialHtml={initialHtml} sessionId="session-070" />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

function bufferFrom(signature: number[], totalLen = 64): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set(signature, 0);
  return buf.buffer;
}
const PDF_SIGNATURE = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x34]; // %PDF-1.4 — a non-docx buffer

describe('ComposeEditor — Blank page mounts editable (FR-08 / R6 D8)', () => {
  it('mounts an editable editor for the blank seed "<p></p>" (NOT reference-only)', async () => {
    renderBornInEditor('<p></p>');
    // Editable editor present…
    expect(await screen.findByRole('textbox')).toBeInTheDocument();
    // …and NOT the reference-only surface.
    expect(screen.queryByTestId('compose-reference-only')).not.toBeInTheDocument();
  });

  it('mounts editable for a non-empty template seed too (parity control — the D8 comparison case)', async () => {
    renderBornInEditor('<h1>Heading</h1><p>Body</p>');
    expect(await screen.findByRole('textbox')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-reference-only')).not.toBeInTheDocument();
  });

  it('still routes genuinely non-editable (non-docx) content to reference-only (negative regression guard)', async () => {
    const documentRef: ComposeEditorDocumentRef = { speDriveItemId: 'x', fileName: 'contract.pdf' };
    render(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider>
          <ComposeEditor docxBytes={bufferFrom(PDF_SIGNATURE)} documentRef={documentRef} sessionId="session-070-neg" />
        </PaneEventBusProvider>
      </FluentProvider>
    );
    expect(await screen.findByTestId('compose-reference-only')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('textbox')).not.toBeInTheDocument());
  });
});
