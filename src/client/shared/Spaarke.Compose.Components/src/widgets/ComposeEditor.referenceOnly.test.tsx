/**
 * ComposeEditor.referenceOnly.test.tsx — Wave 6 (UAT-R4, DEF-G) regression guard.
 *
 * Since Wave 3 made "Open in Compose" open the active SOURCE document, a chat-
 * uploaded PDF (or any non-docx buffer) can reach the Compose editor. Editable
 * Compose content is DOCX-ONLY (mammoth parses OOXML/zip); a non-docx buffer
 * previously made mammoth throw and left a SILENT empty `<p></p>` — a confusing
 * dead-end. Wave 6 detects non-docx from the byte signature BEFORE mammoth and
 * renders an explicit reference-only state instead.
 *
 * Contract under test (ComposeEditor.tsx, docxBytes effect + render):
 *  - NON-DOCX buffer (e.g. a `%PDF-1.4` signature with a .pdf fileName) → the
 *    `compose-reference-only` surface renders; NO editable `role="textbox"`.
 *  - DOCX buffer (ZIP `PK\x03\x04` local-file-header magic) → the editable
 *    editor (`role="textbox"`) renders as before; NO reference-only surface.
 *
 * The DOCX bridge is mocked so the import resolves synchronously (jsdom has no
 * mammoth WASM path); the real editor + real effect + real signature check drive
 * the assertion. For the non-docx case mammoth is asserted NOT to be called
 * (detection happens before the import attempt).
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorDocumentRef } from './ComposeEditor';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap.
// This test never dispatches an action, so a stub token is sufficient. Mirrors
// the identical mock in ComposeEditor.dirtyOnMount.test.tsx.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Resolve the mammoth import synchronously — only exercised on the DOCX path.
const docxToTipTapHtml = jest.fn(async () => ({ html: '<p>Loaded document body</p>', messages: [] }));
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: (...args: unknown[]) => docxToTipTapHtml(...(args as [])),
  // task 011: ComposeEditor imports stampParaIds (called after a real docx import);
  // a no-op keeps this reference-only suite's mock complete.
  stampParaIds: jest.fn(),
  // task 027: snapshot capture after stampParaIds — no-op returning an empty map.
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

/** Build an ArrayBuffer from a leading signature followed by filler bytes. */
function bufferFrom(signature: number[], totalLen = 64): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set(signature, 0);
  return buf.buffer;
}

/** `%PDF-1.4` leading bytes — a non-docx buffer. */
const PDF_SIGNATURE = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x34]; // %PDF-1.4
/** ZIP local-file-header magic `PK\x03\x04` — the necessary signature of a real DOCX. */
const DOCX_SIGNATURE = [0x50, 0x4b, 0x03, 0x04];

function renderEditor(docxBytes: ArrayBuffer, documentRef: ComposeEditorDocumentRef | undefined) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={docxBytes} documentRef={documentRef} sessionId="session-defg" />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe('ComposeEditor — Wave 6 (DEF-G) non-docx reference-only guard', () => {
  beforeEach(() => {
    docxToTipTapHtml.mockClear();
  });

  it('NON-DOCX buffer (%PDF signature, .pdf fileName): renders the reference-only state, NOT an editable editor', async () => {
    renderEditor(bufferFrom(PDF_SIGNATURE), { speDriveItemId: 'src-doc-1', fileName: 'evidence.pdf' });

    // The explicit reference-only surface renders...
    const panel = await screen.findByTestId('compose-reference-only');
    expect(panel).toBeInTheDocument();
    expect(panel).toHaveTextContent(/can.t be edited in Compose/i);
    expect(panel).toHaveTextContent(/evidence\.pdf/);

    // ...and there is NO editable editor and no silent empty ProseMirror surface.
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();

    // mammoth was NEVER invoked — detection happened before the import attempt.
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
  });

  it('DOCX buffer (PK\\x03\\x04 zip signature): renders the editable editor, NOT the reference-only state', async () => {
    renderEditor(bufferFrom(DOCX_SIGNATURE), { speDriveItemId: 'matter-doc-9', fileName: 'contract.docx' });

    // The editable editor mounts (effect ran → mammoth import resolved).
    await screen.findByRole('textbox');
    await waitFor(() => expect(docxToTipTapHtml).toHaveBeenCalledTimes(1));

    // The reference-only surface is absent on the valid-docx path.
    expect(screen.queryByTestId('compose-reference-only')).not.toBeInTheDocument();
  });
});
