/**
 * useWriteMode Hook Unit Tests
 *
 * Validates automatic write mode selection logic (stream vs diff) based on
 * operation type, user override persistence via sessionStorage, and action
 * menu command integration (/mode stream, /mode diff, /mode auto).
 *
 * @see Task 102 - Automatic Write Mode Selection
 * @see Task 105 - Write Mode Integration Tests
 */

import { renderHook, act } from "@testing-library/react";
import {
    useWriteMode,
    resolveAutoMode,
    WRITE_MODE_COMMANDS,
} from "../useWriteMode";
import type { OperationType, WriteMode } from "../useWriteMode";

// ---------------------------------------------------------------------------
// sessionStorage mock
// ---------------------------------------------------------------------------

const mockStorage: Record<string, string> = {};

const sessionStorageMock = {
    getItem: jest.fn((key: string): string | null => mockStorage[key] ?? null),
    setItem: jest.fn((key: string, value: string): void => {
        mockStorage[key] = value;
    }),
    removeItem: jest.fn((key: string): void => {
        delete mockStorage[key];
    }),
    clear: jest.fn((): void => {
        for (const key of Object.keys(mockStorage)) {
            delete mockStorage[key];
        }
    }),
    get length(): number {
        return Object.keys(mockStorage).length;
    },
    key: jest.fn((_index: number): string | null => null),
};

Object.defineProperty(window, "sessionStorage", {
    value: sessionStorageMock,
    writable: true,
});

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
    sessionStorageMock.getItem.mockClear();
    sessionStorageMock.setItem.mockClear();
    sessionStorageMock.removeItem.mockClear();
    sessionStorageMock.clear();
});

// ---------------------------------------------------------------------------
// resolveAutoMode (pure function)
// ---------------------------------------------------------------------------

describe("resolveAutoMode", () => {
    it("resolveAutoMode_Addition_ReturnsStream", () => {
        expect(resolveAutoMode("addition")).toBe("stream");
    });

    it("resolveAutoMode_Revision_ReturnsDiff", () => {
        expect(resolveAutoMode("revision")).toBe("diff");
    });

    it("resolveAutoMode_SelectionRevision_ReturnsDiff", () => {
        expect(resolveAutoMode("selection-revision")).toBe("diff");
    });

    it("resolveAutoMode_Reanalysis_ReturnsStream", () => {
        expect(resolveAutoMode("reanalysis")).toBe("stream");
    });

    it("resolveAutoMode_Unknown_ReturnsStream", () => {
        expect(resolveAutoMode("unknown")).toBe("stream");
    });

    it("resolveAutoMode_UnrecognizedValue_ReturnsStream", () => {
        // Exercise the default case with a value not in the union
        expect(resolveAutoMode("bogus" as OperationType)).toBe("stream");
    });
});

// ---------------------------------------------------------------------------
// useWriteMode - default / initial state
// ---------------------------------------------------------------------------

describe("useWriteMode", () => {
    describe("initial state", () => {
        it("mode_Default_IsStreamWithAutoSource", () => {
            const { result } = renderHook(() => useWriteMode());

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");
        });

        it("mode_CustomDefault_RespectsDefaultModeOption", () => {
            const { result } = renderHook(() =>
                useWriteMode({ defaultMode: "diff" })
            );

            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("auto");
        });
    });

    // -----------------------------------------------------------------------
    // resolveMode - automatic detection
    // -----------------------------------------------------------------------

    describe("resolveMode (auto)", () => {
        it("resolveMode_Addition_ReturnsStream", () => {
            const { result } = renderHook(() => useWriteMode());
            expect(result.current.resolveMode("addition")).toBe("stream");
        });

        it("resolveMode_Revision_ReturnsDiff", () => {
            const { result } = renderHook(() => useWriteMode());
            expect(result.current.resolveMode("revision")).toBe("diff");
        });

        it("resolveMode_SelectionRevision_ReturnsDiff", () => {
            const { result } = renderHook(() => useWriteMode());
            expect(result.current.resolveMode("selection-revision")).toBe("diff");
        });

        it("resolveMode_Reanalysis_ReturnsStream", () => {
            const { result } = renderHook(() => useWriteMode());
            expect(result.current.resolveMode("reanalysis")).toBe("stream");
        });

        it("resolveMode_Unknown_ReturnsStream", () => {
            const { result } = renderHook(() => useWriteMode());
            expect(result.current.resolveMode("unknown")).toBe("stream");
        });
    });

    // -----------------------------------------------------------------------
    // setMode - user override
    // -----------------------------------------------------------------------

    describe("setMode (user override)", () => {
        it("setMode_Stream_OverridesAutoDetection", () => {
            const { result } = renderHook(() => useWriteMode());

            act(() => {
                result.current.setMode("stream");
            });

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("user-override");
        });

        it("setMode_Diff_OverridesAutoDetection", () => {
            const { result } = renderHook(() => useWriteMode());

            act(() => {
                result.current.setMode("diff");
            });

            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("user-override");
        });

        it("resolveMode_WithOverride_IgnoresOperationType", () => {
            const { result } = renderHook(() => useWriteMode());

            // Set override to stream
            act(() => {
                result.current.setMode("stream");
            });

            // Even though revision auto-detects to diff, override wins
            expect(result.current.resolveMode("revision")).toBe("stream");
            expect(result.current.resolveMode("selection-revision")).toBe("stream");
        });

        it("setMode_Override_PersistsAcrossReRenders", () => {
            const { result, rerender } = renderHook(() => useWriteMode());

            act(() => {
                result.current.setMode("diff");
            });

            // Re-render (simulates React re-render without unmount)
            rerender();

            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("user-override");
        });
    });

    // -----------------------------------------------------------------------
    // resetToAuto
    // -----------------------------------------------------------------------

    describe("resetToAuto", () => {
        it("resetToAuto_AfterOverride_ReturnsToAutoDetection", () => {
            const { result } = renderHook(() => useWriteMode());

            // Set override
            act(() => {
                result.current.setMode("diff");
            });
            expect(result.current.source).toBe("user-override");

            // Reset
            act(() => {
                result.current.resetToAuto();
            });

            expect(result.current.mode).toBe("stream"); // default
            expect(result.current.source).toBe("auto");

            // resolveMode should now auto-detect again
            expect(result.current.resolveMode("revision")).toBe("diff");
            expect(result.current.resolveMode("addition")).toBe("stream");
        });

        it("resetToAuto_WhenAlreadyAuto_IsNoOp", () => {
            const { result } = renderHook(() => useWriteMode());

            act(() => {
                result.current.resetToAuto();
            });

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");
        });
    });

    // -----------------------------------------------------------------------
    // sessionStorage persistence
    // -----------------------------------------------------------------------

    describe("sessionStorage persistence", () => {
        it("setMode_WithSessionId_PersistsWithCorrectKey", () => {
            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-42" })
            );

            act(() => {
                result.current.setMode("diff");
            });

            expect(sessionStorageMock.setItem).toHaveBeenCalledWith(
                "sprk-write-mode-override:sess-42",
                "diff"
            );
        });

        it("setMode_WithoutSessionId_UsesDefaultKey", () => {
            const { result } = renderHook(() => useWriteMode());

            act(() => {
                result.current.setMode("stream");
            });

            expect(sessionStorageMock.setItem).toHaveBeenCalledWith(
                "sprk-write-mode-override",
                "stream"
            );
        });

        it("resetToAuto_RemovesFromSessionStorage", () => {
            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-99" })
            );

            act(() => {
                result.current.setMode("diff");
            });

            act(() => {
                result.current.resetToAuto();
            });

            expect(sessionStorageMock.removeItem).toHaveBeenCalledWith(
                "sprk-write-mode-override:sess-99"
            );
        });

        it("mount_WithExistingOverride_RestoresFromSessionStorage", () => {
            // Pre-populate storage before mounting
            mockStorage["sprk-write-mode-override:sess-restore"] = "diff";

            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-restore" })
            );

            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("user-override");
        });

        it("mount_WithoutOverride_DefaultsToAuto", () => {
            // No pre-populated storage
            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-clean" })
            );

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");
        });

        it("mount_SessionStorageUnavailable_DefaultsToAuto", () => {
            // Make sessionStorage.getItem throw (simulates sandboxed iframe)
            sessionStorageMock.getItem.mockImplementationOnce(() => {
                throw new DOMException("Access denied");
            });

            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-blocked" })
            );

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");
        });

        it("mount_InvalidStoredValue_DefaultsToAuto", () => {
            // Pre-populate with an invalid value
            mockStorage["sprk-write-mode-override:sess-bad"] = "bogus";

            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-bad" })
            );

            // "bogus" is not "stream" or "diff", so it should be ignored
            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");
        });
    });

    // -----------------------------------------------------------------------
    // Session change
    // -----------------------------------------------------------------------

    describe("session change", () => {
        it("sessionChange_NewSession_ReReadsOverride", () => {
            // Pre-populate overrides for two different sessions
            mockStorage["sprk-write-mode-override:sess-A"] = "diff";
            mockStorage["sprk-write-mode-override:sess-B"] = "stream";

            const { result, rerender } = renderHook(
                ({ sessionId }: { sessionId: string }) =>
                    useWriteMode({ sessionId }),
                { initialProps: { sessionId: "sess-A" } }
            );

            // Session A has diff override
            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("user-override");

            // Switch to session B
            rerender({ sessionId: "sess-B" });

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("user-override");
        });

        it("sessionChange_ToSessionWithNoOverride_RevertsToAuto", () => {
            mockStorage["sprk-write-mode-override:sess-X"] = "diff";

            const { result, rerender } = renderHook(
                ({ sessionId }: { sessionId: string }) =>
                    useWriteMode({ sessionId }),
                { initialProps: { sessionId: "sess-X" } }
            );

            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("user-override");

            // Switch to session with no stored override
            rerender({ sessionId: "sess-Y" });

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");
        });
    });

    // -----------------------------------------------------------------------
    // WRITE_MODE_COMMANDS (exported constants)
    // -----------------------------------------------------------------------

    describe("WRITE_MODE_COMMANDS", () => {
        it("commands_ExportedArray_HasThreeEntries", () => {
            expect(WRITE_MODE_COMMANDS).toHaveLength(3);
        });

        it("commands_StreamEntry_HasCorrectShape", () => {
            const stream = WRITE_MODE_COMMANDS.find(
                (c) => c.id === "mode_stream"
            );
            expect(stream).toBeDefined();
            expect(stream!.command).toBe("/mode stream");
            expect(stream!.category).toBe("settings");
        });

        it("commands_DiffEntry_HasCorrectShape", () => {
            const diff = WRITE_MODE_COMMANDS.find((c) => c.id === "mode_diff");
            expect(diff).toBeDefined();
            expect(diff!.command).toBe("/mode diff");
            expect(diff!.category).toBe("settings");
        });

        it("commands_AutoEntry_HasCorrectShape", () => {
            const auto = WRITE_MODE_COMMANDS.find((c) => c.id === "mode_auto");
            expect(auto).toBeDefined();
            expect(auto!.command).toBe("/mode auto");
            expect(auto!.category).toBe("settings");
        });
    });

    // -----------------------------------------------------------------------
    // Action menu command integration
    // -----------------------------------------------------------------------

    describe("action menu command integration", () => {
        /**
         * Simulates the pattern from the hook's JSDoc example:
         * function handleModeCommand(commandId: string) {
         *   switch (commandId) {
         *     case "mode_stream": setMode("stream"); break;
         *     case "mode_diff": setMode("diff"); break;
         *     case "mode_auto": resetToAuto(); break;
         *   }
         * }
         */

        it("modeCommand_Stream_SetsStreamOverride", () => {
            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-cmd" })
            );

            // Simulate /mode stream
            act(() => {
                result.current.setMode("stream");
            });

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("user-override");
            // Even revision should resolve to stream now
            expect(result.current.resolveMode("revision")).toBe("stream");
        });

        it("modeCommand_Diff_SetsDiffOverride", () => {
            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-cmd" })
            );

            // Simulate /mode diff
            act(() => {
                result.current.setMode("diff");
            });

            expect(result.current.mode).toBe("diff");
            expect(result.current.source).toBe("user-override");
            // Even addition should resolve to diff now
            expect(result.current.resolveMode("addition")).toBe("diff");
        });

        it("modeCommand_Auto_ResetsToAutoDetection", () => {
            const { result } = renderHook(() =>
                useWriteMode({ sessionId: "sess-cmd" })
            );

            // First set an override
            act(() => {
                result.current.setMode("diff");
            });
            expect(result.current.source).toBe("user-override");

            // Simulate /mode auto
            act(() => {
                result.current.resetToAuto();
            });

            expect(result.current.mode).toBe("stream");
            expect(result.current.source).toBe("auto");

            // Auto-detection should work again
            expect(result.current.resolveMode("revision")).toBe("diff");
            expect(result.current.resolveMode("addition")).toBe("stream");
        });
    });
});
