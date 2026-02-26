/**
 * MockFetchApi - Configurable mock for the global fetch() API.
 *
 * Provides route-based response configuration for testing hooks and services
 * that call the BFF API (analysisApi.ts).
 *
 * @see services/analysisApi.ts
 */

export interface MockRoute {
    /** URL pattern to match (exact match or string includes) */
    pattern: string;
    /** HTTP method to match (default: any) */
    method?: string;
    /** Mock response to return */
    response: MockResponse;
}

export interface MockResponse {
    status?: number;
    statusText?: string;
    ok?: boolean;
    headers?: Record<string, string>;
    body?: unknown;
    /** If set, fetch will reject with this error instead of resolving */
    networkError?: Error;
    /** Delay before resolving (ms) */
    delayMs?: number;
}

/**
 * Install a mock fetch implementation that routes requests based on URL patterns.
 * Returns a teardown function to restore the original fetch.
 */
export function installMockFetch(routes: MockRoute[]): {
    teardown: () => void;
    fetchSpy: jest.Mock;
    getCallHistory: () => Array<{ url: string; init?: RequestInit }>;
} {
    const originalFetch = globalThis.fetch;
    const callHistory: Array<{ url: string; init?: RequestInit }> = [];

    const fetchSpy = jest.fn(
        async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
            const url = typeof input === "string" ? input : input.toString();
            const method = init?.method?.toUpperCase() ?? "GET";

            callHistory.push({ url, init });

            // Find matching route
            const route = routes.find((r) => {
                const methodMatch = !r.method || r.method.toUpperCase() === method;
                const urlMatch = url.includes(r.pattern);
                return methodMatch && urlMatch;
            });

            if (!route) {
                return createMockResponse({
                    status: 404,
                    statusText: "Not Found",
                    ok: false,
                    body: { type: "about:blank", title: "Not Found", status: 404 },
                });
            }

            const mockResp = route.response;

            // Simulate network error
            if (mockResp.networkError) {
                if (mockResp.delayMs) {
                    await delay(mockResp.delayMs);
                }
                throw mockResp.networkError;
            }

            // Simulate delay
            if (mockResp.delayMs) {
                await delay(mockResp.delayMs);
            }

            return createMockResponse(mockResp);
        }
    );

    globalThis.fetch = fetchSpy;

    return {
        teardown: () => {
            globalThis.fetch = originalFetch;
        },
        fetchSpy,
        getCallHistory: () => [...callHistory],
    };
}

function createMockResponse(mock: MockResponse): Response {
    const status = mock.status ?? 200;
    const ok = mock.ok ?? (status >= 200 && status < 300);
    const statusText = mock.statusText ?? (ok ? "OK" : "Error");
    const bodyStr =
        mock.body !== undefined ? JSON.stringify(mock.body) : "";

    const headers = new Headers({
        "Content-Type": "application/json",
        ...(mock.headers ?? {}),
    });

    return {
        ok,
        status,
        statusText,
        headers,
        json: async () => (mock.body !== undefined ? mock.body : {}),
        text: async () => bodyStr,
        blob: async () => new Blob([bodyStr], { type: "application/json" }),
        arrayBuffer: async () => new TextEncoder().encode(bodyStr).buffer,
        clone: () => createMockResponse(mock),
        body: null,
        bodyUsed: false,
        formData: async () => new FormData(),
        redirected: false,
        type: "basic" as ResponseType,
        url: "",
        bytes: async () => new Uint8Array(),
    } as Response;
}

function delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
}
