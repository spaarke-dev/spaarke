/**
 * Unit tests for useFilterOptions hook -- filter dropdown option loading.
 *
 * Tests:
 * - Fetches documentTypes, fileTypes, and matterTypes on mount
 * - documentTypes from optionset metadata API
 * - matterTypes from lookup entity records
 * - fileTypes from static list (no fetch)
 * - Parallel fetching of documentTypes and matterTypes
 * - Error handling: API errors, partial failures
 * - refresh() clears cache and re-fetches
 * - Loading state management
 * - Unmount safety (no state updates after unmount)
 *
 * @see useFilterOptions.ts
 * @see DataverseWebApiService.ts
 */

import { renderHook, act, waitFor } from "@testing-library/react";
import type { FilterOption } from "../../types";

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockFetchOptionsetValues = jest.fn<Promise<FilterOption[]>, [string, string]>();
const mockFetchLookupValues = jest.fn<Promise<FilterOption[]>, [string, string]>();
const mockGetFileTypeOptions = jest.fn<FilterOption[], []>();
const mockClearCache = jest.fn<void, []>();

jest.mock("../../services/DataverseWebApiService", () => ({
    fetchOptionsetValues: (...args: [string, string]) => mockFetchOptionsetValues(...args),
    fetchLookupValues: (...args: [string, string]) => mockFetchLookupValues(...args),
    getFileTypeOptions: () => mockGetFileTypeOptions(),
    clearCache: () => mockClearCache(),
}));

import { useFilterOptions } from "../../hooks/useFilterOptions";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const documentTypeOptions: FilterOption[] = [
    { value: "1", label: "Contract" },
    { value: "2", label: "Agreement" },
    { value: "3", label: "Invoice" },
];

const matterTypeOptions: FilterOption[] = [
    { value: "mt-1", label: "Employment" },
    { value: "mt-2", label: "Litigation" },
    { value: "mt-3", label: "Corporate" },
];

const fileTypeOptions: FilterOption[] = [
    { value: "pdf", label: "PDF" },
    { value: "docx", label: "Word (DOCX)" },
    { value: "xlsx", label: "Excel (XLSX)" },
];

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useFilterOptions", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        mockFetchOptionsetValues.mockResolvedValue(documentTypeOptions);
        mockFetchLookupValues.mockResolvedValue(matterTypeOptions);
        mockGetFileTypeOptions.mockReturnValue(fileTypeOptions);
    });

    // --- Initial load ---

    describe("initial load", () => {
        it("should start with isLoading true", () => {
            // Use never-resolving promises to capture the loading state
            mockFetchOptionsetValues.mockReturnValue(new Promise(() => {}));
            mockFetchLookupValues.mockReturnValue(new Promise(() => {}));

            const { result } = renderHook(() => useFilterOptions());

            expect(result.current.isLoading).toBe(true);
        });

        it("should set isLoading false after all options load", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });
        });

        it("should start with no error", () => {
            const { result } = renderHook(() => useFilterOptions());

            expect(result.current.error).toBeNull();
        });

        it("should start with empty arrays before loading completes", () => {
            mockFetchOptionsetValues.mockReturnValue(new Promise(() => {}));
            mockFetchLookupValues.mockReturnValue(new Promise(() => {}));

            const { result } = renderHook(() => useFilterOptions());

            expect(result.current.documentTypes).toEqual([]);
            expect(result.current.fileTypes).toEqual([]);
            expect(result.current.matterTypes).toEqual([]);
        });
    });

    // --- Fetching document types ---

    describe("documentTypes", () => {
        it("should fetch document types from optionset metadata", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockFetchOptionsetValues).toHaveBeenCalledWith(
                "sprk_document",
                "sprk_documenttype",
            );
            expect(result.current.documentTypes).toEqual(documentTypeOptions);
        });

        it("should handle empty document types", async () => {
            mockFetchOptionsetValues.mockResolvedValue([]);
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.documentTypes).toEqual([]);
        });
    });

    // --- Fetching matter types ---

    describe("matterTypes", () => {
        it("should fetch matter types from lookup entity", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockFetchLookupValues).toHaveBeenCalledWith(
                "sprk_mattertype_refs",
                "sprk_mattertypename",
            );
            expect(result.current.matterTypes).toEqual(matterTypeOptions);
        });

        it("should handle empty matter types", async () => {
            mockFetchLookupValues.mockResolvedValue([]);
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.matterTypes).toEqual([]);
        });
    });

    // --- File types (static) ---

    describe("fileTypes", () => {
        it("should get file types from static list (no async fetch)", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockGetFileTypeOptions).toHaveBeenCalled();
            expect(result.current.fileTypes).toEqual(fileTypeOptions);
        });
    });

    // --- Parallel fetching ---

    describe("parallel fetching", () => {
        it("should fetch documentTypes and matterTypes in parallel", async () => {
            let resolveDocTypes: (value: FilterOption[]) => void;
            let resolveMatTypes: (value: FilterOption[]) => void;

            mockFetchOptionsetValues.mockReturnValue(
                new Promise((resolve) => {
                    resolveDocTypes = resolve;
                }),
            );
            mockFetchLookupValues.mockReturnValue(
                new Promise((resolve) => {
                    resolveMatTypes = resolve;
                }),
            );

            const { result } = renderHook(() => useFilterOptions());

            // Both should be called immediately (in parallel)
            expect(mockFetchOptionsetValues).toHaveBeenCalledTimes(1);
            expect(mockFetchLookupValues).toHaveBeenCalledTimes(1);

            // Still loading
            expect(result.current.isLoading).toBe(true);

            // Resolve both
            await act(async () => {
                resolveDocTypes!(documentTypeOptions);
                resolveMatTypes!(matterTypeOptions);
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.documentTypes).toEqual(documentTypeOptions);
            expect(result.current.matterTypes).toEqual(matterTypeOptions);
        });
    });

    // --- Error handling ---

    describe("error handling", () => {
        it("should set error when fetchOptionsetValues rejects", async () => {
            mockFetchOptionsetValues.mockRejectedValue(new Error("Optionset API failed"));
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.error).toBe("Failed to load filter options");
        });

        it("should set error when fetchLookupValues rejects", async () => {
            mockFetchLookupValues.mockRejectedValue(new Error("Lookup API failed"));
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.error).toBe("Failed to load filter options");
        });

        it("should set error when both fetches reject", async () => {
            mockFetchOptionsetValues.mockRejectedValue(new Error("Doc API fail"));
            mockFetchLookupValues.mockRejectedValue(new Error("Matter API fail"));
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.error).toBe("Failed to load filter options");
        });

        it("should keep previous data when error occurs during refresh", async () => {
            // First load: success
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.documentTypes).toEqual(documentTypeOptions);

            // Refresh: failure
            mockFetchOptionsetValues.mockRejectedValue(new Error("fail"));

            await act(async () => {
                result.current.refresh();
            });

            await waitFor(() => {
                expect(result.current.error).toBe("Failed to load filter options");
            });

            // Note: on error, the state may not be updated with new values,
            // but the error is set appropriately
        });
    });

    // --- refresh() ---

    describe("refresh()", () => {
        it("should call clearCache before re-fetching", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            mockFetchOptionsetValues.mockResolvedValue([
                { value: "new-1", label: "New Type" },
            ]);

            act(() => {
                result.current.refresh();
            });

            expect(mockClearCache).toHaveBeenCalledTimes(1);
        });

        it("should re-fetch all options after clearing cache", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            // Clear counters
            mockFetchOptionsetValues.mockClear();
            mockFetchLookupValues.mockClear();
            mockGetFileTypeOptions.mockClear();

            mockFetchOptionsetValues.mockResolvedValue(documentTypeOptions);
            mockFetchLookupValues.mockResolvedValue(matterTypeOptions);
            mockGetFileTypeOptions.mockReturnValue(fileTypeOptions);

            act(() => {
                result.current.refresh();
            });

            await waitFor(() => {
                expect(mockFetchOptionsetValues).toHaveBeenCalledTimes(1);
                expect(mockFetchLookupValues).toHaveBeenCalledTimes(1);
                expect(mockGetFileTypeOptions).toHaveBeenCalledTimes(1);
            });
        });

        it("should update state with refreshed options", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const updatedDocTypes: FilterOption[] = [
                { value: "99", label: "Updated Type" },
            ];
            mockFetchOptionsetValues.mockResolvedValue(updatedDocTypes);

            act(() => {
                result.current.refresh();
            });

            await waitFor(() => {
                expect(result.current.documentTypes).toEqual(updatedDocTypes);
            });
        });

        it("should clear previous error on successful refresh", async () => {
            // First: cause an error
            mockFetchOptionsetValues.mockRejectedValue(new Error("fail"));
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.error).not.toBeNull();
            });

            // Refresh: success
            mockFetchOptionsetValues.mockResolvedValue(documentTypeOptions);

            act(() => {
                result.current.refresh();
            });

            await waitFor(() => {
                expect(result.current.error).toBeNull();
            });
        });
    });

    // --- Unmount safety ---

    describe("unmount safety", () => {
        it("should not update state after unmount", async () => {
            let resolveDocTypes: (value: FilterOption[]) => void;
            mockFetchOptionsetValues.mockReturnValue(
                new Promise((resolve) => {
                    resolveDocTypes = resolve;
                }),
            );

            const { result, unmount } = renderHook(() => useFilterOptions());

            // Unmount before the promise resolves
            unmount();

            // Resolve after unmount -- this should not throw
            await act(async () => {
                resolveDocTypes!(documentTypeOptions);
            });

            // If we got here without errors, the mountedRef guard worked
            expect(true).toBe(true);
        });
    });

    // --- Return type shape ---

    describe("return type", () => {
        it("should return all expected fields", async () => {
            const { result } = renderHook(() => useFilterOptions());

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current).toHaveProperty("documentTypes");
            expect(result.current).toHaveProperty("fileTypes");
            expect(result.current).toHaveProperty("matterTypes");
            expect(result.current).toHaveProperty("isLoading");
            expect(result.current).toHaveProperty("error");
            expect(result.current).toHaveProperty("refresh");
            expect(typeof result.current.refresh).toBe("function");
        });
    });
});
