/**
 * useSseStream Hook - Suggestions Integration Tests
 *
 * Tests the TYPED suggestions SSE event flow through the useSseStream hook
 * (spaarkeai-assistant-enhancements-r4 task 021a/021b — grounded, capability-backed
 * suggestions; design delta §9a wire contract):
 * - "suggestions" SSE event parsed into ISprkChatFollowup[] (capability/question/action)
 * - the legacy untyped `string[]` shape is DROPPED (never rendered → no dead-end)
 * - a capability with no targetBindingId, an action with no actionId, and an
 *   unknown/missing kind are all dropped (the structural guarantee)
 * - suggestions cleared when a new stream starts / via clearSuggestions()
 * - multiple suggestion events → last wins
 *
 * @see ADR-022 - React 16 APIs only
 */

import { renderHook, act } from '@testing-library/react';
import { useSseStream, parseSseEvent } from '../hooks/useSseStream';
import type { ISprkChatFollowup } from '../types';

// ---------------------------------------------------------------------------
// Typed follow-up fixtures (021a wire shape — camelCase, nulls serialized)
// ---------------------------------------------------------------------------

const cap = (label: string, targetBindingId: string): ISprkChatFollowup => ({
  kind: 'capability',
  label,
  targetBindingId,
  actionId: null,
});
const question = (label: string): ISprkChatFollowup => ({
  kind: 'question',
  label,
  targetBindingId: null,
  actionId: null,
});
const action = (label: string, actionId: string): ISprkChatFollowup => ({
  kind: 'action',
  label,
  targetBindingId: null,
  actionId,
});

// ---------------------------------------------------------------------------
// Polyfills for jsdom (TextEncoder, TextDecoder, ReadableStream)
// ---------------------------------------------------------------------------

import { TextEncoder, TextDecoder } from 'util';
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

// Minimal ReadableStream polyfill for fetch body mocking
if (typeof globalThis.ReadableStream === 'undefined') {
  (globalThis as any).ReadableStream = class ReadableStream {
    private _source: any;
    constructor(source: any) {
      this._source = source;
    }
    getReader() {
      const chunks: Uint8Array[] = [];
      const controller = {
        enqueue: (chunk: Uint8Array) => chunks.push(chunk),
        close: () => {},
      };
      this._source.start(controller);
      let index = 0;
      return {
        read: async () => {
          if (index < chunks.length) {
            return { done: false, value: chunks[index++] };
          }
          return { done: true, value: undefined };
        },
        cancel: async () => {},
      };
    }
  };
}

// ---------------------------------------------------------------------------
// Mock fetch helpers
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

interface SseEventShape {
  type: string;
  content?: string | null;
  suggestions?: unknown[];
  data?: Record<string, unknown>;
}

/** Encode SSE events into a ReadableStream for fetch mock ("data: {json}\n\n"). */
function createSseStream(events: SseEventShape[]): any {
  const encoder = new TextEncoder();
  const fullText = events.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join('');
  const encoded = encoder.encode(fullText);
  return new ReadableStream({
    start(controller: any) {
      controller.enqueue(encoded);
      controller.close();
    },
  });
}

function createSseResponse(events: SseEventShape[], status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    body: createSseStream(events),
    text: jest.fn().mockResolvedValue(''),
    headers: new Headers(),
  } as unknown as Response;
}

// D-AUTH-7 / Auth v2: startStream takes an AccessTokenGetter (`() => Promise<string>`).
const _TEST_TOKEN_VALUE = `header.${btoa(JSON.stringify({ tid: 'tenant-1' }))}.signature`;
const TEST_TOKEN = (): Promise<string> => Promise.resolve(_TEST_TOKEN_VALUE);

beforeEach(() => {
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// parseSseEvent - Suggestions-Specific Tests
// ---------------------------------------------------------------------------

describe('parseSseEvent - Suggestions Events', () => {
  it('parseSseEvent_TypedSuggestionsInDataProperty_ParsedCorrectly', () => {
    const items = [cap('Prioritize my tasks', 'binding-1'), question('What are the risks?')];
    const line = `data: {"type":"suggestions","content":null,"data":{"suggestions":${JSON.stringify(items)}}}`;
    const result = parseSseEvent(line);

    expect(result).not.toBeNull();
    expect(result!.type).toBe('suggestions');
    expect(result!.data?.suggestions).toEqual(items);
  });

  it('parseSseEvent_SuggestionsEventWithEmptyArray_ParsedCorrectly', () => {
    const line = 'data: {"type":"suggestions","content":null,"data":{"suggestions":[]}}';
    const result = parseSseEvent(line);

    expect(result).not.toBeNull();
    expect(result!.type).toBe('suggestions');
    expect(result!.data?.suggestions).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// useSseStream Hook - Typed Suggestions State Tests
// ---------------------------------------------------------------------------

async function runStream(events: SseEventShape[]) {
  mockFetch.mockResolvedValueOnce(createSseResponse(events));
  const { result } = renderHook(() => useSseStream());
  await act(async () => {
    result.current.startStream('https://api.example.com/stream', { message: 'hi' }, TEST_TOKEN);
    await new Promise(r => setTimeout(r, 50));
  });
  return result;
}

describe('useSseStream - Typed Suggestions Handling', () => {
  it('startStream_WithTypedSuggestionsEvent_UpdatesSuggestionsState', async () => {
    const items = [action('Upload File', 'upload'), cap('Prioritize my tasks', 'binding-1'), question('What next?')];
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      { type: 'suggestions', content: null, data: { suggestions: items } },
      { type: 'done', content: null },
    ]);

    expect(result.current.suggestions).toEqual(items);
    expect(result.current.content).toBe('Hello');
    expect(result.current.isDone).toBe(true);
  });

  it('startStream_WithTopLevelTypedSuggestions_UpdatesSuggestionsState', async () => {
    const items = [cap('Open the matter', 'binding-2'), question('How does this compare?')];
    const result = await runStream([
      { type: 'token', content: 'Response text' },
      { type: 'suggestions', content: null, suggestions: items },
      { type: 'done', content: null },
    ]);

    expect(result.current.suggestions).toEqual(items);
  });

  it('startStream_LegacyStringSuggestions_AreDropped', async () => {
    // The retired untyped `string[]` shape carries no `kind` → structurally
    // unroutable → every item dropped (no dead-end can render).
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      { type: 'suggestions', content: null, data: { suggestions: ['Ask about risks', 'Summarize'] } },
      { type: 'done', content: null },
    ]);

    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_MixedTypedAndBareItems_KeepsOnlyTyped', async () => {
    const valid = cap('Prioritize my tasks', 'binding-1');
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      {
        type: 'suggestions',
        content: null,
        data: { suggestions: [valid, 'a bare legacy string', { label: 'no kind' }] },
      },
      { type: 'done', content: null },
    ]);

    expect(result.current.suggestions).toEqual([valid]);
  });

  it('startStream_CapabilityWithoutBindingId_IsDropped', async () => {
    const result = await runStream([
      {
        type: 'suggestions',
        content: null,
        data: { suggestions: [{ kind: 'capability', label: 'X', targetBindingId: null, actionId: null }] },
      },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_ActionWithoutActionId_IsDropped', async () => {
    const result = await runStream([
      {
        type: 'suggestions',
        content: null,
        data: { suggestions: [{ kind: 'action', label: 'X', targetBindingId: null, actionId: null }] },
      },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_UnknownKind_IsDropped', async () => {
    const result = await runStream([
      { type: 'suggestions', content: null, data: { suggestions: [{ kind: 'banana', label: 'X' }] } },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_ActionWithUnroutableActionId_IsDropped', async () => {
    // An action id the client cannot route ({upload,search,select} only) is a
    // soft dead-end — dropped symmetrically with a capability lacking a binding.
    const result = await runStream([
      {
        type: 'suggestions',
        content: null,
        data: { suggestions: [{ kind: 'action', label: 'Do the thing', targetBindingId: null, actionId: 'teleport' }] },
      },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_ActionWithRoutableActionId_IsKept', async () => {
    const item = action('Upload File', 'upload');
    const result = await runStream([
      { type: 'suggestions', content: null, data: { suggestions: [item] } },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([item]);
  });

  it('startStream_NewStream_ClearsPreviousSuggestions', async () => {
    const first = [cap('Old suggestion', 'binding-old')];
    mockFetch.mockResolvedValueOnce(
      createSseResponse([
        { type: 'token', content: 'First' },
        { type: 'suggestions', content: null, data: { suggestions: first } },
        { type: 'done', content: null },
      ])
    );

    const { result } = renderHook(() => useSseStream());
    await act(async () => {
      result.current.startStream('https://api.example.com/stream', { message: 'first' }, TEST_TOKEN);
      await new Promise(r => setTimeout(r, 50));
    });
    expect(result.current.suggestions).toEqual(first);

    mockFetch.mockResolvedValueOnce(
      createSseResponse([
        { type: 'token', content: 'Second' },
        { type: 'done', content: null },
      ])
    );
    await act(async () => {
      result.current.startStream('https://api.example.com/stream', { message: 'second' }, TEST_TOKEN);
      await new Promise(r => setTimeout(r, 50));
    });

    expect(result.current.suggestions).toEqual([]);
    expect(result.current.content).toBe('Second');
  });

  it('startStream_MultipleSuggestionsEvents_LastWins', async () => {
    const firstBatch = [cap('First batch', 'binding-a')];
    const secondBatch = [cap('Second batch', 'binding-b'), question('Updated?')];
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      { type: 'suggestions', content: null, data: { suggestions: firstBatch } },
      { type: 'suggestions', content: null, data: { suggestions: secondBatch } },
      { type: 'done', content: null },
    ]);

    expect(result.current.suggestions).toEqual(secondBatch);
  });

  it('startStream_EmptySuggestionsArray_KeepsEmptyState', async () => {
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      { type: 'suggestions', content: null, data: { suggestions: [] } },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_NoSuggestionsEvent_SuggestionsRemainEmpty', async () => {
    const result = await runStream([
      { type: 'token', content: 'Hello world' },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
    expect(result.current.content).toBe('Hello world');
  });

  it('clearSuggestions_WithExistingSuggestions_ClearsState', async () => {
    const items = [cap('Suggestion 1', 'binding-1'), question('Suggestion 2?')];
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      { type: 'suggestions', content: null, data: { suggestions: items } },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual(items);

    act(() => {
      result.current.clearSuggestions();
    });
    expect(result.current.suggestions).toEqual([]);
  });

  it('clearSuggestions_WhenAlreadyEmpty_RemainsEmpty', () => {
    const { result } = renderHook(() => useSseStream());
    expect(result.current.suggestions).toEqual([]);
    act(() => {
      result.current.clearSuggestions();
    });
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_SuggestionsWithMissingSuggestionsProperty_HandledGracefully', async () => {
    const result = await runStream([
      { type: 'token', content: 'Hello' },
      { type: 'suggestions', content: null, data: {} },
      { type: 'done', content: null },
    ]);
    expect(result.current.suggestions).toEqual([]);
  });

  it('startStream_InitialState_SuggestionsAreEmpty', () => {
    const { result } = renderHook(() => useSseStream());
    expect(result.current.suggestions).toEqual([]);
    expect(result.current.isStreaming).toBe(false);
    expect(result.current.isDone).toBe(false);
  });
});
