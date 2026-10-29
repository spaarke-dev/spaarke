/**
 * Unit tests for `isEmptyResponse` (useBriefingRender.ts) — decides whether
 * the /render response should render the widget's "no data" state.
 *
 * Task 035 (R5 Phase B.6 hardening, per notes/inbound-from-r7/03 item 2):
 * this helper shipped without tests in R7 W12. Added `export` to the
 * previously-module-private function (minimal change per task constraint;
 * no behavior change) so it can be unit tested directly.
 *
 * Covers:
 *   - Fully-empty response (no channels, no tldr text) → true.
 *   - Partial-data-present cases (channels present; tldr summary present;
 *     tldr keyTakeaways present) → each is false.
 *   - Task 062 (FR-32): a flagged-record list ALONE no longer keeps the widget out of the
 *     empty state (the Critical Today list is retired), and a legacy `topAction` field on a
 *     stale response is ignored.
 */

import { isEmptyResponse } from '../src/hooks/useBriefingRender';
import type { NarrateResponse, TldrResult } from '../src/services/briefingService';

function emptyTldr(): TldrResult {
  return { summary: '', keyTakeaways: [], categoryCount: 0, priorityItemCount: 0 };
}

function baseResponse(overrides: Partial<NarrateResponse> = {}): NarrateResponse {
  return {
    tldr: emptyTldr(),
    channelNarratives: [],
    generatedAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

describe('isEmptyResponse', () => {
  it('fully-empty response (no channels, no tldr text) → true', () => {
    expect(isEmptyResponse(baseResponse())).toBe(true);
  });

  it('partial data: channelNarratives present → false', () => {
    const response = baseResponse({
      channelNarratives: [{ category: 'tasks-overdue', bullets: [] }],
    });
    expect(isEmptyResponse(response)).toBe(false);
  });

  it('partial data: tldr.summary present → false', () => {
    const response = baseResponse({ tldr: { ...emptyTldr(), summary: 'Three matters need attention today.' } });
    expect(isEmptyResponse(response)).toBe(false);
  });

  it('a legacy topAction on a stale response does not keep the widget out of the empty state (task 062)', () => {
    const stale = {
      tldr: { ...emptyTldr(), topAction: 'Review the Acme motion.' },
      channelNarratives: [],
      generatedAtUtc: 'x',
    };
    expect(isEmptyResponse(stale as unknown as NarrateResponse)).toBe(true);
  });

  it('partial data: tldr.keyTakeaways present (non-empty array) → false', () => {
    const response = baseResponse({ tldr: { ...emptyTldr(), keyTakeaways: ['One takeaway.'] } });
    expect(isEmptyResponse(response)).toBe(false);
  });

  it('partial data: tldr text is whitespace-only → still counted as empty (trim() check)', () => {
    const response = baseResponse({ tldr: { ...emptyTldr(), summary: '   ' } });
    expect(isEmptyResponse(response)).toBe(true);
  });

  it('a flagged-record list alone no longer keeps the widget out of the empty state (Critical Today is retired, task 062)', () => {
    const stale = {
      ...baseResponse(),
      highPriorityItems: [
        {
          entityType: 'sprk_matter',
          entityId: 'm-1',
          name: 'Acme Matter',
          highPriority: true,
          monitor: false,
          kindLabel: 'Matter',
        },
      ],
    };
    expect(isEmptyResponse(stale)).toBe(true);
  });
});
