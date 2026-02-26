/**
 * useDocumentHistory Hook Unit Tests
 *
 * Validates undo/redo version stack behavior for AI-initiated document changes.
 * @see FR-06 (cancel mid-stream undo), FR-07 (max 20 snapshots)
 */

import { renderHook, act } from "@testing-library/react";
import { useDocumentHistory } from "../useDocumentHistory";
import type { RichTextEditorRef } from "../../components/RichTextEditor";

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function createMockEditorRef(initialHtml: string = ""): {
  ref: React.RefObject<RichTextEditorRef | null>;
  mock: RichTextEditorRef;
} {
  let currentHtml = initialHtml;

  const mock: RichTextEditorRef = {
    focus: jest.fn(),
    getHtml: jest.fn(() => currentHtml),
    setHtml: jest.fn((html: string) => {
      currentHtml = html;
    }),
    clear: jest.fn(),
  };

  const ref = { current: mock } as React.RefObject<RichTextEditorRef | null>;

  return { ref, mock };
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("useDocumentHistory", () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  // ── Empty stack behavior ──────────────────────────────────────────────────

  describe("empty stack", () => {
    it("should initialize with canUndo=false, canRedo=false, historyLength=0", () => {
      const { ref } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(false);
      expect(result.current.historyLength).toBe(0);
    });

    it("should be a no-op when undo is called on empty stack", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      act(() => {
        result.current.undo();
      });

      expect(mock.setHtml).not.toHaveBeenCalled();
      expect(result.current.canUndo).toBe(false);
    });

    it("should be a no-op when redo is called on empty stack", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      act(() => {
        result.current.redo();
      });

      expect(mock.setHtml).not.toHaveBeenCalled();
      expect(result.current.canRedo).toBe(false);
    });
  });

  // ── pushVersion ───────────────────────────────────────────────────────────

  describe("pushVersion", () => {
    it("should capture the current editor HTML", () => {
      const { ref, mock } = createMockEditorRef("<p>Hello</p>");
      const { result } = renderHook(() => useDocumentHistory(ref));

      act(() => {
        result.current.pushVersion();
      });

      expect(mock.getHtml).toHaveBeenCalledTimes(1);
      expect(result.current.historyLength).toBe(1);
    });

    it("should set canUndo=false after first push (only one version, nothing to undo to)", () => {
      const { ref } = createMockEditorRef("<p>v1</p>");
      const { result } = renderHook(() => useDocumentHistory(ref));

      act(() => {
        result.current.pushVersion();
      });

      // With only one snapshot, there is no previous version to undo to
      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(false);
    });

    it("should set canUndo=true after two pushes", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });

      expect(result.current.canUndo).toBe(true);
      expect(result.current.canRedo).toBe(false);
      expect(result.current.historyLength).toBe(2);
    });

    it("should not push if editorRef.current is null", () => {
      const ref = { current: null } as React.RefObject<RichTextEditorRef | null>;
      const { result } = renderHook(() => useDocumentHistory(ref));

      act(() => {
        result.current.pushVersion();
      });

      expect(result.current.historyLength).toBe(0);
    });
  });

  // ── Undo / Redo cycle ─────────────────────────────────────────────────────

  describe("undo/redo cycle", () => {
    it("should undo to the previous version", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      // Push v1
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Push v2
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Undo should restore v1
      act(() => {
        result.current.undo();
      });

      expect(mock.setHtml).toHaveBeenCalledWith("<p>v1</p>");
      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(true);
    });

    it("should redo to the next version after undo", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Undo
      act(() => {
        result.current.undo();
      });

      // Redo should restore v2
      act(() => {
        result.current.redo();
      });

      expect(mock.setHtml).toHaveBeenCalledWith("<p>v2</p>");
      expect(result.current.canUndo).toBe(true);
      expect(result.current.canRedo).toBe(false);
    });

    it("should support multiple undo steps", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      // Push v1, v2, v3
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v3</p>");
      act(() => {
        result.current.pushVersion();
      });

      expect(result.current.historyLength).toBe(3);

      // Undo twice: v3 -> v2 -> v1
      act(() => {
        result.current.undo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v2</p>");

      act(() => {
        result.current.undo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v1</p>");

      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(true);
    });

    it("should not undo past the first version", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Undo once (to v1)
      act(() => {
        result.current.undo();
      });

      // Clear mock call history
      (mock.setHtml as jest.Mock).mockClear();

      // Undo again should be no-op (already at v1)
      act(() => {
        result.current.undo();
      });

      expect(mock.setHtml).not.toHaveBeenCalled();
      expect(result.current.canUndo).toBe(false);
    });

    it("should not redo past the latest version", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Redo on a single-item stack should be no-op
      act(() => {
        result.current.redo();
      });

      expect(mock.setHtml).not.toHaveBeenCalled();
      expect(result.current.canRedo).toBe(false);
    });
  });

  // ── Push after undo truncates forward history ─────────────────────────────

  describe("push after undo truncates forward history", () => {
    it("should truncate forward history when a new version is pushed after undo", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      // Push v1, v2, v3
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v3</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Undo to v2
      act(() => {
        result.current.undo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v2</p>");

      // Push v4 (should truncate v3)
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v4</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Stack should be: [v1, v2, v4]
      expect(result.current.historyLength).toBe(3);
      expect(result.current.canRedo).toBe(false);

      // Undo should go to v2
      act(() => {
        result.current.undo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v2</p>");

      // Redo should go to v4 (not v3)
      act(() => {
        result.current.redo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v4</p>");
    });
  });

  // ── Max versions (FR-07) ──────────────────────────────────────────────────

  describe("max versions (FR-07)", () => {
    it("should enforce max 20 versions by default, discarding oldest", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      // Push 22 versions
      for (let i = 1; i <= 22; i++) {
        (mock.getHtml as jest.Mock).mockReturnValueOnce(`<p>v${i}</p>`);
        act(() => {
          result.current.pushVersion();
        });
      }

      // Stack should be capped at 20
      expect(result.current.historyLength).toBe(20);

      // Undo all the way to the bottom should reach v3 (v1 and v2 were discarded)
      for (let i = 0; i < 19; i++) {
        act(() => {
          result.current.undo();
        });
      }

      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v3</p>");
      expect(result.current.canUndo).toBe(false);
    });

    it("should respect custom maxVersions parameter", () => {
      const { ref, mock } = createMockEditorRef();
      const customMax = 5;
      const { result } = renderHook(() => useDocumentHistory(ref, customMax));

      // Push 7 versions
      for (let i = 1; i <= 7; i++) {
        (mock.getHtml as jest.Mock).mockReturnValueOnce(`<p>v${i}</p>`);
        act(() => {
          result.current.pushVersion();
        });
      }

      // Stack should be capped at 5
      expect(result.current.historyLength).toBe(5);

      // Oldest should be v3 (v1, v2 were discarded)
      for (let i = 0; i < 4; i++) {
        act(() => {
          result.current.undo();
        });
      }

      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v3</p>");
      expect(result.current.canUndo).toBe(false);
    });
  });

  // ── canUndo / canRedo flags ───────────────────────────────────────────────

  describe("canUndo and canRedo flags", () => {
    it("should track flags accurately through a push-undo-redo-push sequence", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      // Initially both false
      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(false);

      // After 1st push: can't undo (only one version), can't redo
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });
      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(false);

      // After 2nd push: can undo, can't redo
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });
      expect(result.current.canUndo).toBe(true);
      expect(result.current.canRedo).toBe(false);

      // After undo: can't undo (at first), can redo
      act(() => {
        result.current.undo();
      });
      expect(result.current.canUndo).toBe(false);
      expect(result.current.canRedo).toBe(true);

      // After redo: can undo, can't redo
      act(() => {
        result.current.redo();
      });
      expect(result.current.canUndo).toBe(true);
      expect(result.current.canRedo).toBe(false);

      // After new push (truncates redo): can undo, can't redo
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v3</p>");
      act(() => {
        result.current.pushVersion();
      });
      expect(result.current.canUndo).toBe(true);
      expect(result.current.canRedo).toBe(false);
    });
  });

  // ── Edge cases ────────────────────────────────────────────────────────────

  describe("edge cases", () => {
    it("should handle getHtml returning empty string", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      (mock.getHtml as jest.Mock).mockReturnValueOnce("");
      act(() => {
        result.current.pushVersion();
      });

      // Empty string is still a valid snapshot
      expect(result.current.historyLength).toBe(1);
    });

    it("should handle rapid push-undo-push cycles", () => {
      const { ref, mock } = createMockEditorRef();
      const { result } = renderHook(() => useDocumentHistory(ref));

      // Push v1, v2
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v1</p>");
      act(() => {
        result.current.pushVersion();
      });
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v2</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Undo to v1
      act(() => {
        result.current.undo();
      });

      // Push v3 (truncates v2)
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v3</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Undo to v1
      act(() => {
        result.current.undo();
      });

      // Push v4 (truncates v3)
      (mock.getHtml as jest.Mock).mockReturnValueOnce("<p>v4</p>");
      act(() => {
        result.current.pushVersion();
      });

      // Stack: [v1, v4]
      expect(result.current.historyLength).toBe(2);

      act(() => {
        result.current.undo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v1</p>");

      act(() => {
        result.current.redo();
      });
      expect(mock.setHtml).toHaveBeenLastCalledWith("<p>v4</p>");
    });
  });
});
