/**
 * Unit tests for useFilters hook
 *
 * @see useFilters.ts for implementation
 */
import { renderHook, act } from "@testing-library/react-hooks";
import { useFilters } from "../../hooks/useFilters";

describe("useFilters", () => {
    it("should initialize with empty filters", () => {
        const { result } = renderHook(() => useFilters());

        expect(result.current.filters).toEqual({
            documentTypes: [],
            matterTypes: [],
            dateRange: null,
            fileTypes: [],
        });
        expect(result.current.hasActiveFilters).toBe(false);
    });

    it("should update document types", () => {
        const { result } = renderHook(() => useFilters());

        act(() => {
            result.current.setFilters({
                ...result.current.filters,
                documentTypes: ["contract", "invoice"],
            });
        });

        expect(result.current.filters.documentTypes).toEqual(["contract", "invoice"]);
        expect(result.current.hasActiveFilters).toBe(true);
    });

    it("should update matter types", () => {
        const { result } = renderHook(() => useFilters());

        act(() => {
            result.current.setFilters({
                ...result.current.filters,
                matterTypes: ["litigation"],
            });
        });

        expect(result.current.filters.matterTypes).toEqual(["litigation"]);
        expect(result.current.hasActiveFilters).toBe(true);
    });

    it("should update date range", () => {
        const { result } = renderHook(() => useFilters());

        act(() => {
            result.current.setFilters({
                ...result.current.filters,
                dateRange: { from: "2026-01-01", to: "2026-01-31" },
            });
        });

        expect(result.current.filters.dateRange).toEqual({
            from: "2026-01-01",
            to: "2026-01-31",
        });
        expect(result.current.hasActiveFilters).toBe(true);
    });

    it("should update file types", () => {
        const { result } = renderHook(() => useFilters());

        act(() => {
            result.current.setFilters({
                ...result.current.filters,
                fileTypes: ["pdf", "docx"],
            });
        });

        expect(result.current.filters.fileTypes).toEqual(["pdf", "docx"]);
        expect(result.current.hasActiveFilters).toBe(true);
    });

    it("should clear all filters", () => {
        const { result } = renderHook(() => useFilters());

        // Set some filters first
        act(() => {
            result.current.setFilters({
                documentTypes: ["contract"],
                matterTypes: ["litigation"],
                dateRange: { from: "2026-01-01", to: null },
                fileTypes: ["pdf"],
            });
        });

        expect(result.current.hasActiveFilters).toBe(true);

        // Clear filters
        act(() => {
            result.current.clearFilters();
        });

        expect(result.current.filters).toEqual({
            documentTypes: [],
            matterTypes: [],
            dateRange: null,
            fileTypes: [],
        });
        expect(result.current.hasActiveFilters).toBe(false);
    });

    it("should detect active filters correctly", () => {
        const { result } = renderHook(() => useFilters());

        // Empty filters - not active
        expect(result.current.hasActiveFilters).toBe(false);

        // With empty arrays - still not active
        act(() => {
            result.current.setFilters({
                documentTypes: [],
                matterTypes: [],
                dateRange: null,
                fileTypes: [],
            });
        });
        expect(result.current.hasActiveFilters).toBe(false);

        // With one filter - active
        act(() => {
            result.current.setFilters({
                ...result.current.filters,
                documentTypes: ["contract"],
            });
        });
        expect(result.current.hasActiveFilters).toBe(true);
    });
});
