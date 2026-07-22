/**
 * ComposeEditor.projection.test.tsx — Phase-1 mammoth removal
 * (design notes/design-server-side-docx-html-conversion.md).
 *
 * Contract under test (ComposeEditor.tsx docxBytes effect):
 *  - When the host supplies a SERVER PROJECTION (stored-document Load), the editor mounts
 *    `projection.html` DIRECTLY and does NOT run the client mammoth convert or `stampParaIds`
 *    (the two-engine drift that caused the recurring save-abort bug class).
 *  - Fail-closed: a `status:'failed'` / `canEdit:false` projection renders the reference-only
 *    "Open in Word" surface — NEVER a blank editable doc over a non-empty baseline.
 *
 * The DOCX bridge is mocked so a spurious mammoth call would be observable (and is asserted absent).
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorDocumentRef } from './ComposeEditor';
import type { ComposeServerProjection } from '../types/compose-contracts';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// If the projection path is correct, mammoth is NEVER called — so a call here is a real defect.
const docxToTipTapHtml = jest.fn(async () => ({ html: '<p>MAMMOTH fallback body</p>', messages: [] }));
jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: (...args: unknown[]) => docxToTipTapHtml(...(args as [])),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

const DOCX_SIGNATURE = [0x50, 0x4b, 0x03, 0x04]; // PK\x03\x04 — passes the editable-docx signature gate

function docxBuffer(totalLen = 64): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set(DOCX_SIGNATURE, 0);
  return buf.buffer;
}

function projection(overrides: Partial<ComposeServerProjection>): ComposeServerProjection {
  return {
    status: 'success',
    canEdit: true,
    html: '<p data-paraid="AB12CD34">PROJECTION body text</p>',
    warnings: [],
    schemaVersion: 'compose-html-v1',
    ...overrides,
  };
}

function renderEditor(proj: ComposeServerProjection | null, documentRef: ComposeEditorDocumentRef | undefined) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          docxBytes={docxBuffer()}
          projection={proj}
          documentRef={documentRef}
          sessionId="session-projection"
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe('ComposeEditor — Phase-1 server projection mount', () => {
  beforeEach(() => docxToTipTapHtml.mockClear());

  it('editable projection: mounts projection.html directly and NEVER runs the mammoth convert', async () => {
    renderEditor(projection({}), { speDriveItemId: 'matter-doc-1', fileName: 'contract.docx' });

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('PROJECTION body text'));

    // The whole point: no client-side mammoth convert on the stored-Load path.
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
    expect(editor).not.toHaveTextContent('MAMMOTH fallback body');
    expect(screen.queryByTestId('compose-reference-only')).not.toBeInTheDocument();
  });

  it('failed projection: fails closed to the reference-only state, NOT a blank editable doc', async () => {
    renderEditor(projection({ status: 'failed', canEdit: false, html: '' }), {
      speDriveItemId: 'matter-doc-2',
      fileName: 'unreadable.docx',
    });

    const panel = await screen.findByTestId('compose-reference-only');
    expect(panel).toBeInTheDocument();
    expect(panel).toHaveTextContent(/unreadable\.docx/);
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(docxToTipTapHtml).not.toHaveBeenCalled();
  });
});
