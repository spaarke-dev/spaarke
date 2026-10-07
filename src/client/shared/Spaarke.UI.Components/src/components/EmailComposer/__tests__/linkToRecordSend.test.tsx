/**
 * linkToRecordSend.test.tsx — spaarkeai-word-add-in-r1 task 098 (owner decision 2026-10-05).
 *
 * Send-level behaviour of the composer's **Link** toggle: a linked document puts the Spaarke record link in the
 * sent body (and the share-link route is never called); when the host cannot produce a link the send is refused
 * with a message on the attachments field instead of silently emailing a missing/dead link.
 * Mocking is at the network boundary only (`authenticatedFetch`).
 */
import * as React from 'react';
import { act } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerHandle, IEmailComposerProps, IAttachmentItem } from '../EmailComposer.types';

const DOC = '11111111-2222-3333-4444-555555555555';
const RECORD_LINK = `https://contoso.crm.dynamics.com/main.aspx?appname=sprk_MatterManagement&pagetype=entityrecord&etn=sprk_document&id=${DOC}`;

function okFetch() {
  return jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => ({ communicationId: 'comm-1' }),
  } as unknown as Response);
}

const linkedDoc: IAttachmentItem = {
  id: 'a1',
  source: 'related',
  fileName: 'brief.pdf',
  sizeBytes: 100,
  documentId: DOC,
  selected: false,
  linkSelected: true,
  linkUrl: 'https://tenant.sharepoint.com/contentstorage/CSP_x/brief.pdf', // an internal SPE URL
};

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  const ref = React.createRef<IEmailComposerHandle>();
  const authenticatedFetch = okFetch();
  renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="compose"
      mount="inline"
      authenticatedFetch={authenticatedFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      initialTo={['recipient@example.com']}
      initialSubject="Subject"
      initialBody="Body"
      initialAttachments={[linkedDoc]}
      {...overrides}
    />
  );
  return { ref, authenticatedFetch };
}

describe('Link to Spaarke record — send', () => {
  it('puts the Spaarke record link in the sent body and never calls /share-link', async () => {
    const { ref, authenticatedFetch } = renderComposer({ onResolveShareLink: async () => RECORD_LINK });

    await act(async () => {
      await ref.current!.send();
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(String(url)).toContain('/api/communications/send');
    expect(String(url)).not.toContain('share-link');
    const sent = JSON.parse(init.body as string) as { body: string };
    expect(sent.body).toContain(RECORD_LINK.replace(/&/g, '&amp;'));
    expect(sent.body).not.toContain('CSP_x');
  });

  it('refuses the send, with a message on the attachments field, when no record link can be built', async () => {
    const { ref, authenticatedFetch } = renderComposer({ onResolveShareLink: async () => null });

    await act(async () => {
      await expect(ref.current!.send()).rejects.toThrow(/brief\.pdf/);
    });

    // Nothing was sent — in particular not the internal SPE URL.
    expect(authenticatedFetch).not.toHaveBeenCalled();
    const state = ref.current!.getState();
    expect(state.validation.errors.map(e => e.code)).toContain('ATTACHMENT_LINK_UNAVAILABLE');
    expect(state.validation.errors.find(e => e.code === 'ATTACHMENT_LINK_UNAVAILABLE')?.field).toBe('attachments');
  });
});
