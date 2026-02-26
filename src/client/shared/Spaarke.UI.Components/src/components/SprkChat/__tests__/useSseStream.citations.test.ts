/**
 * useSseStream Hook - Citations Integration Tests
 *
 * Tests the citations SSE event flow through the useSseStream hook:
 * - "citations" SSE event type parsed correctly
 * - mapSseCitations converts SSE format (sourceName) to ICitation format (source)
 * - Citations state updated with ICitation array
 * - Citations cleared when new stream starts
 * - Page number optional (undefined when not provided)
 * - Empty citations array handled gracefully
 * - Event ordering: tokens -> citations -> suggestions -> done
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
 */
function createSseStream(events: Array<Record<string, unknown>>): any {
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

function createSseResponse(events: Array<Record<string, unknown>>, status = 200): Response {
    return {
        ok: status >= 200 && status < 300,
        status,
        body: createSseStream(events),
        text: jest.fn().mockResolvedValue(""),
        headers: new Headers(),
    } as unknown as Response;
}

const TEST_TOKEN = `header.${btoa(JSON.stringify({ tid: "tenant-1" }))}.signature`;

beforeEach(() => {
    jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// parseSseEvent - Citations-Specific Tests
// ---------------------------------------------------------------------------

describe("parseSseEvent - Citations Events", () => {
    it("parseSseEvent_CitationsEvent_ParsedCorrectly", () => {
        const line = 'data: {"type":"citations","content":null,"data":{"citations":[{"id":1,"sourceName":"Doc.pdf","page":5,"excerpt":"Some text","chunkId":"c1"}]}}';
        const result = parseSseEvent(line);

        expect(result).not.toBeNull();
        expect(result!.type).toBe("citations");
        expect(result!.data?.citations).toHaveLength(1);
        expect(result!.data!.citations![0].sourceName).toBe("Doc.pdf");
    });

    it("parseSseEvent_CitationsEventMultipleItems_ParsedCorrectly", () => {
        const citations = [
            { id: 1, sourceName: "Doc A.pdf", page: 1, excerpt: "Excerpt A", chunkId: "c-a" },
            { id: 2, sourceName: "Doc B.pdf", page: null, excerpt: "Excerpt B", chunkId: "c-b" },
            { id: 3, sourceName: "Doc C.pdf", page: 10, excerpt: "Excerpt C", chunkId: "c-c" },
        ];
        const line = `data: ${JSON.stringify({ type: "citations", content: null, data: { citations } })}`;
        const result = parseSseEvent(line);

        expect(result).not.toBeNull();
        expect(result!.data?.citations).toHaveLength(3);
    });

    it("parseSseEvent_CitationsEventEmptyArray_ParsedCorrectly", () => {
        const line = 'data: {"type":"citations","content":null,"data":{"citations":[]}}';
        const result = parseSseEvent(line);

        expect(result).not.toBeNull();
        expect(result!.type).toBe("citations");
        expect(result!.data?.citations).toEqual([]);
    });
});

// ---------------------------------------------------------------------------
// useSseStream Hook - Citations State Tests
// ---------------------------------------------------------------------------

describe("useSseStream - Citations Handling", () => {
    it("startStream_WithCitationsEvent_UpdatesCitationsState", async () => {
        const events = [
            { type: "token", content: "See source [1] and [2]." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Policy Handbook", page: 42, excerpt: "Employees must comply", chunkId: "chunk-1" },
                        { id: 2, sourceName: "Internal Memo", page: null, excerpt: "Brief excerpt", chunkId: "chunk-2" },
                    ],
                },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.citations).toHaveLength(2);

        // Verify mapSseCitations converted sourceName -> source
        expect(result.current.citations[0]).toEqual({
            id: 1,
            source: "Policy Handbook",
            page: 42,
            excerpt: "Employees must comply",
            chunkId: "chunk-1",
        });

        expect(result.current.citations[1]).toEqual({
            id: 2,
            source: "Internal Memo",
            page: undefined,
            excerpt: "Brief excerpt",
            chunkId: "chunk-2",
        });
    });

    it("startStream_CitationWithNullPage_PageIsUndefined", async () => {
        const events = [
            { type: "token", content: "Reference [1]" },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Web Article", page: null, excerpt: "Article text", chunkId: "chunk-w1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // page: null from SSE should become page: undefined in ICitation
        expect(result.current.citations[0].page).toBeUndefined();
    });

    it("startStream_CitationWithPageNumber_PageIsPreserved", async () => {
        const events = [
            { type: "token", content: "See [1]" },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Annual Report", page: 7, excerpt: "Revenue grew", chunkId: "chunk-r1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.citations[0].page).toBe(7);
    });

    it("startStream_NewStream_ClearsPreviousCitations", async () => {
        // First stream with citations
        const events1 = [
            { type: "token", content: "First [1]" },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Old Doc", page: 1, excerpt: "Old text", chunkId: "old-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events1));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "first" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.citations).toHaveLength(1);

        // Second stream without citations
        const events2 = [
            { type: "token", content: "Second response" },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events2));

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "second" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // Citations should be cleared
        expect(result.current.citations).toEqual([]);
    });

    it("startStream_EmptyCitationsArray_NoCitationsSet", async () => {
        const events = [
            { type: "token", content: "Hello" },
            {
                type: "citations",
                content: null,
                data: { citations: [] },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // mapSseCitations returns [] for empty array, so setCitations([]) is called
        expect(result.current.citations).toEqual([]);
    });

    it("startStream_NoCitationsEvent_CitationsRemainEmpty", async () => {
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

        expect(result.current.citations).toEqual([]);
    });

    it("startStream_CitationsWithoutDataProperty_NoCitationsSet", async () => {
        // Event with type "citations" but missing data.citations
        const events = [
            { type: "token", content: "Hello" },
            { type: "citations", content: null },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // The guard `event.data?.citations` should prevent setCitations from being called
        expect(result.current.citations).toEqual([]);
    });

    it("startStream_MapSseCitations_ConvertsSourceNameToSource", async () => {
        const events = [
            { type: "token", content: "Ref [1]" },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        {
                            id: 1,
                            sourceName: "My Document.pdf",
                            page: 3,
                            excerpt: "Important section about compliance",
                            chunkId: "chunk-abc",
                        },
                    ],
                },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // Verify the mapping: sourceName -> source, and all other fields preserved
        const citation = result.current.citations[0];
        expect(citation.source).toBe("My Document.pdf");
        expect(citation.id).toBe(1);
        expect(citation.page).toBe(3);
        expect(citation.excerpt).toBe("Important section about compliance");
        expect(citation.chunkId).toBe("chunk-abc");
        // sourceName should NOT be present on the ICitation type
        expect((citation as any).sourceName).toBeUndefined();
    });

    it("startStream_InitialState_CitationsAreEmpty", () => {
        const { result } = renderHook(() => useSseStream());

        expect(result.current.citations).toEqual([]);
    });
});

// ---------------------------------------------------------------------------
// Event Ordering Tests
// ---------------------------------------------------------------------------

describe("useSseStream - Event Ordering", () => {
    it("startStream_EventOrdering_TokensCitationsSuggestionsDone", async () => {
        const events = [
            { type: "token", content: "Part 1 " },
            { type: "token", content: "Part 2 [1]" },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Source A", page: 1, excerpt: "Excerpt A", chunkId: "c-a" },
                    ],
                },
            },
            {
                type: "suggestions",
                content: null,
                data: { suggestions: ["Follow-up Q1", "Follow-up Q2"] },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        // All event types should have been processed correctly
        expect(result.current.content).toBe("Part 1 Part 2 [1]");
        expect(result.current.citations).toHaveLength(1);
        expect(result.current.citations[0].source).toBe("Source A");
        expect(result.current.suggestions).toEqual(["Follow-up Q1", "Follow-up Q2"]);
        expect(result.current.isDone).toBe(true);
        expect(result.current.isStreaming).toBe(false);
    });

    it("startStream_BothCitationsAndSuggestions_BothPopulated", async () => {
        const events = [
            { type: "token", content: "Response with [1] and [2] references." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Contract.pdf", page: 12, excerpt: "Section 3.1", chunkId: "c-1" },
                        { id: 2, sourceName: "Amendment.docx", page: null, excerpt: "Revised terms", chunkId: "c-2" },
                    ],
                },
            },
            {
                type: "suggestions",
                content: null,
                data: { suggestions: ["Explain section 3.1", "What changed in the amendment?", "Summarize key differences"] },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.citations).toHaveLength(2);
        expect(result.current.suggestions).toHaveLength(3);
        expect(result.current.isDone).toBe(true);
    });

    it("startStream_SuggestionsOnly_NoCitationsPresent", async () => {
        const events = [
            { type: "token", content: "A general response." },
            {
                type: "suggestions",
                content: null,
                data: { suggestions: ["Tell me more", "What else?"] },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.citations).toEqual([]);
        expect(result.current.suggestions).toEqual(["Tell me more", "What else?"]);
    });

    it("startStream_CitationsOnly_NoSuggestionsPresent", async () => {
        const events = [
            { type: "token", content: "Per [1], the policy states..." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Policy.pdf", page: 5, excerpt: "Section on compliance", chunkId: "p-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];
        mockFetch.mockResolvedValueOnce(createSseResponse(events));

        const { result } = renderHook(() => useSseStream());

        await act(async () => {
            result.current.startStream("https://api.example.com/stream", { message: "hi" }, TEST_TOKEN);
            await new Promise((r) => setTimeout(r, 50));
        });

        expect(result.current.citations).toHaveLength(1);
        expect(result.current.suggestions).toEqual([]);
    });
});
