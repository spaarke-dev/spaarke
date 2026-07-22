/**
 * previewModel.ts
 *
 * Pure bounding logic for the compact record preview (task 030 / FR-13). No
 * I/O, no platform APIs, no React — a plain function over the `by-regarding`
 * read result, so the preview's contract (max 3 threads, last 5 communications,
 * default thread auto-expanded, N-of-M counter) is unit-testable in isolation.
 *
 * REUSE, not reimplementation (NFR-06): the DTO→domain mapping is the shared
 * `mapRegardingReadResultToGroups` (which itself calls
 * `mapThreadMessageDtoToTimelineMessage`), and the chronological ordering is the
 * shared `buildTimeline` — the SAME engines the full `<ConversationView/>` /
 * `<CommunicationTimeline/>` use. This module only SLICES their output for the
 * bounded preview; it invents no message model and no second ordering rule.
 */

import {
  buildTimeline,
  mapRegardingReadResultToGroups,
  type IRegardingReadResultDto,
  type RegardingThreadGroup,
  type TimelineMessage,
} from '@spaarke/ui-components';

/** Preview bound: at most this many threads are shown (FR-13). */
export const PREVIEW_MAX_THREADS = 3;
/** Preview bound: at most this many of a thread's most-recent communications are shown (FR-13). */
export const PREVIEW_MAX_MESSAGES = 5;

/** One thread as bounded for the compact preview. */
export interface PreviewThread {
  threadId: string;
  name: string;
  /** Full communication count for this thread (may exceed `messages.length`). */
  threadMessageCount: number;
  /** The last (most-recent) ≤5 communications, in ascending chronological order. */
  messages: TimelineMessage[];
  /** True when this thread's shown window is truncated (thread has more than the shown window). */
  hasMore: boolean;
}

/** The complete, bounded view-model the preview component renders. */
export interface PreviewModel {
  /** Bounded to ≤`PREVIEW_MAX_THREADS`, newest-active first. */
  threads: PreviewThread[];
  /** Threads shown in the preview (`threads.length`, i.e. the "N" in "N of M"). */
  shownThreadCount: number;
  /** Total threads on the record (the "M" in "N of M"). */
  totalThreadCount: number;
  /** Total communications on the record (across all threads). */
  totalMessageCount: number;
  /** The thread auto-expanded by default (the first / most-recently-active), if any. */
  defaultExpandedThreadId?: string;
}

export interface BuildPreviewOptions {
  maxThreads?: number;
  maxMessages?: number;
}

/**
 * Builds the bounded preview view-model from the `by-regarding` read result.
 *
 * Ordering: threads come newest-active-first from
 * `mapRegardingReadResultToGroups`; within a thread, `buildTimeline` orders
 * messages ascending-chronological and the preview keeps the LAST `maxMessages`
 * (the most recent) so the newest activity is what the user sees.
 */
export function buildPreviewModel(result: IRegardingReadResultDto, options?: BuildPreviewOptions): PreviewModel {
  const maxThreads = options?.maxThreads ?? PREVIEW_MAX_THREADS;
  const maxMessages = options?.maxMessages ?? PREVIEW_MAX_MESSAGES;

  const groups: RegardingThreadGroup[] = mapRegardingReadResultToGroups(result);

  const boundedGroups = groups.slice(0, maxThreads);

  const threads: PreviewThread[] = boundedGroups.map(group => {
    const ordered = buildTimeline(group.messages).map(entry => entry.message);
    const shown = maxMessages > 0 && ordered.length > maxMessages ? ordered.slice(-maxMessages) : ordered;
    return {
      threadId: group.threadId,
      name: group.name,
      threadMessageCount: group.messageCount,
      messages: shown,
      hasMore: ordered.length > shown.length,
    };
  });

  return {
    threads,
    shownThreadCount: threads.length,
    totalThreadCount: result.threadCount,
    totalMessageCount: result.messageCount,
    defaultExpandedThreadId: threads[0]?.threadId,
  };
}
