/**
 * shareLinks.test.ts — owner UAT 2026-07-30 R2 item 12, amended by task 098 (owner decision 2026-10-05).
 *
 * At SEND, `resolveAttachmentShareLinks` swaps the URL of every attachment the author toggled **Link** on
 * (that has a `documentId`) for the link the host resolves — now the Spaarke RECORD link, never a file
 * sharing link. No handler → unchanged. A host that cannot produce a link (null / throw) gets the link
 * OMITTED, never left at the prior internal SPE URL; `findUnresolvedLinks` lets the engine tell the author
 * why. Non-linked attachments are untouched.
 */
import { findUnresolvedLinks, resolveAttachmentShareLinks } from '../EmailComposer';
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

  it('OMITS the link when the resolver returns null (never keeps the internal SPE URL)', async () => {
    const resolver = jest.fn().mockResolvedValue(null);
    const items = [att({ documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(out[0].linkUrl).toBeUndefined();
    expect(findUnresolvedLinks(out).map(a => a.id)).toEqual(['a1']);
  });

  it('OMITS the link when the resolver throws', async () => {
    const resolver = jest.fn().mockRejectedValue(new Error('no org url'));
    const items = [att({ documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' })];
    const out = await resolveAttachmentShareLinks(items, resolver);
    expect(out[0].linkUrl).toBeUndefined();
    expect(findUnresolvedLinks(out)).toHaveLength(1);
  });

  it('findUnresolvedLinks is empty when every requested link resolved, and ignores unlinked rows', async () => {
    const resolver = jest.fn(async (id: string) => `https://org/main.aspx?id=${id}`);
    const items = [
      att({ id: 'a1', documentId: 'd1', linkSelected: true, linkUrl: 'internal://d1' }),
      att({ id: 'a2', documentId: 'd2', linkSelected: false, linkUrl: undefined }),
    ];
    expect(findUnresolvedLinks(await resolveAttachmentShareLinks(items, resolver))).toEqual([]);
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
