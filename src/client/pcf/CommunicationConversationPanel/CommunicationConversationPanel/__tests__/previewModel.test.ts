import { buildPreviewModel, PREVIEW_MAX_THREADS, PREVIEW_MAX_MESSAGES } from '../previewModel';
import type { IRegardingReadResultDto, IThreadMessageDto } from '@spaarke/ui-components';

// Message factory — the by-regarding wire shape (`IThreadMessageDto`).
function msg(id: string, isoSentAt: string, body = `body-${id}`): IThreadMessageDto {
  return {
    messageId: id,
    body,
    bodyFormat: 100000001, // HTML
    communicationType: 100000000, // Email
    from: 'sender@example.com',
    subject: `subject-${id}`,
    to: ['to@example.com'],
    direction: 100000001,
    sentBy: 'user-guid',
    sentByName: 'Test Sender',
    sentAt: isoSentAt,
    createdOn: isoSentAt,
    inReplyTo: null,
    privilege: 0,
    isInternalOnly: false,
    isPrivate: false,
    attachments: [],
  };
}

function thread(threadId: string, name: string, messages: IThreadMessageDto[]) {
  return { threadId, name, messages, count: messages.length };
}

function makeResult(threads: ReturnType<typeof thread>[]): IRegardingReadResultDto {
  return {
    entityType: 'sprk_matter',
    recordId: 'matter-1',
    threads,
    threadCount: threads.length,
    messageCount: threads.reduce((sum, t) => sum + t.messages.length, 0),
  };
}

describe('buildPreviewModel — FR-13 preview bounding', () => {
  it('bounds to at most 3 threads even when the record has more', () => {
    const result = makeResult([
      thread('t1', 'Thread 1', [msg('m1', '2026-01-05T10:00:00Z')]),
      thread('t2', 'Thread 2', [msg('m2', '2026-01-04T10:00:00Z')]),
      thread('t3', 'Thread 3', [msg('m3', '2026-01-03T10:00:00Z')]),
      thread('t4', 'Thread 4', [msg('m4', '2026-01-02T10:00:00Z')]),
      thread('t5', 'Thread 5', [msg('m5', '2026-01-01T10:00:00Z')]),
    ]);

    const model = buildPreviewModel(result);

    expect(PREVIEW_MAX_THREADS).toBe(3);
    expect(model.threads).toHaveLength(3);
    expect(model.shownThreadCount).toBe(3);
    expect(model.totalThreadCount).toBe(5);
  });

  it('bounds a thread to its last 5 communications (most recent), ascending order, hasMore=true', () => {
    const many = Array.from({ length: 8 }, (_, i) =>
      msg(`m${i + 1}`, `2026-01-${String(i + 1).padStart(2, '0')}T10:00:00Z`)
    );
    const result = makeResult([thread('t1', 'Busy thread', many)]);

    const model = buildPreviewModel(result);
    const t = model.threads[0];

    expect(PREVIEW_MAX_MESSAGES).toBe(5);
    expect(t.messages).toHaveLength(5);
    expect(t.threadMessageCount).toBe(8);
    expect(t.hasMore).toBe(true);
    // last 5 => m4..m8, ascending
    expect(t.messages.map(m => m.id)).toEqual(['m4', 'm5', 'm6', 'm7', 'm8']);
  });

  it('auto-expands the default (first / most-recently-active) thread', () => {
    const result = makeResult([
      thread('older', 'Older', [msg('a', '2026-01-01T10:00:00Z')]),
      thread('newest', 'Newest', [msg('b', '2026-02-01T10:00:00Z')]),
    ]);

    const model = buildPreviewModel(result);

    // mapRegardingReadResultToGroups orders newest-active first
    expect(model.threads[0].threadId).toBe('newest');
    expect(model.defaultExpandedThreadId).toBe('newest');
  });

  it('counter reflects the record actual counts (N of M + total messages)', () => {
    const result = makeResult([
      thread('t1', 'One', [msg('m1', '2026-01-02T10:00:00Z'), msg('m2', '2026-01-03T10:00:00Z')]),
      thread('t2', 'Two', [msg('m3', '2026-01-01T10:00:00Z')]),
    ]);

    const model = buildPreviewModel(result);

    expect(model.shownThreadCount).toBe(2);
    expect(model.totalThreadCount).toBe(2);
    expect(model.totalMessageCount).toBe(3);
  });

  it('thread with ≤5 messages is not truncated (hasMore=false)', () => {
    const result = makeResult([
      thread('t1', 'Short', [msg('m1', '2026-01-01T10:00:00Z'), msg('m2', '2026-01-02T10:00:00Z')]),
    ]);

    const model = buildPreviewModel(result);
    expect(model.threads[0].messages).toHaveLength(2);
    expect(model.threads[0].hasMore).toBe(false);
  });

  it('empty record → no threads, zero counters, no default expansion', () => {
    const model = buildPreviewModel(makeResult([]));
    expect(model.threads).toHaveLength(0);
    expect(model.shownThreadCount).toBe(0);
    expect(model.totalThreadCount).toBe(0);
    expect(model.totalMessageCount).toBe(0);
    expect(model.defaultExpandedThreadId).toBeUndefined();
  });
});
