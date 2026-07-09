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
 *   - Fully-empty response (no channels, no tldr text, no high-priority
 *     items) → true.
 *   - Partial-data-present cases (channels present; tldr summary present;
 *     tldr topAction present; tldr keyTakeaways present; high-priority items
 *     present per R7 W12 feedback item 9) → each is false.
 */

import { isEmptyResponse } from '../src/hooks/useBriefingRender';
import type { NarrateResponse, TldrResult } from '../src/services/briefingService';

function emptyTldr(): TldrResult {
  return { summary: '', keyTakeaways: [], topAction: '', categoryCount: 0, priorityItemCount: 0 };
}

function baseResponse(overrides: Partial<NarrateResponse> = {}): NarrateResponse {
  return {
    tldr: emptyTldr(),
    channelNarratives: [],
    generatedAtUtc: new Date().toISOString(),
    highPriorityItems: [],
    ...overrides,
  };
}

describe('isEmptyResponse', () => {
  it('fully-empty response (no channels, no tldr text, no high-priority items) → true', () => {
    expect(isEmptyResponse(baseResponse())).toBe(true);
  });

  it('fully-empty response with highPriorityItems omitted entirely → true', () => {
    const response = baseResponse();
    delete response.highPriorityItems;
    expect(isEmptyResponse(response)).toBe(true);
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

  it('partial data: tldr.topAction present → false', () => {
    const response = baseResponse({ tldr: { ...emptyTldr(), topAction: 'Review the Acme motion.' } });
    expect(isEmptyResponse(response)).toBe(false);
  });

  it('partial data: tldr.keyTakeaways present (non-empty array) → false', () => {
    const response = baseResponse({ tldr: { ...emptyTldr(), keyTakeaways: ['One takeaway.'] } });
    expect(isEmptyResponse(response)).toBe(false);
  });

  it('partial data: tldr text is whitespace-only → still counted as empty (trim() check)', () => {
    const response = baseResponse({ tldr: { ...emptyTldr(), summary: '   ', topAction: '  ' } });
    expect(isEmptyResponse(response)).toBe(true);
  });

  it('partial data: highPriorityItems present alone (R7 W12 feedback item 9) → false', () => {
    const response = baseResponse({
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
    });
    expect(isEmptyResponse(response)).toBe(false);
  });
});
