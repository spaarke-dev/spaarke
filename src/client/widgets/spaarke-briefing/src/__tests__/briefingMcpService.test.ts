/**
 * @jest-environment node
 *
 * The widget's briefing seam (spaarkeai-mcp-bff-api-r2 task 034): how what the `spaarke_get_briefing` tool answered becomes the
 * result the Console's component draws, which source the first read uses (the result that opened the widget, else the tool), and that
 * Refresh and a changed window call the tool. Pure logic: no DOM, no host.
 */
import {
  GET_BRIEFING_TOOL,
  OpeningResult,
  createBriefingMcpService,
  mapBriefingResult,
} from '../briefing/briefingMcpService';
import type { WidgetToolResult } from '../../../shared/mcp/widgetMcpTransport';

const BRIEFING = {
  tldr: {
    summary: 'Two matters need you.',
    keyTakeaways: ['A', 'B'],
    categoryCount: 2,
    priorityItemCount: 1,
    itemRefs: [],
  },
  channelNarratives: [{ category: 'matters', bullets: [{ narrative: 'Acme v. Globex', itemIds: ['1'] }] }],
  generatedAtUtc: '2026-11-26T08:00:00Z',
};

function transportReturning(...results: WidgetToolResult[]) {
  const callTool = jest.fn();
  for (const result of results) callTool.mockResolvedValueOnce(result);
  return { callTool };
}

describe('mapBriefingResult', () => {
  it('maps a briefing to success, with the data as the tool sent it', () => {
    const result = mapBriefingResult(BRIEFING);
    expect(result.status).toBe('success');
    if (result.status === 'success') expect(result.data.channelNarratives).toHaveLength(1);
  });

  it('fills the TL;DR fields an old briefing omitted, the way the Console does', () => {
    const result = mapBriefingResult({ ...BRIEFING, tldr: { categoryCount: 1, priorityItemCount: 0 } });
    expect(result.status).toBe('success');
    if (result.status === 'success') {
      expect(result.data.tldr.keyTakeaways).toEqual([]);
      expect(result.data.tldr.summary).toBe('');
      expect(result.data.tldr.itemRefs).toEqual([]);
    }
  });

  it.each([503, 429])('maps a %s failure to unavailable with fixed text', status => {
    const result = mapBriefingResult({ reasonCode: 'briefing.unavailable', message: 'secret detail', status });
    expect(result).toEqual({ status: 'unavailable', reason: 'The AI daily briefing is temporarily unavailable.' });
  });

  it.each([401, 403])('maps a %s failure to the sign-in message', status => {
    const result = mapBriefingResult({ reasonCode: 'x', message: 'nope', status });
    expect(result).toEqual({ status: 'unavailable', reason: 'Sign-in required to view your daily briefing.' });
  });

  it('maps another tool failure to an error that carries the tool message', () => {
    const result = mapBriefingResult({
      reasonCode: 'briefing.timeout',
      message: 'The briefing took too long.',
      status: 504,
    });
    expect(result).toEqual({ status: 'error', message: 'The briefing took too long.' });
  });

  it.each([[undefined], [null], ['text'], [[]], [{}], [{ channelNarratives: 'no' }]])(
    'maps %p (no briefing, no failure text) to the fixed error',
    value => {
      expect(mapBriefingResult(value)).toEqual({ status: 'error', message: 'Could not load the daily briefing.' });
    }
  );

  it('uses the transport text when there is no structured failure', () => {
    expect(mapBriefingResult(undefined, { isError: true, message: 'The host did not answer.' })).toEqual({
      status: 'error',
      message: 'The host did not answer.',
    });
  });

  it('does not report success for an error result even if it carries a channelNarratives array', () => {
    const result = mapBriefingResult(BRIEFING, { isError: true, message: 'failed' });
    expect(result.status).toBe('error');
  });
});

describe('OpeningResult', () => {
  it('hands the offered result to the first take, once', async () => {
    const opening = new OpeningResult();
    opening.offer(BRIEFING);
    expect(await opening.take(10)).toBe(BRIEFING);
    expect(await opening.take(10)).toBeUndefined();
  });

  it('waits for a result offered after the take', async () => {
    const opening = new OpeningResult();
    const taking = opening.take(1000);
    opening.offer(BRIEFING);
    expect(await taking).toBe(BRIEFING);
  });

  it('gives undefined when nothing arrives in the wait', async () => {
    jest.useFakeTimers();
    try {
      const opening = new OpeningResult();
      const taking = opening.take(500);
      jest.advanceTimersByTime(500);
      expect(await taking).toBeUndefined();
    } finally {
      jest.useRealTimers();
    }
  });

  it('ignores null and undefined offers, and every offer after the first', async () => {
    const opening = new OpeningResult();
    opening.offer(null);
    opening.offer(undefined);
    opening.offer(BRIEFING);
    opening.offer({ other: true });
    expect(await opening.take(10)).toBe(BRIEFING);
  });
});

describe('createBriefingMcpService', () => {
  it('uses the opening result for the first read and makes no tool call', async () => {
    const transport = transportReturning();
    const opening = new OpeningResult();
    opening.offer(BRIEFING);
    const service = createBriefingMcpService({ transport, opening, openingWaitMs: 10 });

    const result = await service.renderBriefing();

    expect(result.status).toBe('success');
    expect(transport.callTool).not.toHaveBeenCalled();
  });

  it('calls the tool for every read after the first (Refresh), passing the window', async () => {
    const transport = transportReturning({ structuredContent: BRIEFING }, { structuredContent: BRIEFING });
    const opening = new OpeningResult();
    opening.offer(BRIEFING);
    const service = createBriefingMcpService({ transport, opening, openingWaitMs: 10 });

    await service.renderBriefing();
    const refreshed = await service.renderBriefing({ dueWithinDays: 3, recencyHours: 48 });

    expect(refreshed.status).toBe('success');
    expect(transport.callTool).toHaveBeenCalledTimes(1);
    expect(transport.callTool).toHaveBeenCalledWith(GET_BRIEFING_TOOL, { dueWithinDays: 3, recencyHours: 48 });
  });

  it('asks the tool itself, with no arguments, when no opening result arrives', async () => {
    const transport = transportReturning({ structuredContent: BRIEFING });
    const service = createBriefingMcpService({ transport, opening: new OpeningResult(), openingWaitMs: 1 });

    const result = await service.renderBriefing();

    expect(result.status).toBe('success');
    expect(transport.callTool).toHaveBeenCalledWith(GET_BRIEFING_TOOL, {});
  });

  it('maps a tool failure to an error with the tool message and never throws', async () => {
    const transport = transportReturning({
      isError: true,
      reasonCode: 'briefing.failed',
      structuredContent: {
        reasonCode: 'briefing.failed',
        message: 'The briefing could not be generated.',
        status: 500,
      },
      content: [{ type: 'text', text: 'The briefing could not be generated.' }],
    });
    const service = createBriefingMcpService({ transport, opening: new OpeningResult(), openingWaitMs: 1 });

    expect(await service.renderBriefing()).toEqual({
      status: 'error',
      message: 'The briefing could not be generated.',
    });
  });

  it('maps a transport failure (no structured content) to the transport text', async () => {
    const transport = transportReturning({
      isError: true,
      reasonCode: 'widget.host_unavailable',
      content: [{ type: 'text', text: 'The host did not answer.' }],
    });
    const service = createBriefingMcpService({ transport, opening: new OpeningResult(), openingWaitMs: 1 });

    expect(await service.renderBriefing()).toEqual({ status: 'error', message: 'The host did not answer.' });
  });

  it('exposes only renderBriefing: no preferences, so the Preferences menu is hidden', () => {
    const service = createBriefingMcpService({ transport: transportReturning(), opening: new OpeningResult() });
    expect(Object.keys(service)).toEqual(['renderBriefing']);
  });
});
