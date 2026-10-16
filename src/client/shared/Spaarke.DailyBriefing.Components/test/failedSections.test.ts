/**
 * unified-access-control-r2 task 152 — a briefing section whose read AS THE CALLER failed is "could not be loaded",
 * never "nothing to report". The BFF names failed sections in `failedChannels`;
 * the widget must (a) stay out of the "all caught up" empty state and (b) name the failed sections.
 */

import { isEmptyResponse } from '../src/hooks/useBriefingRender';
import { describeFailedSections } from '../src/components/failedSections';
import type { NarrateResponse } from '../src/services/briefingService';

function emptyResponse(overrides: Partial<NarrateResponse> = {}): NarrateResponse {
  return {
    tldr: { summary: '', keyTakeaways: [], categoryCount: 0, priorityItemCount: 0 },
    channelNarratives: [],
    generatedAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

describe('failed briefing sections (task 152)', () => {
  it('an otherwise-empty response with a failed channel is NOT empty', () => {
    expect(isEmptyResponse(emptyResponse({ failedChannels: ['matters'] }))).toBe(false);
  });

  it('an otherwise-empty response whose monitored-records read failed is NOT empty (task 062)', () => {
    expect(isEmptyResponse(emptyResponse({ failedChannels: ['monitored'] }))).toBe(false);
  });

  it('no failures and no data is still empty', () => {
    expect(isEmptyResponse(emptyResponse({ failedChannels: [] }))).toBe(true);
  });

  it('names failed channels in reader terms, including the monitored-records subscription', () => {
    expect(describeFailedSections(['matters', 'to-dos', 'monitored'])).toEqual([
      'Matters',
      'To dos',
      'Monitored records',
    ]);
  });

  it('an unknown code falls through verbatim rather than disappearing', () => {
    expect(describeFailedSections(['future-channel'])).toEqual(['future-channel']);
  });
});
