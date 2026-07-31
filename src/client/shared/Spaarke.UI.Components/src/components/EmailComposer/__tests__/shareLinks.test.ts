/**
 * shareLinks.test.ts — owner UAT 2026-07-30 R2 item 12.
 *
 * At SEND, `resolveAttachmentShareLinks` swaps the internal URL of every attachment the author toggled
 * **Link** on (that has a `documentId`) for a recipient-openable SPE sharing link resolved by the host.
 * Best-effort + non-blocking: no handler / null / throw all keep the prior `linkUrl` so a share-link
 * hiccup never fails the send. Non-linked attachments are untouched.
 */
import { resolveAttachmentShareLinks } from '../EmailComposer';
import type { IAttachmentItem } from '../EmailComposer.types';

function att(overrides: Partial<IAttachmentItem>): IAttachmentItem {
  return {
    id: overrides.id ?? 'a1',
    source: overrides.source ?? 'related',
    fileName: overrides.fileName ?? 'brief.pdf',
    sizeBytes: overrides.sizeBytes ?? 100,
    ...overrides,
  } as IAttachmentItem;
}

describe('resolveAttachmentShareLinks (R2 item 12)', () => {
  it('returns attachments unchanged when no handler is supplied', async () => {
    const items = [att({ documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, undefined);
    expect(out[0].linkUrl).toBe('internal://d1');
  });

  it('replaces the linkUrl of a linked document with the resolved sharing link', async () => {
    const resolver = jest.fn().mockResolvedValue('https://share/anon/d1');
    const items = [att({ documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(resolver).toHaveBeenCalledWith('d1');
    expect(out[0].linkUrl).toBe('https://share/anon/d1');
  });

  it('does NOT resolve attachments that are not Link-selected', async () => {
    const resolver = jest.fn().mockResolvedValue('https://share/anon/d1');
    const items = [att({ documentId: 'd1', linkSelected: false, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(resolver).not.toHaveBeenCalled();
    expect(out[0].linkUrl).toBe('internal://d1');
  });

  it('keeps the prior linkUrl when the resolver returns null (share link unavailable)', async () => {
    const resolver = jest.fn().mockResolvedValue(null);
    const items = [att({ documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(out[0].linkUrl).toBe('internal://d1');
  });

  it('keeps the prior linkUrl when the resolver throws (best-effort, never blocks send)', async () => {
    const resolver = jest.fn().mockRejectedValue(new Error('policy blocks anonymous links'));
    const items = [att({ documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(out[0].linkUrl).toBe('internal://d1');
  });

  it('resolves only the linked-with-documentId subset in a mixed list', async () => {
    const resolver = jest.fn(async (id: string) => `https://share/${id}`);
    const items = [
      att({ id: 'a1', documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' }),
      att({ id: 'a2', documentId: 'd2', linkSelected: false, linkUrl: 'internal://d2' }),
      att({ id: 'a3', source: 'local', linkSelected: true, linkUrl: undefined }), // no documentId yet
    ];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(out[0].linkUrl).toBe('https://share/d1');
    expect(out[1].linkUrl).toBe('internal://d2');
    expect(out[2].linkUrl).toBeUndefined();
    expect(resolver).toHaveBeenCalledTimes(1);
  });
});
