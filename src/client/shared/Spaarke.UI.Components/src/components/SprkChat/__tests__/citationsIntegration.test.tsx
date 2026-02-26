/**
 * SprkChat - Citations Integration Tests
 *
 * Integration tests verifying the full SSE -> frontend pipeline for citations:
 * - Citations passed to last assistant message
 * - Citation markers [N] in message text rendered as CitationMarker components
 * - Unmatched markers left as plain text
 * - Citations only shown for assistant messages
 * - Combined flow: both suggestions and citations in same response
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import { screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChat } from "../SprkChat";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";

// ---------------------------------------------------------------------------
// Polyfills for jsdom (TextEncoder, TextDecoder, ReadableStream)
// ---------------------------------------------------------------------------

import { TextEncoder, TextDecoder } from "util";
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

/**
 * Custom ReadableStream polyfill that delivers chunks asynchronously.
 *
 * The async delivery is critical for integration tests: React batches state
 * updates, and the SprkChat component has an effect that only propagates
 * streamed content to the message list when `isStreaming` is true. If all
 * SSE events (token + done) are delivered synchronously, React batches
 * `setContent` and `setIsStreaming(false)` together, causing the
 * `isStreaming && streamedContent` guard in the effect to never be true.
 */
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
                        // Small async delay between chunks to allow React state batching
                        if (index > 0) {
                            await new Promise((r) => setTimeout(r, 5));
                        }
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
// Mock fetch
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

function createFetchResponse(body: unknown, status = 200): Response {
    return {
        ok: status >= 200 && status < 300,
        status,
        text: jest.fn().mockResolvedValue(JSON.stringify(body)),
        json: jest.fn().mockResolvedValue(body),
        headers: new Headers(),
    } as unknown as Response;
}

/**
 * Create an SSE streaming response that delivers events in two chunks:
 * 1. Token events (content-bearing) - allows React to render with isStreaming=true
 * 2. Metadata events (citations, suggestions, done) - completes the stream
 *
 * This split ensures at least one React render cycle where isStreaming=true
 * and streamedContent is set, so the SprkChat effect can propagate content
 * to the message list via updateLastMessage.
 */
function createSseStreamResponse(
    events: Array<Record<string, unknown>>,
    status = 200
): Response {
    const encoder = new TextEncoder();

    // Split events into tokens vs rest (citations, suggestions, done)
    const tokenEvents = events.filter(e => e.type === "token");
    const otherEvents = events.filter(e => e.type !== "token");

    const tokenChunk = encoder.encode(
        tokenEvents.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join("")
    );
    const otherChunk = encoder.encode(
        otherEvents.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join("")
    );

    const fullText = events.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join("");

    const stream = new (globalThis as any).ReadableStream({
        start(controller: any) {
            // Deliver tokens first, then metadata+done in a separate chunk
            if (tokenChunk.length > 0) {
                controller.enqueue(tokenChunk);
            }
            if (otherChunk.length > 0) {
                controller.enqueue(otherChunk);
            }
            controller.close();
        },
    });

    return {
        ok: status >= 200 && status < 300,
        status,
        body: stream,
        text: jest.fn().mockResolvedValue(fullText),
        headers: new Headers(),
    } as unknown as Response;
}

const SESSION_RESPONSE = {
    sessionId: "session-123",
    createdAt: "2026-02-23T10:00:00Z",
};

const PLAYBOOKS_RESPONSE = { playbooks: [] };

const defaultProps = {
    playbookId: "test-playbook-id",
    apiBaseUrl: "https://api.example.com",
    accessToken: "test-access-token",
};

// ---------------------------------------------------------------------------
// Fetch router: route mock responses by URL pattern
// ---------------------------------------------------------------------------

let pendingSseResponses: Response[] = [];

function setupFetchRouter() {
    pendingSseResponses = [];
    mockFetch.mockImplementation((url: string, _options?: any) => {
        if (typeof url === "string" && url.includes("/playbooks")) {
            return Promise.resolve(createFetchResponse(PLAYBOOKS_RESPONSE));
        }
        if (typeof url === "string" && url.includes("/sessions") && !url.includes("/messages")) {
            return Promise.resolve(createFetchResponse(SESSION_RESPONSE));
        }
        if (typeof url === "string" && url.includes("/messages")) {
            const response = pendingSseResponses.shift();
            if (response) {
                return Promise.resolve(response);
            }
            return Promise.resolve(createFetchResponse({ error: "No mock SSE response queued" }, 500));
        }
        return Promise.resolve(createFetchResponse({ error: "Unmocked URL" }, 404));
    });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SprkChat - Citations Integration", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        setupFetchRouter();
    });

    afterEach(() => {
        jest.restoreAllMocks();
    });

    /**
     * Helper: render SprkChat and wait for session to be established.
     * Waits for the textarea to become enabled, which confirms:
     * - Session is created (session !== null)
     * - Session loading is complete (isSessionLoading === false)
     * - Not currently streaming (isStreaming === false)
     */
    async function renderChatWithSession() {
        const user = userEvent.setup();

        await act(async () => {
            renderWithProviders(<SprkChat {...defaultProps} />);
        });

        // Wait for session to be created - textarea disabled reflects: isStreaming || !session || isSessionLoading
        await waitFor(() => {
            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            expect(nativeTextarea).not.toBeDisabled();
        });

        return user;
    }

    /**
     * Helper: type a message, queue an SSE response, and click send.
     */
    async function sendMessageAndStream(
        user: ReturnType<typeof userEvent.setup>,
        messageText: string,
        sseEvents: Array<Record<string, unknown>>
    ) {
        pendingSseResponses.push(createSseStreamResponse(sseEvents));

        const textarea = screen.getByTestId("chat-input-textarea");
        const nativeTextarea = textarea.querySelector("textarea") || textarea;
        await user.type(nativeTextarea, messageText);
        await user.click(screen.getByTestId("chat-send-button"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 1: sseFlow_CitationsEvent_RendersClickableMarkers
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_CitationsEvent_RendersClickableMarkers", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "The policy states [1] and the memo confirms [2]." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Policy Handbook", page: 42, excerpt: "Employees must comply", chunkId: "c-1" },
                        { id: 2, sourceName: "Internal Memo", page: 3, excerpt: "As confirmed by management", chunkId: "c-2" },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "What does the policy say?", sseEvents);

        // Wait for citation markers to be rendered
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        expect(screen.getByTestId("citation-marker-2")).toBeInTheDocument();

        // Markers should display [N] text
        expect(screen.getByTestId("citation-marker-1").textContent).toContain("[1]");
        expect(screen.getByTestId("citation-marker-2").textContent).toContain("[2]");
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 2: sseFlow_CitationClick_OpensPopover
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_CitationClick_OpensPopover", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "According to [1], the rule applies." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Company Rules", page: 10, excerpt: "All employees must follow rule 5.", chunkId: "cr-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Tell me about the rules", sseEvents);

        // Wait for citation marker
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // Click the citation marker to open the popover
        await user.click(screen.getByTestId("citation-marker-1"));

        // Popover should open with citation details
        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-1")).toBeInTheDocument();
        });

        // Verify popover content
        expect(screen.getByText("Company Rules")).toBeInTheDocument();
        expect(screen.getByText("Page 10")).toBeInTheDocument();
        expect(screen.getByText("All employees must follow rule 5.")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 3: sseFlow_MultipleCitations_AllRendered
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_MultipleCitations_AllRendered", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "Sources [1], [2], and [3] all confirm this." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Doc A", page: 1, excerpt: "Excerpt A", chunkId: "a-1" },
                        { id: 2, sourceName: "Doc B", page: 2, excerpt: "Excerpt B", chunkId: "b-1" },
                        { id: 3, sourceName: "Doc C", page: 3, excerpt: "Excerpt C", chunkId: "c-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Show multiple sources", sseEvents);

        // Wait for all citation markers
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
                expect(screen.getByTestId("citation-marker-2")).toBeInTheDocument();
                expect(screen.getByTestId("citation-marker-3")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 4: sseFlow_UnmatchedMarkers_LeftAsPlainText
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_UnmatchedMarkers_LeftAsPlainText", async () => {
        const user = await renderChatWithSession();

        // Response contains [1] and [5], but only citation [1] is provided
        const sseEvents = [
            { type: "token", content: "See [1] and also [5] for details." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Known Doc", page: 1, excerpt: "Known excerpt", chunkId: "k-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Reference test", sseEvents);

        // Wait for citation marker [1]
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // [5] should NOT be rendered as a CitationMarker (no matching citation data)
        expect(screen.queryByTestId("citation-marker-5")).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 5: sseFlow_NoCitations_NoMarkersRendered
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_NoCitations_NoMarkersRendered", async () => {
        const user = await renderChatWithSession();

        // Response with [1] in text but no citations event
        const sseEvents = [
            { type: "token", content: "Something with [1] in text but no citations." },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "No citations test", sseEvents);

        // Wait for message to render
        await waitFor(
            () => {
                expect(screen.getByText("Something with [1] in text but no citations.")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // No citation markers should be rendered
        expect(screen.queryByTestId("citation-marker-1")).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 6: sseFlow_PopoverShowsSourceAndExcerpt
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_PopoverShowsSourceAndExcerpt", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "Reference [1] here." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        {
                            id: 1,
                            sourceName: "Annual Financial Report 2025",
                            page: 15,
                            excerpt: "Revenue increased by 12% year-over-year driven by strong sales.",
                            chunkId: "fin-1",
                        },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Financial data", sseEvents);

        // Wait for marker
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // Click to open popover
        await user.click(screen.getByTestId("citation-marker-1"));

        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-1")).toBeInTheDocument();
        });

        // Verify all popover fields
        expect(screen.getByText("Annual Financial Report 2025")).toBeInTheDocument();
        expect(screen.getByText("Page 15")).toBeInTheDocument();
        expect(screen.getByText("Revenue increased by 12% year-over-year driven by strong sales.")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 7: sseFlow_CitationWithoutPage_NoPageShown
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_CitationWithoutPage_NoPageShown", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "See [1] for details." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Web Article", page: null, excerpt: "Online content excerpt", chunkId: "web-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Web source test", sseEvents);

        // Wait for marker
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // Click to open popover
        await user.click(screen.getByTestId("citation-marker-1"));

        await waitFor(() => {
            expect(screen.getByTestId("citation-popover-1")).toBeInTheDocument();
        });

        // Source should appear, page should NOT
        expect(screen.getByText("Web Article")).toBeInTheDocument();
        expect(screen.queryByText(/^Page\s/)).not.toBeInTheDocument();
    });
});

// ---------------------------------------------------------------------------
// Combined Flow Tests
// ---------------------------------------------------------------------------

describe("SprkChat - Combined Suggestions and Citations Flow", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        setupFetchRouter();
    });

    afterEach(() => {
        jest.restoreAllMocks();
    });

    /**
     * Helper: render SprkChat and wait for session to be established.
     */
    async function renderChatWithSession() {
        const user = userEvent.setup();

        await act(async () => {
            renderWithProviders(<SprkChat {...defaultProps} />);
        });

        // Wait for session to be created - textarea disabled reflects: isStreaming || !session || isSessionLoading
        await waitFor(() => {
            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            expect(nativeTextarea).not.toBeDisabled();
        });

        return user;
    }

    /**
     * Helper: type a message, queue an SSE response, and click send.
     */
    async function sendMessageAndStream(
        user: ReturnType<typeof userEvent.setup>,
        messageText: string,
        sseEvents: Array<Record<string, unknown>>
    ) {
        pendingSseResponses.push(createSseStreamResponse(sseEvents));

        const textarea = screen.getByTestId("chat-input-textarea");
        const nativeTextarea = textarea.querySelector("textarea") || textarea;
        await user.type(nativeTextarea, messageText);
        await user.click(screen.getByTestId("chat-send-button"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 1: combinedFlow_BothEventsPresent_BothFeaturesWork
    // ─────────────────────────────────────────────────────────────────────

    it("combinedFlow_BothEventsPresent_BothFeaturesWork", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "The document [1] states the policy." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Policy Guide", page: 5, excerpt: "Policy excerpt", chunkId: "pg-1" },
                    ],
                },
            },
            {
                type: "suggestions",
                content: null,
                data: { suggestions: ["Explain the policy", "Show related documents"] },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Tell me about the policy", sseEvents);

        // Both citations and suggestions should be present
        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
                expect(screen.getByTestId("sprkchat-suggestions")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // Verify citations
        expect(screen.getByTestId("citation-marker-1").textContent).toContain("[1]");

        // Verify suggestions
        expect(screen.getByTestId("suggestion-chip-0")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-1")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 2: combinedFlow_SuggestionsOnly_NoCitationsRendered
    // ─────────────────────────────────────────────────────────────────────

    it("combinedFlow_SuggestionsOnly_NoCitationsRendered", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "Here is a general answer." },
            {
                type: "suggestions",
                content: null,
                data: { suggestions: ["Tell me more", "What else?"] },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "General question", sseEvents);

        await waitFor(
            () => {
                expect(screen.getByTestId("sprkchat-suggestions")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // No citation markers
        expect(screen.queryByTestId("citation-marker-1")).not.toBeInTheDocument();

        // Suggestions should work
        expect(screen.getByTestId("suggestion-chip-0")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 3: combinedFlow_CitationsOnly_NoSuggestionsShown
    // ─────────────────────────────────────────────────────────────────────

    it("combinedFlow_CitationsOnly_NoSuggestionsShown", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "The data from [1] shows this." },
            {
                type: "citations",
                content: null,
                data: {
                    citations: [
                        { id: 1, sourceName: "Data Report", page: 22, excerpt: "Statistical analysis", chunkId: "dr-1" },
                    ],
                },
            },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Show me data", sseEvents);

        await waitFor(
            () => {
                expect(screen.getByTestId("citation-marker-1")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // No suggestions
        expect(screen.queryByTestId("sprkchat-suggestions")).not.toBeInTheDocument();

        // Citation should work
        expect(screen.getByTestId("citation-marker-1").textContent).toContain("[1]");
    });
});
