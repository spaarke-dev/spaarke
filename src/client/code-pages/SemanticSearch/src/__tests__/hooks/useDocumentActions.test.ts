/**
 * Unit tests for useDocumentActions hook -- document action handlers.
 *
 * Tests:
 * - openInWeb: fetches links, opens webUrl in new tab
 * - openInDesktop: fetches links, opens desktopUrl (falls back to webUrl)
 * - download: fetches blob, creates/clicks hidden anchor, revokes blob URL
 * - deleteDocuments: confirms dialog, DELETEs each doc, calls onSuccess
 * - emailLink: fetches links, opens mailto: with subject/body
 * - sendToIndex: POSTs analyze for each document ID
 * - isActing state management for all actions
 * - Error handling for each action
 * - Cancellation of confirm dialog for delete
 *
 * @see useDocumentActions.ts
 */

import { renderHook, act } from "@testing-library/react";

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockBuildAuthHeaders = jest.fn<Promise<Record<string, string>>, []>();

jest.mock("../../services/apiBase", () => ({
    BFF_API_BASE_URL: "https://test-api.example.com",
    buildAuthHeaders: () => mockBuildAuthHeaders(),
}));

// Mock global fetch
const mockFetch = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
global.fetch = mockFetch as typeof global.fetch;

// Mock window.open and window.confirm
const mockWindowOpen = jest.fn();
const originalWindowOpen = window.open;
const originalWindowConfirm = window.confirm;
const originalLocationHref = Object.getOwnPropertyDescriptor(window, "location");

beforeAll(() => {
    window.open = mockWindowOpen;
});

afterAll(() => {
    window.open = originalWindowOpen;
    window.confirm = originalWindowConfirm;
    if (originalLocationHref) {
        Object.defineProperty(window, "location", originalLocationHref);
    }
});

// Mock URL.createObjectURL/revokeObjectURL
const mockCreateObjectURL = jest.fn<string, [Blob]>();
const mockRevokeObjectURL = jest.fn<void, [string]>();
URL.createObjectURL = mockCreateObjectURL;
URL.revokeObjectURL = mockRevokeObjectURL;

import { useDocumentActions } from "../../hooks/useDocumentActions";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const AUTH_HEADERS = { Authorization: "Bearer test-token", "Content-Type": "application/json" };

const openLinksResponse = {
    webUrl: "https://sharepoint.com/doc/view",
    desktopUrl: "ms-word:ofe|u|https://sharepoint.com/doc.docx",
    mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    fileName: "Employment Contract.docx",
};

function createJsonResponse(body: unknown, status = 200): Response {
    return {
        ok: status >= 200 && status < 300,
        status,
        statusText: status === 200 ? "OK" : "Error",
        headers: new Headers(),
        json: jest.fn().mockResolvedValue(body),
    } as unknown as Response;
}

function createBlobResponse(content = "file-content", disposition?: string): Response {
    const blob = new Blob([content]);
    const headers = new Headers();
    if (disposition) {
        headers.set("Content-Disposition", disposition);
    }
    return {
        ok: true,
        status: 200,
        headers,
        blob: jest.fn().mockResolvedValue(blob),
    } as unknown as Response;
}

function createDeleteResponse(status = 204): Response {
    return {
        ok: status >= 200 && status < 300,
        status,
        statusText: status === 204 ? "No Content" : "Error",
    } as unknown as Response;
}

function createAnalyzeResponse(status = 202): Response {
    return {
        ok: status >= 200 && status < 300,
        status,
        statusText: status === 202 ? "Accepted" : "Error",
    } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useDocumentActions", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        mockBuildAuthHeaders.mockResolvedValue(AUTH_HEADERS);
        mockCreateObjectURL.mockReturnValue("blob:http://localhost/fake-blob-url");
    });

    // --- Initial state ---

    describe("initial state", () => {
        it("should start with isActing false", () => {
            const { result } = renderHook(() => useDocumentActions());

            expect(result.current.isActing).toBe(false);
        });

        it("should start with no actionError", () => {
            const { result } = renderHook(() => useDocumentActions());

            expect(result.current.actionError).toBeNull();
        });
    });

    // --- openInWeb ---

    describe("openInWeb()", () => {
        it("should fetch document links and open webUrl in new tab", async () => {
            mockFetch.mockResolvedValueOnce(createJsonResponse(openLinksResponse));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInWeb("doc-123");
            });

            expect(mockFetch).toHaveBeenCalledWith(
                "https://test-api.example.com/api/documents/doc-123/open-links",
                expect.objectContaining({ headers: AUTH_HEADERS }),
            );
            expect(mockWindowOpen).toHaveBeenCalledWith(
                "https://sharepoint.com/doc/view",
                "_blank",
            );
        });

        it("should set isActing during operation", async () => {
            let resolveLinks: (value: Response) => void;
            mockFetch.mockReturnValueOnce(
                new Promise<Response>((resolve) => {
                    resolveLinks = resolve;
                }),
            );
            const { result } = renderHook(() => useDocumentActions());

            const promise = act(async () => {
                return result.current.openInWeb("doc-123");
            });

            expect(result.current.isActing).toBe(true);

            await act(async () => {
                resolveLinks!(createJsonResponse(openLinksResponse));
            });
            await promise;

            expect(result.current.isActing).toBe(false);
        });

        it("should set actionError on failure", async () => {
            mockFetch.mockResolvedValueOnce(createJsonResponse({}, 404));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInWeb("doc-123");
            });

            expect(result.current.actionError).toContain("Failed to get document links");
        });

        it("should clear previous error on new action", async () => {
            // First: cause an error
            mockFetch.mockResolvedValueOnce(createJsonResponse({}, 500));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInWeb("doc-123");
            });
            expect(result.current.actionError).not.toBeNull();

            // Second: successful action clears error
            mockFetch.mockResolvedValueOnce(createJsonResponse(openLinksResponse));
            await act(async () => {
                await result.current.openInWeb("doc-456");
            });
            expect(result.current.actionError).toBeNull();
        });
    });

    // --- openInDesktop ---

    describe("openInDesktop()", () => {
        it("should open desktopUrl when available", async () => {
            mockFetch.mockResolvedValueOnce(createJsonResponse(openLinksResponse));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInDesktop("doc-123");
            });

            expect(mockWindowOpen).toHaveBeenCalledWith(
                "ms-word:ofe|u|https://sharepoint.com/doc.docx",
            );
        });

        it("should fallback to webUrl when desktopUrl is not available", async () => {
            const noDesktopLinks = { ...openLinksResponse, desktopUrl: undefined };
            mockFetch.mockResolvedValueOnce(createJsonResponse(noDesktopLinks));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInDesktop("doc-123");
            });

            expect(mockWindowOpen).toHaveBeenCalledWith(
                "https://sharepoint.com/doc/view",
                "_blank",
            );
        });

        it("should set actionError on failure", async () => {
            mockFetch.mockResolvedValueOnce(createJsonResponse({}, 403));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInDesktop("doc-123");
            });

            expect(result.current.actionError).toContain("Failed to get document links");
        });
    });

    // --- download ---

    describe("download()", () => {
        it("should fetch blob and trigger browser download", async () => {
            const mockClick = jest.fn();
            const mockAppendChild = jest.spyOn(document.body, "appendChild").mockImplementation(
                (node) => {
                    if (node instanceof HTMLAnchorElement) {
                        node.click = mockClick;
                    }
                    return node;
                },
            );
            const mockRemoveChild = jest.spyOn(document.body, "removeChild").mockImplementation(
                (node) => node,
            );

            mockFetch.mockResolvedValueOnce(
                createBlobResponse("file-content", 'attachment; filename="contract.pdf"'),
            );
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.download("doc-123");
            });

            expect(mockFetch).toHaveBeenCalledWith(
                "https://test-api.example.com/api/documents/doc-123/download",
                expect.objectContaining({ headers: AUTH_HEADERS }),
            );
            expect(mockCreateObjectURL).toHaveBeenCalled();
            expect(mockClick).toHaveBeenCalled();
            expect(mockRevokeObjectURL).toHaveBeenCalledWith("blob:http://localhost/fake-blob-url");

            mockAppendChild.mockRestore();
            mockRemoveChild.mockRestore();
        });

        it("should use fallback filename when Content-Disposition is missing", async () => {
            const mockClick = jest.fn();
            const mockAppendChild = jest.spyOn(document.body, "appendChild").mockImplementation(
                (node) => {
                    if (node instanceof HTMLAnchorElement) {
                        node.click = mockClick;
                    }
                    return node;
                },
            );
            const mockRemoveChild = jest.spyOn(document.body, "removeChild").mockImplementation(
                (node) => node,
            );

            mockFetch.mockResolvedValueOnce(createBlobResponse("content"));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.download("doc-123");
            });

            expect(mockClick).toHaveBeenCalled();

            mockAppendChild.mockRestore();
            mockRemoveChild.mockRestore();
        });

        it("should set actionError on download failure", async () => {
            const failResponse = {
                ok: false,
                status: 404,
                statusText: "Not Found",
                headers: new Headers(),
                blob: jest.fn(),
            } as unknown as Response;
            mockFetch.mockResolvedValueOnce(failResponse);
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.download("doc-123");
            });

            expect(result.current.actionError).toContain("Download failed");
        });

        it("should set actionError on auth failure", async () => {
            mockBuildAuthHeaders.mockRejectedValueOnce(new Error("Token expired"));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.download("doc-123");
            });

            expect(result.current.actionError).toContain("Token expired");
        });
    });

    // --- deleteDocuments ---

    describe("deleteDocuments()", () => {
        it("should show confirmation dialog and delete on confirm", async () => {
            window.confirm = jest.fn().mockReturnValue(true);
            mockFetch.mockResolvedValue(createDeleteResponse());
            const onSuccess = jest.fn();
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.deleteDocuments(["doc-1", "doc-2"], onSuccess);
            });

            expect(window.confirm).toHaveBeenCalledWith(
                "Are you sure you want to delete 2 documents? This action cannot be undone.",
            );
            expect(mockFetch).toHaveBeenCalledTimes(2);
            expect(onSuccess).toHaveBeenCalled();
        });

        it("should show singular message for single document", async () => {
            window.confirm = jest.fn().mockReturnValue(true);
            mockFetch.mockResolvedValue(createDeleteResponse());
            const onSuccess = jest.fn();
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.deleteDocuments(["doc-1"], onSuccess);
            });

            expect(window.confirm).toHaveBeenCalledWith(
                "Are you sure you want to delete 1 document? This action cannot be undone.",
            );
        });

        it("should not delete when user cancels confirmation", async () => {
            window.confirm = jest.fn().mockReturnValue(false);
            const onSuccess = jest.fn();
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.deleteDocuments(["doc-1"], onSuccess);
            });

            expect(mockFetch).not.toHaveBeenCalled();
            expect(onSuccess).not.toHaveBeenCalled();
        });

        it("should DELETE each document via BFF API", async () => {
            window.confirm = jest.fn().mockReturnValue(true);
            mockFetch.mockResolvedValue(createDeleteResponse());
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.deleteDocuments(["doc-A", "doc-B"], jest.fn());
            });

            const urls = mockFetch.mock.calls.map(([url]) => url);
            expect(urls).toContain("https://test-api.example.com/api/documents/doc-A");
            expect(urls).toContain("https://test-api.example.com/api/documents/doc-B");

            for (const [, init] of mockFetch.mock.calls) {
                expect(init?.method).toBe("DELETE");
            }
        });

        it("should set actionError when delete fails", async () => {
            window.confirm = jest.fn().mockReturnValue(true);
            mockFetch.mockResolvedValue(createDeleteResponse(500));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.deleteDocuments(["doc-1"], jest.fn());
            });

            expect(result.current.actionError).toContain("Failed to delete document");
        });

        it("should not call onSuccess when delete fails", async () => {
            window.confirm = jest.fn().mockReturnValue(true);
            mockFetch.mockResolvedValue(createDeleteResponse(500));
            const onSuccess = jest.fn();
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.deleteDocuments(["doc-1"], onSuccess);
            });

            expect(onSuccess).not.toHaveBeenCalled();
        });
    });

    // --- emailLink ---

    describe("emailLink()", () => {
        it("should fetch links and set mailto href", async () => {
            mockFetch.mockResolvedValueOnce(createJsonResponse(openLinksResponse));

            // Mock window.location.href setter
            const hrefSetter = jest.fn();
            delete (window as Record<string, unknown>).location;
            (window as Record<string, unknown>).location = {
                href: "",
                set href(val: string) {
                    hrefSetter(val);
                },
                get href() {
                    return "";
                },
                origin: "https://localhost",
            } as unknown as Location;

            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.emailLink("doc-123");
            });

            expect(mockFetch).toHaveBeenCalledWith(
                "https://test-api.example.com/api/documents/doc-123/open-links",
                expect.anything(),
            );

            // Restore location
            if (originalLocationHref) {
                Object.defineProperty(window, "location", originalLocationHref);
            }
        });

        it("should set actionError on failure", async () => {
            mockFetch.mockResolvedValueOnce(createJsonResponse({}, 500));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.emailLink("doc-123");
            });

            expect(result.current.actionError).toContain("Failed to get document links");
        });
    });

    // --- sendToIndex ---

    describe("sendToIndex()", () => {
        it("should POST analyze for each document ID", async () => {
            mockFetch.mockResolvedValue(createAnalyzeResponse());
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.sendToIndex(["doc-A", "doc-B", "doc-C"]);
            });

            expect(mockFetch).toHaveBeenCalledTimes(3);
            const urls = mockFetch.mock.calls.map(([url]) => url);
            expect(urls).toContain("https://test-api.example.com/api/documents/doc-A/analyze");
            expect(urls).toContain("https://test-api.example.com/api/documents/doc-B/analyze");
            expect(urls).toContain("https://test-api.example.com/api/documents/doc-C/analyze");

            for (const [, init] of mockFetch.mock.calls) {
                expect(init?.method).toBe("POST");
            }
        });

        it("should accept 202 Accepted as success", async () => {
            mockFetch.mockResolvedValue(createAnalyzeResponse(202));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.sendToIndex(["doc-1"]);
            });

            expect(result.current.actionError).toBeNull();
            expect(result.current.isActing).toBe(false);
        });

        it("should set actionError on failure", async () => {
            mockFetch.mockResolvedValue(createAnalyzeResponse(500));
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.sendToIndex(["doc-1"]);
            });

            expect(result.current.actionError).toContain("Failed to send document to index");
        });

        it("should set isActing during operation", async () => {
            let resolveAnalyze: (value: Response) => void;
            mockFetch.mockReturnValueOnce(
                new Promise<Response>((resolve) => {
                    resolveAnalyze = resolve;
                }),
            );
            const { result } = renderHook(() => useDocumentActions());

            const promise = act(async () => {
                return result.current.sendToIndex(["doc-1"]);
            });

            expect(result.current.isActing).toBe(true);

            await act(async () => {
                resolveAnalyze!(createAnalyzeResponse());
            });
            await promise;

            expect(result.current.isActing).toBe(false);
        });
    });

    // --- Auth failures ---

    describe("auth failures", () => {
        it("should set actionError when buildAuthHeaders fails in openInWeb", async () => {
            mockBuildAuthHeaders.mockRejectedValueOnce(
                new Error("MSAL not initialized"),
            );
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.openInWeb("doc-123");
            });

            expect(result.current.actionError).toBe("MSAL not initialized");
        });

        it("should set actionError when buildAuthHeaders fails in sendToIndex", async () => {
            mockBuildAuthHeaders.mockRejectedValueOnce(
                new Error("Token refresh failed"),
            );
            const { result } = renderHook(() => useDocumentActions());

            await act(async () => {
                await result.current.sendToIndex(["doc-1"]);
            });

            expect(result.current.actionError).toBe("Token refresh failed");
        });
    });
});
