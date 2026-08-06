import {
  buildEmailSaveRequest,
  computeQuickSaveIdempotencyKey,
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
  it('is deterministic for the same message id + target', async () => {
    const a = await computeQuickSaveIdempotencyKey('<abc@contoso.com>', target);
    const b = await computeQuickSaveIdempotencyKey('<abc@contoso.com>', target);
    expect(a).toBe(b);
    expect(a.length).toBeGreaterThan(0);
  });

  it('differs when the target differs', async () => {
    const a = await computeQuickSaveIdempotencyKey('<abc@contoso.com>', target);
    const other: EntitySearchResult = { ...target, id: '22222222-2222-2222-2222-222222222222' };
    const b = await computeQuickSaveIdempotencyKey('<abc@contoso.com>', other);
    expect(a).not.toBe(b);
  });
});
