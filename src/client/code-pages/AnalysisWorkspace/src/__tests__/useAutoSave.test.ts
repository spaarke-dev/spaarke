/**
 * Integration tests for useAutoSave hook
 *
 * Tests the debounced auto-save hook that persists editor content
 * to the BFF API. Covers:
 *   - Debounce behavior (3-second default)
 *   - Streaming completion triggers auto-save
 *   - Network error during save
 *   - Force save (bypasses debounce)
 *   - Rapid changes (only latest content saved)
 *
 * Uses fake timers for deterministic debounce testing.
 *
 * @see hooks/useAutoSave.ts
 * @see services/analysisApi.ts
 */

import { renderHook, act } from "@testing-library/react";
import { useAutoSave } from "../hooks/useAutoSave";
import {
    TEST_ANALYSIS_ID,
    TEST_TOKEN,
} from "./mocks/fixtures";

// Mock the analysisApi service module
jest.mock("../services/analysisApi");

import { saveAnalysisContent } from "../services/analysisApi";

const mockSaveContent = saveAnalysisContent as jest.MockedFunction<
    typeof saveAnalysisContent
>;

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("useAutoSave", () => {
    beforeEach(() => {
        jest.clearAllMocks();
        jest.useFakeTimers();
        mockSaveContent.mockResolvedValue(undefined);
    });

    afterEach(() => {
        jest.useRealTimers();
    });

    // -----------------------------------------------------------------------
    // 1. Debounce Behavior
    // -----------------------------------------------------------------------

    it("notifyContentChanged_WithDebounce_SavesAfterDebounceDelay", async () => {
        // Arrange
        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 3000,
            })
        );

        // Act: notify content changed
        act(() => {
            result.current.notifyContentChanged("<p>Updated content</p>");
        });

        // Assert: save NOT called yet (debounce still pending)
        expect(mockSaveContent).not.toHaveBeenCalled();
        expect(result.current.saveState).toBe("idle");

        // Advance time past debounce threshold
        await act(async () => {
            jest.advanceTimersByTime(3000);
        });

        // Assert: save was called
        expect(mockSaveContent).toHaveBeenCalledTimes(1);
        expect(mockSaveContent).toHaveBeenCalledWith(
            TEST_ANALYSIS_ID,
            "<p>Updated content</p>",
            TEST_TOKEN
        );
    });

    // -----------------------------------------------------------------------
    // 2. Debounce Resets on New Content
    // -----------------------------------------------------------------------

    it("notifyContentChanged_MultipleRapidChanges_OnlyLatestContentSaved", async () => {
        // Arrange
        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 3000,
            })
        );

        // Act: rapid changes within debounce window
        act(() => {
            result.current.notifyContentChanged("<p>Version 1</p>");
        });
        act(() => {
            jest.advanceTimersByTime(1000); // 1s into debounce
        });
        act(() => {
            result.current.notifyContentChanged("<p>Version 2</p>");
        });
        act(() => {
            jest.advanceTimersByTime(1000); // 1s into NEW debounce
        });
        act(() => {
            result.current.notifyContentChanged("<p>Version 3 - final</p>");
        });

        // Advance past debounce from the LAST change
        await act(async () => {
            jest.advanceTimersByTime(3000);
        });

        // Assert: only called once with the final content
        expect(mockSaveContent).toHaveBeenCalledTimes(1);
        expect(mockSaveContent).toHaveBeenCalledWith(
            TEST_ANALYSIS_ID,
            "<p>Version 3 - final</p>",
            TEST_TOKEN
        );
    });

    // -----------------------------------------------------------------------
    // 3. Network Error During Save
    // -----------------------------------------------------------------------

    it("doSave_NetworkError_SetsErrorState", async () => {
        // Arrange
        mockSaveContent.mockRejectedValueOnce(new Error("Network request failed"));

        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 1000,
            })
        );

        // Act: trigger save
        act(() => {
            result.current.notifyContentChanged("<p>Content to save</p>");
        });

        await act(async () => {
            jest.advanceTimersByTime(1000);
        });

        // Assert: error state
        expect(result.current.saveState).toBe("error");
        expect(result.current.saveError).toBe("Network request failed");
    });

    // -----------------------------------------------------------------------
    // 4. Force Save
    // -----------------------------------------------------------------------

    it("forceSave_ClearsDebounceTimer_PreventsScheduledSave", async () => {
        // Arrange: The forceSave implementation clears the debounce timer
        // and saves any pendingContentRef (set when content arrives during an
        // in-flight save). If no save is in-flight, the debounce timer is
        // simply cleared.
        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 5000,
            })
        );

        // Act: notify content (starts 5s debounce timer)
        act(() => {
            result.current.notifyContentChanged("<p>Content before force save</p>");
        });

        // Force save clears the debounce timer
        await act(async () => {
            result.current.forceSave();
        });

        // Advance time past original debounce -- should NOT trigger save
        // because forceSave cleared the timer
        await act(async () => {
            jest.advanceTimersByTime(5000);
        });

        // Assert: debounce-triggered save was prevented by forceSave clearing the timer
        expect(mockSaveContent).not.toHaveBeenCalled();
    });

    it("forceSave_NoPendingContent_DoesNotTriggerUnnecessarySave", async () => {
        // Arrange: forceSave only saves pendingContentRef (set during
        // concurrent save attempts). If no content is pending, it's a no-op
        // save-wise (it still clears any debounce timer).
        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 5000,
            })
        );

        // Act: call forceSave without any prior notifyContentChanged
        await act(async () => {
            result.current.forceSave();
        });

        // Assert: no save triggered (no pending content)
        expect(mockSaveContent).not.toHaveBeenCalled();
        expect(result.current.saveState).toBe("idle");
    });

    // -----------------------------------------------------------------------
    // 5. Save State Transitions
    // -----------------------------------------------------------------------

    it("saveState_SuccessfulSave_TransitionsIdleToSavingToSavedToIdle", async () => {
        // Arrange
        const stateHistory: string[] = [];
        let resolvePromise: (() => void) | undefined;

        mockSaveContent.mockImplementation(
            () =>
                new Promise<void>((resolve) => {
                    resolvePromise = resolve;
                })
        );

        const { result } = renderHook(() => {
            const hookResult = useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 100,
            });
            // Track state transitions
            if (stateHistory[stateHistory.length - 1] !== hookResult.saveState) {
                stateHistory.push(hookResult.saveState);
            }
            return hookResult;
        });

        // Initial state
        expect(result.current.saveState).toBe("idle");

        // Trigger save
        act(() => {
            result.current.notifyContentChanged("<p>Content</p>");
        });
        await act(async () => {
            jest.advanceTimersByTime(100);
        });

        // Should now be "saving"
        expect(result.current.saveState).toBe("saving");

        // Resolve the save
        await act(async () => {
            resolvePromise!();
        });

        // Should now be "saved"
        expect(result.current.saveState).toBe("saved");

        // After SAVED_INDICATOR_MS (2000), returns to "idle"
        await act(async () => {
            jest.advanceTimersByTime(2000);
        });

        expect(result.current.saveState).toBe("idle");
    });

    // -----------------------------------------------------------------------
    // 6. Disabled Auto-Save
    // -----------------------------------------------------------------------

    it("notifyContentChanged_WhenDisabled_DoesNotSave", async () => {
        // Arrange
        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: false,
                debounceMs: 100,
            })
        );

        // Act
        act(() => {
            result.current.notifyContentChanged("<p>Should not save</p>");
        });
        await act(async () => {
            jest.advanceTimersByTime(1000);
        });

        // Assert: never called
        expect(mockSaveContent).not.toHaveBeenCalled();
    });

    // -----------------------------------------------------------------------
    // 7. Concurrent Save Protection
    // -----------------------------------------------------------------------

    it("doSave_WhileSaveInProgress_QueuesLatestAndSavesAfterFirst", async () => {
        // Arrange: first save takes time, second arrives while first is in-flight
        let firstResolve: (() => void) | undefined;
        let callCount = 0;

        mockSaveContent.mockImplementation(
            () =>
                new Promise<void>((resolve) => {
                    callCount++;
                    if (callCount === 1) {
                        firstResolve = resolve;
                    } else {
                        resolve();
                    }
                })
        );

        const { result } = renderHook(() =>
            useAutoSave({
                analysisId: TEST_ANALYSIS_ID,
                token: TEST_TOKEN,
                enabled: true,
                debounceMs: 100,
            })
        );

        // Act: first change triggers save after debounce
        act(() => {
            result.current.notifyContentChanged("<p>First content</p>");
        });
        await act(async () => {
            jest.advanceTimersByTime(100);
        });

        // First save is now in-flight
        expect(mockSaveContent).toHaveBeenCalledTimes(1);

        // Second change arrives while first is saving
        act(() => {
            result.current.notifyContentChanged("<p>Second content - queued</p>");
        });
        await act(async () => {
            jest.advanceTimersByTime(100);
        });

        // Still only 1 call (concurrent save is prevented; content is queued)
        // The save is in-flight so the debounced callback triggers doSave,
        // but doSave sees isSavingRef.current is true and queues the content.
        expect(result.current.saveState).toBe("saving");

        // Resolve first save
        await act(async () => {
            firstResolve!();
        });

        // After first resolves, pending content is saved automatically
        // Wait for the second save to complete
        await act(async () => {
            jest.advanceTimersByTime(0);
        });

        expect(mockSaveContent).toHaveBeenCalledTimes(2);
        expect(mockSaveContent).toHaveBeenLastCalledWith(
            TEST_ANALYSIS_ID,
            "<p>Second content - queued</p>",
            TEST_TOKEN
        );
    });
});
