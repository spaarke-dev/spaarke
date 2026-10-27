/**
 * Task 133: the pure rules between the Outlook attachment list and the from-email contract
 * (projects/ebilling-r1/notes/095-from-email-contract.md): which attachments are listed and why one cannot be sent,
 * the wire marks, and the seam input.
 */
import type { AttachmentInfo } from '@shared/adapters/types';
import {
  buildInvoiceSubmissionInput,
  normalizeFirmInvoiceNumber,
  toInvoiceMarkingAttachments,
  toSubmissionMarks,
} from '../invoiceSubmissionSeam';
import { MAX_ATTACHMENT_BYTES } from '../emailContentCapture';

const att = (over: Partial<AttachmentInfo> & Pick<AttachmentInfo, 'id' | 'name'>): AttachmentInfo => ({
  contentType: 'application/pdf',
  size: 1000,
  isInline: false,
  attachmentType: 'file',
  ...over,
});

describe('toInvoiceMarkingAttachments', () => {
  it('lists every non-inline attachment and leaves inline ones out', () => {
    const listed = toInvoiceMarkingAttachments([
      att({ id: '1', name: 'INV-4711.pdf' }),
      att({ id: '2', name: 'logo.png', contentType: 'image/png', isInline: true }),
      att({ id: '3', name: 'timesheet.csv', contentType: 'text/csv' }),
    ]);
    expect(listed.map(a => a.id)).toEqual(['1', '3']);
    expect(listed[0]).toEqual({
      id: '1',
      fileName: 'INV-4711.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1000,
      suggestedInvoice: true,
    });
    expect(listed[1]!.suggestedInvoice).toBeUndefined();
  });

  it('must-fire: a cloud link, a file over 20 MB and a file past the budget are disabled with a reason', () => {
    const listed = toInvoiceMarkingAttachments(
      [
        att({ id: 'cloud', name: 'shared.pdf', attachmentType: 'cloud' }),
        att({ id: 'big', name: 'big.pdf', size: MAX_ATTACHMENT_BYTES + 1 }),
        att({ id: 'fits', name: 'a.pdf', size: 600 }),
        att({ id: 'past', name: 'b.pdf', size: 600 }),
      ],
      1000 // budget: a.pdf's base64 (800) fits, b.pdf's does not
    );
    expect(listed.find(a => a.id === 'cloud')!.disabledReason).toMatch(/cloud/);
    expect(listed.find(a => a.id === 'big')!.disabledReason).toMatch(/larger than 20 MB/);
    expect(listed.find(a => a.id === 'fits')!.disabledReason).toBeUndefined();
    expect(listed.find(a => a.id === 'past')!.disabledReason).toMatch(/does not fit/);
    // A disabled PDF is never suggested as the invoice.
    expect(listed.find(a => a.id === 'big')!.suggestedInvoice).toBeUndefined();
  });

  it('inline attachments count against the budget (they travel in the same request)', () => {
    const listed = toInvoiceMarkingAttachments(
      [
        att({ id: 'logo', name: 'logo.png', isInline: true, size: 600 }),
        att({ id: 'inv', name: 'inv.pdf', size: 600 }),
      ],
      1000
    );
    expect(listed).toHaveLength(1);
    expect(listed[0]!.disabledReason).toMatch(/does not fit/);
  });

  it('must-not-fire: small file attachments are all sendable', () => {
    const listed = toInvoiceMarkingAttachments([att({ id: '1', name: 'a.pdf' }), att({ id: '2', name: 'b.docx' })]);
    expect(listed.every(a => a.disabledReason === undefined)).toBe(true);
  });
});

describe('toSubmissionMarks', () => {
  it('one wire mark per listed attachment: Invoice / Supporting / DontSave; disabled and unmarked are DontSave', () => {
    const listed = [
      { id: '1', fileName: 'INV-4711.pdf', contentType: 'application/pdf', sizeBytes: 182344 },
      { id: '2', fileName: 'timesheet.csv', contentType: 'text/csv', sizeBytes: 40211 },
      { id: '3', fileName: 'notes.txt', contentType: 'text/plain', sizeBytes: 10 },
      { id: '4', fileName: 'scan.pdf', contentType: 'application/pdf', sizeBytes: 9, disabledReason: 'cloud' },
      { id: '5', fileName: 'x.bin', contentType: 'application/octet-stream', sizeBytes: 1 },
    ];
    expect(toSubmissionMarks(listed, { '1': 'invoice', '2': 'supporting', '3': 'skip', '4': 'supporting' })).toEqual([
      { attachmentId: '1', fileName: 'INV-4711.pdf', size: 182344, contentType: 'application/pdf', mark: 'Invoice' },
      { attachmentId: '2', fileName: 'timesheet.csv', size: 40211, contentType: 'text/csv', mark: 'Supporting' },
      { attachmentId: '3', fileName: 'notes.txt', size: 10, contentType: 'text/plain', mark: 'DontSave' },
      { attachmentId: '4', fileName: 'scan.pdf', size: 9, contentType: 'application/pdf', mark: 'DontSave' },
      { attachmentId: '5', fileName: 'x.bin', size: 1, contentType: 'application/octet-stream', mark: 'DontSave' },
    ]);
  });
});

describe('normalizeFirmInvoiceNumber', () => {
  it('trims, and accepts 1-100 characters only', () => {
    expect(normalizeFirmInvoiceNumber('  INV-4711 ')).toBe('INV-4711');
    expect(normalizeFirmInvoiceNumber('   ')).toBeNull();
    expect(normalizeFirmInvoiceNumber('x'.repeat(100))).toHaveLength(100);
    expect(normalizeFirmInvoiceNumber('x'.repeat(101))).toBeNull();
  });
});

describe('buildInvoiceSubmissionInput', () => {
  const listed = [{ id: '1', fileName: 'INV-4711.pdf', contentType: 'application/pdf', sizeBytes: 182344 }];

  it('New: canonical GUIDs, trimmed number, blank PO is null, invoiceId null', () => {
    expect(
      buildInvoiceSubmissionInput({
        target: { entityType: 'Matter', entityId: '{6F1C0E8A-2B1D-4C7E-9A52-0D3F1B7E9C41}' },
        attachments: listed,
        marking: { '1': 'invoice' },
        firmId: '8B0C6F1E-4C2A-4F7E-9A51-2F3D1C0E9B77',
        firmInvoiceNumber: ' INV-4711 ',
        poNumber: '  ',
        mode: 'New',
        invoiceId: 'ignored-for-new',
      })
    ).toEqual({
      targetEntity: { entityType: 'Matter', entityId: '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41' },
      marks: [
        { attachmentId: '1', fileName: 'INV-4711.pdf', size: 182344, contentType: 'application/pdf', mark: 'Invoice' },
      ],
      firmId: '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77',
      firmInvoiceNumber: 'INV-4711',
      poNumber: null,
      mode: 'New',
      invoiceId: null,
    });
  });

  it('Correction carries the invoice id; a PO number is kept', () => {
    const input = buildInvoiceSubmissionInput({
      target: { entityType: 'WorkAssignment', entityId: 'aaaa' },
      attachments: listed,
      marking: { '1': 'invoice' },
      firmId: 'bbbb',
      firmInvoiceNumber: 'N',
      poNumber: ' PO-9 ',
      mode: 'Correction',
      invoiceId: '{CCCC}',
    });
    expect(input.invoiceId).toBe('cccc');
    expect(input.poNumber).toBe('PO-9');
    expect(input.targetEntity.entityType).toBe('WorkAssignment');
  });
});
