/**
 * CommunicationTimeline.buildTimeline.ts
 *
 * Pure helpers for the timeline's ordering + reply-nesting model (task 060,
 * step 6). No I/O, no platform APIs (ADR-012) — everything here is a plain
 * function over data, so it is unit-testable in isolation (see
 * `__tests__/buildTimeline.test.ts`) and reusable by task 080's client scope.
 *
 * Design decision — chronological-primary, depth-as-annotation:
 * FR-10 asks for messages "interleaved... in chronological order... with
 * reply nesting". Rather than re-grouping children directly under their
 * parent (which could pull a reply out of strict time order relative to
 * unrelated messages), `buildTimeline` keeps the OUTER order strictly
 * chronological (by `sentOn ?? createdOn`) and computes `depth` independently
 * by walking each message's `inReplyTo` ancestry. Depth is rendered as
 * indentation (see `MessageRow`), not as re-sequencing.
 *
 * This also degrades gracefully for the RFC-2822-vs-GUID mismatch noted on
 * `TimelineMessage.inReplyTo`: for inbound email, `inReplyTo` is usually an
 * RFC-2822 `Internet-Message-Id` that will not match any in-thread message's
 * GUID `id` — those rows simply resolve to depth 0 (flat), which is correct
 * behavior, not a bug. Only when `inReplyTo` DOES match an in-thread `id`
 * (plausible for `Message`-channel rows once thread continuity stamps GUIDs)
 * does indentation appear.
 */
import type { IRegardingReadResultDto, IThreadMessageDto } from '../../services/communicationTimelineApi';
import {
  BODY_FORMAT_PLAIN_TEXT,
  COMMUNICATION_TYPE_MESSAGE,
  type RegardingThreadGroup,
  type TimelineMessage,
} from './CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// DTO → domain mapping
// ---------------------------------------------------------------------------

/** Maps one wire `ThreadMessageDto` (camelCase, raw choice ints) into the timeline's domain shape. */
export function mapThreadMessageDtoToTimelineMessage(dto: IThreadMessageDto): TimelineMessage {
  return {
    id: dto.messageId,
    channelType: dto.communicationType === COMMUNICATION_TYPE_MESSAGE ? 'message' : 'email',
    channelTypeRaw: dto.communicationType,
    sender: dto.from,
    sentOn: dto.sentAt ?? dto.createdOn,
    createdOn: dto.createdOn,
    body: dto.body,
    bodyFormat: dto.bodyFormat === BODY_FORMAT_PLAIN_TEXT ? 'text' : 'html',
    inReplyTo: dto.inReplyTo,
    privilege: dto.privilege,
    attachments: dto.attachments.map(a => ({
      id: a.communicationAttachmentId,
      documentId: a.documentId ?? undefined,
      fileName: a.fileName ?? undefined,
      attachmentType: a.attachmentType ?? undefined,
    })),
  };
}

// ---------------------------------------------------------------------------
// Ordering + nesting
// ---------------------------------------------------------------------------

export interface TimelineEntry {
  message: TimelineMessage;
  /** Reply-nesting depth (0 = root / no resolvable parent within this message set). */
  depth: number;
}

function timeValue(m: TimelineMessage): number {
  const raw = m.sentOn ?? m.createdOn;
  if (!raw) return 0;
  const parsed = Date.parse(raw);
  return Number.isNaN(parsed) ? 0 : parsed;
}

function byTime(a: TimelineMessage, b: TimelineMessage): number {
  const diff = timeValue(a) - timeValue(b);
  if (diff !== 0) return diff;
  // Stable tie-break for otherwise-simultaneous messages.
  return a.id.localeCompare(b.id);
}

/** Safety cap on ancestry-walk length — guards against pathological (very long or corrupted) reply chains. */
const MAX_DEPTH_HOPS = 64;

/**
 * Orders `messages` chronologically and annotates each with a reply-nesting
 * `depth` computed by walking `inReplyTo` ancestry.
 *
 * Iterative by design (not recursive) so a cycle in the reply graph (data
 * corruption — a message cannot legitimately reply-chain back to itself)
 * degrades EVERY message in that cycle to depth 0, rather than a
 * cache-order-dependent partial result. `depthCache` only ever stores a
 * message's fully-resolved final depth, never an in-progress value, so
 * completed results are safe to reuse across the top-level `.map()` calls
 * without cross-contaminating an unrelated cycle detection.
 */
export function buildTimeline(messages: TimelineMessage[]): TimelineEntry[] {
  if (messages.length === 0) return [];

  const byId = new Map<string, TimelineMessage>();
  for (const m of messages) byId.set(m.id, m);

  const depthCache = new Map<string, number>();

  function computeDepth(start: TimelineMessage): number {
    const cached = depthCache.get(start.id);
    if (cached !== undefined) return cached;

    const seen = new Set<string>([start.id]);
    let current = start;
    let depth = 0;

    while (true) {
      const parentId = current.inReplyTo;
      if (!parentId || parentId === current.id || !byId.has(parentId)) break; // root — no resolvable parent
      if (seen.has(parentId) || depth >= MAX_DEPTH_HOPS) {
        depth = 0; // cycle, or pathologically long chain — treat as root rather than recurse/loop forever
        break;
      }
      seen.add(parentId);
      current = byId.get(parentId) as TimelineMessage;
      depth += 1;
    }

    depthCache.set(start.id, depth);
    return depth;
  }

  return [...messages].sort(byTime).map(message => ({ message, depth: computeDepth(message) }));
}

// ---------------------------------------------------------------------------
// Regarding mode — DTO → group mapping (R2 task 020, FR-03)
// ---------------------------------------------------------------------------

/** Shown when a thread's `sprk_name` is null/blank rather than rendering an empty group header. */
const UNNAMED_THREAD_LABEL = 'Untitled thread';

/**
 * Maps the `by-regarding` endpoint's response (task 010) into the collapsible
 * groups the regarding-mode Timeline renders (task 020). Each group's
 * `messages` uses the SAME `mapThreadMessageDtoToTimelineMessage` mapping the
 * thread-id mode already uses — the group's expanded body then calls
 * `buildTimeline` on those messages exactly like the thread-id mode does, so
 * there is only ONE interleave/nesting engine (no fork, ADR-012/§11).
 *
 * Design decision — group ordering: groups are ordered by their most recent
 * message's timestamp, NEWEST first (falling back to the thread's position in
 * the server response — oldest-created first — for a thread with no
 * messages). Rationale: a record's most recently active conversation is the
 * one a user opening the record view is almost always looking for; the
 * server's own thread order (createdon asc) is a construction order, not a
 * relevance order. This is a presentational sort, not an access decision
 * (NFR-03 — no message is added, removed, or hidden by this step).
 */
export function mapRegardingReadResultToGroups(result: IRegardingReadResultDto): RegardingThreadGroup[] {
  const groups = result.threads.map(thread => {
    const messages = thread.messages.map(mapThreadMessageDtoToTimelineMessage);
    return {
      threadId: thread.threadId,
      name: thread.name && thread.name.trim().length > 0 ? thread.name : UNNAMED_THREAD_LABEL,
      messageCount: thread.count,
      messages,
    };
  });

  function latestActivity(group: RegardingThreadGroup): number {
    let latest = 0;
    for (const m of group.messages) {
      const raw = m.sentOn ?? m.createdOn;
      if (!raw) continue;
      const parsed = Date.parse(raw);
      if (!Number.isNaN(parsed) && parsed > latest) latest = parsed;
    }
    return latest;
  }

  return [...groups].sort((a, b) => latestActivity(b) - latestActivity(a));
}
