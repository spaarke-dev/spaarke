/**
 * CommunicationTimeline.characterize.buildTimeline.test.ts (task 010, NFR-08)
 *
 * Fills two gaps `__tests__/buildTimeline.test.ts` (task 060) does not cover
 * before Phase 2/3 (bubble `ConversationView`) extends `buildTimeline`:
 *
 * 1. DAY-BOUNDARY GROUPING — the task 010 POML brief assumed `buildTimeline`
 *    already groups entries by calendar day ("day-boundary grouping" was
 *    listed as an acceptance criterion to characterize). Reading the CURRENT
 *    production code (`CommunicationTimeline.buildTimeline.ts`) shows this is
 *    NOT true: `buildTimeline` returns a flat, purely chronological
 *    `TimelineEntry[]` — each entry is exactly `{ message, depth }`, no day
 *    label, no group key, no synthetic divider rows. Day-boundary grouping
 *    does not exist yet anywhere in the timeline (`CommunicationTimeline.tsx`
 *    renders `timeline.map(entry => <MessageRow .../>)` directly with no day
 *    dividers). This is Phase 2/3 NET-NEW work, not a modification of
 *    existing behavior — see `projects/messaging-communication-app-r3/notes/
 *    task-010-notes.md` for the full note to Phase 2/3 authors. The tests
 *    below pin the CURRENT flat shape so that work lands as an additive
 *    change with a loud, obvious diff against this file.
 *
 * 2. The `MAX_DEPTH_HOPS` (64) pathological-chain guard in `computeDepth` —
 *    the existing suite's cycle-safety test exercises a 2-3 message cycle,
 *    but not a strictly non-cyclic chain that simply exceeds the hop cap.
 */
import { buildTimeline } from '../CommunicationTimeline.buildTimeline';
import type { TimelineMessage } from '../CommunicationTimeline.types';

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

describe('buildTimeline — day-boundary grouping is CURRENTLY ABSENT (see file header)', () => {
  it('returns a flat array across a multi-day span — no day-divider/group rows are interspersed', () => {
    const day1 = msg('day1-a', { sentOn: '2026-07-01T09:00:00Z' });
    const day2 = msg('day2-a', { sentOn: '2026-07-02T09:00:00Z' });
    const day3 = msg('day3-a', { sentOn: '2026-07-03T09:00:00Z' });

    const result = buildTimeline([day3, day1, day2]);

    // Chronological, flat — output length equals input length exactly (no synthetic day-divider entries).
    expect(result).toHaveLength(3);
    expect(result.map(e => e.message.id)).toEqual(['day1-a', 'day2-a', 'day3-a']);
  });

  it('each entry is exactly {message, depth} — no dayLabel/groupKey/isNewDay field is present', () => {
    const result = buildTimeline([msg('a', { sentOn: '2026-07-01T09:00:00Z' })]);
    expect(Object.keys(result[0]).sort()).toEqual(['depth', 'message']);
  });
});

describe('buildTimeline — MAX_DEPTH_HOPS pathological (non-cyclic) chain guard', () => {
  it('a strictly linear reply chain far past the 64-hop cap degrades the tail message to depth 0', () => {
    const CHAIN_LENGTH = 70; // root + 69 replies — well past the 64-hop cap
    const baseMs = Date.parse('2026-07-01T09:00:00Z');
    const messages: TimelineMessage[] = [];
    for (let i = 0; i < CHAIN_LENGTH; i += 1) {
      messages.push(
        msg(`m${i}`, {
          // One message per minute via epoch-ms arithmetic (not string padding) so this stays
          // valid regardless of CHAIN_LENGTH — a naive `minutes = i` string template breaks past 59.
          sentOn: new Date(baseMs + i * 60_000).toISOString(),
          inReplyTo: i === 0 ? undefined : `m${i - 1}`,
        })
      );
    }

    const result = buildTimeline(messages);
    const tail = result[result.length - 1];
    expect(tail.message.id).toBe(`m${CHAIN_LENGTH - 1}`);
    // Pathologically long (non-cyclic) chain degrades to root rather than reporting depth 69 —
    // same defensive posture as the cycle-safety guard, exercised here for the non-cyclic case.
    expect(tail.depth).toBe(0);
  });

  it('a short reply chain well under the cap reports its true depth (contrast case — not degraded)', () => {
    const root = msg('root', { sentOn: '2026-07-01T09:00:00Z' });
    const r1 = msg('r1', { sentOn: '2026-07-01T09:01:00Z', inReplyTo: 'root' });
    const r2 = msg('r2', { sentOn: '2026-07-01T09:02:00Z', inReplyTo: 'r1' });
    const r3 = msg('r3', { sentOn: '2026-07-01T09:03:00Z', inReplyTo: 'r2' });

    const result = buildTimeline([root, r1, r2, r3]);
    const byId = new Map(result.map(e => [e.message.id, e.depth]));

    expect(byId.get('root')).toBe(0);
    expect(byId.get('r1')).toBe(1);
    expect(byId.get('r2')).toBe(2);
    expect(byId.get('r3')).toBe(3);
  });
});
