/**
 * Unit tests for CommunicationAttachmentsService pure helpers + the webAPI query.
 *
 * Covers: record projection (_sprk_document_value mapping), inline-image
 * filtering, .eml/.msg routing detection, file-type label, and the
 * retrieveMultipleRecords query shape (filter by parent communication).
 */

import {
  CommunicationAttachmentsService,
  IAttachmentsWebApi,
  projectAttachmentRecord,
  filterFileAttachments,
  isEmailMessageAttachment,
  isDocumentUploaded,
  fileTypeLabel,
  cleanGuid,
} from '../CommunicationAttachments/services/CommunicationAttachmentsService';
import { AttachmentType, IAttachmentItem, IAttachmentRecord, IDocumentRecord } from '../CommunicationAttachments/types';

const DOC = '_sprk_document_value';
const DOC_FMT = '_sprk_document_value@OData.Community.Display.V1.FormattedValue';

describe('projectAttachmentRecord', () => {
  it('maps _sprk_document_value into documentId and formatted value into documentName', () => {
    const record: IAttachmentRecord = {
      sprk_communicationattachmentid: 'aaaaaaaa-0000-0000-0000-000000000001',
      sprk_name: 'Report.pdf',
      sprk_attachmenttype: AttachmentType.File,
      [DOC]: '{BBBBBBBB-0000-0000-0000-000000000002}',
      [DOC_FMT]: 'Report.pdf (Document)',
    };
    const item = projectAttachmentRecord(record);
    expect(item.attachmentId).toBe('aaaaaaaa-0000-0000-0000-000000000001');
    expect(item.name).toBe('Report.pdf');
    expect(item.attachmentType).toBe(AttachmentType.File);
    expect(item.documentId).toBe('BBBBBBBB-0000-0000-0000-000000000002'); // braces stripped (case preserved — GUIDs are case-insensitive)
    expect(item.documentName).toBe('Report.pdf (Document)');
  });

  it('tolerates a missing document lookup', () => {
    const item = projectAttachmentRecord({ sprk_name: 'Orphan.txt', sprk_attachmenttype: 100000000 });
    expect(item.documentId).toBeNull();
    expect(item.documentName).toBeNull();
  });
});

describe('filterFileAttachments', () => {
  it('removes inline-image attachments and keeps files + untyped rows', () => {
    const items: IAttachmentItem[] = [
      { attachmentId: '1', name: 'a.pdf', attachmentType: AttachmentType.File, documentId: 'd1', documentName: null },
      {
        attachmentId: '2',
        name: 'logo.png',
        attachmentType: AttachmentType.InlineImage,
        documentId: 'd2',
        documentName: null,
      },
      { attachmentId: '3', name: 'note.txt', attachmentType: null, documentId: 'd3', documentName: null },
    ];
    const filtered = filterFileAttachments(items);
    expect(filtered.map(i => i.attachmentId)).toEqual(['1', '3']);
  });
});

describe('isEmailMessageAttachment', () => {
  it.each([
    ['message.eml', true],
    ['forwarded.MSG', true],
    ['Report.pdf', false],
    ['archive.eml.pdf', false],
    ['', false],
  ])('detects %s -> %s', (name, expected) => {
    expect(isEmailMessageAttachment({ name })).toBe(expected);
  });
});

describe('fileTypeLabel', () => {
  it.each([
    ['Report.pdf', 'PDF'],
    ['sheet.xlsx', 'XLSX'],
    ['noext', 'File'],
    ['trailing.', 'File'],
  ])('%s -> %s', (name, expected) => {
    expect(fileTypeLabel(name)).toBe(expected);
  });
});

describe('cleanGuid', () => {
  it('strips braces and whitespace', () => {
    expect(cleanGuid('  {ABC}  ')).toBe('ABC');
    expect(cleanGuid(null)).toBe('');
  });
});

describe('isDocumentUploaded', () => {
  it.each<[string, IDocumentRecord, boolean]>([
    ['graphitemid present', { sprk_graphitemid: 'b!drive-item' }, true],
    ['hasfile true', { sprk_hasfile: true }, true],
    ['graphitemid empty + hasfile false', { sprk_graphitemid: '  ', sprk_hasfile: false }, false],
    ['nothing set', {}, false],
    ['hasfile null, no item id', { sprk_hasfile: null, sprk_graphitemid: null }, false],
  ])('%s -> %s', (_label, record, expected) => {
    expect(isDocumentUploaded(record)).toBe(expected);
  });
});

describe('CommunicationAttachmentsService.getFileAttachments', () => {
  const makeWebApi = (entities: IAttachmentRecord[]): { api: IAttachmentsWebApi; calls: string[] } => {
    const calls: string[] = [];
    const api: IAttachmentsWebApi = {
      retrieveMultipleRecords: jest.fn(async (_entity: string, options?: string) => {
        calls.push(options ?? '');
        return { entities } as { entities: never[] };
      }),
    };
    return { api, calls };
  };

  /**
   * WebAPI mock that routes by entity logical name: attachment query returns the
   * attachment rows; the `sprk_document` enrichment query returns the doc rows.
   */
  const makeRoutedWebApi = (
    attachments: IAttachmentRecord[],
    documents: IDocumentRecord[]
  ): { api: IAttachmentsWebApi; docQueries: string[] } => {
    const docQueries: string[] = [];
    const api: IAttachmentsWebApi = {
      retrieveMultipleRecords: jest.fn(async (entity: string, options?: string) => {
        if (entity === 'sprk_document') {
          docQueries.push(options ?? '');
          return { entities: documents } as { entities: never[] };
        }
        return { entities: attachments } as { entities: never[] };
      }),
    };
    return { api, docQueries };
  };

  it('enriches each attachment with its document SPE upload status via a single sprk_document read', async () => {
    const { api, docQueries } = makeRoutedWebApi(
      [
        { sprk_communicationattachmentid: '1', sprk_name: 'a.pdf', sprk_attachmenttype: AttachmentType.File, [DOC]: 'd1' },
        { sprk_communicationattachmentid: '2', sprk_name: 'b.docx', sprk_attachmenttype: AttachmentType.File, [DOC]: 'd2' },
      ],
      [
        { sprk_documentid: 'd1', sprk_graphitemid: 'b!item-1' }, // uploaded (SPE item id)
        { sprk_documentid: 'd2', sprk_hasfile: false }, // not uploaded
      ]
    );
    const svc = new CommunicationAttachmentsService(api);
    const items = await svc.getFileAttachments('comm-1');

    expect(items.find(i => i.documentId === 'd1')?.uploaded).toBe(true);
    expect(items.find(i => i.documentId === 'd2')?.uploaded).toBe(false);
    // One enrichment query only (OR-filter over the distinct doc ids), no BFF call.
    expect(docQueries).toHaveLength(1);
    expect(docQueries[0]).toContain('sprk_hasfile');
    expect(docQueries[0]).toContain('sprk_graphitemid');
    expect(docQueries[0]).toContain('sprk_documentid eq d1');
    expect(docQueries[0]).toContain('sprk_documentid eq d2');
  });

  it('degrades to uploaded=false when the document enrichment read fails', async () => {
    const api: IAttachmentsWebApi = {
      retrieveMultipleRecords: jest.fn(async (entity: string) => {
        if (entity === 'sprk_document') throw new Error('boom');
        return {
          entities: [
            { sprk_communicationattachmentid: '1', sprk_name: 'a.pdf', sprk_attachmenttype: AttachmentType.File, [DOC]: 'd1' },
          ],
        } as { entities: never[] };
      }),
    };
    const svc = new CommunicationAttachmentsService(api);
    const items = await svc.getFileAttachments('comm-1');
    expect(items).toHaveLength(1);
    expect(items[0].uploaded).toBe(false);
  });

  it('queries sprk_communicationattachment filtered by the parent communication, selecting the document lookup', async () => {
    const { api, calls } = makeWebApi([]);
    const svc = new CommunicationAttachmentsService(api);
    await svc.getFileAttachments('{11111111-1111-1111-1111-111111111111}');

    expect(api.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    const query = calls[0];
    expect(query).toContain('_sprk_communication_value eq 11111111-1111-1111-1111-111111111111');
    expect(query).toContain('sprk_attachmenttype');
    expect(query).toContain('_sprk_document_value');
    expect(query).toContain('$orderby=createdon asc');
  });

  it('returns projected file attachments with inline images removed', async () => {
    const { api } = makeWebApi([
      {
        sprk_communicationattachmentid: '1',
        sprk_name: 'a.pdf',
        sprk_attachmenttype: AttachmentType.File,
        [DOC]: 'd1',
      },
      {
        sprk_communicationattachmentid: '2',
        sprk_name: 'logo.png',
        sprk_attachmenttype: AttachmentType.InlineImage,
        [DOC]: 'd2',
      },
    ]);
    const svc = new CommunicationAttachmentsService(api);
    const items = await svc.getFileAttachments('comm-1');
    expect(items).toHaveLength(1);
    expect(items[0].name).toBe('a.pdf');
    expect(items[0].documentId).toBe('d1');
  });

  it('returns [] for an empty communication id without querying', async () => {
    const { api } = makeWebApi([]);
    const svc = new CommunicationAttachmentsService(api);
    const items = await svc.getFileAttachments('');
    expect(items).toEqual([]);
    expect(api.retrieveMultipleRecords).not.toHaveBeenCalled();
  });
});
