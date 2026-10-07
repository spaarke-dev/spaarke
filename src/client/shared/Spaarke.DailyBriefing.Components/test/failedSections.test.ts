/**
 * unified-access-control-r2 task 152 — a briefing section whose read AS THE CALLER failed is "could not be loaded",
 * never "nothing to report". The BFF names failed sections in `failedChannels` / `highPriorityFailedEntityTypes`;
 * the widget must (a) stay out of the "all caught up" empty state and (b) name the failed sections.
 */

import { isEmptyResponse } from '../src/hooks/useBriefingRender';
import { describeFailedSections } from '../src/components/failedSections';
import type { NarrateResponse } from '../src/services/briefingService';

function emptyResponse(overrides: Partial<NarrateResponse> = {}): NarrateResponse {
  return {
    tldr: { summary: '', keyTakeaways: [], topAction: '', categoryCount: 0, priorityItemCount: 0 },
    channelNarratives: [],
    generatedAtUtc: new Date().toISOString(),
    highPriorityItems: [],
    ...overrides,
  };
}

describe('failed briefing sections (task 152)', () => {
  it('an otherwise-empty response with a failed channel is NOT empty', () => {
    expect(isEmptyResponse(emptyResponse({ failedChannels: ['matters'] }))).toBe(false);
  });

  it('an otherwise-empty response with a failed High Priority entity is NOT empty', () => {
    expect(isEmptyResponse(emptyResponse({ highPriorityFailedEntityTypes: ['sprk_invoice'] }))).toBe(false);
  });

  it('no failures and no data is still empty', () => {
    expect(isEmptyResponse(emptyResponse({ failedChannels: [], highPriorityFailedEntityTypes: [] }))).toBe(true);
  });

  it('names failed channels and High Priority entities in reader terms', () => {
    expect(describeFailedSections(['matters', 'to-dos'], ['sprk_invoice', 'sprk_event'])).toEqual([
      'Matters',
      'To dos',
      'High priority (invoices, tasks)',
    ]);
  });

  it('an unknown code falls through verbatim rather than disappearing', () => {
    expect(describeFailedSections(['future-channel'], [])).toEqual(['future-channel']);
  });
});
