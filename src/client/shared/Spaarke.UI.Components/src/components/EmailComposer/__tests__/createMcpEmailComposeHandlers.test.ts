/**
 * The MCP email host (spaarkeai-mcp-bff-api-r2 task 011): attachment picker via spaarke_list_documents, Send via
 * spaarke_send_email with one idempotency key per compose session, tool refusals through the engine's failure path,
 * and no Xrm. A fake McpToolCaller stands in for the transport.
 */
import * as xrmContext from '../../../utils/xrmContext';
import { SendCommunicationError, sendCommunication } from '../../../services/communicationApi';
import { describeSendFailure, toSendCommunicationError } from '../describeSendFailure';
import { createMcpEmailComposeHandlers } from '../createMcpEmailComposeHandlers';
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
  associations: [{ entityType: 'sprk_matter', entityId: 'someone-elses-matter' }],
};

describe('createMcpEmailComposeHandlers - Send', () => {
  it('calls spaarke_send_email once with the recipients, subject, body, matter, documents and an idempotency key', async () => {
    const caller = fakeCaller(() => ({ structuredContent: { communicationId: 'comm-9' } }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

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
      regardingId: 'matter-1', // the host's matter, not the request's association
      attachmentDocumentIds: ['doc-1', 'doc-2'],
    });
    expect(typeof args.idempotencyKey).toBe('string');
    expect(args.idempotencyKey).toBe(host.idempotencyKey);
    expect(host.associations).toEqual([
      { entityType: 'sprk_matter', entityId: 'matter-1', entityName: 'Smith v. Jones' },
    ]);
  });

  it('reuses the same idempotency key when Send is pressed again after a failure', async () => {
    let calls = 0;
    const caller = fakeCaller(() =>
      ++calls === 1
        ? { isError: true, reasonCode: 'sdap.communication.sender_disabled' }
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

  it('surfaces an isError result through the send-failure path with the tool reasonCode, and does not retry', async () => {
    const caller = fakeCaller(() => ({
      isError: true,
      reasonCode: 'sdap.communication.recipient_refused',
      content: [{ type: 'text', text: 'Recipient domain is not allowed.' }],
    }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });

    const err = await sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch }).catch(e => e);

    expect(err).toBeInstanceOf(SendCommunicationError);
    expect((err as SendCommunicationError).code).toBe('sdap.communication.recipient_refused');
    // The engine normalises with toSendCommunicationError and describes with describeSendFailure.
    const described = describeSendFailure(toSendCommunicationError(err));
    expect(described.message).toContain('Recipient domain is not allowed.');
    expect(caller.callTool).toHaveBeenCalledTimes(1);
  });

  it('lets a transport failure (rejected callTool) propagate, with no retry', async () => {
    const caller = fakeCaller(() => {
      throw new TypeError('Failed to fetch');
    });
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER });
    await expect(sendCommunication(sendOptions, { authenticatedFetch: host.authenticatedFetch })).rejects.toThrow(
      'Failed to fetch'
    );
    expect(caller.callTool).toHaveBeenCalledTimes(1);
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
    const pickDocument = jest.fn(async (docs: { id: string }[]) => docs[0] as never);
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER, pickDocument });

    const picked = await host.onLookupRecord!('sprk_document');

    expect(caller.callTool).toHaveBeenCalledTimes(1);
    expect(caller.callTool).toHaveBeenCalledWith('spaarke_list_documents', {
      regardingEntityType: 'sprk_matter',
      regardingId: 'matter-1',
    });
    expect(pickDocument.mock.calls[0][0].map(d => d.id)).toEqual(['doc-1', 'doc-2']); // row without an id is dropped
    expect(picked).toEqual({
      entityType: 'sprk_document',
      id: 'doc-1',
      name: 'Contract.pdf',
      url: 'https://x/doc-1',
      sizeBytes: 100,
    });
  });

  it('returns null for any other entity type without calling a tool, when cancelled, and when the list errors', async () => {
    const caller = fakeCaller(() => ({ structuredContent: { documents } }));
    const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER, pickDocument: async () => null });
    expect(await host.onLookupRecord!('sprk_matter')).toBeNull();
    expect(caller.callTool).not.toHaveBeenCalled();
    expect(await host.onLookupRecord!('sprk_document')).toBeNull(); // cancelled

    const failing = fakeCaller(() => ({ isError: true, reasonCode: 'x' }));
    const pick = jest.fn();
    const host2 = createMcpEmailComposeHandlers({ caller: failing, regarding: MATTER, pickDocument: pick });
    expect(await host2.onLookupRecord!('sprk_document')).toBeNull();
    expect(pick).not.toHaveBeenCalled();
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
      const host = createMcpEmailComposeHandlers({ caller, regarding: MATTER, pickDocument: async d => d[0] });
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
