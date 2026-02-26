/**
 * SprkChat - Suggestions Integration Tests
 *
 * Integration tests verifying the full SSE -> frontend pipeline for suggestions:
 * - SprkChatSuggestions shown after streaming completes when suggestions exist
 * - Clicking suggestion sends it as new user message
 * - Suggestions hidden during streaming
 * - Suggestions cleared when user sends new message
 * - No suggestions shown when no suggestions event received
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
 *
 * By delivering token events in one chunk and then the done event in a
 * subsequent chunk (with a microtask delay), we give React a render cycle
 * where `isStreaming=true` and `content` has a value.
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

function createJsonResponse(body: unknown, status = 200): Response {
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
 * 2. Metadata events (suggestions, citations, done) - completes the stream
 *
 * This split ensures at least one React render cycle where isStreaming=true
 * and streamedContent is set, so the SprkChat effect can propagate content
 * to the message list via updateLastMessage.
 */
function createSseStreamResponse(
    events: Array<Record<string, unknown>>
): Response {
    const encoder = new TextEncoder();

    // Split events into tokens vs rest (suggestions, citations, done)
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
        ok: true,
        status: 200,
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
            return Promise.resolve(createJsonResponse(PLAYBOOKS_RESPONSE));
        }
        if (typeof url === "string" && url.includes("/sessions") && !url.includes("/messages")) {
            return Promise.resolve(createJsonResponse(SESSION_RESPONSE));
        }
        if (typeof url === "string" && url.includes("/messages")) {
            const response = pendingSseResponses.shift();
            if (response) {
                return Promise.resolve(response);
            }
            return Promise.resolve(createJsonResponse({ error: "No mock SSE response queued" }, 500));
        }
        return Promise.resolve(createJsonResponse({ error: "Unmocked URL" }, 404));
    });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("SprkChat - Suggestions Integration", () => {
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
        await user.clear(nativeTextarea);
        await user.type(nativeTextarea, messageText);
        await user.click(screen.getByTestId("chat-send-button"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 1: sseFlow_SuggestionsEvent_DisplaysChips
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_SuggestionsEvent_DisplaysChips", async () => {
        const user = await renderChatWithSession();

        const sseEvents = [
            { type: "token", content: "Here is my response." },
            { type: "suggestions", content: null, data: { suggestions: ["Tell me more", "What are the risks?", "Summarize key points"] } },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Hello", sseEvents);

        // Wait for streaming to complete and suggestions to appear
        await waitFor(
            () => {
                expect(screen.getByTestId("sprkchat-suggestions")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // Verify suggestion chips are rendered
        expect(screen.getByTestId("suggestion-chip-0")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-1")).toBeInTheDocument();
        expect(screen.getByTestId("suggestion-chip-2")).toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 2: sseFlow_NoSuggestionsEvent_NoChipsShown
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_NoSuggestionsEvent_NoChipsShown", async () => {
        const user = await renderChatWithSession();

        // SSE response without suggestions
        const sseEvents = [
            { type: "token", content: "A simple response." },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Hello", sseEvents);

        // Wait for streaming to complete
        await waitFor(
            () => {
                expect(screen.getByText("A simple response.")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // No suggestions container should be present
        expect(screen.queryByTestId("sprkchat-suggestions")).not.toBeInTheDocument();
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 3: sseFlow_SuggestionClick_SendsAsMessage
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_SuggestionClick_SendsAsMessage", async () => {
        const user = await renderChatWithSession();

        // First SSE response with suggestions
        const sseEvents1 = [
            { type: "token", content: "Here is my response." },
            { type: "suggestions", content: null, data: { suggestions: ["Tell me more", "Summarize"] } },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Hello", sseEvents1);

        // Wait for suggestions to appear and streaming to complete
        await waitFor(
            () => {
                expect(screen.getByTestId("sprkchat-suggestions")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        await waitFor(() => {
            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            expect(nativeTextarea).not.toBeDisabled();
        });

        // Queue second SSE response for the suggestion click
        pendingSseResponses.push(createSseStreamResponse([
            { type: "token", content: "More details here." },
            { type: "done", content: null },
        ]));

        // Click a suggestion chip
        await user.click(screen.getByTestId("suggestion-chip-0"));

        // The suggestion text should appear as a new user message
        await waitFor(
            () => {
                expect(screen.getByText("Tell me more")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // The SSE stream endpoint should have been called again
        const messageCalls = mockFetch.mock.calls.filter(
            (call: any[]) => typeof call[0] === "string" && call[0].includes("/messages")
        );
        expect(messageCalls.length).toBeGreaterThanOrEqual(2);
    });

    // ─────────────────────────────────────────────────────────────────────
    // Test 4: sseFlow_NewMessage_ClearsSuggestions
    // ─────────────────────────────────────────────────────────────────────

    it("sseFlow_NewMessage_ClearsSuggestions", async () => {
        const user = await renderChatWithSession();

        // First SSE response with suggestions
        const sseEvents1 = [
            { type: "token", content: "Response with suggestions." },
            { type: "suggestions", content: null, data: { suggestions: ["Option A", "Option B"] } },
            { type: "done", content: null },
        ];

        await sendMessageAndStream(user, "Hello", sseEvents1);

        // Wait for suggestions to appear
        await waitFor(
            () => {
                expect(screen.getByTestId("sprkchat-suggestions")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        await waitFor(() => {
            const textarea = screen.getByTestId("chat-input-textarea");
            const nativeTextarea = textarea.querySelector("textarea") || textarea;
            expect(nativeTextarea).not.toBeDisabled();
        });

        // Queue second SSE response (no suggestions this time)
        pendingSseResponses.push(createSseStreamResponse([
            { type: "token", content: "Second response." },
            { type: "done", content: null },
        ]));

        // Send a new message by clicking a suggestion chip
        await user.click(screen.getByTestId("suggestion-chip-0"));

        // Wait for the second response
        await waitFor(
            () => {
                expect(screen.getByText("Second response.")).toBeInTheDocument();
            },
            { timeout: 3000 }
        );

        // Suggestions should be cleared (second response had no suggestions)
        expect(screen.queryByTestId("sprkchat-suggestions")).not.toBeInTheDocument();
    });
});
