/**
 * Unit tests for MemoList (FR-16).
 *
 * Test harness
 * ────────────
 * Notepad's devDeps do NOT include `@testing-library/react`, so we render the
 * component using a minimalist harness built from `react-dom/client` + React
 * 18's `act`. This mirrors `useSprkMemoRepository.test.ts`.
 *
 * FluentProvider is required — Fluent v9 Menu/Button depend on it. Menu items
 * are rendered in a portal off `document.body`, so DOM queries use
 * `document.body.querySelector*` rather than the mounted container.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import * as React from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { MemoList } from "../MemoList";
import type { Memo } from "../../types/memo";

// -- Fixtures -----------------------------------------------------------------

const CREATED_1 = "2026-05-01T10:15:00Z";
const CREATED_2 = "2026-05-02T11:20:00Z";
const CREATED_3 = "2026-05-03T12:25:00Z";

/** Sorted most-recent first (parent responsibility). */
const THREE_MEMOS: Memo[] = [
  {
    sprk_memoid: "id-3",
    sprk_name: "Third title",
    sprk_memobody: "Body 3",
    sprk_regardingrecordid: "parent-1",
    createdby: { id: "user-1", name: "Alice" },
    createdon: CREATED_3,
    modifiedon: CREATED_3,
  },
  {
    sprk_memoid: "id-2",
    sprk_name: "Untitled",
    sprk_memobody: "Body 2 line one\nBody 2 line two",
    sprk_regardingrecordid: "parent-1",
    createdby: { id: "user-1", name: "Alice" },
    createdon: CREATED_2,
    modifiedon: CREATED_2,
  },
  {
    sprk_memoid: "id-1",
    sprk_name: "First title",
    sprk_memobody: "Body 1",
    sprk_regardingrecordid: "parent-1",
    createdby: { id: "user-1", name: "Alice" },
    createdon: CREATED_1,
    modifiedon: CREATED_1,
  },
];

// -- Test harness -------------------------------------------------------------

interface RenderResult {
  container: HTMLElement;
  root: Root;
  unmount: () => void;
}

async function renderMemoList(
  props: React.ComponentProps<typeof MemoList>
): Promise<RenderResult> {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  await act(async () => {
    root.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(MemoList, props)
      )
    );
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

/** Locate the visible trigger Button (aria-label "Prior memos"). */
function getTriggerButton(): HTMLButtonElement {
  const btn = document.body.querySelector<HTMLButtonElement>(
    'button[aria-label="Prior memos"]'
  );
  if (!btn) throw new Error("MemoList trigger button not found");
  return btn;
}

/** Click the trigger and let Menu portal mount. */
async function openMenu(): Promise<void> {
  const btn = getTriggerButton();
  await act(async () => {
    btn.click();
  });
  // Let Fluent's portal / focus manager settle
  await act(async () => {
    await Promise.resolve();
  });
}

/** All MenuItem elements in the portal (identified by role="menuitem"). */
function getMenuItems(): HTMLElement[] {
  return Array.from(
    document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')
  );
}

// -- Tests --------------------------------------------------------------------

describe("MemoList — FR-16", () => {
  afterEach(() => {
    // Belt-and-braces: strip any lingering portal roots between tests
    document.body.innerHTML = "";
  });

  it("renders an empty-state MenuItem when memos is empty", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: [],
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    expect(items).toHaveLength(1);
    expect(items[0].textContent).toContain("No memos yet");

    rendered.unmount();
  });

  it("renders one MenuItem per memo when memos are provided", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: THREE_MEMOS,
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    expect(items).toHaveLength(3);

    rendered.unmount();
  });

  it("shows deriveTitle result for each row (sprk_name wins over body)", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: THREE_MEMOS,
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    // id-3 has real sprk_name "Third title" → name wins
    expect(items[0].textContent).toContain("Third title");
    // id-2 has sprk_name "Untitled" → falls through to body first line
    expect(items[1].textContent).toContain("Body 2 line one");
    expect(items[1].textContent).not.toContain("Body 2 line two");
    // id-1 has real sprk_name "First title"
    expect(items[2].textContent).toContain("First title");

    rendered.unmount();
  });

  it("renders createdon via Intl.DateTimeFormat (locale-aware)", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: [THREE_MEMOS[0]],
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const expected = new Intl.DateTimeFormat(undefined, {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(CREATED_3));

    const items = getMenuItems();
    expect(items[0].textContent).toContain(expected);

    rendered.unmount();
  });

  it("invokes onSelect with the correct id when a MenuItem is clicked", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: THREE_MEMOS,
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    // Click the middle memo (id-2)
    await act(async () => {
      items[1].click();
    });

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith("id-2");

    rendered.unmount();
  });

  it("marks the current memo with data-current=true", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: THREE_MEMOS,
      currentMemoId: "id-2",
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    expect(items[0].getAttribute("data-current")).toBe("false");
    expect(items[1].getAttribute("data-current")).toBe("true");
    expect(items[2].getAttribute("data-current")).toBe("false");

    rendered.unmount();
  });

  it("empty-state MenuItem does not invoke onSelect when clicked", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: [],
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    expect(items).toHaveLength(1);
    // Empty item is disabled — click should not fire onSelect
    await act(async () => {
      items[0].click();
    });
    expect(onSelect).not.toHaveBeenCalled();

    rendered.unmount();
  });

  it("disables the trigger button when disabled=true", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: THREE_MEMOS,
      currentMemoId: null,
      onSelect,
      disabled: true,
    });

    const btn = getTriggerButton();
    expect(btn.disabled).toBe(true);

    rendered.unmount();
  });

  it("preserves the memos array order (does not re-sort)", async () => {
    const onSelect = jest.fn();
    // Pass an intentionally out-of-order array to prove parent order is honored
    const outOfOrder: Memo[] = [
      THREE_MEMOS[2], // id-1
      THREE_MEMOS[0], // id-3
      THREE_MEMOS[1], // id-2
    ];
    const rendered = await renderMemoList({
      memos: outOfOrder,
      currentMemoId: null,
      onSelect,
    });

    await openMenu();

    const items = getMenuItems();
    expect(items[0].getAttribute("data-memoid")).toBe("id-1");
    expect(items[1].getAttribute("data-memoid")).toBe("id-3");
    expect(items[2].getAttribute("data-memoid")).toBe("id-2");

    rendered.unmount();
  });

  it("closes the Menu when a MenuItem is clicked (portal cleared)", async () => {
    const onSelect = jest.fn();
    const rendered = await renderMemoList({
      memos: THREE_MEMOS,
      currentMemoId: null,
      onSelect,
    });

    await openMenu();
    expect(getMenuItems()).toHaveLength(3);

    await act(async () => {
      getMenuItems()[0].click();
    });
    // After click, Fluent's Menu unmounts its portal — no menuitems remain
    await act(async () => {
      await Promise.resolve();
    });
    expect(getMenuItems()).toHaveLength(0);

    rendered.unmount();
  });
});
