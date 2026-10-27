/**
 * Task 133: the pure rules between the Outlook attachment list and the from-email contract
 * (projects/ebilling-r1/notes/095-from-email-contract.md): which attachments are listed and why one cannot be sent,
 * the wire marks, and the seam input.
 */
import type { AttachmentInfo } from '@shared/adapters/types';
import {
  applyMarkingBudget,
  buildInvoiceSubmissionInput,
  describeOverBudget,
  isPdfAttachment,
  normalizeFirmInvoiceNumber,
  toInvoiceMarkingAttachments,
  toSubmissionMarks,
} from '../invoiceSubmissionSeam';
import { MAX_ATTACHMENT_BYTES, captureEmailContent } from '../emailContentCapture';

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
    });
    // No suggestedInvoice: Outlook has no hint beyond the PDF rule, which the marking component applies itself.
    expect(listed.every(a => a.suggestedInvoice === undefined)).toBe(true);
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
  });

  it('must-not-fire: an inline picture before the PDF never disables it (capture sends marked files first, inline last)', () => {
    const listed = toInvoiceMarkingAttachments(
      [
        att({ id: 'logo', name: 'logo.png', isInline: true, size: 600 }),
        att({ id: 'inv', name: 'inv.pdf', size: 600 }),
      ],
      1000 // the picture's base64 (800) would leave no room for the PDF's (800) if it were counted first
    );
    expect(listed).toHaveLength(1);
    expect(listed[0]!.disabledReason).toBeUndefined();
  });

  it('agrees with the real capture: the PDF the pre-check leaves enabled is the one the capture sends', async () => {
    const email = [
      att({ id: 'logo', name: 'logo.png', isInline: true, size: 600 }),
      att({ id: 'inv', name: 'inv.pdf', size: 600 }),
    ];
    const listed = toInvoiceMarkingAttachments(email, 1100);
    const marked = new Set(listed.filter(a => !a.disabledReason).map(a => a.id));
    const captured = await captureEmailContent(
      { getBody: async () => 'x', getAttachmentContent: async () => ({ content: 'A'.repeat(800) }) },
      email,
      marked,
      1100
    );
    expect(captured.attachments.map(a => a.attachmentId)).toEqual(['inv']);
    expect(captured.skipped.map(k => k.attachmentId)).toEqual(['logo']);
  });

  it('the budget is spent on the listed (markable) attachments in the email order; the one past it is disabled', () => {
    const listed = toInvoiceMarkingAttachments(
      [
        att({ id: 'logo', name: 'logo.png', isInline: true, size: 600 }),
        att({ id: 'a', name: 'a.pdf', size: 600 }),
        att({ id: 'b', name: 'b.csv', size: 600 }),
      ],
      1000
    );
    expect(listed.find(a => a.id === 'a')!.disabledReason).toBeUndefined();
    expect(listed.find(a => a.id === 'b')!.disabledReason).toMatch(/does not fit/);
  });

  it('must-not-fire: small file attachments are all sendable', () => {
    const listed = toInvoiceMarkingAttachments([att({ id: '1', name: 'a.pdf' }), att({ id: '2', name: 'b.docx' })]);
    expect(listed.every(a => a.disabledReason === undefined)).toBe(true);
  });
});

describe('applyMarkingBudget (the pre-check follows the live marking)', () => {
  // Budget 1000: each 600-byte file costs 800 in base64, so only one of the two fits at a time.
  const BUDGET = 1000;
  const email = [
    att({ id: 'csv', name: 'timesheet.csv', contentType: 'text/csv', size: 600 }),
    att({ id: 'pdf', name: 'inv.pdf', size: 600 }),
  ];
  const listed = toInvoiceMarkingAttachments(email, BUDGET);
  const row = (rows: ReturnType<typeof applyMarkingBudget>['attachments'], id: string) => rows.find(a => a.id === id)!;

  it("must-fire: a large Supporting file keeps the PDF disabled, and the PDF becomes markable when that file is set to Don't save", () => {
    const withCsv = applyMarkingBudget(listed, { csv: 'supporting', pdf: 'skip' }, BUDGET);
    expect(row(withCsv.attachments, 'pdf').disabledReason).toMatch(/does not fit/);
    expect(row(withCsv.attachments, 'pdf').budgetDisabled).toBe(true);
    expect(withCsv.overBudget).toEqual([]);

    const csvDropped = applyMarkingBudget(listed, { csv: 'skip', pdf: 'skip' }, BUDGET);
    expect(row(csvDropped.attachments, 'pdf').disabledReason).toBeUndefined();
    // Nothing else is marked, so the file just set to Don't save fits again as well.
    expect(row(csvDropped.attachments, 'csv').disabledReason).toBeUndefined();
  });

  it('a file earlier in the email stays markable beside a marked later one (the capture spends the budget in email order)', () => {
    const pdfMarked = applyMarkingBudget(listed, { csv: 'skip', pdf: 'invoice' }, BUDGET);
    expect(row(pdfMarked.attachments, 'csv').disabledReason).toBeUndefined();
    expect(pdfMarked.overBudget).toEqual([]);
  });

  it('must-fire: a cloud link and a file over 20 MB stay disabled whatever the marking, with their own reason', () => {
    const mixed = toInvoiceMarkingAttachments(
      [
        att({ id: 'cloud', name: 'shared.pdf', attachmentType: 'cloud', size: 10 }),
        att({ id: 'huge', name: 'huge.pdf', size: MAX_ATTACHMENT_BYTES + 1 }),
        att({ id: 'ok', name: 'ok.pdf', size: 600 }),
      ],
      BUDGET
    );
    const { attachments } = applyMarkingBudget(mixed, { cloud: 'skip', huge: 'skip', ok: 'skip' }, BUDGET);
    expect(row(attachments, 'cloud').disabledReason).toMatch(/cloud/);
    expect(row(attachments, 'cloud').budgetDisabled).toBeUndefined();
    expect(row(attachments, 'huge').disabledReason).toMatch(/larger than 20 MB/);
    expect(row(attachments, 'huge').budgetDisabled).toBeUndefined();
    expect(row(attachments, 'ok').disabledReason).toBeUndefined();
  });

  it('must-not-fire: a mark change that pushes a MARKED file over the budget unmarks and disables nothing; it is reported', () => {
    const both = applyMarkingBudget(listed, { csv: 'supporting', pdf: 'invoice' }, BUDGET);
    expect(both.overBudget.map(a => a.id)).toEqual(['pdf']); // the capture spends the budget in email order
    expect(row(both.attachments, 'pdf').disabledReason).toBeUndefined();
    expect(row(both.attachments, 'csv').disabledReason).toBeUndefined();
    expect(describeOverBudget(both.overBudget[0]!, BUDGET)).toMatch(/inv\.pdf.*doesn't fit/);
  });

  it('must-not-fire: everything that fits has nothing disabled and nothing over budget', () => {
    const roomy = toInvoiceMarkingAttachments(email); // the real 28 MB budget
    const result = applyMarkingBudget(roomy, { csv: 'supporting', pdf: 'invoice' });
    expect(result.attachments.every(a => !a.disabledReason)).toBe(true);
    expect(result.overBudget).toEqual([]);
  });

  it.each([
    ['csv only', { csv: 'supporting', pdf: 'skip' }],
    ['pdf only', { csv: 'skip', pdf: 'invoice' }],
    ['both', { csv: 'supporting', pdf: 'supporting' }],
  ] as const)(
    'agrees with the real capture (%s): files it reports over budget are the ones the capture leaves out',
    async (_n, marking) => {
      const { overBudget } = applyMarkingBudget(listed, marking, BUDGET);
      const marked = new Set(
        Object.entries(marking)
          .filter(([, role]) => role !== 'skip')
          .map(([id]) => id)
      );
      const captured = await captureEmailContent(
        { getBody: async () => 'x', getAttachmentContent: async () => ({ content: 'A'.repeat(800) }) },
        email,
        marked,
        BUDGET
      );
      const leftOutMarked = captured.skipped.filter(k => k.ticked).map(k => k.attachmentId);
      expect(overBudget.map(a => a.id)).toEqual(leftOutMarked);
    }
  );

  it('agrees with the real capture: a file the pre-check marks as markable is sent when it is marked', async () => {
    const csvDropped = applyMarkingBudget(listed, { csv: 'skip', pdf: 'skip' }, BUDGET);
    expect(row(csvDropped.attachments, 'pdf').disabledReason).toBeUndefined();
    const captured = await captureEmailContent(
      { getBody: async () => 'x', getAttachmentContent: async () => ({ content: 'A'.repeat(800) }) },
      email,
      new Set(['pdf']),
      BUDGET
    );
    expect(captured.attachments.map(a => a.attachmentId)).toContain('pdf');
    expect(captured.skipped.filter(k => k.ticked)).toEqual([]);
  });
});

describe('isPdfAttachment', () => {
  it("follows the marking component's rule: PDF content type, else a .pdf name", () => {
    expect(isPdfAttachment({ fileName: 'x.bin', contentType: 'application/pdf' })).toBe(true);
    expect(isPdfAttachment({ fileName: ' x.PDF ', contentType: 'application/octet-stream' })).toBe(true);
    expect(isPdfAttachment({ fileName: 'x.csv', contentType: 'text/csv' })).toBe(false);
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
