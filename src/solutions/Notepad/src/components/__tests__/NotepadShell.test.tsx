/**
 * Unit tests for NotepadShell (spec FR-14/15/16/18 integration).
 *
 * Approach
 * ────────
 * Notepad's devDeps do NOT include `@testing-library/react`. We render the
 * component using the same react-dom + `act` harness as the sibling suites
 * (CreatedByPopover, MemoList, MemoEditor).
 *
 * The two hooks (`useLaunchContext`, `useSprkMemoRepository`) are mocked at
 * the module level via `jest.mock`. Each test resets the mock return values
 * before mounting so we can cover:
 *   • FR-13/19 error surfaces (invalid launch context, unsupported entity)
 *   • Repository error surface
 *   • FR-14 derived title from the current memo
 *   • FR-14 "No memo" fallback when list is empty
 *   • FR-16 MemoList wiring (renders trigger, click → setCurrentMemo)
 *   • FR-15 `+` create → createMemo() invoked
 *   • FR-18 CreatedByPopover only rendered when currentMemo is non-null
 *   • FR-17 editor onChange → updateBody
 *   • Debounce-flush-before-switch: both `+` and MemoList paths flush the
 *     pending debounced write against the OLD memo id before mutating state.
 *
 * Portal-aware: MemoList's Menu and CreatedByPopover's Popover mount to
 * document.body — DOM queries use document.body when locating them.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

// React 18 concurrent-mode act flag — must be set BEFORE React import.
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import * as React from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

// ─── Mock the two hooks the shell composes ──────────────────────────────

jest.mock("../../hooks/useLaunchContext", () => ({
  useLaunchContext: jest.fn(),
}));

jest.mock("../../hooks/useSprkMemoRepository", () => ({
  useSprkMemoRepository: jest.fn(),
}));

import { NotepadShell } from "../NotepadShell";
import { useLaunchContext } from "../../hooks/useLaunchContext";
import { useSprkMemoRepository } from "../../hooks/useSprkMemoRepository";
import type { Memo } from "../../types/memo";

const mockedUseLaunchContext = useLaunchContext as jest.MockedFunction<
  typeof useLaunchContext
>;
const mockedUseSprkMemoRepository =
  useSprkMemoRepository as jest.MockedFunction<typeof useSprkMemoRepository>;

// ─── Fixtures ────────────────────────────────────────────────────────────

const VALID_LAUNCH = {
  regardingEntity: "sprk_matter",
  regardingId: "11111111-1111-1111-1111-111111111111",
  valid: true,
} as const;

const MEMO_1: Memo = {
  sprk_memoid: "memo-1",
  sprk_name: "First title",
  sprk_memobody: "Body 1 first line\nBody 1 second line",
  sprk_regardingrecordid: "parent-1",
  createdby: { id: "user-1", name: "Alice Author" },
  createdon: "2026-05-03T12:25:00Z",
  modifiedon: "2026-05-03T12:25:00Z",
};

const MEMO_2: Memo = {
  sprk_memoid: "memo-2",
  sprk_name: "Untitled",
  sprk_memobody: "Body 2 first line\nBody 2 second line",
  sprk_regardingrecordid: "parent-1",
  createdby: { id: "user-1", name: "Alice Author" },
  createdon: "2026-05-02T11:20:00Z",
  modifiedon: "2026-05-02T11:20:00Z",
};

/** Build a minimal repository-result stub with sensible defaults. */
function buildRepo(overrides: Partial<
  ReturnType<typeof useSprkMemoRepository>
> = {}): ReturnType<typeof useSprkMemoRepository> {
  return {
    memos: [],
    loading: false,
    error: null,
    unsupported: false,
    currentMemo: null,
    setCurrentMemo: jest.fn(),
    createMemo: jest.fn(async () => null),
    updateBody: jest.fn(),
    updateName: jest.fn(async () => undefined),
    refetch: jest.fn(async () => undefined),
    ...overrides,
  };
}

// ─── Harness ─────────────────────────────────────────────────────────────

interface Harness {
  container: HTMLElement;
  root: Root;
  unmount: () => void;
}

async function mount(): Promise<Harness> {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => {
    root.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(NotepadShell)
      )
    );
  });
  await act(async () => {
    await Promise.resolve();
  });
  return {
    container,
    root,
    unmount: () => {
      act(() => {
        root.unmount();
      });
      if (container.parentNode) {
        container.parentNode.removeChild(container);
      }
    },
  };
}

async function click(el: Element): Promise<void> {
  await act(async () => {
    el.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
  });
  await act(async () => {
    await Promise.resolve();
  });
}

function pressKeyOnTextarea(
  textarea: HTMLTextAreaElement,
  opts: { key: string; ctrlKey?: boolean; metaKey?: boolean }
): void {
  act(() => {
    textarea.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: opts.key,
        ctrlKey: opts.ctrlKey ?? false,
        metaKey: opts.metaKey ?? false,
        bubbles: true,
        cancelable: true,
      })
    );
  });
}

// ─── Tests ───────────────────────────────────────────────────────────────

describe("NotepadShell — FR-14/15/16/18 integration", () => {
  afterEach(() => {
    // Sweep any lingering portal nodes (MemoList Menu, CreatedByPopover Surface)
    document.body.innerHTML = "";
    jest.clearAllMocks();
  });

  // ────────────────────────────────────────────────────────────────────────
  // Error surfaces (task 038 · FR-13 MessageBar)
  //
  // These wrap the outer div with a Fluent v9 MessageBar (intent="error" or
  // "warning") + Close button. data-testid values are preserved from task 037
  // ("notepad-shell-invalid-launch" / "-unsupported" / "-repo-error").
  // ────────────────────────────────────────────────────────────────────────

  it("renders invalid-launch MessageBar when useLaunchContext.valid is false", async () => {
    mockedUseLaunchContext.mockReturnValue({
      regardingEntity: null,
      regardingId: null,
      valid: false,
      error: "Missing regarding context: regardingEntity",
    });
    mockedUseSprkMemoRepository.mockReturnValue(buildRepo());

    const h = await mount();

    const errorEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-invalid-launch"]'
    );
    expect(errorEl).not.toBeNull();
    expect(errorEl!.getAttribute("data-error-state")).toBe("invalid-launch");
    // MessageBar title
    expect(errorEl!.textContent).toContain("Cannot open Notepad");
    // Body from useLaunchContext.error
    expect(errorEl!.textContent).toContain(
      "Missing regarding context: regardingEntity"
    );
    // Fluent v9 MessageBar renders as role="group" with an aria-labelledby
    // pointing to the title. Presence of role="group" confirms the MessageBar
    // shell mounted (vs. a bare div).
    const messageBar = errorEl!.querySelector('[role="group"]');
    expect(messageBar).not.toBeNull();
    // Top-bar surface NOT rendered in error state
    expect(
      h.container.querySelector('[data-testid="notepad-shell"]')
    ).toBeNull();
    expect(
      h.container.querySelector('[data-testid="notepad-shell-new"]')
    ).toBeNull();

    h.unmount();
  });

  it("renders unsupported-entity MessageBar when repository.unsupported is true", async () => {
    mockedUseLaunchContext.mockReturnValue({
      regardingEntity: "sprk_document",
      regardingId: "22222222-2222-2222-2222-222222222222",
      valid: true,
    });
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ unsupported: true })
    );

    const h = await mount();

    const errorEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-unsupported"]'
    );
    expect(errorEl).not.toBeNull();
    expect(errorEl!.getAttribute("data-error-state")).toBe("unsupported");
    expect(errorEl!.textContent).toContain("Unsupported entity type");
    expect(errorEl!.textContent).toContain("sprk_document");
    // Fluent v9 MessageBar renders as role="group"
    const messageBar = errorEl!.querySelector('[role="group"]');
    expect(messageBar).not.toBeNull();
    // Top bar NOT rendered
    expect(
      h.container.querySelector('[data-testid="notepad-shell-new"]')
    ).toBeNull();

    h.unmount();
  });

  it("renders repo-error MessageBar when repository.error is set", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ error: new Error("Boom from Xrm") })
    );

    const h = await mount();

    const errorEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-repo-error"]'
    );
    expect(errorEl).not.toBeNull();
    expect(errorEl!.textContent).toContain("Failed to load memos");
    expect(errorEl!.textContent).toContain("Boom from Xrm");
    // Top bar NOT rendered
    expect(
      h.container.querySelector('[data-testid="notepad-shell-new"]')
    ).toBeNull();

    h.unmount();
  });

  it("repo-error state takes precedence when launch context is valid but repo failed", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1],
        currentMemo: MEMO_1,
        error: new Error("Downstream Xrm failure"),
      })
    );

    const h = await mount();

    // Repo-error banner takes precedence over normal shell rendering.
    const errorEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-repo-error"]'
    );
    expect(errorEl).not.toBeNull();
    expect(errorEl!.textContent).toContain("Downstream Xrm failure");

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // FR-13 Close-button behavior (U-01 resolution)
  //
  // The Close button attempts (1) window.close, then (2) postMessage to the
  // parent. Both are wrapped in defensive try/catch so a broken host never
  // throws unhandled.
  // ────────────────────────────────────────────────────────────────────────

  it("invalid-launch Close button invokes window.close and postMessage", async () => {
    const originalClose = window.close;
    const originalPostMessage = window.parent.postMessage;
    const closeSpy = jest.fn();
    const postSpy = jest.fn();
    // Replace window.close AND window.parent.postMessage in-place so the
    // handler picks them up.
    (window as any).close = closeSpy;
    (window.parent as any).postMessage = postSpy;

    try {
      mockedUseLaunchContext.mockReturnValue({
        regardingEntity: null,
        regardingId: null,
        valid: false,
        error: "Missing regardingId",
      });
      mockedUseSprkMemoRepository.mockReturnValue(buildRepo());

      const h = await mount();

      // Click the primary Close button (labeled "Close")
      const closeBtn = h.container.querySelector<HTMLButtonElement>(
        '[data-testid="notepad-error-close-button"]'
      );
      expect(closeBtn).not.toBeNull();
      await click(closeBtn!);

      expect(closeSpy).toHaveBeenCalledTimes(1);
      expect(postSpy).toHaveBeenCalledTimes(1);
      expect(postSpy).toHaveBeenCalledWith(
        { type: "notepad-close" },
        "*"
      );

      h.unmount();
    } finally {
      (window as any).close = originalClose;
      (window.parent as any).postMessage = originalPostMessage;
    }
  });

  it("Close icon (containerAction) also invokes window.close and postMessage", async () => {
    const originalClose = window.close;
    const originalPostMessage = window.parent.postMessage;
    const closeSpy = jest.fn();
    const postSpy = jest.fn();
    (window as any).close = closeSpy;
    (window.parent as any).postMessage = postSpy;

    try {
      mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
      mockedUseSprkMemoRepository.mockReturnValue(
        buildRepo({ error: new Error("Repo down") })
      );

      const h = await mount();

      const closeIcon = h.container.querySelector<HTMLButtonElement>(
        '[data-testid="notepad-error-close-icon"]'
      );
      expect(closeIcon).not.toBeNull();
      await click(closeIcon!);

      expect(closeSpy).toHaveBeenCalledTimes(1);
      expect(postSpy).toHaveBeenCalledTimes(1);

      h.unmount();
    } finally {
      (window as any).close = originalClose;
      (window.parent as any).postMessage = originalPostMessage;
    }
  });

  it("Close button still calls postMessage when window.close throws", async () => {
    const originalClose = window.close;
    const originalPostMessage = window.parent.postMessage;
    const closeSpy = jest.fn(() => {
      throw new Error("window.close not allowed in this host");
    });
    const postSpy = jest.fn();
    (window as any).close = closeSpy;
    (window.parent as any).postMessage = postSpy;

    try {
      mockedUseLaunchContext.mockReturnValue({
        regardingEntity: null,
        regardingId: null,
        valid: false,
        error: "Missing regardingEntity",
      });
      mockedUseSprkMemoRepository.mockReturnValue(buildRepo());

      const h = await mount();

      const closeBtn = h.container.querySelector<HTMLButtonElement>(
        '[data-testid="notepad-error-close-button"]'
      );

      // Should NOT throw even though window.close threw
      await expect(click(closeBtn!)).resolves.not.toThrow();

      expect(closeSpy).toHaveBeenCalledTimes(1);
      // postMessage fallback still runs
      expect(postSpy).toHaveBeenCalledTimes(1);

      h.unmount();
    } finally {
      (window as any).close = originalClose;
      (window.parent as any).postMessage = originalPostMessage;
    }
  });

  it("Close button does not throw when BOTH window.close and postMessage throw", async () => {
    const originalClose = window.close;
    const originalPostMessage = window.parent.postMessage;
    const closeSpy = jest.fn(() => {
      throw new Error("close blocked");
    });
    const postSpy = jest.fn(() => {
      throw new Error("postMessage blocked");
    });
    (window as any).close = closeSpy;
    (window.parent as any).postMessage = postSpy;

    try {
      mockedUseLaunchContext.mockReturnValue({
        regardingEntity: null,
        regardingId: null,
        valid: false,
        error: "Missing regardingEntity",
      });
      mockedUseSprkMemoRepository.mockReturnValue(buildRepo());

      const h = await mount();
      const closeBtn = h.container.querySelector<HTMLButtonElement>(
        '[data-testid="notepad-error-close-button"]'
      );

      await expect(click(closeBtn!)).resolves.not.toThrow();
      expect(closeSpy).toHaveBeenCalled();
      expect(postSpy).toHaveBeenCalled();

      h.unmount();
    } finally {
      (window as any).close = originalClose;
      (window.parent as any).postMessage = originalPostMessage;
    }
  });

  // ────────────────────────────────────────────────────────────────────────
  // FR-14 title derivation
  // ────────────────────────────────────────────────────────────────────────

  it("renders derived title from currentMemo.sprk_name (name wins)", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ memos: [MEMO_1], currentMemo: MEMO_1 })
    );

    const h = await mount();

    const titleEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-title"]'
    );
    expect(titleEl).not.toBeNull();
    expect(titleEl!.textContent).toBe("First title");

    h.unmount();
  });

  it("falls back to first non-empty line of body when sprk_name is 'Untitled'", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ memos: [MEMO_2], currentMemo: MEMO_2 })
    );

    const h = await mount();

    const titleEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-title"]'
    );
    expect(titleEl!.textContent).toBe("Body 2 first line");

    h.unmount();
  });

  it("renders 'No memo' title when currentMemo is null", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ memos: [], currentMemo: null })
    );

    const h = await mount();

    const titleEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-title"]'
    );
    expect(titleEl!.textContent).toBe("No memo");

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // FR-18 CreatedByPopover conditional rendering
  // ────────────────────────────────────────────────────────────────────────

  it("renders CreatedByPopover trigger only when currentMemo is non-null", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ memos: [MEMO_1], currentMemo: MEMO_1 })
    );

    const h = await mount();

    // The CreatedByPopover trigger has data-testid="created-by-trigger"
    const trigger = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="created-by-trigger"]'
    );
    expect(trigger).not.toBeNull();

    h.unmount();
  });

  it("does NOT render CreatedByPopover trigger when currentMemo is null", async () => {
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({ memos: [], currentMemo: null })
    );

    const h = await mount();

    const trigger = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="created-by-trigger"]'
    );
    expect(trigger).toBeNull();

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // FR-15 `+` new memo
  // ────────────────────────────────────────────────────────────────────────

  it("clicking `+` invokes repository.createMemo", async () => {
    const createMemo = jest.fn(async () => null);
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1],
        currentMemo: MEMO_1,
        createMemo,
      })
    );

    const h = await mount();

    const btn = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="notepad-shell-new"]'
    );
    expect(btn).not.toBeNull();
    await click(btn!);

    expect(createMemo).toHaveBeenCalledTimes(1);

    h.unmount();
  });

  it("clicking `+` flushes pending debounced write against OLD memo BEFORE createMemo", async () => {
    const updateBody = jest.fn();
    const createMemo = jest.fn(async () => null);
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1],
        currentMemo: MEMO_1,
        updateBody,
        createMemo,
      })
    );

    const h = await mount();
    const btn = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="notepad-shell-new"]'
    );
    await click(btn!);

    // updateBody called with (currentMemo.sprk_memobody, { immediate: true })
    expect(updateBody).toHaveBeenCalledTimes(1);
    expect(updateBody).toHaveBeenCalledWith(MEMO_1.sprk_memobody, {
      immediate: true,
    });
    // …and createMemo called after
    expect(createMemo).toHaveBeenCalledTimes(1);
    expect(updateBody.mock.invocationCallOrder[0]).toBeLessThan(
      createMemo.mock.invocationCallOrder[0]
    );

    h.unmount();
  });

  it("clicking `+` when currentMemo is null does NOT call updateBody (no memo to flush)", async () => {
    const updateBody = jest.fn();
    const createMemo = jest.fn(async () => null);
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [],
        currentMemo: null,
        updateBody,
        createMemo,
      })
    );

    const h = await mount();
    const btn = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="notepad-shell-new"]'
    );
    await click(btn!);

    expect(updateBody).not.toHaveBeenCalled();
    expect(createMemo).toHaveBeenCalledTimes(1);

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // FR-16 MemoList wiring — click → setCurrentMemo, with flush-before-switch
  // ────────────────────────────────────────────────────────────────────────

  it("MemoList row click invokes setCurrentMemo with the target memo id", async () => {
    const setCurrentMemo = jest.fn();
    const updateBody = jest.fn();
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1, MEMO_2],
        currentMemo: MEMO_1,
        setCurrentMemo,
        updateBody,
      })
    );

    const h = await mount();

    // Open the MemoList Menu by clicking the trigger.
    const trigger = document.body.querySelector<HTMLButtonElement>(
      'button[aria-label="Prior memos"]'
    );
    expect(trigger).not.toBeNull();
    await click(trigger!);

    // Portal-mounted menu items live under document.body.
    const items = Array.from(
      document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')
    );
    expect(items.length).toBeGreaterThanOrEqual(2);

    // Click the second row (MEMO_2 id) — parent order is preserved.
    const memo2Item = items.find(
      (el) => el.getAttribute("data-memoid") === MEMO_2.sprk_memoid
    );
    expect(memo2Item).not.toBeUndefined();
    await click(memo2Item!);

    expect(setCurrentMemo).toHaveBeenCalledWith(MEMO_2.sprk_memoid);

    h.unmount();
  });

  it("MemoList row click flushes pending debounced write against OLD memo BEFORE setCurrentMemo", async () => {
    const setCurrentMemo = jest.fn();
    const updateBody = jest.fn();
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1, MEMO_2],
        currentMemo: MEMO_1,
        setCurrentMemo,
        updateBody,
      })
    );

    const h = await mount();

    const trigger = document.body.querySelector<HTMLButtonElement>(
      'button[aria-label="Prior memos"]'
    );
    await click(trigger!);

    const items = Array.from(
      document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')
    );
    const memo2Item = items.find(
      (el) => el.getAttribute("data-memoid") === MEMO_2.sprk_memoid
    );
    await click(memo2Item!);

    // Flush called against the OLD memo's body
    expect(updateBody).toHaveBeenCalledTimes(1);
    expect(updateBody).toHaveBeenCalledWith(MEMO_1.sprk_memobody, {
      immediate: true,
    });
    // Switch happens after flush
    expect(setCurrentMemo).toHaveBeenCalledWith(MEMO_2.sprk_memoid);
    expect(updateBody.mock.invocationCallOrder[0]).toBeLessThan(
      setCurrentMemo.mock.invocationCallOrder[0]
    );

    h.unmount();
  });

  it("MemoList click on CURRENT memo does NOT flush (no-op switch)", async () => {
    const setCurrentMemo = jest.fn();
    const updateBody = jest.fn();
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1, MEMO_2],
        currentMemo: MEMO_1,
        setCurrentMemo,
        updateBody,
      })
    );

    const h = await mount();

    const trigger = document.body.querySelector<HTMLButtonElement>(
      'button[aria-label="Prior memos"]'
    );
    await click(trigger!);

    const items = Array.from(
      document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')
    );
    // Click the current memo (MEMO_1).
    const memo1Item = items.find(
      (el) => el.getAttribute("data-memoid") === MEMO_1.sprk_memoid
    );
    await click(memo1Item!);

    // No flush needed — but setCurrentMemo is still called (idempotent).
    expect(updateBody).not.toHaveBeenCalled();
    expect(setCurrentMemo).toHaveBeenCalledWith(MEMO_1.sprk_memoid);

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // FR-17 editor wiring
  // ────────────────────────────────────────────────────────────────────────

  it("MemoEditor typing triggers updateBody WITHOUT immediate flag", async () => {
    const updateBody = jest.fn();
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1],
        currentMemo: MEMO_1,
        updateBody,
      })
    );

    const h = await mount();

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textarea).not.toBeNull();

    // Simulate a keystroke change event.
    // Fluent v9 Textarea's onChange fires via React's synthetic event system,
    // which reads the value through React's tracked descriptor. Direct
    // `.value =` bypasses that; we MUST use the prototype's native setter so
    // React sees the change.
    await act(async () => {
      const proto = window.HTMLTextAreaElement.prototype;
      const desc = Object.getOwnPropertyDescriptor(proto, "value");
      desc?.set?.call(textarea, "typed text");
      textarea!.dispatchEvent(new Event("input", { bubbles: true }));
    });

    expect(updateBody).toHaveBeenCalled();
    // Non-immediate call has no options arg (or options.immediate falsy).
    const lastCall = updateBody.mock.calls[updateBody.mock.calls.length - 1];
    expect(lastCall[0]).toBe("typed text");
    expect(lastCall[1]?.immediate).toBeFalsy();

    h.unmount();
  });

  it("MemoEditor Ctrl+Enter triggers updateBody with immediate=true", async () => {
    const updateBody = jest.fn();
    mockedUseLaunchContext.mockReturnValue(VALID_LAUNCH);
    mockedUseSprkMemoRepository.mockReturnValue(
      buildRepo({
        memos: [MEMO_1],
        currentMemo: MEMO_1,
        updateBody,
      })
    );

    const h = await mount();

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textarea).not.toBeNull();

    // Set value first so currentTarget.value is meaningful.
    await act(async () => {
      textarea!.value = "final body";
      textarea!.dispatchEvent(new Event("input", { bubbles: true }));
    });
    updateBody.mockClear();

    pressKeyOnTextarea(textarea!, { key: "Enter", ctrlKey: true });

    // Expect immediate: true dispatch
    const immediateCall = updateBody.mock.calls.find(
      (c) => c[1]?.immediate === true
    );
    expect(immediateCall).toBeDefined();
    expect(immediateCall![0]).toBe("final body");

    h.unmount();
  });
});
