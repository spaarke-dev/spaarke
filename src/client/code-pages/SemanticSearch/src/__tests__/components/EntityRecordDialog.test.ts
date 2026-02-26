/**
 * Unit tests for EntityRecordDialog module
 *
 * Covers:
 *   - Domain-to-entity mapping (documents, matters, projects, invoices)
 *   - Xrm.Navigation.navigateTo called with correct parameters
 *   - Dialog dimensions (70% width, 80% height, target: 2)
 *   - Graceful handling when Xrm is not available
 *   - Error handling when navigateTo throws
 */

import { openEntityRecord } from "../../components/EntityRecordDialog";
import type { SearchDomain } from "../../types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function createMockXrm() {
    return {
        Navigation: {
            navigateTo: jest.fn(),
        },
    };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("EntityRecordDialog", () => {
    let originalXrm: unknown;

    beforeEach(() => {
        originalXrm = (window as any).Xrm;
        jest.clearAllMocks();
    });

    afterEach(() => {
        (window as any).Xrm = originalXrm;
    });

    // ---- Domain-to-Entity Mapping ----

    describe("domain-to-entity mapping", () => {
        it.each<[SearchDomain, string]>([
            ["documents", "sprk_document"],
            ["matters", "sprk_matter"],
            ["projects", "sprk_project"],
            ["invoices", "sprk_invoice"],
        ])(
            "maps domain '%s' to entity '%s'",
            (domain, expectedEntity) => {
                const mockXrm = createMockXrm();
                (window as any).Xrm = mockXrm;

                openEntityRecord("test-guid-123", domain);

                expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                    expect.objectContaining({
                        entityName: expectedEntity,
                    }),
                    expect.anything(),
                );
            },
        );
    });

    // ---- navigateTo Parameters ----

    describe("navigateTo call parameters", () => {
        it("passes pageType 'entityrecord'", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("guid-abc", "documents");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                expect.objectContaining({
                    pageType: "entityrecord",
                }),
                expect.anything(),
            );
        });

        it("passes the recordId as entityId", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("my-record-guid-456", "matters");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                expect.objectContaining({
                    entityId: "my-record-guid-456",
                }),
                expect.anything(),
            );
        });

        it("opens as dialog (target: 2)", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("guid-xyz", "projects");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                expect.anything(),
                expect.objectContaining({
                    target: 2,
                }),
            );
        });

        it("sets dialog width to 70%", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("guid-xyz", "invoices");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                expect.anything(),
                expect.objectContaining({
                    width: { value: 70, unit: "%" },
                }),
            );
        });

        it("sets dialog height to 80%", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("guid-xyz", "documents");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                expect.anything(),
                expect.objectContaining({
                    height: { value: 80, unit: "%" },
                }),
            );
        });

        it("passes complete page input and navigation options", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("full-test-guid", "matters");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledWith(
                {
                    pageType: "entityrecord",
                    entityName: "sprk_matter",
                    entityId: "full-test-guid",
                },
                {
                    target: 2,
                    width: { value: 70, unit: "%" },
                    height: { value: 80, unit: "%" },
                },
            );
        });
    });

    // ---- Xrm Not Available ----

    describe("when Xrm is not available", () => {
        it("does not throw when window.Xrm is undefined", () => {
            (window as any).Xrm = undefined;

            expect(() => {
                openEntityRecord("guid-123", "documents");
            }).not.toThrow();
        });

        it("does not throw when window.Xrm is null", () => {
            (window as any).Xrm = null;

            expect(() => {
                openEntityRecord("guid-123", "documents");
            }).not.toThrow();
        });

        it("does not throw when Xrm.Navigation is undefined", () => {
            (window as any).Xrm = {};

            expect(() => {
                openEntityRecord("guid-123", "matters");
            }).not.toThrow();
        });

        it("does not throw when Xrm.Navigation.navigateTo is undefined", () => {
            (window as any).Xrm = { Navigation: {} };

            expect(() => {
                openEntityRecord("guid-123", "projects");
            }).not.toThrow();
        });

        it("logs a warning when Xrm.Navigation is not available", () => {
            const warnSpy = jest.spyOn(console, "warn").mockImplementation();
            (window as any).Xrm = undefined;

            openEntityRecord("guid-123", "documents");

            expect(warnSpy).toHaveBeenCalledWith(
                expect.stringContaining("Xrm.Navigation not available"),
            );
            warnSpy.mockRestore();
        });
    });

    // ---- Error Handling ----

    describe("error handling", () => {
        it("catches and logs error when navigateTo throws", () => {
            const errorSpy = jest.spyOn(console, "error").mockImplementation();
            const mockXrm = createMockXrm();
            mockXrm.Navigation.navigateTo.mockImplementation(() => {
                throw new Error("Navigation failed");
            });
            (window as any).Xrm = mockXrm;

            expect(() => {
                openEntityRecord("guid-123", "documents");
            }).not.toThrow();

            expect(errorSpy).toHaveBeenCalledWith(
                expect.stringContaining("Failed to open record dialog"),
                expect.any(Error),
            );
            errorSpy.mockRestore();
        });

        it("does not propagate the error to the caller", () => {
            const mockXrm = createMockXrm();
            mockXrm.Navigation.navigateTo.mockImplementation(() => {
                throw new TypeError("Cannot read properties");
            });
            (window as any).Xrm = mockXrm;

            jest.spyOn(console, "error").mockImplementation();

            const result = openEntityRecord("guid-456", "invoices");

            // Function returns void (undefined) even on error
            expect(result).toBeUndefined();

            (console.error as jest.Mock).mockRestore();
        });
    });

    // ---- Multiple Calls ----

    describe("multiple calls", () => {
        it("can be called multiple times with different domains", () => {
            const mockXrm = createMockXrm();
            (window as any).Xrm = mockXrm;

            openEntityRecord("guid-1", "documents");
            openEntityRecord("guid-2", "matters");
            openEntityRecord("guid-3", "projects");

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalledTimes(3);

            expect(mockXrm.Navigation.navigateTo).toHaveBeenNthCalledWith(
                1,
                expect.objectContaining({ entityName: "sprk_document", entityId: "guid-1" }),
                expect.anything(),
            );
            expect(mockXrm.Navigation.navigateTo).toHaveBeenNthCalledWith(
                2,
                expect.objectContaining({ entityName: "sprk_matter", entityId: "guid-2" }),
                expect.anything(),
            );
            expect(mockXrm.Navigation.navigateTo).toHaveBeenNthCalledWith(
                3,
                expect.objectContaining({ entityName: "sprk_project", entityId: "guid-3" }),
                expect.anything(),
            );
        });
    });
});
