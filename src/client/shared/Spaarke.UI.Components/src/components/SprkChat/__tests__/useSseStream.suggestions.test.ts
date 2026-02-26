/**
 * useSseStream Hook - Suggestions Integration Tests
 *
 * Tests the suggestions SSE event flow through the useSseStream hook:
 * - "suggestions" SSE event type parsed correctly
 * - Suggestions state updated with string array
 * - Suggestions cleared when new stream starts
 * - Multiple suggestion events handled (last wins)
 * - Missing/empty suggestions array handled gracefully
 * - Suggestions cleared by clearSuggestions()
 *
 * @see ADR-022 - React 16 APIs only
 */

import { renderHook, act } from "@testing-library/react";
import { useSseStream, parseSseEvent } from "../hooks/useSseStream";

// ---------------------------------------------------------------------------
// Polyfills for jsdom (TextEncoder, TextDecoder, ReadableStream)
// ---------------------------------------------------------------------------

import { TextEncoder, TextDecoder } from "util";
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

// Minimal ReadableStream polyfill for fetch body mocking
if (typeof globalThis.ReadableStream === "undefined") {
    (globalThis as any).ReadableStream = class ReadableStream {
        private _source: any;
        constructor(source: any) {
            this._source = source;
        }
        getReader() {
            const chunks: Uint8Array[] = [];
            let closed = false;
            const controller = {
                enqueue: (chunk: Uint8Array) => chunks.push(chunk),
                close: () => { closed = true; },
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

/**
 * Encode SSE events into a ReadableStream for fetch mock.
 * Each event is formatted as "data: {json}\n\n".
 */
function createSseStream(events: Array<{ type: string; content?: string | null; suggestions?: string[]; data?: Record<string, unknown> }>): any {
    const encoder = new TextEncoder();
    const lines = events.map((evt) => `data: ${JSON.stringify(evt)}\n\n`);
    const fullText = lines.join("");
    const encoded = encoder.encode(fullText);

    return new ReadableStream({
        start(controller: any) {
            controller.enqueue(encoded);
            controller.close();
        },
    });
}

function createSseResponse(events: Array<{ type: string; content?: string | null; suggestions?: string[]; data?: Record<string, unknown> }>, status = 200): Response {
    return {
        ok: status >= 200 && status < 300,
        status,
        body: createSseStream(events),
        text: jest.fn().mockResolvedValue(""),
        headers: new Headers(),
    } as unknown as Response;
}

// A simple JWT-like token for extractTenantId (base64-encoded payload with tid)
const TEST_TOKEN = `header.${btoa(JSON.stringify({ tid: "tenant-1" }))}.signature`;

beforeEach(() => {
    jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// parseSseEvent - Suggestions-Specific Tests
// ---------------------------------------------------------------------------

describe("parseSseEvent - Suggestions Events", () => {
    it("parseSseEvent_SuggestionsEventWithDataProperty_ParsedCorrectly", () => {
        const line = 'data: {"type":"suggestions","content":null,"data":{"suggestions":["s1","s2","s3"]}}';
        const result = parseSseEvent(line);

        expect(result).not.toBeNull();
        expect(result!.type).toBe("suggestions");
        expect(result!.data?.suggestions).toEqual(["s1", "s2", "s3"]);
    });

    it("parseSseEvent_SuggestionsEventWithTopLevelProperty_ParsedCorrectly", () => {
        const line = 'data: {"type":"suggestions","content":null,"suggestions":["s1","s2"]}';
        const result = parseSseEvent(line);

        expect(result).not.toBeNull();
        expect(result!.type).toBe("suggestions");
        expect(result!.suggestions).toEqual(["s1", "s2"]);
    });

    it("parseSseEvent_SuggestionsEventWithEmptyArray_ParsedCorrectly", () => {
        const line = 'data: {"type":"suggestions","content":null,"data":{"suggestions":[]}}';
        const result = parseSseEvent(line);

        expect(result).not.toBeNull();
        expect(result!.type).toBe("suggestions");
        expect(result!.data?.suggestions).toEqual([]);
    });
});

// ---------------------------------------------------------------------------
// useSseStream Hook - Suggestions State Tests
// ---------------------------------------------------------------------------

describe("useSseStream - Suggestions Handling", () => {
    it("startStream_WithSuggestionsEvent_UpdatesSuggestionsState", async () => {
        const events = [
            { type: "token", content: "Hello" },
            { type: "suggestions", content: null, data: { suggestions: ["Ask about risks", "Summarize", "Next steps"] } },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            // Wait for stream to complete
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.suggestions).toEqual(["Ask about risks", "Summarize", "Next steps"]);
        expect(result.current.content).toBe("Hello");
        expect(result.current.isDone).toBe(true);
    });

    it("startStream_WithTopLevelSuggestions_UpdatesSuggestionsState", async () => {
        const events = [
            { type: "token", content: "Response text" },
            { type: "suggestions", content: null, suggestions: ["Option A", "Option B"] },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.suggestions).toEqual(["Option A", "Option B"]);
    });

    it("startStream_NewStream_ClearsPreviousSuggestions", async () => {
        // First stream with suggestions
        const events1 = [
            { type: "token", content: "First" },
            { type: "suggestions", content: null, data: { suggestions: ["Old suggestion"] } },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events1));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "first" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.suggestions).toEqual(["Old suggestion"]);

        // Second stream - suggestions should be cleared at start
        const events2 = [
            { type: "token", content: "Second" },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events2));

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "second" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // Suggestions should be cleared because second stream had no suggestions event
        expect(result.current.suggestions).toEqual([]);
        expect(result.current.content).toBe("Second");
    });

    it("startStream_MultipleSuggestionsEvents_LastWins", async () => {
        const events = [
            { type: "token", content: "Hello" },
            { type: "suggestions", content: null, data: { suggestions: ["First batch"] } },
            { type: "suggestions", content: null, data: { suggestions: ["Second batch", "Updated"] } },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // The last suggestions event should have overwritten the first
        expect(result.current.suggestions).toEqual(["Second batch", "Updated"]);
    });

    it("startStream_EmptySuggestionsArray_KeepsEmptyState", async () => {
        const events = [
            { type: "token", content: "Hello" },
            { type: "suggestions", content: null, data: { suggestions: [] } },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // Empty suggestions array should not update state (filtered by parseSuggestions)
        expect(result.current.suggestions).toEqual([]);
    });

    it("startStream_SuggestionsWithNonStringElements_FiltersInvalid", async () => {
        // Simulate malformed suggestions where some elements are not strings
        const events = [
            { type: "token", content: "Hello" },
            {
                type: "suggestions",
                content: null,
                data: { suggestions: ["Valid suggestion", "", "Another valid one"] },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // Empty strings should be filtered out by parseSuggestions
        expect(result.current.suggestions).toEqual(["Valid suggestion", "Another valid one"]);
    });

    it("startStream_NoSuggestionsEvent_SuggestionsRemainEmpty", async () => {
        const events = [
            { type: "token", content: "Hello world" },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.suggestions).toEqual([]);
        expect(result.current.content).toBe("Hello world");
    });

    it("clearSuggestions_WithExistingSuggestions_ClearsState", async () => {
        const events = [
            { type: "token", content: "Hello" },
            { type: "suggestions", content: null, data: { suggestions: ["Suggestion 1", "Suggestion 2"] } },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.suggestions).toEqual(["Suggestion 1", "Suggestion 2"]);

        // Call clearSuggestions
        act(() => {
            result.current.clearSuggestions();
        });

        expect(result.current.suggestions).toEqual([]);
    });

    it("clearSuggestions_WhenAlreadyEmpty_RemainsEmpty", () => {
        const { result } = renderHook(() => useSseStream());

        // Initial state should have empty suggestions
        expect(result.current.suggestions).toEqual([]);

        act(() => {
            result.current.clearSuggestions();
        });

        expect(result.current.suggestions).toEqual([]);
    });

    it("startStream_SuggestionsWithMissingSuggestionsProperty_HandledGracefully", async () => {
        // Event with type "suggestions" but no suggestions data at all
        const events = [
            { type: "token", content: "Hello" },
            { type: "suggestions", content: null, data: {} },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // No suggestions should be set — parseSuggestions returns [] for missing data
        expect(result.current.suggestions).toEqual([]);
    });

    it("startStream_InitialState_SuggestionsAreEmpty", () => {
        const { result } = renderHook(() => useSseStream());

        expect(result.current.suggestions).toEqual([]);
        expect(result.current.isStreaming).toBe(false);
        expect(result.current.isDone).toBe(false);
    });
});
