/**
 * Source-attachment enumeration contract for the Actions PCF (task 104, D1/D2).
 * Protects: inline images excluded, `sprk_document` GUID + deep-link projected,
 * rows without a document dropped, and enumeration failures degrade to [].
 */

import {
  buildDocumentLinkUrl,
  projectSourceAttachment,
  filterFileAttachments,
  fetchSourceAttachments,
  type ISourceAttachmentRecord,
  type IActionsWebApi,
} from '../CommunicationActions/attachmentsSource';

const DV = 'https://org.crm.dynamics.com';

function rec(overrides: Partial<ISourceAttachmentRecord>): ISourceAttachmentRecord {
  return {
    sprk_communicationattachmentid: 'att-1',
    sprk_name: 'contract.pdf',
    sprk_attachmenttype: 100000000, // File
    _sprk_document_value: 'doc-1',
    ...overrides,
  };
}

describe('buildDocumentLinkUrl', () => {
  it('builds a sprk_document deep-link and tolerates a trailing slash', () => {
    expect(buildDocumentLinkUrl(DV + '/', 'doc-1')).toBe(
      `${DV}/main.aspx?pagetype=entityrecord&etn=sprk_document&id=doc-1`
    );
  });
});

describe('projectSourceAttachment', () => {
  it('projects a file row into a composer attachment with documentId + linkUrl', () => {
    const item = projectSourceAttachment(rec({}), DV);
    expect(item).not.toBeNull();
    expect(item!.source).toBe('related');
    expect(item!.fileName).toBe('contract.pdf');
    expect(item!.documentId).toBe('doc-1');
    expect(item!.linkUrl).toContain('etn=sprk_document&id=doc-1');
    expect(item!.sizeBytes).toBe(0);
  });

  it('drops rows without a backing document (cannot attach or link)', () => {
    expect(projectSourceAttachment(rec({ _sprk_document_value: null }), DV)).toBeNull();
  });

  it('strips braces from the document GUID', () => {
    const item = projectSourceAttachment(rec({ _sprk_document_value: '{DOC-2}' }), DV);
    expect(item!.documentId).toBe('DOC-2');
  });
});

describe('filterFileAttachments', () => {
  it('excludes inline-image rows', () => {
    const rows = [rec({}), rec({ sprk_communicationattachmentid: 'att-2', sprk_attachmenttype: 100000001 })];
    expect(filterFileAttachments(rows)).toHaveLength(1);
  });
});

describe('fetchSourceAttachments', () => {
  it('queries sprk_communicationattachment and returns projected file attachments', async () => {
    const retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [rec({}), rec({ sprk_communicationattachmentid: 'att-2', sprk_attachmenttype: 100000001 })],
    });
    const webApi: IActionsWebApi = { retrieveMultipleRecords };
    const items = await fetchSourceAttachments(webApi, 'comm-1', DV);

    expect(retrieveMultipleRecords).toHaveBeenCalledWith(
      'sprk_communicationattachment',
      expect.stringContaining('_sprk_communication_value eq comm-1')
    );
    expect(items).toHaveLength(1); // inline image filtered out
    expect(items[0].documentId).toBe('doc-1');
  });

  it('degrades to [] on a query failure (best-effort, never a hard failure)', async () => {
    const webApi: IActionsWebApi = {
      retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('boom')),
    };
    expect(await fetchSourceAttachments(webApi, 'comm-1', DV)).toEqual([]);
  });

  it('returns [] for a blank communication id without querying', async () => {
    const retrieveMultipleRecords = jest.fn();
    await fetchSourceAttachments({ retrieveMultipleRecords }, '', DV);
    expect(retrieveMultipleRecords).not.toHaveBeenCalled();
  });
});
