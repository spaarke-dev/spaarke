/**
 * attachOnCompose.test.tsx — task 042 / FR-20 attach-on-compose.
 *
 * Two capabilities, one policy:
 *   1. A locally-picked file is validated against CHAT-ATTACHMENT-POLICY.md
 *      (25 MB binary cap + MIME allow-list) BEFORE it enters composer state.
 *      A rejected (oversize / disallowed-MIME) file is surfaced with a VISIBLE
 *      error and is never added / never sent (the FR-20 negative criterion).
 *   2. A validated local file resolves to a governed Document via the injected
 *      `onUploadLocalAttachment` and then flows through the EXISTING send path
 *      (`attachmentDocumentIds`) — no second send/upload path (ADR-045).
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import {
  validateLocalAttachmentFile,
  validateState,
  initialState,
  mapStateToSendRequest,
  emailComposerReducer,
  ATTACHMENT_MAX_FILE_BYTES,
} from '../EmailComposer.reducer';
import type { EmailComposerState, IAttachmentItem, IEmailComposerProps } from '../EmailComposer.types';

const MB = 1024 * 1024;
const PDF = 'application/pdf';
const noopFetch = jest.fn();

function composeState(overrides: Partial<EmailComposerState> = {}): EmailComposerState {
  const props: IEmailComposerProps = {
    mode: 'compose',
    mount: 'page',
    authenticatedFetch: noopFetch as unknown as IEmailComposerProps['authenticatedFetch'],
  };
  return { ...initialState(props), ...overrides };
}

function localItem(id: string, sizeBytes: number, mimeType: string | undefined): IAttachmentItem {
  return { id, source: 'local', fileName: `${id}.bin`, sizeBytes, mimeType, selected: true };
}

// jsdom File.size reflects the byte length of the blob parts; build a File of a
// requested logical size cheaply by lying about `size` via a getter override.
function fileOfSize(name: string, sizeBytes: number, type: string): File {
  const f = new File(['x'], name, { type });
  Object.defineProperty(f, 'size', { value: sizeBytes });
  return f;
}

// ---------------------------------------------------------------------------
// 1. Pure policy gate
// ---------------------------------------------------------------------------

describe('validateLocalAttachmentFile — CHAT-ATTACHMENT-POLICY gate', () => {
  it('rejects a file over the 25 MB per-file binary cap', () => {
    const rejection = validateLocalAttachmentFile({ size: ATTACHMENT_MAX_FILE_BYTES + 1, type: PDF, name: 'big.pdf' });
    expect(rejection?.code).toBe('ATTACHMENT_TOO_LARGE');
    expect(rejection?.message).toMatch(/25 MB/);
  });

  it('rejects a disallowed MIME type', () => {
    const rejection = validateLocalAttachmentFile({ size: 1024, type: 'application/x-msdownload', name: 'evil.exe' });
    expect(rejection?.code).toBe('ATTACHMENT_BLOCKED_TYPE');
  });

  it('rejects an empty/unknown MIME type', () => {
    const rejection = validateLocalAttachmentFile({ size: 1024, type: '', name: 'mystery' });
    expect(rejection?.code).toBe('ATTACHMENT_BLOCKED_TYPE');
  });

  it('accepts an allowed type within the cap', () => {
    expect(validateLocalAttachmentFile({ size: 5 * MB, type: PDF, name: 'ok.pdf' })).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// 2. Send-time defense-in-depth (validateState) — scoped to local picks
// ---------------------------------------------------------------------------

describe('validateState — per-local-file cap + MIME (defense in depth)', () => {
  it('raises ATTACHMENT_TOO_LARGE for an oversize LOCAL file', () => {
    const state = composeState({ attachments: [localItem('l1', ATTACHMENT_MAX_FILE_BYTES + 1, PDF)] });
    expect(validateState(state, { forSend: false }).errors.map(e => e.code)).toContain('ATTACHMENT_TOO_LARGE');
  });

  it('raises ATTACHMENT_BLOCKED_TYPE for a disallowed LOCAL file', () => {
    const state = composeState({ attachments: [localItem('l2', 1024, 'application/zip')] });
    expect(validateState(state, { forSend: false }).errors.map(e => e.code)).toContain('ATTACHMENT_BLOCKED_TYPE');
  });

  it('does NOT apply the per-file MIME/size gate to governed (spe/related/wizard) Documents', () => {
    const related: IAttachmentItem = {
      id: 'r1',
      source: 'related',
      fileName: 'sheet.xlsx',
      sizeBytes: 26 * MB, // over the per-file binary cap, but a governed Document is exempt
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      documentId: 'doc-r1',
    };
    const codes = validateState(composeState({ attachments: [related] }), { forSend: false }).errors.map(e => e.code);
    expect(codes).not.toContain('ATTACHMENT_BLOCKED_TYPE');
    // (It still counts toward the aggregate cap, but a single 26 MB item is under the 35 MB total.)
    expect(codes).not.toContain('ATTACHMENT_TOO_LARGE');
  });
});

// ---------------------------------------------------------------------------
// 3. Resolved local pick flows through the EXISTING send path
// ---------------------------------------------------------------------------

describe('RESOLVE_ATTACHMENT_DOCUMENT + mapStateToSendRequest', () => {
  it('patches documentId and includes it in attachmentDocumentIds', () => {
    let state = composeState({ attachments: [localItem('l1', 1024, PDF)] });
    // Before resolution the local file has no documentId → excluded from the payload.
    expect(mapStateToSendRequest(state).attachmentDocumentIds).toEqual([]);

    state = emailComposerReducer(state, { type: 'RESOLVE_ATTACHMENT_DOCUMENT', id: 'l1', documentId: 'doc-99' });
    expect(state.attachments[0].documentId).toBe('doc-99');
    // After resolution it rides the existing send request (no new field).
    expect(mapStateToSendRequest(state).attachmentDocumentIds).toEqual(['doc-99']);
  });
});

// ---------------------------------------------------------------------------
// 4. Composer pick-time rejection is VISIBLE and not added (FR-20 negative)
//    The local-file picker + policy gate now live in EmailComposer (owner UAT
//    2026-07-24 — AttachmentList is display-only); the hidden input carries the
//    aria-label "Choose files from your computer".
// ---------------------------------------------------------------------------

describe('EmailComposer — local pick rejection is visible (FR-20 negative)', () => {
  function renderComposer() {
    const ref = React.createRef<import('../EmailComposer.types').IEmailComposerHandle>();
    const utils = renderWithProviders(
      <EmailComposer
        ref={ref}
        mode="compose"
        mount="page"
        authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
        attachmentSources={[{ kind: 'local' }]}
      />
    );
    const input = utils.container.querySelector('input[type="file"]') as HTMLInputElement;
    return { ref, input };
  }

  it('rejects an oversize file with a visible error and never adds it', () => {
    const { ref, input } = renderComposer();
    const big = fileOfSize('big.pdf', ATTACHMENT_MAX_FILE_BYTES + 1, PDF);
    fireEvent.change(input, { target: { files: [big] } });
    expect(screen.getByRole('alert')).toHaveTextContent(/exceeds the 25 MB per-file limit/);
    expect(ref.current?.getState().attachments ?? []).toHaveLength(0);
  });

  it('rejects a disallowed-MIME file with a visible error and never adds it', () => {
    const { ref, input } = renderComposer();
    const bad = fileOfSize('evil.exe', 1024, 'application/x-msdownload');
    fireEvent.change(input, { target: { files: [bad] } });
    expect(screen.getByRole('alert')).toHaveTextContent(/unsupported type/i);
    expect(ref.current?.getState().attachments ?? []).toHaveLength(0);
  });

  it('accepts an allowed file within the cap (adds it, no error)', () => {
    const { ref, input } = renderComposer();
    const ok = fileOfSize('ok.pdf', 2 * MB, PDF);
    fireEvent.change(input, { target: { files: [ok] } });
    const atts = ref.current?.getState().attachments ?? [];
    expect(atts).toHaveLength(1);
    expect(atts[0]).toMatchObject({ source: 'local', fileName: 'ok.pdf', mimeType: PDF });
  });
});

// ---------------------------------------------------------------------------
// 5. Composer wiring — a picked valid file uploads + becomes send-eligible
// ---------------------------------------------------------------------------

describe('EmailComposer — attach-on-compose upload wiring', () => {
  it('uploads a validated local file and flows it into the existing send path', async () => {
    const onUploadLocalAttachment = jest.fn().mockResolvedValue({ documentId: 'doc-uploaded' });
    const ref = React.createRef<import('../EmailComposer.types').IEmailComposerHandle>();
    const utils = renderWithProviders(
      <EmailComposer
        ref={ref}
        mode="compose"
        mount="page"
        authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
        attachmentSources={[{ kind: 'local' }]}
        onUploadLocalAttachment={onUploadLocalAttachment}
        initialTo={['a@example.com']}
        initialSubject="Hi"
        initialBody="Body"
      />
    );

    const input = utils.container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [fileOfSize('ok.pdf', 2 * MB, PDF)] } });

    expect(onUploadLocalAttachment).toHaveBeenCalledTimes(1);
    await waitFor(() => {
      const atts = ref.current?.getState().attachments ?? [];
      expect(atts.some(a => a.documentId === 'doc-uploaded')).toBe(true);
    });
    // The resolved document rides the EXISTING send request.
    expect(mapStateToSendRequest(ref.current!.getState()).attachmentDocumentIds).toContain('doc-uploaded');
  });
});
