import {
  buildEmailSaveRequest,
  buildDocumentSaveRequest,
  computeQuickSaveIdempotencyKey,
  arrayBufferToBase64,
  type QuickSaveEmailContext,
} from '../quickSaveHelpers';
import type { EntitySearchResult } from '../../hooks/useEntitySearch';

const target: EntitySearchResult = {
  id: '11111111-1111-1111-1111-111111111111',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Smith v Jones',
};

const context: QuickSaveEmailContext = {
  internetMessageId: '<abc@contoso.com>',
  subject: 'Re: Contract terms',
  senderEmail: 'sender@contoso.com',
  senderName: 'The Sender',
  recipients: [
    { email: 'a@x.com', displayName: 'A', type: 'to' },
    { email: 'b@x.com', type: 'cc' },
  ],
  sentDate: new Date('2026-08-01T10:00:00Z'),
};

describe('buildEmailSaveRequest', () => {
  it('files to the predicted record via the Email save contract', () => {
    const req = buildEmailSaveRequest(context, target, 'idem-key-1');

    expect(req.contentType).toBe('Email');
    expect(req.triggerAiProcessing).toBe(true);
    // Target entity uses the Dataverse logical name (not the picker's display EntityType).
    expect(req.targetEntity).toEqual({
      entityType: 'sprk_matter',
      entityId: '11111111-1111-1111-1111-111111111111',
      displayName: 'Smith v Jones',
    });
    // Body/attachments are fetched server-side; client sends only the message id + metadata.
    expect(req.email.internetMessageId).toBe('<abc@contoso.com>');
    expect(req.email.subject).toBe('Re: Contract terms');
    expect(req.email.senderEmail).toBe('sender@contoso.com');
    expect(req.email.body).toBeUndefined();
    // Recipient types are mapped to the server's PascalCase enum.
    expect(req.email.recipients).toEqual([
      { type: 'To', email: 'a@x.com', name: 'A' },
      { type: 'Cc', email: 'b@x.com' },
    ]);
    expect(req.email.sentDate).toBe('2026-08-01T10:00:00.000Z');
    // Idempotency key travels in the body (the server accepts it there or via header).
    expect(req.idempotencyKey).toBe('idem-key-1');
  });

  it("marks the name system-derived: the ribbon files under the email's own subject, never a typed name (task 046)", () => {
    const req = buildEmailSaveRequest(context, target, 'idem-key-1');

    expect(req.email.isNameSystemDerived).toBe(true);
  });

  it('falls back to a placeholder subject/sender when the email lacks them', () => {
    const bare: QuickSaveEmailContext = { internetMessageId: '<x@y>', subject: '' };
    const req = buildEmailSaveRequest(bare, target, 'k');
    expect(req.documentMetadata.name).toBe('Untitled Email');
    expect(req.email.subject).toBe('Untitled Email');
    expect(req.email.senderEmail).toBe('unknown@placeholder.com');
    expect(req.email.recipients).toEqual([]);
    expect(req.email.sentDate).toBeUndefined();
  });
});

describe('computeQuickSaveIdempotencyKey', () => {
  it('is deterministic for the same message id + target (email kind)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: '<abc@contoso.com>', target });
    const b = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: '<abc@contoso.com>', target });
    expect(a).toBe(b);
    expect(a.length).toBeGreaterThan(0);
  });

  it('differs when the target differs (email kind)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: '<abc@contoso.com>', target });
    const other: EntitySearchResult = { ...target, id: '22222222-2222-2222-2222-222222222222' };
    const b = await computeQuickSaveIdempotencyKey({
      kind: 'email',
      internetMessageId: '<abc@contoso.com>',
      target: other,
    });
    expect(a).not.toBe(b);
  });

  it('is deterministic for the same title + content (document kind)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'AAAA' });
    const b = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'AAAA' });
    expect(a).toBe(b);
    expect(a.length).toBeGreaterThan(0);
  });

  it('differs when the document content differs (task 037 double-click-does-not-duplicate guard)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'AAAA' });
    const b = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'BBBB' });
    expect(a).not.toBe(b);
  });

  it('the email and document kinds never collide even over similar-looking inputs', async () => {
    const emailKey = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: 'x', target });
    const documentKey = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'x', contentBase64: '' });
    expect(emailKey).not.toBe(documentKey);
  });
});

describe('buildDocumentSaveRequest (task 037 / FR-17 — Word ribbon quickSave)', () => {
  it('builds an unfiled CREATE request — no targetEntity, no existingDocumentId/isNewVersion', () => {
    const req = buildDocumentSaveRequest({ title: 'Contract' }, 'BASE64BYTES', 'idem-key-1');

    expect(req.contentType).toBe('Document');
    expect(req.triggerAiProcessing).toBe(true);
    expect(req.document).toEqual({
      fileName: 'Contract.docx',
      title: 'Contract',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      contentBase64: 'BASE64BYTES',
    });
    expect(req.idempotencyKey).toBe('idem-key-1');
    // Association is optional for a Document save (mirrors useSaveFlow's "Association is optional"
    // rule) — a quick save never guesses a target entity.
    expect(req).not.toHaveProperty('targetEntity');
    expect(req.document).not.toHaveProperty('existingDocumentId');
    expect(req.document).not.toHaveProperty('isNewVersion');
  });

  it('does not double an existing .docx extension', () => {
    const req = buildDocumentSaveRequest({ title: 'Contract.docx' }, 'x', 'k');
    expect(req.document.fileName).toBe('Contract.docx');
  });

  it('falls back to a placeholder title when blank', () => {
    const req = buildDocumentSaveRequest({ title: '' }, 'x', 'k');
    expect(req.document.title).toBe('Document');
    expect(req.document.fileName).toBe('Document.docx');
  });
});

describe('arrayBufferToBase64', () => {
  it('round-trips known bytes the same way SaveView.captureDocumentContent does (Uint8Array -> btoa)', () => {
    const bytes = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x00, 0xff]);
    const base64 = arrayBufferToBase64(bytes.buffer);
    // Decode back and compare — proves this is a real, correct base64 encoding, not just "some string".
    const decoded = atob(base64);
    const roundTripped = Uint8Array.from(decoded, c => c.charCodeAt(0));
    expect(Array.from(roundTripped)).toEqual(Array.from(bytes));
  });

  it('handles an empty buffer', () => {
    expect(arrayBufferToBase64(new ArrayBuffer(0))).toBe('');
  });
});
