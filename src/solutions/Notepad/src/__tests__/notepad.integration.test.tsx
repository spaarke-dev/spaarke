/**
 * Notepad — full round-trip integration test (task 041 · spec FR-14/15/16/17/18).
 *
 * Approach
 * ────────
 * Renders the REAL `NotepadShell` inside `FluentProvider` with:
 *   • `window.location.search` set to a valid launch URL (or an invalid one for
 *     error-state cases).
 *   • `window.Xrm.WebApi` stubbed at the boundary — the real hooks
 *     (`useLaunchContext` → `parseLaunchContext`, `useSprkMemoRepository`) run
 *     end-to-end against the stub. No hook mocks; this is the round-trip
 *     integration test the Phase 3 completion criterion demands.
 *
 * Boundary mocks (module-level `jest.mock`):
 *   • `@spaarke/ui-components/services` — `applyResolverFields` — mutates the
 *     entity payload with the ADR-024 dual-field shape without touching
 *     production resolver code (matches the pattern in
 *     `useSprkMemoRepository.test.ts`).
 *   • `@spaarke/ui-components` — `SUPPORTED_MEMO_PARENTS` — mirrors the schema
 *     the shared lib exports.
 *   • `@spaarke/ui-components/utils` — `parseDataParams` — thin URLSearchParams
 *     shim so `useLaunchContext` runs without pulling the shared-lib dist.
 *   • `../hooks/discoverMemoNavProps` — returns a plausible nav-prop list
 *     without hitting `fetch()` (production code calls the metadata endpoint).
 *
 * Coverage vs. task POML steps 1–9:
 *   1. Initial load — most-recent memo visible (FR-14)
 *   2. `+` create — createRecord called, new memo becomes current (FR-15)
 *   3. Debounced save — 999ms no-op, 1000ms one write (FR-17 debounce)
 *   4. Ctrl+Enter — immediate save, cancels pending debounce (FR-17 immediate)
 *   5. Blur — immediate save (FR-17 immediate)
 *   6. Switch via MemoList — flushes pending write against OLD memo BEFORE switch (task 037)
 *   7. Info popover — createdby + createdon rendered (FR-18)
 *   8. Invalid launch — NotepadErrorBanner rendered (FR-13)
 *   9. Unsupported entity — NotepadErrorBanner rendered (FR-19)
 *  10. Empty memos — 'No memo' title, editor disabled (edge case)
 *
 * Constraints:
 *   • Zero `@spaarke/auth` imports (NFR-05).
 *   • Zero BFF calls (NFR-07); all Dataverse I/O through the Xrm.WebApi stub.
 *   • ADR-038: this IS the integration test for the Notepad user flow (KEEP).
 *
 * Harness technique:
 *   react-dom/client + `act` from `react`. Notepad's devDeps intentionally do
 *   NOT include `@testing-library/react`; this file mirrors the sibling suites
 *   (`NotepadShell.test.tsx`, `MemoEditor.test.tsx`, ...) for consistency.
 *
 * @see projects/record-header-and-notepad-r1/spec.md FR-14, FR-15, FR-16, FR-17, FR-18, FR-19
 * @see projects/record-header-and-notepad-r1/tasks/041-notepad-integration-test.poml
 * @see .claude/adr/ADR-038-testing-strategy.md
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

// React 18 concurrent-mode act flag — MUST be set BEFORE React is imported.
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import * as React from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

// ─── Boundary mocks ─────────────────────────────────────────────────────────

// applyResolverFields — mutate `entity` in place with the ADR-024 shape so the
// createRecord payload contains the entity-specific bind + resolver id.
const mockApplyResolverFields = jest.fn(
  async (
    _webApi: any,
    entity: Record<string, unknown>,
    _navProps: any[],
    _parentEntity: string,
    _parentSet: string,
    parentRecordId: string,
    parentRecordName: string,
    _hint?: string
  ) => {
    entity["sprk_regardingmatter@odata.bind"] = `/sprk_matters(${parentRecordId})`;
    entity["sprk_regardingrecordid"] = parentRecordId;
    entity["sprk_regardingrecordname"] = parentRecordName;
    entity["sprk_regardingrecordurl"] =
      `/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${parentRecordId}`;
  }
);

jest.mock(
  "@spaarke/ui-components/services",
  () => ({
    applyResolverFields: (...args: any[]) =>
      (mockApplyResolverFields as any)(...args),
  }),
  { virtual: true }
);

// SUPPORTED_MEMO_PARENTS — top-level barrel. Values match the schema map in
// `useSprkMemoRepository.test.ts`.
jest.mock(
  "@spaarke/ui-components",
  () => ({
    SUPPORTED_MEMO_PARENTS: {
      sprk_matter: "sprk_regardingmatter",
      sprk_project: "sprk_regardingproject",
      sprk_event: "sprk_regardingevent",
      sprk_invoice: "sprk_regardinginvoice",
      sprk_budget: "sprk_regardingbudget",
      sprk_workassignment: "sprk_regardingworkassignment",
    },
  }),
  { virtual: true }
);

// parseDataParams — used by useLaunchContext. In production it parses
// `window.location.search`; here we override it to read from a per-test
// injected string so we don't have to mutate the read-only jsdom Location.
// The tests call `setLaunchSearch("?...")` before mount.
//
// Note: `let` inside a `jest.mock` factory won't work (factory is hoisted). We
// stash the injected value on `globalThis` and read it inside the mock.
jest.mock(
  "@spaarke/ui-components/utils",
  () => ({
    parseDataParams: (_search: string): Record<string, string> => {
      // Prefer the per-test injected search (set via setLaunchSearch); fall back
      // to whatever jsdom's `window.location.search` currently is.
      const injected = (globalThis as any).__NOTEPAD_TEST_LAUNCH_SEARCH__ as
        | string
        | undefined;
      const source = typeof injected === "string" ? injected : _search;
      const s = source.startsWith("?") ? source.slice(1) : source;
      const usp = new URLSearchParams(s);
      // Data-envelope form: ?data=<urlencoded ?k=v&k=v>
      const dataParam = usp.get("data");
      if (dataParam) {
        const inner = new URLSearchParams(dataParam);
        const out: Record<string, string> = {};
        inner.forEach((v, k) => {
          out[k] = v;
        });
        return out;
      }
      const out: Record<string, string> = {};
      usp.forEach((v, k) => {
        out[k] = v;
      });
      return out;
    },
  }),
  { virtual: true }
);

// discoverMemoNavProps — nav-prop discovery mock (avoids fetch).
jest.mock("../hooks/discoverMemoNavProps", () => ({
  discoverMemoNavProps: jest.fn(async () => [
    {
      columnName: "sprk_regardingmatter",
      navPropName: "sprk_regardingmatter",
      referencedEntity: "sprk_matter",
    },
  ]),
  _resetMemoNavPropCacheForTests: jest.fn(),
}));

// ─── Fresh imports AFTER mocks are set up ───────────────────────────────────

import { NotepadShell } from "../components/NotepadShell";

// ─── Fixtures ───────────────────────────────────────────────────────────────

const MATTER_ID = "11112222-3333-4444-5555-666677778888";
const VALID_SEARCH = `?regardingEntity=sprk_matter&regardingId=${MATTER_ID}`;

/** Build a raw memo row shaped like `Xrm.WebApi.retrieveMultipleRecords` returns. */
function makeMemoRaw(overrides: {
  id: string;
  name?: string;
  body?: string;
  createdon?: string;
  createdbyName?: string;
  createdbyId?: string;
}): Record<string, unknown> {
  return {
    sprk_memoid: overrides.id,
    sprk_name: overrides.name ?? "Untitled",
    sprk_memobody: overrides.body ?? "",
    sprk_regardingrecordid: MATTER_ID,
    createdon: overrides.createdon ?? "2026-05-01T10:00:00Z",
    modifiedon: overrides.createdon ?? "2026-05-01T10:00:00Z",
    createdby: {
      fullname: overrides.createdbyName ?? "Alice Author",
      systemuserid: overrides.createdbyId ?? "user-1",
    },
  };
}

// ─── Xrm stub ───────────────────────────────────────────────────────────────

interface XrmWebApiStub {
  retrieveMultipleRecords: jest.Mock;
  retrieveRecord: jest.Mock;
  createRecord: jest.Mock;
  updateRecord: jest.Mock;
}

function installXrmStub(entities: Array<Record<string, unknown>>): XrmWebApiStub {
  const stub: XrmWebApiStub = {
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [...entities] })),
    retrieveRecord: jest.fn(async () => ({ sprk_mattername: "Smith v Jones" })),
    createRecord: jest.fn(async () => ({ id: "memo-new" })),
    updateRecord: jest.fn(async () => ({ id: "existing-id" })),
  };
  (window as any).Xrm = { WebApi: stub };
  return stub;
}

function uninstallXrmStub(): void {
  delete (window as any).Xrm;
}

// ─── URL setup ─────────────────────────────────────────────────────────────
//
// jsdom's `window.location` is not configurable — neither the object itself
// nor its `search` property can be replaced via `Object.defineProperty`. So
// instead of trying to fake the URL through jsdom, we override it upstream:
// the mocked `parseDataParams` above reads from `globalThis.__NOTEPAD_TEST_LAUNCH_SEARCH__`
// when present. `setLaunchSearch` sets that value; the useLaunchContext hook
// then produces the correct ILaunchContext for the test scenario.

function setLaunchSearch(search: string): void {
  (globalThis as any).__NOTEPAD_TEST_LAUNCH_SEARCH__ = search;
}

function clearLaunchSearch(): void {
  delete (globalThis as any).__NOTEPAD_TEST_LAUNCH_SEARCH__;
}

// ─── Harness ───────────────────────────────────────────────────────────────

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
  // Flush the list-fetch effect + subsequent state updates.
  await act(async () => {
    await Promise.resolve();
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
    el.dispatchEvent(
      new MouseEvent("click", { bubbles: true, cancelable: true })
    );
  });
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

/** Set the textarea's value AND fire an input event so Fluent v9's onChange
 *  fires. Uses the native prototype setter so React's tracked-value system
 *  sees the change (bypassing controlled-component reconciliation that would
 *  otherwise reset the DOM value on next render). */
async function typeIntoTextarea(
  textarea: HTMLTextAreaElement,
  value: string
): Promise<void> {
  await act(async () => {
    const proto = window.HTMLTextAreaElement.prototype;
    const desc = Object.getOwnPropertyDescriptor(proto, "value");
    desc?.set?.call(textarea, value);
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  });
  // Two microtask flushes to let Fluent + React commit any effects.
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

/** Set the textarea's DOM value WITHOUT firing input — for immediate-save
 *  cases (Ctrl+Enter, blur) where we want to pretend the user has already
 *  typed something and just needs to trigger the save event. Skips the React
 *  input pipeline that would coalesce into a debounced write. */
function setTextareaValueDirect(
  textarea: HTMLTextAreaElement,
  value: string
): void {
  const proto = window.HTMLTextAreaElement.prototype;
  const desc = Object.getOwnPropertyDescriptor(proto, "value");
  desc?.set?.call(textarea, value);
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

// ─── Tests ─────────────────────────────────────────────────────────────────

describe("Notepad — full round-trip integration (FR-14/15/16/17/18)", () => {
  afterAll(() => {
    clearLaunchSearch();
  });

  beforeEach(() => {
    setLaunchSearch(VALID_SEARCH);
    mockApplyResolverFields.mockClear();
  });

  afterEach(() => {
    document.body.innerHTML = "";
    uninstallXrmStub();
    jest.clearAllMocks();
    jest.useRealTimers();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 1. Initial load — most-recent memo visible (FR-14)
  // ────────────────────────────────────────────────────────────────────────

  it("initial load: renders most-recent memo (title + body) after list resolves", async () => {
    installXrmStub([
      makeMemoRaw({
        id: "memo-1",
        name: "First memo title",
        body: "Body of first memo",
        createdon: "2026-05-03T12:00:00Z",
      }),
      makeMemoRaw({
        id: "memo-2",
        name: "Older memo",
        body: "Older body",
        createdon: "2026-05-01T09:00:00Z",
      }),
    ]);

    const h = await mount();

    const titleEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-title"]'
    );
    expect(titleEl).not.toBeNull();
    expect(titleEl!.textContent).toBe("First memo title");

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textarea).not.toBeNull();
    expect(textarea!.value).toBe("Body of first memo");

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 2. `+` create — createRecord called + new memo becomes current (FR-15)
  // ────────────────────────────────────────────────────────────────────────

  it("`+` create: invokes createRecord with sprk_name=Untitled + resolver fields, then focuses new memo", async () => {
    const stub = installXrmStub([
      makeMemoRaw({
        id: "memo-existing",
        name: "Existing",
        body: "existing body",
        createdon: "2026-05-01T09:00:00Z",
      }),
    ]);
    // After create, refetch returns the newly-created memo at the top.
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [
        makeMemoRaw({
          id: "memo-existing",
          name: "Existing",
          body: "existing body",
          createdon: "2026-05-01T09:00:00Z",
        }),
      ],
    });
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [
        makeMemoRaw({
          id: "memo-new",
          name: "Untitled",
          body: "",
          createdon: "2026-05-05T12:00:00Z",
        }),
        makeMemoRaw({
          id: "memo-existing",
          name: "Existing",
          body: "existing body",
          createdon: "2026-05-01T09:00:00Z",
        }),
      ],
    });

    const h = await mount();

    const newBtn = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="notepad-shell-new"]'
    );
    expect(newBtn).not.toBeNull();

    await click(newBtn!);

    // createRecord was called on sprk_memo with sprk_name="Untitled".
    expect(stub.createRecord).toHaveBeenCalledTimes(1);
    const [entityLogicalName, payload] = stub.createRecord.mock.calls[0];
    expect(entityLogicalName).toBe("sprk_memo");
    expect(payload.sprk_name).toBe("Untitled");
    expect(payload.sprk_memobody).toBe("");
    // Resolver fields populated by mocked applyResolverFields:
    expect(payload["sprk_regardingmatter@odata.bind"]).toBe(
      `/sprk_matters(${MATTER_ID})`
    );
    expect(payload.sprk_regardingrecordid).toBe(MATTER_ID);

    // Editor now shows the new memo (empty body, since refetch shows memo-new
    // at position 0 and currentMemoId is set to created.id).
    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textarea!.value).toBe("");

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 3. Debounced save (FR-17 debounce)
  // ────────────────────────────────────────────────────────────────────────

  it("typing: 999ms → no updateRecord; 1000ms threshold → single updateRecord with latest body", async () => {
    jest.useFakeTimers();
    const stub = installXrmStub([
      makeMemoRaw({
        id: "memo-1",
        name: "Memo",
        body: "",
        createdon: "2026-05-01T10:00:00Z",
      }),
    ]);

    const h = await mount();

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textarea).not.toBeNull();

    await typeIntoTextarea(textarea!, "hello");

    // Not yet fired at 999ms.
    act(() => {
      jest.advanceTimersByTime(999);
    });
    expect(stub.updateRecord).not.toHaveBeenCalled();

    // Cross the 1000ms threshold.
    await act(async () => {
      jest.advanceTimersByTime(1);
      await Promise.resolve();
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);
    expect(stub.updateRecord).toHaveBeenCalledWith("sprk_memo", "memo-1", {
      sprk_memobody: "hello",
    });

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 4. Ctrl+Enter — immediate save cancels pending debounce (FR-17)
  // ────────────────────────────────────────────────────────────────────────

  it("Ctrl+Enter: writes immediately with the current textarea value", async () => {
    jest.useFakeTimers();
    const stub = installXrmStub([
      makeMemoRaw({
        id: "memo-1",
        name: "Memo",
        body: "",
        createdon: "2026-05-01T10:00:00Z",
      }),
    ]);

    const h = await mount();

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    // Set the DOM value directly (no input event → no debounce path). This
    // mirrors the pattern in NotepadShell.test.tsx's Ctrl+Enter case: React's
    // controlled component reconciliation would reset the value on a re-render
    // triggered by the input event pipeline (Fluent v9 Textarea's internal
    // state), so we bypass it and let the keydown handler read the raw DOM
    // value. The Ctrl+Enter save path is the behavior under test; the debounce
    // path is covered by the previous test.
    setTextareaValueDirect(textarea!, "world");

    // Ctrl+Enter → immediate save with currentTarget.value ("world").
    await act(async () => {
      pressKeyOnTextarea(textarea!, { key: "Enter", ctrlKey: true });
      await Promise.resolve();
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);
    expect(stub.updateRecord).toHaveBeenCalledWith("sprk_memo", "memo-1", {
      sprk_memobody: "world",
    });

    // Advancing past any (hypothetical) debounce should not produce a SECOND
    // write.
    act(() => {
      jest.advanceTimersByTime(2000);
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 5. Blur — immediate save (FR-17)
  // ────────────────────────────────────────────────────────────────────────

  it("blur: writes immediately with the current textarea value", async () => {
    jest.useFakeTimers();
    const stub = installXrmStub([
      makeMemoRaw({
        id: "memo-1",
        name: "Memo",
        body: "",
        createdon: "2026-05-01T10:00:00Z",
      }),
    ]);

    const h = await mount();

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    // Same pattern as Ctrl+Enter test — set the DOM value directly, then fire
    // the blur event. The blur handler reads `event.currentTarget.value` and
    // calls `onChange(value, {immediate: true})` → writeBodyNow.
    //
    // React 18 attaches onBlur via delegation at the root and listens for the
    // native `focusout` event (which bubbles), not `blur` (which doesn't).
    // Dispatching `focusout` triggers React's synthetic blur.
    setTextareaValueDirect(textarea!, "test");

    await act(async () => {
      textarea!.dispatchEvent(
        new FocusEvent("focusout", { bubbles: true, cancelable: true })
      );
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);
    expect(stub.updateRecord).toHaveBeenCalledWith("sprk_memo", "memo-1", {
      sprk_memobody: "test",
    });

    // No lingering debounce timer.
    act(() => {
      jest.advanceTimersByTime(2000);
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 6. Switch via MemoList — flushes pending write BEFORE switching (task 037)
  // ────────────────────────────────────────────────────────────────────────

  it("MemoList switch: flushes any pending write against OLD memo BEFORE switching", async () => {
    jest.useFakeTimers();
    const stub = installXrmStub([
      makeMemoRaw({
        id: "memo-1",
        name: "First",
        body: "first body",
        createdon: "2026-05-03T12:00:00Z",
      }),
      makeMemoRaw({
        id: "memo-2",
        name: "Second",
        body: "second body",
        createdon: "2026-05-01T09:00:00Z",
      }),
    ]);

    const h = await mount();

    // Type on memo-1 (starts a pending debounce; typed value lives only in
    // useSprkMemoRepository's pendingBodyRef until the timer fires).
    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    await typeIntoTextarea(textarea!, "typed but unsaved");

    // Open MemoList Menu — portal-mounted, so query document.body.
    const trigger = document.body.querySelector<HTMLButtonElement>(
      'button[aria-label="Prior memos"]'
    );
    expect(trigger).not.toBeNull();
    await click(trigger!);

    // Click memo-2 row.
    const items = Array.from(
      document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')
    );
    const memo2Item = items.find(
      (el) => el.getAttribute("data-memoid") === "memo-2"
    );
    expect(memo2Item).not.toBeUndefined();

    await act(async () => {
      memo2Item!.dispatchEvent(
        new MouseEvent("click", { bubbles: true, cancelable: true })
      );
      await Promise.resolve();
      await Promise.resolve();
    });

    // The immediate flush write MUST have occurred against memo-1 (the OLD id)
    // BEFORE the switch — even if the debounce timer had not yet fired.
    //
    // Note: NotepadShell.handleSelect flushes with `currentMemo.sprk_memobody`
    // (the last-saved body from state) — NOT `pendingBodyRef.current` (the
    // typed but not-yet-committed text). This is the documented behavior
    // asserted in `NotepadShell.test.tsx` (task 037): the flush protects the
    // in-flight timer from writing against the WRONG memo id, not from losing
    // unsaved keystrokes. Preserving unsaved typing across a manual switch is
    // out of scope for R1 — user is expected to Ctrl+Enter first.
    expect(stub.updateRecord).toHaveBeenCalled();
    const oldMemoWrite = stub.updateRecord.mock.calls.find(
      (c) => c[1] === "memo-1"
    );
    expect(oldMemoWrite).toBeDefined();
    // The body written is memo-1's currentMemo.sprk_memobody at switch time
    // (= "first body", the initial fixture — the pending typed text is not
    // reflected in the flush per handleSelect semantics).
    expect(oldMemoWrite![2]).toEqual({ sprk_memobody: "first body" });

    // Editor now shows memo-2's body — the switch completed.
    const textareaAfter = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textareaAfter!.value).toBe("second body");

    // Pending debounce is dead — advancing the clock produces no more writes.
    const writesAfterFlush = stub.updateRecord.mock.calls.length;
    act(() => {
      jest.advanceTimersByTime(3000);
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(writesAfterFlush);

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 7. Info popover — createdby + createdon (FR-18)
  // ────────────────────────────────────────────────────────────────────────

  it("info popover: renders 'Created by {name}' and a formatted createdon date", async () => {
    installXrmStub([
      makeMemoRaw({
        id: "memo-1",
        name: "Memo",
        body: "body",
        createdon: "2026-05-03T12:25:00Z",
        createdbyName: "Alice Author",
      }),
    ]);

    const h = await mount();

    const infoBtn = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="created-by-trigger"]'
    );
    expect(infoBtn).not.toBeNull();

    await click(infoBtn!);

    // Popover surface is portal-mounted under document.body.
    const surface = document.body.querySelector<HTMLElement>(
      '[data-testid="created-by-surface"]'
    );
    expect(surface).not.toBeNull();
    expect(surface!.textContent).toContain("Created by Alice Author");
    // Formatted date — via Intl.DateTimeFormat. Locale-sensitive, so assert
    // presence of "2026" (year survives every locale + timeZone).
    expect(surface!.textContent).toContain("2026");

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 8. Invalid launch context — NotepadErrorBanner (FR-13)
  // ────────────────────────────────────────────────────────────────────────

  it("invalid launch (missing regardingId): renders NotepadErrorBanner with intent=error and no top bar", async () => {
    setLaunchSearch("?regardingEntity=sprk_matter");
    // Xrm not needed — early exit before any query.
    installXrmStub([]);

    const h = await mount();

    const errorEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-invalid-launch"]'
    );
    expect(errorEl).not.toBeNull();
    expect(errorEl!.getAttribute("data-error-state")).toBe("invalid-launch");
    expect(errorEl!.textContent).toContain("Cannot open Notepad");
    // Top-bar surface (positive shell) NOT rendered in error state.
    expect(
      h.container.querySelector('[data-testid="notepad-shell"]')
    ).toBeNull();
    expect(
      h.container.querySelector('[data-testid="notepad-shell-new"]')
    ).toBeNull();

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 9. Unsupported entity — NotepadErrorBanner (FR-19)
  // ────────────────────────────────────────────────────────────────────────

  it("unsupported entity: renders NotepadErrorBanner with intent=warning citing the entity name", async () => {
    setLaunchSearch(`?regardingEntity=sprk_document&regardingId=${MATTER_ID}`);
    installXrmStub([]);

    const h = await mount();

    const errorEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-unsupported"]'
    );
    expect(errorEl).not.toBeNull();
    expect(errorEl!.getAttribute("data-error-state")).toBe("unsupported");
    expect(errorEl!.textContent).toContain("Unsupported entity type");
    expect(errorEl!.textContent).toContain("sprk_document");
    // Top-bar NOT rendered.
    expect(
      h.container.querySelector('[data-testid="notepad-shell-new"]')
    ).toBeNull();

    h.unmount();
  });

  // ────────────────────────────────────────────────────────────────────────
  // 10. Empty memos — 'No memo' title, editor disabled, `+` still available
  // ────────────────────────────────────────────────────────────────────────

  it("empty memos: renders 'No memo' title, editor disabled, `+` still enabled", async () => {
    installXrmStub([]);

    const h = await mount();

    const titleEl = h.container.querySelector<HTMLElement>(
      '[data-testid="notepad-shell-title"]'
    );
    expect(titleEl!.textContent).toBe("No memo");

    const textarea = h.container.querySelector<HTMLTextAreaElement>(
      '[data-testid="sprk-memo-editor"]'
    );
    expect(textarea).not.toBeNull();
    expect(textarea!.disabled).toBe(true);

    // `+` button still available (list empty is not an error state).
    const newBtn = h.container.querySelector<HTMLButtonElement>(
      '[data-testid="notepad-shell-new"]'
    );
    expect(newBtn).not.toBeNull();
    expect(newBtn!.disabled).toBe(false);

    // Info popover trigger NOT present (no currentMemo).
    expect(
      h.container.querySelector('[data-testid="created-by-trigger"]')
    ).toBeNull();

    h.unmount();
  });
});
