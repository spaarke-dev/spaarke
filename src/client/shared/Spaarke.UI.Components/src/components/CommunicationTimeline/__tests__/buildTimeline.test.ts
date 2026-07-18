/**
 * buildTimeline.test.ts (task 060, step 6/10)
 *
 * Unit coverage for the pure `buildTimeline` (chronological ordering +
 * reply-nesting depth) and `mapThreadMessageDtoToTimelineMessage` (DTO →
 * domain mapping) helpers. Both are pure (no I/O, no platform APIs —
 * ADR-012), so these are ADR-038 domain-logic behavior contracts
 * (MAINTAIN-class), not scaffolding.
 */
import { buildTimeline, mapThreadMessageDtoToTimelineMessage } from '../CommunicationTimeline.buildTimeline';
import type { TimelineMessage } from '../CommunicationTimeline.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

function msg(id: string, overrides: Partial<TimelineMessage> = {}): TimelineMessage {
  return {
    id,
    channelType: 'email',
    channelTypeRaw: 100000000,
    sender: `sender-${id}@example.com`,
    sentOn: undefined,
    createdOn: undefined,
    body: `body-${id}`,
    bodyFormat: 'html',
    inReplyTo: undefined,
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

describe('buildTimeline — chronological ordering', () => {
  it('returns an empty array for no messages', () => {
    expect(buildTimeline([])).toEqual([]);
  });

  it('orders messages chronologically by sentOn ascending', () => {
    const a = msg('a', { sentOn: '2026-07-01T10:00:00Z' });
    const b = msg('b', { sentOn: '2026-07-01T09:00:00Z' });
    const c = msg('c', { sentOn: '2026-07-01T11:00:00Z' });

    const result = buildTimeline([a, b, c]);

    expect(result.map(e => e.message.id)).toEqual(['b', 'a', 'c']);
  });

  it('falls back to createdOn when sentOn is absent', () => {
    const a = msg('a', { sentOn: undefined, createdOn: '2026-07-01T10:00:00Z' });
    const b = msg('b', { sentOn: undefined, createdOn: '2026-07-01T09:00:00Z' });

    const result = buildTimeline([a, b]);

    expect(result.map(e => e.message.id)).toEqual(['b', 'a']);
  });

  it('interleaves email and chat messages purely by time, channel-agnostic', () => {
    const email = msg('email-1', { channelType: 'email', sentOn: '2026-07-01T10:00:00Z' });
    const chat = msg('chat-1', { channelType: 'message', sentOn: '2026-07-01T10:05:00Z' });
    const emailReply = msg('email-2', { channelType: 'email', sentOn: '2026-07-01T10:10:00Z' });

    const result = buildTimeline([emailReply, email, chat]);

    expect(result.map(e => e.message.id)).toEqual(['email-1', 'chat-1', 'email-2']);
  });

  it('stable tie-breaks simultaneous messages by id', () => {
    const a = msg('b-id', { sentOn: '2026-07-01T10:00:00Z' });
    const b = msg('a-id', { sentOn: '2026-07-01T10:00:00Z' });

    const result = buildTimeline([a, b]);

    expect(result.map(e => e.message.id)).toEqual(['a-id', 'b-id']);
  });
});

describe('buildTimeline — reply-nesting depth', () => {
  it('assigns depth 0 to a message with no inReplyTo', () => {
    const root = msg('root', { sentOn: '2026-07-01T10:00:00Z' });
    const result = buildTimeline([root]);
    expect(result[0].depth).toBe(0);
  });

  it('assigns depth 1 to a direct reply whose inReplyTo matches an in-thread id', () => {
    const root = msg('root', { sentOn: '2026-07-01T10:00:00Z' });
    const reply = msg('reply', { sentOn: '2026-07-01T10:05:00Z', inReplyTo: 'root' });

    const result = buildTimeline([root, reply]);
    const byId = new Map(result.map(e => [e.message.id, e.depth]));

    expect(byId.get('root')).toBe(0);
    expect(byId.get('reply')).toBe(1);
  });

  it('assigns increasing depth across a reply chain, independent of display order', () => {
    const root = msg('root', { sentOn: '2026-07-01T10:00:00Z' });
    const reply1 = msg('reply1', { sentOn: '2026-07-01T10:05:00Z', inReplyTo: 'root' });
    const reply2 = msg('reply2', { sentOn: '2026-07-01T10:10:00Z', inReplyTo: 'reply1' });

    const result = buildTimeline([reply2, root, reply1]);
    const byId = new Map(result.map(e => [e.message.id, e.depth]));

    // Order stays strictly chronological — nesting is depth-only, not re-grouping.
    expect(result.map(e => e.message.id)).toEqual(['root', 'reply1', 'reply2']);
    expect(byId.get('root')).toBe(0);
    expect(byId.get('reply1')).toBe(1);
    expect(byId.get('reply2')).toBe(2);
  });

  it('degrades to depth 0 when inReplyTo does not match any in-thread id (e.g. RFC-2822 Internet-Message-Id)', () => {
    const email = msg('email-1', {
      sentOn: '2026-07-01T10:00:00Z',
      inReplyTo: '<some-external-rfc2822-id@mail.example.com>',
    });

    const result = buildTimeline([email]);
    expect(result[0].depth).toBe(0);
  });

  it('is cycle-safe — a self-referencing or circular inReplyTo chain resolves to depth 0 rather than recursing forever', () => {
    const a = msg('a', { sentOn: '2026-07-01T10:00:00Z', inReplyTo: 'b' });
    const b = msg('b', { sentOn: '2026-07-01T10:05:00Z', inReplyTo: 'a' });
    const selfRef = msg('self', { sentOn: '2026-07-01T10:10:00Z', inReplyTo: 'self' });

    expect(() => buildTimeline([a, b, selfRef])).not.toThrow();
    const result = buildTimeline([a, b, selfRef]);
    for (const entry of result) {
      expect(entry.depth).toBe(0);
    }
  });
});

describe('mapThreadMessageDtoToTimelineMessage', () => {
  function dto(overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
    return {
      messageId: 'm1',
      body: '<p>hi</p>',
      bodyFormat: 100000001, // HTML
      communicationType: 100000000, // Email
      from: 'alice@example.com',
      sentAt: '2026-07-01T10:00:00Z',
      createdOn: '2026-07-01T09:59:00Z',
      inReplyTo: null,
      privilege: 0,
      attachments: [],
      ...overrides,
    };
  }

  it('maps CommunicationType.Email (100000000) to channelType "email"', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ communicationType: 100000000 }));
    expect(result.channelType).toBe('email');
  });

  it('maps CommunicationType.Message (100000004) to channelType "message"', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ communicationType: 100000004 }));
    expect(result.channelType).toBe('message');
  });

  it('defaults unknown/null communicationType to "email"', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ communicationType: null }));
    expect(result.channelType).toBe('email');
  });

  it('maps BodyFormat.PlainText (100000000) to bodyFormat "text"', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ bodyFormat: 100000000 }));
    expect(result.bodyFormat).toBe('text');
  });

  it('maps BodyFormat.HTML (100000001) to bodyFormat "html"', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ bodyFormat: 100000001 }));
    expect(result.bodyFormat).toBe('html');
  });

  it('defaults unknown/null bodyFormat to "html"', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ bodyFormat: null }));
    expect(result.bodyFormat).toBe('html');
  });

  it('prefers sentAt over createdOn for sentOn, but keeps createdOn separately for the poll cursor', () => {
    const result = mapThreadMessageDtoToTimelineMessage(
      dto({ sentAt: '2026-07-01T10:00:00Z', createdOn: '2026-07-01T09:59:00Z' })
    );
    expect(result.sentOn).toBe('2026-07-01T10:00:00Z');
    expect(result.createdOn).toBe('2026-07-01T09:59:00Z');
  });

  it('falls back to createdOn for sentOn when sentAt is null', () => {
    const result = mapThreadMessageDtoToTimelineMessage(dto({ sentAt: null, createdOn: '2026-07-01T09:59:00Z' }));
    expect(result.sentOn).toBe('2026-07-01T09:59:00Z');
  });

  it('maps attachment refs field-for-field, converting null → undefined', () => {
    const result = mapThreadMessageDtoToTimelineMessage(
      dto({
        attachments: [
          {
            communicationAttachmentId: 'att-1',
            documentId: 'doc-1',
            fileName: 'contract.pdf',
            attachmentType: 100000000,
          },
          { communicationAttachmentId: 'att-2', documentId: null, fileName: null, attachmentType: null },
        ],
      })
    );

    expect(result.attachments).toEqual([
      { id: 'att-1', documentId: 'doc-1', fileName: 'contract.pdf', attachmentType: 100000000 },
      { id: 'att-2', documentId: undefined, fileName: undefined, attachmentType: undefined },
    ]);
  });
});
