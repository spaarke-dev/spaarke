/**
 * The MCP email host (spaarkeai-mcp-bff-api-r2 task 011): attachment picker via spaarke_list_documents, Send via
 * spaarke_send_email with one idempotency key per compose session, tool refusals through the engine's failure path,
 * and no Xrm. A fake McpToolCaller stands in for the transport.
 */
import * as xrmContext from '../../../utils/xrmContext';
import { SendCommunicationError, sendCommunication } from '../../../services/communicationApi';
import { describeSendFailure, mayHaveBeenSent, toSendCommunicationError } from '../describeSendFailure';
import {
  McpHostRefusal,
  createMcpEmailComposeHandlers,
  type McpDocumentListOutcome,
} from '../createMcpEmailComposeHandlers';
import type { McpToolCaller, McpToolCallResult } from '../mcpToolCaller';

const MATTER = { id: 'matter-1', name: 'Smith v. Jones' };

function fakeCaller(
  handler: (name: string, args: Record<string, unknown>) => McpToolCallResult | Promise<McpToolCallResult>
) {
  const callTool = jest.fn(async (name: string, args: Record<string, unknown>) => handler(name, args));
  return { callTool } as McpToolCaller & { callTool: jest.Mock };
}

const sendOptions = {
  to: ['a@example.com'],
  cc: ['c@example.com'],
  subject: 'Hello',
  body: '<p>Hi</p>',
  bodyFormat: 'html' as const,
  attachmentDocumentIds: ['doc-1', 'doc-2'],
  associations: [{ entityType: 'sprk_matter', entityId: 'matter-1' }],
};

/** A tool failure in the real server shape (McpToolResults.cs FromError). */
function serverError(body: { reasonCode: string; message: string; status: number; correlationId?: string }) {
  return {
    isError: true,
    content: [{ type: 'text', text: body.message }],
    structuredContent: body,
  } satisfies McpToolCallResult;
}

describe('createMcpEmailComposeHandlers - Send', () => {
  it('calls spaarke_send_email once with the recipients, subject, body, matter, documents and an idempotency key', async () => {
    const caller = fakeCaller(() => ({ structuredContent: { communicationId: 'comm-9' } }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    const keyBeforeSend = host.idempotencyKey;

    const result = await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch });

    expect(result).toEqual({ communicationId: 'comm-9' });
    expect(caller.callTool).toHaveBeenCalledTimes(1);
    const [name, args] = caller.callTool.mock.calls[0];
    expect(name).toBe('spaarke_send_email');
    expect(args).toMatchObject({
      to: ['a@example.com'],
      cc: ['c@example.com'],
      subject: 'Hello',
      body: '<p>Hi</p>',
      regardingEntityType: 'sprk_matter',
      regardingId: 'matter-1',
      attachmentDocumentIds: ['doc-1', 'doc-2'],
    });
    expect(typeof args.idempotencyKey).toBe('string');
    expect(args.idempotencyKey).toBe(keyBeforeSend);
    expect(host.associations).toEqual([
      { entityType: 'sprk_matter', entityId: 'matter-1', entityName: 'Smith v. Jones' },
    ]);
  });

  it('reuses the same idempotency key when Send is pressed again after a failure', async () => {
    let calls = 0;
    const caller = fakeCaller(() =>
      ++calls === 1
        ? serverError({ reasonCode: 'sdap.communication.sender_disabled', message: 'Sender disabled.', status: 403 })
        : { structuredContent: { communicationId: 'comm-2' } }
    );
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    await expect(
      sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch })
    ).rejects.toBeInstanceOf(SendCommunicationError);
    await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch });

    const keys = caller.callTool.mock.calls.map(c => c[1].idempotencyKey);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
  });

  it('uses a supplied idempotency key, and distinct compose sessions get distinct keys', () => {
    const caller = fakeCaller(() => ({}));
    expect(createMcpEmailComposeHandlers({ caller, regarding: MATTER, idempotencyKey: 'k-1' }).idempotencyKey).toBe(
      'k-1'
    );
    const a = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    const b = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    expect(a.idempotencyKey).not.toBe(b.idempotencyKey);
  });

  it('surfaces a 422 refusal through the send-failure path with the tool reasonCode, and does not retry', async () => {
    const caller = fakeCaller(() =>
      serverError({
        reasonCode: 'sdap.communication.recipient_refused',
        message: 'Recipient domain is not allowed.',
        status: 422,
        correlationId: 'corr-422',
      })
    );
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    const err = (await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(
      e => e
    )) as SendCommunicationError;

    expect(err).toBeInstanceOf(SendCommunicationError);
    expect(err.status).toBe(422);
    expect(err.code).toBe('sdap.communication.recipient_refused');
    expect(err.correlationId).toBe('corr-422');
    // The engine normalises with toSendCommunicationError and describes with describeSendFailure.
    const described = describeSendFailure(toSendCommunicationError(err));
    expect(described.message).toContain('Recipient domain is not allowed.');
    expect(described.reference).toBe('corr-422');
    expect(caller.callTool).toHaveBeenCalledTimes(1);
  });

  it('keeps a 403 as a 403 with its code and reference (permission wording, nothing "may have been sent")', async () => {
    const caller = fakeCaller(() =>
      serverError({
        reasonCode: 'sdap.communication.matter_access_denied',
        message: 'You do not have access to this matter.',
        status: 403,
        correlationId: 'corr-403',
      })
    );
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    const err = (await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(
      e => e
    )) as SendCommunicationError;

    expect(err.status).toBe(403);
    expect(err.code).toBe('sdap.communication.matter_access_denied');
    expect(err.correlationId).toBe('corr-403');
    expect(mayHaveBeenSent(err)).toBe(false);
    const described = describeSendFailure(err);
    expect(described.message).toContain("You don't have permission to send this email.");
    expect(described.message).toContain('You do not have access to this matter.');
    expect(described.reference).toBe('corr-403');
  });

  it('keeps a 500 as a 500: the email MAY have been sent, so nothing it carried is deleted', async () => {
    const caller = fakeCaller(() =>
      serverError({
        reasonCode: 'sdap.communication.send_failed',
        message: 'The send failed after the mailbox accepted it.',
        status: 500,
        correlationId: 'corr-500',
      })
    );
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    const err = (await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(
      e => e
    )) as SendCommunicationError;

    expect(err.status).toBe(500);
    expect(err.code).toBe('sdap.communication.send_failed');
    expect(mayHaveBeenSent(err)).toBe(true);
    expect(describeSendFailure(err).reference).toBe('corr-500');
  });

  it('defaults to 422 only when the structured result names no status', async () => {
    const caller = fakeCaller(() => ({
      isError: true,
      structuredContent: { reasonCode: 'sdap.communication.x', message: 'No status here.' },
    }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    const err = (await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(
      e => e
    )) as SendCommunicationError;
    expect(err.status).toBe(422);
    expect(err.code).toBe('sdap.communication.x');
  });

  it('treats an isError with no structured result as an unknown outcome: status 0, NETWORK, may have been sent', async () => {
    const caller = fakeCaller(() => ({
      isError: true,
      content: [{ type: 'text', text: 'An error occurred invoking spaarke_send_email.' }],
    }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    const err = (await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(
      e => e
    )) as SendCommunicationError;

    expect(err).toBeInstanceOf(SendCommunicationError);
    expect(err.status).toBe(0);
    expect(err.code).toBe('NETWORK');
    expect(mayHaveBeenSent(err)).toBe(true);
    expect(describeSendFailure(err).title).toBe('Email may not have been sent');
  });

  it('treats a rejected callTool the same way (NETWORK, may have been sent), with no retry', async () => {
    const caller = fakeCaller(() => {
      throw new TypeError('Failed to fetch');
    });
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    const err = (await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(
      e => e
    )) as SendCommunicationError;

    expect(err).toBeInstanceOf(SendCommunicationError);
    expect(err.status).toBe(0);
    expect(err.code).toBe('NETWORK');
    expect(mayHaveBeenSent(err)).toBe(true);
    expect(caller.callTool).toHaveBeenCalledTimes(1);
  });

  it('rotates the key after a Send that returned a communicationId, and keeps it after a "sent, unconfirmed" success', async () => {
    let calls = 0;
    const caller = fakeCaller(() => (++calls === 1 ? {} : { structuredContent: { communicationId: 'c' } }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    const first = host.idempotencyKey;

    await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(() => undefined); // no id
    expect(host.idempotencyKey).toBe(first);
    await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }); // id: key spent
    expect(host.idempotencyKey).not.toBe(first);
    await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch });

    const keys = caller.callTool.mock.calls.map(c => c[1].idempotencyKey);
    expect(keys[0]).toBe(first);
    expect(keys[1]).toBe(first);
    expect(keys[2]).not.toBe(first);
  });

  describe('backstop: what the tool cannot send is refused, never dropped', () => {
    async function refused(options: Record<string, unknown>) {
      const caller = fakeCaller(() => ({ structuredContent: { communicationId: 'c' } }));
      const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
      const err = (await sendCommunication({ ...sendOptions, ...options } as typeof sendOptions, {
        authenticatedFetch: host.authenticatedFetch,
      }).catch(e => e)) as SendCommunicationError;
      return { err, caller };
    }

    it('refuses a request that carries Bcc, without calling the tool', async () => {
      const { err, caller } = await refused({ bcc: ['hidden@example.com'] });
      expect(err).toBeInstanceOf(SendCommunicationError);
      expect(err.code).toBe(McpHostRefusal.BccNotSupported);
      expect(mayHaveBeenSent(err)).toBe(false);
      expect(describeSendFailure(err).message).toContain('Bcc recipients cannot be sent from here');
      expect(caller.callTool).not.toHaveBeenCalled();
    });

    it('refuses isInternalOnly=true, without calling the tool', async () => {
      const { err, caller } = await refused({ isInternalOnly: true });
      expect(err.code).toBe(McpHostRefusal.InternalOnlyNotSupported);
      expect(describeSendFailure(err).message).toContain('Internal Only emails cannot be sent from here');
      expect(caller.callTool).not.toHaveBeenCalled();
    });

    it('refuses sending from the user mailbox, without calling the tool', async () => {
      const { err, caller } = await refused({ sendMode: 'user' });
      expect(err.code).toBe(McpHostRefusal.OwnMailboxNotSupported);
      expect(caller.callTool).not.toHaveBeenCalled();
    });

    it('refuses when the request no longer relates the email to the matter (removed or swapped)', async () => {
      for (const associations of [[], undefined, [{ entityType: 'sprk_matter', entityId: 'someone-elses-matter' }]]) {
        const { err, caller } = await refused({ associations });
        expect(err.code).toBe(McpHostRefusal.MatterRemoved);
        expect(describeSendFailure(err).message).toContain('matter can no longer be removed');
        expect(caller.callTool).not.toHaveBeenCalled();
      }
    });

    it('sends normally when isInternalOnly is false, Bcc is empty and the matter is related (id case and braces ignored)', async () => {
      const { err: result, caller } = await refused({
        isInternalOnly: false,
        bcc: [],
        associations: [{ entityType: 'sprk_matter', entityId: '{MATTER-1}' }],
      });
      expect(result).toEqual({ communicationId: 'c' });
      expect(caller.callTool).toHaveBeenCalledTimes(1);
    });
  });

  it('refuses any request other than POST /api/communications/send', async () => {
    const caller = fakeCaller(() => ({}));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    await expect(host.authenticatedFetch('/api/other', { method: 'POST', body: '{}' })).rejects.toThrow(/serves only/);
    expect(caller.callTool).not.toHaveBeenCalled();
  });
});

describe('createMcpEmailComposeHandlers - attachment picker', () => {
  const documents = [
    { id: 'doc-1', name: 'Contract.pdf', sizeBytes: 100, fileType: 'pdf', url: 'https://x/doc-1' },
    { id: 'doc-2', name: 'Letter.docx' },
    { name: 'no id' },
  ];

  it('lists documents from spaarke_list_documents for the regarding matter only, and returns the picked record', async () => {
    const caller = fakeCaller(() => ({ structuredContent: { documents } }));
    const pickDocument = jest.fn(async (o: McpDocumentListOutcome) => o.documents?.[0] ?? null);
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER, pickDocument });

    const picked = await host.onLookupRecord!('sprk_document');

    expect(caller.callTool).toHaveBeenCalledTimes(1);
    expect(caller.callTool).toHaveBeenCalledWith('spaarke_list_documents', {
      regardingEntityType: 'sprk_matter',
      regardingId: 'matter-1',
    });
    expect(pickDocument.mock.calls[0][0].documents?.map(d => d.id)).toEqual(['doc-1', 'doc-2']); // row without an id is dropped
    expect(picked).toEqual({
      entityType: 'sprk_document',
      id: 'doc-1',
      name: 'Contract.pdf',
      url: 'https://x/doc-1',
      sizeBytes: 100,
    });
  });

  it('returns null for any other entity type without calling a tool, and when the user cancels', async () => {
    const caller = fakeCaller(() => ({ structuredContent: { documents } }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER, pickDocument: async () => null });
    expect(await host.onLookupRecord!('sprk_matter')).toBeNull();
    expect(caller.callTool).not.toHaveBeenCalled();
    expect(await host.onLookupRecord!('sprk_document')).toBeNull(); // cancelled
  });

  it('hands the picker an error to show when the list is refused (real server shape), and returns null', async () => {
    const failing = fakeCaller(() =>
      serverError({
        reasonCode: 'sdap.documents.matter_access_denied',
        message: 'You do not have access to this matter.',
        status: 403,
        correlationId: 'corr-list',
      })
    );
    const pick = jest.fn(async (_outcome: McpDocumentListOutcome) => null);
    const host = createMcpEmailComposeHandlers({ caller: failing, regarding: MATTER, pickDocument: pick });

    expect(await host.onLookupRecord!('sprk_document')).toBeNull();

    expect(pick).toHaveBeenCalledTimes(1);
    expect(pick).toHaveBeenCalledWith({ error: 'You do not have access to this matter.', reference: 'corr-list' });
  });

  it('hands the picker a readable error for an isError with no structured result, and when callTool rejects', async () => {
    const noStructured = fakeCaller(() => ({ isError: true, content: [{ type: 'text', text: 'Tool failed.' }] }));
    const pick1 = jest.fn(async (_outcome: McpDocumentListOutcome) => null);
    await createMcpEmailComposeHandlers({
      caller: noStructured,
      regarding: MATTER,
      pickDocument: pick1,
    }).onLookupRecord!('sprk_document');
    expect(pick1).toHaveBeenCalledWith({ error: 'Tool failed.', reference: undefined });

    const rejecting = fakeCaller(() => {
      throw new TypeError('Failed to fetch');
    });
    const pick2 = jest.fn(async (_outcome: McpDocumentListOutcome) => null);
    const picked = await createMcpEmailComposeHandlers({
      caller: rejecting,
      regarding: MATTER,
      pickDocument: pick2,
    }).onLookupRecord!('sprk_document');
    expect(picked).toBeNull();
    expect(pick2).toHaveBeenCalledTimes(1); // the rejection is caught here, not left to the engine's console.warn
    const outcome = pick2.mock.calls[0][0];
    expect(outcome.error).toMatch(/could not be loaded/i);
    expect(outcome.documents).toBeUndefined();
  });

  it('hands the picker {documents} (no error) on success, including an empty matter', async () => {
    const caller = fakeCaller(() => ({ structuredContent: { documents: [] } }));
    const pick = jest.fn(async (_outcome: McpDocumentListOutcome) => null);
    await createMcpEmailComposeHandlers({ caller, regarding: MATTER, pickDocument: pick }).onLookupRecord!(
      'sprk_document'
    );
    expect(pick).toHaveBeenCalledWith({ documents: [] });
  });

  it('leaves the picker undefined without a pickDocument UI, and every out-of-scope handler is absent', () => {
    const host = createMcpEmailComposeHandlers({ caller: fakeCaller(() => ({})), regarding: MATTER });
    expect(host.onLookupRecord).toBeUndefined();
    for (const key of [
      'onLookupRecipients',
      'onAddRelationship',
      'onUploadLocalAttachment',
      'onListEmailTemplates',
      'onRenderEmailTemplate',
      'onDraftWithAi',
      'onSearchRecipients',
    ]) {
      expect(host).not.toHaveProperty(key);
    }
    expect(host.attachmentSources).toEqual([{ kind: 'related' }]); // no local-file source
  });
});

describe('createMcpEmailComposeHandlers - no Xrm', () => {
  it('never reads window.Xrm or calls getXrm', async () => {
    const getXrmSpy = jest.spyOn(xrmContext, 'getXrm');
    const xrmGetter = jest.fn();
    Object.defineProperty(window, 'Xrm', { configurable: true, get: xrmGetter });
    try {
      const caller = fakeCaller(name =>
        name === 'spaarke_list_documents'
          ? { structuredContent: { documents: [{ id: 'd', name: 'D' }] } }
          : { structuredContent: { communicationId: 'c' } }
      );
      const host = createMcpEmailComposeHandlers({
        caller,
        regarding: MATTER,
        pickDocument: async o => o.documents?.[0] ?? null,
      });
      await host.onLookupRecord!('sprk_document');
      await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch });
      expect(getXrmSpy).not.toHaveBeenCalled();
      expect(xrmGetter).not.toHaveBeenCalled();
    } finally {
      delete (window as unknown as Record<string, unknown>).Xrm;
      getXrmSpy.mockRestore();
    }
  });
});
