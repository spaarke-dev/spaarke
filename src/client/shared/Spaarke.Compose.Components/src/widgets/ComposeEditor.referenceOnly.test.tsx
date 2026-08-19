/**
 * ComposeEditor.referenceOnly.test.tsx — Wave 6 (UAT-R4, DEF-G) regression guard.
 *
 * Since Wave 3 made "Open in Compose" open the active SOURCE document, a chat-
 * uploaded PDF (or any non-docx buffer) can reach the Compose editor. Editable
 * Compose content is DOCX-ONLY (the server projection parses OOXML/zip); a
 * non-docx buffer previously made the client-side mammoth reader throw and left
 * a SILENT empty `<p></p>` — a confusing dead-end. Wave 6 detects non-docx from
 * the byte signature BEFORE the mount and renders an explicit reference-only
 * state instead.
 *
 * Contract under test (ComposeEditor.tsx, docxBytes effect + render):
 *  - NON-DOCX buffer (e.g. a `%PDF-1.4` signature with a .pdf fileName) → the
 *    `compose-reference-only` surface renders; NO editable `role="textbox"`
 *    (regardless of whether a `projection` prop is supplied).
 *  - DOCX buffer (ZIP `PK\x03\x04` local-file-header magic) WITH a server
 *    `projection` → the editable editor (`role="textbox"`) renders; NO
 *    reference-only surface. Task 013 (F-2 "one reader"): the client-side
 *    mammoth reader is DELETED, so a DOCX buffer with NO projection now
 *    renders the error/unavailable state — see ComposeEditor.projection.test.tsx
 *    for that contract; this file stays scoped to the non-docx-vs-docx signature
 *    gate.
 *
 * The DOCX bridge is mocked as a regression guard: `docxToTipTapHtml` no longer
 * exists in production, but the mock's presence lets this suite assert it is
 * NEVER called on either path — a spurious call would indicate a reintroduced
 * client-side reader.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorDocumentRef } from './ComposeEditor';
import type { ComposeServerProjection } from '../types/compose-contracts';

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

// Regression guard only (task 013): production no longer imports `docxToTipTapHtml` — this mock
// exists so a spurious call is observable if a client-side reader is ever reintroduced.
const docxToTipTapHtml = jest.fn(async () => ({ html: '<p>Loaded document body</p>', messages: [] }));
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: (...args: unknown[]) => docxToTipTapHtml(...(args as [])),
  stampParaIds: jest.fn(),
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

function renderEditor(
  docxBytes: ArrayBuffer,
  documentRef: ComposeEditorDocumentRef | undefined,
  projection: ComposeServerProjection | null = null,
  sourceFormat: string | null = null
) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          docxBytes={docxBytes}
          projection={projection}
          documentRef={documentRef}
          sourceFormat={sourceFormat}
          sessionId="session-defg"
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

const EDITABLE_PROJECTION: ComposeServerProjection = {
  status: 'success',
  canEdit: true,
  html: '<p data-paraid="AB12CD34">Synthesized from a PDF</p>',
  warnings: [],
  schemaVersion: 'compose-html-v1',
};

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

    // No client-side reader was invoked — detection happened before the mount attempt.
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
  });

  it('DOCX buffer (PK\\x03\\x04 zip signature) WITH a server projection: renders the editable editor, NOT the reference-only state', async () => {
    renderEditor(
      bufferFrom(DOCX_SIGNATURE),
      { speDriveItemId: 'matter-doc-9', fileName: 'contract.docx' },
      {
        status: 'success',
        canEdit: true,
        html: '<p data-paraid="AB12CD34">Loaded document body</p>',
        warnings: [],
        schemaVersion: 'compose-html-v1',
      }
    );

    // The editable editor mounts via the server projection — the DOCX-only signature gate passed.
    await screen.findByRole('textbox');
    await waitFor(() => expect(screen.queryByTestId('compose-reference-only')).not.toBeInTheDocument());
    await waitFor(() => expect(screen.queryByTestId('compose-projection-unavailable')).not.toBeInTheDocument());

    // F-2 "one reader" (task 013): no client-side reader is ever invoked, on either path.
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
  });
});

describe('ComposeEditor — FR-06 PDF import parity (task 051): sourceFormat admits the synthesized docx', () => {
  beforeEach(() => {
    docxToTipTapHtml.mockClear();
  });

  it('PDF-sourced mount (synthesized DOCX bytes + .pdf display name + sourceFormat="pdf"): renders EDITABLE, not reference-only', async () => {
    // The server intake fork (task 050) returns a SYNTHESIZED docx (PK zip) whose display name still ends
    // in .pdf. The editable gate must trust the bytes + the sourceFormat marker and admit it — the whole
    // point of FR-06 parity. Without task 051 the .pdf extension routed this to reference-only.
    renderEditor(
      bufferFrom(DOCX_SIGNATURE),
      { speDriveItemId: '', fileName: 'Corteva NDA (signed).pdf' },
      EDITABLE_PROJECTION,
      'pdf'
    );

    await screen.findByRole('textbox');
    await waitFor(() => expect(screen.queryByTestId('compose-reference-only')).not.toBeInTheDocument());
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
  });

  it('sourceFormat="pdf" still trusts the BYTES: a non-docx buffer under a PDF marker stays reference-only (never editable over non-docx)', async () => {
    // Defensive: sourceFormat==='pdf' skips the .pdf EXTENSION rejection but still requires real docx
    // magic bytes (isDocxBytes) — a raw PDF buffer (server contract violation) must NOT mount editable.
    renderEditor(bufferFrom(PDF_SIGNATURE), { speDriveItemId: '', fileName: 'raw.pdf' }, EDITABLE_PROJECTION, 'pdf');

    const panel = await screen.findByTestId('compose-reference-only');
    expect(panel).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
  });

  it('a .pdf name WITHOUT a sourceFormat marker stays reference-only (un-intakeable PDF regression guard)', async () => {
    // A PDF that the server could NOT intake (DI gate off / parse failure) never earns sourceFormat='pdf';
    // even a coincidental PK-zip under a .pdf name is reference-only — admission is intake-door-gated.
    renderEditor(
      bufferFrom(DOCX_SIGNATURE),
      { speDriveItemId: '', fileName: 'unintakeable.pdf' },
      EDITABLE_PROJECTION,
      null
    );

    const panel = await screen.findByTestId('compose-reference-only');
    expect(panel).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
});
